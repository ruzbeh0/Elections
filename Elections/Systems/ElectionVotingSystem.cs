using Elections.Bridge;
using Elections.Components;
using Game;
using Game.Buildings;
using Game.Citizens;
using Game.Common;
using Game.Prefabs;
using Game.Simulation;
using Game.Tools;
using Game.UI;
using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace Elections.Systems
{
    public partial class ElectionVotingSystem : GameSystemBase
    {
        private const int kUpdateInterval = 4096;
        private const int kSocialVoteChirpChancePercent = 2;
        private const int kMaxSocialVoteChirpsPerElection = 6;
        private const float kMinVotingVisitMinutes = 5f;
        private const float kMaxVotingVisitMinutes = 40f;

        private EntityQuery m_StateQuery;
        private EntityQuery m_AvailableCitizenQuery;
        private EntityQuery m_ActiveVoteQuery;
        private EntityQuery m_SchoolQuery;
        private EntityQuery m_PoliceQuery;
        private EntityQuery m_FireQuery;
        private EntityQuery m_WelfareQuery;
        private EntityQuery m_AdminQuery;
        private SimulationSystem m_SimulationSystem;
        private NameSystem m_NameSystem;
        private PrefabSystem m_PrefabSystem;
        private bool m_LoggedMissingRealisticTrips;
        private bool m_LoggedNoPollingPlaces;
        private int m_SocialVoteChirpDayKey;
        private int m_SocialVoteChirpCount;

        public override int GetUpdateInterval(SystemUpdatePhase phase)
        {
            return kUpdateInterval;
        }

        protected override void OnCreate()
        {
            base.OnCreate();

            m_SimulationSystem = World.GetOrCreateSystemManaged<SimulationSystem>();
            m_NameSystem = World.GetExistingSystemManaged<NameSystem>();
            m_PrefabSystem = World.GetOrCreateSystemManaged<PrefabSystem>();
            m_StateQuery = GetEntityQuery(ComponentType.ReadWrite<ElectionState>());
            m_AvailableCitizenQuery = GetEntityQuery(new EntityQueryDesc
            {
                All = new[]
                {
                    ComponentType.ReadOnly<Citizen>(),
                    ComponentType.ReadOnly<HouseholdMember>(),
                    ComponentType.ReadOnly<CurrentBuilding>(),
                    ComponentType.ReadWrite<TripNeeded>()
                },
                None = new[]
                {
                    ComponentType.Exclude<ElectionVoteTrip>(),
                    ComponentType.Exclude<TravelPurpose>(),
                    ComponentType.Exclude<Deleted>(),
                    ComponentType.Exclude<Temp>(),
                    ComponentType.Exclude<HealthProblem>()
                }
            });
            m_ActiveVoteQuery = GetEntityQuery(new EntityQueryDesc
            {
                All = new[]
                {
                    ComponentType.ReadWrite<ElectionVoteTrip>(),
                    ComponentType.ReadOnly<Citizen>(),
                    ComponentType.ReadOnly<CurrentBuilding>()
                },
                None = new[]
                {
                    ComponentType.Exclude<Deleted>(),
                    ComponentType.Exclude<Temp>(),
                    ComponentType.Exclude<HealthProblem>()
                }
            });

            m_SchoolQuery = CreatePollingPlaceQuery(ComponentType.ReadOnly<Game.Buildings.School>());
            m_PoliceQuery = CreatePollingPlaceQuery(ComponentType.ReadOnly<Game.Buildings.PoliceStation>());
            m_FireQuery = CreatePollingPlaceQuery(ComponentType.ReadOnly<Game.Buildings.FireStation>());
            m_WelfareQuery = CreatePollingPlaceQuery(ComponentType.ReadOnly<Game.Buildings.WelfareOffice>());
            m_AdminQuery = CreatePollingPlaceQuery(ComponentType.ReadOnly<AdminBuilding>());
        }

        protected override void OnUpdate()
        {
            if (Mod.m_Setting == null || !Mod.m_Setting.EnableElections || m_StateQuery.IsEmptyIgnoreFilter)
                return;

            Entity stateEntity = m_StateQuery.GetSingletonEntity();
            ElectionState state = EntityManager.GetComponentData<ElectionState>(stateEntity);
            if (state.stage != ElectionCampaignStage.Voting || !state.HasCandidates)
                return;

            if (!RealisticTripsBridge.TryGetCurrentDateTime(out DateTime now))
                return;

            int dayKey = ElectionUtility.CurrentCalendarDayKey(World, now);
            if (dayKey != state.electionDayKey)
                return;

            EnsureElectionTiming(ref state);
            ProcessArrivals(ref state, now);

            int minuteOfDay = ElectionUtility.MinuteOfDay(now);
            if (minuteOfDay >= state.votingStartMinute && minuteOfDay < state.votingEndMinute)
                RequestVotingTrips(ref state, now);

            EntityManager.SetComponentData(stateEntity, state);
        }

        private static void EnsureElectionTiming(ref ElectionState state)
        {
            state.votingStartMinute = ElectionUtility.NormalizeVotingStartMinute(state.votingStartMinute);
            state.votingEndMinute = ElectionUtility.NormalizeVotingEndMinute(state.votingEndMinute);
            state.resultsAnnouncementMinute = state.resultsAnnouncementMinute > 0
                ? state.resultsAnnouncementMinute
                : ElectionUtility.ResultsAnnouncementMinute;
        }

        private EntityQuery CreatePollingPlaceQuery(ComponentType componentType)
        {
            return GetEntityQuery(new EntityQueryDesc
            {
                All = new[]
                {
                    ComponentType.ReadOnly<Building>(),
                    componentType
                },
                None = new[]
                {
                    ComponentType.Exclude<Deleted>(),
                    ComponentType.Exclude<Temp>()
                }
            });
        }

        private void RequestVotingTrips(ref ElectionState state, DateTime now)
        {
            if (!RealisticTripsBridge.IsAvailable)
            {
                if (!m_LoggedMissingRealisticTrips)
                {
                    m_LoggedMissingRealisticTrips = true;
                    ElectionDebug.Log($"Voting trip request skipped at {ElectionUtility.FormatCurrentDate(World, now)} {now:HH:mm}: Realistic Trips bridge is unavailable.");
                    CustomChirpsBridge.PostChirp("Election voting trips require Realistic Trips. No residents can be sent to polling places through the current bridge.", DepartmentAccountBridge.CensusBureau, Entity.Null, "Election Board");
                }
                return;
            }

            List<Entity> pollingPlaces = GetPollingPlaces();
            if (pollingPlaces.Count == 0)
            {
                if (!m_LoggedNoPollingPlaces)
                {
                    m_LoggedNoPollingPlaces = true;
                    ElectionDebug.Log($"Voting trip request skipped at {ElectionUtility.FormatCurrentDate(World, now)} {now:HH:mm}: no polling place buildings were found.");
                }

                return;
            }

            m_LoggedNoPollingPlaces = false;

            int teenHourlyChance = math.clamp(Mod.m_Setting?.TeenHourlyVotingAttemptPercent ?? 50, 1, 100);
            int adultHourlyChance = math.clamp(Mod.m_Setting?.HourlyVotingAttemptPercent ?? 75, 1, 100);
            int elderlyHourlyChance = math.clamp(Mod.m_Setting?.ElderlyHourlyVotingAttemptPercent ?? 30, 1, 100);
            float updateHours = kUpdateInterval / (float)TimeSystem.kTicksPerDay * 24f;
            bool sundayMode = Mod.m_Setting?.ElectionDayActsLikeSunday ?? true;
            int eligibleCount = 0;
            int teenEligibleCount = 0;
            int adultEligibleCount = 0;
            int elderlyEligibleCount = 0;
            int randomSelectedCount = 0;
            int blockedByWorkCount = 0;
            int rejectedByBridgeCount = 0;
            int requestedCount = 0;

            using (NativeArray<Entity> citizens = m_AvailableCitizenQuery.ToEntityArray(Allocator.Temp))
            using (NativeArray<Citizen> citizenData = m_AvailableCitizenQuery.ToComponentDataArray<Citizen>(Allocator.Temp))
            {
                for (int i = 0; i < citizens.Length; i++)
                {
                    Entity citizen = citizens[i];
                    Citizen data = citizenData[i];
                    if (!ElectionUtility.IsEligibleVoterResident(EntityManager, citizen, data))
                        continue;

                    eligibleCount++;
                    CitizenAge age = data.GetAge();
                    if (age == CitizenAge.Teen)
                        teenEligibleCount++;
                    else if (age == CitizenAge.Elderly)
                        elderlyEligibleCount++;
                    else
                        adultEligibleCount++;

                    int hourlyChance = GetHourlyVotingAttemptPercent(age, teenHourlyChance, adultHourlyChance, elderlyHourlyChance);
                    float chancePerUpdate = math.saturate(hourlyChance / 100f * updateHours);
                    Unity.Mathematics.Random random = CreateRandom(citizen, state.electionDayKey, (int)m_SimulationSystem.frameIndex);
                    if (random.NextFloat() > chancePerUpdate)
                        continue;

                    randomSelectedCount++;
                    Entity pollingPlace = pollingPlaces[random.NextInt(pollingPlaces.Count)];
                    if (!RealisticTripsBridge.CanRequestTrip(citizen, pollingPlace))
                    {
                        rejectedByBridgeCount++;
                        continue;
                    }

                    if (!sundayMode && !RealisticTripsBridge.IsCitizenOutsideWorkHours(citizen))
                    {
                        blockedByWorkCount++;
                        continue;
                    }

                    float visitDurationMinutes = random.NextFloat(kMinVotingVisitMinutes, kMaxVotingVisitMinutes);
                    if (!RealisticTripsBridge.RequestVotingTrip(citizen, pollingPlace, visitDurationMinutes, 100))
                    {
                        rejectedByBridgeCount++;
                        continue;
                    }

                    EntityManager.AddComponentData(citizen, new ElectionVoteTrip
                    {
                        version = 1,
                        electionDayKey = state.electionDayKey,
                        pollingPlace = pollingPlace,
                        voted = false,
                        chosenCandidate = -1
                    });
                    state.voteRequests++;
                    requestedCount++;
                }

                ElectionDebug.Log($"Voting trip request update: date={ElectionUtility.FormatCurrentDate(World, now)} {now:HH:mm}, pollingPlaces={pollingPlaces.Count}, availableResidents={citizens.Length}, eligibleResidents={eligibleCount}, eligibleByAge=teen:{teenEligibleCount}/adult:{adultEligibleCount}/elderly:{elderlyEligibleCount}, hourlyChanceByAge=teen:{teenHourlyChance}/adult:{adultHourlyChance}/elderly:{elderlyHourlyChance}, selectedByChance={randomSelectedCount}, blockedByWork={blockedByWorkCount}, rejectedByBridge={rejectedByBridgeCount}, requestedThisUpdate={requestedCount}, totalRequests={state.voteRequests}, sundayMode={sundayMode}, visitDurationMinutes={kMinVotingVisitMinutes:0}-{kMaxVotingVisitMinutes:0}.");
            }
        }

        private static int GetHourlyVotingAttemptPercent(CitizenAge age, int teenHourlyChance, int adultHourlyChance, int elderlyHourlyChance)
        {
            if (age == CitizenAge.Teen)
                return teenHourlyChance;

            if (age == CitizenAge.Elderly)
                return elderlyHourlyChance;

            return adultHourlyChance;
        }

        private void ProcessArrivals(ref ElectionState state, DateTime now)
        {
            int activeTrips = 0;
            int arrivals = 0;
            using (NativeArray<Entity> entities = m_ActiveVoteQuery.ToEntityArray(Allocator.Temp))
            using (NativeArray<ElectionVoteTrip> voteTrips = m_ActiveVoteQuery.ToComponentDataArray<ElectionVoteTrip>(Allocator.Temp))
            using (NativeArray<Citizen> citizens = m_ActiveVoteQuery.ToComponentDataArray<Citizen>(Allocator.Temp))
            using (NativeArray<CurrentBuilding> currentBuildings = m_ActiveVoteQuery.ToComponentDataArray<CurrentBuilding>(Allocator.Temp))
            {
                activeTrips = entities.Length;
                for (int i = 0; i < entities.Length; i++)
                {
                    ElectionVoteTrip voteTrip = voteTrips[i];
                    if (voteTrip.voted || voteTrip.electionDayKey != state.electionDayKey)
                        continue;

                    if (currentBuildings[i].m_CurrentBuilding != voteTrip.pollingPlace)
                        continue;

                    float probabilityA = ElectionUtility.GetVoteProbabilityForA(EntityManager, entities[i], citizens[i], state);
                    Unity.Mathematics.Random random = CreateRandom(entities[i], state.electionDayKey, 65537 + state.voteArrivals);
                    Entity voter = entities[i];
                    Entity pollingPlace = voteTrip.pollingPlace;
                    int chosenCandidate;
                    if (random.NextFloat() < probabilityA)
                    {
                        state.votesA++;
                        chosenCandidate = 0;
                    }
                    else
                    {
                        state.votesB++;
                        chosenCandidate = 1;
                    }

                    voteTrip.chosenCandidate = chosenCandidate;
                    voteTrip.voted = true;
                    state.voteArrivals++;
                    arrivals++;
                    EntityManager.SetComponentData(voter, voteTrip);
                    PostCandidateVotedChirp(ref state, voter, pollingPlace);
                    TryQueueSocialVoteChirp(state, voter, pollingPlace, chosenCandidate, random);
                }
            }

            if (activeTrips > 0 || arrivals > 0)
                ElectionDebug.Log($"Voting arrival update: date={ElectionUtility.FormatCurrentDate(World, now)} {now:HH:mm}, activeTrips={activeTrips}, arrivalsThisUpdate={arrivals}, totalArrivals={state.voteArrivals}, votesA={state.votesA}, votesB={state.votesB}.");
        }

        private void PostCandidateVotedChirp(ref ElectionState state, Entity voter, Entity pollingPlace)
        {
            bool candidateA = voter == state.candidateA;
            bool candidateB = voter == state.candidateB;
            if ((!candidateA && !candidateB) ||
                (candidateA && state.candidateAVotedChirpSent) ||
                (candidateB && state.candidateBVotedChirpSent))
            {
                return;
            }

            int portraitIndex = candidateA ? state.candidateAPortraitIndex : state.candidateBPortraitIndex;
            string fallbackName = candidateA ? "Candidate A" : "Candidate B";
            string name = GetCitizenName(voter, fallbackName);
            string locationName = GetBuildingName(pollingPlace, "my voting site");
            string portraitImageSource = CandidatePortraitCatalog.GetPortraitImageSource(EntityManager, voter, portraitIndex);
            string endTime = ElectionUtility.FormatHourText(state.votingEndMinute);
            string text = $"I am {{LINK_1}}. I just voted at {locationName}. Polls are open until {endTime}; please make sure your voice is counted.";

            if (!CustomChirpsBridge.PostLargeChirpFromEntityWithPortraitImage(text, voter, voter, portraitImageSource, name))
                CustomChirpsBridge.PostChirpFromEntity(text, voter, voter, name);

            if (candidateA)
                state.candidateAVotedChirpSent = true;
            else
                state.candidateBVotedChirpSent = true;
        }

        private void TryQueueSocialVoteChirp(ElectionState state, Entity voter, Entity pollingPlace, int chosenCandidate, Unity.Mathematics.Random random)
        {
            if (voter == state.candidateA || voter == state.candidateB)
                return;

            if (m_SocialVoteChirpDayKey != state.electionDayKey)
            {
                m_SocialVoteChirpDayKey = state.electionDayKey;
                m_SocialVoteChirpCount = 0;
            }

            if (m_SocialVoteChirpCount >= kMaxSocialVoteChirpsPerElection ||
                random.NextInt(100) >= kSocialVoteChirpChancePercent)
            {
                return;
            }

            string candidateName = chosenCandidate == 0
                ? GetCitizenName(state.candidateA, "Candidate A")
                : GetCitizenName(state.candidateB, "Candidate B");

            if (SocialTripsBridge.TryQueueElectionVoteChirp(voter, pollingPlace, candidateName, chosenCandidate, state.electionDayKey))
                m_SocialVoteChirpCount++;
        }

        private string GetCitizenName(Entity citizen, string fallback)
        {
            return ElectionNameUtility.GetCitizenFullName(m_NameSystem, EntityManager, citizen, fallback);
        }

        private string GetBuildingName(Entity building, string fallback)
        {
            if (building == Entity.Null || !EntityManager.Exists(building))
                return fallback;

            try
            {
                string name = m_NameSystem?.GetRenderedLabelName(building);
                name = SanitizeName(name, null);
                if (!string.IsNullOrWhiteSpace(name))
                    return name;
            }
            catch
            {
            }

            try
            {
                if (m_PrefabSystem != null && EntityManager.HasComponent<PrefabRef>(building))
                {
                    PrefabRef prefabRef = EntityManager.GetComponentData<PrefabRef>(building);
                    string name = m_PrefabSystem.GetPrefabName(prefabRef.m_Prefab);
                    name = SanitizeName(name, null);
                    if (!string.IsNullOrWhiteSpace(name))
                        return name;
                }
            }
            catch
            {
            }

            return fallback;
        }

        private static string SanitizeName(string value, string fallback)
        {
            if (string.IsNullOrWhiteSpace(value))
                return fallback;

            string sanitized = value.Trim();
            return string.IsNullOrWhiteSpace(sanitized) || sanitized == "Unknown" ? fallback : sanitized;
        }

        private List<Entity> GetPollingPlaces()
        {
            List<Entity> pollingPlaces = new List<Entity>(64);
            AddPollingPlaces(m_SchoolQuery, pollingPlaces);
            AddPollingPlaces(m_PoliceQuery, pollingPlaces);
            AddPollingPlaces(m_FireQuery, pollingPlaces);
            AddPollingPlaces(m_WelfareQuery, pollingPlaces);
            AddPollingPlaces(m_AdminQuery, pollingPlaces);
            return pollingPlaces;
        }

        private static void AddPollingPlaces(EntityQuery query, List<Entity> pollingPlaces)
        {
            using (NativeArray<Entity> entities = query.ToEntityArray(Allocator.Temp))
            {
                for (int i = 0; i < entities.Length; i++)
                    pollingPlaces.Add(entities[i]);
            }
        }

        private static Unity.Mathematics.Random CreateRandom(Entity entity, int dayKey, int salt)
        {
            uint seed = (uint)math.max(1, math.abs(entity.Index * 73856093 + entity.Version * 19349663 + dayKey * 83492791 + salt));
            return Unity.Mathematics.Random.CreateFromIndex(seed);
        }
    }
}
