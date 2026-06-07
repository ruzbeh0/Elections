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
        private EntityQuery m_WelfareQuery;
        private EntityQuery m_AdminQuery;
        private EntityQuery m_PostQuery;
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
            m_WelfareQuery = CreatePollingPlaceQuery(ComponentType.ReadOnly<Game.Buildings.WelfareOffice>());
            m_AdminQuery = CreatePollingPlaceQuery(ComponentType.ReadOnly<AdminBuilding>());
            m_PostQuery = CreatePollingPlaceQuery(ComponentType.ReadOnly<Game.Buildings.PostFacility>());
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
            int minuteOfDay = ElectionUtility.MinuteOfDay(now);
            if (minuteOfDay < state.votingEndMinute)
                ProcessArrivals(ref state, now);

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
                    ComponentType.Exclude<Temp>(),
                    ComponentType.Exclude<Owner>()
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

            int teenDailyTurnout = math.clamp(
                (Mod.m_Setting?.TeenDailyVotingTurnoutPercent ?? ElectionUtility.DefaultTeenDailyVotingTurnoutPercent) + state.teenTurnoutBonusPercent,
                1,
                100);
            int adultDailyTurnout = math.clamp(
                (Mod.m_Setting?.AdultDailyVotingTurnoutPercent ?? ElectionUtility.DefaultAdultDailyVotingTurnoutPercent) + state.adultTurnoutBonusPercent,
                1,
                100);
            int elderlyDailyTurnout = math.clamp(
                (Mod.m_Setting?.ElderlyDailyVotingTurnoutPercent ?? ElectionUtility.DefaultElderlyDailyVotingTurnoutPercent) + state.elderlyTurnoutBonusPercent,
                1,
                100);
            float votingHours = math.max(1f, (state.votingEndMinute - state.votingStartMinute) / 60f);
            float updateHours = kUpdateInterval / (float)TimeSystem.kTicksPerDay * 24f;
            bool sundayMode = state.electionDayHolidayScheduled;
            int eligibleCount = 0;
            int teenEligibleCount = 0;
            int adultEligibleCount = 0;
            int elderlyEligibleCount = 0;
            int randomSelectedCount = 0;
            int alreadyTrackedCount = 0;
            int blockedByWorkCount = 0;
            int rejectedByBridgeCount = 0;
            int requestedCount = 0;

            using (NativeArray<Entity> citizens = m_AvailableCitizenQuery.ToEntityArray(Allocator.Temp))
            using (NativeArray<Citizen> citizenData = m_AvailableCitizenQuery.ToComponentDataArray<Citizen>(Allocator.Temp))
            using (NativeArray<CurrentBuilding> currentBuildings = m_AvailableCitizenQuery.ToComponentDataArray<CurrentBuilding>(Allocator.Temp))
            {
                for (int i = 0; i < citizens.Length; i++)
                {
                    Entity citizen = citizens[i];
                    Citizen data = citizenData[i];
                    if (EntityManager.HasComponent<ElectionVoteTrip>(citizen))
                    {
                        alreadyTrackedCount++;
                        continue;
                    }

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

                    int dailyTurnout = math.clamp(
                        GetDailyVotingAttemptPercent(age, teenDailyTurnout, adultDailyTurnout, elderlyDailyTurnout) +
                        GetEducationDailyTurnoutBonusPercent(state, data.GetEducationLevel()),
                        1,
                        100);
                    float hourlyChance = dailyTurnout / votingHours;
                    float turnoutMultiplier = ElectionUtility.GetVotingTurnoutMultiplier(data);
                    if (state.strictVotingIdLawPassed &&
                        data.GetEducationLevel() <= 0 &&
                        EntityManager.HasComponent<Worker>(citizen))
                    {
                        turnoutMultiplier *= 1f - ElectionUtility.StrictVotingIdUneducatedWorkerTurnoutPenaltyPercent / 100f;
                    }

                    float chancePerUpdate = math.saturate(hourlyChance / 100f * updateHours * turnoutMultiplier);
                    Unity.Mathematics.Random random = CreateRandom(citizen, state.electionDayKey, (int)m_SimulationSystem.frameIndex);
                    if (random.NextFloat() > chancePerUpdate)
                        continue;

                    randomSelectedCount++;
                    if (!TryPickPollingPlace(citizen, currentBuildings[i], pollingPlaces, random, out Entity pollingPlace))
                    {
                        rejectedByBridgeCount++;
                        continue;
                    }

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

                ElectionDebug.Log($"Voting trip request update: date={ElectionUtility.FormatCurrentDate(World, now)} {now:HH:mm}, pollingPlaces={pollingPlaces.Count}, availableResidents={citizens.Length}, eligibleResidents={eligibleCount}, eligibleByAge=teen:{teenEligibleCount}/adult:{adultEligibleCount}/elderly:{elderlyEligibleCount}, dailyTurnoutByAge=teen:{teenDailyTurnout}/adult:{adultDailyTurnout}/elderly:{elderlyDailyTurnout}, dailyTurnoutBonusByEducation=uneducated:{state.uneducatedTurnoutBonusPercent}/poorlyEducated:{state.educatedTurnoutBonusPercent}, votingHours={votingHours:0.##}, selectedByChance={randomSelectedCount}, alreadyTracked={alreadyTrackedCount}, blockedByWork={blockedByWorkCount}, rejectedByBridge={rejectedByBridgeCount}, requestedThisUpdate={requestedCount}, totalRequests={state.voteRequests}, holidayMode={sundayMode}, visitDurationMinutes={kMinVotingVisitMinutes:0}-{kMaxVotingVisitMinutes:0}.");
            }
        }

        private static int GetDailyVotingAttemptPercent(CitizenAge age, int teenDailyTurnout, int adultDailyTurnout, int elderlyDailyTurnout)
        {
            if (age == CitizenAge.Teen)
                return teenDailyTurnout;

            if (age == CitizenAge.Elderly)
                return elderlyDailyTurnout;

            return adultDailyTurnout;
        }

        private static int GetEducationDailyTurnoutBonusPercent(ElectionState state, int education)
        {
            switch (math.clamp(education, 0, 4))
            {
                case 0:
                    return state.uneducatedTurnoutBonusPercent;
                case 1:
                    return state.educatedTurnoutBonusPercent;
                default:
                    return 0;
            }
        }

        private bool TryPickPollingPlace(Entity citizen, CurrentBuilding currentBuilding, List<Entity> pollingPlaces, Unity.Mathematics.Random random, out Entity pollingPlace)
        {
            pollingPlace = Entity.Null;
            if (pollingPlaces.Count == 0)
                return false;

            if (!TryGetVotingOrigin(citizen, currentBuilding, out float3 origin))
            {
                pollingPlace = pollingPlaces[random.NextInt(pollingPlaces.Count)];
                return true;
            }

            float bestDistance = float.MaxValue;
            for (int i = 0; i < pollingPlaces.Count; i++)
            {
                Entity candidate = pollingPlaces[i];
                if (!TryGetEntityPosition(candidate, out float3 position))
                    continue;

                float distance = math.distancesq(origin, position);
                if (pollingPlace == Entity.Null || distance < bestDistance)
                {
                    pollingPlace = candidate;
                    bestDistance = distance;
                }
            }

            if (pollingPlace == Entity.Null)
                pollingPlace = pollingPlaces[random.NextInt(pollingPlaces.Count)];

            return pollingPlace != Entity.Null;
        }

        private bool TryGetVotingOrigin(Entity citizen, CurrentBuilding currentBuilding, out float3 position)
        {
            if (TryGetEntityPosition(currentBuilding.m_CurrentBuilding, out position))
                return true;

            return TryGetEntityPosition(citizen, out position);
        }

        private bool TryGetEntityPosition(Entity entity, out float3 position)
        {
            position = default;
            if (entity == Entity.Null ||
                !EntityManager.Exists(entity) ||
                !EntityManager.HasComponent<Game.Objects.Transform>(entity))
            {
                return false;
            }

            position = EntityManager.GetComponentData<Game.Objects.Transform>(entity).m_Position;
            return true;
        }

        private void ProcessArrivals(ref ElectionState state, DateTime now)
        {
            int activeTrips = 0;
            int arrivals = 0;
            int alreadyRecordedVotes = 0;
            using (NativeArray<Entity> entities = m_ActiveVoteQuery.ToEntityArray(Allocator.Temp))
            using (NativeArray<ElectionVoteTrip> voteTrips = m_ActiveVoteQuery.ToComponentDataArray<ElectionVoteTrip>(Allocator.Temp))
            using (NativeArray<Citizen> citizens = m_ActiveVoteQuery.ToComponentDataArray<Citizen>(Allocator.Temp))
            using (NativeArray<CurrentBuilding> currentBuildings = m_ActiveVoteQuery.ToComponentDataArray<CurrentBuilding>(Allocator.Temp))
            {
                activeTrips = entities.Length;
                for (int i = 0; i < entities.Length; i++)
                {
                    ElectionVoteTrip voteTrip = voteTrips[i];
                    if (voteTrip.electionDayKey != state.electionDayKey)
                        continue;

                    if (HasRecordedVote(voteTrip))
                    {
                        alreadyRecordedVotes++;
                        if (!voteTrip.voted)
                        {
                            voteTrip.voted = true;
                            EntityManager.SetComponentData(entities[i], voteTrip);
                        }

                        continue;
                    }

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
                ElectionDebug.Log($"Voting arrival update: date={ElectionUtility.FormatCurrentDate(World, now)} {now:HH:mm}, activeTrips={activeTrips}, arrivalsThisUpdate={arrivals}, alreadyRecordedVotes={alreadyRecordedVotes}, totalArrivals={state.voteArrivals}, votesA={state.votesA}, votesB={state.votesB}.");
        }

        private static bool HasRecordedVote(ElectionVoteTrip voteTrip)
        {
            return voteTrip.voted ||
                   voteTrip.chosenCandidate == 0 ||
                   voteTrip.chosenCandidate == 1;
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
            bool hasPollingPlaceLink = IsValidChirpTarget(pollingPlace);
            string text = hasPollingPlaceLink
                ? $"I just voted at {{LINK_2}}. Polls are open until {endTime}; please make sure your voice is counted."
                : $"I just voted at {locationName}. Polls are open until {endTime}; please make sure your voice is counted.";
            string fallbackText = $"I just voted at {locationName}. Polls are open until {endTime}; please make sure your voice is counted.";

            bool posted = hasPollingPlaceLink &&
                CustomChirpsBridge.PostLargeChirpFromEntityWithPortraitImage(text, voter, voter, pollingPlace, portraitImageSource, name);
            if (!posted && !CustomChirpsBridge.PostLargeChirpFromEntityWithPortraitImage(fallbackText, voter, voter, portraitImageSource, name))
                CustomChirpsBridge.PostChirpFromEntity(fallbackText, voter, voter, name);

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

            Entity candidate = chosenCandidate == 0 ? state.candidateA : state.candidateB;
            string candidateName = chosenCandidate == 0
                ? GetCitizenName(candidate, "Candidate A")
                : GetCitizenName(candidate, "Candidate B");

            if (SocialTripsBridge.TryQueueElectionVoteChirp(voter, pollingPlace, candidate, candidateName, chosenCandidate, state.electionDayKey))
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

        private bool IsValidChirpTarget(Entity entity)
        {
            return entity != Entity.Null &&
                   EntityManager.Exists(entity) &&
                   !EntityManager.HasComponent<Deleted>(entity) &&
                   !EntityManager.HasComponent<Temp>(entity);
        }

        private List<Entity> GetPollingPlaces()
        {
            List<Entity> pollingPlaces = new List<Entity>(64);
            AddPollingPlaces(m_SchoolQuery, pollingPlaces);
            AddPollingPlaces(m_WelfareQuery, pollingPlaces);
            AddPollingPlaces(m_AdminQuery, pollingPlaces);
            AddPollingPlaces(m_PostQuery, pollingPlaces);
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
