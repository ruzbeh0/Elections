using Elections.Bridge;
using Elections.Components;
using Elections.Models;
using Game;
using Game.Buildings;
using Game.Citizens;
using Game.Common;
using Game.Companies;
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
        private const int kVotingTripRequestScanBatchSize = 4096;

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
        private int m_VotingTripRequestCursor;
        private int m_VotingTripRequestElectionDayKey;
        private int m_SocialVoteChirpDayKey;
        private int m_SocialVoteChirpCount;
        private readonly List<PollingPlaceInfo> m_PollingPlaces = new List<PollingPlaceInfo>(64);

        private struct PollingPlaceInfo
        {
            public Entity entity;
            public float3 position;
        }

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
                    ComponentType.ReadOnly<Game.Objects.Transform>(),
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
                    CustomChirpsBridge.PostChirp(
                        L("Lifecycle.VotingTrips.MissingRealisticTrips", "Election voting trips require Realistic Trips. No residents can be sent to polling places through the current bridge."),
                        DepartmentAccountBridge.CensusBureau,
                        Entity.Null,
                        L("Lifecycle.Department.ElectionBoard", "Election Board"));
                }
                return;
            }

            List<PollingPlaceInfo> pollingPlaces = GetPollingPlaces();
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
            float weightedVotingHours = GetWeightedVotingHours(state.votingStartMinute, state.votingEndMinute);
            float updateHours = kUpdateInterval / (float)RealisticTripsBridge.GetTicksPerDay() * 24f;
            int minuteOfDay = ElectionUtility.MinuteOfDay(now);
            float votingHourChanceWeight = GetVotingHourChanceWeight(minuteOfDay, state.votingStartMinute, state.votingEndMinute);
            bool sundayMode = state.electionDayHolidayScheduled;
            int eligibleCount = 0;
            int teenEligibleCount = 0;
            int adultEligibleCount = 0;
            int elderlyEligibleCount = 0;
            int randomSelectedCount = 0;
            int alreadyTrackedCount = 0;
            int blockedByWorkCount = 0;
            int adjustedWorkWindowCount = 0;
            int noAvailableWorkWindowCount = 0;
            int rejectedByBridgeCount = 0;
            int requestedCount = 0;
            float weightedAvailableHoursTotal = 0f;

            using (NativeArray<Entity> citizens = m_AvailableCitizenQuery.ToEntityArray(Allocator.Temp))
            {
                if (citizens.Length == 0)
                {
                    m_VotingTripRequestCursor = 0;
                    return;
                }

                if (m_VotingTripRequestElectionDayKey != state.electionDayKey)
                {
                    m_VotingTripRequestElectionDayKey = state.electionDayKey;
                    m_VotingTripRequestCursor = 0;
                }

                int scanCount = math.min(citizens.Length, kVotingTripRequestScanBatchSize);
                int startIndex = math.clamp(m_VotingTripRequestCursor, 0, citizens.Length - 1);
                float scanIntervalMultiplier = citizens.Length > scanCount
                    ? citizens.Length / (float)scanCount
                    : 1f;

                for (int offset = 0; offset < scanCount; offset++)
                {
                    int i = (startIndex + offset) % citizens.Length;
                    Entity citizen = citizens[i];
                    if (EntityManager.HasComponent<ElectionVoteTrip>(citizen))
                    {
                        alreadyTrackedCount++;
                        continue;
                    }

                    Citizen data = EntityManager.GetComponentData<Citizen>(citizen);
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
                        GetEducationDailyTurnoutBonusPercent(state, data.GetEducationLevel()) +
                        ElectionUtility.GetTargetedTurnoutBonusPercent(EntityManager, citizen, data, state),
                        1,
                        100);
                    dailyTurnout = ElectionCandidateTags.ApplyTurnoutModifier(dailyTurnout, state);
                    float turnoutMultiplier = ElectionUtility.GetVotingTurnoutMultiplier(data);

                    int workStartMinute = 0;
                    int workEndMinute = 0;
                    bool hasWorkWindow = !sundayMode && TryGetWorkWindow(citizen, out workStartMinute, out workEndMinute);
                    if (hasWorkWindow && IsMinuteInWorkWindow(minuteOfDay, workStartMinute, workEndMinute))
                    {
                        blockedByWorkCount++;
                        continue;
                    }

                    float weightedAvailableHours = sundayMode
                        ? weightedVotingHours
                        : GetWeightedAvailableVotingHours(
                            hasWorkWindow,
                            workStartMinute,
                            workEndMinute,
                            state.votingStartMinute,
                            state.votingEndMinute,
                            weightedVotingHours);
                    if (weightedAvailableHours <= 0f)
                    {
                        noAvailableWorkWindowCount++;
                        continue;
                    }

                    weightedAvailableHoursTotal += weightedAvailableHours;
                    if (!sundayMode && weightedAvailableHours < weightedVotingHours - 0.001f)
                        adjustedWorkWindowCount++;

                    float dailyProbability = math.saturate(dailyTurnout / 100f * turnoutMultiplier);
                    float weightedUpdateHours = updateHours * scanIntervalMultiplier * votingHourChanceWeight;
                    float chancePerUpdate = GetElectionChancePerUpdate(dailyProbability, weightedUpdateHours, weightedAvailableHours);
                    Unity.Mathematics.Random random = CreateRandom(citizen, state.electionDayKey, (int)m_SimulationSystem.frameIndex);
                    if (random.NextFloat() > chancePerUpdate)
                        continue;

                    randomSelectedCount++;
                    CurrentBuilding currentBuilding = EntityManager.GetComponentData<CurrentBuilding>(citizen);
                    if (!TryPickPollingPlace(citizen, currentBuilding, pollingPlaces, random, out Entity pollingPlace))
                    {
                        rejectedByBridgeCount++;
                        continue;
                    }

                    if (!RealisticTripsBridge.CanRequestTrip(citizen, pollingPlace))
                    {
                        rejectedByBridgeCount++;
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

                m_VotingTripRequestCursor = (startIndex + scanCount) % citizens.Length;

                int chanceEligibleCount = math.max(0, eligibleCount - blockedByWorkCount - noAvailableWorkWindowCount);
                if (ElectionDebug.Enabled)
                {
                    float averageWeightedAvailableHours = chanceEligibleCount > 0 ? weightedAvailableHoursTotal / chanceEligibleCount : 0f;
                    ElectionDebug.Log($"Voting trip request update: date={ElectionUtility.FormatCurrentDate(World, now)} {now:HH:mm}, pollingPlaces={pollingPlaces.Count}, availableResidents={citizens.Length}, scannedResidents={scanCount}, nextScanIndex={m_VotingTripRequestCursor}, scanIntervalMultiplier={scanIntervalMultiplier:0.##}, eligibleResidents={eligibleCount}, eligibleByAge=teen:{teenEligibleCount}/adult:{adultEligibleCount}/elderly:{elderlyEligibleCount}, dailyTurnoutByAge=teen:{teenDailyTurnout}/adult:{adultDailyTurnout}/elderly:{elderlyDailyTurnout}, dailyTurnoutBonusByEducation=uneducated:{state.uneducatedTurnoutBonusPercent}/poorlyEducated:{state.educatedTurnoutBonusPercent}/educatedPlus:{state.civicForumTurnoutBonusPercent}, votingHours={votingHours:0.##}, weightedVotingHours={weightedVotingHours:0.##}, averageWeightedAvailableHours={averageWeightedAvailableHours:0.##}, votingHourChanceWeight={votingHourChanceWeight:0.###}, selectedByChance={randomSelectedCount}, alreadyTracked={alreadyTrackedCount}, blockedByWork={blockedByWorkCount}, workWindowAdjusted={adjustedWorkWindowCount}, noAvailableWorkWindow={noAvailableWorkWindowCount}, rejectedByBridge={rejectedByBridgeCount}, requestedThisUpdate={requestedCount}, totalRequests={state.voteRequests}, holidayMode={sundayMode}, visitDurationMinutes={kMinVotingVisitMinutes:0}-{kMaxVotingVisitMinutes:0}.");
                }
            }
        }

        private static float GetVotingHourChanceWeight(int minuteOfDay, int votingStartMinute, int votingEndMinute)
        {
            if (votingEndMinute - votingStartMinute < 120)
                return 1f;

            int lastHourStartMinute = votingEndMinute - 60;
            int previousHourStartMinute = votingEndMinute - 120;
            if (minuteOfDay >= lastHourStartMinute && minuteOfDay < votingEndMinute)
                return 1f / 3f;

            if (minuteOfDay >= previousHourStartMinute && minuteOfDay < lastHourStartMinute)
                return 5f / 3f;

            return 1f;
        }

        private static float GetElectionChancePerUpdate(float electionProbability, float weightedUpdateHours, float weightedAvailableHours)
        {
            electionProbability = math.saturate(electionProbability);
            if (electionProbability <= 0f || weightedUpdateHours <= 0f || weightedAvailableHours <= 0f)
                return 0f;

            if (electionProbability >= 1f)
                return 1f;

            float exponent = math.max(0f, weightedUpdateHours / weightedAvailableHours);
            return math.saturate(1f - math.pow(1f - electionProbability, exponent));
        }

        private static bool IsMinuteInWorkWindow(int minuteOfDay, int workStartMinute, int workEndMinute)
        {
            if (workStartMinute == workEndMinute)
                return false;

            if (workStartMinute < workEndMinute)
                return minuteOfDay >= workStartMinute && minuteOfDay < workEndMinute;

            return minuteOfDay >= workStartMinute || minuteOfDay < workEndMinute;
        }

        private static float GetWeightedAvailableVotingHours(bool hasWorkWindow, int workStartMinute, int workEndMinute, int votingStartMinute, int votingEndMinute, float weightedVotingHours)
        {
            if (!hasWorkWindow)
                return weightedVotingHours;

            float workOverlapHours = GetWeightedWorkOverlapHours(votingStartMinute, votingEndMinute, workStartMinute, workEndMinute);
            return math.max(0f, weightedVotingHours - workOverlapHours);
        }

        private bool TryGetWorkWindow(Entity citizen, out int workStartMinute, out int workEndMinute)
        {
            workStartMinute = 0;
            workEndMinute = 0;

            if (RealisticTripsBridge.TryGetCitizenWorkWindow(
                    EntityManager,
                    citizen,
                    out bool dayOff,
                    out float goToWork,
                    out float endWork))
            {
                if (dayOff || goToWork < 0f || endWork < 0f)
                    return false;

                workStartMinute = NormalizedDayToMinute(goToWork);
                workEndMinute = NormalizedDayToMinute(endWork);
                return workStartMinute != workEndMinute;
            }

            if (!EntityManager.HasComponent<Worker>(citizen))
                return false;

            Worker worker = EntityManager.GetComponentData<Worker>(citizen);
            switch (worker.m_Shift)
            {
                case Workshift.Evening:
                    workStartMinute = 14 * 60;
                    workEndMinute = 22 * 60;
                    return true;
                case Workshift.Night:
                    workStartMinute = 22 * 60;
                    workEndMinute = 6 * 60;
                    return true;
                default:
                    workStartMinute = 8 * 60;
                    workEndMinute = 17 * 60;
                    return true;
            }
        }

        private static int NormalizedDayToMinute(float normalizedTime)
        {
            if (normalizedTime <= 0f)
                return 0;

            if (normalizedTime >= 1f)
                return 24 * 60;

            return math.clamp((int)math.round(normalizedTime * 24f * 60f), 0, 24 * 60);
        }

        private static float GetWeightedVotingHours(int votingStartMinute, int votingEndMinute)
        {
            return math.max(1f / 60f, GetWeightedIntervalHours(votingStartMinute, votingEndMinute, votingStartMinute, votingEndMinute));
        }

        private static float GetWeightedWorkOverlapHours(int votingStartMinute, int votingEndMinute, int workStartMinute, int workEndMinute)
        {
            if (workStartMinute == workEndMinute)
                return 0f;

            if (workStartMinute < workEndMinute)
                return GetWeightedIntervalHours(workStartMinute, workEndMinute, votingStartMinute, votingEndMinute);

            return GetWeightedIntervalHours(0, workEndMinute, votingStartMinute, votingEndMinute) +
                   GetWeightedIntervalHours(workStartMinute, 24 * 60, votingStartMinute, votingEndMinute);
        }

        private static float GetWeightedIntervalHours(int intervalStartMinute, int intervalEndMinute, int votingStartMinute, int votingEndMinute)
        {
            int startMinute = math.max(intervalStartMinute, votingStartMinute);
            int endMinute = math.min(intervalEndMinute, votingEndMinute);
            if (endMinute <= startMinute)
                return 0f;

            if (votingEndMinute - votingStartMinute < 120)
                return (endMinute - startMinute) / 60f;

            int lastHourStartMinute = votingEndMinute - 60;
            int previousHourStartMinute = votingEndMinute - 120;
            return GetWeightedSegmentHours(startMinute, endMinute, votingStartMinute, previousHourStartMinute, 1f) +
                   GetWeightedSegmentHours(startMinute, endMinute, previousHourStartMinute, lastHourStartMinute, 5f / 3f) +
                   GetWeightedSegmentHours(startMinute, endMinute, lastHourStartMinute, votingEndMinute, 1f / 3f);
        }

        private static float GetWeightedSegmentHours(int intervalStartMinute, int intervalEndMinute, int segmentStartMinute, int segmentEndMinute, float weight)
        {
            int startMinute = math.max(intervalStartMinute, segmentStartMinute);
            int endMinute = math.min(intervalEndMinute, segmentEndMinute);
            return endMinute > startMinute ? (endMinute - startMinute) / 60f * weight : 0f;
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
                    return state.civicForumTurnoutBonusPercent;
            }
        }

        private bool TryPickPollingPlace(Entity citizen, CurrentBuilding currentBuilding, List<PollingPlaceInfo> pollingPlaces, Unity.Mathematics.Random random, out Entity pollingPlace)
        {
            pollingPlace = Entity.Null;
            if (pollingPlaces.Count == 0)
                return false;

            if (!TryGetVotingOrigin(citizen, currentBuilding, out float3 origin))
            {
                pollingPlace = pollingPlaces[random.NextInt(pollingPlaces.Count)].entity;
                return true;
            }

            float bestDistance = float.MaxValue;
            for (int i = 0; i < pollingPlaces.Count; i++)
            {
                PollingPlaceInfo candidate = pollingPlaces[i];

                float distance = math.distancesq(origin, candidate.position);
                if (pollingPlace == Entity.Null || distance < bestDistance)
                {
                    pollingPlace = candidate.entity;
                    bestDistance = distance;
                }
            }

            if (pollingPlace == Entity.Null)
                pollingPlace = pollingPlaces[random.NextInt(pollingPlaces.Count)].entity;

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

                    Unity.Mathematics.Random random = CreateRandom(entities[i], state.electionDayKey, 65537 + state.voteArrivals);
                    Entity voter = entities[i];
                    Entity pollingPlace = voteTrip.pollingPlace;
                    int chosenCandidate = ElectionUtility.PickVoteCandidate(EntityManager, voter, citizens[i], state, random.NextFloat());
                    if (!state.IsActiveCandidateIndex(chosenCandidate))
                        continue;

                    state.AddCandidateVote(chosenCandidate);
                    voteTrip.chosenCandidate = chosenCandidate;
                    voteTrip.voted = true;
                    state.voteArrivals++;
                    arrivals++;
                    EntityManager.SetComponentData(voter, voteTrip);
                    PostCandidateVotedChirp(ref state, voter, pollingPlace);
                    TryQueueSocialVoteChirp(state, voter, pollingPlace, chosenCandidate, random);
                }
            }

            if (ElectionDebug.Enabled && (activeTrips > 0 || arrivals > 0))
                ElectionDebug.Log($"Voting arrival update: date={ElectionUtility.FormatCurrentDate(World, now)} {now:HH:mm}, activeTrips={activeTrips}, arrivalsThisUpdate={arrivals}, alreadyRecordedVotes={alreadyRecordedVotes}, totalArrivals={state.voteArrivals}, votes={FormatVoteTotals(state)}.");
        }

        private static bool HasRecordedVote(ElectionVoteTrip voteTrip)
        {
            return voteTrip.voted ||
                   voteTrip.chosenCandidate == 0 ||
                   voteTrip.chosenCandidate == 1 ||
                   voteTrip.chosenCandidate == 2 ||
                   voteTrip.chosenCandidate == 3;
        }

        private void PostCandidateVotedChirp(ref ElectionState state, Entity voter, Entity pollingPlace)
        {
            int candidateIndex = -1;
            int candidateCount = state.ActiveCandidateCount;
            for (int i = 0; i < candidateCount; i++)
            {
                if (voter == state.GetCandidate(i))
                {
                    candidateIndex = i;
                    break;
                }
            }

            if (candidateIndex < 0 || state.GetCandidateVotedChirpSent(candidateIndex))
            {
                return;
            }

            int portraitIndex = state.GetCandidatePortraitIndex(candidateIndex);
            string name = GetCandidateChirpName(voter);
            string locationName = GetBuildingName(pollingPlace, L("Lifecycle.Name.MyVotingSite", "my voting site"));
            string portraitImageSource = CandidatePortraitCatalog.GetPortraitImageSource(EntityManager, voter, portraitIndex);
            string endTime = ElectionUtility.FormatHourText(state.votingEndMinute);
            bool hasPollingPlaceLink = IsValidChirpTarget(pollingPlace);
            string text = hasPollingPlaceLink
                ? LF("Lifecycle.Vote.IJustVoted", "I just voted at {0}. Polls are open until {1}; please make sure your voice is counted.", "{LINK_2}", endTime)
                : LF("Lifecycle.Vote.IJustVoted", "I just voted at {0}. Polls are open until {1}; please make sure your voice is counted.", locationName, endTime);
            string fallbackText = LF("Lifecycle.Vote.IJustVoted", "I just voted at {0}. Polls are open until {1}; please make sure your voice is counted.", locationName, endTime);

            bool posted = hasPollingPlaceLink &&
                CustomChirpsBridge.PostLargeChirpFromEntityWithPortraitImage(text, voter, voter, pollingPlace, portraitImageSource, name);
            if (!posted && !CustomChirpsBridge.PostLargeChirpFromEntityWithPortraitImage(fallbackText, voter, voter, portraitImageSource, name))
                CustomChirpsBridge.PostChirpFromEntity(fallbackText, voter, voter, name);

            state.SetCandidateVotedChirpSent(candidateIndex, true);
        }

        private void TryQueueSocialVoteChirp(ElectionState state, Entity voter, Entity pollingPlace, int chosenCandidate, Unity.Mathematics.Random random)
        {
            int candidateCount = state.ActiveCandidateCount;
            for (int i = 0; i < candidateCount; i++)
            {
                if (voter == state.GetCandidate(i))
                    return;
            }

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

            Entity candidate = state.GetCandidate(chosenCandidate);
            string candidateName = GetCandidateChirpName(candidate);

            if (SocialTripsBridge.TryQueueElectionVoteChirp(voter, pollingPlace, candidate, candidateName, chosenCandidate, state.electionDayKey))
                m_SocialVoteChirpCount++;
        }

        private static string FormatVoteTotals(ElectionState state)
        {
            int candidateCount = state.ActiveCandidateCount;
            string result = string.Empty;
            for (int i = 0; i < candidateCount; i++)
            {
                if (i > 0)
                    result += "/";
                result += state.GetCandidateVotes(i).ToString();
            }

            return result;
        }

        private string GetCitizenName(Entity citizen, string fallback)
        {
            return ElectionNameUtility.GetCitizenFullName(m_NameSystem, EntityManager, citizen, fallback);
        }

        private string GetCandidateChirpName(Entity candidate)
        {
            return GetCitizenName(candidate, L("Lifecycle.Name.Candidate", "the candidate"));
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

        private static string L(string key, string fallback)
        {
            return ElectionLocalization.Translate(key, fallback);
        }

        private static string LF(string key, string fallback, params object[] args)
        {
            return ElectionLocalization.Format(key, fallback, args);
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

        private List<PollingPlaceInfo> GetPollingPlaces()
        {
            m_PollingPlaces.Clear();
            AddPollingPlaces(m_SchoolQuery, m_PollingPlaces);
            AddPollingPlaces(m_WelfareQuery, m_PollingPlaces);
            AddPollingPlaces(m_AdminQuery, m_PollingPlaces);
            AddPollingPlaces(m_PostQuery, m_PollingPlaces);
            return m_PollingPlaces;
        }

        private static void AddPollingPlaces(EntityQuery query, List<PollingPlaceInfo> pollingPlaces)
        {
            using (NativeArray<Entity> entities = query.ToEntityArray(Allocator.Temp))
            using (NativeArray<Game.Objects.Transform> transforms = query.ToComponentDataArray<Game.Objects.Transform>(Allocator.Temp))
            {
                for (int i = 0; i < entities.Length; i++)
                {
                    pollingPlaces.Add(new PollingPlaceInfo
                    {
                        entity = entities[i],
                        position = transforms[i].m_Position
                    });
                }
            }
        }

        private static Unity.Mathematics.Random CreateRandom(Entity entity, int dayKey, int salt)
        {
            uint seed = (uint)math.max(1, math.abs(entity.Index * 73856093 + entity.Version * 19349663 + dayKey * 83492791 + salt));
            return Unity.Mathematics.Random.CreateFromIndex(seed);
        }
    }
}
