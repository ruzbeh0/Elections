using Elections.Bridge;
using Elections.Components;
using Elections.Models;
using Game;
using Game.City;
using Game.Citizens;
using Game.Common;
using Game.Economy;
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
    public partial class ElectionLifecycleSystem : GameSystemBase
    {
        public const int BribeAmount = 5000000;
        public const int MinimumPopulation = 1000;

        private const int kDonationSofteningThreshold = 10000000;

        private EntityQuery m_StateQuery;
        private EntityQuery m_CandidateQuery;
        private EntityQuery m_VoteTripQuery;
        private CitySystem m_CitySystem;
        private NameSystem m_NameSystem;
        private SimulationSystem m_SimulationSystem;
        private PendingDonationChirp m_CandidateADonationChirp;
        private PendingDonationChirp m_CandidateBDonationChirp;
        private Entity m_LastLoggedStateEntity;
        private string m_LastInvalidCandidateReport;
        private string m_LastCandidateReplacementFailureReport;
        private bool m_LoggedMissingDateTime;
        private bool m_LoggedPopulationGate;

        private struct PendingDonationChirp
        {
            public long dueUtcTicks;
            public Entity candidate;
            public string name;
            public int portraitIndex;
            public int batchAmount;
            public int totalDonation;
            public bool softenedPlatform;
            public string softenedLabel;
            public string softenedPreviousValue;
            public string softenedCurrentValue;
        }

        private struct PlatformSofteningResult
        {
            public bool softened;
            public string label;
            public string previousValue;
            public string currentValue;
        }

        public override int GetUpdateInterval(SystemUpdatePhase phase)
        {
            return 1024;
        }

        protected override void OnCreate()
        {
            base.OnCreate();

            m_CitySystem = World.GetOrCreateSystemManaged<CitySystem>();
            m_NameSystem = World.GetExistingSystemManaged<NameSystem>();
            m_SimulationSystem = World.GetOrCreateSystemManaged<SimulationSystem>();

            m_StateQuery = GetEntityQuery(ComponentType.ReadWrite<ElectionState>());
            m_CandidateQuery = GetEntityQuery(new EntityQueryDesc
            {
                All = new[]
                {
                    ComponentType.ReadOnly<Citizen>(),
                    ComponentType.ReadOnly<HouseholdMember>()
                },
                None = new[]
                {
                    ComponentType.Exclude<Deleted>(),
                    ComponentType.Exclude<Temp>(),
                    ComponentType.Exclude<HealthProblem>()
                }
            });
            m_VoteTripQuery = GetEntityQuery(ComponentType.ReadWrite<ElectionVoteTrip>());
        }

        protected override void OnUpdate()
        {
            if (Mod.m_Setting == null)
                return;

            if (!Mod.m_Setting.EnableElections)
            {
                RealisticTripsBridge.ClearElectionDaySundayOverride();
                RealisticTripsBridge.ClearElectionDaySpecialEventsSuppressed();
                return;
            }

            Entity stateEntity = EnsureStateEntity();
            ElectionState state = EntityManager.GetComponentData<ElectionState>(stateEntity);
            if (!RealisticTripsBridge.TryGetCurrentDateTime(out DateTime now))
            {
                if (!m_LoggedMissingDateTime)
                {
                    m_LoggedMissingDateTime = true;
                    DebugLog("Realistic Trips date/time is not available; lifecycle update is paused.");
                }

                return;
            }

            m_LoggedMissingDateTime = false;

            PrepareStateForCurrentDate(ref state, now);
            SyncElectionDaySundayOverride(state, now);
            SyncElectionDaySpecialEventSuppression(state, now);
            if (!state.initialized ||
                (!state.HasCandidates && state.stage == ElectionCampaignStage.None && GetPopulation() < MinimumPopulation))
            {
                EntityManager.SetComponentData(stateEntity, state);
                return;
            }

            ElectionUtility.GetCurrentCalendarDate(World, now, out int calendarYear, out int calendarMonth, out int calendarDay);
            int dayKey = ElectionUtility.DayKey(calendarYear, calendarMonth, calendarDay);

            RepairActiveCampaignSchedule(ref state, now);

            if (state.lastProcessedDayKey != dayKey)
            {
                ProcessNewDay(ref state, now, dayKey);
                state.lastProcessedDayKey = dayKey;
            }

            ProcessTimedPollRelease(ref state, now);
            ProcessScheduledElectionStart(ref state, now);

            if (state.stage == ElectionCampaignStage.Voting &&
                IsCurrentCalendarAtOrAfter(now, state.electionDayKey, GetResultsAnnouncementMinute(state)))
            {
                CompleteElection(ref state, now);
            }

            ProcessScheduledPlatformChirps(ref state, now);
            ProcessScheduledPollResponseChirps(ref state);
            ProcessScheduledElectionReminderChirps(ref state, now);
            ProcessScheduledDonationThankYouChirps();
            ProcessScheduledCorruptionInvestigationChirp(ref state);

            EntityManager.SetComponentData(stateEntity, state);
        }

        public bool TryGetStateForUI(out ElectionState state)
        {
            state = default;
            if (Mod.m_Setting == null)
                return false;

            Entity stateEntity = EnsureStateEntity();
            state = EntityManager.GetComponentData<ElectionState>(stateEntity);

            if (Mod.m_Setting.EnableElections &&
                RealisticTripsBridge.TryGetCurrentDateTime(out DateTime now))
            {
                PrepareStateForCurrentDate(ref state, now);
                EntityManager.SetComponentData(stateEntity, state);
            }

            return true;
        }

        public int GetCurrentPopulationForUI()
        {
            return GetPopulation();
        }

        public void ForceStartCampaignFromSettings()
        {
            Entity stateEntity = EnsureStateEntity();
            ElectionState state = EntityManager.GetComponentData<ElectionState>(stateEntity);
            if (!RealisticTripsBridge.TryGetCurrentDateTime(out DateTime now))
                return;

            DebugLog("ForceStartCampaign setting button pressed.");
            if (!HasMinimumPopulation("force start campaign"))
            {
                PostElectionChirp($"The Election Board will not start a campaign until the city reaches {MinimumPopulation:n0} population.", Entity.Null);
                return;
            }

            StartCampaign(ref state, now, accelerated: ElectionUtility.CurrentCalendarMonth(World, now) <= 7, reason: "debug setting force start campaign");
            EntityManager.SetComponentData(stateEntity, state);
        }

        public void ForceElectionTodayFromSettings()
        {
            Entity stateEntity = EnsureStateEntity();
            ElectionState state = EntityManager.GetComponentData<ElectionState>(stateEntity);
            if (!RealisticTripsBridge.TryGetCurrentDateTime(out DateTime now))
                return;

            DebugLog("ForceElectionToday setting button pressed.");
            if (!HasMinimumPopulation("force election today"))
            {
                PostElectionChirp($"The Election Board will not start voting until the city reaches {MinimumPopulation:n0} population.", Entity.Null);
                return;
            }

            if (!state.HasCandidates)
                StartCampaign(ref state, now, accelerated: true, reason: "debug setting force election needed candidates");

            StartElection(ref state, now);
            EntityManager.SetComponentData(stateEntity, state);
        }

        public void DonateFromSettings(int candidateIndex)
        {
            Donate(candidateIndex, 0);
        }

        public void Donate(int candidateIndex, int tierIndex)
        {
            Entity stateEntity = EnsureStateEntity();
            ElectionState state = EntityManager.GetComponentData<ElectionState>(stateEntity);

            if (!IsActiveCampaignStage(state.stage) || !state.HasCandidates)
            {
                DebugLog($"Donation rejected: no active race with candidates. candidateIndex={candidateIndex}, tierIndex={tierIndex}, state={DescribeState(state)}");
                PostElectionChirp("Campaign donations are available while an active mayoral race has selected candidates.", Entity.Null);
                return;
            }

            if (candidateIndex != 0 && candidateIndex != 1)
            {
                DebugLog($"Donation rejected: invalid candidateIndex={candidateIndex}.");
                return;
            }

            if (!ElectionDonationTiers.TryGet(tierIndex, out ElectionDonationTier tier))
            {
                DebugLog($"Donation rejected: invalid tierIndex={tierIndex}.");
                return;
            }

            Entity candidate = candidateIndex == 0 ? state.candidateA : state.candidateB;
            string name = GetEntityName(candidate, candidateIndex == 0 ? "Candidate A" : "Candidate B");

            int amount = tier.Amount;
            if (!TrySpendCityMoney(amount))
            {
                DebugLog($"Donation rejected: city could not spend {amount:n0} for {name}.");
                PostElectionChirp($"The city does not have enough money to donate {amount:n0}.", Entity.Null);
                return;
            }

            if (candidateIndex == 0)
                state.donationA += amount;
            else
                state.donationB += amount;

            int totalDonation = candidateIndex == 0 ? state.donationA : state.donationB;
            int portraitIndex = candidateIndex == 0 ? state.candidateAPortraitIndex : state.candidateBPortraitIndex;
            PlatformSofteningResult softening = TrySoftenCandidatePlatform(ref state, candidateIndex, totalDonation);
            ScheduleCandidateDonationThankYouChirp(candidateIndex, candidate, name, portraitIndex, amount, totalDonation, softening);
            DebugLog($"Donation accepted: candidateIndex={candidateIndex}, candidate={DescribeEntity(candidate, name)}, amount={amount:n0}, totalDonation={totalDonation:n0}, thank-you chirp due in 1 real-world minute.");
            EntityManager.SetComponentData(stateEntity, state);
        }

        public void BribeMayor(int candidateIndex)
        {
            Entity stateEntity = EnsureStateEntity();
            ElectionState state = EntityManager.GetComponentData<ElectionState>(stateEntity);
            if (!RealisticTripsBridge.TryGetCurrentDateTime(out DateTime now))
                return;

            PrepareStateForCurrentDate(ref state, now);

            if (!IsActiveCampaignStage(state.stage) || !state.HasCandidates)
            {
                DebugLog($"Bribe rejected: no active race with candidates. candidateIndex={candidateIndex}, state={DescribeState(state)}");
                PostElectionChirp("Mayoral platform meetings are only available while an active race has selected candidates.", Entity.Null);
                EntityManager.SetComponentData(stateEntity, state);
                return;
            }

            if (candidateIndex != 0 && candidateIndex != 1)
            {
                DebugLog($"Bribe rejected: invalid candidateIndex={candidateIndex}.");
                EntityManager.SetComponentData(stateEntity, state);
                return;
            }

            int dayKey = ElectionUtility.CurrentCalendarDayKey(World, now);
            if (state.bribeDayKey == dayKey)
            {
                DebugLog($"Bribe rejected: already attempted on dayKey={dayKey}.");
                PostElectionChirp("The mayor's schedule only allows one candidate platform meeting per in-game day.", state.mayor);
                EntityManager.SetComponentData(stateEntity, state);
                return;
            }

            Entity candidate = candidateIndex == 0 ? state.candidateA : state.candidateB;
            string candidateName = GetEntityName(candidate, candidateIndex == 0 ? "Candidate A" : "Candidate B");
            string mayorName = GetEntityName(state.mayor, "the mayor");

            if (!TrySpendCityMoney(BribeAmount))
            {
                DebugLog($"Bribe rejected: city could not spend {BribeAmount:n0} for target={candidateName}.");
                PostElectionChirp($"The city does not have enough money to fund a {BribeAmount:n0} mayoral outreach effort.", Entity.Null);
                EntityManager.SetComponentData(stateEntity, state);
                return;
            }

            state.bribeDayKey = dayKey;

            Unity.Mathematics.Random random = CreateCampaignRandom(now, 91337 + candidateIndex * 307 + state.candidateAEffectId + state.candidateBEffectId);
            if (random.NextInt(100) < 25)
            {
                ConvinceCandidateToChangePlatform(ref state, now, candidateIndex, candidateName, mayorName);
            }
            else
            {
                DebugLog($"Bribe failed: mayor={DescribeEntity(state.mayor, mayorName)}, target={DescribeEntity(candidate, candidateName)}, amount={BribeAmount:n0}.");
                PostMayorPlatformMeetingChirp(state, mayorName, candidateName, false, default, default);

                if (random.NextInt(100) < 20)
                {
                    state.corruptionInvestigationChirpUtcTicks = DateTime.UtcNow.AddMinutes(1).Ticks;
                    state.corruptionInvestigationMayor = state.mayor;
                    DebugLog($"Scheduled corruption investigation chirp for mayor={DescribeEntity(state.mayor, mayorName)} at {new DateTime(state.corruptionInvestigationChirpUtcTicks):O} UTC.");
                }
            }

            EntityManager.SetComponentData(stateEntity, state);
        }

        private Entity EnsureStateEntity()
        {
            Entity city = m_CitySystem.City;
            if (city != Entity.Null && EntityManager.Exists(city))
            {
                if (EntityManager.HasComponent<ElectionState>(city))
                {
                    RemoveExtraStateEntities(city);
                    LogStateEntity(city, "using city entity with saved ElectionState");
                    return city;
                }

                using (NativeArray<Entity> existingStates = m_StateQuery.ToEntityArray(Allocator.Temp))
                {
                    if (existingStates.Length > 0)
                    {
                        Entity existing = existingStates[0];
                        ElectionState existingState = EntityManager.GetComponentData<ElectionState>(existing);
                        EntityManager.AddComponentData(city, existingState);
                        DebugLog($"Moved ElectionState from standalone entity {FormatEntity(existing)} to city entity {FormatEntity(city)}.");
                        RemoveExtraStateEntities(city);
                        LogStateEntity(city, "moved existing ElectionState to city entity");
                        return city;
                    }
                }

                EntityManager.AddComponentData(city, CreateDefaultState());
                DebugLog($"Created default ElectionState on city entity {FormatEntity(city)}.");
                LogStateEntity(city, "created city ElectionState");
                return city;
            }

            if (!m_StateQuery.IsEmptyIgnoreFilter)
            {
                Entity existingStateEntity = m_StateQuery.GetSingletonEntity();
                LogStateEntity(existingStateEntity, "using standalone ElectionState entity");
                return existingStateEntity;
            }

            Entity newStateEntity = EntityManager.CreateEntity(typeof(ElectionState));
            EntityManager.SetComponentData(newStateEntity, CreateDefaultState());
            DebugLog($"Created standalone default ElectionState entity {FormatEntity(newStateEntity)}.");
            LogStateEntity(newStateEntity, "created standalone ElectionState");
            return newStateEntity;
        }

        private static ElectionState CreateDefaultState()
        {
            return new ElectionState
            {
                version = ElectionState.CurrentVersion,
                initialized = false,
                stage = ElectionCampaignStage.None,
                lastProcessedDayKey = 0,
                appliedEffectId = 0,
                appliedModifierType1 = -1,
                appliedModifierType2 = -1,
                corruptionInvestigationMayor = Entity.Null
            };
        }

        private void RemoveExtraStateEntities(Entity keepEntity)
        {
            using (NativeArray<Entity> existingStates = m_StateQuery.ToEntityArray(Allocator.Temp))
            {
                for (int i = 0; i < existingStates.Length; i++)
                {
                    Entity entity = existingStates[i];
                    if (entity != keepEntity && EntityManager.Exists(entity))
                    {
                        DebugLog($"Removing extra ElectionState entity {FormatEntity(entity)}; keeping {FormatEntity(keepEntity)}.");
                        EntityManager.DestroyEntity(entity);
                    }
                }
            }
        }

        private void PrepareStateForCurrentDate(ref ElectionState state, DateTime now)
        {
            if (state.version < ElectionState.CurrentVersion)
            {
                DebugLog($"State version updated in memory from {state.version} to {ElectionState.CurrentVersion}.");
                state.version = ElectionState.CurrentVersion;
            }

            EnsureActiveElectionTiming(ref state);

            if (!HasMinimumPopulation("state preparation") &&
                !state.HasCandidates &&
                state.stage == ElectionCampaignStage.None)
            {
                state.version = ElectionState.CurrentVersion;
                return;
            }

            if (!state.initialized)
            {
                DebugLog($"Initializing ElectionState at {ElectionUtility.FormatCurrentDate(World, now)}.");
                InitializeState(ref state, now);
            }

            RepairLoadedState(ref state, now);
        }

        private void RepairLoadedState(ref ElectionState state, DateTime now)
        {
            EnsureTemporaryMayor(ref state, now);
            RepairLegacyMayorEffectId(ref state, now);

            if (IsActiveCampaignStage(state.stage) && !HasValidCandidatePair(state))
            {
                ReplaceInvalidCampaignCandidates(ref state, now);
                return;
            }

            RepairLegacyCandidateEffectIds(ref state, now);

            if (IsActiveCampaignStage(state.stage))
            {
                m_LastInvalidCandidateReport = null;
                m_LastCandidateReplacementFailureReport = null;
            }

            if (!state.HasCandidates && state.stage == ElectionCampaignStage.None)
            {
                int month = ElectionUtility.CurrentCalendarMonth(World, now);
                if (month <= 7)
                {
                    StartCampaign(ref state, now, accelerated: true, reason: "repair empty inactive state before August");
                    return;
                }

                if (month >= 10)
                    StartCampaign(ref state, now, accelerated: false, reason: "repair empty inactive state during regular campaign season");
            }
        }

        private void RepairLegacyMayorEffectId(ref ElectionState state, DateTime now)
        {
            if (state.mayorEffectId <= 0 || ElectionEffects.IsGeneratedId(state.mayorEffectId))
                return;

            int oldEffectId = state.mayorEffectId;
            Entity seedEntity = state.mayor != Entity.Null ? state.mayor : state.candidateA;
            state.mayorEffectId = PickEffect(seedEntity, now, 4241 + oldEffectId);
            state.mayorNegativeSoftened = false;
            DebugLog($"Migrated legacy mayor platform effect: {oldEffectId} -> {state.mayorEffectId}.");
        }

        private void RepairLegacyCandidateEffectIds(ref ElectionState state, DateTime now)
        {
            if (!state.HasCandidates)
                return;

            bool changed = false;
            if (state.candidateAEffectId <= 0 || !ElectionEffects.IsGeneratedId(state.candidateAEffectId))
            {
                int oldEffectId = state.candidateAEffectId;
                state.candidateAEffectId = PickEffect(state.candidateA, now, 17 + oldEffectId);
                changed = true;
                DebugLog($"Migrated legacy Candidate A platform effect: {oldEffectId} -> {state.candidateAEffectId}.");
            }

            if (state.candidateBEffectId <= 0 || !ElectionEffects.IsGeneratedId(state.candidateBEffectId))
            {
                int oldEffectId = state.candidateBEffectId;
                state.candidateBEffectId = PickDifferentEffect(state.candidateB, now, 7919 + oldEffectId, state.candidateAEffectId);
                changed = true;
                DebugLog($"Migrated legacy Candidate B platform effect: {oldEffectId} -> {state.candidateBEffectId}.");
            }

            if (changed && ElectionEffects.HasSamePlatform(state.candidateAEffectId, state.candidateBEffectId))
                state.candidateBEffectId = PickDifferentEffect(state.candidateB, now, 104729, state.candidateAEffectId);
        }

        private static bool IsActiveCampaignStage(ElectionCampaignStage stage)
        {
            return stage == ElectionCampaignStage.CandidatesSelected ||
                   stage == ElectionCampaignStage.PollReleased ||
                   stage == ElectionCampaignStage.Voting;
        }

        private bool HasValidCandidatePair(ElectionState state)
        {
            return IsValidResidentEntity(state.candidateA) && IsValidResidentEntity(state.candidateB);
        }

        private bool IsValidResidentEntity(Entity entity)
        {
            if (entity == Entity.Null ||
                !EntityManager.Exists(entity) ||
                !EntityManager.HasComponent<Citizen>(entity) ||
                EntityManager.HasComponent<Deleted>(entity) ||
                EntityManager.HasComponent<Temp>(entity) ||
                EntityManager.HasComponent<HealthProblem>(entity))
            {
                return false;
            }

            return ElectionUtility.IsEligibleResident(EntityManager, entity, EntityManager.GetComponentData<Citizen>(entity));
        }

        private void ReplaceInvalidCampaignCandidates(ref ElectionState state, DateTime now)
        {
            bool candidateAValid = IsValidResidentEntity(state.candidateA);
            bool candidateBValid = IsValidResidentEntity(state.candidateB);
            LogInvalidActiveCampaignCandidates(state, now);

            bool replaced = false;
            Entity originalCandidateA = state.candidateA;
            Entity originalCandidateB = state.candidateB;

            if (!candidateAValid)
                replaced |= TryReplaceInvalidCampaignCandidate(ref state, now, 0, originalCandidateA, state.candidateB);

            if (!candidateBValid)
                replaced |= TryReplaceInvalidCampaignCandidate(ref state, now, 1, originalCandidateB, state.candidateA);

            if (!replaced)
                return;

            m_LastInvalidCandidateReport = null;
            m_LastCandidateReplacementFailureReport = null;

            if (state.stage == ElectionCampaignStage.PollReleased)
            {
                RunPoll(ref state, now);
                SchedulePollResponseChirps(ref state);
                PostRevisedPollChirp(state, now);
            }
        }

        private bool TryReplaceInvalidCampaignCandidate(ref ElectionState state, DateTime now, int candidateIndex, Entity oldCandidate, Entity otherCandidate)
        {
            string slotName = candidateIndex == 0 ? "Candidate A" : "Candidate B";
            string oldName = GetEntityName(oldCandidate, slotName);
            string invalidReason = GetResidentValidityReason(oldCandidate);

            if (!TryPickReplacementCandidate(now, oldCandidate, otherCandidate, candidateIndex, out Entity replacement))
            {
                ReportCandidateReplacementFailure(state, now, candidateIndex, oldCandidate, invalidReason);
                return false;
            }

            ApplyCandidateReplacement(ref state, now, candidateIndex, replacement);
            string replacementName = GetEntityName(replacement, slotName);
            DebugLog($"Candidate replaced: slot={slotName}, old={DescribeEntity(oldCandidate, oldName)}, replacement={DescribeEntity(replacement, replacementName)}, reason={invalidReason}, state={DescribeState(state)}.");
            PostCandidateReplacementChirp(state, candidateIndex, oldName, replacement, replacementName);
            return true;
        }

        private void ApplyCandidateReplacement(ref ElectionState state, DateTime now, int candidateIndex, Entity replacement)
        {
            DateTime utcNow = DateTime.UtcNow;

            if (candidateIndex == 0)
            {
                state.candidateA = replacement;
                state.candidateAEffectId = PickDifferentEffect(replacement, now, 17017, state.candidateBEffectId);
                CaptureCandidateProfile(replacement, out state.candidateAAge, out state.candidateAEducation, out state.candidateAWorkType, out state.candidateAWealth);
                state.candidateAPortraitIndex = CandidatePortraitCatalog.PickPortraitIndex(replacement, 17);
                state.candidateANegativeSoftened = false;
                state.candidateASoftenAttempted = false;
                state.candidateAPlatformChirpSent = false;
                state.candidateAPlatformChirpUtcTicks = utcNow.AddMinutes(2).Ticks;
                state.candidateAPlatformChirpDayKey = 0;
                state.candidateAPlatformChirpMinute = 0;
                state.candidateAPollResponseChirpSent = true;
                state.candidateAPollResponseChirpUtcTicks = 0;
                m_CandidateADonationChirp = default;
            }
            else
            {
                state.candidateB = replacement;
                state.candidateBEffectId = PickDifferentEffect(replacement, now, 79191, state.candidateAEffectId);
                CaptureCandidateProfile(replacement, out state.candidateBAge, out state.candidateBEducation, out state.candidateBWorkType, out state.candidateBWealth);
                state.candidateBPortraitIndex = CandidatePortraitCatalog.PickPortraitIndex(replacement, 7919);
                state.candidateBNegativeSoftened = false;
                state.candidateBSoftenAttempted = false;
                state.candidateBPlatformChirpSent = false;
                state.candidateBPlatformChirpUtcTicks = utcNow.AddMinutes(2).Ticks;
                state.candidateBPlatformChirpDayKey = 0;
                state.candidateBPlatformChirpMinute = 0;
                state.candidateBPollResponseChirpSent = true;
                state.candidateBPollResponseChirpUtcTicks = 0;
                m_CandidateBDonationChirp = default;
            }
        }

        private bool TryPickReplacementCandidate(DateTime now, Entity excludedCandidate, Entity otherCandidate, int candidateIndex, out Entity replacement)
        {
            replacement = Entity.Null;

            using (NativeArray<Entity> entities = m_CandidateQuery.ToEntityArray(Allocator.Temp))
            using (NativeArray<Citizen> citizens = m_CandidateQuery.ToComponentDataArray<Citizen>(Allocator.Temp))
            {
                List<Entity> eligibleReplacements = new List<Entity>(entities.Length);
                for (int i = 0; i < entities.Length; i++)
                {
                    Entity candidate = entities[i];
                    if (candidate == excludedCandidate ||
                        candidate == otherCandidate ||
                        !ElectionUtility.IsEligibleResident(EntityManager, candidate, citizens[i]))
                    {
                        continue;
                    }

                    eligibleReplacements.Add(candidate);
                }

                if (eligibleReplacements.Count == 0)
                {
                    DebugLog($"Replacement candidate selection failed: slot={candidateIndex}, queryCount={entities.Length}, excluded={FormatEntity(excludedCandidate)}, otherCandidate={FormatEntity(otherCandidate)}.");
                    return false;
                }

                uint seed = (uint)math.max(1, now.Year * 10000 + now.Month * 100 + now.Day + (int)m_SimulationSystem.frameIndex + 7001 + candidateIndex * 101);
                Unity.Mathematics.Random random = Unity.Mathematics.Random.CreateFromIndex(seed);
                replacement = eligibleReplacements[random.NextInt(eligibleReplacements.Count)];
                DebugLog($"Replacement candidate selected: slot={candidateIndex}, eligibleCount={eligibleReplacements.Count}, seed={seed}, replacement={DescribeEntity(replacement, "Replacement Candidate")}.");
                return true;
            }
        }

        private void PostCandidateReplacementChirp(ElectionState state, int candidateIndex, string oldName, Entity replacement, string replacementName)
        {
            string slotName = candidateIndex == 0 ? "Candidate A" : "Candidate B";
            string pollDate = ElectionUtility.FormatDate(state.pollYear, state.pollMonth, 1);
            string electionDate = ElectionUtility.FormatDate(state.electionYear, state.electionMonth, 1);
            int inheritedDonation = candidateIndex == 0 ? state.donationA : state.donationB;
            string donationText = inheritedDonation > 0
                ? $" Existing campaign donations totaling {inheritedDonation:n0} remain with this campaign."
                : string.Empty;
            string text = $"The Election Board has replaced {slotName}. {oldName} can no longer participate because their resident record is no longer eligible. {{LINK_1}} is now certified for the mayoral race.{donationText} Poll: {pollDate}. Election: {electionDate}.";
            PostElectionChirp(text, replacement);
            DebugLog($"Candidate replacement chirp posted: slot={slotName}, oldName={oldName}, replacement={DescribeEntity(replacement, replacementName)}, inheritedDonation={inheritedDonation:n0}, poll={pollDate}, election={electionDate}.");
        }

        private void ReportCandidateReplacementFailure(ElectionState state, DateTime now, int candidateIndex, Entity oldCandidate, string invalidReason)
        {
            string slotName = candidateIndex == 0 ? "Candidate A" : "Candidate B";
            string report = $"Replacement failed at {ElectionUtility.FormatCurrentDate(World, now)}: slot={slotName}, old={DescribeEntity(oldCandidate, slotName)}, reason={invalidReason}, state={DescribeState(state)}.";

            if (report == m_LastCandidateReplacementFailureReport)
                return;

            m_LastCandidateReplacementFailureReport = report;
            DebugLog(report);
            PostElectionChirp($"The Election Board found that {slotName} can no longer participate, but no eligible replacement candidate is currently available. The board will keep checking for an eligible resident.", Entity.Null);
        }

        private void PostRevisedPollChirp(ElectionState state, DateTime now)
        {
            string nameA = GetEntityName(state.candidateA, "Candidate A");
            string nameB = GetEntityName(state.candidateB, "Candidate B");
            ElectionPollSummary summary = ElectionPollUtility.BuildSummary(state.pollVotesA, state.pollVotesB, state.pollUndecided, nameA, nameB);

            DebugLog($"Revised poll released after candidate replacement: date={ElectionUtility.FormatCurrentDate(World, now)}, total={summary.Total}, A={nameA} {summary.PercentA}% ({state.pollVotesA}), B={nameB} {summary.PercentB}% ({state.pollVotesB}), undecided={summary.PercentUndecided}% ({state.pollUndecided}), marginOfError={summary.MarginOfError}, label={summary.Label}.");
            PostElectionChirpWithCandidates(
                $"Because the candidate field changed, the Election Board has issued an updated campaign poll: {{LINK_1}} {summary.PercentA}%, {{LINK_2}} {summary.PercentB}%, undecided {summary.PercentUndecided}% with a +/-{summary.MarginOfError}% margin of error. {summary.Label}.",
                state.candidateA,
                state.candidateB);
        }

        private void EnsureTemporaryMayor(ref ElectionState state, DateTime now)
        {
            if (IsValidResidentEntity(state.mayor))
                return;

            if (!TryPickTemporaryMayor(now, out Entity mayor))
            {
                DebugLog($"Temporary mayor repair skipped at {ElectionUtility.FormatCurrentDate(World, now)}: no eligible resident found. Previous mayor={DescribeEntity(state.mayor, "Temporary Mayor")}");
                return;
            }

            state.mayor = mayor;
            state.mayorEffectId = 0;
            state.mayorNegativeSoftened = false;
            ElectionUtility.GetCurrentCalendarDate(World, now, out int year, out _, out _);
            state.mayorEffectTermYear = year;
            state.mayorMoneyApplied = false;

            string name = GetEntityName(mayor, "Temporary Mayor");
            string portraitImageSource = CandidatePortraitCatalog.GetPortraitImageSource(
                EntityManager,
                mayor,
                GetMayorPortraitIndex(state));
            string text = "I am {LINK_1}. I will serve as temporary mayor under the Democratic Transition platform: no new city policy changes, only supervising the election process until residents choose a mayor.";
            DebugLog($"Temporary mayor assigned: mayor={DescribeEntity(mayor, name)}, termYear={state.mayorEffectTermYear}.");

            if (!CustomChirpsBridge.PostLargeChirpFromEntityWithPortraitImage(text, mayor, mayor, portraitImageSource, name))
                CustomChirpsBridge.PostChirpFromEntity(text, mayor, mayor, name);
        }

        private void InitializeState(ref ElectionState state, DateTime now)
        {
            state.version = ElectionState.CurrentVersion;
            state.initialized = true;
            int month = ElectionUtility.CurrentCalendarMonth(World, now);

            if (month <= 7)
            {
                StartCampaign(ref state, now, accelerated: true, reason: "initial install/load before August");
                return;
            }

            if (month >= 10)
            {
                StartCampaign(ref state, now, accelerated: false, reason: "initial install/load during regular campaign season");
                return;
            }

            ElectionUtility.GetCurrentCalendarDate(World, now, out int year, out _, out _);
            DebugLog($"Initialized inactive election state at {ElectionUtility.FormatCurrentDate(World, now)}; next regular campaign is {ElectionUtility.FormatDate(year, 10, 1)}.");
            PostElectionChirp($"Elections is active. The next regular mayoral campaign begins on {ElectionUtility.FormatDate(year, 10, 1)}.", Entity.Null);
        }

        private void ProcessNewDay(ref ElectionState state, DateTime now, int dayKey)
        {
            ElectionUtility.GetCurrentCalendarDate(World, now, out int year, out int month, out int day);
            DebugLog($"New election date processed: {ElectionUtility.FormatDate(year, month, day)}, dayKey={dayKey}, state={DescribeState(state)}.");

            if (month == 10 && day == 1 && state.selectionYear != year)
            {
                StartCampaign(ref state, now, accelerated: false, reason: "regular October campaign start");
            }

            if (state.HasCandidates &&
                state.stage != ElectionCampaignStage.Voting &&
                day == 1 &&
                year == state.electionYear &&
                month == state.electionMonth)
            {
                DebugLog($"Election day reached: {ElectionUtility.FormatDate(year, month, day)}. Voting starts at {ElectionUtility.FormatHourText(ElectionUtility.GetConfiguredVotingStartMinute())}.");
            }
        }

        private void ProcessScheduledElectionStart(ref ElectionState state, DateTime now)
        {
            if (!state.HasCandidates || state.stage == ElectionCampaignStage.None || state.stage == ElectionCampaignStage.Voting)
                return;

            ElectionUtility.GetCurrentCalendarDate(World, now, out int year, out int month, out int day);
            if (day != 1 || year != state.electionYear || month != state.electionMonth)
                return;

            int startMinute = ElectionUtility.GetConfiguredVotingStartMinute();
            if (ElectionUtility.MinuteOfDay(now) < startMinute)
                return;

            StartElection(ref state, now);
        }

        private void ProcessTimedPollRelease(ref ElectionState state, DateTime now)
        {
            if (state.stage != ElectionCampaignStage.CandidatesSelected || !state.HasCandidates)
                return;

            ElectionUtility.GetCurrentCalendarDate(World, now, out int year, out int month, out int day);
            int compare = ElectionUtility.CompareCalendarDate(year, month, day, state.pollYear, state.pollMonth, 1);
            if (compare < 0)
                return;

            if (compare == 0 && now.Hour < 8)
                return;

            DebugLog($"Poll release due: now={ElectionUtility.FormatCurrentDate(World, now)} {now:HH:mm}, scheduled={ElectionUtility.FormatDate(state.pollYear, state.pollMonth, 1)}, state={DescribeState(state)}.");
            ReleasePoll(ref state, now);
        }

        private void RepairActiveCampaignSchedule(ref ElectionState state, DateTime now)
        {
            if (!state.HasCandidates || state.stage == ElectionCampaignStage.None || state.stage == ElectionCampaignStage.Voting)
                return;

            if (ElectionEffects.HasSamePlatform(state.candidateAEffectId, state.candidateBEffectId))
            {
                int oldEffect = state.candidateBEffectId;
                state.candidateBEffectId = PickDifferentEffect(state.candidateB, now, 104729, state.candidateAEffectId);
                DebugLog($"Repaired duplicate candidate platform effect: candidateB {oldEffect} -> {state.candidateBEffectId}.");
            }

            ElectionUtility.GetCurrentCalendarDate(World, now, out int year, out int month, out int day);

            if (state.stage == ElectionCampaignStage.CandidatesSelected &&
                ElectionUtility.CompareCalendarDate(state.pollYear, state.pollMonth, 1, year, month, day) < 0)
            {
                int oldPollYear = state.pollYear;
                int oldPollMonth = state.pollMonth;
                int oldElectionYear = state.electionYear;
                int oldElectionMonth = state.electionMonth;
                ElectionUtility.AddCalendarMonths(year, month, 1, out state.pollYear, out state.pollMonth);
                ElectionUtility.AddCalendarMonths(year, month, 2, out state.electionYear, out state.electionMonth);
                DebugLog($"Repaired stale campaign schedule: poll {ElectionUtility.FormatDate(oldPollYear, oldPollMonth, 1)} -> {ElectionUtility.FormatDate(state.pollYear, state.pollMonth, 1)}, election {ElectionUtility.FormatDate(oldElectionYear, oldElectionMonth, 1)} -> {ElectionUtility.FormatDate(state.electionYear, state.electionMonth, 1)}.");
            }

            if (state.stage == ElectionCampaignStage.PollReleased &&
                ElectionUtility.CompareCalendarDate(state.electionYear, state.electionMonth, 1, year, month, day) < 0)
            {
                int oldElectionYear = state.electionYear;
                int oldElectionMonth = state.electionMonth;
                ElectionUtility.AddCalendarMonths(year, month, 1, out state.electionYear, out state.electionMonth);
                DebugLog($"Repaired stale post-poll election date: {ElectionUtility.FormatDate(oldElectionYear, oldElectionMonth, 1)} -> {ElectionUtility.FormatDate(state.electionYear, state.electionMonth, 1)}.");
            }

            EnsureElectionReminderChirpsScheduled(ref state);
        }

        private void StartCampaign(ref ElectionState state, DateTime now, bool accelerated, string reason = "unspecified")
        {
            DebugLog($"StartCampaign requested: reason={reason}, accelerated={accelerated}, previousState={DescribeState(state)}.");
            if (!HasMinimumPopulation($"start campaign ({reason})"))
                return;

            if (!TryPickCandidates(now, out Entity candidateA, out Entity candidateB))
            {
                DebugLog($"StartCampaign failed: no eligible candidate pair found. reason={reason}.");
                PostElectionChirp("The election board could not find two eligible adult residents for the mayoral race.", Entity.Null);
                return;
            }

            state.stage = ElectionCampaignStage.CandidatesSelected;
            state.acceleratedCycle = accelerated;
            ElectionUtility.GetCurrentCalendarDate(World, now, out int year, out int month, out _);
            state.selectionYear = year;
            state.selectionMonth = month;
            state.candidateA = candidateA;
            state.candidateB = candidateB;
            state.candidateAEffectId = PickEffect(candidateA, now, 17);
            state.candidateBEffectId = PickDifferentEffect(candidateB, now, 7919, state.candidateAEffectId);
            CaptureCandidateProfile(candidateA, out state.candidateAAge, out state.candidateAEducation, out state.candidateAWorkType, out state.candidateAWealth);
            CaptureCandidateProfile(candidateB, out state.candidateBAge, out state.candidateBEducation, out state.candidateBWorkType, out state.candidateBWealth);
            state.donationA = 0;
            state.donationB = 0;
            state.candidateANegativeSoftened = false;
            state.candidateBNegativeSoftened = false;
            state.candidateASoftenAttempted = false;
            state.candidateBSoftenAttempted = false;
            state.pollVotesA = 0;
            state.pollVotesB = 0;
            state.pollUndecided = 0;
            state.candidateAPollResponseChirpSent = true;
            state.candidateBPollResponseChirpSent = true;
            state.candidateAPollResponseChirpUtcTicks = 0;
            state.candidateBPollResponseChirpUtcTicks = 0;
            state.voteRequests = 0;
            state.voteArrivals = 0;
            state.votesA = 0;
            state.votesB = 0;
            state.candidateAPortraitIndex = CandidatePortraitCatalog.PickPortraitIndex(candidateA, 17);
            state.candidateBPortraitIndex = CandidatePortraitCatalog.PickPortraitIndex(candidateB, 7919);
            SchedulePlatformChirps(ref state, now);

            if (accelerated)
            {
                ElectionUtility.AddCalendarMonths(year, month, 1, out state.pollYear, out state.pollMonth);
                ElectionUtility.AddCalendarMonths(year, month, 2, out state.electionYear, out state.electionMonth);
                state.mayorTermYear = year;
            }
            else
            {
                state.pollYear = year;
                state.pollMonth = 11;
                state.electionYear = year + 1;
                state.electionMonth = 1;
                state.mayorTermYear = year + 1;
            }

            EnsureElectionReminderChirpsScheduled(ref state);

            string pollDate = ElectionUtility.FormatDate(state.pollYear, state.pollMonth, 1);
            string electionDate = ElectionUtility.FormatDate(state.electionYear, state.electionMonth, 1);
            m_LastInvalidCandidateReport = null;
            DebugLog($"Campaign started: reason={reason}, date={ElectionUtility.FormatCurrentDate(World, now)}, accelerated={accelerated}, A={DescribeEntity(candidateA, "Candidate A")}, B={DescribeEntity(candidateB, "Candidate B")}, effects={state.candidateAEffectId}/{state.candidateBEffectId}, portraits={state.candidateAPortraitIndex}/{state.candidateBPortraitIndex}, poll={pollDate}, election={electionDate}, state={DescribeState(state)}.");

            PostElectionChirpWithCandidates(
                $"The mayoral race has begun. The candidates are {{LINK_1}} and {{LINK_2}}. Poll: {pollDate}. Election: {electionDate}.",
                candidateA,
                candidateB);
        }

        private void SchedulePlatformChirps(ref ElectionState state, DateTime now)
        {
            DateTime utcNow = DateTime.UtcNow;
            state.candidateAPlatformChirpSent = false;
            state.candidateBPlatformChirpSent = false;
            state.candidateAPlatformChirpUtcTicks = utcNow.AddMinutes(2).Ticks;
            state.candidateBPlatformChirpUtcTicks = utcNow.AddMinutes(4).Ticks;
            state.candidateAPlatformChirpDayKey = 0;
            state.candidateBPlatformChirpDayKey = 0;
            state.candidateAPlatformChirpMinute = 0;
            state.candidateBPlatformChirpMinute = 0;
            DebugLog($"Scheduled platform chirps for {DescribeEntity(state.candidateA, "Candidate A")} at {new DateTime(state.candidateAPlatformChirpUtcTicks):O} UTC and {DescribeEntity(state.candidateB, "Candidate B")} at {new DateTime(state.candidateBPlatformChirpUtcTicks):O} UTC.");
        }

        private void ProcessScheduledPlatformChirps(ref ElectionState state, DateTime now)
        {
            if (!state.HasCandidates || state.stage == ElectionCampaignStage.None || state.stage == ElectionCampaignStage.Voting)
                return;

            long utcNowTicks = DateTime.UtcNow.Ticks;

            if (!state.candidateAPlatformChirpSent &&
                IsPlatformChirpDue(now, utcNowTicks, state.candidateAPlatformChirpUtcTicks, state.candidateAPlatformChirpDayKey, state.candidateAPlatformChirpMinute))
            {
                DebugLog($"Posting scheduled platform chirp for Candidate A: {DescribeEntity(state.candidateA, "Candidate A")}.");
                PostCandidatePlatformChirp(state, true);
                state.candidateAPlatformChirpSent = true;
            }

            if (!state.candidateBPlatformChirpSent &&
                IsPlatformChirpDue(now, utcNowTicks, state.candidateBPlatformChirpUtcTicks, state.candidateBPlatformChirpDayKey, state.candidateBPlatformChirpMinute))
            {
                DebugLog($"Posting scheduled platform chirp for Candidate B: {DescribeEntity(state.candidateB, "Candidate B")}.");
                PostCandidatePlatformChirp(state, false);
                state.candidateBPlatformChirpSent = true;
            }
        }

        private static bool IsPlatformChirpDue(DateTime now, long utcNowTicks, long dueUtcTicks, int legacyDayKey, int legacyMinute)
        {
            if (dueUtcTicks > 0)
                return utcNowTicks >= dueUtcTicks;

            return legacyDayKey == 0 || ElectionUtility.IsAtOrAfter(now, legacyDayKey, legacyMinute);
        }

        private void PostCandidatePlatformChirp(ElectionState state, bool candidateA)
        {
            Entity candidate = candidateA ? state.candidateA : state.candidateB;
            int effectId = candidateA ? state.candidateAEffectId : state.candidateBEffectId;
            bool negativeSoftened = candidateA ? state.candidateANegativeSoftened : state.candidateBNegativeSoftened;
            int portraitIndex = candidateA ? state.candidateAPortraitIndex : state.candidateBPortraitIndex;
            string fallbackName = candidateA ? "Candidate A" : "Candidate B";

            if (candidate == Entity.Null || !EntityManager.Exists(candidate))
            {
                DebugLog($"Skipped platform chirp for {fallbackName}: candidate entity is not available ({FormatEntity(candidate)}).");
                return;
            }

            ElectionEffectDefinition effect = ElectionEffects.Get(effectId, negativeSoftened);
            string name = GetEntityName(candidate, fallbackName);
            string portraitImageSource = CandidatePortraitCatalog.GetPortraitImageSource(EntityManager, candidate, portraitIndex);
            string profileIntro = GetCandidateProfileIntro(state, candidateA);
            string text = $"I am {{LINK_1}}, {profileIntro}. My platform is {effect.Name}. It {effect.Description}.";
            DebugLog($"Candidate platform chirp posted: candidate={DescribeEntity(candidate, fallbackName)}, effectId={effectId}, effect={effect.Name}, portraitIndex={portraitIndex}.");

            if (!CustomChirpsBridge.PostLargeChirpFromEntityWithPortraitImage(text, candidate, candidate, portraitImageSource, name))
                CustomChirpsBridge.PostChirpFromEntity(text, candidate, candidate, name);
        }

        private void ReleasePoll(ref ElectionState state, DateTime now)
        {
            if (!state.HasCandidates)
            {
                DebugLog("Poll release skipped: state has no candidate pair.");
                return;
            }

            RunPoll(ref state, now);
            state.stage = ElectionCampaignStage.PollReleased;

            string nameA = GetEntityName(state.candidateA, "Candidate A");
            string nameB = GetEntityName(state.candidateB, "Candidate B");
            ElectionPollSummary summary = ElectionPollUtility.BuildSummary(state.pollVotesA, state.pollVotesB, state.pollUndecided, nameA, nameB);
            DebugLog($"Poll released: date={ElectionUtility.FormatCurrentDate(World, now)}, total={summary.Total}, A={nameA} {summary.PercentA}% ({state.pollVotesA}), B={nameB} {summary.PercentB}% ({state.pollVotesB}), undecided={summary.PercentUndecided}% ({state.pollUndecided}), marginOfError={summary.MarginOfError}, label={summary.Label}.");

            PostElectionChirpWithCandidates(
                $"Campaign poll released on {ElectionUtility.FormatCurrentDate(World, now)} from {summary.Total:n0} sampled eligible residents: {{LINK_1}} {summary.PercentA}%, {{LINK_2}} {summary.PercentB}%, undecided {summary.PercentUndecided}% with a +/-{summary.MarginOfError}% margin of error. {summary.Label}. Donations remain available in the Elections options panel.",
                state.candidateA,
                state.candidateB);

            SchedulePollResponseChirps(ref state);
        }

        private void SchedulePollResponseChirps(ref ElectionState state)
        {
            DateTime utcNow = DateTime.UtcNow;
            state.candidateAPollResponseChirpSent = false;
            state.candidateBPollResponseChirpSent = false;

            bool candidateAFirst = state.pollVotesA <= state.pollVotesB;
            state.candidateAPollResponseChirpUtcTicks = utcNow.AddMinutes(candidateAFirst ? 2 : 4).Ticks;
            state.candidateBPollResponseChirpUtcTicks = utcNow.AddMinutes(candidateAFirst ? 4 : 2).Ticks;
            DebugLog($"Scheduled poll response chirps: candidateAFirst={candidateAFirst}, A due={new DateTime(state.candidateAPollResponseChirpUtcTicks):O} UTC, B due={new DateTime(state.candidateBPollResponseChirpUtcTicks):O} UTC.");
        }

        private void ProcessScheduledPollResponseChirps(ref ElectionState state)
        {
            if (!state.HasCandidates || state.stage != ElectionCampaignStage.PollReleased)
                return;

            long utcNowTicks = DateTime.UtcNow.Ticks;

            if (!state.candidateAPollResponseChirpSent &&
                state.candidateAPollResponseChirpUtcTicks > 0 &&
                utcNowTicks >= state.candidateAPollResponseChirpUtcTicks)
            {
                DebugLog($"Posting scheduled poll response chirp for Candidate A: {DescribeEntity(state.candidateA, "Candidate A")}.");
                PostCandidatePollResponseChirp(state.candidateA, state.candidateB, state.candidateAEffectId, state.candidateAPortraitIndex, "Candidate A", true, state);
                state.candidateAPollResponseChirpSent = true;
            }

            if (!state.candidateBPollResponseChirpSent &&
                state.candidateBPollResponseChirpUtcTicks > 0 &&
                utcNowTicks >= state.candidateBPollResponseChirpUtcTicks)
            {
                DebugLog($"Posting scheduled poll response chirp for Candidate B: {DescribeEntity(state.candidateB, "Candidate B")}.");
                PostCandidatePollResponseChirp(state.candidateB, state.candidateA, state.candidateBEffectId, state.candidateBPortraitIndex, "Candidate B", false, state);
                state.candidateBPollResponseChirpSent = true;
            }
        }

        private void EnsureElectionReminderChirpsScheduled(ref ElectionState state)
        {
            if (!state.HasCandidates ||
                state.electionYear <= 0 ||
                state.electionMonth <= 0 ||
                state.stage == ElectionCampaignStage.None ||
                state.stage == ElectionCampaignStage.Voting)
            {
                return;
            }

            ElectionUtility.GetPreviousCalendarDate(
                state.electionYear,
                state.electionMonth,
                1,
                out int reminderYear,
                out int reminderMonth,
                out int reminderDay);

            int reminderDayKey = ElectionUtility.DayKey(reminderYear, reminderMonth, reminderDay);
            const int reminderMinute = 17 * 60;

            if (state.candidateAElectionReminderChirpDayKey != reminderDayKey ||
                state.candidateAElectionReminderChirpMinute != reminderMinute)
            {
                state.candidateAElectionReminderChirpSent = false;
                state.candidateAElectionReminderChirpDayKey = reminderDayKey;
                state.candidateAElectionReminderChirpMinute = reminderMinute;
            }

            if (state.candidateBElectionReminderChirpDayKey != reminderDayKey ||
                state.candidateBElectionReminderChirpMinute != reminderMinute)
            {
                state.candidateBElectionReminderChirpSent = false;
                state.candidateBElectionReminderChirpDayKey = reminderDayKey;
                state.candidateBElectionReminderChirpMinute = reminderMinute;
            }
        }

        private void ProcessScheduledElectionReminderChirps(ref ElectionState state, DateTime now)
        {
            if (!state.HasCandidates || state.stage == ElectionCampaignStage.None || state.stage == ElectionCampaignStage.Voting)
                return;

            EnsureElectionReminderChirpsScheduled(ref state);

            if (!state.candidateAElectionReminderChirpSent &&
                state.candidateAElectionReminderChirpDayKey > 0 &&
                IsCurrentCalendarAtOrAfter(now, state.candidateAElectionReminderChirpDayKey, state.candidateAElectionReminderChirpMinute))
            {
                DebugLog($"Posting election reminder chirp for Candidate A: {DescribeEntity(state.candidateA, "Candidate A")}.");
                PostCandidateElectionReminderChirp(state, true, now);
                state.candidateAElectionReminderChirpSent = true;
            }

            if (!state.candidateBElectionReminderChirpSent &&
                state.candidateBElectionReminderChirpDayKey > 0 &&
                IsCurrentCalendarAtOrAfter(now, state.candidateBElectionReminderChirpDayKey, state.candidateBElectionReminderChirpMinute))
            {
                DebugLog($"Posting election reminder chirp for Candidate B: {DescribeEntity(state.candidateB, "Candidate B")}.");
                PostCandidateElectionReminderChirp(state, false, now);
                state.candidateBElectionReminderChirpSent = true;
            }
        }

        private void PostCandidatePollResponseChirp(Entity candidate, Entity opponent, int effectId, int portraitIndex, string fallbackName, bool candidateIsA, ElectionState state)
        {
            if (candidate == Entity.Null || !EntityManager.Exists(candidate))
            {
                DebugLog($"Skipped poll response chirp for {fallbackName}: candidate entity is not available ({FormatEntity(candidate)}).");
                return;
            }

            int ownVotes = candidateIsA ? state.pollVotesA : state.pollVotesB;
            int opponentVotes = candidateIsA ? state.pollVotesB : state.pollVotesA;
            string name = GetEntityName(candidate, fallbackName);
            string opponentName = GetEntityName(opponent, candidateIsA ? "Candidate B" : "Candidate A");
            string portraitImageSource = CandidatePortraitCatalog.GetPortraitImageSource(EntityManager, candidate, portraitIndex);
            DebugLog($"Candidate poll response chirp posted: candidate={DescribeEntity(candidate, fallbackName)}, opponent={DescribeEntity(opponent, candidateIsA ? "Candidate B" : "Candidate A")}, ownVotes={ownVotes}, opponentVotes={opponentVotes}, undecided={state.pollUndecided}, effectId={effectId}, portraitIndex={portraitIndex}.");

            string resultComment;
            ElectionPollSummary summary = ElectionPollUtility.BuildSummary(
                state.pollVotesA,
                state.pollVotesB,
                state.pollUndecided,
                GetEntityName(state.candidateA, "Candidate A"),
                GetEntityName(state.candidateB, "Candidate B"));
            if (summary.WithinMargin)
                resultComment = $"The latest poll is a statistical dead heat against {opponentName}.";
            else if (ownVotes > opponentVotes)
                resultComment = $"The latest poll has us ahead of {opponentName}, outside the +/-{summary.MarginOfError}% margin of error.";
            else if (ownVotes < opponentVotes)
                resultComment = $"The latest poll has us behind {opponentName}, but undecided voters can still move this race.";
            else
                resultComment = $"The latest poll has us tied with {opponentName}.";

            string text = $"I am {{LINK_1}}. {resultComment} Donations are open, and every contribution helps move this race.";

            if (!CustomChirpsBridge.PostLargeChirpFromEntityWithPortraitImage(text, candidate, candidate, portraitImageSource, name))
                CustomChirpsBridge.PostChirpFromEntity(text, candidate, candidate, name);
        }

        private void PostCandidateElectionReminderChirp(ElectionState state, bool candidateA, DateTime now)
        {
            Entity candidate = candidateA ? state.candidateA : state.candidateB;
            Entity opponent = candidateA ? state.candidateB : state.candidateA;
            string fallbackName = candidateA ? "Candidate A" : "Candidate B";
            string opponentFallbackName = candidateA ? "Candidate B" : "Candidate A";
            int portraitIndex = candidateA ? state.candidateAPortraitIndex : state.candidateBPortraitIndex;
            int effectId = candidateA ? state.candidateAEffectId : state.candidateBEffectId;
            bool negativeSoftened = candidateA ? state.candidateANegativeSoftened : state.candidateBNegativeSoftened;
            int opponentEffectId = candidateA ? state.candidateBEffectId : state.candidateAEffectId;
            bool opponentNegativeSoftened = candidateA ? state.candidateBNegativeSoftened : state.candidateANegativeSoftened;

            if (candidate == Entity.Null || !EntityManager.Exists(candidate))
            {
                DebugLog($"Skipped election reminder chirp for {fallbackName}: candidate entity is not available ({FormatEntity(candidate)}).");
                return;
            }

            string name = GetEntityName(candidate, fallbackName);
            string opponentName = GetEntityName(opponent, opponentFallbackName);
            ElectionEffectDefinition effect = ElectionEffects.Get(effectId, negativeSoftened);
            ElectionEffectDefinition opponentEffect = ElectionEffects.Get(opponentEffectId, opponentNegativeSoftened);
            string profileIntro = GetCandidateProfileIntro(state, candidateA);
            string portraitImageSource = CandidatePortraitCatalog.GetPortraitImageSource(EntityManager, candidate, portraitIndex);
            string text = PickElectionReminderMessage(state, candidateA, now, profileIntro, opponentName, effect, opponentEffect);

            DebugLog($"Election reminder chirp posted: candidate={DescribeEntity(candidate, fallbackName)}, opponent={DescribeEntity(opponent, opponentFallbackName)}, effectId={effectId}, opponentEffectId={opponentEffectId}, portraitIndex={portraitIndex}.");
            if (!CustomChirpsBridge.PostLargeChirpFromEntityWithPortraitImage(text, candidate, candidate, portraitImageSource, name))
                CustomChirpsBridge.PostChirpFromEntity(text, candidate, candidate, name);
        }

        private string PickElectionReminderMessage(
            ElectionState state,
            bool candidateA,
            DateTime now,
            string profileIntro,
            string opponentName,
            ElectionEffectDefinition effect,
            ElectionEffectDefinition opponentEffect)
        {
            int candidateIndex = candidateA ? 0 : 1;
            int seed = math.abs(
                state.electionYear * 10000 +
                state.electionMonth * 257 +
                candidateIndex * 7919 +
                state.candidateAEffectId * 17 +
                state.candidateBEffectId * 31 +
                now.Day);
            Unity.Mathematics.Random random = Unity.Mathematics.Random.CreateFromIndex((uint)math.max(1, seed));
            int template = random.NextInt(6);
            string votingWindow = GetUpcomingVotingWindowText(state);

            switch (template)
            {
                case 0:
                    return $"I am {{LINK_1}}, {profileIntro}. Tomorrow is election day. Polls are open from {votingWindow}, and I am asking for your vote.";
                case 1:
                    return $"I am {{LINK_1}}. Tomorrow, residents choose the next mayor. My platform {effect.PositiveImpact.Sentence}, and every vote can shape the city.";
                case 2:
                    return $"I am {{LINK_1}}. Make a plan to vote tomorrow from {votingWindow}. This race is about {effect.PositiveImpact.Label.ToLowerInvariant()}, and your voice matters.";
                case 3:
                    return $"I am {{LINK_1}}. Tomorrow's election is a choice: my platform {effect.PositiveImpact.Sentence}, while {opponentName}'s platform {opponentEffect.NegativeImpact.Sentence}.";
                case 4:
                    return $"I am {{LINK_1}}. {opponentName} still has to explain why their platform {opponentEffect.NegativeImpact.Sentence}. Tomorrow, voters can demand better.";
                default:
                    return $"I am {{LINK_1}}. One day remains before the election, and I am ready to serve. Please vote tomorrow from {votingWindow}.";
            }
        }

        private string GetUpcomingVotingWindowText(ElectionState state)
        {
            int startMinute = state.stage == ElectionCampaignStage.Voting
                ? ElectionUtility.NormalizeVotingStartMinute(state.votingStartMinute)
                : ElectionUtility.GetConfiguredVotingStartMinute();
            int endMinute = state.stage == ElectionCampaignStage.Voting
                ? ElectionUtility.NormalizeVotingEndMinute(state.votingEndMinute)
                : ElectionUtility.GetConfiguredVotingEndMinute();

            return FormatVotingWindow(startMinute, endMinute);
        }

        private static string FormatVotingWindow(int startMinute, int endMinute)
        {
            return $"{ElectionUtility.FormatHourText(startMinute)} to {ElectionUtility.FormatHourText(endMinute)}";
        }

        private static int GetResultsAnnouncementMinute(ElectionState state)
        {
            return state.resultsAnnouncementMinute > 0
                ? state.resultsAnnouncementMinute
                : ElectionUtility.ResultsAnnouncementMinute;
        }

        private static void EnsureActiveElectionTiming(ref ElectionState state)
        {
            if (state.stage != ElectionCampaignStage.Voting)
                return;

            state.votingStartMinute = ElectionUtility.NormalizeVotingStartMinute(state.votingStartMinute);
            state.votingEndMinute = ElectionUtility.NormalizeVotingEndMinute(state.votingEndMinute);
            state.resultsAnnouncementMinute = GetResultsAnnouncementMinute(state);
        }

        private bool IsCurrentCalendarAtOrAfter(DateTime now, int dayKey, int minuteOfDay)
        {
            int currentDayKey = ElectionUtility.CurrentCalendarDayKey(World, now);
            if (currentDayKey != dayKey)
                return currentDayKey > dayKey;

            return ElectionUtility.MinuteOfDay(now) >= minuteOfDay;
        }

        private void ScheduleCandidateDonationThankYouChirp(int candidateIndex, Entity candidate, string name, int portraitIndex, int amount, int totalDonation, PlatformSofteningResult softening)
        {
            if (candidateIndex == 0)
                ScheduleCandidateDonationThankYouChirp(ref m_CandidateADonationChirp, candidate, name, portraitIndex, amount, totalDonation, softening);
            else
                ScheduleCandidateDonationThankYouChirp(ref m_CandidateBDonationChirp, candidate, name, portraitIndex, amount, totalDonation, softening);
        }

        private static void ScheduleCandidateDonationThankYouChirp(ref PendingDonationChirp pending, Entity candidate, string name, int portraitIndex, int amount, int totalDonation, PlatformSofteningResult softening)
        {
            if (pending.candidate != candidate)
            {
                pending.batchAmount = 0;
                pending.softenedPlatform = false;
                pending.softenedLabel = null;
                pending.softenedPreviousValue = null;
                pending.softenedCurrentValue = null;
            }

            pending.candidate = candidate;
            pending.name = name;
            pending.portraitIndex = portraitIndex;
            pending.batchAmount += amount;
            pending.totalDonation = totalDonation;
            pending.dueUtcTicks = DateTime.UtcNow.AddMinutes(1).Ticks;

            if (softening.softened)
            {
                pending.softenedPlatform = true;
                pending.softenedLabel = softening.label;
                pending.softenedPreviousValue = softening.previousValue;
                pending.softenedCurrentValue = softening.currentValue;
            }
        }

        private void ProcessScheduledDonationThankYouChirps()
        {
            long utcNowTicks = DateTime.UtcNow.Ticks;
            ProcessScheduledDonationThankYouChirp(ref m_CandidateADonationChirp, utcNowTicks);
            ProcessScheduledDonationThankYouChirp(ref m_CandidateBDonationChirp, utcNowTicks);
        }

        private void ProcessScheduledDonationThankYouChirp(ref PendingDonationChirp pending, long utcNowTicks)
        {
            if (pending.dueUtcTicks <= 0 || utcNowTicks < pending.dueUtcTicks)
                return;

            PostCandidateDonationThankYouChirp(
                pending.candidate,
                pending.name,
                pending.portraitIndex,
                pending.batchAmount,
                pending.totalDonation,
                pending.softenedPlatform,
                pending.softenedLabel,
                pending.softenedPreviousValue,
                pending.softenedCurrentValue);

            pending = default(PendingDonationChirp);
        }

        private void PostCandidateDonationThankYouChirp(Entity candidate, string name, int portraitIndex, int amount, int totalDonation, bool softenedPlatform, string softenedLabel, string softenedPreviousValue, string softenedCurrentValue)
        {
            if (candidate == Entity.Null || !EntityManager.Exists(candidate))
            {
                DebugLog($"Skipped delayed donation thank-you chirp for {name}: candidate entity is not available ({FormatEntity(candidate)}).");
                return;
            }

            string portraitImageSource = CandidatePortraitCatalog.GetPortraitImageSource(EntityManager, candidate, portraitIndex);
            string donationText = amount == ElectionDonationTiers.FixedDonationAmount
                ? $"the campaign donation of {amount:n0}"
                : $"the campaign donations totaling {amount:n0}";
            string softeningText = softenedPlatform
                ? $" The campaign has softened its platform: {softenedLabel} changed from {softenedPreviousValue} to {softenedCurrentValue}."
                : string.Empty;
            string text = $"I am {{LINK_1}}. Thank you for {donationText}. Total donated to my campaign so far is {totalDonation:n0}.{softeningText}";
            DebugLog($"Posting delayed donation thank-you chirp: candidate={DescribeEntity(candidate, name)}, batchAmount={amount:n0}, totalDonation={totalDonation:n0}, portraitIndex={portraitIndex}.");

            if (!CustomChirpsBridge.PostLargeChirpFromEntityWithPortraitImage(text, candidate, candidate, portraitImageSource, name))
                CustomChirpsBridge.PostChirpFromEntity(text, candidate, candidate, name);
        }

        private PlatformSofteningResult TrySoftenCandidatePlatform(ref ElectionState state, int candidateIndex, int totalDonation)
        {
            if (totalDonation <= kDonationSofteningThreshold)
                return default;

            bool alreadyAttempted = candidateIndex == 0 ? state.candidateASoftenAttempted : state.candidateBSoftenAttempted;
            if (alreadyAttempted)
                return default;

            if (candidateIndex == 0)
                state.candidateASoftenAttempted = true;
            else
                state.candidateBSoftenAttempted = true;

            int effectId = candidateIndex == 0 ? state.candidateAEffectId : state.candidateBEffectId;
            Unity.Mathematics.Random random = CreateCampaignRandom(DateTime.UtcNow, 37717 + candidateIndex * 719 + effectId + totalDonation);
            if (random.NextInt(100) >= 50)
            {
                DebugLog($"Platform softening attempt failed: candidateIndex={candidateIndex}, totalDonation={totalDonation:n0}, threshold={kDonationSofteningThreshold:n0}.");
                return default;
            }

            if (candidateIndex == 0)
                state.candidateANegativeSoftened = true;
            else
                state.candidateBNegativeSoftened = true;

            ElectionEffectDefinition previous = ElectionEffects.Get(effectId);
            ElectionEffectDefinition current = ElectionEffects.Get(effectId, true);
            DebugLog($"Platform softened: candidateIndex={candidateIndex}, totalDonation={totalDonation:n0}, effectId={effectId}, {previous.NegativeImpact.Label} {previous.NegativeImpact.ValueText}->{current.NegativeImpact.ValueText}.");

            return new PlatformSofteningResult
            {
                softened = true,
                label = previous.NegativeImpact.Label,
                previousValue = previous.NegativeImpact.ValueText,
                currentValue = current.NegativeImpact.ValueText
            };
        }

        private void ConvinceCandidateToChangePlatform(ref ElectionState state, DateTime now, int candidateIndex, string candidateName, string mayorName)
        {
            Entity candidate = candidateIndex == 0 ? state.candidateA : state.candidateB;
            int oldEffectId = candidateIndex == 0 ? state.candidateAEffectId : state.candidateBEffectId;
            int otherEffectId = candidateIndex == 0 ? state.candidateBEffectId : state.candidateAEffectId;
            bool negativeSoftened = candidateIndex == 0 ? state.candidateANegativeSoftened : state.candidateBNegativeSoftened;
            ElectionEffectDefinition oldEffect = ElectionEffects.Get(oldEffectId, negativeSoftened);

            int newEffectId = PickDifferentEffect(candidate, now, 55103 + candidateIndex * 811 + oldEffectId + (int)m_SimulationSystem.frameIndex, otherEffectId);
            for (int attempt = 0; attempt < 64 &&
                    (ElectionEffects.HasSamePlatform(newEffectId, oldEffectId) ||
                     ElectionEffects.HasSamePlatform(newEffectId, otherEffectId) ||
                     HasSameNegativeImpact(newEffectId, oldEffectId)); attempt++)
            {
                int value = math.abs(newEffectId + candidate.Index * 173 + candidate.Version * 19 + attempt * 12289 + (int)m_SimulationSystem.frameIndex);
                newEffectId = ElectionEffects.CreateRandomId(value);
            }

            if (candidateIndex == 0)
            {
                state.candidateAEffectId = newEffectId;
                state.candidateAPlatformChirpSent = false;
                state.candidateAPlatformChirpUtcTicks = DateTime.UtcNow.AddMinutes(1).Ticks;
            }
            else
            {
                state.candidateBEffectId = newEffectId;
                state.candidateBPlatformChirpSent = false;
                state.candidateBPlatformChirpUtcTicks = DateTime.UtcNow.AddMinutes(1).Ticks;
            }

            ElectionEffectDefinition newEffect = ElectionEffects.Get(newEffectId, negativeSoftened);
            DebugLog($"Bribe succeeded: mayor={DescribeEntity(state.mayor, mayorName)}, target={DescribeEntity(candidate, candidateName)}, oldEffect={oldEffectId}, newEffect={newEffectId}, softened={negativeSoftened}.");
            PostMayorPlatformMeetingChirp(state, mayorName, candidateName, true, oldEffect, newEffect);
        }

        private void PostMayorPlatformMeetingChirp(ElectionState state, string mayorName, string candidateName, bool succeeded, ElectionEffectDefinition oldEffect, ElectionEffectDefinition newEffect)
        {
            Entity mayor = state.mayor;
            if (mayor == Entity.Null || !EntityManager.Exists(mayor))
            {
                PostElectionChirp(
                    succeeded
                        ? $"{candidateName} agreed to revise their platform after a mayoral meeting."
                        : $"{candidateName}'s platform did not change after a mayoral meeting.",
                    Entity.Null);
                return;
            }

            string portraitImageSource = CandidatePortraitCatalog.GetPortraitImageSource(
                EntityManager,
                mayor,
                GetMayorPortraitIndex(state));
            string text = succeeded
                ? $"I am {{LINK_1}}. I met with {candidateName} today to discuss their platform. We found enough common ground that they agreed to revise it. Their updated tradeoff is {newEffect.NegativeImpact.ValueText} {newEffect.NegativeImpact.Label}."
                : $"I am {{LINK_1}}. I met with {candidateName} today to discuss their platform. The conversation was constructive, but we still disagreed on key points, and I was not able to persuade them to revise it.";

            if (!CustomChirpsBridge.PostLargeChirpFromEntityWithPortraitImage(text, mayor, mayor, portraitImageSource, mayorName))
                CustomChirpsBridge.PostChirpFromEntity(text, mayor, mayor, mayorName);
        }

        private void ProcessScheduledCorruptionInvestigationChirp(ref ElectionState state)
        {
            if (state.corruptionInvestigationChirpUtcTicks <= 0 ||
                DateTime.UtcNow.Ticks < state.corruptionInvestigationChirpUtcTicks)
            {
                return;
            }

            Entity mayor = state.corruptionInvestigationMayor != Entity.Null ? state.corruptionInvestigationMayor : state.mayor;
            string mayorName = GetEntityName(mayor, "the mayor");
            CustomChirpsBridge.PostChirp(
                $"Police confirm {mayorName} is facing a corruption investigation after allegations of mayoral campaign bribery.",
                DepartmentAccountBridge.Police,
                mayor,
                "Police Department");
            DebugLog($"Posted corruption investigation chirp for mayor={DescribeEntity(mayor, mayorName)}.");
            state.corruptionInvestigationChirpUtcTicks = 0;
            state.corruptionInvestigationMayor = Entity.Null;
        }

        private void StartElection(ref ElectionState state, DateTime now)
        {
            if (!state.HasCandidates)
            {
                DebugLog($"Election start skipped: no candidate pair. state={DescribeState(state)}");
                return;
            }

            ClearVoteTrips();
            state.stage = ElectionCampaignStage.Voting;
            state.electionDayKey = ElectionUtility.CurrentCalendarDayKey(World, now);
            state.votingStartMinute = ElectionUtility.GetConfiguredVotingStartMinute();
            state.votingEndMinute = ElectionUtility.GetConfiguredVotingEndMinute();
            state.resultsAnnouncementMinute = ElectionUtility.ResultsAnnouncementMinute;
            state.voteRequests = 0;
            state.voteArrivals = 0;
            state.votesA = 0;
            state.votesB = 0;
            state.candidateAVotedChirpSent = false;
            state.candidateBVotedChirpSent = false;
            string votingWindow = FormatVotingWindow(state.votingStartMinute, state.votingEndMinute);
            string resultsTime = ElectionUtility.FormatHourText(state.resultsAnnouncementMinute);
            DebugLog($"Election started: date={ElectionUtility.FormatCurrentDate(World, now)}, electionDayKey={state.electionDayKey}, votingWindow={ElectionUtility.FormatClockTime(state.votingStartMinute)}-{ElectionUtility.FormatClockTime(state.votingEndMinute)}, results={ElectionUtility.FormatClockTime(state.resultsAnnouncementMinute)}, A={DescribeEntity(state.candidateA, "Candidate A")}, B={DescribeEntity(state.candidateB, "Candidate B")}.");

            PostElectionChirp($"Election day has begun on {ElectionUtility.FormatCurrentDate(World, now)}. Polls are open from {votingWindow} at education, police, fire, welfare, and administration buildings. You can watch voting in real time by clicking the Voting sites button to view the overlay. Results will be announced at {resultsTime}.", Entity.Null);
        }

        private void CompleteElection(ref ElectionState state, DateTime now)
        {
            EnsureActiveElectionTiming(ref state);

            int winnerIndex;
            if (state.votesA > state.votesB)
            {
                winnerIndex = 0;
            }
            else if (state.votesB > state.votesA)
            {
                winnerIndex = 1;
            }
            else
            {
                winnerIndex = (state.electionDayKey + state.voteArrivals) % 2;
            }

            Entity winner = winnerIndex == 0 ? state.candidateA : state.candidateB;
            int effectId = winnerIndex == 0 ? state.candidateAEffectId : state.candidateBEffectId;
            bool winnerNegativeSoftened = winnerIndex == 0 ? state.candidateANegativeSoftened : state.candidateBNegativeSoftened;
            string winnerName = GetEntityName(winner, winnerIndex == 0 ? "Candidate A" : "Candidate B");
            string nameA = GetEntityName(state.candidateA, "Candidate A");
            string nameB = GetEntityName(state.candidateB, "Candidate B");
            int population = GetPopulation();
            int turnoutPct = population > 0 ? (int)math.round((state.votesA + state.votesB) * 100f / population) : 0;

            state.mayor = winner;
            state.mayorEffectId = effectId;
            state.mayorNegativeSoftened = winnerNegativeSoftened;
            ElectionUtility.GetCurrentCalendarDate(World, now, out int currentYear, out _, out _);
            state.mayorEffectTermYear = state.mayorTermYear != 0 ? state.mayorTermYear : currentYear;
            state.mayorMoneyApplied = false;
            state.stage = ElectionCampaignStage.None;
            DebugLog($"Election completed: date={ElectionUtility.FormatCurrentDate(World, now)}, winnerIndex={winnerIndex}, winner={DescribeEntity(winner, winnerName)}, votesA={state.votesA}, votesB={state.votesB}, voteRequests={state.voteRequests}, voteArrivals={state.voteArrivals}, population={population}, turnoutPct={turnoutPct}, effectId={effectId}.");

            ElectionEffectDefinition effect = ElectionEffects.Get(effectId, winnerNegativeSoftened);
            PostElectionChirp(
                $"Election results for {ElectionUtility.FormatCurrentDate(World, now)} are final. {winnerName} has been elected mayor. Turnout was {turnoutPct}% of the population ({state.votesA + state.votesB:n0} votes). Results: {nameA} {state.votesA:n0}, {nameB} {state.votesB:n0}. Mayor effect: {effect.Name}, which {effect.Description}.",
                winner);

            ClearVoteTrips();
        }

        private void SyncElectionDaySundayOverride(ElectionState state, DateTime now)
        {
            if (!(Mod.m_Setting?.ElectionDayActsLikeSunday ?? true) ||
                !state.HasCandidates ||
                state.electionYear <= 0 ||
                state.electionMonth <= 0)
            {
                RealisticTripsBridge.ClearElectionDaySundayOverride();
                return;
            }

            ElectionUtility.GetCurrentCalendarDate(World, now, out int currentYear, out int currentMonth, out int currentDay);
            int compare = ElectionUtility.CompareCalendarDate(
                state.electionYear,
                state.electionMonth,
                1,
                currentYear,
                currentMonth,
                currentDay);
            if (compare < 0)
            {
                RealisticTripsBridge.ClearElectionDaySundayOverride();
                return;
            }

            int daysPerMonth = RealisticTripsBridge.GetDaysPerMonth();
            int electionDayOfYear = (state.electionMonth - 1) * daysPerMonth + 1;
            RealisticTripsBridge.SetElectionDaySundayOverride(state.electionYear, electionDayOfYear, true);
        }

        private void SyncElectionDaySpecialEventSuppression(ElectionState state, DateTime now)
        {
            if (!state.HasCandidates ||
                state.electionYear <= 0 ||
                state.electionMonth <= 0)
            {
                RealisticTripsBridge.ClearElectionDaySpecialEventsSuppressed();
                return;
            }

            ElectionUtility.GetCurrentCalendarDate(World, now, out int currentYear, out int currentMonth, out int currentDay);
            int compare = ElectionUtility.CompareCalendarDate(
                state.electionYear,
                state.electionMonth,
                1,
                currentYear,
                currentMonth,
                currentDay);
            if (compare < 0)
            {
                RealisticTripsBridge.ClearElectionDaySpecialEventsSuppressed();
                return;
            }

            int daysPerMonth = RealisticTripsBridge.GetDaysPerMonth();
            int electionDayOfYear = (state.electionMonth - 1) * daysPerMonth + 1;
            RealisticTripsBridge.SetElectionDaySpecialEventsSuppressed(state.electionYear, electionDayOfYear, true);
        }

        private bool TryPickCandidates(DateTime now, out Entity candidateA, out Entity candidateB)
        {
            candidateA = Entity.Null;
            candidateB = Entity.Null;

            using (NativeArray<Entity> entities = m_CandidateQuery.ToEntityArray(Allocator.Temp))
            using (NativeArray<Citizen> citizens = m_CandidateQuery.ToComponentDataArray<Citizen>(Allocator.Temp))
            {
                if (entities.Length < 2)
                {
                    DebugLog($"Candidate selection failed: eligible query returned only {entities.Length} resident entities.");
                    return false;
                }

                uint seed = (uint)math.max(1, now.Year * 10000 + now.Month * 100 + now.Day + (int)m_SimulationSystem.frameIndex);
                Unity.Mathematics.Random random = Unity.Mathematics.Random.CreateFromIndex(seed);

                for (int attempt = 0; attempt < entities.Length * 4 && candidateA == Entity.Null; attempt++)
                {
                    int index = random.NextInt(entities.Length);
                    if (ElectionUtility.IsEligibleResident(EntityManager, entities[index], citizens[index]))
                        candidateA = entities[index];
                }

                for (int attempt = 0; attempt < entities.Length * 4 && candidateB == Entity.Null; attempt++)
                {
                    int index = random.NextInt(entities.Length);
                    if (entities[index] != candidateA && ElectionUtility.IsEligibleResident(EntityManager, entities[index], citizens[index]))
                        candidateB = entities[index];
                }

                DebugLog($"Candidate selection attempt complete: queryCount={entities.Length}, seed={seed}, candidateA={DescribeEntity(candidateA, "Candidate A")}, candidateB={DescribeEntity(candidateB, "Candidate B")}.");
            }

            return candidateA != Entity.Null && candidateB != Entity.Null;
        }

        private bool TryPickTemporaryMayor(DateTime now, out Entity mayor)
        {
            mayor = Entity.Null;

            using (NativeArray<Entity> entities = m_CandidateQuery.ToEntityArray(Allocator.Temp))
            using (NativeArray<Citizen> citizens = m_CandidateQuery.ToComponentDataArray<Citizen>(Allocator.Temp))
            {
                if (entities.Length < 1)
                {
                    DebugLog("Temporary mayor selection failed: eligible query returned no resident entities.");
                    return false;
                }

                uint seed = (uint)math.max(1, now.Year * 10000 + now.Month * 131 + now.Day + 4241 + (int)m_SimulationSystem.frameIndex);
                Unity.Mathematics.Random random = Unity.Mathematics.Random.CreateFromIndex(seed);

                for (int attempt = 0; attempt < entities.Length * 4; attempt++)
                {
                    int index = random.NextInt(entities.Length);
                    if (ElectionUtility.IsEligibleResident(EntityManager, entities[index], citizens[index]))
                    {
                        mayor = entities[index];
                        DebugLog($"Temporary mayor selection succeeded: queryCount={entities.Length}, seed={seed}, mayor={DescribeEntity(mayor, "Temporary Mayor")}.");
                        return true;
                    }
                }
            }

            DebugLog("Temporary mayor selection failed after random attempts.");
            return false;
        }

        private void RunPoll(ref ElectionState state, DateTime now)
        {
            state.pollVotesA = 0;
            state.pollVotesB = 0;
            state.pollUndecided = 0;

            int samplePercent = math.clamp(Mod.m_Setting?.PollSamplePercent ?? 2, 1, 10);
            int population = GetPopulation();
            int targetSampleCount = math.max(1, (int)math.ceil(population * samplePercent / 100f));
            uint seed = (uint)math.max(1, now.Year * 10000 + now.Month * 101 + now.Day + 3109);
            Unity.Mathematics.Random random = Unity.Mathematics.Random.CreateFromIndex(seed);
            int eligibleCount = 0;
            int sampleCount = 0;

            using (NativeArray<Entity> entities = m_CandidateQuery.ToEntityArray(Allocator.Temp))
            using (NativeArray<Citizen> citizens = m_CandidateQuery.ToComponentDataArray<Citizen>(Allocator.Temp))
            {
                List<int> eligibleIndices = new List<int>(math.min(entities.Length, targetSampleCount));
                for (int i = 0; i < entities.Length; i++)
                {
                    if (ElectionUtility.IsEligibleVoterResident(EntityManager, entities[i], citizens[i]))
                        eligibleIndices.Add(i);
                }

                eligibleCount = eligibleIndices.Count;
                sampleCount = math.min(targetSampleCount, eligibleCount);
                for (int i = 0; i < sampleCount; i++)
                {
                    int swapIndex = i + random.NextInt(eligibleIndices.Count - i);
                    (eligibleIndices[i], eligibleIndices[swapIndex]) = (eligibleIndices[swapIndex], eligibleIndices[i]);
                    int citizenIndex = eligibleIndices[i];

                    float probabilityA = ElectionUtility.GetVoteProbabilityForA(EntityManager, entities[citizenIndex], citizens[citizenIndex], state);
                    float undecidedChance = ElectionUtility.GetUndecidedProbability(probabilityA, state);
                    if (random.NextFloat() < undecidedChance)
                    {
                        state.pollUndecided++;
                    }
                    else if (random.NextFloat() < probabilityA)
                    {
                        state.pollVotesA++;
                    }
                    else
                    {
                        state.pollVotesB++;
                    }
                }
            }

            ApplyPollUndecidedFloor(ref state, sampleCount);
            DebugLog($"Poll simulation complete: date={ElectionUtility.FormatCurrentDate(World, now)}, population={population}, samplePercent={samplePercent}, targetSample={targetSampleCount}, eligibleResidents={eligibleCount}, actualSample={sampleCount}, seed={seed}, votesA={state.pollVotesA}, votesB={state.pollVotesB}, undecided={state.pollUndecided}, donationA={state.donationA:n0}, donationB={state.donationB:n0}.");
        }

        private static void ApplyPollUndecidedFloor(ref ElectionState state, int sampleCount)
        {
            if (sampleCount < 6)
                return;

            int targetUndecided = math.max(1, (int)math.round(sampleCount * 0.08f));
            while (state.pollUndecided < targetUndecided)
            {
                if (state.pollVotesA >= state.pollVotesB && state.pollVotesA > 0)
                {
                    state.pollVotesA--;
                }
                else if (state.pollVotesB > 0)
                {
                    state.pollVotesB--;
                }
                else if (state.pollVotesA > 0)
                {
                    state.pollVotesA--;
                }
                else
                {
                    return;
                }

                state.pollUndecided++;
            }
        }

        private void CaptureCandidateProfile(Entity candidate, out int age, out int education, out int workType, out int wealth)
        {
            Citizen citizen = EntityManager.GetComponentData<Citizen>(candidate);
            age = (int)citizen.GetAge();
            education = citizen.GetEducationLevel();
            workType = ElectionUtility.GetWorkType(EntityManager, candidate);
            wealth = ElectionUtility.GetWealthBracket(EntityManager, candidate);
        }

        private string GetCandidateProfileIntro(ElectionState state, bool candidateA)
        {
            Entity candidate = candidateA ? state.candidateA : state.candidateB;
            int age = candidateA ? state.candidateAAge : state.candidateBAge;
            int education = candidateA ? state.candidateAEducation : state.candidateBEducation;
            int workType = candidateA ? state.candidateAWorkType : state.candidateBWorkType;
            int wealth = candidateA ? state.candidateAWealth : state.candidateBWealth;
            return ElectionCandidateProfileUtility.BuildChirpIntro(EntityManager, candidate, age, education, workType, wealth);
        }

        private int PickEffect(Entity candidate, DateTime now, int salt)
        {
            int value = math.abs(candidate.Index * 397 + candidate.Version * 31 + now.Year * 17 + now.Month * 101 + salt);
            return ElectionEffects.CreateRandomId(value);
        }

        private Unity.Mathematics.Random CreateCampaignRandom(DateTime now, int salt)
        {
            int value = math.abs(now.Year * 10000 + now.Month * 101 + now.Day * 17 + salt + (int)m_SimulationSystem.frameIndex);
            return Unity.Mathematics.Random.CreateFromIndex((uint)math.max(1, value));
        }

        private int PickDifferentEffect(Entity candidate, DateTime now, int salt, int excludedEffectId)
        {
            int effectId = PickEffect(candidate, now, salt);
            for (int attempt = 0; attempt < 8 && ElectionEffects.HasSamePlatform(effectId, excludedEffectId); attempt++)
            {
                int value = math.abs(effectId + candidate.Index * 131 + candidate.Version * 17 + salt + attempt * 7919);
                effectId = ElectionEffects.CreateRandomId(value);
            }

            return effectId;
        }

        private static bool HasSameNegativeImpact(int firstEffectId, int secondEffectId)
        {
            if (firstEffectId == secondEffectId)
                return true;

            return ElectionEffects.Get(firstEffectId).NegativeImpact.Key == ElectionEffects.Get(secondEffectId).NegativeImpact.Key;
        }

        private bool TrySpendCityMoney(int amount)
        {
            Entity city = m_CitySystem.City;
            if (city == Entity.Null || !EntityManager.HasComponent<PlayerMoney>(city))
                return false;

            PlayerMoney money = EntityManager.GetComponentData<PlayerMoney>(city);
            if (!money.m_Unlimited && money.money < amount)
                return false;

            money.Subtract(amount);
            EntityManager.SetComponentData(city, money);
            return true;
        }

        private int GetPopulation()
        {
            Entity city = m_CitySystem.City;
            if (city != Entity.Null && EntityManager.HasComponent<Population>(city))
                return math.max(0, EntityManager.GetComponentData<Population>(city).m_Population);

            return 0;
        }

        private bool HasMinimumPopulation(string operation)
        {
            int population = GetPopulation();
            if (population >= MinimumPopulation)
            {
                m_LoggedPopulationGate = false;
                return true;
            }

            if (!m_LoggedPopulationGate)
            {
                m_LoggedPopulationGate = true;
                DebugLog($"Election lifecycle paused for {operation}: population {population:n0}/{MinimumPopulation:n0}.");
            }

            return false;
        }

        private string GetEntityName(Entity entity, string fallback)
        {
            return ElectionNameUtility.GetCitizenFullName(m_NameSystem, EntityManager, entity, fallback);
        }

        private static int GetMayorPortraitIndex(ElectionState state)
        {
            if (state.mayor == state.candidateA && state.candidateAPortraitIndex >= 0)
                return state.candidateAPortraitIndex;

            if (state.mayor == state.candidateB && state.candidateBPortraitIndex >= 0)
                return state.candidateBPortraitIndex;

            return CandidatePortraitCatalog.PickPortraitIndex(state.mayor, 4241);
        }

        private void DebugLog(string message)
        {
            ElectionDebug.Log(message);
        }

        private void LogStateEntity(Entity entity, string reason)
        {
            if (!ElectionDebug.Enabled || m_LastLoggedStateEntity == entity)
                return;

            m_LastLoggedStateEntity = entity;
            DebugLog($"ElectionState entity changed: {FormatEntity(entity)} ({reason}).");
        }

        private void LogInvalidActiveCampaignCandidates(ElectionState state, DateTime now)
        {
            string report =
                $"Active campaign candidate validity issue at {ElectionUtility.FormatCurrentDate(World, now)}: " +
                $"state={DescribeState(state)}, A={DescribeEntity(state.candidateA, "Candidate A")}, B={DescribeEntity(state.candidateB, "Candidate B")}. " +
                "Replacing only the invalid candidate slot and preserving the rest of the campaign.";

            if (report == m_LastInvalidCandidateReport)
                return;

            m_LastInvalidCandidateReport = report;
            DebugLog(report);
        }

        private string DescribeState(ElectionState state)
        {
            return $"version={state.version}, initialized={state.initialized}, stage={state.stage}, accelerated={state.acceleratedCycle}, " +
                   $"selection={FormatMaybeDate(state.selectionYear, state.selectionMonth)}, poll={FormatMaybeDate(state.pollYear, state.pollMonth)}, election={FormatMaybeDate(state.electionYear, state.electionMonth)}, " +
                   $"hasCandidates={state.HasCandidates}, donations={state.donationA:n0}/{state.donationB:n0}, pollVotes={state.pollVotesA}/{state.pollVotesB}/{state.pollUndecided}, votes={state.votesA}/{state.votesB}, mayor={FormatEntity(state.mayor)}";
        }

        private static string FormatMaybeDate(int year, int month)
        {
            if (year <= 0 || month <= 0)
                return "unset";

            return ElectionUtility.FormatDate(year, month, 1);
        }

        private string DescribeEntity(Entity entity, string fallback)
        {
            string name = entity != Entity.Null && EntityManager.Exists(entity)
                ? GetEntityName(entity, fallback)
                : fallback;

            return $"{name} {FormatEntity(entity)} [{GetResidentValidityReason(entity)}]";
        }

        private static string FormatEntity(Entity entity)
        {
            if (entity == Entity.Null)
                return "Entity.Null";

            return $"Entity({entity.Index}:{entity.Version})";
        }

        private string GetResidentValidityReason(Entity entity)
        {
            if (entity == Entity.Null)
                return "invalid: null";

            if (!EntityManager.Exists(entity))
                return "invalid: missing entity";

            List<string> reasons = new List<string>(8);
            if (!EntityManager.HasComponent<Citizen>(entity))
            {
                reasons.Add("missing Citizen");
            }
            else
            {
                Citizen citizen = EntityManager.GetComponentData<Citizen>(entity);
                CitizenAge age = citizen.GetAge();
                if (age != CitizenAge.Adult && age != CitizenAge.Elderly)
                    reasons.Add($"ineligible age {age}");
                if ((citizen.m_State & CitizenFlags.Tourist) != 0)
                    reasons.Add("Tourist");
                if ((citizen.m_State & CitizenFlags.Commuter) != 0)
                    reasons.Add("Commuter");
            }

            if (!EntityManager.HasComponent<HouseholdMember>(entity))
                reasons.Add("missing HouseholdMember");
            if (EntityManager.HasComponent<Deleted>(entity))
                reasons.Add("Deleted");
            if (EntityManager.HasComponent<Temp>(entity))
                reasons.Add("Temp");
            if (EntityManager.HasComponent<HealthProblem>(entity))
                reasons.Add("HealthProblem");

            if (reasons.Count == 0)
                return "valid resident";

            return "invalid: " + string.Join(", ", reasons);
        }

        private void PostElectionChirp(string text, Entity target)
        {
            CustomChirpsBridge.PostChirp(text, DepartmentAccountBridge.CensusBureau, target, "Election Board");
        }

        private void PostElectionChirpWithCandidates(string text, Entity candidateA, Entity candidateB)
        {
            CustomChirpsBridge.PostChirpWith2Targets(text, DepartmentAccountBridge.CensusBureau, candidateA, candidateB, "Election Board");
        }

        private void ClearVoteTrips()
        {
            int cleared = 0;
            using (NativeArray<Entity> entities = m_VoteTripQuery.ToEntityArray(Allocator.Temp))
            {
                for (int i = 0; i < entities.Length; i++)
                {
                    if (EntityManager.HasComponent<ElectionVoteTrip>(entities[i]))
                    {
                        EntityManager.RemoveComponent<ElectionVoteTrip>(entities[i]);
                        cleared++;
                    }
                }
            }

            if (cleared > 0)
                DebugLog($"Cleared {cleared} election vote trip markers.");
        }
    }
}
