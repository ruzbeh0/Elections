using Elections.Bridge;
using Elections.Components;
using Elections.Models;
using Game;
using Game.Buildings;
using Game.City;
using Game.Citizens;
using Game.Common;
using Game.Economy;
using Game.Events;
using Game.Prefabs;
using Game.Simulation;
using Game.Tools;
using Game.UI;
using System;
using System.Collections.Generic;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;

namespace Elections.Systems
{
    public partial class ElectionLifecycleSystem : GameSystemBase
    {
        public const int BribeAmount = 5000000;
        public const int MinimumPopulation = 1500;
        public const int CashAssistanceTurnoutBonusPercent = 20;

        private const int kGoodPollMinimumSampleCount = 600;
        private const int kGoodPollPopulationShareDivisor = 100;
        private const int kVictoryPartyStartMinute = 19 * 60;
        private const int kVictoryPartySupporterChancePercent = 60;
        private const int kVictoryPartyMaxTripRequests = 1500;
        private const int kVictoryPartyPopulationShareDivisor = 10;
        private const int kVictoryPartyBatchTripLimit = 100;
        private const int kVictoryPartyBatchScanLimit = 1200;
        private const int kVictoryPartyBatchIntervalMinutes = 5;
        private const float kVictoryPartyMinDurationMinutes = 30f;
        private const float kVictoryPartyMaxDurationMinutes = 90f;
        private const int kBribeMeetingWindowHours = 24;
        private const int kBribeMeetingRetryMinutes = 30;
        private const int kBribeMeetingPriority = 150;
        private const int kBribeMeetingSuccessChancePercent = 60;
        private const float kBribeMeetingDurationMinutes = 60f;
        private const int kCorruptionRiskStepPercent = 10;
        private const int kCorruptionRiskMaxSteps = 5;
        private const int kPartyReputationEventMinute = 20 * 60;
        private const int kPartyReputationEventChancePercent = 10;
        private const int kPartyMilestoneReputationGain = 5;
        private const int kPartyPositiveEventReputationGain = 5;
        private const int kPartyMinorNegativeEventReputationLoss = 5;
        private const int kVoteTamperingMinBeforePollCloseMinutes = 30;
        private const int kVoteTamperingMaxBeforePollCloseMinutes = 180;
        private const float kVoteTamperingFireIntensity = 0.85f;
        private const ushort kElectionCorruptionJailTime = 180;
        private const int kSupportProgramBalanceVersion = 2;

        private EntityQuery m_StateQuery;
        private EntityQuery m_CandidateQuery;
        private EntityQuery m_VoteTripQuery;
        private EntityQuery m_CityHallQuery;
        private EntityQuery m_LandmarkQuery;
        private EntityQuery m_ParkQuery;
        private EntityQuery m_BribeLeisureVenueQuery;
        private CitySystem m_CitySystem;
        private CityServiceBudgetSystem m_CityServiceBudgetSystem;
        private NameSystem m_NameSystem;
        private SimulationSystem m_SimulationSystem;
        private PrefabSystem m_PrefabSystem;
        private PendingDonationChirp m_CandidateADonationChirp;
        private PendingDonationChirp m_CandidateBDonationChirp;
        private PendingDonationChirp m_CandidateCDonationChirp;
        private PendingDonationChirp m_CandidateDDonationChirp;
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

        private struct CandidateSimilarityProfile
        {
            public int age;
            public int education;
            public int workType;
            public int wealth;
            public bool hasCar;
            public bool hasCarKnown;
        }

        private struct CandidateSlotSnapshot
        {
            public Entity candidate;
            public int effectId;
            public int age;
            public int education;
            public int workType;
            public int wealth;
            public int portraitIndex;
            public int tagId;
            public int supportModifierPercent;
            public int donation;
            public bool negativeSoftened;
            public bool softenAttempted;
            public int corruptionRiskSteps;
            public bool platformChirpSent;
            public int platformChirpDayKey;
            public int platformChirpMinute;
            public long platformChirpUtcTicks;
        }

        private struct PartySlotSnapshot
        {
            public string name;
            public int color;
            public int reputation;
            public int consecutiveTerms;
            public int wins;
            public int lastTagReplacementYear;
            public int tagId1;
            public int tagId2;
            public int tagId3;
        }

        private struct PollSampleCandidate
        {
            public int citizenIndex;
            public float priority;
        }

        private struct PollingPlaceVoteTally
        {
            public int votesA;
            public int votesB;
            public int votesC;
            public int votesD;
        }

        [BurstCompile]
        private struct TallyVotesByPollingPlaceJob : IJob
        {
            [ReadOnly] public NativeArray<ElectionVoteTrip> voteTrips;
            public int electionDayKey;
            public NativeParallelHashMap<Entity, PollingPlaceVoteTally> tallies;

            public void Execute()
            {
                for (int i = 0; i < voteTrips.Length; i++)
                {
                    ElectionVoteTrip voteTrip = voteTrips[i];
                    if (!voteTrip.voted ||
                        voteTrip.electionDayKey != electionDayKey ||
                        voteTrip.pollingPlace == Entity.Null)
                    {
                        continue;
                    }

                    if (voteTrip.chosenCandidate < 0 || voteTrip.chosenCandidate >= ElectionState.MaxCandidateCount)
                        continue;

                    tallies.TryGetValue(voteTrip.pollingPlace, out PollingPlaceVoteTally tally);
                    AddTallyVote(ref tally, voteTrip.chosenCandidate);

                    tallies[voteTrip.pollingPlace] = tally;
                }
            }
        }

        [BurstCompile]
        private struct TallyVotesForPollingPlaceJob : IJob
        {
            [ReadOnly] public NativeArray<ElectionVoteTrip> voteTrips;
            public int electionDayKey;
            public Entity pollingPlace;
            public NativeArray<PollingPlaceVoteTally> tally;

            public void Execute()
            {
                PollingPlaceVoteTally result = default;
                for (int i = 0; i < voteTrips.Length; i++)
                {
                    ElectionVoteTrip voteTrip = voteTrips[i];
                    if (!voteTrip.voted ||
                        voteTrip.electionDayKey != electionDayKey ||
                        voteTrip.pollingPlace != pollingPlace)
                    {
                        continue;
                    }

                    AddTallyVote(ref result, voteTrip.chosenCandidate);
                }

                tally[0] = result;
            }
        }

        private static void AddTallyVote(ref PollingPlaceVoteTally tally, int candidateIndex)
        {
            switch (candidateIndex)
            {
                case 0:
                    tally.votesA++;
                    break;
                case 1:
                    tally.votesB++;
                    break;
                case 2:
                    tally.votesC++;
                    break;
                case 3:
                    tally.votesD++;
                    break;
            }
        }

        public override int GetUpdateInterval(SystemUpdatePhase phase)
        {
            return 1024;
        }

        protected override void OnCreate()
        {
            base.OnCreate();

            m_CitySystem = World.GetOrCreateSystemManaged<CitySystem>();
            m_CityServiceBudgetSystem = World.GetOrCreateSystemManaged<CityServiceBudgetSystem>();
            m_NameSystem = World.GetExistingSystemManaged<NameSystem>();
            m_SimulationSystem = World.GetOrCreateSystemManaged<SimulationSystem>();
            m_PrefabSystem = World.GetOrCreateSystemManaged<PrefabSystem>();

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
            m_CityHallQuery = GetCelebrationVenueQuery(ComponentType.ReadOnly<AdminBuilding>());
            m_LandmarkQuery = GetCelebrationVenueQuery(ComponentType.ReadOnly<Signature>());
            m_ParkQuery = GetCelebrationVenueQuery(ComponentType.ReadOnly<Game.Buildings.Park>());
            m_BribeLeisureVenueQuery = GetLeisureVenueQuery();
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

            ProcessPendingMayorInauguration(ref state, now);
            ProcessPartyReputationEvents(ref state, now, dayKey);
            ProcessTimedPollRelease(ref state, now);
            ProcessScheduledElectionStart(ref state, now);
            ProcessElectionVictoryPartyTrips(ref state, now);
            ProcessVoteTampering(ref state, now);
            ProcessVotingClosedChirp(ref state, now);

            if (state.stage == ElectionCampaignStage.Voting &&
                IsCurrentCalendarAtOrAfter(now, state.electionDayKey, GetResultsAnnouncementMinute(state)))
            {
                CompleteElection(ref state, now);
            }

            ProcessScheduledPlatformChirps(ref state, now);
            ProcessScheduledPollResponseChirps(ref state);
            ProcessScheduledElectionReminderChirps(ref state, now);
            ProcessPendingBribeMeeting(ref state, now);
            ProcessScheduledDonationThankYouChirps();
            ProcessScheduledEndorsementChirp(ref state);
            ProcessScheduledStrictVotingIdChirp(ref state);
            ProcessScheduledVoteTamperingProtestChirp(ref state);
            ProcessScheduledCorruptionInvestigationChirp(ref state);
            ProcessScheduledVictoryPartyChirps(ref state);

            EntityManager.SetComponentData(stateEntity, state);
        }

        private EntityQuery GetCelebrationVenueQuery(ComponentType markerType)
        {
            return GetEntityQuery(new EntityQueryDesc
            {
                All = new[]
                {
                    ComponentType.ReadOnly<Building>(),
                    ComponentType.ReadOnly<PrefabRef>(),
                    markerType
                },
                None = new[]
                {
                    ComponentType.Exclude<Deleted>(),
                    ComponentType.Exclude<Temp>()
                }
            });
        }

        private EntityQuery GetLeisureVenueQuery()
        {
            return GetEntityQuery(new EntityQueryDesc
            {
                All = new[]
                {
                    ComponentType.ReadOnly<Building>(),
                    ComponentType.ReadOnly<PrefabRef>(),
                    ComponentType.ReadOnly<Game.Buildings.LeisureProvider>()
                },
                None = new[]
                {
                    ComponentType.Exclude<Deleted>(),
                    ComponentType.Exclude<Temp>(),
                    ComponentType.Exclude<Owner>()
                }
            });
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

        public void RenameParty(int partyIndex, string name)
        {
            if (!ArePartiesEnabled())
                return;

            Entity stateEntity = EnsureStateEntity();
            ElectionState state = EntityManager.GetComponentData<ElectionState>(stateEntity);
            if (RealisticTripsBridge.TryGetCurrentDateTime(out DateTime now))
                PrepareStateForCurrentDate(ref state, now);
            else
                EnsurePartyState(ref state);

            if (!IsManagedPartyIndex(state, partyIndex))
                return;

            state.SetPartyName(partyIndex, SanitizePartyName(name, ElectionPartyTags.GetDefaultName(partyIndex)));
            EntityManager.SetComponentData(stateEntity, state);
        }

        public void SetPartyColor(int partyIndex, string color)
        {
            if (!ArePartiesEnabled())
                return;

            Entity stateEntity = EnsureStateEntity();
            ElectionState state = EntityManager.GetComponentData<ElectionState>(stateEntity);
            if (RealisticTripsBridge.TryGetCurrentDateTime(out DateTime now))
                PrepareStateForCurrentDate(ref state, now);
            else
                EnsurePartyState(ref state);

            if (!IsManagedPartyIndex(state, partyIndex) || !TryParsePartyColor(color, out int packedColor))
                return;

            state.SetPartyColor(partyIndex, packedColor);
            EntityManager.SetComponentData(stateEntity, state);
        }

        public void SetPartyTags(int partyIndex, string tagIds)
        {
            if (!ArePartiesEnabled())
                return;

            Entity stateEntity = EnsureStateEntity();
            ElectionState state = EntityManager.GetComponentData<ElectionState>(stateEntity);
            if (!RealisticTripsBridge.TryGetCurrentDateTime(out DateTime now))
                return;

            PrepareStateForCurrentDate(ref state, now);
            if (!IsManagedPartyIndex(state, partyIndex))
                return;

            if (state.stage != ElectionCampaignStage.None)
            {
                PostElectionChirp(L("Lifecycle.PartyTags.ReplaceOutsideCycle", "Party tags can only be replaced outside the election cycle."), Entity.Null);
                EntityManager.SetComponentData(stateEntity, state);
                return;
            }

            ElectionUtility.GetCurrentCalendarDate(World, now, out int year, out _, out _);
            if (!TryParsePartyTagSelection(tagIds, out int tagId1, out int tagId2, out int tagId3))
            {
                PostElectionChirp(L("Lifecycle.PartyTags.ChooseExactlyThree", "Choose exactly three party tags before saving."), Entity.Null);
                EntityManager.SetComponentData(stateEntity, state);
                return;
            }

            if (!IsValidPartyTagSelection(tagId1, tagId2, tagId3, out string reason))
            {
                PostElectionChirp(reason, Entity.Null);
                EntityManager.SetComponentData(stateEntity, state);
                return;
            }

            if (HasSamePartyTagSet(state, partyIndex, tagId1, tagId2, tagId3))
            {
                PostElectionChirp(LF("Lifecycle.PartyTags.AlreadyHasSet", "{0} already has that party tag set.", state.GetPartyName(partyIndex)), Entity.Null);
                EntityManager.SetComponentData(stateEntity, state);
                return;
            }

            state.SetPartyTagId(partyIndex, 0, tagId1);
            state.SetPartyTagId(partyIndex, 1, tagId2);
            state.SetPartyTagId(partyIndex, 2, tagId3);
            state.SetPartyLastTagReplacementYear(partyIndex, year);

            DebugLog($"Party tags replaced: party={partyIndex}:{state.GetPartyName(partyIndex)}, tags={tagId1},{tagId2},{tagId3}, year={year}.");
            PostElectionChirp(LF("Lifecycle.PartyTags.ReplacedSet", "{0} replaced its party tags.", state.GetPartyName(partyIndex)), Entity.Null);
            EntityManager.SetComponentData(stateEntity, state);
        }

        public void ReplacePartyTag(int partyIndex, int slotIndex)
        {
            if (!ArePartiesEnabled() || slotIndex < 0 || slotIndex >= ElectionPartyTags.TagsPerParty)
                return;

            Entity stateEntity = EnsureStateEntity();
            ElectionState state = EntityManager.GetComponentData<ElectionState>(stateEntity);
            if (!RealisticTripsBridge.TryGetCurrentDateTime(out DateTime now))
                return;

            PrepareStateForCurrentDate(ref state, now);
            if (!IsManagedPartyIndex(state, partyIndex))
                return;

            if (state.stage != ElectionCampaignStage.None)
            {
                PostElectionChirp(L("Lifecycle.PartyTags.ReplaceOutsideCycle", "Party tags can only be replaced outside the election cycle."), Entity.Null);
                EntityManager.SetComponentData(stateEntity, state);
                return;
            }

            ElectionUtility.GetCurrentCalendarDate(World, now, out int year, out _, out _);
            if (state.GetPartyLastTagReplacementYear(partyIndex) == year)
            {
                PostElectionChirp(LF("Lifecycle.PartyTags.AlreadyReplacedTagThisYear", "{0} already replaced a party tag this year.", state.GetPartyName(partyIndex)), Entity.Null);
                EntityManager.SetComponentData(stateEntity, state);
                return;
            }

            Unity.Mathematics.Random random = CreateCampaignRandom(now, 73091 + partyIndex * 1741 + slotIndex * 379);
            int replacementTagId = ElectionPartyTags.PickReplacementTag(state, partyIndex, slotIndex, ref random);
            if (replacementTagId == state.GetPartyTagId(partyIndex, slotIndex))
            {
                PostElectionChirp(LF("Lifecycle.PartyTags.NoBalancedReplacement", "{0} has no balanced replacement available for that tag.", state.GetPartyName(partyIndex)), Entity.Null);
                EntityManager.SetComponentData(stateEntity, state);
                return;
            }

            ElectionPartyTagDefinition oldTag = ElectionPartyTags.Get(state.GetPartyTagId(partyIndex, slotIndex));
            ElectionPartyTagDefinition newTag = ElectionPartyTags.Get(replacementTagId);
            state.SetPartyTagId(partyIndex, slotIndex, replacementTagId);
            state.SetPartyLastTagReplacementYear(partyIndex, year);
            DebugLog($"Party tag replaced: party={partyIndex}:{state.GetPartyName(partyIndex)}, slot={slotIndex}, old={oldTag.Name}, new={newTag.Name}, year={year}.");
            PostElectionChirp(LF("Lifecycle.PartyTags.ReplacedTag", "{0} replaced {1} with {2}.", state.GetPartyName(partyIndex), oldTag.Name, newTag.Name), Entity.Null);
            EntityManager.SetComponentData(stateEntity, state);
        }

        private static bool TryParsePartyTagSelection(string tagIds, out int tagId1, out int tagId2, out int tagId3)
        {
            tagId1 = ElectionPartyTags.None;
            tagId2 = ElectionPartyTags.None;
            tagId3 = ElectionPartyTags.None;

            if (string.IsNullOrWhiteSpace(tagIds))
                return false;

            string[] parts = tagIds.Split(new[] { ',', ';', '|', ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != ElectionPartyTags.TagsPerParty)
                return false;

            if (!int.TryParse(parts[0], out tagId1) ||
                !int.TryParse(parts[1], out tagId2) ||
                !int.TryParse(parts[2], out tagId3))
            {
                tagId1 = ElectionPartyTags.None;
                tagId2 = ElectionPartyTags.None;
                tagId3 = ElectionPartyTags.None;
                return false;
            }

            tagId1 = ElectionPartyTags.NormalizeId(tagId1);
            tagId2 = ElectionPartyTags.NormalizeId(tagId2);
            tagId3 = ElectionPartyTags.NormalizeId(tagId3);
            return true;
        }

        private static bool IsValidPartyTagSelection(int tagId1, int tagId2, int tagId3, out string reason)
        {
            if (!ElectionPartyTags.HasTag(tagId1) ||
                !ElectionPartyTags.HasTag(tagId2) ||
                !ElectionPartyTags.HasTag(tagId3))
            {
                reason = L("Lifecycle.PartyTags.ChooseExactlyThree", "Choose exactly three party tags before saving.");
                return false;
            }

            if (tagId1 == tagId2 || tagId1 == tagId3 || tagId2 == tagId3)
            {
                reason = L("Lifecycle.PartyTags.MustBeUnique", "Party tags must be unique.");
                return false;
            }

            int total = ElectionPartyTags.GetValue(tagId1) + ElectionPartyTags.GetValue(tagId2) + ElectionPartyTags.GetValue(tagId3);
            if (total != 0)
            {
                reason = L("Lifecycle.PartyTags.MustBalance", "Party tag values must add up to zero.");
                return false;
            }

            reason = string.Empty;
            return true;
        }

        private static bool HasSamePartyTagSet(ElectionState state, int partyIndex, int tagId1, int tagId2, int tagId3)
        {
            int matchedTags = 0;
            for (int slot = 0; slot < ElectionPartyTags.TagsPerParty; slot++)
            {
                int currentTagId = state.GetPartyTagId(partyIndex, slot);
                if (currentTagId == tagId1 || currentTagId == tagId2 || currentTagId == tagId3)
                    matchedTags++;
            }

            return matchedTags == ElectionPartyTags.TagsPerParty;
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
                PostElectionChirp(LF("Lifecycle.MinimumPopulation.StartCampaign", "The Election Board will not start a campaign until the city reaches {0:n0} population.", MinimumPopulation), Entity.Null);
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
                PostElectionChirp(LF("Lifecycle.MinimumPopulation.StartVoting", "The Election Board will not start voting until the city reaches {0:n0} population.", MinimumPopulation), Entity.Null);
                return;
            }

            if (!state.HasCandidates)
                StartCampaign(ref state, now, accelerated: true, reason: "debug setting force election needed candidates");

            StartElection(ref state, now);
            EntityManager.SetComponentData(stateEntity, state);
        }

        public void ForceGeneratePollFromSettings()
        {
            Entity stateEntity = EnsureStateEntity();
            ElectionState state = EntityManager.GetComponentData<ElectionState>(stateEntity);
            if (!RealisticTripsBridge.TryGetCurrentDateTime(out DateTime now))
                return;

            DebugLog("ForceGeneratePoll setting button pressed.");
            if (!HasMinimumPopulation("force generate poll"))
            {
                PostElectionChirp(LF("Lifecycle.MinimumPopulation.GeneratePoll", "The Election Board will not generate a poll until the city reaches {0:n0} population.", MinimumPopulation), Entity.Null);
                return;
            }

            PrepareStateForCurrentDate(ref state, now);
            if (state.stage == ElectionCampaignStage.Voting || IsElectionDay(state, now))
            {
                PostElectionChirp(L("Lifecycle.Poll.DebugUnavailableElectionDay", "Debug poll generation is unavailable on election day."), Entity.Null);
                EntityManager.SetComponentData(stateEntity, state);
                return;
            }

            if (!state.HasCandidates || !IsActiveCampaignStage(state.stage))
                StartCampaign(ref state, now, accelerated: ElectionUtility.CurrentCalendarMonth(World, now) <= 7, reason: "debug setting force generate poll");

            if (state.HasCandidates && state.stage != ElectionCampaignStage.Voting)
                ReleasePoll(ref state, now);

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
            bool hasDateTime = RealisticTripsBridge.TryGetCurrentDateTime(out DateTime now);
            if (hasDateTime)
                PrepareStateForCurrentDate(ref state, now);
            else
            {
                DebugLog($"Donation rejected: current date is unavailable. candidateIndex={candidateIndex}, tierIndex={tierIndex}, state={DescribeState(state)}");
                PostElectionChirp(L("Lifecycle.Donation.NoDate", "Campaign donations need the current city date and are unavailable right now."), Entity.Null);
                EntityManager.SetComponentData(stateEntity, state);
                return;
            }

            if (!IsActiveCampaignStage(state.stage) || !state.HasCandidates)
            {
                DebugLog($"Donation rejected: no active race with candidates. candidateIndex={candidateIndex}, tierIndex={tierIndex}, state={DescribeState(state)}");
                PostElectionChirp(L("Lifecycle.Donation.NoRace", "Campaign donations are available while an active mayoral race has selected candidates."), Entity.Null);
                EntityManager.SetComponentData(stateEntity, state);
                return;
            }

            if (state.stage == ElectionCampaignStage.Voting ||
                (hasDateTime && IsElectionDay(state, now)))
            {
                DebugLog($"Donation rejected: donations are closed on election day. candidateIndex={candidateIndex}, tierIndex={tierIndex}, state={DescribeState(state)}");
                PostElectionChirp(L("Lifecycle.Donation.ElectionDay", "Campaign donations are closed on election day."), Entity.Null);
                EntityManager.SetComponentData(stateEntity, state);
                return;
            }

            if (!IsDonationOpenStage(state.stage))
            {
                DebugLog($"Donation rejected: donations are not open in stage {state.stage}. candidateIndex={candidateIndex}, tierIndex={tierIndex}, state={DescribeState(state)}");
                PostElectionChirp(L("Lifecycle.Donation.Closed", "Campaign donations are available before election day while an active mayoral race has selected candidates."), Entity.Null);
                EntityManager.SetComponentData(stateEntity, state);
                return;
            }

            int dayKey = ElectionUtility.CurrentCalendarDayKey(World, now);
            if (state.donationDayKey == dayKey)
            {
                DebugLog($"Donation rejected: already donated today. candidateIndex={candidateIndex}, tierIndex={tierIndex}, dayKey={dayKey}.");
                PostElectionChirp(L("Lifecycle.Donation.UsedToday", "Only one campaign donation can be made per day."), Entity.Null);
                EntityManager.SetComponentData(stateEntity, state);
                return;
            }

            if (!state.IsActiveCandidateIndex(candidateIndex))
            {
                DebugLog($"Donation rejected: invalid candidateIndex={candidateIndex}.");
                return;
            }

            int campaignDonationAmount = GetCampaignDonationAmount(state);
            if (!ElectionDonationTiers.TryGet(tierIndex, campaignDonationAmount, out ElectionDonationTier tier))
            {
                DebugLog($"Donation rejected: invalid tierIndex={tierIndex}.");
                return;
            }

            Entity candidate = state.GetCandidate(candidateIndex);
            string name = GetCandidateChirpName(candidate);

            int tagId = GetCandidateTagId(state, candidateIndex);
            int amount = ElectionCandidateTags.GetDonationCost(tagId, tier.Amount);
            int effectiveAmount = ElectionCandidateTags.GetDonationCredit(tagId, tier.Amount);
            if (ArePartiesEnabled())
                effectiveAmount = ElectionPartyTags.ApplyDonationCredit(state, state.GetCandidatePartyIndex(candidateIndex), effectiveAmount);
            if (!TrySpendCityMoney(amount))
            {
                DebugLog($"Donation rejected: city could not spend {amount:n0} for {name}.");
                PostElectionChirp(LF("Lifecycle.Donation.InsufficientFunds", "The city does not have enough money to donate {0:n0}.", amount), Entity.Null);
                return;
            }

            state.AddCandidateDonation(candidateIndex, effectiveAmount);
            state.donationDayKey = dayKey;

            int totalDonation = state.GetCandidateDonation(candidateIndex);
            int portraitIndex = state.GetCandidatePortraitIndex(candidateIndex);
            PlatformSofteningResult softening = TrySoftenCandidatePlatform(ref state, candidateIndex, totalDonation);
            ScheduleCandidateDonationThankYouChirp(candidateIndex, candidate, name, portraitIndex, effectiveAmount, totalDonation, softening);
            DebugLog($"Donation accepted: candidateIndex={candidateIndex}, candidate={DescribeEntity(candidate, name)}, cost={amount:n0}, effectiveAmount={effectiveAmount:n0}, tag={ElectionCandidateTags.Get(tagId).Name}, totalDonation={totalDonation:n0}, thank-you chirp due in 1 real-world minute.");
            EntityManager.SetComponentData(stateEntity, state);
        }

        public void RunSupportProgram(int programIndex)
        {
            Entity stateEntity = EnsureStateEntity();
            ElectionState state = EntityManager.GetComponentData<ElectionState>(stateEntity);
            if (!RealisticTripsBridge.TryGetCurrentDateTime(out DateTime now))
                return;

            PrepareStateForCurrentDate(ref state, now);

            if (!ElectionSupportPrograms.TryGet(programIndex, out ElectionSupportProgramDefinition program))
            {
                DebugLog($"Support program rejected: invalid programIndex={programIndex}.");
                EntityManager.SetComponentData(stateEntity, state);
                return;
            }

            if (!IsDonationOpenStage(state.stage) || !state.HasCandidates || IsElectionDay(state, now))
            {
                DebugLog($"Support program rejected: program={program.Title}, state={DescribeState(state)}.");
                PostElectionChirp(L("Lifecycle.Support.Closed", "Civic programs are available before election day while an active mayoral race has selected candidates."), Entity.Null);
                EntityManager.SetComponentData(stateEntity, state);
                return;
            }

            int dayKey = ElectionUtility.CurrentCalendarDayKey(World, now);
            if (state.supportProgramDayKey == dayKey)
            {
                string usedLabel = GetSupportProgramLabel(state.supportProgramIdToday);
                DebugLog($"Support program rejected: already funded today. requested={program.Title}, usedToday={usedLabel}, dayKey={dayKey}.");
                PostElectionChirp(LF("Lifecycle.Support.UsedToday", "Only one civic program can be funded per day. Today's program is {0}.", usedLabel), Entity.Null);
                EntityManager.SetComponentData(stateEntity, state);
                return;
            }

            if (program.Type == ElectionSupportProgramType.ElectionDayHoliday &&
                state.electionDayHolidayScheduled)
            {
                DebugLog("Support program rejected: election day holiday is already scheduled.");
                PostElectionChirp(L("Lifecycle.Support.HolidayScheduled", "Election day is already scheduled as a holiday."), Entity.Null);
                EntityManager.SetComponentData(stateEntity, state);
                return;
            }

            int amount = ElectionSupportPrograms.GetCost(state.campaignDonationAmount);
            if (!TrySpendCityMoney(amount))
            {
                DebugLog($"Support program rejected: city could not spend {amount:n0} for {program.Title}.");
                PostElectionChirp(LF("Lifecycle.Support.InsufficientFunds", "The city does not have enough money to fund {0} for {1:n0}.", program.Title, amount), Entity.Null);
                EntityManager.SetComponentData(stateEntity, state);
                return;
            }

            ApplySupportProgram(ref state, program.Type);
            state.supportProgramDayKey = dayKey;
            state.supportProgramIdToday = program.Index;
            SyncElectionDaySundayOverride(state, now);

            string outcome = GetSupportProgramOutcome(state, program.Type);
            DebugLog($"Support program funded: program={program.Title}, cost={amount:n0}, dayKey={dayKey}, outcome={outcome}.");
            PostElectionChirp(LF("Lifecycle.Support.Funded", "{0} funded for {1:n0}. {2}", program.Title, amount, outcome), Entity.Null);
            EntityManager.SetComponentData(stateEntity, state);
        }

        public void BribeMayor(int candidateIndex)
        {
            Entity stateEntity = EnsureStateEntity();
            ElectionState state = EntityManager.GetComponentData<ElectionState>(stateEntity);
            if (!RealisticTripsBridge.TryGetCurrentDateTime(out DateTime now))
                return;

            PrepareStateForCurrentDate(ref state, now);

            ProcessPendingBribeMeeting(ref state, now);

            if (!IsDonationOpenStage(state.stage) || !state.HasCandidates || IsElectionDay(state, now))
            {
                DebugLog($"Bribe rejected: no active race with candidates. candidateIndex={candidateIndex}, state={DescribeState(state)}");
                PostElectionChirp(L("Lifecycle.Bribe.Closed", "Mayoral platform meetings are only available before election day while an active race has selected candidates."), Entity.Null);
                EntityManager.SetComponentData(stateEntity, state);
                return;
            }

            if (!state.IsActiveCandidateIndex(candidateIndex))
            {
                DebugLog($"Bribe rejected: invalid candidateIndex={candidateIndex}.");
                EntityManager.SetComponentData(stateEntity, state);
                return;
            }

            if (HasPendingBribeMeeting(state) || state.bribeBlockedUntilTicks > now.Ticks)
            {
                DebugLog($"Bribe rejected: mayor schedule is blocked until {new DateTime(state.bribeBlockedUntilTicks):O}.");
                PostElectionChirp(L("Lifecycle.Bribe.ScheduleBlocked", "The mayor's schedule is already reserved for a candidate platform meeting attempt."), state.mayor);
                EntityManager.SetComponentData(stateEntity, state);
                return;
            }

            Entity candidate = state.GetCandidate(candidateIndex);
            string candidateName = GetCandidateChirpName(candidate);
            string mayorName = GetEntityName(state.mayor, L("Lifecycle.Name.Mayor", "the mayor"));
            int bribeAmount = GetCampaignBribeAmount(state);

            if (!TrySpendCityMoney(bribeAmount))
            {
                DebugLog($"Bribe rejected: city could not spend {bribeAmount:n0} for target={candidateName}.");
                PostElectionChirp(LF("Lifecycle.Bribe.InsufficientFunds", "The city does not have enough money to fund a {0:n0} mayoral outreach effort.", bribeAmount), Entity.Null);
                EntityManager.SetComponentData(stateEntity, state);
                return;
            }

            RegisterCandidateCorruptionRisk(ref state, candidateIndex);
            RegisterMayorBribe(ref state, bribeAmount);
            BeginBribeMeetingAttempt(ref state, now, candidateIndex, candidate, mayorName, candidateName);
            if (!TryScheduleBribeMeeting(ref state, now))
                state.bribeMeetingNextAttemptTicks = now.AddMinutes(kBribeMeetingRetryMinutes).Ticks;

            EntityManager.SetComponentData(stateEntity, state);
        }

        public void EndorseCandidate(int candidateIndex)
        {
            Entity stateEntity = EnsureStateEntity();
            ElectionState state = EntityManager.GetComponentData<ElectionState>(stateEntity);
            if (!RealisticTripsBridge.TryGetCurrentDateTime(out DateTime now))
                return;

            PrepareStateForCurrentDate(ref state, now);
            ProcessPendingBribeMeeting(ref state, now);

            if (!IsDonationOpenStage(state.stage) || !state.HasCandidates || IsElectionDay(state, now))
            {
                DebugLog($"Endorsement rejected: no active race with candidates. candidateIndex={candidateIndex}, state={DescribeState(state)}");
                PostElectionChirp(L("Lifecycle.Endorse.Closed", "Mayoral endorsements are only available before election day while an active race has selected candidates."), Entity.Null);
                EntityManager.SetComponentData(stateEntity, state);
                return;
            }

            if (!state.IsActiveCandidateIndex(candidateIndex))
            {
                DebugLog($"Endorsement rejected: invalid candidateIndex={candidateIndex}.");
                EntityManager.SetComponentData(stateEntity, state);
                return;
            }

            if (state.IsActiveCandidateIndex(state.mayorEndorsementCandidateIndex))
            {
                DebugLog($"Endorsement rejected: mayor already endorsed candidateIndex={state.mayorEndorsementCandidateIndex}, candidate={FormatEntity(state.mayorEndorsementCandidate)}.");
                PostElectionChirp(L("Lifecycle.Endorse.AlreadyUsed", "The mayor has already endorsed a candidate in this election cycle."), state.mayor);
                EntityManager.SetComponentData(stateEntity, state);
                return;
            }

            if (HasPendingBribeMeeting(state) || state.bribeBlockedUntilTicks > now.Ticks)
            {
                DebugLog($"Endorsement rejected: mayor schedule is blocked until {new DateTime(state.bribeBlockedUntilTicks):O}.");
                PostElectionChirp(L("Lifecycle.MayorAction.ScheduleBlocked", "The mayor's schedule is already reserved for a campaign action today."), state.mayor);
                EntityManager.SetComponentData(stateEntity, state);
                return;
            }

            Entity candidate = state.GetCandidate(candidateIndex);
            string candidateName = GetCandidateChirpName(candidate);
            string mayorName = GetEntityName(state.mayor, L("Lifecycle.Name.Mayor", "the mayor"));
            int bribeAmount = GetCampaignBribeAmount(state);

            if (!TrySpendCityMoney(bribeAmount))
            {
                DebugLog($"Endorsement rejected: city could not spend {bribeAmount:n0} for target={candidateName}.");
                PostElectionChirp(LF("Lifecycle.Endorse.InsufficientFunds", "The city does not have enough money to fund a {0:n0} mayoral endorsement effort.", bribeAmount), Entity.Null);
                EntityManager.SetComponentData(stateEntity, state);
                return;
            }

            RegisterCandidateCorruptionRisk(ref state, candidateIndex);
            RegisterMayorBribe(ref state, bribeAmount);
            state.bribeDayKey = ElectionUtility.CurrentCalendarDayKey(World, now);
            state.bribeBlockedUntilTicks = GetRestOfDayBlockTicks(now);
            ResetBribeMeetingState(ref state);
            state.mayorEndorsementCandidateIndex = candidateIndex;
            state.mayorEndorsementCandidate = candidate;
            state.mayorEndorsementChirpUtcTicks = DateTime.UtcNow.AddMinutes(1).Ticks;
            state.mayorEndorsementChirpSent = false;

            DebugLog($"Mayor endorsement scheduled: mayor={DescribeEntity(state.mayor, mayorName)}, candidateIndex={candidateIndex}, candidate={DescribeEntity(candidate, candidateName)}, cost={bribeAmount:n0}, chirpDue={new DateTime(state.mayorEndorsementChirpUtcTicks):O} UTC, blockedUntil={new DateTime(state.bribeBlockedUntilTicks):O}.");
            EntityManager.SetComponentData(stateEntity, state);
        }

        public void CashAssistance()
        {
            Entity stateEntity = EnsureStateEntity();
            ElectionState state = EntityManager.GetComponentData<ElectionState>(stateEntity);
            if (!RealisticTripsBridge.TryGetCurrentDateTime(out DateTime now))
                return;

            PrepareStateForCurrentDate(ref state, now);
            ProcessPendingBribeMeeting(ref state, now);

            if (!IsDonationOpenStage(state.stage) || !state.HasCandidates || IsElectionDay(state, now))
            {
                DebugLog($"Cash Assistance rejected: no active race with candidates. state={DescribeState(state)}");
                PostElectionChirp(L("Lifecycle.CashAssistance.Closed", "Cash Assistance is only available before election day while an active race has selected candidates."), Entity.Null);
                EntityManager.SetComponentData(stateEntity, state);
                return;
            }

            if (state.cashAssistanceTurnoutBonusPercent > 0)
            {
                DebugLog("Cash Assistance rejected: already funded this election cycle.");
                PostElectionChirp(L("Lifecycle.CashAssistance.AlreadyFunded", "Cash Assistance has already been funded this election cycle."), Entity.Null);
                EntityManager.SetComponentData(stateEntity, state);
                return;
            }

            if (HasPendingBribeMeeting(state) || state.bribeBlockedUntilTicks > now.Ticks)
            {
                DebugLog($"Cash Assistance rejected: mayor schedule is blocked until {new DateTime(state.bribeBlockedUntilTicks):O}.");
                PostElectionChirp(L("Lifecycle.MayorAction.ScheduleBlocked", "The mayor's schedule is already reserved for a campaign action today."), state.mayor);
                EntityManager.SetComponentData(stateEntity, state);
                return;
            }

            int bribeAmount = GetCampaignBribeAmount(state);
            if (!TrySpendCityMoney(bribeAmount))
            {
                DebugLog($"Cash Assistance rejected: city could not spend {bribeAmount:n0}.");
                PostElectionChirp(LF("Lifecycle.CashAssistance.InsufficientFunds", "The city does not have enough money to fund a {0:n0} Cash Assistance operation.", bribeAmount), Entity.Null);
                EntityManager.SetComponentData(stateEntity, state);
                return;
            }

            RegisterMayorBribe(ref state, bribeAmount);
            state.bribeDayKey = ElectionUtility.CurrentCalendarDayKey(World, now);
            state.bribeBlockedUntilTicks = GetRestOfDayBlockTicks(now);
            ResetBribeMeetingState(ref state);
            state.cashAssistanceTurnoutBonusPercent = math.clamp(CashAssistanceTurnoutBonusPercent, 0, 100);

            DebugLog($"Cash Assistance funded: turnoutBonus={CashAssistanceTurnoutBonusPercent}%, cost={bribeAmount:n0}, blockedUntil={new DateTime(state.bribeBlockedUntilTicks):O}.");
            PostElectionChirp(LF("Lifecycle.CashAssistance.Funded", "Cash Assistance funded. Struggling and modest-income residents get +{0}% election turnout.", CashAssistanceTurnoutBonusPercent), Entity.Null);
            EntityManager.SetComponentData(stateEntity, state);
        }

        public void ScheduleVoteTampering(int beneficiaryCandidateIndex)
        {
            Entity stateEntity = EnsureStateEntity();
            ElectionState state = EntityManager.GetComponentData<ElectionState>(stateEntity);
            if (!RealisticTripsBridge.TryGetCurrentDateTime(out DateTime now))
                return;

            PrepareStateForCurrentDate(ref state, now);
            ProcessPendingBribeMeeting(ref state, now);

            if (!IsDonationOpenStage(state.stage) || !state.HasCandidates || IsElectionDay(state, now))
            {
                DebugLog($"Vote tampering rejected: no active race with candidates. beneficiaryCandidateIndex={beneficiaryCandidateIndex}, state={DescribeState(state)}");
                PostElectionChirp(L("Lifecycle.Tamper.Closed", "Vote-count tampering can only be arranged before election day while an active race has selected candidates."), Entity.Null);
                EntityManager.SetComponentData(stateEntity, state);
                return;
            }

            if (!state.IsActiveCandidateIndex(beneficiaryCandidateIndex))
            {
                DebugLog($"Vote tampering rejected: invalid beneficiaryCandidateIndex={beneficiaryCandidateIndex}.");
                EntityManager.SetComponentData(stateEntity, state);
                return;
            }

            if (state.IsActiveCandidateIndex(state.voteTamperingCandidateIndex))
            {
                DebugLog($"Vote tampering rejected: an operation is already planned for candidateIndex={state.voteTamperingCandidateIndex}, candidate={FormatEntity(state.voteTamperingCandidate)}.");
                PostElectionChirp(L("Lifecycle.Tamper.AlreadyPlanned", "A vote-count tampering operation is already planned for this election cycle."), Entity.Null);
                EntityManager.SetComponentData(stateEntity, state);
                return;
            }

            if (HasPendingBribeMeeting(state) || state.bribeBlockedUntilTicks > now.Ticks)
            {
                DebugLog($"Vote tampering rejected: mayor schedule is blocked until {new DateTime(state.bribeBlockedUntilTicks):O}.");
                PostElectionChirp(L("Lifecycle.MayorAction.ScheduleBlocked", "The mayor's schedule is already reserved for a campaign action today."), state.mayor);
                EntityManager.SetComponentData(stateEntity, state);
                return;
            }

            Entity beneficiary = state.GetCandidate(beneficiaryCandidateIndex);
            string beneficiaryName = GetCandidateChirpName(beneficiary);
            int bribeAmount = GetCampaignBribeAmount(state);

            if (!TrySpendCityMoney(bribeAmount))
            {
                DebugLog($"Vote tampering rejected: city could not spend {bribeAmount:n0} for beneficiary={beneficiaryName}.");
                PostElectionChirp(LF("Lifecycle.Tamper.InsufficientFunds", "The city does not have enough money to fund a {0:n0} vote-count operation.", bribeAmount), Entity.Null);
                EntityManager.SetComponentData(stateEntity, state);
                return;
            }

            RegisterCandidateCorruptionRisk(ref state, beneficiaryCandidateIndex);
            RegisterMayorBribe(ref state, bribeAmount);
            state.bribeDayKey = ElectionUtility.CurrentCalendarDayKey(World, now);
            state.bribeBlockedUntilTicks = GetRestOfDayBlockTicks(now);
            ResetBribeMeetingState(ref state);
            state.voteTamperingCandidateIndex = beneficiaryCandidateIndex;
            state.voteTamperingCandidate = beneficiary;
            state.voteTamperingScheduledMinute = 0;
            state.voteTamperingFireStarted = false;
            state.voteTamperingResolved = false;
            state.voteTamperingPollingPlace = Entity.Null;
            state.voteTamperingLostVotesA = 0;
            state.voteTamperingLostVotesB = 0;
            state.voteTamperingProtestChirpUtcTicks = 0;
            state.voteTamperingProtestChirpSent = false;

            DebugLog($"Vote tampering operation scheduled: beneficiaryIndex={beneficiaryCandidateIndex}, beneficiary={DescribeEntity(beneficiary, beneficiaryName)}, cost={bribeAmount:n0}, blockedUntil={new DateTime(state.bribeBlockedUntilTicks):O}, corruptionRiskSteps={GetCandidateCorruptionRiskSteps(state, beneficiaryCandidateIndex)}.");
            EntityManager.SetComponentData(stateEntity, state);
        }

        public void ProposeStrictVotingIdLaw()
        {
            SetElectionLegislation((int)ElectionLegislationType.VoterIdentification, true);
        }

        public void RepealStrictVotingIdLaw()
        {
            SetElectionLegislation((int)ElectionLegislationType.VoterIdentification, false);
        }

        public void SetElectionLegislation(int legislationIndex, bool active)
        {
            Entity stateEntity = EnsureStateEntity();
            ElectionState state = EntityManager.GetComponentData<ElectionState>(stateEntity);
            if (!RealisticTripsBridge.TryGetCurrentDateTime(out DateTime now))
                return;

            PrepareStateForCurrentDate(ref state, now);
            ProcessScheduledStrictVotingIdChirp(ref state);

            if (!ElectionLegislation.TryGet(legislationIndex, out ElectionLegislationDefinition legislation))
            {
                DebugLog($"Election legislation rejected: invalid legislationIndex={legislationIndex}.");
                EntityManager.SetComponentData(stateEntity, state);
                return;
            }

            if (!IsDonationOpenStage(state.stage) || !state.HasCandidates || IsElectionDay(state, now))
            {
                DebugLog($"Election legislation rejected: no active race with candidates. legislation={legislation.Title}, active={active}, state={DescribeState(state)}");
                PostElectionChirp(L("Lifecycle.Legislation.Closed", "Election legislation can only be changed before election day while an active race has selected candidates."), Entity.Null);
                EntityManager.SetComponentData(stateEntity, state);
                return;
            }

            int dayKey = ElectionUtility.CurrentCalendarDayKey(World, now);
            if (state.legislationActionDayKey == dayKey)
            {
                DebugLog($"Election legislation rejected: daily legislation action already used. legislation={legislation.Title}, active={active}, dayKey={dayKey}.");
                PostElectionChirp(L("Lifecycle.Legislation.UsedToday", "Only one election legislation action can be attempted per day."), Entity.Null);
                EntityManager.SetComponentData(stateEntity, state);
                return;
            }

            if (state.HasLegislation(legislation.Type) == active)
            {
                DebugLog($"Election legislation rejected: legislation already {(active ? "active" : "inactive")}. legislation={legislation.Title}.");
                PostElectionChirp(active
                    ? LF("Lifecycle.Legislation.AlreadyActive", "{0} is already in effect.", legislation.Title)
                    : LF("Lifecycle.Legislation.AlreadyRepealed", "{0} is already repealed.", legislation.Title), Entity.Null);
                EntityManager.SetComponentData(stateEntity, state);
                return;
            }

            int cost = ElectionLegislation.GetCost(GetCampaignBribeAmount(state));
            if (!TrySpendCityMoney(cost))
            {
                DebugLog($"Election legislation rejected: city could not spend {cost:n0}. legislation={legislation.Title}, active={active}.");
                string action = active
                    ? L("Lifecycle.Legislation.Action.Proposal", "proposal")
                    : L("Lifecycle.Legislation.Action.Repeal", "repeal");
                PostElectionChirp(LF("Lifecycle.Legislation.InsufficientFunds", "The city does not have enough money to fund a {0:n0} legislation {1}.", cost, action), Entity.Null);
                EntityManager.SetComponentData(stateEntity, state);
                return;
            }

            state.legislationActionDayKey = dayKey;
            int chancePercent = ElectionLegislation.GetActionChancePercent(state);
            Unity.Mathematics.Random random = CreateCampaignRandom(now, 91453 + legislationIndex * 9176 + (active ? 11 : 23));
            bool succeeded = random.NextInt(100) < chancePercent;

            if (succeeded)
            {
                state.SetLegislation(legislation.Type, active);
                ResetStrictVotingIdProposalState(ref state);
            }

            string actionLabel = active ? "pass" : "repeal";
            string outcomeText = succeeded
                ? active
                    ? LF("Lifecycle.Legislation.Outcome.Passed", "{0} passed after lobbying.", legislation.Title)
                    : LF("Lifecycle.Legislation.Outcome.Repealed", "{0} was repealed after lobbying.", legislation.Title)
                : active
                    ? LF("Lifecycle.Legislation.Outcome.FailedPass", "{0} failed to pass after lobbying.", legislation.Title)
                    : LF("Lifecycle.Legislation.Outcome.FailedRepeal", "The repeal of {0} failed after lobbying.", legislation.Title);
            DebugLog($"Election legislation action resolved: legislation={legislation.Title}, action={actionLabel}, succeeded={succeeded}, chance={chancePercent}%, cost={cost:n0}, flags={state.legislationFlags}, dayKey={dayKey}.");
            PostElectionChirp(LF("Lifecycle.Legislation.Outcome.WithChance", "{0} Success chance was {1}% based on incumbent party reputation.", outcomeText, chancePercent), Entity.Null);
            EntityManager.SetComponentData(stateEntity, state);
        }

        private static long GetRestOfDayBlockTicks(DateTime now)
        {
            return now.Date.AddDays(1).Ticks;
        }

        private static void RegisterCandidateCorruptionRisk(ref ElectionState state, int candidateIndex)
        {
            if (!state.IsActiveCandidateIndex(candidateIndex))
                return;

            int increment = 1;
            if (ArePartiesEnabled())
                increment += ElectionPartyTags.GetCorruptionRiskStepBonus(state, state.GetCandidatePartyIndex(candidateIndex));

            state.SetCandidateCorruptionRiskSteps(
                candidateIndex,
                math.min(kCorruptionRiskMaxSteps, state.GetCandidateCorruptionRiskSteps(candidateIndex) + increment));
        }

        private static int GetCandidateCorruptionRiskSteps(ElectionState state, int candidateIndex)
        {
            return state.GetCandidateCorruptionRiskSteps(candidateIndex);
        }

        private static void RegisterMayorBribe(ref ElectionState state, int amount)
        {
            if (amount <= 0 || state.mayor == Entity.Null)
                return;

            if (state.mayorBribeRecipient != state.mayor)
            {
                state.mayorBribeRecipient = state.mayor;
                state.mayorBribeTotal = 0;
            }

            state.mayorBribeTotal = ClampMoneyAmount((long)state.mayorBribeTotal + amount);
        }

        private void BeginBribeMeetingAttempt(ref ElectionState state, DateTime now, int candidateIndex, Entity candidate, string mayorName, string candidateName)
        {
            long blockedUntilTicks = now.AddHours(kBribeMeetingWindowHours).Ticks;
            state.bribeDayKey = ElectionUtility.CurrentCalendarDayKey(World, now);
            state.bribeBlockedUntilTicks = blockedUntilTicks;
            state.bribeMeetingCandidateIndex = candidateIndex;
            state.bribeMeetingCandidate = candidate;
            state.bribeMeetingVenue = Entity.Null;
            state.bribeMeetingDeadlineTicks = blockedUntilTicks;
            state.bribeMeetingNextAttemptTicks = now.Ticks;
            state.bribeMeetingTripsRequested = false;
            DebugLog($"Bribe meeting attempt started: mayor={DescribeEntity(state.mayor, mayorName)}, target={DescribeEntity(candidate, candidateName)}, deadline={new DateTime(blockedUntilTicks):O}, amount={GetCampaignBribeAmount(state):n0}.");
        }

        private void ProcessPendingBribeMeeting(ref ElectionState state, DateTime now)
        {
            if (!HasPendingBribeMeeting(state))
                return;

            if (state.bribeBlockedUntilTicks <= 0 || state.bribeBlockedUntilTicks < state.bribeMeetingDeadlineTicks)
                state.bribeBlockedUntilTicks = state.bribeMeetingDeadlineTicks;

            int candidateIndex = state.bribeMeetingCandidateIndex;
            Entity candidate = state.IsActiveCandidateIndex(candidateIndex) ? state.GetCandidate(candidateIndex) : Entity.Null;
            string candidateName = GetEntityName(candidate, L("Lifecycle.Name.Candidate", "the candidate"));
            string mayorName = GetEntityName(state.mayor, L("Lifecycle.Name.Mayor", "the mayor"));

            if (!IsDonationOpenStage(state.stage) ||
                IsElectionDay(state, now) ||
                !state.HasCandidates ||
                !IsValidChirpCitizen(state.mayor) ||
                !IsValidChirpCitizen(candidate) ||
                (state.bribeMeetingCandidate != Entity.Null && state.bribeMeetingCandidate != candidate))
            {
                DebugLog($"Bribe meeting canceled: campaign state changed before meeting. state={DescribeState(state)}, targetIndex={candidateIndex}.");
                PostBribeMeetingUnableChirp(state, candidate, mayorName, candidateName);
                ResetBribeMeetingState(ref state);
                return;
            }

            if (!state.bribeMeetingTripsRequested && now.Ticks >= state.bribeMeetingDeadlineTicks)
            {
                DebugLog($"Bribe meeting failed to schedule before deadline: mayor={DescribeEntity(state.mayor, mayorName)}, target={DescribeEntity(candidate, candidateName)}, deadline={new DateTime(state.bribeMeetingDeadlineTicks):O}.");
                PostBribeMeetingUnableChirp(state, candidate, mayorName, candidateName);
                ResetBribeMeetingState(ref state);
                return;
            }

            if (state.bribeMeetingTripsRequested)
            {
                if (!IsValidVenue(state.bribeMeetingVenue))
                {
                    DebugLog($"Bribe meeting venue became invalid before arrival; retrying scheduling. venue={FormatEntity(state.bribeMeetingVenue)}.");
                    state.bribeMeetingVenue = Entity.Null;
                    state.bribeMeetingTripsRequested = false;
                    state.bribeMeetingNextAttemptTicks = now.Ticks;
                    return;
                }

                if (IsAtBuilding(state.mayor, state.bribeMeetingVenue) &&
                    IsAtBuilding(candidate, state.bribeMeetingVenue))
                {
                    ResolveBribeMeeting(ref state, now, candidateIndex, candidate, mayorName, candidateName);
                }

                return;
            }

            if (state.bribeMeetingNextAttemptTicks > now.Ticks)
                return;

            if (!TryScheduleBribeMeeting(ref state, now))
            {
                state.bribeMeetingNextAttemptTicks = now.AddMinutes(kBribeMeetingRetryMinutes).Ticks;
                DebugLog($"Bribe meeting schedule attempt failed; next attempt at {new DateTime(state.bribeMeetingNextAttemptTicks):O}. mayor={DescribeEntity(state.mayor, mayorName)}, target={DescribeEntity(candidate, candidateName)}.");
            }
        }

        private static bool HasPendingBribeMeeting(ElectionState state)
        {
            return state.IsActiveCandidateIndex(state.bribeMeetingCandidateIndex) &&
                   state.bribeMeetingDeadlineTicks > 0;
        }

        private bool TryScheduleBribeMeeting(ref ElectionState state, DateTime now)
        {
            if (!RealisticTripsBridge.IsAvailable)
                return false;

            int candidateIndex = state.bribeMeetingCandidateIndex;
            Entity candidate = state.IsActiveCandidateIndex(candidateIndex) ? state.GetCandidate(candidateIndex) : Entity.Null;
            Entity mayor = state.mayor;
            if (!IsValidChirpCitizen(mayor) || !IsValidChirpCitizen(candidate))
                return false;

            if (!RealisticTripsBridge.IsCitizenOutsideWorkHours(mayor) ||
                !RealisticTripsBridge.IsCitizenOutsideWorkHours(candidate))
            {
                return false;
            }

            if (!TryFindBribeMeetingVenue(mayor, candidate, out Entity venue))
                return false;

            bool mayorAccepted = RealisticTripsBridge.RequestBribeMeetingTrip(
                mayor,
                venue,
                candidate,
                kBribeMeetingDurationMinutes,
                kBribeMeetingPriority);
            if (!mayorAccepted)
                return false;

            bool candidateAccepted = RealisticTripsBridge.RequestBribeMeetingTrip(
                candidate,
                venue,
                mayor,
                kBribeMeetingDurationMinutes,
                kBribeMeetingPriority);
            if (!candidateAccepted)
                return false;

            state.bribeMeetingVenue = venue;
            state.bribeMeetingTripsRequested = true;
            state.bribeMeetingNextAttemptTicks = 0;
            PostBribeMeetingTravelChirp(state, candidateIndex, candidate, venue);
            DebugLog($"Bribe meeting trips scheduled: mayor={FormatEntity(mayor)}, candidate={FormatEntity(candidate)}, venue={FormatEntity(venue)}, date={ElectionUtility.FormatCurrentDate(World, now)} {now:HH:mm}.");
            return true;
        }

        private bool TryFindBribeMeetingVenue(Entity mayor, Entity candidate, out Entity venue)
        {
            venue = Entity.Null;
            if (m_BribeLeisureVenueQuery.IsEmptyIgnoreFilter)
                return false;

            bool hasMayorPosition = TryGetCitizenPosition(mayor, out float3 mayorPosition);
            bool hasCandidatePosition = TryGetCitizenPosition(candidate, out float3 candidatePosition);
            int bestScore = int.MinValue;
            float bestDistance = float.MaxValue;

            using (NativeArray<Entity> entities = m_BribeLeisureVenueQuery.ToEntityArray(Allocator.Temp))
            {
                for (int i = 0; i < entities.Length; i++)
                {
                    Entity entity = entities[i];
                    if (!IsValidVenue(entity) ||
                        !RealisticTripsBridge.CanRequestTrip(mayor, entity) ||
                        !RealisticTripsBridge.CanRequestTrip(candidate, entity))
                    {
                        continue;
                    }

                    int score = GetVenueScore(entity);
                    float distance = float.MaxValue;
                    if (TryGetEntityPosition(entity, out float3 venuePosition))
                    {
                        distance = 0f;
                        if (hasMayorPosition)
                            distance += math.distancesq(mayorPosition, venuePosition);
                        if (hasCandidatePosition)
                            distance += math.distancesq(candidatePosition, venuePosition);
                    }

                    if (venue == Entity.Null ||
                        score > bestScore ||
                        (score == bestScore && distance < bestDistance))
                    {
                        venue = entity;
                        bestScore = score;
                        bestDistance = distance;
                    }
                }
            }

            return venue != Entity.Null;
        }

        private bool TryGetCitizenPosition(Entity citizen, out float3 position)
        {
            if (citizen != Entity.Null &&
                EntityManager.Exists(citizen) &&
                EntityManager.HasComponent<CurrentBuilding>(citizen))
            {
                Entity building = EntityManager.GetComponentData<CurrentBuilding>(citizen).m_CurrentBuilding;
                if (TryGetEntityPosition(building, out position))
                    return true;
            }

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

        private bool IsAtBuilding(Entity citizen, Entity building)
        {
            return citizen != Entity.Null &&
                   building != Entity.Null &&
                   EntityManager.Exists(citizen) &&
                   EntityManager.HasComponent<CurrentBuilding>(citizen) &&
                   EntityManager.GetComponentData<CurrentBuilding>(citizen).m_CurrentBuilding == building;
        }

        private void ResolveBribeMeeting(ref ElectionState state, DateTime now, int candidateIndex, Entity candidate, string mayorName, string candidateName)
        {
            Unity.Mathematics.Random random = CreateCampaignRandom(
                now,
                91337 + candidateIndex * 307 + GetCandidateEffectSeed(state) + (int)(state.bribeMeetingDeadlineTicks % 104729));
            int successChancePercent = kBribeMeetingSuccessChancePercent;
            if (ArePartiesEnabled())
                successChancePercent += ElectionPartyTags.GetBribeMeetingSuccessChanceDelta(state, state.GetCandidatePartyIndex(candidateIndex));
            successChancePercent = math.clamp(successChancePercent, 0, 100);

            if (random.NextInt(100) < successChancePercent)
            {
                ConvinceCandidateToChangePlatform(ref state, now, candidateIndex, candidateName, mayorName);
            }
            else
            {
                DebugLog($"Bribe meeting completed without platform change: mayor={DescribeEntity(state.mayor, mayorName)}, target={DescribeEntity(candidate, candidateName)}, venue={FormatEntity(state.bribeMeetingVenue)}, amount={GetCampaignBribeAmount(state):n0}, successChance={successChancePercent}%.");
                PostMayorPlatformMeetingChirp(state, candidate, mayorName, candidateName, false, default, default);

                if (random.NextInt(100) < 20)
                {
                    state.corruptionInvestigationChirpUtcTicks = DateTime.UtcNow.AddMinutes(1).Ticks;
                    state.corruptionInvestigationMayor = state.mayor;
                    DebugLog($"Scheduled corruption investigation chirp for mayor={DescribeEntity(state.mayor, mayorName)} at {new DateTime(state.corruptionInvestigationChirpUtcTicks):O} UTC.");
                }
            }

            ResetBribeMeetingState(ref state);
        }

        private void PostBribeMeetingTravelChirp(ElectionState state, int candidateIndex, Entity candidate, Entity venue)
        {
            Entity mayor = state.mayor;
            bool useMayorSender = IsValidChirpCitizen(mayor);
            Entity sender = useMayorSender ? mayor : candidate;
            if (!IsValidChirpCitizen(sender) || !IsValidVenue(venue))
                return;

            Entity target1 = useMayorSender ? mayor : candidate;
            Entity target2 = useMayorSender ? candidate : mayor;
            Entity target3 = venue;
            string senderName = GetEntityName(sender, useMayorSender ? L("Lifecycle.Name.Mayor", "the mayor") : L("Lifecycle.Name.Candidate", "the candidate"));
            int portraitIndex = useMayorSender
                ? GetMayorPortraitIndex(state)
                : state.GetCandidatePortraitIndex(candidateIndex);
            string portraitImageSource = CandidatePortraitCatalog.GetPortraitImageSource(EntityManager, sender, portraitIndex);
            string text = useMayorSender
                ? LF("Lifecycle.Bribe.Travel.Mayor", "Heading to {{LINK_3}} to meet {{LINK_2}} about a campaign platform discussion.")
                : LF("Lifecycle.Bribe.Travel.Candidate", "Heading to {{LINK_3}} to meet {{LINK_2}} about the campaign platform.");

            bool posted = IsValidChirpCitizen(target2) &&
                CustomChirpsBridge.PostLargeChirpFromEntityWithPortraitImage(text, sender, target1, target2, target3, portraitImageSource, senderName);
            if (!posted)
                CustomChirpsBridge.PostChirpFromEntityWith3Targets(text, sender, target1, target2, target3, senderName);
        }

        private void PostBribeMeetingUnableChirp(ElectionState state, Entity candidate, string mayorName, string candidateName)
        {
            bool hasMayorLink = IsValidChirpCitizen(state.mayor);
            bool hasCandidateLink = IsValidChirpCitizen(candidate);
            if (hasMayorLink && hasCandidateLink && CustomChirpsBridge.SupportsChirpWith2Targets())
            {
                CustomChirpsBridge.PostChirpWith2Targets(
                    LF("Lifecycle.Bribe.Unable", "Election Board reports {0} tried to meet {1} for a campaign platform discussion, but no suitable time and leisure venue could be arranged within 24 in-game hours.", "{LINK_1}", "{LINK_2}"),
                    DepartmentAccountBridge.CensusBureau,
                    state.mayor,
                    candidate,
                    L("Lifecycle.Department.ElectionBoard", "Election Board"));
                return;
            }

            Entity target = hasMayorLink ? state.mayor : hasCandidateLink ? candidate : Entity.Null;
            string subject = hasMayorLink ? "{LINK_1}" : mayorName;
            string targetText = hasMayorLink ? candidateName : hasCandidateLink ? "{LINK_1}" : candidateName;
            PostElectionChirp(
                LF("Lifecycle.Bribe.Unable", "Election Board reports {0} tried to meet {1} for a campaign platform discussion, but no suitable time and leisure venue could be arranged within 24 in-game hours.", subject, targetText),
                target);
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
            ElectionState state = new ElectionState
            {
                version = ElectionState.CurrentVersion,
                initialized = false,
                democraticTransitionCompleted = false,
                stage = ElectionCampaignStage.None,
                lastProcessedDayKey = 0,
                appliedEffectId = 0,
                appliedModifierType1 = -1,
                appliedModifierType2 = -1,
                campaignDonationAmount = ElectionDonationTiers.FixedDonationAmount,
                campaignBribeAmount = BribeAmount,
                candidateCount = ElectionState.DefaultCandidateCount,
                candidateCPortraitIndex = -1,
                candidateDPortraitIndex = -1,
                candidateATagId = ElectionCandidateTags.None,
                candidateBTagId = ElectionCandidateTags.None,
                candidateCTagId = ElectionCandidateTags.None,
                candidateDTagId = ElectionCandidateTags.None,
                mayorTagId = ElectionCandidateTags.None,
                mayorPartyIndex = -1,
                outgoingMayorTagId = ElectionCandidateTags.None,
                outgoingMayorPartyIndex = -1,
                corruptionInvestigationMayor = Entity.Null,
                mayorBribeRecipient = Entity.Null,
                outgoingMayor = Entity.Null,
                pendingMayor = Entity.Null,
                pendingMayorCandidateIndex = -1,
                pendingMayorPartyIndex = -1,
                bribeMeetingCandidateIndex = -1,
                mayorEndorsementCandidateIndex = -1,
                mayorEndorsementCandidate = Entity.Null,
                voteTamperingCandidateIndex = -1,
                voteTamperingCandidate = Entity.Null,
                voteTamperingPollingPlace = Entity.Null,
                supportProgramIdToday = -1,
                trackedMilestoneLevel = -1
            };
            ResetVictoryPartyState(ref state);
            ResetBribeMeetingState(ref state, true);
            ResetMayorEndorsementState(ref state);
            ResetVoteTamperingState(ref state);
            ResetCandidateCorruptionRiskState(ref state);
            ResetMayorBribeTrackingState(ref state);
            ResetOutgoingMayorState(ref state);
            ResetPendingMayorState(ref state);
            ResetSupportProgramState(ref state);
            ResetCashAssistanceState(ref state);
            ResetStrictVotingIdProposalState(ref state);
            return state;
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
            if (state.version != ElectionState.CurrentVersion)
            {
                DebugLog($"State version normalized in memory from {state.version} to {ElectionState.CurrentVersion}.");
                state.version = ElectionState.CurrentVersion;
            }

            EnsureLegislationState(ref state);
            EnsureActiveElectionTiming(ref state);
            EnsureCampaignCosts(ref state);
            EnsureCandidateTags(ref state);
            if (ArePartiesEnabled())
                EnsurePartyState(ref state);
            EnsureSupportProgramState(ref state);

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
            RepairDemocraticTransitionCompletion(ref state);
            EnsureTemporaryMayor(ref state, now);
            RepairLegacyMayorEffectId(ref state, now);

            if (IsActiveCampaignStage(state.stage) && !HasValidCandidateField(state))
            {
                ReplaceInvalidCampaignCandidates(ref state, now);
                return;
            }

            RepairLegacyCandidateEffectIds(ref state, now);
            EnsureDistinctCampaignPortraits(ref state);

            if (IsActiveCampaignStage(state.stage))
            {
                m_LastInvalidCandidateReport = null;
                m_LastCandidateReplacementFailureReport = null;
            }

            if (!state.HasCandidates && state.stage == ElectionCampaignStage.None)
            {
                int month = ElectionUtility.CurrentCalendarMonth(World, now);
                if (month <= 7 && !state.democraticTransitionCompleted)
                {
                    StartCampaign(ref state, now, accelerated: true, reason: "repair empty inactive state before August");
                    return;
                }

                if (month >= GetRegularCampaignStartMonth())
                    StartCampaign(ref state, now, accelerated: false, reason: "repair empty inactive state during regular campaign season");
            }
        }

        private void RepairDemocraticTransitionCompletion(ref ElectionState state)
        {
            if (state.democraticTransitionCompleted || !HasCompletedMayorElectionState(state))
                return;

            state.democraticTransitionCompleted = true;
            DebugLog($"Democratic transition marked complete from existing mayoral state: mayor={FormatEntity(state.mayor)}, mayorEffectId={state.mayorEffectId}, pendingMayor={FormatEntity(state.pendingMayor)}, electionDayKey={state.electionDayKey}, votes={FormatVoteTotals(state)}.");
        }

        private static bool HasCompletedMayorElectionState(ElectionState state)
        {
            if (state.pendingMayor != Entity.Null &&
                (state.pendingMayorEffectId > 0 || state.pendingMayorTermYear > 0))
            {
                return true;
            }

            if (state.mayorEffectId > 0 || state.appliedEffectId > 0)
            {
                return true;
            }

            if (state.stage != ElectionCampaignStage.None)
                return false;

            if (state.electionDayKey > 0 &&
                (state.voteArrivals > 0 ||
                    state.votesA + state.votesB + state.votesC + state.votesD > 0))
            {
                return true;
            }

            return state.mayor != Entity.Null &&
                   state.mayorTermYear > 0 &&
                   state.electionYear > 0 &&
                   state.electionMonth > 0;
        }

        private static void EnsureLegislationState(ref ElectionState state)
        {
            state.legislationFlags = ElectionLegislation.NormalizeFlags(state.legislationFlags);
            if (state.strictVotingIdLawPassed)
                state.SetLegislation(ElectionLegislationType.VoterIdentification, true);
            else
                state.strictVotingIdLawPassed = state.HasLegislation(ElectionLegislationType.VoterIdentification);
        }

        private void RepairLegacyMayorEffectId(ref ElectionState state, DateTime now)
        {
            if (state.mayorEffectId <= 0 || ElectionEffects.IsGeneratedId(state.mayorEffectId))
                return;

            int oldEffectId = state.mayorEffectId;
            Entity seedEntity = state.mayor != Entity.Null ? state.mayor : state.GetCandidate(0);
            state.mayorEffectId = PickEffect(seedEntity, now, 4241 + oldEffectId);
            state.mayorNegativeSoftened = false;
            DebugLog($"Migrated legacy mayor platform effect: {oldEffectId} -> {state.mayorEffectId}.");
        }

        private void RepairLegacyCandidateEffectIds(ref ElectionState state, DateTime now)
        {
            if (!state.HasCandidates)
                return;

            bool changed = false;
            int candidateCount = state.ActiveCandidateCount;
            for (int i = 0; i < candidateCount; i++)
            {
                int effectId = state.GetCandidateEffectId(i);
                if (effectId <= 0 || !ElectionEffects.IsGeneratedId(effectId))
                {
                    int oldEffectId = effectId;
                    int newEffectId = PickUniqueCandidateEffect(state, state.GetCandidate(i), now, i);
                    state.SetCandidateEffectId(i, newEffectId);
                    changed = true;
                    DebugLog($"Migrated legacy {ElectionState.GetCandidateFallbackName(i)} platform effect: {oldEffectId} -> {newEffectId}.");
                }
            }

            if (changed)
                RepairDuplicateCandidateEffects(ref state, now);
        }

        private void RepairDuplicateCandidateEffects(ref ElectionState state, DateTime now)
        {
            int candidateCount = state.ActiveCandidateCount;
            for (int i = 1; i < candidateCount; i++)
            {
                int effectId = state.GetCandidateEffectId(i);
                if (!HasSamePlatformAsPreviousCandidate(state, i, effectId))
                    continue;

                int oldEffectId = effectId;
                int newEffectId = PickUniqueCandidateEffect(state, state.GetCandidate(i), now, i);
                state.SetCandidateEffectId(i, newEffectId);
                DebugLog($"Repaired duplicate candidate platform effect: {ElectionState.GetCandidateFallbackName(i)} {oldEffectId} -> {newEffectId}.");
            }
        }

        private static bool IsActiveCampaignStage(ElectionCampaignStage stage)
        {
            return stage == ElectionCampaignStage.CandidatesSelected ||
                   stage == ElectionCampaignStage.PollReleased ||
                   stage == ElectionCampaignStage.Voting;
        }

        private static bool IsDonationOpenStage(ElectionCampaignStage stage)
        {
            return stage == ElectionCampaignStage.CandidatesSelected ||
                   stage == ElectionCampaignStage.PollReleased;
        }

        private static int GetCampaignDonationAmount(ElectionState state)
        {
            return ElectionDonationTiers.NormalizeDonationAmount(state.campaignDonationAmount);
        }

        private static int GetCampaignBribeAmount(ElectionState state)
        {
            return GetCampaignBribeAmount(state, state.mayorTagId);
        }

        private static int GetCampaignBribeAmount(ElectionState state, int mayorTagId)
        {
            int baseAmount = state.campaignBribeAmount > 0 ? state.campaignBribeAmount : BribeAmount;
            return ElectionCandidateTags.GetMayorActionCost(mayorTagId, baseAmount);
        }

        private static int GetDonationSofteningThreshold(ElectionState state)
        {
            return math.max(1, ClampMoneyAmount((long)GetCampaignDonationAmount(state) * 10L));
        }

        private static void EnsureCampaignCosts(ref ElectionState state)
        {
            if (state.campaignDonationAmount <= 0)
                state.campaignDonationAmount = ElectionDonationTiers.FixedDonationAmount;

            if (state.campaignBribeAmount <= 0)
                state.campaignBribeAmount = BribeAmount;
        }

        private static void EnsureCandidateTags(ref ElectionState state)
        {
            state.candidateCount = ElectionState.NormalizeCandidateCount(state.candidateCount <= 0 ? ElectionState.DefaultCandidateCount : state.candidateCount);
            for (int i = 0; i < ElectionState.MaxCandidateCount; i++)
                state.SetCandidateTagId(i, ElectionCandidateTags.NormalizeId(state.GetCandidateTagId(i)));

            state.mayorTagId = ElectionCandidateTags.NormalizeId(state.mayorTagId);
            state.outgoingMayorTagId = ElectionCandidateTags.NormalizeId(state.outgoingMayorTagId);
            state.appliedEffectTagId = ElectionCandidateTags.NormalizeId(state.appliedEffectTagId);

            int candidateCount = state.ActiveCandidateCount;
            for (int i = 0; i < candidateCount; i++)
            {
                int tagId = state.GetCandidateTagId(i);
                if (tagId == ElectionCandidateTags.None)
                    continue;

                for (int j = 0; j < i; j++)
                {
                    if (state.GetCandidateTagId(j) == tagId)
                    {
                        state.SetCandidateTagId(i, ElectionCandidateTags.None);
                        break;
                    }
                }
            }
        }

        private static bool ArePartiesEnabled()
        {
            return Mod.m_Setting?.EnableParties ?? false;
        }

        private static bool IsRunoffVotingEnabled()
        {
            return Mod.m_Setting?.EnableRunoffVoting ?? false;
        }

        private static int GetRegularCampaignStartMonth()
        {
            return IsRunoffVotingEnabled() ? 9 : 10;
        }

        private static int GetManagedPartyCount(ElectionState state)
        {
            return state.HasCandidates
                ? state.ActiveCandidateCount
                : GetConfiguredCandidateCount();
        }

        private static bool IsManagedPartyIndex(ElectionState state, int partyIndex)
        {
            return partyIndex >= 0 && partyIndex < GetManagedPartyCount(state);
        }

        private static void EnsurePartyState(ref ElectionState state)
        {
            state.candidateCount = ElectionState.NormalizeCandidateCount(state.candidateCount <= 0 ? ElectionState.DefaultCandidateCount : state.candidateCount);
            for (int i = 0; i < ElectionState.MaxCandidateCount; i++)
            {
                if (string.IsNullOrWhiteSpace(state.GetPartyName(i)))
                    state.SetPartyName(i, ElectionPartyTags.GetDefaultName(i));

                if (state.GetPartyColor(i) == 0)
                    state.SetPartyColor(i, ElectionPartyTags.GetDefaultColor(i));

                bool resetTags = ShouldResetPartyTags(state, i);
                if (resetTags)
                {
                    if (state.GetPartyReputation(i) == 0)
                        state.SetPartyReputation(i, ElectionPartyTags.DefaultReputation);

                    for (int slot = 0; slot < ElectionPartyTags.TagsPerParty; slot++)
                        state.SetPartyTagId(i, slot, ElectionPartyTags.GetDefaultTagId(i, slot));
                }
                else
                {
                    for (int slot = 0; slot < ElectionPartyTags.TagsPerParty; slot++)
                        state.SetPartyTagId(i, slot, ElectionPartyTags.NormalizeId(state.GetPartyTagId(i, slot)));
                    state.SetPartyReputation(i, state.GetPartyReputation(i));
                }

                state.SetPartyConsecutiveTerms(i, state.GetPartyConsecutiveTerms(i));
                state.SetPartyWins(i, state.GetPartyWins(i));
            }

            state.mayorPartyIndex = NormalizeKnownPartyIndex(state, state.mayorPartyIndex, state.mayor);
            state.outgoingMayorPartyIndex = NormalizeKnownPartyIndex(state, state.outgoingMayorPartyIndex, state.outgoingMayor);
        }

        private static bool ShouldResetPartyTags(ElectionState state, int partyIndex)
        {
            int sum = 0;
            for (int slot = 0; slot < ElectionPartyTags.TagsPerParty; slot++)
            {
                int tagId = ElectionPartyTags.NormalizeId(state.GetPartyTagId(partyIndex, slot));
                if (!ElectionPartyTags.HasTag(tagId))
                    return true;

                for (int previousSlot = 0; previousSlot < slot; previousSlot++)
                {
                    if (state.GetPartyTagId(partyIndex, previousSlot) == tagId)
                        return true;
                }

                sum += ElectionPartyTags.GetValue(tagId);
            }

            return sum != 0;
        }

        private static int NormalizeKnownPartyIndex(ElectionState state, int partyIndex, Entity citizen)
        {
            if (ElectionState.IsPartyIndex(partyIndex))
                return partyIndex;

            if (citizen == Entity.Null)
                return -1;

            for (int i = 0; i < ElectionState.MaxCandidateCount; i++)
            {
                if (state.GetCandidate(i) == citizen)
                    return i;
            }

            return -1;
        }

        private static string SanitizePartyName(string name, string fallback)
        {
            name = (name ?? string.Empty).Trim();
            if (name.Length == 0)
                name = fallback;

            string sanitized = string.Empty;
            for (int i = 0; i < name.Length && sanitized.Length < 48; i++)
            {
                char value = name[i];
                if (value >= 32 && value <= 126)
                    sanitized += value;
            }

            sanitized = sanitized.Trim();
            return sanitized.Length == 0 ? fallback : sanitized;
        }

        private static bool TryParsePartyColor(string value, out int color)
        {
            color = 0;
            value = (value ?? string.Empty).Trim();
            if (value.StartsWith("#", StringComparison.Ordinal))
                value = value.Substring(1);

            if (value.Length != 6)
                return false;

            int parsed = 0;
            for (int i = 0; i < value.Length; i++)
            {
                int nibble = ParseHexNibble(value[i]);
                if (nibble < 0)
                    return false;

                parsed = (parsed << 4) | nibble;
            }

            color = parsed & 0xffffff;
            return true;
        }

        private static int ParseHexNibble(char value)
        {
            if (value >= '0' && value <= '9')
                return value - '0';
            if (value >= 'a' && value <= 'f')
                return value - 'a' + 10;
            if (value >= 'A' && value <= 'F')
                return value - 'A' + 10;
            return -1;
        }

        private static void EnsureSupportProgramState(ref ElectionState state)
        {
            if (state.supportProgramIdToday < -1 || state.supportProgramIdToday >= ElectionSupportPrograms.Count)
                state.supportProgramIdToday = -1;

            if (state.supportProgramBalanceVersion < kSupportProgramBalanceVersion)
                MigrateSupportProgramBalance(ref state);
            else if (state.supportProgramBalanceVersion > kSupportProgramBalanceVersion)
                state.supportProgramBalanceVersion = kSupportProgramBalanceVersion;

            state.teenTurnoutBonusPercent = math.clamp(state.teenTurnoutBonusPercent, 0, 100);
            state.adultTurnoutBonusPercent = math.clamp(state.adultTurnoutBonusPercent, 0, 100);
            state.elderlyTurnoutBonusPercent = math.clamp(state.elderlyTurnoutBonusPercent, 0, 100);
            state.uneducatedTurnoutBonusPercent = math.clamp(state.uneducatedTurnoutBonusPercent, 0, 100);
            state.educatedTurnoutBonusPercent = math.clamp(state.educatedTurnoutBonusPercent, 0, 100);
            state.lowIncomeTurnoutBonusPercent = math.clamp(state.lowIncomeTurnoutBonusPercent, 0, 100);
            state.transitVoucherTurnoutBonusPercent = math.clamp(state.transitVoucherTurnoutBonusPercent, 0, 100);
            state.civicForumTurnoutBonusPercent = math.clamp(state.civicForumTurnoutBonusPercent, 0, 100);
            state.cashAssistanceTurnoutBonusPercent = math.clamp(state.cashAssistanceTurnoutBonusPercent, 0, 100);
        }

        private static void MigrateSupportProgramBalance(ref ElectionState state)
        {
            if (IsDonationOpenStage(state.stage))
            {
                state.teenTurnoutBonusPercent = MigrateLegacyTurnoutBonusPercent(
                    state.teenTurnoutBonusPercent,
                    ElectionSupportPrograms.TeenTurnoutProgramDailyBonusPercent);
                state.elderlyTurnoutBonusPercent = MigrateLegacyTurnoutBonusPercent(
                    state.elderlyTurnoutBonusPercent,
                    ElectionSupportPrograms.ElderlyTurnoutProgramDailyBonusPercent);
            }

            state.supportProgramBalanceVersion = kSupportProgramBalanceVersion;
        }

        private static int MigrateLegacyTurnoutBonusPercent(int currentPercent, int newProgramBonusPercent)
        {
            currentPercent = math.clamp(currentPercent, 0, 100);
            if (currentPercent <= 0 ||
                newProgramBonusPercent == ElectionSupportPrograms.LegacyTurnoutProgramDailyBonusPercent)
            {
                return currentPercent;
            }

            int fundedProgramCount = (currentPercent + ElectionSupportPrograms.LegacyTurnoutProgramDailyBonusPercent - 1) /
                ElectionSupportPrograms.LegacyTurnoutProgramDailyBonusPercent;
            return math.clamp(fundedProgramCount * newProgramBonusPercent, 0, 100);
        }

        private static void ApplySupportProgram(ref ElectionState state, ElectionSupportProgramType type)
        {
            switch (type)
            {
                case ElectionSupportProgramType.ElectionDayHoliday:
                    state.electionDayHolidayScheduled = true;
                    break;
                case ElectionSupportProgramType.TeenVoterEducation:
                    state.teenTurnoutBonusPercent = math.clamp(
                        state.teenTurnoutBonusPercent + ElectionSupportPrograms.GetBonusPercent(type),
                        0,
                        100);
                    break;
                case ElectionSupportProgramType.AdultVoterEducation:
                    state.adultTurnoutBonusPercent = math.clamp(
                        state.adultTurnoutBonusPercent + ElectionSupportPrograms.GetBonusPercent(type),
                        0,
                        100);
                    break;
                case ElectionSupportProgramType.ElderlyVoterEducation:
                    state.elderlyTurnoutBonusPercent = math.clamp(
                        state.elderlyTurnoutBonusPercent + ElectionSupportPrograms.GetBonusPercent(type),
                        0,
                        100);
                    break;
                case ElectionSupportProgramType.VoterEducation:
                    state.uneducatedTurnoutBonusPercent = math.clamp(
                        state.uneducatedTurnoutBonusPercent + ElectionSupportPrograms.GetBonusPercent(type),
                        0,
                        100);
                    state.educatedTurnoutBonusPercent = math.clamp(
                        state.educatedTurnoutBonusPercent + ElectionSupportPrograms.GetBonusPercent(type),
                        0,
                        100);
                    break;
                case ElectionSupportProgramType.LowIncomeVoterOutreach:
                    state.lowIncomeTurnoutBonusPercent = math.clamp(
                        state.lowIncomeTurnoutBonusPercent + ElectionSupportPrograms.GetBonusPercent(type),
                        0,
                        100);
                    break;
                case ElectionSupportProgramType.TransitVouchers:
                    state.transitVoucherTurnoutBonusPercent = math.clamp(
                        state.transitVoucherTurnoutBonusPercent + ElectionSupportPrograms.GetBonusPercent(type),
                        0,
                        100);
                    break;
                case ElectionSupportProgramType.CivicForums:
                    state.civicForumTurnoutBonusPercent = math.clamp(
                        state.civicForumTurnoutBonusPercent + ElectionSupportPrograms.GetBonusPercent(type),
                        0,
                        100);
                    break;
            }
        }

        private static string GetSupportProgramOutcome(ElectionState state, ElectionSupportProgramType type)
        {
            switch (type)
            {
                case ElectionSupportProgramType.ElectionDayHoliday:
                    return L("Lifecycle.Support.Outcome.Holiday", "Election day will be treated as a holiday for resident schedules.");
                case ElectionSupportProgramType.TeenVoterEducation:
                    return LF("Lifecycle.Support.Outcome.Teen", "Teen election turnout bonus is now +{0}%.", state.teenTurnoutBonusPercent);
                case ElectionSupportProgramType.AdultVoterEducation:
                    return LF("Lifecycle.Support.Outcome.Adult", "Adult election turnout bonus is now +{0}%.", state.adultTurnoutBonusPercent);
                case ElectionSupportProgramType.ElderlyVoterEducation:
                    return LF("Lifecycle.Support.Outcome.Elderly", "Elderly election turnout bonus is now +{0}%.", state.elderlyTurnoutBonusPercent);
                case ElectionSupportProgramType.VoterEducation:
                    return LF("Lifecycle.Support.Outcome.Education", "Uneducated and poorly educated election turnout bonuses are now +{0}%.", math.max(state.uneducatedTurnoutBonusPercent, state.educatedTurnoutBonusPercent));
                case ElectionSupportProgramType.LowIncomeVoterOutreach:
                    return LF("Lifecycle.Support.Outcome.LowIncome", "Struggling and modest-income resident turnout bonus is now +{0}%.", state.lowIncomeTurnoutBonusPercent);
                case ElectionSupportProgramType.TransitVouchers:
                    return LF("Lifecycle.Support.Outcome.Transit", "Transit voucher turnout bonus is now +{0}% for eligible residents without cars.", state.transitVoucherTurnoutBonusPercent);
                case ElectionSupportProgramType.CivicForums:
                    return LF("Lifecycle.Support.Outcome.CivicForums", "Educated resident turnout bonus is now +{0}%.", state.civicForumTurnoutBonusPercent);
                default:
                    return L("Lifecycle.Support.Outcome.Generic", "The civic program is active.");
            }
        }

        private static string GetSupportProgramLabel(int programIndex)
        {
            return ElectionSupportPrograms.TryGet(programIndex, out ElectionSupportProgramDefinition program)
                ? program.Title
                : "a civic program";
        }

        private void SetCampaignCostsFromMonthlyBalance(ref ElectionState state)
        {
            int monthlyBalance = GetMonthlyBalance();
            if (monthlyBalance > 0)
            {
                int donationAmount = RoundToOneSignificantDigit(monthlyBalance * 0.10d);
                state.campaignDonationAmount = math.max(1, donationAmount);
                state.campaignBribeAmount = ClampMoneyAmount((long)state.campaignDonationAmount * 3L);
            }
            else
            {
                state.campaignDonationAmount = math.max(1, ElectionDonationTiers.FixedDonationAmount / 10);
                state.campaignBribeAmount = math.max(1, BribeAmount / 10);
            }

            DebugLog($"Campaign costs set from monthly balance: balance={monthlyBalance:n0}, donation={state.campaignDonationAmount:n0}, bribe={state.campaignBribeAmount:n0}.");
        }

        private int GetMonthlyBalance()
        {
            try
            {
                return m_CityServiceBudgetSystem != null ? m_CityServiceBudgetSystem.GetBalance() : 0;
            }
            catch (Exception ex)
            {
                DebugLog($"Could not read monthly budget balance; using fallback campaign costs. error={ex.Message}");
                return 0;
            }
        }

        private static int RoundToOneSignificantDigit(double value)
        {
            if (value <= 0d)
                return 0;

            double magnitude = Math.Pow(10d, Math.Floor(Math.Log10(value)));
            double rounded = Math.Round(value / magnitude, MidpointRounding.AwayFromZero) * magnitude;
            return ClampMoneyAmount((long)Math.Round(rounded, MidpointRounding.AwayFromZero));
        }

        private static int ClampMoneyAmount(long amount)
        {
            if (amount <= 0L)
                return 0;

            return amount > int.MaxValue ? int.MaxValue : (int)amount;
        }

        private static void ResetVictoryPartyState(ref ElectionState state)
        {
            state.victoryPartyVenue = Entity.Null;
            state.victoryPartyElectionDayKey = 0;
            state.victoryPartyWinnerIndex = -1;
            state.victoryPartyTripsRequested = false;
            state.victoryPartyTripRequests = 0;
            state.victoryWinnerChirpSent = false;
            state.victoryLoserChirpSent = false;
            state.victoryWinnerChirpUtcTicks = 0;
            state.victoryLoserChirpUtcTicks = 0;
            state.victoryPartyNextVoterIndex = 0;
            state.victoryPartyNextTripBatchTicks = 0;
            state.victoryPartyWinnerTripRequested = false;
        }

        private static void ResetBribeMeetingState(ref ElectionState state, bool clearBlock = false)
        {
            state.bribeMeetingCandidateIndex = -1;
            state.bribeMeetingCandidate = Entity.Null;
            state.bribeMeetingVenue = Entity.Null;
            state.bribeMeetingDeadlineTicks = 0;
            state.bribeMeetingNextAttemptTicks = 0;
            state.bribeMeetingTripsRequested = false;

            if (clearBlock)
            {
                state.bribeBlockedUntilTicks = 0;
                state.bribeDayKey = 0;
            }
        }

        private static void ResetMayorEndorsementState(ref ElectionState state)
        {
            state.mayorEndorsementCandidateIndex = -1;
            state.mayorEndorsementCandidate = Entity.Null;
            state.mayorEndorsementChirpUtcTicks = 0;
            state.mayorEndorsementChirpSent = false;
        }

        private static void ResetVoteTamperingState(ref ElectionState state)
        {
            state.voteTamperingCandidateIndex = -1;
            state.voteTamperingCandidate = Entity.Null;
            state.voteTamperingScheduledMinute = 0;
            state.voteTamperingFireStarted = false;
            state.voteTamperingResolved = false;
            state.voteTamperingPollingPlace = Entity.Null;
            state.voteTamperingLostVotesA = 0;
            state.voteTamperingLostVotesB = 0;
            state.voteTamperingLostVotesC = 0;
            state.voteTamperingLostVotesD = 0;
            state.voteTamperingProtestChirpUtcTicks = 0;
            state.voteTamperingProtestChirpSent = false;
        }

        private static void ResetCandidateCorruptionRiskState(ref ElectionState state)
        {
            state.candidateACorruptionRiskSteps = 0;
            state.candidateBCorruptionRiskSteps = 0;
            state.candidateCCorruptionRiskSteps = 0;
            state.candidateDCorruptionRiskSteps = 0;
            state.corruptionArrestCheckCompleted = false;
        }

        private static void ResetMayorBribeTrackingState(ref ElectionState state)
        {
            state.mayorBribeRecipient = Entity.Null;
            state.mayorBribeTotal = 0;
        }

        private static void ResetOutgoingMayorState(ref ElectionState state)
        {
            state.outgoingMayor = Entity.Null;
            state.outgoingMayorBribeTotal = 0;
            state.outgoingMayorTagId = ElectionCandidateTags.None;
            state.outgoingMayorPartyIndex = -1;
        }

        private static void ResetPendingMayorState(ref ElectionState state)
        {
            state.pendingMayorCandidateIndex = -1;
            state.pendingMayor = Entity.Null;
            state.pendingMayorEffectId = 0;
            state.pendingMayorTagId = ElectionCandidateTags.None;
            state.pendingMayorNegativeSoftened = false;
            state.pendingMayorPartyIndex = -1;
            state.pendingMayorTermYear = 0;
            state.pendingMayorInaugurated = false;
        }

        private static void ResetSupportProgramState(ref ElectionState state)
        {
            state.electionDayHolidayScheduled = false;
            state.supportProgramDayKey = 0;
            state.supportProgramIdToday = -1;
            state.teenTurnoutBonusPercent = 0;
            state.adultTurnoutBonusPercent = 0;
            state.elderlyTurnoutBonusPercent = 0;
            state.uneducatedTurnoutBonusPercent = 0;
            state.educatedTurnoutBonusPercent = 0;
            state.lowIncomeTurnoutBonusPercent = 0;
            state.transitVoucherTurnoutBonusPercent = 0;
            state.civicForumTurnoutBonusPercent = 0;
            state.supportProgramBalanceVersion = kSupportProgramBalanceVersion;
        }

        private static void ResetCashAssistanceState(ref ElectionState state)
        {
            state.cashAssistanceTurnoutBonusPercent = 0;
        }

        private static void ResetStrictVotingIdProposalState(ref ElectionState state)
        {
            state.strictVotingIdProposalPending = false;
            state.strictVotingIdProposalPassed = false;
            state.strictVotingIdChirpUtcTicks = 0;
            state.strictVotingIdChirpSent = false;
        }

        private static void ResetPollState(ref ElectionState state)
        {
            state.pollVotesA = 0;
            state.pollVotesB = 0;
            state.pollVotesC = 0;
            state.pollVotesD = 0;
            state.pollUndecided = 0;
            state.pollTeenVotesA = 0;
            state.pollTeenVotesB = 0;
            state.pollTeenVotesC = 0;
            state.pollTeenVotesD = 0;
            state.pollTeenUndecided = 0;
            state.pollAdultVotesA = 0;
            state.pollAdultVotesB = 0;
            state.pollAdultVotesC = 0;
            state.pollAdultVotesD = 0;
            state.pollAdultUndecided = 0;
            state.pollElderlyVotesA = 0;
            state.pollElderlyVotesB = 0;
            state.pollElderlyVotesC = 0;
            state.pollElderlyVotesD = 0;
            state.pollElderlyUndecided = 0;
            state.pollEducation0VotesA = 0;
            state.pollEducation0VotesB = 0;
            state.pollEducation0VotesC = 0;
            state.pollEducation0VotesD = 0;
            state.pollEducation0Undecided = 0;
            state.pollEducation1VotesA = 0;
            state.pollEducation1VotesB = 0;
            state.pollEducation1VotesC = 0;
            state.pollEducation1VotesD = 0;
            state.pollEducation1Undecided = 0;
            state.pollEducation2VotesA = 0;
            state.pollEducation2VotesB = 0;
            state.pollEducation2VotesC = 0;
            state.pollEducation2VotesD = 0;
            state.pollEducation2Undecided = 0;
            state.pollEducation3VotesA = 0;
            state.pollEducation3VotesB = 0;
            state.pollEducation3VotesC = 0;
            state.pollEducation3VotesD = 0;
            state.pollEducation3Undecided = 0;
            state.pollEducation4VotesA = 0;
            state.pollEducation4VotesB = 0;
            state.pollEducation4VotesC = 0;
            state.pollEducation4VotesD = 0;
            state.pollEducation4Undecided = 0;
            state.pollIncome0VotesA = 0;
            state.pollIncome0VotesB = 0;
            state.pollIncome0VotesC = 0;
            state.pollIncome0VotesD = 0;
            state.pollIncome0Undecided = 0;
            state.pollIncome1VotesA = 0;
            state.pollIncome1VotesB = 0;
            state.pollIncome1VotesC = 0;
            state.pollIncome1VotesD = 0;
            state.pollIncome1Undecided = 0;
            state.pollIncome2VotesA = 0;
            state.pollIncome2VotesB = 0;
            state.pollIncome2VotesC = 0;
            state.pollIncome2VotesD = 0;
            state.pollIncome2Undecided = 0;
            state.pollIncome3VotesA = 0;
            state.pollIncome3VotesB = 0;
            state.pollIncome3VotesC = 0;
            state.pollIncome3VotesD = 0;
            state.pollIncome3Undecided = 0;
            state.pollIncome4VotesA = 0;
            state.pollIncome4VotesB = 0;
            state.pollIncome4VotesC = 0;
            state.pollIncome4VotesD = 0;
            state.pollIncome4Undecided = 0;
        }

        private bool IsElectionDay(ElectionState state, DateTime now)
        {
            ElectionUtility.GetCurrentCalendarDate(World, now, out int year, out int month, out int day);
            if (state.stage == ElectionCampaignStage.Voting && state.electionDayKey > 0)
                return ElectionUtility.DayKey(year, month, day) == state.electionDayKey;

            return state.electionYear > 0 &&
                   state.electionMonth > 0 &&
                   year == state.electionYear &&
                   month == state.electionMonth &&
                   day == 1;
        }

        private bool HasValidCandidateField(ElectionState state)
        {
            int candidateCount = state.ActiveCandidateCount;
            if (candidateCount < ElectionState.MinCandidateCount)
                return false;

            for (int i = 0; i < candidateCount; i++)
            {
                Entity candidate = state.GetCandidate(i);
                if (!IsValidResidentEntity(candidate) || candidate == state.mayor)
                    return false;

                for (int j = 0; j < i; j++)
                {
                    if (candidate == state.GetCandidate(j))
                        return false;
                }
            }

            return true;
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
            LogInvalidActiveCampaignCandidates(state, now);

            bool replaced = false;
            int candidateCount = state.ActiveCandidateCount;
            for (int i = 0; i < candidateCount; i++)
            {
                Entity originalCandidate = state.GetCandidate(i);
                if (!IsValidCampaignCandidate(state, i))
                    replaced |= TryReplaceInvalidCampaignCandidate(ref state, now, i, originalCandidate);
            }

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

        private bool IsValidCampaignCandidate(ElectionState state, int candidateIndex)
        {
            Entity candidate = state.GetCandidate(candidateIndex);
            if (!IsValidResidentEntity(candidate) || candidate == state.mayor)
                return false;

            int candidateCount = state.ActiveCandidateCount;
            for (int i = 0; i < candidateCount; i++)
            {
                if (i != candidateIndex && candidate == state.GetCandidate(i))
                    return false;
            }

            return true;
        }

        private bool TryReplaceInvalidCampaignCandidate(ref ElectionState state, DateTime now, int candidateIndex, Entity oldCandidate)
        {
            string slotName = ElectionState.GetCandidateFallbackName(candidateIndex);
            string oldName = GetCandidateChirpName(oldCandidate);
            string invalidReason = GetResidentValidityReason(oldCandidate);

            if (!TryPickReplacementCandidate(state, now, oldCandidate, candidateIndex, out Entity replacement))
            {
                ReportCandidateReplacementFailure(state, now, candidateIndex, oldCandidate, invalidReason);
                return false;
            }

            ApplyCandidateReplacement(ref state, now, candidateIndex, replacement);
            string replacementName = GetCandidateChirpName(replacement);
            DebugLog($"Candidate replaced: slot={slotName}, old={DescribeEntity(oldCandidate, oldName)}, replacement={DescribeEntity(replacement, replacementName)}, reason={invalidReason}, state={DescribeState(state)}.");
            PostCandidateReplacementChirp(state, candidateIndex, oldName, replacement, replacementName);
            return true;
        }

        private void ApplyCandidateReplacement(ref ElectionState state, DateTime now, int candidateIndex, Entity replacement)
        {
            DateTime utcNow = DateTime.UtcNow;
            int inheritedEffectId = state.GetCandidateEffectId(candidateIndex);
            int inheritedTagId = state.GetCandidateTagId(candidateIndex);
            bool inheritedNegativeSoftened = state.GetCandidateNegativeSoftened(candidateIndex);
            bool inheritedSoftenAttempted = state.GetCandidateSoftenAttempted(candidateIndex);
            state.SetCandidate(candidateIndex, replacement);
            state.SetCandidateEffectId(candidateIndex, inheritedEffectId != 0
                ? inheritedEffectId
                : PickUniqueCandidateEffect(state, replacement, now, candidateIndex));
            CaptureCandidateProfile(replacement, out int age, out int education, out int workType, out int wealth);
            state.SetCandidateProfile(candidateIndex, age, education, workType, wealth);
            state.SetCandidateTagId(candidateIndex, inheritedTagId);
            state.SetCandidatePortraitIndex(
                candidateIndex,
                PickDistinctPortraitIndex(
                    replacement,
                    17 + candidateIndex * 7901,
                    state.mayor,
                    GetBaseMayorPortraitIndex(state),
                    GetNearestOtherCandidate(state, candidateIndex),
                    GetNearestOtherCandidatePortraitIndex(state, candidateIndex)));
            state.SetCandidateNegativeSoftened(candidateIndex, inheritedNegativeSoftened);
            state.SetCandidateSoftenAttempted(candidateIndex, inheritedSoftenAttempted);
            state.SetCandidatePlatformChirpSent(candidateIndex, false);
            state.SetCandidatePlatformChirpUtcTicks(candidateIndex, utcNow.AddMinutes(2).Ticks);
            state.SetCandidatePlatformChirpDayKey(candidateIndex, 0);
            state.SetCandidatePlatformChirpMinute(candidateIndex, 0);
            state.SetCandidatePollResponseChirpSent(candidateIndex, true);
            state.SetCandidatePollResponseChirpUtcTicks(candidateIndex, 0);
            ClearPendingDonationChirp(candidateIndex);
            EnsureDistinctCampaignPortraits(ref state);
        }

        private bool TryPickReplacementCandidate(ElectionState state, DateTime now, Entity excludedCandidate, int candidateIndex, out Entity replacement)
        {
            replacement = Entity.Null;
            CandidateSimilarityProfile targetProfile = GetReplacementTargetProfile(state, candidateIndex, excludedCandidate);

            using (NativeArray<Entity> entities = m_CandidateQuery.ToEntityArray(Allocator.Temp))
            using (NativeArray<Citizen> citizens = m_CandidateQuery.ToComponentDataArray<Citizen>(Allocator.Temp))
            {
                uint seed = (uint)math.max(1, now.Year * 10000 + now.Month * 100 + now.Day + (int)m_SimulationSystem.frameIndex + 7001 + candidateIndex * 101);
                Unity.Mathematics.Random random = Unity.Mathematics.Random.CreateFromIndex(seed);
                int eligibleCount = 0;
                int bestScore = int.MaxValue;
                int bestTieCount = 0;
                for (int i = 0; i < entities.Length; i++)
                {
                    Entity candidate = entities[i];
                    if (candidate == excludedCandidate ||
                        candidate == state.mayor ||
                        IsCandidateAlreadyInCampaign(state, candidateIndex, candidate) ||
                        !ElectionUtility.IsEligibleResident(EntityManager, candidate, citizens[i]))
                    {
                        continue;
                    }

                    eligibleCount++;
                    int score = GetReplacementSimilarityScore(candidate, citizens[i], targetProfile);
                    if (score < bestScore)
                    {
                        bestScore = score;
                        bestTieCount = 1;
                        replacement = candidate;
                    }
                    else if (score == bestScore)
                    {
                        bestTieCount++;
                        if (random.NextInt(bestTieCount) == 0)
                            replacement = candidate;
                    }
                }

                if (eligibleCount == 0 || replacement == Entity.Null)
                {
                    DebugLog($"Replacement candidate selection failed: slot={candidateIndex}, queryCount={entities.Length}, excluded={FormatEntity(excludedCandidate)}, currentMayor={FormatEntity(state.mayor)}.");
                    return false;
                }

                DebugLog($"Replacement candidate selected: slot={candidateIndex}, eligibleCount={eligibleCount}, seed={seed}, bestScore={bestScore}, targetProfile={DescribeReplacementProfile(targetProfile)}, replacement={DescribeEntity(replacement, "Replacement Candidate")}.");
                return true;
            }
        }

        private CandidateSimilarityProfile GetReplacementTargetProfile(ElectionState state, int candidateIndex, Entity oldCandidate)
        {
            CandidateSimilarityProfile profile = new CandidateSimilarityProfile
            {
                age = state.GetCandidateAge(candidateIndex),
                education = state.GetCandidateEducation(candidateIndex),
                workType = state.GetCandidateWorkType(candidateIndex),
                wealth = state.GetCandidateWealth(candidateIndex),
                hasCar = false,
                hasCarKnown = false
            };

            if (oldCandidate != Entity.Null &&
                EntityManager.Exists(oldCandidate))
            {
                profile.hasCar = HasRegisteredCar(oldCandidate);
                profile.hasCarKnown = true;

                if (EntityManager.HasComponent<Citizen>(oldCandidate))
                {
                    Citizen oldCitizen = EntityManager.GetComponentData<Citizen>(oldCandidate);
                    profile.age = (int)oldCitizen.GetAge();
                    profile.education = oldCitizen.GetEducationLevel();
                    profile.workType = ElectionUtility.GetWorkType(EntityManager, oldCandidate);
                    profile.wealth = ElectionUtility.GetWealthBracket(EntityManager, oldCandidate);
                }
            }

            return profile;
        }

        private int GetReplacementSimilarityScore(Entity candidate, Citizen citizen, CandidateSimilarityProfile targetProfile)
        {
            int age = (int)citizen.GetAge();
            int education = citizen.GetEducationLevel();
            int workType = ElectionUtility.GetWorkType(EntityManager, candidate);
            int wealth = ElectionUtility.GetWealthBracket(EntityManager, candidate);

            int score = 0;
            score += math.abs(age - targetProfile.age) * 1200;
            score += math.abs(education - targetProfile.education) * 180;
            score += math.abs(wealth - targetProfile.wealth) * 180;
            score += GetWorkTypeDistance(workType, targetProfile.workType) * 55;

            if (targetProfile.hasCarKnown && HasRegisteredCar(candidate) != targetProfile.hasCar)
                score += 220;

            return score;
        }

        private static int GetWorkTypeDistance(int workType, int targetWorkType)
        {
            int category = GetWorkTypeCategory(workType);
            int targetCategory = GetWorkTypeCategory(targetWorkType);
            if (category != targetCategory)
                return 12 + math.abs(category - targetCategory) * 4;

            return math.abs(workType - targetWorkType);
        }

        private static int GetWorkTypeCategory(int workType)
        {
            if (workType >= 30)
                return 2;
            if (workType >= 10)
                return 1;
            return 0;
        }

        private bool HasRegisteredCar(Entity candidate)
        {
            return candidate != Entity.Null &&
                   EntityManager.Exists(candidate) &&
                   EntityManager.HasComponent<CarKeeper>(candidate);
        }

        private static string DescribeReplacementProfile(CandidateSimilarityProfile profile)
        {
            string carText = profile.hasCarKnown
                ? profile.hasCar ? "car=yes" : "car=no"
                : "car=unknown";
            return $"age={profile.age}, education={profile.education}, workType={profile.workType}, wealth={profile.wealth}, {carText}";
        }

        private static bool CampaignHasAnyCandidateTag(ElectionState state)
        {
            int candidateCount = state.ActiveCandidateCount;
            for (int i = 0; i < candidateCount; i++)
            {
                if (state.GetCandidateTagId(i) != ElectionCandidateTags.None)
                    return true;
            }

            return false;
        }

        private static bool IsCandidateAlreadyInCampaign(ElectionState state, int replacementIndex, Entity candidate)
        {
            int candidateCount = state.ActiveCandidateCount;
            for (int i = 0; i < candidateCount; i++)
            {
                if (i != replacementIndex && state.GetCandidate(i) == candidate)
                    return true;
            }

            return false;
        }

        private static Entity GetNearestOtherCandidate(ElectionState state, int candidateIndex)
        {
            int candidateCount = state.ActiveCandidateCount;
            for (int i = 0; i < candidateCount; i++)
            {
                if (i != candidateIndex)
                    return state.GetCandidate(i);
            }

            return Entity.Null;
        }

        private static int GetNearestOtherCandidatePortraitIndex(ElectionState state, int candidateIndex)
        {
            int candidateCount = state.ActiveCandidateCount;
            for (int i = 0; i < candidateCount; i++)
            {
                if (i != candidateIndex)
                    return state.GetCandidatePortraitIndex(i);
            }

            return -1;
        }

        private void PostCandidateReplacementChirp(ElectionState state, int candidateIndex, string oldName, Entity replacement, string replacementName)
        {
            string slotName = ElectionState.GetCandidateFallbackName(candidateIndex);
            string pollDate = ElectionUtility.FormatDate(state.pollYear, state.pollMonth, 1);
            string electionDate = ElectionUtility.FormatDate(state.electionYear, state.electionMonth, 1);
            int inheritedDonation = state.GetCandidateDonation(candidateIndex);
            string donationText = inheritedDonation > 0
                ? LF("Lifecycle.Replacement.InheritedDonation", " Existing campaign donations totaling {0:n0} remain with this campaign.", inheritedDonation)
                : string.Empty;
            bool hasReplacementLink = IsValidChirpCitizen(replacement);
            string replacementReference = hasReplacementLink ? "{LINK_1}" : replacementName;
            string text = LF("Lifecycle.Replacement.Certified", "The Election Board says {0} can no longer participate because their resident record is no longer eligible. {1} is now certified for the mayoral race.{2} Poll: {3}. Election: {4}.", oldName, replacementReference, donationText, pollDate, electionDate);
            PostElectionChirp(text, hasReplacementLink ? replacement : Entity.Null);
            DebugLog($"Candidate replacement chirp posted: slot={slotName}, oldName={oldName}, replacement={DescribeEntity(replacement, replacementName)}, inheritedDonation={inheritedDonation:n0}, poll={pollDate}, election={electionDate}.");
        }

        private void ReportCandidateReplacementFailure(ElectionState state, DateTime now, int candidateIndex, Entity oldCandidate, string invalidReason)
        {
            string slotName = ElectionState.GetCandidateFallbackName(candidateIndex);
            string oldName = GetCandidateChirpName(oldCandidate);
            string report = $"Replacement failed at {ElectionUtility.FormatCurrentDate(World, now)}: slot={slotName}, old={DescribeEntity(oldCandidate, oldName)}, reason={invalidReason}, state={DescribeState(state)}.";

            if (report == m_LastCandidateReplacementFailureReport)
                return;

            m_LastCandidateReplacementFailureReport = report;
            DebugLog(report);
            PostElectionChirp(LF("Lifecycle.Replacement.NoEligible", "The Election Board found that {0} can no longer participate, but no eligible replacement candidate is currently available. The board will keep checking for an eligible resident.", oldName), Entity.Null);
        }

        private void PostRevisedPollChirp(ElectionState state, DateTime now)
        {
            ElectionPollSummary summary = BuildPollSummary(state);

            DebugLog($"Revised poll released after candidate replacement: date={ElectionUtility.FormatCurrentDate(World, now)}, total={summary.Total}, candidates={DescribePollResults(state, summary)}, undecided={summary.PercentUndecided}% ({state.pollUndecided}), marginOfError={summary.MarginOfError}, label={summary.Label}.");
            if (state.ActiveCandidateCount == 2)
            {
                PostElectionChirpWithCandidates(
                    LF("Lifecycle.Replacement.RevisedPoll.TwoLinks", "Because the candidate field changed, the Election Board has issued an updated campaign poll: {{LINK_1}} {0}%, {{LINK_2}} {1}%, undecided {2}% with a +/-{3}% margin of error. {4}.", summary.PercentA, summary.PercentB, summary.PercentUndecided, summary.MarginOfError, summary.Label),
                    state.candidateA,
                    state.candidateB);
                return;
            }

            PostElectionChirp(
                LF("Lifecycle.Replacement.RevisedPoll.Named", "Because the candidate field changed, the Election Board has issued an updated campaign poll: {0}, undecided {1}% with a +/-{2}% margin of error. {3}.", DescribePollResults(state, summary), summary.PercentUndecided, summary.MarginOfError, summary.Label),
                Entity.Null);
        }

        private void EnsureTemporaryMayor(ref ElectionState state, DateTime now)
        {
            if (IsValidResidentEntity(state.mayor) ||
                (state.democraticTransitionCompleted && IsExistingResidentMayorEntity(state.mayor)))
            {
                return;
            }

            if (!TryPickTemporaryMayor(now, out Entity mayor))
            {
                DebugLog($"Temporary mayor repair skipped at {ElectionUtility.FormatCurrentDate(World, now)}: no eligible resident found. Previous mayor={DescribeEntity(state.mayor, L("Lifecycle.Name.TemporaryMayor", "Temporary Mayor"))}");
                return;
            }

            bool transitionMayor = !state.democraticTransitionCompleted;
            state.mayor = mayor;
            if (transitionMayor)
            {
                state.mayorEffectId = 0;
                state.mayorNegativeSoftened = false;
                state.mayorTagId = ElectionCandidateTags.None;
                state.mayorPartyIndex = -1;
            }

            ElectionUtility.GetCurrentCalendarDate(World, now, out int year, out _, out _);
            if (state.mayorEffectTermYear <= 0)
                state.mayorEffectTermYear = year;

            if (transitionMayor)
                state.mayorMoneyApplied = false;

            string name = GetEntityName(mayor, L("Lifecycle.Name.TemporaryMayor", "Temporary Mayor"));
            string portraitImageSource = CandidatePortraitCatalog.GetPortraitImageSource(
                EntityManager,
                mayor,
                GetMayorPortraitIndex(state));
            string text = transitionMayor
                ? LF("Lifecycle.TemporaryMayor.Intro", "I am {{LINK_1}}. I will serve as temporary mayor under the Democratic Transition platform: no new city policy changes, only supervising the election process until residents choose a mayor.")
                : LF("Lifecycle.ActingMayor.Intro", "I am {{LINK_1}}. I will serve as acting mayor until the next scheduled mayoral election. The elected mayoral platform remains in effect.");
            DebugLog(transitionMayor
                ? $"Temporary mayor assigned: mayor={DescribeEntity(mayor, name)}, termYear={state.mayorEffectTermYear}."
                : $"Acting mayor assigned after democratic transition: mayor={DescribeEntity(mayor, name)}, preservedEffectId={state.mayorEffectId}, termYear={state.mayorEffectTermYear}.");

            if (!CustomChirpsBridge.PostLargeChirpFromEntityWithPortraitImage(text, mayor, mayor, portraitImageSource, name))
                CustomChirpsBridge.PostChirpFromEntity(text, mayor, mayor, name);
        }

        private bool IsExistingResidentMayorEntity(Entity entity)
        {
            if (entity == Entity.Null ||
                !EntityManager.Exists(entity) ||
                !EntityManager.HasComponent<Citizen>(entity) ||
                EntityManager.HasComponent<Deleted>(entity) ||
                EntityManager.HasComponent<Temp>(entity))
            {
                return false;
            }

            return ElectionUtility.IsEligibleResident(EntityManager, entity, EntityManager.GetComponentData<Citizen>(entity));
        }

        private void InitializeState(ref ElectionState state, DateTime now)
        {
            state.version = ElectionState.CurrentVersion;
            state.initialized = true;
            int month = ElectionUtility.CurrentCalendarMonth(World, now);

            if (month <= 7 && !state.democraticTransitionCompleted)
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
            PostElectionChirp(LF("Lifecycle.Initialize.Active", "Elections is active. The next regular mayoral campaign begins on {0}.", ElectionUtility.FormatDate(year, 10, 1)), Entity.Null);
        }

        private void ProcessNewDay(ref ElectionState state, DateTime now, int dayKey)
        {
            ElectionUtility.GetCurrentCalendarDate(World, now, out int year, out int month, out int day);
            DebugLog($"New election date processed: {ElectionUtility.FormatDate(year, month, day)}, dayKey={dayKey}, state={DescribeState(state)}.");

            ProcessCandidateCorruptionArrestCheck(ref state, now, dayKey);

            if (month == GetRegularCampaignStartMonth() && day == 1 && state.selectionYear != year)
            {
                StartCampaign(ref state, now, accelerated: false, reason: IsRunoffVotingEnabled() ? "regular September campaign start" : "regular October campaign start");
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

        private void ProcessPartyReputationEvents(ref ElectionState state, DateTime now, int dayKey)
        {
            if (!ArePartiesEnabled() ||
                ElectionUtility.MinuteOfDay(now) < kPartyReputationEventMinute ||
                state.partyReputationEventDayKey == dayKey)
            {
                return;
            }

            state.partyReputationEventDayKey = dayKey;
            int currentMilestoneLevel = GetAchievedMilestoneLevel();
            if (state.trackedMilestoneLevel < 0)
            {
                state.trackedMilestoneLevel = currentMilestoneLevel;
            }
            else if (currentMilestoneLevel > state.trackedMilestoneLevel)
            {
                TryApplyPartyMilestoneBonus(ref state, now, currentMilestoneLevel);
                state.trackedMilestoneLevel = currentMilestoneLevel;
            }

            Unity.Mathematics.Random random = CreateCampaignRandom(now, 121403 + dayKey);
            if (random.NextInt(100) >= kPartyReputationEventChancePercent)
            {
                DebugLog($"Party reputation event check cleared: dayKey={dayKey}, chance={kPartyReputationEventChancePercent}%.");
                return;
            }

            int partyIndex = PickPartyReputationEventTarget(state, ref random);
            if (!ElectionState.IsPartyIndex(partyIndex))
            {
                DebugLog($"Party reputation event skipped: no managed party target. dayKey={dayKey}.");
                return;
            }

            bool incumbent = partyIndex == state.mayorPartyIndex;
            int negativeChance = GetPartyNegativeEventChancePercent(state, partyIndex, incumbent);
            bool negative = random.NextInt(100) < negativeChance;
            if (negative)
                ApplyNegativePartyReputationEvent(ref state, partyIndex, incumbent, ref random);
            else
                ApplyPositivePartyReputationEvent(ref state, partyIndex, incumbent, ref random);
        }

        private void TryApplyPartyMilestoneBonus(ref ElectionState state, DateTime now, int currentMilestoneLevel)
        {
            ElectionUtility.GetCurrentCalendarDate(World, now, out int year, out _, out _);
            if (state.partyMilestoneBonusYear == year ||
                !IsManagedPartyIndex(state, state.mayorPartyIndex))
            {
                return;
            }

            int partyIndex = state.mayorPartyIndex;
            int before = state.GetPartyReputation(partyIndex);
            state.AddPartyReputation(partyIndex, kPartyMilestoneReputationGain);
            state.partyMilestoneBonusYear = year;
            string partyName = state.GetPartyName(partyIndex);
            DebugLog($"Party milestone reputation bonus: party={partyIndex}:{partyName}, milestone={currentMilestoneLevel}, reputation={before}->{state.GetPartyReputation(partyIndex)}, year={year}.");
            PostElectionChirp(LF("Lifecycle.Party.Milestone", "{0} received credit for the city's new milestone. Party reputation +{1}.", partyName, kPartyMilestoneReputationGain), Entity.Null);
        }

        private int PickPartyReputationEventTarget(ElectionState state, ref Unity.Mathematics.Random random)
        {
            int partyCount = GetManagedPartyCount(state);
            if (partyCount <= 0)
                return -1;

            int incumbent = IsManagedPartyIndex(state, state.mayorPartyIndex) ? state.mayorPartyIndex : -1;
            if (incumbent >= 0 && random.NextInt(100) < 60)
                return incumbent;

            if (partyCount == 1)
                return incumbent >= 0 ? incumbent : 0;

            int selected = random.NextInt(partyCount);
            if (incumbent >= 0 && selected == incumbent)
                selected = (selected + 1 + random.NextInt(partyCount - 1)) % partyCount;

            return selected;
        }

        private static int GetPartyNegativeEventChancePercent(ElectionState state, int partyIndex, bool incumbent)
        {
            int chance = incumbent ? 65 : 45;
            if (ElectionPartyTags.HasPartyTag(state, partyIndex, ElectionPartyTags.CivicTrust))
                chance -= 15;
            if (ElectionPartyTags.HasPartyTag(state, partyIndex, ElectionPartyTags.ScandalProne))
                chance += 15;
            if (incumbent && state.GetPartyConsecutiveTerms(partyIndex) > 1)
                chance += 5;
            return math.clamp(chance, 20, 90);
        }

        private void ApplyNegativePartyReputationEvent(ref ElectionState state, int partyIndex, bool incumbent, ref Unity.Mathematics.Random random)
        {
            bool major = random.NextInt(100) < (ElectionPartyTags.HasPartyTag(state, partyIndex, ElectionPartyTags.ScandalProne) ? 35 : 20);
            int delta = major
                ? ElectionPartyTags.GetScandalReputationDelta(state, partyIndex)
                : -kPartyMinorNegativeEventReputationLoss;
            int before = state.GetPartyReputation(partyIndex);
            state.AddPartyReputation(partyIndex, delta);

            string partyName = state.GetPartyName(partyIndex);
            string text = PickNegativePartyReputationEventText(partyName, delta, major, incumbent, ref random);
            DebugLog($"Party reputation event: party={partyIndex}:{partyName}, type={(major ? "major-negative" : "minor-negative")}, incumbent={incumbent}, delta={delta}, reputation={before}->{state.GetPartyReputation(partyIndex)}.");
            PostElectionChirp(text, Entity.Null);
        }

        private void ApplyPositivePartyReputationEvent(ref ElectionState state, int partyIndex, bool incumbent, ref Unity.Mathematics.Random random)
        {
            int before = state.GetPartyReputation(partyIndex);
            state.AddPartyReputation(partyIndex, kPartyPositiveEventReputationGain);

            string partyName = state.GetPartyName(partyIndex);
            string text = PickPositivePartyReputationEventText(partyName, kPartyPositiveEventReputationGain, incumbent, ref random);
            DebugLog($"Party reputation event: party={partyIndex}:{partyName}, type=positive, incumbent={incumbent}, delta={kPartyPositiveEventReputationGain}, reputation={before}->{state.GetPartyReputation(partyIndex)}.");
            PostElectionChirp(text, Entity.Null);
        }

        private static string PickNegativePartyReputationEventText(string partyName, int delta, bool major, bool incumbent, ref Unity.Mathematics.Random random)
        {
            int loss = math.abs(delta);
            if (major)
            {
                switch (random.NextInt(3))
                {
                    case 0:
                        return LF("Lifecycle.PartyEvent.Negative.Major.0", "A corruption scandal damaged {0}. Party reputation -{1}.", partyName, loss);
                    case 1:
                        return LF("Lifecycle.PartyEvent.Negative.Major.1", "Investigators raised serious ethics questions around {0}. Party reputation -{1}.", partyName, loss);
                    default:
                        return LF("Lifecycle.PartyEvent.Negative.Major.2", "A donor influence scandal put {0} under pressure. Party reputation -{1}.", partyName, loss);
                }
            }

            switch (random.NextInt(4))
            {
                case 0:
                    return LF("Lifecycle.PartyEvent.Negative.Minor.0", "Affair allegations involving senior {0} figures hurt public trust. Party reputation -{1}.", partyName, loss);
                case 1:
                    return LF("Lifecycle.PartyEvent.Negative.Minor.1", "{0} faced criticism over a donor disclosure mistake. Party reputation -{1}.", partyName, loss);
                case 2:
                    return LF("Lifecycle.PartyEvent.Negative.Minor.2", "A minor ethics complaint made headlines for {0}. Party reputation -{1}.", partyName, loss);
                default:
                    return incumbent
                        ? LF("Lifecycle.PartyEvent.Negative.Minor.Incumbent", "Residents blamed {0} for a messy City Hall decision. Party reputation -{1}.", partyName, loss)
                        : LF("Lifecycle.PartyEvent.Negative.Minor.Challenger", "{0} drew criticism for an unpopular campaign statement. Party reputation -{1}.", partyName, loss);
            }
        }

        private static string PickPositivePartyReputationEventText(string partyName, int gain, bool incumbent, ref Unity.Mathematics.Random random)
        {
            switch (random.NextInt(4))
            {
                case 0:
                    return LF("Lifecycle.PartyEvent.Positive.0", "A clean audit improved trust in {0}. Party reputation +{1}.", partyName, gain);
                case 1:
                    return LF("Lifecycle.PartyEvent.Positive.1", "{0} received praise for transparent public service work. Party reputation +{1}.", partyName, gain);
                case 2:
                    return incumbent
                        ? LF("Lifecycle.PartyEvent.Positive.Incumbent", "Residents credited {0} for a steady city response. Party reputation +{1}.", partyName, gain)
                        : LF("Lifecycle.PartyEvent.Positive.Challenger", "{0} gained attention for a constructive policy proposal. Party reputation +{1}.", partyName, gain);
                default:
                    return LF("Lifecycle.PartyEvent.Positive.3", "{0} benefited from positive local coverage. Party reputation +{1}.", partyName, gain);
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

            RepairDuplicateCandidateEffects(ref state, now);

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

            int requestedCandidateCount = GetConfiguredCandidateCount();
            Entity[] selectedCandidates = new Entity[requestedCandidateCount];
            if (ArePartiesEnabled())
                EnsurePartyState(ref state);

            if (!TryPickCandidates(now, state, state.mayor, requestedCandidateCount, selectedCandidates))
            {
                DebugLog($"StartCampaign failed: no eligible candidate field found. reason={reason}, requestedCandidateCount={requestedCandidateCount}.");
                PostElectionChirp(LF("Lifecycle.Campaign.NoEligibleCandidates", "The election board could not find {0} eligible adult residents for the mayoral race.", requestedCandidateCount), Entity.Null);
                return;
            }

            state.stage = ElectionCampaignStage.CandidatesSelected;
            state.acceleratedCycle = accelerated;
            state.runoffEnabledForCycle = !accelerated && IsRunoffVotingEnabled();
            state.runoffActive = false;
            state.runoffOriginalCandidateCount = requestedCandidateCount;
            ElectionUtility.GetCurrentCalendarDate(World, now, out int year, out int month, out _);
            state.selectionYear = year;
            state.selectionMonth = month;
            state.candidateCount = requestedCandidateCount;
            InitializeCampaignCandidates(ref state, now, selectedCandidates);
            AssignCampaignStandings(ref state, now);
            state.donationDayKey = 0;
            SetCampaignCostsFromMonthlyBalance(ref state);
            ResetPollState(ref state);
            state.voteRequests = 0;
            state.voteArrivals = 0;
            ResetCandidateVoteState(ref state);
            ResetVictoryPartyState(ref state);
            ResetBribeMeetingState(ref state, true);
            ResetMayorEndorsementState(ref state);
            ResetVoteTamperingState(ref state);
            ResetCandidateCorruptionRiskState(ref state);
            ResetMayorBribeTrackingState(ref state);
            ResetOutgoingMayorState(ref state);
            ResetPendingMayorState(ref state);
            ResetSupportProgramState(ref state);
            ResetCashAssistanceState(ref state);
            ResetStrictVotingIdProposalState(ref state);
            AssignCampaignPortraits(ref state);
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
                state.pollMonth = state.runoffEnabledForCycle ? 10 : 11;
                state.electionYear = state.runoffEnabledForCycle ? year : year + 1;
                state.electionMonth = state.runoffEnabledForCycle ? 11 : 1;
                state.mayorTermYear = year + 1;
            }

            EnsureElectionReminderChirpsScheduled(ref state);

            string pollDate = ElectionUtility.FormatDate(state.pollYear, state.pollMonth, 1);
            string electionDate = ElectionUtility.FormatDate(state.electionYear, state.electionMonth, 1);
            m_LastInvalidCandidateReport = null;
            DebugLog($"Campaign started: reason={reason}, date={ElectionUtility.FormatCurrentDate(World, now)}, accelerated={accelerated}, candidateCount={state.ActiveCandidateCount}, candidates={DescribeCandidates(state)}, effects={DescribeCandidateEffects(state)}, tags={DescribeCandidateTags(state)}, standings={DescribeCandidateStandings(state)}, portraits={DescribeCandidatePortraits(state)}, donationAmount={state.campaignDonationAmount:n0}, bribeAmount={state.campaignBribeAmount:n0}, poll={pollDate}, election={electionDate}, state={DescribeState(state)}.");

            PostCampaignStartedChirp(state, pollDate, electionDate);
        }

        private static int GetConfiguredCandidateCount()
        {
            return ElectionState.NormalizeCandidateCount(Mod.m_Setting?.CandidateCount ?? ElectionState.DefaultCandidateCount);
        }

        private void InitializeCampaignCandidates(ref ElectionState state, DateTime now, Entity[] selectedCandidates)
        {
            int candidateCount = ElectionState.NormalizeCandidateCount(selectedCandidates?.Length ?? ElectionState.DefaultCandidateCount);
            state.candidateCount = candidateCount;
            for (int i = 0; i < ElectionState.MaxCandidateCount; i++)
            {
                Entity candidate = i < candidateCount ? selectedCandidates[i] : Entity.Null;
                state.SetCandidate(i, candidate);
                state.SetCandidateEffectId(i, candidate == Entity.Null ? 0 : PickUniqueCandidateEffect(state, candidate, now, i));
                if (candidate != Entity.Null)
                {
                    CaptureCandidateProfile(candidate, out int age, out int education, out int workType, out int wealth);
                    state.SetCandidateProfile(i, age, education, workType, wealth);
                    state.SetCandidateTagId(i, PickUniqueCandidateTag(state, i, candidate, age, education, now));
                }
                else
                {
                    state.SetCandidateProfile(i, 0, 0, 0, 0);
                    state.SetCandidateTagId(i, ElectionCandidateTags.None);
                }

                state.SetCandidateDonation(i, 0);
                state.SetCandidateSupportModifierPercent(i, 0);
                state.SetCandidateNegativeSoftened(i, false);
                state.SetCandidateSoftenAttempted(i, false);
                state.SetCandidatePollResponseChirpSent(i, true);
                state.SetCandidatePollResponseChirpUtcTicks(i, 0);
                state.SetCandidatePlatformChirpSent(i, true);
                state.SetCandidatePlatformChirpUtcTicks(i, 0);
                state.SetCandidatePlatformChirpDayKey(i, 0);
                state.SetCandidatePlatformChirpMinute(i, 0);
                state.SetCandidateElectionReminderChirpSent(i, true);
                state.SetCandidateElectionReminderChirpDayKey(i, 0);
                state.SetCandidateElectionReminderChirpMinute(i, 0);
                ClearPendingDonationChirp(i);
            }
        }

        private void AssignCampaignStandings(ref ElectionState state, DateTime now)
        {
            int candidateCount = state.ActiveCandidateCount;
            if (candidateCount < ElectionState.MinCandidateCount)
            {
                for (int i = 0; i < ElectionState.MaxCandidateCount; i++)
                    state.SetCandidateSupportModifierPercent(i, 0);
                return;
            }

            Unity.Mathematics.Random random = CreateCampaignRandom(now, 194317 + candidateCount * 7919);
            int[] standingValues = CreateCampaignStandingShape(candidateCount, ref random);
            int[] order = { 0, 1, 2, 3 };
            int[] scores = new int[ElectionState.MaxCandidateCount];

            for (int i = 0; i < candidateCount; i++)
                scores[i] = GetCandidateStandingScore(state, i, ref random);

            SortCandidateOrderByScore(order, scores, candidateCount);
            SortDescending(standingValues, candidateCount);

            for (int rank = 0; rank < candidateCount; rank++)
                state.SetCandidateSupportModifierPercent(order[rank], standingValues[rank]);

            for (int i = candidateCount; i < ElectionState.MaxCandidateCount; i++)
                state.SetCandidateSupportModifierPercent(i, 0);

            DebugLog($"Campaign standings assigned: scores={FormatIntList(scores)}, standings={DescribeCandidateStandings(state)}.");
        }

        private int GetCandidateStandingScore(ElectionState state, int candidateIndex, ref Unity.Mathematics.Random random)
        {
            int score = random.NextInt(0, 100);
            score += GetCandidateTagStandingScore(state.GetCandidateTagId(candidateIndex));
            score += state.GetCandidateEducation(candidateIndex) * 2;
            score += state.GetCandidateWealth(candidateIndex);

            if (ArePartiesEnabled())
            {
                int partyIndex = state.GetCandidatePartyIndex(candidateIndex);
                if (ElectionState.IsPartyIndex(partyIndex))
                {
                    score += state.GetPartyReputation(partyIndex) - ElectionPartyTags.DefaultReputation;
                    if (partyIndex == state.mayorPartyIndex)
                        score += 6;

                    int consecutiveTerms = state.GetPartyConsecutiveTerms(partyIndex);
                    if (consecutiveTerms > 1)
                        score -= (consecutiveTerms - 1) * 8;
                }
            }

            return score;
        }

        private static int GetCandidateTagStandingScore(int tagId)
        {
            switch (ElectionCandidateTags.NormalizeId(tagId))
            {
                case ElectionCandidateTags.Charismatic:
                    return 18;
                case ElectionCandidateTags.Grassroots:
                case ElectionCandidateTags.Fundraiser:
                    return 10;
                case ElectionCandidateTags.EliteConnections:
                    return 8;
                case ElectionCandidateTags.Honest:
                    return 6;
                case ElectionCandidateTags.Corrupt:
                    return -8;
                case ElectionCandidateTags.Cautious:
                    return -10;
                case ElectionCandidateTags.PoorSpeaker:
                    return -18;
                case ElectionCandidateTags.ControversialPast:
                    return -22;
            }

            ElectionCandidateTagTone tone = ElectionCandidateTags.Get(tagId).Tone;
            if (tone == ElectionCandidateTagTone.Advantage)
                return 8;
            if (tone == ElectionCandidateTagTone.Disadvantage)
                return -8;
            return 0;
        }

        private static int[] CreateCampaignStandingShape(int candidateCount, ref Unity.Mathematics.Random random)
        {
            int[] values = new int[ElectionState.MaxCandidateCount];
            int shape = random.NextInt(0, 100);

            if (candidateCount <= 2)
            {
                if (shape < 70)
                {
                    values[0] = RandomRangeInclusive(ref random, 10, 22);
                    values[1] = -RandomRangeInclusive(ref random, 10, 22);
                }
                else if (shape < 90)
                {
                    values[0] = RandomRangeInclusive(ref random, 22, 36);
                    values[1] = -RandomRangeInclusive(ref random, 16, 30);
                }
                else
                {
                    values[0] = RandomRangeInclusive(ref random, -5, 9);
                    values[1] = RandomRangeInclusive(ref random, -7, 7);
                }
            }
            else if (candidateCount == 3)
            {
                if (shape < 70)
                {
                    values[0] = RandomRangeInclusive(ref random, 16, 30);
                    values[1] = RandomRangeInclusive(ref random, -6, 8);
                    values[2] = -RandomRangeInclusive(ref random, 25, 38);
                }
                else if (shape < 90)
                {
                    values[0] = RandomRangeInclusive(ref random, 28, 40);
                    values[1] = RandomRangeInclusive(ref random, 6, 18);
                    values[2] = -RandomRangeInclusive(ref random, 30, 40);
                }
                else
                {
                    values[0] = RandomRangeInclusive(ref random, -4, 12);
                    values[1] = RandomRangeInclusive(ref random, -6, 10);
                    values[2] = RandomRangeInclusive(ref random, -10, 8);
                }
            }
            else
            {
                if (shape < 65)
                {
                    values[0] = RandomRangeInclusive(ref random, 18, 30);
                    values[1] = RandomRangeInclusive(ref random, 10, 22);
                    values[2] = RandomRangeInclusive(ref random, -8, 4);
                    values[3] = -RandomRangeInclusive(ref random, 28, 40);
                }
                else if (shape < 85)
                {
                    values[0] = RandomRangeInclusive(ref random, 28, 40);
                    values[1] = RandomRangeInclusive(ref random, 2, 14);
                    values[2] = RandomRangeInclusive(ref random, -10, 2);
                    values[3] = -RandomRangeInclusive(ref random, 30, 40);
                }
                else if (shape < 95)
                {
                    values[0] = RandomRangeInclusive(ref random, 14, 24);
                    values[1] = RandomRangeInclusive(ref random, 9, 20);
                    values[2] = RandomRangeInclusive(ref random, 2, 12);
                    values[3] = -RandomRangeInclusive(ref random, 25, 36);
                }
                else
                {
                    values[0] = RandomRangeInclusive(ref random, -4, 10);
                    values[1] = RandomRangeInclusive(ref random, -6, 8);
                    values[2] = RandomRangeInclusive(ref random, -8, 6);
                    values[3] = RandomRangeInclusive(ref random, -12, 4);
                }
            }

            return values;
        }

        private static int RandomRangeInclusive(ref Unity.Mathematics.Random random, int min, int max)
        {
            return random.NextInt(min, max + 1);
        }

        private static void SortCandidateOrderByScore(int[] order, int[] scores, int count)
        {
            for (int i = 0; i < count - 1; i++)
            {
                for (int j = i + 1; j < count; j++)
                {
                    if (scores[order[j]] > scores[order[i]])
                    {
                        int temp = order[i];
                        order[i] = order[j];
                        order[j] = temp;
                    }
                }
            }
        }

        private static void SortDescending(int[] values, int count)
        {
            for (int i = 0; i < count - 1; i++)
            {
                for (int j = i + 1; j < count; j++)
                {
                    if (values[j] > values[i])
                    {
                        int temp = values[i];
                        values[i] = values[j];
                        values[j] = temp;
                    }
                }
            }
        }

        private int PickUniqueCandidateEffect(ElectionState state, Entity candidate, DateTime now, int candidateIndex)
        {
            int effectId = PickEffect(candidate, now, 17 + candidateIndex * 7901);
            for (int attempt = 0; attempt < 32 && HasSamePlatformAsPreviousCandidate(state, candidateIndex, effectId); attempt++)
                effectId = PickEffect(candidate, now, 104729 + candidateIndex * 1543 + attempt * 7919 + effectId);

            return effectId;
        }

        private static bool HasSamePlatformAsPreviousCandidate(ElectionState state, int candidateIndex, int effectId)
        {
            for (int i = 0; i < candidateIndex; i++)
            {
                if (ElectionEffects.HasSamePlatform(effectId, state.GetCandidateEffectId(i)))
                    return true;
            }

            return false;
        }

        private static bool HasSamePlatformAsOtherCandidate(ElectionState state, int candidateIndex, int effectId)
        {
            int candidateCount = state.ActiveCandidateCount;
            for (int i = 0; i < candidateCount; i++)
            {
                if (i != candidateIndex && ElectionEffects.HasSamePlatform(effectId, state.GetCandidateEffectId(i)))
                    return true;
            }

            return false;
        }

        private int PickUniqueCandidateTag(ElectionState state, int candidateIndex, Entity candidate, int age, int education, DateTime now)
        {
            int excludedTagId = candidateIndex > 0 ? state.GetCandidateTagId(candidateIndex - 1) : ElectionCandidateTags.None;
            int tagId = PickCandidateTag(candidate, age, education, now, 2879 + candidateIndex * 2513, excludedTagId);
            for (int i = 0; i < candidateIndex; i++)
            {
                if (tagId != ElectionCandidateTags.None && tagId == state.GetCandidateTagId(i))
                    return ElectionCandidateTags.None;
            }

            return tagId;
        }

        private void AssignCampaignPortraits(ref ElectionState state)
        {
            int mayorPortraitIndex = GetBaseMayorPortraitIndex(state);
            int candidateCount = state.ActiveCandidateCount;
            for (int i = 0; i < candidateCount; i++)
            {
                Entity excludedCandidate = i > 0 ? state.GetCandidate(i - 1) : Entity.Null;
                int excludedPortrait = i > 0 ? state.GetCandidatePortraitIndex(i - 1) : -1;
                state.SetCandidatePortraitIndex(
                    i,
                    PickDistinctPortraitIndex(
                        state.GetCandidate(i),
                        17 + i * 7901,
                        state.mayor,
                        mayorPortraitIndex,
                        excludedCandidate,
                        excludedPortrait));
            }

            for (int i = candidateCount; i < ElectionState.MaxCandidateCount; i++)
                state.SetCandidatePortraitIndex(i, -1);

            EnsureDistinctCampaignPortraits(ref state);
        }

        private static void ResetCandidateVoteState(ref ElectionState state)
        {
            for (int i = 0; i < ElectionState.MaxCandidateCount; i++)
            {
                state.SetCandidateVotes(i, 0);
                state.SetCandidateVotedChirpSent(i, false);
            }
        }

        private void PostCampaignStartedChirp(ElectionState state, string pollDate, string electionDate)
        {
            int candidateCount = state.ActiveCandidateCount;
            if (candidateCount == 2)
            {
                PostElectionChirpWithCandidates(
                    LF("Lifecycle.Campaign.Started.TwoLinks", "The mayoral race has begun. The candidates are {{LINK_1}} and {{LINK_2}}. Poll: {0}. Election: {1}.", pollDate, electionDate),
                    state.candidateA,
                    state.candidateB);
                return;
            }

            PostElectionChirp(
                LF("Lifecycle.Campaign.Started.Named", "The mayoral race has begun with {0} candidates: {1}. Poll: {2}. Election: {3}.", candidateCount, GetCandidateNamesText(state), pollDate, electionDate),
                Entity.Null);
        }

        private string GetCandidateNamesText(ElectionState state)
        {
            int candidateCount = state.ActiveCandidateCount;
            string result = string.Empty;
            for (int i = 0; i < candidateCount; i++)
            {
                if (i > 0)
                    result += i == candidateCount - 1 ? L("Lifecycle.List.And", " and ") : ", ";
                result += GetCandidateChirpName(state, i);
            }

            return result;
        }

        private string DescribeCandidates(ElectionState state)
        {
            int candidateCount = state.ActiveCandidateCount;
            string result = string.Empty;
            for (int i = 0; i < candidateCount; i++)
            {
                if (i > 0)
                    result += "; ";
                result += $"{i}:{DescribeEntity(state.GetCandidate(i), ElectionState.GetCandidateFallbackName(i))}";
            }

            return result;
        }

        private static string DescribeCandidateEffects(ElectionState state)
        {
            int candidateCount = state.ActiveCandidateCount;
            string result = string.Empty;
            for (int i = 0; i < candidateCount; i++)
            {
                if (i > 0)
                    result += "/";
                result += state.GetCandidateEffectId(i).ToString();
            }

            return result;
        }

        private static string DescribeCandidateTags(ElectionState state)
        {
            int candidateCount = state.ActiveCandidateCount;
            string result = string.Empty;
            for (int i = 0; i < candidateCount; i++)
            {
                if (i > 0)
                    result += "/";
                result += ElectionCandidateTags.Get(state.GetCandidateTagId(i)).Name;
            }

            return result;
        }

        private static string DescribeCandidatePortraits(ElectionState state)
        {
            int candidateCount = state.ActiveCandidateCount;
            string result = string.Empty;
            for (int i = 0; i < candidateCount; i++)
            {
                if (i > 0)
                    result += "/";
                result += state.GetCandidatePortraitIndex(i).ToString();
            }

            return result;
        }

        private static string DescribeCandidateStandings(ElectionState state)
        {
            int candidateCount = state.ActiveCandidateCount;
            string result = string.Empty;
            for (int i = 0; i < candidateCount; i++)
            {
                if (i > 0)
                    result += "/";
                int value = state.GetCandidateSupportModifierPercent(i);
                result += value > 0 ? $"+{value}%" : $"{value}%";
            }

            return result;
        }

        private void SchedulePlatformChirps(ref ElectionState state, DateTime now)
        {
            DateTime utcNow = DateTime.UtcNow;
            int candidateCount = state.ActiveCandidateCount;
            for (int i = 0; i < candidateCount; i++)
            {
                state.SetCandidatePlatformChirpSent(i, false);
                state.SetCandidatePlatformChirpUtcTicks(i, utcNow.AddMinutes(2 + i * 2).Ticks);
                state.SetCandidatePlatformChirpDayKey(i, 0);
                state.SetCandidatePlatformChirpMinute(i, 0);
            }

            DebugLog($"Scheduled platform chirps for {candidateCount} candidates: {DescribeCandidates(state)}.");
        }

        private void ProcessScheduledPlatformChirps(ref ElectionState state, DateTime now)
        {
            if (!state.HasCandidates || state.stage == ElectionCampaignStage.None || state.stage == ElectionCampaignStage.Voting)
                return;

            long utcNowTicks = DateTime.UtcNow.Ticks;
            int candidateCount = state.ActiveCandidateCount;
            for (int i = 0; i < candidateCount; i++)
            {
                if (!state.GetCandidatePlatformChirpSent(i) &&
                    IsPlatformChirpDue(now, utcNowTicks, state.GetCandidatePlatformChirpUtcTicks(i), state.GetCandidatePlatformChirpDayKey(i), state.GetCandidatePlatformChirpMinute(i)))
                {
                    DebugLog($"Posting scheduled platform chirp for {ElectionState.GetCandidateFallbackName(i)}: {DescribeEntity(state.GetCandidate(i), ElectionState.GetCandidateFallbackName(i))}.");
                    PostCandidatePlatformChirp(state, i);
                    state.SetCandidatePlatformChirpSent(i, true);
                }
            }
        }

        private static bool IsPlatformChirpDue(DateTime now, long utcNowTicks, long dueUtcTicks, int legacyDayKey, int legacyMinute)
        {
            if (dueUtcTicks > 0)
                return utcNowTicks >= dueUtcTicks;

            return legacyDayKey == 0 || ElectionUtility.IsAtOrAfter(now, legacyDayKey, legacyMinute);
        }

        private void PostCandidatePlatformChirp(ElectionState state, int candidateIndex)
        {
            Entity candidate = state.GetCandidate(candidateIndex);
            int portraitIndex = state.GetCandidatePortraitIndex(candidateIndex);
            string fallbackName = ElectionState.GetCandidateFallbackName(candidateIndex);

            if (candidate == Entity.Null || !EntityManager.Exists(candidate))
            {
                DebugLog($"Skipped platform chirp for {fallbackName}: candidate entity is not available ({FormatEntity(candidate)}).");
                return;
            }

            int effectId = state.GetCandidateEffectId(candidateIndex);
            ElectionEffectDefinition effect = GetCandidateEffectDefinition(state, candidateIndex);
            string name = GetCandidateChirpName(candidate);
            string portraitImageSource = CandidatePortraitCatalog.GetPortraitImageSource(EntityManager, candidate, portraitIndex);
            string profileIntro = GetCandidateProfileIntro(state, candidateIndex);
            string text = LF("Lifecycle.Platform.CandidateIntro", "I am {{LINK_1}}, {0}. My platform {1}.", profileIntro, effect.Description);
            DebugLog($"Candidate platform chirp posted: candidate={DescribeEntity(candidate, fallbackName)}, effectId={effectId}, effect={effect.Name}, portraitIndex={portraitIndex}.");

            if (!CustomChirpsBridge.PostLargeChirpFromEntityWithPortraitImage(text, candidate, candidate, portraitImageSource, name))
                CustomChirpsBridge.PostChirpFromEntity(text, candidate, candidate, name);
        }

        private void ReleasePoll(ref ElectionState state, DateTime now)
        {
            if (!state.HasCandidates)
            {
                DebugLog("Poll release skipped: state has no candidate field.");
                return;
            }

            RunPoll(ref state, now);
            state.stage = ElectionCampaignStage.PollReleased;

            ElectionPollSummary summary = BuildPollSummary(state);
            DebugLog($"Poll released: date={ElectionUtility.FormatCurrentDate(World, now)}, total={summary.Total}, candidates={DescribePollResults(state, summary)}, undecided={summary.PercentUndecided}% ({state.pollUndecided}), marginOfError={summary.MarginOfError}, label={summary.Label}.");

            PostPollReleasedChirp(state, now, summary);

            SchedulePollResponseChirps(ref state);
        }

        private ElectionPollSummary BuildPollSummary(ElectionState state)
        {
            return ElectionPollUtility.BuildSummary(
                state.pollVotesA,
                state.pollVotesB,
                state.pollVotesC,
                state.pollVotesD,
                state.pollUndecided,
                state.ActiveCandidateCount,
                GetCandidateChirpName(state.candidateA),
                GetCandidateChirpName(state.candidateB),
                GetCandidateChirpName(state.candidateC),
                GetCandidateChirpName(state.candidateD));
        }

        private ElectionPollSummary BuildPollSummary(ElectionState state, int votesA, int votesB, int votesC, int votesD, int undecided)
        {
            return ElectionPollUtility.BuildSummary(
                votesA,
                votesB,
                votesC,
                votesD,
                undecided,
                state.ActiveCandidateCount,
                GetCandidateChirpName(state.candidateA),
                GetCandidateChirpName(state.candidateB),
                GetCandidateChirpName(state.candidateC),
                GetCandidateChirpName(state.candidateD));
        }

        private void PostPollReleasedChirp(ElectionState state, DateTime now, ElectionPollSummary summary)
        {
            string date = ElectionUtility.FormatCurrentDate(World, now);
            if (state.ActiveCandidateCount == 2)
            {
                PostElectionChirpWithCandidates(
                    LF("Lifecycle.Poll.Release.TwoLinks", "Campaign poll released on {0} from {1:n0} sampled eligible residents: {{LINK_1}} {2}%, {{LINK_2}} {3}%, undecided {4}% with a +/-{5}% margin of error. {6}. Donations remain available in the Elections options panel.", date, summary.Total, summary.PercentA, summary.PercentB, summary.PercentUndecided, summary.MarginOfError, summary.Label),
                    state.candidateA,
                    state.candidateB);
                return;
            }

            PostElectionChirp(
                LF("Lifecycle.Poll.Release.Named", "Campaign poll released on {0} from {1:n0} sampled eligible residents: {2}, undecided {3}% with a +/-{4}% margin of error. {5}. Donations remain available in the Elections options panel.", date, summary.Total, DescribePollResults(state, summary), summary.PercentUndecided, summary.MarginOfError, summary.Label),
                Entity.Null);
        }

        private string DescribePollResults(ElectionState state, ElectionPollSummary summary)
        {
            int candidateCount = state.ActiveCandidateCount;
            string result = string.Empty;
            for (int i = 0; i < candidateCount; i++)
            {
                if (i > 0)
                    result += ", ";
                result += $"{GetCandidateChirpName(state, i)} {GetPollPercent(summary, i)}% ({state.GetCandidatePollVotes(i)})";
            }

            return result;
        }

        private static int GetPollLeaderIndex(ElectionState state)
        {
            int candidateCount = state.ActiveCandidateCount;
            int leaderIndex = -1;
            int leaderVotes = -1;
            bool tied = false;
            for (int i = 0; i < candidateCount; i++)
            {
                int votes = state.GetCandidatePollVotes(i);
                if (votes > leaderVotes)
                {
                    leaderVotes = votes;
                    leaderIndex = i;
                    tied = false;
                }
                else if (votes == leaderVotes)
                {
                    tied = true;
                }
            }

            return tied ? -1 : leaderIndex;
        }

        private static int GetPollPercent(ElectionPollSummary summary, int candidateIndex)
        {
            switch (candidateIndex)
            {
                case 0:
                    return summary.PercentA;
                case 1:
                    return summary.PercentB;
                case 2:
                    return summary.PercentC;
                case 3:
                    return summary.PercentD;
                default:
                    return 0;
            }
        }

        private void SchedulePollResponseChirps(ref ElectionState state)
        {
            DateTime utcNow = DateTime.UtcNow;
            int candidateCount = state.ActiveCandidateCount;
            for (int i = 0; i < candidateCount; i++)
            {
                state.SetCandidatePollResponseChirpSent(i, false);
                state.SetCandidatePollResponseChirpUtcTicks(i, utcNow.AddMinutes(2 + i * 2).Ticks);
            }

            DebugLog($"Scheduled poll response chirps for {candidateCount} candidates.");
        }

        private void ProcessScheduledPollResponseChirps(ref ElectionState state)
        {
            if (!state.HasCandidates || state.stage != ElectionCampaignStage.PollReleased)
                return;

            long utcNowTicks = DateTime.UtcNow.Ticks;
            int candidateCount = state.ActiveCandidateCount;
            for (int i = 0; i < candidateCount; i++)
            {
                if (!state.GetCandidatePollResponseChirpSent(i) &&
                    state.GetCandidatePollResponseChirpUtcTicks(i) > 0 &&
                    utcNowTicks >= state.GetCandidatePollResponseChirpUtcTicks(i))
                {
                    DebugLog($"Posting scheduled poll response chirp for {ElectionState.GetCandidateFallbackName(i)}: {DescribeEntity(state.GetCandidate(i), ElectionState.GetCandidateFallbackName(i))}.");
                    PostCandidatePollResponseChirp(state, i);
                    state.SetCandidatePollResponseChirpSent(i, true);
                }
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

            int candidateCount = state.ActiveCandidateCount;
            for (int i = 0; i < candidateCount; i++)
            {
                if (state.GetCandidateElectionReminderChirpDayKey(i) != reminderDayKey ||
                    state.GetCandidateElectionReminderChirpMinute(i) != reminderMinute)
                {
                    state.SetCandidateElectionReminderChirpSent(i, false);
                    state.SetCandidateElectionReminderChirpDayKey(i, reminderDayKey);
                    state.SetCandidateElectionReminderChirpMinute(i, reminderMinute);
                }
            }
        }

        private void ProcessScheduledElectionReminderChirps(ref ElectionState state, DateTime now)
        {
            if (!state.HasCandidates || state.stage == ElectionCampaignStage.None || state.stage == ElectionCampaignStage.Voting)
                return;

            EnsureElectionReminderChirpsScheduled(ref state);

            int candidateCount = state.ActiveCandidateCount;
            for (int i = 0; i < candidateCount; i++)
            {
                if (!state.GetCandidateElectionReminderChirpSent(i) &&
                    state.GetCandidateElectionReminderChirpDayKey(i) > 0 &&
                    IsCurrentCalendarAtOrAfter(now, state.GetCandidateElectionReminderChirpDayKey(i), state.GetCandidateElectionReminderChirpMinute(i)))
                {
                    DebugLog($"Posting election reminder chirp for {ElectionState.GetCandidateFallbackName(i)}: {DescribeEntity(state.GetCandidate(i), ElectionState.GetCandidateFallbackName(i))}.");
                    PostCandidateElectionReminderChirp(state, i, now);
                    state.SetCandidateElectionReminderChirpSent(i, true);
                }
            }
        }

        private void PostCandidatePollResponseChirp(ElectionState state, int candidateIndex)
        {
            Entity candidate = state.GetCandidate(candidateIndex);
            string fallbackName = ElectionState.GetCandidateFallbackName(candidateIndex);
            if (candidate == Entity.Null || !EntityManager.Exists(candidate))
            {
                DebugLog($"Skipped poll response chirp for {fallbackName}: candidate entity is not available ({FormatEntity(candidate)}).");
                return;
            }

            int ownVotes = state.GetCandidatePollVotes(candidateIndex);
            int leaderIndex = GetPollLeaderIndex(state);
            Entity opponent = leaderIndex >= 0 && leaderIndex != candidateIndex ? state.GetCandidate(leaderIndex) : Entity.Null;
            int opponentVotes = leaderIndex >= 0 ? state.GetCandidatePollVotes(leaderIndex) : ownVotes;
            string name = GetCandidateChirpName(candidate);
            string opponentName = leaderIndex >= 0
                ? GetCandidateChirpName(state, leaderIndex)
                : L("Lifecycle.Name.Field", "the field");
            int portraitIndex = state.GetCandidatePortraitIndex(candidateIndex);
            string portraitImageSource = CandidatePortraitCatalog.GetPortraitImageSource(EntityManager, candidate, portraitIndex);
            DebugLog($"Candidate poll response chirp posted: candidate={DescribeEntity(candidate, fallbackName)}, opponent={DescribeEntity(opponent, opponentName)}, ownVotes={ownVotes}, opponentVotes={opponentVotes}, undecided={state.pollUndecided}, effectId={state.GetCandidateEffectId(candidateIndex)}, portraitIndex={portraitIndex}.");

            ElectionPollSummary summary = BuildPollSummary(state);
            bool hasOpponentLink = leaderIndex >= 0 && leaderIndex != candidateIndex && IsValidChirpCitizen(opponent);
            string resultComment = BuildPollResponseComment(ownVotes, opponentVotes, summary, hasOpponentLink ? "{LINK_2}" : opponentName);
            string fallbackResultComment = hasOpponentLink
                ? BuildPollResponseComment(ownVotes, opponentVotes, summary, opponentName)
                : resultComment;

            string text = LF("Lifecycle.PollResponse.WithCta", "{0} Donations are open, and every contribution helps move this race.", resultComment);
            string fallbackText = LF("Lifecycle.PollResponse.WithCta", "{0} Donations are open, and every contribution helps move this race.", fallbackResultComment);

            bool posted = hasOpponentLink &&
                CustomChirpsBridge.PostLargeChirpFromEntityWithPortraitImage(text, candidate, candidate, opponent, portraitImageSource, name);
            if (!posted && !CustomChirpsBridge.PostLargeChirpFromEntityWithPortraitImage(fallbackText, candidate, candidate, portraitImageSource, name))
                CustomChirpsBridge.PostChirpFromEntity(fallbackText, candidate, candidate, name);
        }

        private static string BuildPollResponseComment(int ownVotes, int opponentVotes, ElectionPollSummary summary, string opponentReference)
        {
            if (summary.WithinMargin)
                return LF("Lifecycle.PollResponse.DeadHeat", "The latest poll is a statistical dead heat against {0}.", opponentReference);
            if (ownVotes > opponentVotes)
                return LF("Lifecycle.PollResponse.Ahead", "The latest poll has us ahead of {0}, outside the +/-{1}% margin of error.", opponentReference, summary.MarginOfError);
            if (ownVotes < opponentVotes)
                return LF("Lifecycle.PollResponse.Behind", "The latest poll has us behind {0}, but undecided voters can still move this race.", opponentReference);

            return LF("Lifecycle.PollResponse.Tied", "The latest poll has us tied with {0}.", opponentReference);
        }

        private void PostCandidateElectionReminderChirp(ElectionState state, int candidateIndex, DateTime now)
        {
            Entity candidate = state.GetCandidate(candidateIndex);
            int opponentIndex = GetReminderOpponentIndex(state, candidateIndex);
            Entity opponent = opponentIndex >= 0 ? state.GetCandidate(opponentIndex) : Entity.Null;
            string fallbackName = ElectionState.GetCandidateFallbackName(candidateIndex);
            string opponentFallbackName = opponentIndex >= 0 ? ElectionState.GetCandidateFallbackName(opponentIndex) : L("Lifecycle.Name.Field", "the field");
            int portraitIndex = state.GetCandidatePortraitIndex(candidateIndex);
            int effectId = state.GetCandidateEffectId(candidateIndex);
            bool negativeSoftened = state.GetCandidateNegativeSoftened(candidateIndex);
            int opponentEffectId = opponentIndex >= 0 ? state.GetCandidateEffectId(opponentIndex) : 0;
            bool opponentNegativeSoftened = opponentIndex >= 0 && state.GetCandidateNegativeSoftened(opponentIndex);

            if (candidate == Entity.Null || !EntityManager.Exists(candidate))
            {
                DebugLog($"Skipped election reminder chirp for {fallbackName}: candidate entity is not available ({FormatEntity(candidate)}).");
                return;
            }

            string name = GetCandidateChirpName(candidate);
            string opponentName = opponentIndex >= 0 ? GetCandidateChirpName(state, opponentIndex) : opponentFallbackName;
            ElectionEffectDefinition effect = GetCandidateEffectDefinition(state, candidateIndex, effectId, negativeSoftened);
            ElectionEffectDefinition opponentEffect = opponentIndex >= 0
                ? GetCandidateEffectDefinition(state, opponentIndex, opponentEffectId, opponentNegativeSoftened)
                : ElectionEffects.Get(opponentEffectId);
            string profileIntro = GetCandidateProfileIntro(state, candidateIndex);
            string portraitImageSource = CandidatePortraitCatalog.GetPortraitImageSource(EntityManager, candidate, portraitIndex);
            bool hasOpponentLink = IsValidChirpCitizen(opponent);
            string text = PickElectionReminderMessage(state, candidateIndex, now, profileIntro, hasOpponentLink ? "{LINK_2}" : opponentName, effect, opponentEffect);
            string fallbackText = hasOpponentLink
                ? PickElectionReminderMessage(state, candidateIndex, now, profileIntro, opponentName, effect, opponentEffect)
                : text;

            DebugLog($"Election reminder chirp posted: candidate={DescribeEntity(candidate, fallbackName)}, opponent={DescribeEntity(opponent, opponentFallbackName)}, effectId={effectId}, opponentEffectId={opponentEffectId}, portraitIndex={portraitIndex}.");
            bool posted = hasOpponentLink &&
                CustomChirpsBridge.PostLargeChirpFromEntityWithPortraitImage(text, candidate, candidate, opponent, portraitImageSource, name);
            if (!posted && !CustomChirpsBridge.PostLargeChirpFromEntityWithPortraitImage(fallbackText, candidate, candidate, portraitImageSource, name))
                CustomChirpsBridge.PostChirpFromEntity(fallbackText, candidate, candidate, name);
        }

        private string PickElectionReminderMessage(
            ElectionState state,
            int candidateIndex,
            DateTime now,
            string profileIntro,
            string opponentReference,
            ElectionEffectDefinition effect,
            ElectionEffectDefinition opponentEffect)
        {
            int seed = math.abs(
                state.electionYear * 10000 +
                state.electionMonth * 257 +
                candidateIndex * 7919 +
                GetCandidateEffectSeed(state) +
                now.Day);
            Unity.Mathematics.Random random = Unity.Mathematics.Random.CreateFromIndex((uint)math.max(1, seed));
            int template = random.NextInt(6);
            string votingWindow = GetUpcomingVotingWindowText(state);

            switch (template)
            {
                case 0:
                    return LF("Lifecycle.Reminder.0", "Tomorrow is election day. Polls are open from {0}, and I am asking for your vote.", votingWindow);
                case 1:
                    return LF("Lifecycle.Reminder.1", "Tomorrow, residents choose the next mayor. My platform {0}, and every vote can shape the city.", effect.PositiveImpact.Sentence);
                case 2:
                    return LF("Lifecycle.Reminder.2", "Make a plan to vote tomorrow from {0}. This race is about {1}, and your voice matters.", votingWindow, effect.PositiveImpact.Label.ToLowerInvariant());
                case 3:
                    return LF("Lifecycle.Reminder.3", "Tomorrow's election is a choice: my platform {0}, while {1}'s platform {2}.", effect.PositiveImpact.Sentence, opponentReference, opponentEffect.NegativeImpact.Sentence);
                case 4:
                    return LF("Lifecycle.Reminder.4", "{0} still has to explain why their platform {1}. Tomorrow, voters can demand better.", opponentReference, opponentEffect.NegativeImpact.Sentence);
                default:
                    return LF("Lifecycle.Reminder.5", "One day remains before the election, and I am ready to serve. Please vote tomorrow from {0}.", votingWindow);
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

        private static int GetReminderOpponentIndex(ElectionState state, int candidateIndex)
        {
            int leaderIndex = GetPollLeaderIndex(state);
            if (leaderIndex >= 0 && leaderIndex != candidateIndex)
                return leaderIndex;

            int candidateCount = state.ActiveCandidateCount;
            if (candidateCount <= 1)
                return -1;

            for (int i = 0; i < candidateCount; i++)
            {
                if (i != candidateIndex)
                    return i;
            }

            return -1;
        }

        private static int GetCandidateEffectSeed(ElectionState state)
        {
            int seed = 0;
            int candidateCount = state.ActiveCandidateCount;
            for (int i = 0; i < candidateCount; i++)
                seed += state.GetCandidateEffectId(i) * (17 + i * 14);

            return seed;
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
            else if (candidateIndex == 1)
                ScheduleCandidateDonationThankYouChirp(ref m_CandidateBDonationChirp, candidate, name, portraitIndex, amount, totalDonation, softening);
            else if (candidateIndex == 2)
                ScheduleCandidateDonationThankYouChirp(ref m_CandidateCDonationChirp, candidate, name, portraitIndex, amount, totalDonation, softening);
            else if (candidateIndex == 3)
                ScheduleCandidateDonationThankYouChirp(ref m_CandidateDDonationChirp, candidate, name, portraitIndex, amount, totalDonation, softening);
        }

        private void ClearPendingDonationChirp(int candidateIndex)
        {
            if (candidateIndex == 0)
                m_CandidateADonationChirp = default;
            else if (candidateIndex == 1)
                m_CandidateBDonationChirp = default;
            else if (candidateIndex == 2)
                m_CandidateCDonationChirp = default;
            else if (candidateIndex == 3)
                m_CandidateDDonationChirp = default;
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
            ProcessScheduledDonationThankYouChirp(ref m_CandidateCDonationChirp, utcNowTicks);
            ProcessScheduledDonationThankYouChirp(ref m_CandidateDDonationChirp, utcNowTicks);
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
            string donationText = LF("Lifecycle.Donation.ThankYou.Support", "your campaign support totaling {0:n0}", amount);
            string softeningText = softenedPlatform
                ? LF("Lifecycle.Donation.ThankYou.Softened", " The campaign has softened its platform: {0} changed from {1} to {2}.", softenedLabel, softenedPreviousValue, softenedCurrentValue)
                : string.Empty;
            string text = LF("Lifecycle.Donation.ThankYou.Message", "Thank you for {0}. Total donated to my campaign so far is {1:n0}.{2}", donationText, totalDonation, softeningText);
            DebugLog($"Posting delayed donation thank-you chirp: candidate={DescribeEntity(candidate, name)}, batchAmount={amount:n0}, totalDonation={totalDonation:n0}, portraitIndex={portraitIndex}.");

            if (!CustomChirpsBridge.PostLargeChirpFromEntityWithPortraitImage(text, candidate, candidate, portraitImageSource, name))
                CustomChirpsBridge.PostChirpFromEntity(text, candidate, candidate, name);
        }

        private void ProcessScheduledEndorsementChirp(ref ElectionState state)
        {
            if (state.mayorEndorsementChirpSent ||
                state.mayorEndorsementChirpUtcTicks <= 0 ||
                DateTime.UtcNow.Ticks < state.mayorEndorsementChirpUtcTicks)
            {
                return;
            }

            int candidateIndex = state.mayorEndorsementCandidateIndex;
            if (!state.IsActiveCandidateIndex(candidateIndex))
            {
                state.mayorEndorsementChirpSent = true;
                state.mayorEndorsementChirpUtcTicks = 0;
                return;
            }

            Entity candidate = state.GetCandidate(candidateIndex);
            if (candidate == Entity.Null ||
                state.mayorEndorsementCandidate != candidate)
            {
                DebugLog($"Skipped scheduled endorsement chirp because the endorsed candidate changed. candidateIndex={candidateIndex}, expected={FormatEntity(state.mayorEndorsementCandidate)}, current={FormatEntity(candidate)}.");
                state.mayorEndorsementChirpSent = true;
                state.mayorEndorsementChirpUtcTicks = 0;
                return;
            }

            PostMayorEndorsementChirp(state, candidateIndex, candidate);
            state.mayorEndorsementChirpSent = true;
            state.mayorEndorsementChirpUtcTicks = 0;
        }

        private void ProcessScheduledStrictVotingIdChirp(ref ElectionState state)
        {
            if (state.strictVotingIdChirpSent ||
                !state.strictVotingIdProposalPending ||
                state.strictVotingIdChirpUtcTicks <= 0 ||
                DateTime.UtcNow.Ticks < state.strictVotingIdChirpUtcTicks)
            {
                return;
            }

            if (state.strictVotingIdProposalPassed)
                state.SetLegislation(ElectionLegislationType.VoterIdentification, true);

            PostStrictVotingIdOutcomeChirp(state, state.strictVotingIdProposalPassed);
            state.strictVotingIdProposalPending = false;
            state.strictVotingIdProposalPassed = false;
            state.strictVotingIdChirpSent = true;
            state.strictVotingIdChirpUtcTicks = 0;
        }

        private void PostStrictVotingIdOutcomeChirp(ElectionState state, bool passed)
        {
            Entity mayor = state.mayor;
            string mayorName = GetEntityName(mayor, L("Lifecycle.Name.Mayor", "the mayor"));
            string text = passed
                ? L("Lifecycle.StrictVotingId.Passed", "The stricter voting ID proposal passed. Election staff will apply the new verification rules for this mayoral race.")
                : L("Lifecycle.StrictVotingId.Failed", "The stricter voting ID proposal did not pass. Voting rules will stay unchanged for this mayoral race.");

            if (!IsValidChirpCitizen(mayor))
            {
                PostElectionChirp(text, Entity.Null);
                DebugLog($"Strict voting ID outcome chirp posted by Election Board fallback: passed={passed}, mayor={DescribeEntity(mayor, mayorName)}.");
                return;
            }

            string portraitImageSource = CandidatePortraitCatalog.GetPortraitImageSource(
                EntityManager,
                mayor,
                GetMayorPortraitIndex(state));

            if (!CustomChirpsBridge.PostLargeChirpFromEntityWithPortraitImage(text, mayor, Entity.Null, portraitImageSource, mayorName))
                CustomChirpsBridge.PostChirpFromEntity(text, mayor, Entity.Null, mayorName);

            DebugLog($"Strict voting ID outcome chirp posted: passed={passed}, mayor={DescribeEntity(mayor, mayorName)}.");
        }

        private void PostMayorEndorsementChirp(ElectionState state, int candidateIndex, Entity candidate)
        {
            Entity mayor = state.mayor;
            string mayorName = GetEntityName(mayor, L("Lifecycle.Name.Mayor", "the mayor"));
            string candidateName = GetCandidateChirpName(candidate);
            bool hasCandidateLink = IsValidChirpCitizen(candidate);

            if (!IsValidChirpCitizen(mayor))
            {
                string candidateReference = hasCandidateLink ? "{LINK_1}" : candidateName;
                PostElectionChirp(
                    LF("Lifecycle.Endorse.Board", "The mayor endorsed {0} for mayor. Happy residents may give that endorsement extra weight in this election.", candidateReference),
                    hasCandidateLink ? candidate : Entity.Null);
                DebugLog($"Mayor endorsement chirp posted by Election Board fallback: mayor={DescribeEntity(mayor, mayorName)}, candidate={DescribeEntity(candidate, candidateName)}.");
                return;
            }

            string portraitImageSource = CandidatePortraitCatalog.GetPortraitImageSource(
                EntityManager,
                mayor,
                GetMayorPortraitIndex(state));
            string linkedCandidateReference = hasCandidateLink ? "{LINK_2}" : candidateName;
            string text = LF("Lifecycle.Endorse.Mayor", "I endorse {0} for mayor. Residents who are happy with the city's direction should know I trust them to carry this work forward.", linkedCandidateReference);
            string fallbackText = LF("Lifecycle.Endorse.Mayor", "I endorse {0} for mayor. Residents who are happy with the city's direction should know I trust them to carry this work forward.", candidateName);

            bool posted = hasCandidateLink &&
                CustomChirpsBridge.PostLargeChirpFromEntityWithPortraitImage(text, mayor, mayor, candidate, portraitImageSource, mayorName);
            if (!posted && !CustomChirpsBridge.PostLargeChirpFromEntityWithPortraitImage(fallbackText, mayor, mayor, portraitImageSource, mayorName))
                CustomChirpsBridge.PostChirpFromEntity(fallbackText, mayor, mayor, mayorName);

            DebugLog($"Mayor endorsement chirp posted: mayor={DescribeEntity(mayor, mayorName)}, candidateIndex={candidateIndex}, candidate={DescribeEntity(candidate, candidateName)}.");
        }

        private PlatformSofteningResult TrySoftenCandidatePlatform(ref ElectionState state, int candidateIndex, int totalDonation)
        {
            int donationSofteningThreshold = GetDonationSofteningThreshold(state);
            if (totalDonation <= donationSofteningThreshold)
                return default;

            bool alreadyAttempted = state.GetCandidateSoftenAttempted(candidateIndex);
            if (alreadyAttempted)
                return default;

            state.SetCandidateSoftenAttempted(candidateIndex, true);

            int effectId = state.GetCandidateEffectId(candidateIndex);
            Unity.Mathematics.Random random = CreateCampaignRandom(DateTime.UtcNow, 37717 + candidateIndex * 719 + effectId + totalDonation);
            int softeningChancePercent = 50;
            if (ArePartiesEnabled())
                softeningChancePercent += ElectionPartyTags.GetSofteningChanceDelta(state, state.GetCandidatePartyIndex(candidateIndex));
            softeningChancePercent = math.clamp(softeningChancePercent, 0, 100);

            if (random.NextInt(100) >= softeningChancePercent)
            {
                DebugLog($"Platform softening attempt failed: candidateIndex={candidateIndex}, totalDonation={totalDonation:n0}, threshold={donationSofteningThreshold:n0}, chance={softeningChancePercent}%.");
                return default;
            }

            state.SetCandidateNegativeSoftened(candidateIndex, true);

            ElectionEffectDefinition previous = GetCandidateEffectDefinition(state, candidateIndex, effectId, false);
            ElectionEffectDefinition current = GetCandidateEffectDefinition(state, candidateIndex, effectId, true);
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
            Entity candidate = state.GetCandidate(candidateIndex);
            int oldEffectId = state.GetCandidateEffectId(candidateIndex);
            bool negativeSoftened = state.GetCandidateNegativeSoftened(candidateIndex);
            ElectionEffectDefinition oldEffect = GetCandidateEffectDefinition(state, candidateIndex, oldEffectId, negativeSoftened);

            int newEffectId = PickEffect(candidate, now, 55103 + candidateIndex * 811 + oldEffectId + (int)m_SimulationSystem.frameIndex);
            for (int attempt = 0; attempt < 64 &&
                    (ElectionEffects.HasSamePlatform(newEffectId, oldEffectId) ||
                     HasSamePlatformAsOtherCandidate(state, candidateIndex, newEffectId) ||
                     HasSameNegativeImpact(newEffectId, oldEffectId)); attempt++)
            {
                int value = math.abs(newEffectId + candidate.Index * 173 + candidate.Version * 19 + attempt * 12289 + (int)m_SimulationSystem.frameIndex);
                newEffectId = ElectionEffects.CreateRandomId(value);
            }

            state.SetCandidateEffectId(candidateIndex, newEffectId);
            state.SetCandidatePlatformChirpSent(candidateIndex, false);
            state.SetCandidatePlatformChirpUtcTicks(candidateIndex, DateTime.UtcNow.AddMinutes(1).Ticks);

            ElectionEffectDefinition newEffect = GetCandidateEffectDefinition(state, candidateIndex, newEffectId, negativeSoftened);
            DebugLog($"Bribe succeeded: mayor={DescribeEntity(state.mayor, mayorName)}, target={DescribeEntity(candidate, candidateName)}, oldEffect={oldEffectId}, newEffect={newEffectId}, softened={negativeSoftened}.");
            PostMayorPlatformMeetingChirp(state, candidate, mayorName, candidateName, true, oldEffect, newEffect);
        }

        private void PostMayorPlatformMeetingChirp(ElectionState state, Entity candidate, string mayorName, string candidateName, bool succeeded, ElectionEffectDefinition oldEffect, ElectionEffectDefinition newEffect)
        {
            Entity mayor = state.mayor;
            bool hasCandidateLink = IsValidChirpCitizen(candidate);
            if (mayor == Entity.Null || !EntityManager.Exists(mayor))
            {
                string candidateReference = hasCandidateLink ? "{LINK_1}" : candidateName;
                PostElectionChirp(
                    succeeded
                        ? LF("Lifecycle.PlatformMeeting.Board.Success", "{0} agreed to revise their platform after a mayoral meeting.", candidateReference)
                        : LF("Lifecycle.PlatformMeeting.Board.Failed", "{0}'s platform did not change after a mayoral meeting.", candidateReference),
                    hasCandidateLink ? candidate : Entity.Null);
                return;
            }

            string portraitImageSource = CandidatePortraitCatalog.GetPortraitImageSource(
                EntityManager,
                mayor,
                GetMayorPortraitIndex(state));
            string linkedCandidateReference = hasCandidateLink ? "{LINK_2}" : candidateName;
            string text = succeeded
                ? LF("Lifecycle.PlatformMeeting.Mayor.Success", "I met with {0} today to discuss their platform. We found enough common ground that they agreed to revise it. Their updated tradeoff is {1} {2}.", linkedCandidateReference, newEffect.NegativeImpact.ValueText, newEffect.NegativeImpact.Label)
                : LF("Lifecycle.PlatformMeeting.Mayor.Failed", "I met with {0} today to discuss their platform. The conversation was constructive, but we still disagreed on key points, and I was not able to persuade them to revise it.", linkedCandidateReference);
            string fallbackText = succeeded
                ? LF("Lifecycle.PlatformMeeting.Mayor.Success", "I met with {0} today to discuss their platform. We found enough common ground that they agreed to revise it. Their updated tradeoff is {1} {2}.", candidateName, newEffect.NegativeImpact.ValueText, newEffect.NegativeImpact.Label)
                : LF("Lifecycle.PlatformMeeting.Mayor.Failed", "I met with {0} today to discuss their platform. The conversation was constructive, but we still disagreed on key points, and I was not able to persuade them to revise it.", candidateName);

            bool posted = hasCandidateLink &&
                CustomChirpsBridge.PostLargeChirpFromEntityWithPortraitImage(text, mayor, mayor, candidate, portraitImageSource, mayorName);
            if (!posted && !CustomChirpsBridge.PostLargeChirpFromEntityWithPortraitImage(fallbackText, mayor, mayor, portraitImageSource, mayorName))
                CustomChirpsBridge.PostChirpFromEntity(fallbackText, mayor, mayor, mayorName);
        }

        private void ProcessScheduledCorruptionInvestigationChirp(ref ElectionState state)
        {
            if (state.corruptionInvestigationChirpUtcTicks <= 0 ||
                DateTime.UtcNow.Ticks < state.corruptionInvestigationChirpUtcTicks)
            {
                return;
            }

            Entity mayor = state.corruptionInvestigationMayor != Entity.Null ? state.corruptionInvestigationMayor : state.mayor;
            string mayorName = GetEntityName(mayor, L("Lifecycle.Name.Mayor", "the mayor"));
            bool hasMayorLink = IsValidChirpCitizen(mayor);
            CustomChirpsBridge.PostChirp(
                LF("Lifecycle.CorruptionInvestigation.Mayor", "Police confirm {0} is facing a corruption investigation after allegations of mayoral campaign bribery.", hasMayorLink ? "{LINK_1}" : mayorName),
                DepartmentAccountBridge.Police,
                hasMayorLink ? mayor : Entity.Null,
                L("Lifecycle.Department.Police", "Police Department"));
            DebugLog($"Posted corruption investigation chirp for mayor={DescribeEntity(mayor, mayorName)}.");
            if (ArePartiesEnabled())
                ApplyPartyScandalReputationPenalty(ref state, state.mayorPartyIndex, "mayoral corruption investigation");
            state.corruptionInvestigationChirpUtcTicks = 0;
            state.corruptionInvestigationMayor = Entity.Null;
        }

        private void StartElection(ref ElectionState state, DateTime now)
        {
            if (!state.HasCandidates)
            {
                DebugLog($"Election start skipped: no candidate field. state={DescribeState(state)}");
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
            ResetCandidateVoteState(ref state);
            state.votingClosedChirpSent = false;
            ResetVictoryPartyState(ref state);
            ResetBribeMeetingState(ref state, true);
            SyncElectionDaySundayOverride(state, now);
            SyncElectionDaySpecialEventSuppression(state, now);
            string votingWindow = FormatVotingWindow(state.votingStartMinute, state.votingEndMinute);
            string resultsTime = ElectionUtility.FormatHourText(state.resultsAnnouncementMinute);
            DebugLog($"Election started: date={ElectionUtility.FormatCurrentDate(World, now)}, electionDayKey={state.electionDayKey}, votingWindow={ElectionUtility.FormatClockTime(state.votingStartMinute)}-{ElectionUtility.FormatClockTime(state.votingEndMinute)}, results={ElectionUtility.FormatClockTime(state.resultsAnnouncementMinute)}, holidayScheduled={state.electionDayHolidayScheduled}, candidates={DescribeCandidates(state)}.");

            if (state.ActiveCandidateCount == 2)
            {
                PostElectionChirpWithCandidates(
                    LF("Lifecycle.Election.Start.TwoLinks", "Election day has begun on {0} for {{LINK_1}} and {{LINK_2}}. Polls are open from {1} at education, welfare, administration, and postal buildings. You can watch voting in real time by clicking the Voting sites button to view the overlay. Results will be announced at {2}.", ElectionUtility.FormatCurrentDate(World, now), votingWindow, resultsTime),
                    state.candidateA,
                    state.candidateB);
            }
            else
            {
                PostElectionChirp(
                    LF("Lifecycle.Election.Start.Named", "Election day has begun on {0} for {1}. Polls are open from {2} at education, welfare, administration, and postal buildings. You can watch voting in real time by clicking the Voting sites button to view the overlay. Results will be announced at {3}.", ElectionUtility.FormatCurrentDate(World, now), GetCandidateNamesText(state), votingWindow, resultsTime),
                    Entity.Null);
            }
        }

        private void ProcessVotingClosedChirp(ref ElectionState state, DateTime now)
        {
            int votingEndMinute = ElectionUtility.NormalizeVotingEndMinute(state.votingEndMinute);
            if (state.votingClosedChirpSent ||
                state.stage != ElectionCampaignStage.Voting ||
                state.electionDayKey <= 0 ||
                !IsCurrentCalendarAtOrAfter(now, state.electionDayKey, votingEndMinute))
            {
                return;
            }

            state.votingEndMinute = votingEndMinute;
            state.votingClosedChirpSent = true;
            string resultsTime = ElectionUtility.FormatHourText(GetResultsAnnouncementMinute(state));
            PostElectionChirp(
                LF("Lifecycle.Election.VotingClosed", "All voting sites are now closed. Results of the election will be announced at {0}.", resultsTime),
                Entity.Null);
            DebugLog($"Voting closed chirp posted: date={ElectionUtility.FormatCurrentDate(World, now)}, electionDayKey={state.electionDayKey}, votingEnd={ElectionUtility.FormatClockTime(state.votingEndMinute)}, results={ElectionUtility.FormatClockTime(GetResultsAnnouncementMinute(state))}.");
        }

        private void ProcessVoteTampering(ref ElectionState state, DateTime now)
        {
            if (state.stage != ElectionCampaignStage.Voting ||
                !state.HasCandidates ||
                !state.IsActiveCandidateIndex(state.voteTamperingCandidateIndex) ||
                state.voteTamperingCandidate == Entity.Null)
            {
                return;
            }

            if (state.voteTamperingCandidate != state.GetCandidate(state.voteTamperingCandidateIndex))
            {
                DebugLog($"Vote tampering canceled because the beneficiary candidate changed. beneficiaryIndex={state.voteTamperingCandidateIndex}, expected={FormatEntity(state.voteTamperingCandidate)}.");
                ResetVoteTamperingState(ref state);
                return;
            }

            EnsureVoteTamperingScheduledMinute(ref state, now);
            int minuteOfDay = ElectionUtility.MinuteOfDay(now);
            if (!state.voteTamperingFireStarted && minuteOfDay < state.voteTamperingScheduledMinute)
                return;

            if (!state.voteTamperingFireStarted)
            {
                if (!TryFindVoteTamperingTarget(state, out Entity pollingPlace, out PollingPlaceVoteTally tally))
                {
                    if (minuteOfDay < state.votingEndMinute - 10)
                        return;

                    DebugLog($"Vote tampering operation expired without a suitable opposing-margin polling place. beneficiaryIndex={state.voteTamperingCandidateIndex}, state={DescribeState(state)}.");
                    state.voteTamperingFireStarted = true;
                    state.voteTamperingResolved = true;
                    return;
                }

                Unity.Mathematics.Random random = CreateCampaignRandom(
                    now,
                    65171 + state.voteTamperingCandidateIndex * 917 + state.electionDayKey + GetTallySeed(tally));
                StartPollingPlaceFire(pollingPlace, now);
                CalculateInitialTamperingLoss(state.voteTamperingCandidateIndex, tally, ref random, out int lostIndex, out int lostVotes);
                ApplyVoteLoss(ref state, lostIndex, lostVotes);
                state.voteTamperingPollingPlace = pollingPlace;
                state.voteTamperingFireStarted = true;
                if (lostIndex >= 0)
                    state.AddVoteTamperingLostVotes(lostIndex, lostVotes);
                PostVoteTamperingLossChirp(state, pollingPlace, lostIndex, lostVotes, destroyed: false);
                ScheduleVoteTamperingProtestChirp(ref state);
                DebugLog($"Vote tampering fire started: beneficiaryIndex={state.voteTamperingCandidateIndex}, pollingPlace={FormatEntity(pollingPlace)}, tally={FormatTallyVotes(tally, state.ActiveCandidateCount)}, lostIndex={lostIndex}, lostVotes={lostVotes}, scheduledMinute={state.voteTamperingScheduledMinute}, date={ElectionUtility.FormatCurrentDate(World, now)} {now:HH:mm}.");
            }

            if (state.voteTamperingResolved ||
                state.voteTamperingPollingPlace == Entity.Null ||
                !EntityManager.Exists(state.voteTamperingPollingPlace) ||
                !EntityManager.HasComponent<Destroyed>(state.voteTamperingPollingPlace))
            {
                return;
            }

            PollingPlaceVoteTally currentTally = TallyVotesForPollingPlace(state.electionDayKey, state.voteTamperingPollingPlace);
            int[] remainingLostVotes = GetRemainingTamperingLosses(state, currentTally);
            ApplyVoteLosses(ref state, remainingLostVotes);
            for (int i = 0; i < remainingLostVotes.Length; i++)
                state.AddVoteTamperingLostVotes(i, remainingLostVotes[i]);
            state.voteTamperingResolved = true;

            int totalRemainingLost = SumLosses(remainingLostVotes);
            if (totalRemainingLost > 0)
            {
                PostVoteTamperingLossChirp(state, state.voteTamperingPollingPlace, remainingLostVotes, destroyed: true);
                ScheduleVoteTamperingProtestChirp(ref state);
            }

            DebugLog($"Vote tampering polling place destroyed: pollingPlace={FormatEntity(state.voteTamperingPollingPlace)}, remainingLost={FormatLosses(remainingLostVotes)}, totalLost={FormatTamperingLosses(state)}.");
        }

        private void EnsureVoteTamperingScheduledMinute(ref ElectionState state, DateTime now)
        {
            if (state.voteTamperingScheduledMinute > 0)
                return;

            int latestMinute = math.max(state.votingStartMinute, state.votingEndMinute - kVoteTamperingMinBeforePollCloseMinutes);
            int earliestMinute = math.max(state.votingStartMinute, state.votingEndMinute - kVoteTamperingMaxBeforePollCloseMinutes);
            if (latestMinute < earliestMinute)
                latestMinute = earliestMinute;

            Unity.Mathematics.Random random = CreateCampaignRandom(
                now,
                41143 + state.voteTamperingCandidateIndex * 101 + state.electionDayKey);
            state.voteTamperingScheduledMinute = random.NextInt(earliestMinute, latestMinute + 1);
            DebugLog($"Vote tampering operation assigned election-day minute {ElectionUtility.FormatClockTime(state.voteTamperingScheduledMinute)} for beneficiaryIndex={state.voteTamperingCandidateIndex}.");
        }

        private bool TryFindVoteTamperingTarget(ElectionState state, out Entity pollingPlace, out PollingPlaceVoteTally bestTally)
        {
            pollingPlace = Entity.Null;
            bestTally = default;

            int bestMargin = 0;
            int bestOpponentVotes = 0;

            using (NativeArray<ElectionVoteTrip> voteTrips = m_VoteTripQuery.ToComponentDataArray<ElectionVoteTrip>(Allocator.TempJob))
            using (NativeParallelHashMap<Entity, PollingPlaceVoteTally> tallies = new NativeParallelHashMap<Entity, PollingPlaceVoteTally>(math.max(1, voteTrips.Length), Allocator.TempJob))
            {
                new TallyVotesByPollingPlaceJob
                {
                    voteTrips = voteTrips,
                    electionDayKey = state.electionDayKey,
                    tallies = tallies
                }.Schedule().Complete();

                using (NativeKeyValueArrays<Entity, PollingPlaceVoteTally> keyValues = tallies.GetKeyValueArrays(Allocator.Temp))
                {
                    for (int i = 0; i < keyValues.Length; i++)
                    {
                        Entity candidatePollingPlace = keyValues.Keys[i];
                        if (!IsValidVenue(candidatePollingPlace))
                            continue;

                        PollingPlaceVoteTally tally = keyValues.Values[i];
                        int beneficiaryVotes = GetTallyVotes(tally, state.voteTamperingCandidateIndex);
                        int leadingOpponentIndex = GetLeadingOpponentIndex(state, tally, state.voteTamperingCandidateIndex);
                        int opponentVotes = leadingOpponentIndex >= 0 ? GetTallyVotes(tally, leadingOpponentIndex) : 0;
                        int margin = opponentVotes - beneficiaryVotes;
                        if (margin <= 0 || opponentVotes <= 0)
                            continue;

                        if (pollingPlace == Entity.Null ||
                            margin > bestMargin ||
                            margin == bestMargin && opponentVotes > bestOpponentVotes)
                        {
                            pollingPlace = candidatePollingPlace;
                            bestTally = tally;
                            bestMargin = margin;
                            bestOpponentVotes = opponentVotes;
                        }
                    }
                }
            }

            return pollingPlace != Entity.Null;
        }

        private static int GetLeadingOpponentIndex(ElectionState state, PollingPlaceVoteTally tally, int beneficiaryIndex)
        {
            int candidateCount = state.ActiveCandidateCount;
            int bestIndex = -1;
            int bestVotes = -1;
            for (int i = 0; i < candidateCount; i++)
            {
                if (i == beneficiaryIndex)
                    continue;

                int votes = GetTallyVotes(tally, i);
                if (votes > bestVotes)
                {
                    bestVotes = votes;
                    bestIndex = i;
                }
            }

            return bestIndex;
        }

        private PollingPlaceVoteTally TallyVotesForPollingPlace(int electionDayKey, Entity pollingPlace)
        {
            if (pollingPlace == Entity.Null)
                return default;

            using (NativeArray<ElectionVoteTrip> voteTrips = m_VoteTripQuery.ToComponentDataArray<ElectionVoteTrip>(Allocator.TempJob))
            using (NativeArray<PollingPlaceVoteTally> tally = new NativeArray<PollingPlaceVoteTally>(1, Allocator.TempJob))
            {
                new TallyVotesForPollingPlaceJob
                {
                    voteTrips = voteTrips,
                    electionDayKey = electionDayKey,
                    pollingPlace = pollingPlace,
                    tally = tally
                }.Schedule().Complete();

                return tally[0];
            }
        }

        private void StartPollingPlaceFire(Entity pollingPlace, DateTime now)
        {
            if (!IsValidVenue(pollingPlace))
                return;

            OnFire onFire = EntityManager.HasComponent<OnFire>(pollingPlace)
                ? EntityManager.GetComponentData<OnFire>(pollingPlace)
                : new OnFire(Entity.Null, kVoteTamperingFireIntensity, (uint)m_SimulationSystem.frameIndex);
            onFire.m_Event = Entity.Null;
            onFire.m_Intensity = math.max(onFire.m_Intensity, kVoteTamperingFireIntensity);
            if (onFire.m_RequestFrame == 0)
                onFire.m_RequestFrame = (uint)m_SimulationSystem.frameIndex;

            if (EntityManager.HasComponent<OnFire>(pollingPlace))
                EntityManager.SetComponentData(pollingPlace, onFire);
            else
                EntityManager.AddComponentData(pollingPlace, onFire);

            if (!EntityManager.HasComponent<BatchesUpdated>(pollingPlace))
                EntityManager.AddComponent<BatchesUpdated>(pollingPlace);

            DebugLog($"Polling place fire started for vote tampering: pollingPlace={FormatEntity(pollingPlace)}, intensity={onFire.m_Intensity:0.##}, frame={m_SimulationSystem.frameIndex}, date={ElectionUtility.FormatCurrentDate(World, now)} {now:HH:mm}.");
        }

        private static void CalculateInitialTamperingLoss(int beneficiaryIndex, PollingPlaceVoteTally tally, ref Unity.Mathematics.Random random, out int lostIndex, out int lostVotes)
        {
            lostIndex = -1;
            lostVotes = 0;
            int opponentIndex = GetLeadingOpponentIndexForTally(tally, beneficiaryIndex);
            int opponentVotes = opponentIndex >= 0 ? GetTallyVotes(tally, opponentIndex) : 0;
            if (opponentVotes <= 0)
                return;

            int minLoss = math.max(1, (int)math.floor(opponentVotes * 0.12f));
            int maxLoss = math.max(minLoss, (int)math.ceil(opponentVotes * 0.35f));
            lostVotes = random.NextInt(minLoss, math.min(opponentVotes, maxLoss) + 1);
            lostIndex = opponentIndex;
        }

        private static int GetLeadingOpponentIndexForTally(PollingPlaceVoteTally tally, int beneficiaryIndex)
        {
            int bestIndex = -1;
            int bestVotes = -1;
            for (int i = 0; i < ElectionState.MaxCandidateCount; i++)
            {
                if (i == beneficiaryIndex)
                    continue;

                int votes = GetTallyVotes(tally, i);
                if (votes > bestVotes)
                {
                    bestVotes = votes;
                    bestIndex = i;
                }
            }

            return bestIndex;
        }

        private static int GetTallyVotes(PollingPlaceVoteTally tally, int candidateIndex)
        {
            switch (candidateIndex)
            {
                case 0:
                    return tally.votesA;
                case 1:
                    return tally.votesB;
                case 2:
                    return tally.votesC;
                case 3:
                    return tally.votesD;
                default:
                    return 0;
            }
        }

        private static void ApplyVoteLoss(ref ElectionState state, int candidateIndex, int lostVotes)
        {
            if (state.IsActiveCandidateIndex(candidateIndex))
                state.SubtractCandidateVotes(candidateIndex, lostVotes);
        }

        private static void ApplyVoteLosses(ref ElectionState state, int[] lostVotes)
        {
            if (lostVotes == null)
                return;

            int count = math.min(state.ActiveCandidateCount, lostVotes.Length);
            for (int i = 0; i < count; i++)
                state.SubtractCandidateVotes(i, lostVotes[i]);
        }

        private static int[] GetRemainingTamperingLosses(ElectionState state, PollingPlaceVoteTally tally)
        {
            int[] losses = new int[state.ActiveCandidateCount];
            for (int i = 0; i < losses.Length; i++)
                losses[i] = math.max(0, GetTallyVotes(tally, i) - state.GetVoteTamperingLostVotes(i));

            return losses;
        }

        private static int SumLosses(int[] losses)
        {
            int total = 0;
            if (losses == null)
                return total;

            for (int i = 0; i < losses.Length; i++)
                total += math.max(0, losses[i]);

            return total;
        }

        private static string FormatLosses(int[] losses)
        {
            if (losses == null || losses.Length == 0)
                return "none";

            string result = string.Empty;
            for (int i = 0; i < losses.Length; i++)
            {
                if (i > 0)
                    result += "/";
                result += losses[i].ToString();
            }

            return result;
        }

        private static string FormatTallyVotes(PollingPlaceVoteTally tally, int candidateCount)
        {
            string result = string.Empty;
            for (int i = 0; i < candidateCount; i++)
            {
                if (i > 0)
                    result += "/";
                result += GetTallyVotes(tally, i).ToString();
            }

            return result;
        }

        private static string FormatTamperingLosses(ElectionState state)
        {
            int candidateCount = state.ActiveCandidateCount;
            string result = string.Empty;
            for (int i = 0; i < candidateCount; i++)
            {
                if (i > 0)
                    result += "/";
                result += state.GetVoteTamperingLostVotes(i).ToString();
            }

            return result;
        }

        private static int GetTallySeed(PollingPlaceVoteTally tally)
        {
            return tally.votesA * 31 + tally.votesB * 43 + tally.votesC * 59 + tally.votesD * 71;
        }

        private void PostVoteTamperingLossChirp(ElectionState state, Entity pollingPlace, int lostIndex, int lostVotes, bool destroyed)
        {
            int[] losses = new int[state.ActiveCandidateCount];
            if (lostIndex >= 0 && lostIndex < losses.Length)
                losses[lostIndex] = math.max(0, lostVotes);

            PostVoteTamperingLossChirp(state, pollingPlace, losses, destroyed);
        }

        private void PostVoteTamperingLossChirp(ElectionState state, Entity pollingPlace, int[] lostVotes, bool destroyed)
        {
            int total = SumLosses(lostVotes);
            if (total <= 0)
                return;

            string locationName = GetBuildingName(pollingPlace, L("Lifecycle.Name.VotingSite", "a voting site"));
            string lostSummary = FormatVoteLossSummary(state, lostVotes, linked: false);
            string text = destroyed
                ? LF("Lifecycle.Tamper.Loss.Destroyed", "Fire & Rescue confirms {0} was destroyed. Election Board says all remaining ballots at that site were lost: {1}.", "{LINK_1}", lostSummary)
                : LF("Lifecycle.Tamper.Loss.Fire", "Fire crews are responding to a fire at {0}. Election Board says {1:n0} ballots were destroyed before they could be counted: {2}.", "{LINK_1}", total, lostSummary);
            string fallbackText = destroyed
                ? LF("Lifecycle.Tamper.Loss.Destroyed", "Fire & Rescue confirms {0} was destroyed. Election Board says all remaining ballots at that site were lost: {1}.", locationName, lostSummary)
                : LF("Lifecycle.Tamper.Loss.Fire", "Fire crews are responding to a fire at {0}. Election Board says {1:n0} ballots were destroyed before they could be counted: {2}.", locationName, total, lostSummary);

            bool posted = IsValidVenue(pollingPlace) &&
                CustomChirpsBridge.PostChirp(text, DepartmentAccountBridge.FireRescue, pollingPlace, L("Lifecycle.Department.FireRescue", "Fire & Rescue"));
            if (!posted)
                CustomChirpsBridge.PostChirp(fallbackText, DepartmentAccountBridge.FireRescue, IsValidVenue(pollingPlace) ? pollingPlace : Entity.Null, L("Lifecycle.Department.FireRescue", "Fire & Rescue"));
        }

        private string FormatVoteLossSummary(ElectionState state, int[] lostVotes, bool linked)
        {
            string result = string.Empty;
            int count = math.min(state.ActiveCandidateCount, lostVotes?.Length ?? 0);
            for (int i = 0; i < count; i++)
            {
                int lost = math.max(0, lostVotes[i]);
                if (lost <= 0)
                    continue;

                if (!string.IsNullOrEmpty(result))
                    result += ", ";

                string name = linked ? $"{{LINK_{i + 2}}}" : GetCandidateChirpName(state, i);
                result += $"{name} -{lost:n0}";
            }

            return string.IsNullOrEmpty(result) ? L("Lifecycle.Tamper.Loss.NoBallots", "no ballots") : result;
        }

        private static void ScheduleVoteTamperingProtestChirp(ref ElectionState state)
        {
            state.voteTamperingProtestChirpUtcTicks = DateTime.UtcNow.AddMinutes(2).Ticks;
            state.voteTamperingProtestChirpSent = false;
        }

        private void ProcessScheduledVoteTamperingProtestChirp(ref ElectionState state)
        {
            if (state.voteTamperingProtestChirpSent ||
                state.voteTamperingProtestChirpUtcTicks <= 0 ||
                DateTime.UtcNow.Ticks < state.voteTamperingProtestChirpUtcTicks)
            {
                return;
            }

            int affectedIndex = GetMostAffectedTamperingCandidateIndex(state);
            Entity affected = state.GetCandidate(affectedIndex);
            Entity opponent = state.IsActiveCandidateIndex(state.voteTamperingCandidateIndex) && state.voteTamperingCandidateIndex != affectedIndex
                ? state.GetCandidate(state.voteTamperingCandidateIndex)
                : GetNearestOtherCandidate(state, affectedIndex);
            if (!IsValidChirpCitizen(affected))
            {
                state.voteTamperingProtestChirpSent = true;
                state.voteTamperingProtestChirpUtcTicks = 0;
                return;
            }

            string fallbackName = ElectionState.GetCandidateFallbackName(affectedIndex);
            string affectedName = GetCandidateChirpName(affected);
            string opponentName = GetEntityName(opponent, L("Lifecycle.Name.OpposingCampaign", "the opposing campaign"));
            int portraitIndex = state.GetCandidatePortraitIndex(affectedIndex);
            string portraitImageSource = CandidatePortraitCatalog.GetPortraitImageSource(EntityManager, affected, portraitIndex);
            int lost = state.GetVoteTamperingLostVotes(affectedIndex);
            string locationName = GetBuildingName(state.voteTamperingPollingPlace, L("Lifecycle.Name.ThatVotingSite", "that voting site"));
            bool hasOpponentLink = IsValidChirpCitizen(opponent);
            bool hasLocationLink = IsValidVenue(state.voteTamperingPollingPlace);
            string locationReference = hasLocationLink ? "{LINK_3}" : locationName;
            string opponentReference = hasOpponentLink ? "{LINK_2}" : opponentName;
            string text = LF("Lifecycle.Tamper.Protest", "Our campaign lost {0:n0} votes after the fire at {1}. This count cannot be trusted, and {2} should support a full investigation.", lost, locationReference, opponentReference);
            string fallbackText = LF("Lifecycle.Tamper.Protest", "Our campaign lost {0:n0} votes after the fire at {1}. This count cannot be trusted, and {2} should support a full investigation.", lost, locationName, opponentName);

            bool posted = hasOpponentLink && hasLocationLink &&
                CustomChirpsBridge.PostLargeChirpFromEntityWithPortraitImage(text, affected, affected, opponent, state.voteTamperingPollingPlace, portraitImageSource, affectedName);
            if (!posted && hasOpponentLink)
            {
                string textWithOpponent = LF("Lifecycle.Tamper.Protest", "Our campaign lost {0:n0} votes after the fire at {1}. This count cannot be trusted, and {2} should support a full investigation.", lost, locationName, "{LINK_2}");
                posted = CustomChirpsBridge.PostLargeChirpFromEntityWithPortraitImage(textWithOpponent, affected, affected, opponent, portraitImageSource, affectedName);
            }

            if (!posted && !CustomChirpsBridge.PostLargeChirpFromEntityWithPortraitImage(fallbackText, affected, affected, portraitImageSource, affectedName))
                CustomChirpsBridge.PostChirpFromEntity(fallbackText, affected, affected, affectedName);

            state.voteTamperingProtestChirpSent = true;
            state.voteTamperingProtestChirpUtcTicks = 0;
            DebugLog($"Vote tampering protest chirp posted: affected={DescribeEntity(affected, fallbackName)}, lost={lost}, opponent={DescribeEntity(opponent, opponentName)}, pollingPlace={FormatEntity(state.voteTamperingPollingPlace)}.");
        }

        private static int GetMostAffectedTamperingCandidateIndex(ElectionState state)
        {
            int bestIndex = -1;
            int bestLostVotes = -1;
            int candidateCount = state.ActiveCandidateCount;
            for (int i = 0; i < candidateCount; i++)
            {
                int lostVotes = state.GetVoteTamperingLostVotes(i);
                if (lostVotes > bestLostVotes)
                {
                    bestLostVotes = lostVotes;
                    bestIndex = i;
                }
            }

            if (bestLostVotes > 0)
                return bestIndex;

            for (int i = 0; i < candidateCount; i++)
            {
                if (i != state.voteTamperingCandidateIndex)
                    return i;
            }

            return 0;
        }

        private void ProcessCandidateCorruptionArrestCheck(ref ElectionState state, DateTime now, int dayKey)
        {
            if (state.corruptionArrestCheckCompleted ||
                state.electionDayKey <= 0 ||
                dayKey <= state.electionDayKey ||
                state.stage == ElectionCampaignStage.Voting ||
                state.runoffActive ||
                (state.pendingMayor != Entity.Null && !state.pendingMayorInaugurated) ||
                !state.HasCandidates)
            {
                return;
            }

            state.corruptionArrestCheckCompleted = true;
            int candidateCount = state.ActiveCandidateCount;
            for (int i = 0; i < candidateCount; i++)
                TryArrestCandidateForElectionCorruption(ref state, now, i);

            bool outgoingMayorArrested = IsCitizenArrested(state.outgoingMayor) ||
                                         TryArrestOutgoingMayorForBribery(ref state, now);
            if (!outgoingMayorArrested)
                PostOutgoingMayorFarewellChirp(ref state, now);
            ResetOutgoingMayorState(ref state);
        }

        private void TryArrestCandidateForElectionCorruption(ref ElectionState state, DateTime now, int candidateIndex)
        {
            int riskSteps = GetCandidateCorruptionRiskSteps(state, candidateIndex);
            if (riskSteps <= 0)
                return;

            int partyIndex = state.GetCandidatePartyIndex(candidateIndex);
            int chancePercent = math.min(kCorruptionRiskMaxSteps * kCorruptionRiskStepPercent, riskSteps * kCorruptionRiskStepPercent);
            if (ArePartiesEnabled())
                chancePercent += ElectionPartyTags.GetCorruptionChanceDelta(state, partyIndex);
            chancePercent = math.clamp(chancePercent, 0, kCorruptionRiskMaxSteps * kCorruptionRiskStepPercent);
            Unity.Mathematics.Random random = CreateCampaignRandom(
                now,
                98317 + candidateIndex * 2003 + state.electionDayKey * 17 + riskSteps * 101);
            if (random.NextInt(100) >= chancePercent)
            {
                DebugLog($"Election corruption arrest check cleared: candidateIndex={candidateIndex}, riskSteps={riskSteps}, chance={chancePercent}%.");
                return;
            }

            Entity candidate = state.GetCandidate(candidateIndex);
            if (!IsValidChirpCitizen(candidate))
            {
                DebugLog($"Election corruption arrest skipped: candidate entity invalid. candidateIndex={candidateIndex}, candidate={FormatEntity(candidate)}, riskSteps={riskSteps}, chance={chancePercent}%.");
                return;
            }

            Criminal criminal = EntityManager.HasComponent<Criminal>(candidate)
                ? EntityManager.GetComponentData<Criminal>(candidate)
                : default;
            criminal.m_Event = Entity.Null;
            criminal.m_JailTime = (ushort)math.max((int)criminal.m_JailTime, kElectionCorruptionJailTime);
            criminal.m_Flags |= CriminalFlags.Arrested | CriminalFlags.Sentenced;
            if (EntityManager.HasComponent<Criminal>(candidate))
                EntityManager.SetComponentData(candidate, criminal);
            else
                EntityManager.AddComponentData(candidate, criminal);

            string fallbackName = ElectionState.GetCandidateFallbackName(candidateIndex);
            string candidateName = GetEntityName(candidate, fallbackName);
            CustomChirpsBridge.PostChirp(
                riskSteps == 1
                    ? LF("Lifecycle.CorruptionArrest.Candidate.Singular", "Police confirm {{LINK_1}} has been arrested after an election-corruption investigation. Detectives linked the campaign to {0} suspicious mayoral campaign action.", riskSteps)
                    : LF("Lifecycle.CorruptionArrest.Candidate.Plural", "Police confirm {{LINK_1}} has been arrested after an election-corruption investigation. Detectives linked the campaign to {0} suspicious mayoral campaign actions.", riskSteps),
                DepartmentAccountBridge.Police,
                candidate,
                L("Lifecycle.Department.Police", "Police Department"));
            if (ArePartiesEnabled())
                ApplyPartyScandalReputationPenalty(ref state, partyIndex, "candidate election-corruption arrest");
            DebugLog($"Election corruption arrest applied: candidate={DescribeEntity(candidate, candidateName)}, riskSteps={riskSteps}, chance={chancePercent}%, jailTime={criminal.m_JailTime}, flags={criminal.m_Flags}.");
        }

        private bool TryArrestOutgoingMayorForBribery(ref ElectionState state, DateTime now)
        {
            Entity mayor = state.outgoingMayor;
            int riskSteps = GetOutgoingMayorBribeRiskSteps(state);
            if (mayor == Entity.Null || riskSteps <= 0)
                return false;

            int chancePercent = math.min(kCorruptionRiskMaxSteps * kCorruptionRiskStepPercent, riskSteps * kCorruptionRiskStepPercent);
            if (ArePartiesEnabled())
                chancePercent += ElectionPartyTags.GetCorruptionChanceDelta(state, state.outgoingMayorPartyIndex);
            chancePercent = math.clamp(chancePercent, 0, kCorruptionRiskMaxSteps * kCorruptionRiskStepPercent);
            Unity.Mathematics.Random random = CreateCampaignRandom(
                now,
                121013 + state.electionDayKey * 19 + mayor.Index * 37 + mayor.Version * 53 + state.outgoingMayorBribeTotal);
            if (random.NextInt(100) >= chancePercent)
            {
                DebugLog($"Outgoing mayor bribery arrest check cleared: mayor={FormatEntity(mayor)}, bribeTotal={state.outgoingMayorBribeTotal:n0}, riskSteps={riskSteps}, chance={chancePercent}%.");
                return false;
            }

            if (!IsValidChirpCitizen(mayor))
            {
                DebugLog($"Outgoing mayor bribery arrest skipped: mayor entity invalid. mayor={FormatEntity(mayor)}, bribeTotal={state.outgoingMayorBribeTotal:n0}, riskSteps={riskSteps}, chance={chancePercent}%.");
                return false;
            }

            Criminal criminal = EntityManager.HasComponent<Criminal>(mayor)
                ? EntityManager.GetComponentData<Criminal>(mayor)
                : default;
            criminal.m_Event = Entity.Null;
            criminal.m_JailTime = (ushort)math.max((int)criminal.m_JailTime, kElectionCorruptionJailTime);
            criminal.m_Flags |= CriminalFlags.Arrested | CriminalFlags.Sentenced;
            if (EntityManager.HasComponent<Criminal>(mayor))
                EntityManager.SetComponentData(mayor, criminal);
            else
                EntityManager.AddComponentData(mayor, criminal);

            string mayorName = GetEntityName(mayor, L("Lifecycle.Name.FormerMayor", "the former mayor"));
            CustomChirpsBridge.PostChirp(
                riskSteps == 1
                    ? LF("Lifecycle.CorruptionArrest.Mayor.Singular", "Police confirm {{LINK_1}} has been arrested after a mayoral bribery investigation. Detectives linked the former mayor to {0} suspicious campaign action.", riskSteps)
                    : LF("Lifecycle.CorruptionArrest.Mayor.Plural", "Police confirm {{LINK_1}} has been arrested after a mayoral bribery investigation. Detectives linked the former mayor to {0} suspicious campaign actions.", riskSteps),
                DepartmentAccountBridge.Police,
                mayor,
                L("Lifecycle.Department.Police", "Police Department"));
            if (ArePartiesEnabled())
                ApplyPartyScandalReputationPenalty(ref state, state.outgoingMayorPartyIndex, "outgoing mayor bribery arrest");
            DebugLog($"Outgoing mayor bribery arrest applied: mayor={DescribeEntity(mayor, mayorName)}, bribeTotal={state.outgoingMayorBribeTotal:n0}, riskSteps={riskSteps}, chance={chancePercent}%, jailTime={criminal.m_JailTime}, flags={criminal.m_Flags}.");
            return true;
        }

        private void PostOutgoingMayorFarewellChirp(ref ElectionState state, DateTime now)
        {
            Entity mayor = state.outgoingMayor;
            if (mayor == Entity.Null)
                return;

            string mayorName = GetEntityName(mayor, L("Lifecycle.Name.FormerMayor", "the former mayor"));
            if (!IsValidChirpCitizen(mayor))
            {
                DebugLog($"Outgoing mayor farewell chirp skipped: mayor entity invalid. mayor={DescribeEntity(mayor, mayorName)}, bribeTotal={state.outgoingMayorBribeTotal:n0}.");
                return;
            }

            int donation = 0;
            int donationPercent = 0;
            if (state.outgoingMayorBribeTotal > 0)
            {
                Unity.Mathematics.Random random = CreateCampaignRandom(
                    now,
                    130363 + state.electionDayKey * 23 + mayor.Index * 41 + mayor.Version * 59 + state.outgoingMayorBribeTotal);
                donationPercent = random.NextInt(10, 31);
                donation = ClampMoneyAmount((long)state.outgoingMayorBribeTotal * donationPercent / 100L);
                if (donation > 0 && !TryAddCityMoney(donation))
                    donation = 0;
            }

            string cityName = GetCityName();
            string text = donation > 0
                ? LF("Lifecycle.OutgoingMayor.FarewellDonation", "Thank you, {0}, for the time I was mayor. I am donating {1:n0} back to the city as I leave office.", cityName, donation)
                : LF("Lifecycle.OutgoingMayor.Farewell", "Thank you, {0}, for the time I was mayor. Serving this city has been an honor.", cityName);
            int portraitIndex = GetPortraitIndexForCitizen(state, mayor);
            string portraitImageSource = CandidatePortraitCatalog.GetPortraitImageSource(EntityManager, mayor, portraitIndex);
            if (!CustomChirpsBridge.PostLargeChirpFromEntityWithPortraitImage(text, mayor, Entity.Null, portraitImageSource, mayorName))
                CustomChirpsBridge.PostChirpFromEntity(text, mayor, Entity.Null, mayorName);

            DebugLog($"Outgoing mayor farewell chirp posted: mayor={DescribeEntity(mayor, mayorName)}, bribeTotal={state.outgoingMayorBribeTotal:n0}, donationPercent={donationPercent}, donation={donation:n0}.");
        }

        private static int GetOutgoingMayorBribeRiskSteps(ElectionState state)
        {
            if (state.outgoingMayorBribeTotal <= 0)
                return 0;

            int bribeAmount = GetCampaignBribeAmount(state, state.outgoingMayorTagId);
            if (bribeAmount <= 0)
                return 1;

            long steps = ((long)state.outgoingMayorBribeTotal + bribeAmount - 1L) / bribeAmount;
            return (int)Math.Min(kCorruptionRiskMaxSteps, Math.Max(1L, steps));
        }

        private bool IsCitizenArrested(Entity citizen)
        {
            if (citizen == Entity.Null ||
                !EntityManager.Exists(citizen) ||
                !EntityManager.HasComponent<Criminal>(citizen))
            {
                return false;
            }

            Criminal criminal = EntityManager.GetComponentData<Criminal>(citizen);
            return (criminal.m_Flags & CriminalFlags.Arrested) != 0;
        }

        private void ProcessElectionVictoryPartyTrips(ref ElectionState state, DateTime now)
        {
            if (state.stage != ElectionCampaignStage.Voting ||
                !state.HasCandidates ||
                state.victoryPartyTripsRequested)
            {
                return;
            }

            int dayKey = ElectionUtility.CurrentCalendarDayKey(World, now);
            if (dayKey != state.electionDayKey ||
                ElectionUtility.MinuteOfDay(now) < kVictoryPartyStartMinute)
            {
                return;
            }

            if (state.victoryPartyElectionDayKey != state.electionDayKey ||
                !state.IsActiveCandidateIndex(state.victoryPartyWinnerIndex))
            {
                int winnerIndex = GetElectionWinnerIndex(state);
                if (state.runoffEnabledForCycle &&
                    !state.runoffActive &&
                    !HasCandidateReachedMajority(state, winnerIndex, GetTotalVotes(state)))
                {
                    return;
                }

                state.victoryPartyWinnerIndex = winnerIndex;
                state.victoryPartyElectionDayKey = state.electionDayKey;
                state.victoryPartyNextVoterIndex = 0;
                state.victoryPartyNextTripBatchTicks = now.Ticks;
                state.victoryPartyWinnerTripRequested = false;
                state.victoryPartyTripRequests = 0;

                if (!TryFindVictoryPartyVenue(out Entity venue))
                {
                    state.victoryPartyVenue = Entity.Null;
                    state.victoryPartyTripsRequested = true;
                    DebugLog($"Election victory party skipped: no City Hall, landmark, or park venue was available. winnerIndex={winnerIndex}, votes={FormatVoteTotals(state)}.");
                    return;
                }

                state.victoryPartyVenue = venue;
                int tripRequestCap = GetVictoryPartyTripRequestCap();
                DebugLog($"Election victory party scheduling started: date={ElectionUtility.FormatCurrentDate(World, now)} {now:HH:mm}, winnerIndex={winnerIndex}, venue={GetBuildingName(venue, L("Lifecycle.Name.CelebrationSite", "the celebration site"))} {FormatEntity(venue)}, maxRequests={tripRequestCap}, batchLimit={kVictoryPartyBatchTripLimit}, batchIntervalMinutes={kVictoryPartyBatchIntervalMinutes}.");
            }

            if (state.victoryPartyVenue == Entity.Null || !IsValidVenue(state.victoryPartyVenue))
            {
                state.victoryPartyTripsRequested = true;
                DebugLog($"Election victory party scheduling stopped: venue is no longer valid. venue={FormatEntity(state.victoryPartyVenue)}.");
                return;
            }

            if (state.victoryPartyNextTripBatchTicks > now.Ticks)
                return;

            Entity venueEntity = state.victoryPartyVenue;
            int winnerIndexForBatch = state.victoryPartyWinnerIndex;
            Entity winner = state.GetCandidate(winnerIndexForBatch);
            int maxTripRequests = GetVictoryPartyTripRequestCap();
            int requestsBeforeBatch = state.victoryPartyTripRequests;
            int requestsThisBatch = 0;
            int eligibleSupporters = 0;
            int selectedSupporters = 0;
            int rejectedSupporters = 0;
            int scannedSupporters = 0;

            if (!state.victoryPartyWinnerTripRequested && state.victoryPartyTripRequests < maxTripRequests)
            {
                state.victoryPartyWinnerTripRequested = true;
                if (winner != Entity.Null && EntityManager.Exists(winner))
                {
                    Unity.Mathematics.Random random = CreateVictoryPartyRandom(winner, state.electionDayKey, 104729);
                    if (RequestVictoryPartyTrip(
                            winner,
                            venueEntity,
                            random.NextFloat(kVictoryPartyMinDurationMinutes, kVictoryPartyMaxDurationMinutes),
                            110))
                    {
                        state.victoryPartyTripRequests++;
                        requestsThisBatch++;
                    }
                }
            }

            using (NativeArray<Entity> voters = m_VoteTripQuery.ToEntityArray(Allocator.Temp))
            using (NativeArray<ElectionVoteTrip> voteTrips = m_VoteTripQuery.ToComponentDataArray<ElectionVoteTrip>(Allocator.Temp))
            {
                int startIndex = math.clamp(state.victoryPartyNextVoterIndex, 0, voters.Length);
                int scanLimit = math.min(voters.Length, startIndex + kVictoryPartyBatchScanLimit);
                int i = startIndex;
                for (; i < scanLimit &&
                       requestsThisBatch < kVictoryPartyBatchTripLimit &&
                       state.victoryPartyTripRequests < maxTripRequests; i++)
                {
                    scannedSupporters++;
                    Entity voter = voters[i];
                    ElectionVoteTrip voteTrip = voteTrips[i];
                    if (!voteTrip.voted ||
                        voteTrip.electionDayKey != state.electionDayKey ||
                        voteTrip.chosenCandidate != winnerIndexForBatch ||
                        voter == winner)
                    {
                        continue;
                    }

                    eligibleSupporters++;
                    Unity.Mathematics.Random random = CreateVictoryPartyRandom(voter, state.electionDayKey, 8111);
                    if (random.NextInt(100) >= kVictoryPartySupporterChancePercent)
                        continue;

                    selectedSupporters++;
                    float duration = random.NextFloat(kVictoryPartyMinDurationMinutes, kVictoryPartyMaxDurationMinutes);
                    if (RequestVictoryPartyTrip(voter, venueEntity, duration, 90))
                    {
                        state.victoryPartyTripRequests++;
                        requestsThisBatch++;
                    }
                    else
                    {
                        rejectedSupporters++;
                    }
                }

                state.victoryPartyNextVoterIndex = i;

                if (state.victoryPartyTripRequests >= maxTripRequests ||
                    state.victoryPartyNextVoterIndex >= voters.Length)
                {
                    state.victoryPartyTripsRequested = true;
                }
                else
                {
                    state.victoryPartyNextTripBatchTicks = now.AddMinutes(kVictoryPartyBatchIntervalMinutes).Ticks;
                }
            }

            string venueName = GetBuildingName(venueEntity, L("Lifecycle.Name.CelebrationSite", "the celebration site"));
            DebugLog($"Election victory party batch processed: date={ElectionUtility.FormatCurrentDate(World, now)} {now:HH:mm}, winnerIndex={winnerIndexForBatch}, venue={venueName} {FormatEntity(venueEntity)}, scanned={scannedSupporters}, eligibleSupporters={eligibleSupporters}, selectedSupporters={selectedSupporters}, rejectedSupporters={rejectedSupporters}, requestsThisBatch={requestsThisBatch}, totalRequests={state.victoryPartyTripRequests}, maxRequests={maxTripRequests}, previousRequests={requestsBeforeBatch}, nextIndex={state.victoryPartyNextVoterIndex}, completed={state.victoryPartyTripsRequested}.");
        }

        private int GetVictoryPartyTripRequestCap()
        {
            int populationCap = GetPopulation() / kVictoryPartyPopulationShareDivisor;
            return math.max(1, math.min(kVictoryPartyMaxTripRequests, populationCap));
        }

        private static void CaptureOutgoingMayorForPostElection(ref ElectionState state, Entity winner)
        {
            Entity previousMayor = state.mayor;
            if (previousMayor == Entity.Null || previousMayor == winner)
            {
                ResetOutgoingMayorState(ref state);
                ResetMayorBribeTrackingState(ref state);
                return;
            }

            state.outgoingMayor = previousMayor;
            state.outgoingMayorBribeTotal = state.mayorBribeRecipient == previousMayor
                ? state.mayorBribeTotal
                : 0;
            state.outgoingMayorTagId = state.mayorTagId;
            state.outgoingMayorPartyIndex = state.mayorPartyIndex;
            ResetMayorBribeTrackingState(ref state);
        }

        private static void ApplyPartyElectionResults(ref ElectionState state, int winnerIndex, int previousMayorPartyIndex)
        {
            if (!state.IsActiveCandidateIndex(winnerIndex))
                return;

            int candidateCount = state.ActiveCandidateCount;
            int[] reputationDeltas = GetPartyElectionReputationDeltas(state, winnerIndex, candidateCount);
            for (int i = 0; i < candidateCount; i++)
            {
                int beforeReputation = state.GetPartyReputation(i);
                int reputationDelta = reputationDeltas[i];
                int consecutiveTerms;
                if (i == winnerIndex)
                {
                    consecutiveTerms = previousMayorPartyIndex == i
                        ? state.GetPartyConsecutiveTerms(i) + 1
                        : 1;
                    state.SetPartyConsecutiveTerms(i, consecutiveTerms);
                    state.SetPartyWins(i, state.GetPartyWins(i) + 1);
                }
                else
                {
                    consecutiveTerms = 0;
                    state.SetPartyConsecutiveTerms(i, 0);
                }

                state.AddPartyReputation(i, reputationDelta);
                ElectionDebug.Log($"Party election reputation changed: party={i}:{state.GetPartyName(i)}, result={(i == winnerIndex ? "win" : "nonwinner")}, votes={state.GetCandidateVotes(i)}, delta={reputationDelta}, reputation={beforeReputation}->{state.GetPartyReputation(i)}, consecutiveTerms={consecutiveTerms}, wins={state.GetPartyWins(i)}.");
            }

            state.mayorPartyIndex = winnerIndex;
        }

        private static int[] GetPartyElectionReputationDeltas(ElectionState state, int winnerIndex, int candidateCount)
        {
            int[] deltas = new int[candidateCount];
            int nonWinnerCount = candidateCount - 1;
            if (nonWinnerCount <= 0)
                return deltas;

            const int pool = ElectionPartyTags.NonWinnerReputationGainPool;
            long totalNonWinnerVotes = 0;
            for (int i = 0; i < candidateCount; i++)
            {
                if (i == winnerIndex)
                    continue;

                totalNonWinnerVotes += math.max(0, state.GetCandidateVotes(i));
            }

            if (totalNonWinnerVotes <= 0)
            {
                int baseGain = pool / nonWinnerCount;
                int remainder = pool % nonWinnerCount;
                int rank = 0;
                for (int i = 0; i < candidateCount; i++)
                {
                    if (i == winnerIndex)
                        continue;

                    deltas[i] = baseGain + (rank < remainder ? 1 : 0);
                    rank++;
                }

                return deltas;
            }

            long[] remainders = new long[candidateCount];
            int allocated = 0;
            for (int i = 0; i < candidateCount; i++)
            {
                if (i == winnerIndex)
                    continue;

                long weightedVotes = (long)math.max(0, state.GetCandidateVotes(i)) * pool;
                deltas[i] = (int)(weightedVotes / totalNonWinnerVotes);
                remainders[i] = weightedVotes % totalNonWinnerVotes;
                allocated += deltas[i];
            }

            int remaining = math.max(0, pool - allocated);
            for (int point = 0; point < remaining; point++)
            {
                int selectedIndex = -1;
                for (int i = 0; i < candidateCount; i++)
                {
                    if (i == winnerIndex)
                        continue;

                    if (selectedIndex < 0 ||
                        remainders[i] > remainders[selectedIndex] ||
                        (remainders[i] == remainders[selectedIndex] && state.GetCandidateVotes(i) > state.GetCandidateVotes(selectedIndex)) ||
                        (remainders[i] == remainders[selectedIndex] && state.GetCandidateVotes(i) == state.GetCandidateVotes(selectedIndex) && i < selectedIndex))
                    {
                        selectedIndex = i;
                    }
                }

                if (selectedIndex < 0)
                    break;

                deltas[selectedIndex]++;
                remainders[selectedIndex] = -1;
            }

            return deltas;
        }

        private static void ApplyPartyScandalReputationPenalty(ref ElectionState state, int partyIndex, string reason)
        {
            if (!ElectionState.IsPartyIndex(partyIndex))
                return;

            int delta = ElectionPartyTags.GetScandalReputationDelta(state, partyIndex);
            state.AddPartyReputation(partyIndex, delta);
            ElectionDebug.Log($"Party scandal reputation changed: party={partyIndex}:{state.GetPartyName(partyIndex)}, delta={delta}, reputation={state.GetPartyReputation(partyIndex)}, reason={reason}.");
        }

        private void CompleteElection(ref ElectionState state, DateTime now)
        {
            EnsureActiveElectionTiming(ref state);

            int winnerIndex = GetElectionWinnerIndex(state);
            int totalVotes = GetTotalVotes(state);
            if (state.runoffEnabledForCycle &&
                !state.runoffActive &&
                !HasCandidateReachedMajority(state, winnerIndex, totalVotes))
            {
                BeginRunoff(ref state, now, totalVotes);
                return;
            }

            CompleteFinalElection(ref state, now, winnerIndex);
        }

        private void BeginRunoff(ref ElectionState state, DateTime now, int totalVotes)
        {
            int firstIndex = GetElectionWinnerIndex(state);
            int secondIndex = GetRunnerUpIndex(state, firstIndex);
            if (secondIndex < 0)
            {
                DebugLog($"Runoff fallback: no runner-up available, completing election with winnerIndex={firstIndex}. state={DescribeState(state)}.");
                CompleteFinalElection(ref state, now, firstIndex);
                return;
            }

            int originalCandidateCount = state.ActiveCandidateCount;
            int[] endorsementBonuses = new int[2];
            PostRunoffStartedChirp(state, now, firstIndex, secondIndex, totalVotes);

            for (int i = 0; i < originalCandidateCount; i++)
            {
                if (i == firstIndex || i == secondIndex)
                    continue;

                int endorsedOriginalIndex = PickRunoffEndorsementTarget(state, i, firstIndex, secondIndex);
                int endorsedFinalistSlot = endorsedOriginalIndex == firstIndex ? 0 : 1;
                int bonusPercent = GetRunoffEndorsementBonusPercent(state, i, totalVotes);
                endorsementBonuses[endorsedFinalistSlot] += bonusPercent;
                PostRunoffEndorsementChirp(state, i, endorsedOriginalIndex, bonusPercent, totalVotes);
            }

            CompactRunoffFinalists(ref state, firstIndex, secondIndex, endorsementBonuses);

            ElectionUtility.GetCurrentCalendarDate(World, now, out int year, out _, out _);
            state.stage = ElectionCampaignStage.CandidatesSelected;
            state.runoffActive = true;
            state.runoffOriginalCandidateCount = originalCandidateCount;
            state.pollYear = year;
            state.pollMonth = 12;
            state.electionYear = year + 1;
            state.electionMonth = 1;
            state.mayorTermYear = year + 1;
            state.electionDayKey = 0;
            state.voteRequests = 0;
            state.voteArrivals = 0;
            state.votingClosedChirpSent = false;
            state.votingStartMinute = ElectionUtility.GetConfiguredVotingStartMinute();
            state.votingEndMinute = ElectionUtility.GetConfiguredVotingEndMinute();
            state.resultsAnnouncementMinute = ElectionUtility.ResultsAnnouncementMinute;
            ResetPollState(ref state);
            ResetCandidateVoteState(ref state);
            ResetVictoryPartyState(ref state);
            ResetBribeMeetingState(ref state);
            ResetVoteTamperingState(ref state);
            EnsureElectionReminderChirpsScheduled(ref state);
            ClearVoteTrips();

            DebugLog($"Runoff started: date={ElectionUtility.FormatCurrentDate(World, now)}, originalCandidateCount={originalCandidateCount}, finalists={DescribeCandidates(state)}, endorsementBonuses={FormatIntList(endorsementBonuses)}, poll={ElectionUtility.FormatDate(state.pollYear, state.pollMonth, 1)}, election={ElectionUtility.FormatDate(state.electionYear, state.electionMonth, 1)}, standings={DescribeCandidateStandings(state)}, mayorPartyIndex={state.mayorPartyIndex}, state={DescribeState(state)}.");
        }

        private void CompactRunoffFinalists(ref ElectionState state, int firstOriginalIndex, int secondOriginalIndex, int[] endorsementBonuses)
        {
            CandidateSlotSnapshot[] candidateSnapshots = CaptureCandidateSlots(state);
            PartySlotSnapshot[] partySnapshots = CapturePartySlots(state);
            int originalMayorPartyIndex = state.mayorPartyIndex;
            int originalOutgoingMayorPartyIndex = state.outgoingMayorPartyIndex;
            int originalEndorsementIndex = state.mayorEndorsementCandidateIndex;
            Entity originalEndorsementCandidate = state.mayorEndorsementCandidate;

            RestoreCandidateSlot(ref state, 0, candidateSnapshots[firstOriginalIndex], GetRunoffFinalistSupportModifier(candidateSnapshots[firstOriginalIndex], endorsementBonuses, 0));
            RestoreCandidateSlot(ref state, 1, candidateSnapshots[secondOriginalIndex], GetRunoffFinalistSupportModifier(candidateSnapshots[secondOriginalIndex], endorsementBonuses, 1));
            for (int i = 2; i < ElectionState.MaxCandidateCount; i++)
                ClearCandidateSlot(ref state, i);

            state.candidateCount = ElectionState.MinCandidateCount;
            ClearAllPendingDonationChirps();
            RemapMayorEndorsementForRunoff(ref state, originalEndorsementIndex, originalEndorsementCandidate, firstOriginalIndex, secondOriginalIndex);

            if (ArePartiesEnabled())
            {
                CopyPartySlot(ref state, 0, partySnapshots[firstOriginalIndex]);
                CopyPartySlot(ref state, 1, partySnapshots[secondOriginalIndex]);
                int preservationSlot = ElectionState.MinCandidateCount;
                state.mayorPartyIndex = RemapPartyIndexForRunoff(ref state, originalMayorPartyIndex, firstOriginalIndex, secondOriginalIndex, partySnapshots, ref preservationSlot);
                state.outgoingMayorPartyIndex = RemapPartyIndexForRunoff(ref state, originalOutgoingMayorPartyIndex, firstOriginalIndex, secondOriginalIndex, partySnapshots, ref preservationSlot);
                if (ElectionState.IsPartyIndex(state.mayorPartyIndex) && state.appliedEffectPartySignature != 0)
                    state.appliedEffectPartySignature = state.GetPartyPlatformSignature(state.mayorPartyIndex);
            }
            else
            {
                state.mayorPartyIndex = -1;
                state.outgoingMayorPartyIndex = -1;
            }
        }

        private static CandidateSlotSnapshot[] CaptureCandidateSlots(ElectionState state)
        {
            CandidateSlotSnapshot[] snapshots = new CandidateSlotSnapshot[ElectionState.MaxCandidateCount];
            for (int i = 0; i < ElectionState.MaxCandidateCount; i++)
                snapshots[i] = CaptureCandidateSlot(state, i);

            return snapshots;
        }

        private static CandidateSlotSnapshot CaptureCandidateSlot(ElectionState state, int index)
        {
            return new CandidateSlotSnapshot
            {
                candidate = state.GetCandidate(index),
                effectId = state.GetCandidateEffectId(index),
                age = state.GetCandidateAge(index),
                education = state.GetCandidateEducation(index),
                workType = state.GetCandidateWorkType(index),
                wealth = state.GetCandidateWealth(index),
                portraitIndex = state.GetCandidatePortraitIndex(index),
                tagId = state.GetCandidateTagId(index),
                supportModifierPercent = state.GetCandidateSupportModifierPercent(index),
                donation = state.GetCandidateDonation(index),
                negativeSoftened = state.GetCandidateNegativeSoftened(index),
                softenAttempted = state.GetCandidateSoftenAttempted(index),
                corruptionRiskSteps = state.GetCandidateCorruptionRiskSteps(index),
                platformChirpSent = state.GetCandidatePlatformChirpSent(index),
                platformChirpDayKey = state.GetCandidatePlatformChirpDayKey(index),
                platformChirpMinute = state.GetCandidatePlatformChirpMinute(index),
                platformChirpUtcTicks = state.GetCandidatePlatformChirpUtcTicks(index)
            };
        }

        private static void RestoreCandidateSlot(ref ElectionState state, int index, CandidateSlotSnapshot snapshot, int supportModifierPercent)
        {
            state.SetCandidate(index, snapshot.candidate);
            state.SetCandidateEffectId(index, snapshot.effectId);
            state.SetCandidateProfile(index, snapshot.age, snapshot.education, snapshot.workType, snapshot.wealth);
            state.SetCandidatePortraitIndex(index, snapshot.portraitIndex);
            state.SetCandidateTagId(index, snapshot.tagId);
            state.SetCandidateSupportModifierPercent(index, supportModifierPercent);
            state.SetCandidateDonation(index, snapshot.donation);
            state.SetCandidateNegativeSoftened(index, snapshot.negativeSoftened);
            state.SetCandidateSoftenAttempted(index, snapshot.softenAttempted);
            state.SetCandidateCorruptionRiskSteps(index, snapshot.corruptionRiskSteps);
            state.SetCandidatePlatformChirpSent(index, snapshot.platformChirpSent);
            state.SetCandidatePlatformChirpDayKey(index, snapshot.platformChirpDayKey);
            state.SetCandidatePlatformChirpMinute(index, snapshot.platformChirpMinute);
            state.SetCandidatePlatformChirpUtcTicks(index, snapshot.platformChirpUtcTicks);
            state.SetCandidatePollResponseChirpSent(index, true);
            state.SetCandidatePollResponseChirpUtcTicks(index, 0);
            state.SetCandidateElectionReminderChirpSent(index, true);
            state.SetCandidateElectionReminderChirpDayKey(index, 0);
            state.SetCandidateElectionReminderChirpMinute(index, 0);
            state.SetCandidateVotedChirpSent(index, false);
        }

        private static void ClearCandidateSlot(ref ElectionState state, int index)
        {
            state.SetCandidate(index, Entity.Null);
            state.SetCandidateEffectId(index, 0);
            state.SetCandidateProfile(index, 0, 0, 0, 0);
            state.SetCandidatePortraitIndex(index, -1);
            state.SetCandidateTagId(index, ElectionCandidateTags.None);
            state.SetCandidateSupportModifierPercent(index, 0);
            state.SetCandidateDonation(index, 0);
            state.SetCandidateNegativeSoftened(index, false);
            state.SetCandidateSoftenAttempted(index, false);
            state.SetCandidateVotes(index, 0);
            state.SetCandidatePollVotes(index, 0);
            state.SetCandidateCorruptionRiskSteps(index, 0);
            state.SetCandidatePlatformChirpSent(index, true);
            state.SetCandidatePlatformChirpDayKey(index, 0);
            state.SetCandidatePlatformChirpMinute(index, 0);
            state.SetCandidatePlatformChirpUtcTicks(index, 0);
            state.SetCandidatePollResponseChirpSent(index, true);
            state.SetCandidatePollResponseChirpUtcTicks(index, 0);
            state.SetCandidateElectionReminderChirpSent(index, true);
            state.SetCandidateElectionReminderChirpDayKey(index, 0);
            state.SetCandidateElectionReminderChirpMinute(index, 0);
            state.SetCandidateVotedChirpSent(index, false);
        }

        private void ClearAllPendingDonationChirps()
        {
            for (int i = 0; i < ElectionState.MaxCandidateCount; i++)
                ClearPendingDonationChirp(i);
        }

        private static int GetRunoffFinalistSupportModifier(CandidateSlotSnapshot snapshot, int[] endorsementBonuses, int finalistSlot)
        {
            int bonus = endorsementBonuses != null && finalistSlot >= 0 && finalistSlot < endorsementBonuses.Length
                ? endorsementBonuses[finalistSlot]
                : 0;
            return ElectionState.ClampCandidateSupportModifierPercent(snapshot.supportModifierPercent + bonus);
        }

        private static PartySlotSnapshot[] CapturePartySlots(ElectionState state)
        {
            PartySlotSnapshot[] snapshots = new PartySlotSnapshot[ElectionState.MaxCandidateCount];
            for (int i = 0; i < ElectionState.MaxCandidateCount; i++)
                snapshots[i] = CapturePartySlot(state, i);

            return snapshots;
        }

        private static PartySlotSnapshot CapturePartySlot(ElectionState state, int index)
        {
            return new PartySlotSnapshot
            {
                name = state.GetPartyName(index),
                color = state.GetPartyColor(index),
                reputation = state.GetPartyReputation(index),
                consecutiveTerms = state.GetPartyConsecutiveTerms(index),
                wins = state.GetPartyWins(index),
                lastTagReplacementYear = state.GetPartyLastTagReplacementYear(index),
                tagId1 = state.GetPartyTagId(index, 0),
                tagId2 = state.GetPartyTagId(index, 1),
                tagId3 = state.GetPartyTagId(index, 2)
            };
        }

        private static void CopyPartySlot(ref ElectionState state, int targetIndex, PartySlotSnapshot snapshot)
        {
            state.SetPartyName(targetIndex, snapshot.name);
            state.SetPartyColor(targetIndex, snapshot.color);
            state.SetPartyReputation(targetIndex, snapshot.reputation);
            state.SetPartyConsecutiveTerms(targetIndex, snapshot.consecutiveTerms);
            state.SetPartyWins(targetIndex, snapshot.wins);
            state.SetPartyLastTagReplacementYear(targetIndex, snapshot.lastTagReplacementYear);
            state.SetPartyTagId(targetIndex, 0, snapshot.tagId1);
            state.SetPartyTagId(targetIndex, 1, snapshot.tagId2);
            state.SetPartyTagId(targetIndex, 2, snapshot.tagId3);
        }

        private static int RemapPartyIndexForRunoff(
            ref ElectionState state,
            int originalPartyIndex,
            int firstOriginalIndex,
            int secondOriginalIndex,
            PartySlotSnapshot[] partySnapshots,
            ref int preservationSlot)
        {
            if (!ElectionState.IsPartyIndex(originalPartyIndex))
                return -1;

            if (originalPartyIndex == firstOriginalIndex)
                return 0;

            if (originalPartyIndex == secondOriginalIndex)
                return 1;

            while (preservationSlot < ElectionState.MaxCandidateCount)
            {
                int slot = preservationSlot++;
                if (slot == 0 || slot == 1)
                    continue;

                CopyPartySlot(ref state, slot, partySnapshots[originalPartyIndex]);
                return slot;
            }

            return originalPartyIndex;
        }

        private static void RemapMayorEndorsementForRunoff(ref ElectionState state, int originalEndorsementIndex, Entity originalEndorsementCandidate, int firstOriginalIndex, int secondOriginalIndex)
        {
            if (originalEndorsementCandidate == Entity.Null)
            {
                ResetMayorEndorsementState(ref state);
                return;
            }

            if (originalEndorsementIndex == firstOriginalIndex && state.GetCandidate(0) == originalEndorsementCandidate)
            {
                state.mayorEndorsementCandidateIndex = 0;
                state.mayorEndorsementCandidate = originalEndorsementCandidate;
                return;
            }

            if (originalEndorsementIndex == secondOriginalIndex && state.GetCandidate(1) == originalEndorsementCandidate)
            {
                state.mayorEndorsementCandidateIndex = 1;
                state.mayorEndorsementCandidate = originalEndorsementCandidate;
                return;
            }

            ResetMayorEndorsementState(ref state);
        }

        private int PickRunoffEndorsementTarget(ElectionState state, int eliminatedIndex, int firstIndex, int secondIndex)
        {
            int firstScore = GetRunoffEndorsementCompatibilityScore(state, eliminatedIndex, firstIndex);
            int secondScore = GetRunoffEndorsementCompatibilityScore(state, eliminatedIndex, secondIndex);
            if (firstScore != secondScore)
                return firstScore > secondScore ? firstIndex : secondIndex;

            int firstVotes = state.GetCandidateVotes(firstIndex);
            int secondVotes = state.GetCandidateVotes(secondIndex);
            if (firstVotes != secondVotes)
                return firstVotes > secondVotes ? firstIndex : secondIndex;

            return firstIndex < secondIndex ? firstIndex : secondIndex;
        }

        private int GetRunoffEndorsementCompatibilityScore(ElectionState state, int eliminatedIndex, int finalistIndex)
        {
            int score = 50000 - GetCandidateProfileDistance(state, eliminatedIndex, finalistIndex);
            score += GetCandidateTagCompatibilityScore(state.GetCandidateTagId(eliminatedIndex), state.GetCandidateTagId(finalistIndex));
            if (ArePartiesEnabled())
                score += GetPartyCompatibilityScore(state, state.GetCandidatePartyIndex(eliminatedIndex), state.GetCandidatePartyIndex(finalistIndex));

            return score;
        }

        private static int GetCandidateProfileDistance(ElectionState state, int firstIndex, int secondIndex)
        {
            int score = 0;
            score += math.abs(state.GetCandidateAge(firstIndex) - state.GetCandidateAge(secondIndex)) * 1200;
            score += math.abs(state.GetCandidateEducation(firstIndex) - state.GetCandidateEducation(secondIndex)) * 180;
            score += math.abs(state.GetCandidateWealth(firstIndex) - state.GetCandidateWealth(secondIndex)) * 180;
            score += GetWorkTypeDistance(state.GetCandidateWorkType(firstIndex), state.GetCandidateWorkType(secondIndex)) * 55;
            return score;
        }

        private static int GetCandidateTagCompatibilityScore(int firstTagId, int secondTagId)
        {
            firstTagId = ElectionCandidateTags.NormalizeId(firstTagId);
            secondTagId = ElectionCandidateTags.NormalizeId(secondTagId);
            if (firstTagId == secondTagId && firstTagId != ElectionCandidateTags.None)
                return 500;

            ElectionCandidateTagTone firstTone = ElectionCandidateTags.Get(firstTagId).Tone;
            ElectionCandidateTagTone secondTone = ElectionCandidateTags.Get(secondTagId).Tone;
            if (firstTone == secondTone)
                return 180;

            if (firstTone == ElectionCandidateTagTone.Mixed || secondTone == ElectionCandidateTagTone.Mixed)
                return 80;

            return 0;
        }

        private static int GetPartyCompatibilityScore(ElectionState state, int firstPartyIndex, int secondPartyIndex)
        {
            if (!ElectionState.IsPartyIndex(firstPartyIndex) || !ElectionState.IsPartyIndex(secondPartyIndex))
                return 0;

            int score = 0;
            score -= math.abs(state.GetPartyReputation(firstPartyIndex) - state.GetPartyReputation(secondPartyIndex)) * 4;
            for (int firstSlot = 0; firstSlot < ElectionPartyTags.TagsPerParty; firstSlot++)
            {
                int firstTagId = state.GetPartyTagId(firstPartyIndex, firstSlot);
                ElectionCandidateTagTone firstTone = ElectionPartyTags.Get(firstTagId).Tone;
                for (int secondSlot = 0; secondSlot < ElectionPartyTags.TagsPerParty; secondSlot++)
                {
                    int secondTagId = state.GetPartyTagId(secondPartyIndex, secondSlot);
                    if (firstTagId == secondTagId && firstTagId != ElectionPartyTags.None)
                    {
                        score += 140;
                    }
                    else if (firstTone == ElectionPartyTags.Get(secondTagId).Tone)
                    {
                        score += 35;
                    }
                }
            }

            return score;
        }

        private static int GetRunoffEndorsementBonusPercent(ElectionState state, int eliminatedIndex, int totalVotes)
        {
            int votes = math.max(0, state.GetCandidateVotes(eliminatedIndex));
            if (votes <= 0 || totalVotes <= 0)
                return 0;

            return math.clamp((int)math.round(votes * 100f / totalVotes), 0, 40);
        }

        private void PostRunoffStartedChirp(ElectionState state, DateTime now, int firstIndex, int secondIndex, int totalVotes)
        {
            Entity first = state.GetCandidate(firstIndex);
            Entity second = state.GetCandidate(secondIndex);
            string firstName = GetCandidateChirpName(first);
            string secondName = GetCandidateChirpName(second);
            int firstPct = GetCandidateResultPercent(state, firstIndex);
            int secondPct = GetCandidateResultPercent(state, secondIndex);
            ElectionUtility.GetCurrentCalendarDate(World, now, out int calendarYear, out _, out _);
            int runoffPollYear = calendarYear;
            int runoffElectionYear = calendarYear + 1;
            string pollDate = ElectionUtility.FormatDate(runoffPollYear, 12, 1);
            string electionDate = ElectionUtility.FormatDate(runoffElectionYear, 1, 1);
            string votesText = totalVotes > 0
                ? LF("Lifecycle.Runoff.VotesCounted", "{0:n0} votes counted", totalVotes)
                : L("Lifecycle.Runoff.NoVotesCounted", "no votes counted");

            if (IsValidChirpCitizen(first) && IsValidChirpCitizen(second) && CustomChirpsBridge.SupportsChirpWith2Targets())
            {
                PostElectionChirpWithCandidates(
                    LF("Lifecycle.Runoff.Started.TwoLinks", "No mayoral candidate reached 50% after {0}. {{LINK_1}} ({1}%) and {{LINK_2}} ({2}%) advance to a runoff. The runoff poll is {3} at 8 AM, and the final election is {4}. Current mayoral programs and legislation continue into the runoff.", votesText, firstPct, secondPct, pollDate, electionDate),
                    first,
                    second);
                return;
            }

            PostElectionChirp(
                LF("Lifecycle.Runoff.Started.Named", "No mayoral candidate reached 50% after {0}. {1} ({2}%) and {3} ({4}%) advance to a runoff. The runoff poll is {5} at 8 AM, and the final election is {6}. Current mayoral programs and legislation continue into the runoff.", votesText, firstName, firstPct, secondName, secondPct, pollDate, electionDate),
                Entity.Null);
        }

        private void PostRunoffEndorsementChirp(ElectionState state, int eliminatedIndex, int endorsedIndex, int bonusPercent, int totalVotes)
        {
            Entity eliminated = state.GetCandidate(eliminatedIndex);
            Entity endorsed = state.GetCandidate(endorsedIndex);
            string eliminatedName = GetCandidateChirpName(eliminated);
            string endorsedName = GetCandidateChirpName(endorsed);
            int votePercent = GetCandidateResultPercent(state, eliminatedIndex);
            int votes = state.GetCandidateVotes(eliminatedIndex);
            string bonusText = bonusPercent > 0
                ? LF("Lifecycle.Runoff.Endorsement.Bonus", " Our {0}% share of the first-round vote becomes a {1} runoff support bonus for their campaign.", votePercent, FormatSignedPercent(bonusPercent))
                : L("Lifecycle.Runoff.Endorsement.NoBonus", " We did not receive enough first-round votes to move the runoff math.");
            string fallbackText = LF("Lifecycle.Runoff.Endorsement.Candidate", "We came up short with {0:n0} of {1:n0} votes. I am supporting {2} in the runoff.{3}", votes, totalVotes, endorsedName, bonusText);

            if (IsValidChirpCitizen(eliminated))
            {
                int portraitIndex = state.GetCandidatePortraitIndex(eliminatedIndex);
                string portraitImageSource = CandidatePortraitCatalog.GetPortraitImageSource(EntityManager, eliminated, portraitIndex);
                string linkedText = IsValidChirpCitizen(endorsed)
                    ? LF("Lifecycle.Runoff.Endorsement.Candidate", "We came up short with {0:n0} of {1:n0} votes. I am supporting {2} in the runoff.{3}", votes, totalVotes, "{LINK_1}", bonusText)
                    : fallbackText;
                bool posted = IsValidChirpCitizen(endorsed) &&
                    CustomChirpsBridge.PostLargeChirpFromEntityWithPortraitImage(linkedText, eliminated, endorsed, portraitImageSource, eliminatedName);
                if (!posted && CustomChirpsBridge.PostLargeChirpFromEntityWithPortraitImage(fallbackText, eliminated, Entity.Null, portraitImageSource, eliminatedName))
                    return;
                if (!posted)
                    CustomChirpsBridge.PostChirpFromEntity(fallbackText, eliminated, Entity.Null, eliminatedName);
                return;
            }

            PostElectionChirp(LF("Lifecycle.Runoff.Endorsement.Board", "{0} endorsed {1} in the runoff.{2}", eliminatedName, endorsedName, bonusText), endorsed);
        }

        private static string FormatSignedPercent(int value)
        {
            return value > 0 ? $"+{value}%" : $"{value}%";
        }

        private void CompleteFinalElection(ref ElectionState state, DateTime now, int winnerIndex)
        {
            Entity winner = state.GetCandidate(winnerIndex);
            int effectId = state.GetCandidateEffectId(winnerIndex);
            int winnerTagId = state.GetCandidateTagId(winnerIndex);
            bool winnerNegativeSoftened = state.GetCandidateNegativeSoftened(winnerIndex);
            string winnerName = GetCandidateChirpName(winner);
            int population = GetPopulation();
            int eligibleVoters = GetEligibleVoterCount();
            int totalVotes = GetTotalVotes(state);
            int turnoutPct = eligibleVoters > 0 ? (int)math.round(totalVotes * 100f / eligibleVoters) : 0;
            Entity previousMayor = state.mayor;
            int previousMayorPartyIndex = state.mayorPartyIndex;
            bool inaugurateNow = ShouldInaugurateWinnerNow(state, now);

            if (inaugurateNow)
            {
                ApplyWinnerAsMayor(ref state, now, winnerIndex, winner, effectId, winnerTagId, winnerNegativeSoftened, previousMayorPartyIndex);
            }
            else
            {
                SchedulePendingMayor(ref state, winnerIndex, winner, effectId, winnerTagId, winnerNegativeSoftened);
            }

            state.democraticTransitionCompleted = true;
            state.stage = ElectionCampaignStage.None;
            state.runoffActive = false;
            state.victoryPartyWinnerIndex = winnerIndex;
            state.victoryPartyElectionDayKey = state.electionDayKey;
            if (state.victoryPartyVenue == Entity.Null && TryFindVictoryPartyVenue(out Entity resultVenue))
                state.victoryPartyVenue = resultVenue;
            state.victoryWinnerChirpSent = false;
            state.victoryLoserChirpSent = false;
            state.victoryWinnerChirpUtcTicks = DateTime.UtcNow.AddMinutes(1).Ticks;
            state.victoryLoserChirpUtcTicks = DateTime.UtcNow.AddMinutes(2).Ticks;
            ResetBribeMeetingState(ref state, true);
            DebugLog($"Election completed: date={ElectionUtility.FormatCurrentDate(World, now)}, winnerIndex={winnerIndex}, winner={DescribeEntity(winner, winnerName)}, previousMayor={FormatEntity(previousMayor)}, inauguratedNow={inaugurateNow}, pendingMayor={FormatEntity(state.pendingMayor)}, outgoingMayor={FormatEntity(state.outgoingMayor)}, outgoingMayorBribeTotal={state.outgoingMayorBribeTotal:n0}, votes={FormatVoteTotals(state)}, voteRequests={state.voteRequests}, voteArrivals={state.voteArrivals}, population={population}, eligibleVoters={eligibleVoters}, turnoutPct={turnoutPct}, effectId={effectId}, tag={ElectionCandidateTags.Get(winnerTagId).Name}.");

            PostFinalElectionResultsChirp(state, now, winnerIndex, winnerName, effectId, winnerNegativeSoftened, eligibleVoters, totalVotes, turnoutPct, inaugurateNow);
            DebugLog($"Scheduled victory result chirps: winnerDue={new DateTime(state.victoryWinnerChirpUtcTicks):O} UTC, loserDue={new DateTime(state.victoryLoserChirpUtcTicks):O} UTC, venue={FormatEntity(state.victoryPartyVenue)}.");

            ClearVoteTrips();

            if (inaugurateNow)
                ProcessCandidateCorruptionArrestCheck(ref state, now, ElectionUtility.CurrentCalendarDayKey(World, now));
        }

        private void PostFinalElectionResultsChirp(ElectionState state, DateTime now, int winnerIndex, string winnerName, int effectId, bool winnerNegativeSoftened, int eligibleVoters, int totalVotes, int turnoutPct, bool inauguratedNow)
        {
            ElectionEffectDefinition effect = GetCandidateEffectDefinition(state, winnerIndex, effectId, winnerNegativeSoftened);
            Entity announcementVenue = IsValidVenue(state.victoryPartyVenue) ? state.victoryPartyVenue : Entity.Null;
            string namedPartyText = announcementVenue != Entity.Null
                ? LF("Lifecycle.Results.SupportersNamed", " Supporters are gathering at {0}.", GetBuildingName(announcementVenue, L("Lifecycle.Name.CelebrationSite", "the celebration site")))
                : string.Empty;
            string linkedPartyText = announcementVenue != Entity.Null
                ? LF("Lifecycle.Results.SupportersLink3", " Supporters are gathering at {{LINK_3}}.")
                : string.Empty;
            string venueOnlyLinkedPartyText = announcementVenue != Entity.Null
                ? LF("Lifecycle.Results.SupportersLink1", " Supporters are gathering at {{LINK_1}}.")
                : string.Empty;
            string turnoutDenominatorText = eligibleVoters > 0
                ? LF("Lifecycle.Results.TurnoutDenominator.Eligible", "eligible voters ({0:n0} of {1:n0})", totalVotes, eligibleVoters)
                : LF("Lifecycle.Results.TurnoutDenominator.Votes", "eligible voters ({0:n0} votes)", totalVotes);
            string transitionText = inauguratedNow
                ? L("Lifecycle.Results.Transition.Today", " The new mayor takes office today.")
                : LF("Lifecycle.Results.Transition.Future", " The mayor-elect will take office on {0}; the current mayor remains in office until then.", ElectionUtility.FormatDate(state.mayorTermYear, 1, 1));
            string resultsIntro = LF("Lifecycle.Results.Intro", "Election results for {0} are final. {1} has been elected mayor. Turnout was {2}% of {3}.", ElectionUtility.FormatCurrentDate(World, now), winnerName, turnoutPct, turnoutDenominatorText);
            string linkedCandidateResults = state.ActiveCandidateCount == 2
                ? LF("Lifecycle.Results.CandidateLinks", " Results: {{LINK_1}} {0}%, {{LINK_2}} {1}%.", GetCandidateResultPercent(state, 0), GetCandidateResultPercent(state, 1))
                : LF("Lifecycle.Results.CandidateNames", " Results: {0}.", GetNamedResultText(state));
            string namedCandidateResults = LF("Lifecycle.Results.CandidateNames", " Results: {0}.", GetNamedResultText(state));
            string platformSubject = inauguratedNow
                ? L("Lifecycle.Results.Platform.NewMayor", "The new mayor's platform")
                : L("Lifecycle.Results.Platform.MayorElect", "The mayor-elect's platform");
            string platformText = LF("Lifecycle.Results.PlatformText", " {0} {1}.{2}", platformSubject, effect.Description, transitionText);
            if (state.ActiveCandidateCount == 2)
            {
                PostElectionResultsChirp(
                    $"{resultsIntro}{linkedCandidateResults}{platformText}{linkedPartyText}",
                    $"{resultsIntro}{linkedCandidateResults}{platformText}{namedPartyText}",
                    $"{resultsIntro}{namedCandidateResults}{platformText}{venueOnlyLinkedPartyText}",
                    $"{resultsIntro}{namedCandidateResults}{platformText}{namedPartyText}",
                    state.candidateA,
                    state.candidateB,
                    announcementVenue);
            }
            else if (announcementVenue != Entity.Null)
            {
                PostElectionChirp(
                    $"{resultsIntro}{namedCandidateResults}{platformText}{venueOnlyLinkedPartyText}",
                    announcementVenue);
            }
            else
            {
                PostElectionChirp(
                    $"{resultsIntro}{namedCandidateResults}{platformText}{namedPartyText}",
                    Entity.Null);
            }
        }

        private static bool HasCandidateReachedMajority(ElectionState state, int candidateIndex, int totalVotes)
        {
            if (!state.IsActiveCandidateIndex(candidateIndex) || totalVotes <= 0)
                return false;

            int votes = state.GetCandidateVotes(candidateIndex);
            for (int i = 0; i < state.ActiveCandidateCount; i++)
            {
                if (i != candidateIndex && state.GetCandidateVotes(i) == votes)
                    return false;
            }

            return votes * 2 >= totalVotes;
        }

        private bool ShouldInaugurateWinnerNow(ElectionState state, DateTime now)
        {
            if (!state.runoffEnabledForCycle)
                return true;

            ElectionUtility.GetCurrentCalendarDate(World, now, out int year, out int month, out int day);
            int termYear = state.mayorTermYear > 0 ? state.mayorTermYear : year;
            return ElectionUtility.CompareCalendarDate(year, month, day, termYear, 1, 1) >= 0;
        }

        private void SchedulePendingMayor(ref ElectionState state, int winnerIndex, Entity winner, int effectId, int tagId, bool negativeSoftened)
        {
            state.pendingMayorCandidateIndex = winnerIndex;
            state.pendingMayor = winner;
            state.pendingMayorEffectId = effectId;
            state.pendingMayorTagId = tagId;
            state.pendingMayorNegativeSoftened = negativeSoftened;
            state.pendingMayorPartyIndex = ArePartiesEnabled() ? winnerIndex : -1;
            state.pendingMayorTermYear = state.mayorTermYear;
            state.pendingMayorInaugurated = false;
        }

        private void ApplyWinnerAsMayor(ref ElectionState state, DateTime now, int winnerIndex, Entity winner, int effectId, int tagId, bool negativeSoftened, int previousMayorPartyIndex)
        {
            CaptureOutgoingMayorForPostElection(ref state, winner);

            if (ArePartiesEnabled())
                ApplyPartyElectionResults(ref state, winnerIndex, previousMayorPartyIndex);
            else
                state.mayorPartyIndex = -1;

            state.mayor = winner;
            state.mayorEffectId = effectId;
            state.mayorTagId = tagId;
            state.mayorNegativeSoftened = negativeSoftened;
            ElectionUtility.GetCurrentCalendarDate(World, now, out int currentYear, out _, out _);
            state.mayorEffectTermYear = state.mayorTermYear != 0 ? state.mayorTermYear : currentYear;
            state.mayorMoneyApplied = false;
            state.pendingMayorInaugurated = true;
        }

        private void ProcessPendingMayorInauguration(ref ElectionState state, DateTime now)
        {
            if (state.pendingMayor == Entity.Null || state.pendingMayorInaugurated)
                return;

            int termYear = state.pendingMayorTermYear > 0 ? state.pendingMayorTermYear : state.mayorTermYear;
            if (termYear <= 0)
                return;

            ElectionUtility.GetCurrentCalendarDate(World, now, out int year, out int month, out int day);
            if (ElectionUtility.CompareCalendarDate(year, month, day, termYear, 1, 1) < 0)
                return;

            int previousMayorPartyIndex = state.mayorPartyIndex;
            int winnerIndex = state.IsActiveCandidateIndex(state.pendingMayorCandidateIndex)
                ? state.pendingMayorCandidateIndex
                : FindCandidateIndex(state, state.pendingMayor);
            if (!state.IsActiveCandidateIndex(winnerIndex))
                winnerIndex = GetElectionWinnerIndex(state);

            Entity winner = state.pendingMayor;
            int effectId = state.pendingMayorEffectId;
            int tagId = state.pendingMayorTagId;
            bool negativeSoftened = state.pendingMayorNegativeSoftened;
            string winnerName = GetCandidateChirpName(winner);

            ApplyWinnerAsMayor(ref state, now, winnerIndex, winner, effectId, tagId, negativeSoftened, previousMayorPartyIndex);
            state.democraticTransitionCompleted = true;
            ResetPendingMayorState(ref state);

            PostElectionChirp(
                LF("Lifecycle.PendingMayor.Inaugurated", "{0} takes office as mayor today. The election transition is complete, and the new mayoral platform is now active.", winnerName),
                IsValidChirpCitizen(winner) ? winner : Entity.Null);
            DebugLog($"Pending mayor inaugurated: date={ElectionUtility.FormatCurrentDate(World, now)}, winnerIndex={winnerIndex}, winner={DescribeEntity(winner, winnerName)}, effectId={effectId}, tag={ElectionCandidateTags.Get(tagId).Name}.");

            ProcessCandidateCorruptionArrestCheck(ref state, now, ElectionUtility.CurrentCalendarDayKey(World, now));
        }

        private static int FindCandidateIndex(ElectionState state, Entity candidate)
        {
            int candidateCount = state.ActiveCandidateCount;
            for (int i = 0; i < candidateCount; i++)
            {
                if (state.GetCandidate(i) == candidate)
                    return i;
            }

            return -1;
        }

        private void ProcessScheduledVictoryPartyChirps(ref ElectionState state)
        {
            long utcNowTicks = DateTime.UtcNow.Ticks;

            if (!state.victoryWinnerChirpSent &&
                state.victoryWinnerChirpUtcTicks > 0 &&
                utcNowTicks >= state.victoryWinnerChirpUtcTicks)
            {
                PostWinnerVictoryChirp(state);
                state.victoryWinnerChirpSent = true;
            }

            if (!state.victoryLoserChirpSent &&
                state.victoryLoserChirpUtcTicks > 0 &&
                utcNowTicks >= state.victoryLoserChirpUtcTicks)
            {
                PostLoserResultChirp(state);
                state.victoryLoserChirpSent = true;
            }
        }

        private void PostWinnerVictoryChirp(ElectionState state)
        {
            int winnerIndex = state.IsActiveCandidateIndex(state.victoryPartyWinnerIndex)
                ? state.victoryPartyWinnerIndex
                : GetElectionWinnerIndex(state);
            Entity winner = state.GetCandidate(winnerIndex);
            if (!IsValidChirpCitizen(winner))
            {
                DebugLog($"Skipped winner victory chirp: winner entity is not available ({FormatEntity(winner)}).");
                return;
            }

            string fallbackName = ElectionState.GetCandidateFallbackName(winnerIndex);
            string name = GetCandidateChirpName(winner);
            int portraitIndex = state.GetCandidatePortraitIndex(winnerIndex);
            string portraitImageSource = CandidatePortraitCatalog.GetPortraitImageSource(EntityManager, winner, portraitIndex);
            Entity venue = IsValidVenue(state.victoryPartyVenue) ? state.victoryPartyVenue : Entity.Null;
            string cityName = GetCityName();
            bool pendingInauguration = state.pendingMayor == winner && !state.pendingMayorInaugurated;
            string transitionText = pendingInauguration
                ? LF("Lifecycle.Victory.Transition.Future", " We take office on {0}.", ElectionUtility.FormatDate(state.pendingMayorTermYear > 0 ? state.pendingMayorTermYear : state.mayorTermYear, 1, 1))
                : L("Lifecycle.Victory.Transition.Tomorrow", " Tomorrow we get to work.");
            string text = venue != Entity.Null
                ? LF("Lifecycle.Victory.Winner.AtVenue", "Thank you, {0}. We won tonight, and I am celebrating with supporters at {{LINK_2}}.{1}", cityName, transitionText)
                : LF("Lifecycle.Victory.Winner.NoVenue", "Thank you, {0}. We won tonight, and I am celebrating with supporters.{1}", cityName, transitionText);
            string fallbackText = LF("Lifecycle.Victory.Winner.NoVenue", "Thank you, {0}. We won tonight, and I am celebrating with supporters.{1}", cityName, transitionText);

            bool posted = venue != Entity.Null &&
                CustomChirpsBridge.PostLargeChirpFromEntityWithPortraitImage(text, winner, winner, venue, portraitImageSource, name);
            if (!posted && !CustomChirpsBridge.PostLargeChirpFromEntityWithPortraitImage(fallbackText, winner, winner, portraitImageSource, name))
                CustomChirpsBridge.PostChirpFromEntity(fallbackText, winner, winner, name);

            DebugLog($"Winner victory chirp posted: winner={DescribeEntity(winner, fallbackName)}, venue={FormatEntity(venue)}.");
        }

        private void PostLoserResultChirp(ElectionState state)
        {
            int winnerIndex = state.IsActiveCandidateIndex(state.victoryPartyWinnerIndex)
                ? state.victoryPartyWinnerIndex
                : GetElectionWinnerIndex(state);
            int loserIndex = GetRunnerUpIndex(state, winnerIndex);
            if (loserIndex < 0)
                return;

            Entity loser = state.GetCandidate(loserIndex);
            Entity winner = state.GetCandidate(winnerIndex);
            if (!IsValidChirpCitizen(loser))
            {
                DebugLog($"Skipped loser result chirp: loser entity is not available ({FormatEntity(loser)}).");
                return;
            }

            string loserFallbackName = ElectionState.GetCandidateFallbackName(loserIndex);
            string winnerFallbackName = ElectionState.GetCandidateFallbackName(winnerIndex);
            string loserName = GetCandidateChirpName(loser);
            string winnerName = GetCandidateChirpName(winner);
            int portraitIndex = state.GetCandidatePortraitIndex(loserIndex);
            string portraitImageSource = CandidatePortraitCatalog.GetPortraitImageSource(EntityManager, loser, portraitIndex);
            float marginPct = GetResultMarginPercent(state);
            bool closeRace = marginPct <= 5f;
            int rejectionChance = closeRace ? 70 : 30;
            Unity.Mathematics.Random random = Unity.Mathematics.Random.CreateFromIndex((uint)math.max(1, state.electionDayKey * 397 + GetVoteSeed(state) + 2027));
            bool rejectsResult = random.NextInt(100) < rejectionChance;
            bool pendingInauguration = state.pendingMayor == winner && !state.pendingMayorInaugurated;
            string winnerRoleText = pendingInauguration
                ? L("Lifecycle.Victory.Loser.Role.MayorElect", "as mayor-elect")
                : L("Lifecycle.Victory.Loser.Role.Mayor", "as mayor");
            string text = rejectsResult
                ? LF("Lifecycle.Victory.Loser.Reject", "I do not accept tonight's result. The margin was {0:0.#}%, and our campaign is asking for a full review of the count.", marginPct)
                : LF("Lifecycle.Victory.Loser.Concede", "Tonight did not go our way. I congratulate {0} and wish them well {1}.", "{LINK_1}", winnerRoleText);
            string fallbackText = rejectsResult
                ? text
                : LF("Lifecycle.Victory.Loser.Concede", "Tonight did not go our way. I congratulate {0} and wish them well {1}.", winnerName, winnerRoleText);

            bool posted = !rejectsResult &&
                IsValidChirpCitizen(winner) &&
                CustomChirpsBridge.PostLargeChirpFromEntityWithPortraitImage(text, loser, winner, portraitImageSource, loserName);
            if (!posted && !CustomChirpsBridge.PostLargeChirpFromEntityWithPortraitImage(fallbackText, loser, Entity.Null, portraitImageSource, loserName))
                CustomChirpsBridge.PostChirpFromEntity(fallbackText, loser, Entity.Null, loserName);

            DebugLog($"Loser result chirp posted: loser={DescribeEntity(loser, loserFallbackName)}, winner={DescribeEntity(winner, winnerFallbackName)}, closeRace={closeRace}, marginPct={marginPct:0.##}, rejectionChance={rejectionChance}, rejectsResult={rejectsResult}, winnerName={winnerName}.");
        }

        private int GetElectionWinnerIndex(ElectionState state)
        {
            int candidateCount = state.ActiveCandidateCount;
            int leaderIndex = 0;
            int leaderVotes = state.GetCandidateVotes(0);
            int tiedCount = 1;

            for (int i = 1; i < candidateCount; i++)
            {
                int votes = state.GetCandidateVotes(i);
                if (votes > leaderVotes)
                {
                    leaderVotes = votes;
                    leaderIndex = i;
                    tiedCount = 1;
                }
                else if (votes == leaderVotes)
                {
                    tiedCount++;
                }
            }

            if (tiedCount <= 1)
                return leaderIndex;

            int tieChoice = (state.electionDayKey + state.voteArrivals) % tiedCount;
            for (int i = 0; i < candidateCount; i++)
            {
                if (state.GetCandidateVotes(i) != leaderVotes)
                    continue;

                if (tieChoice == 0)
                    return i;

                tieChoice--;
            }

            return leaderIndex;
        }

        private static float GetResultMarginPercent(ElectionState state)
        {
            int totalVotes = GetTotalVotes(state);
            if (totalVotes == 0)
                return 0f;

            int winnerIndex = GetWinnerIndexFromVotes(state);
            int runnerUpIndex = GetRunnerUpIndex(state, winnerIndex);
            int winnerVotes = state.GetCandidateVotes(winnerIndex);
            int runnerUpVotes = runnerUpIndex >= 0 ? state.GetCandidateVotes(runnerUpIndex) : 0;
            return math.abs(winnerVotes - runnerUpVotes) * 100f / totalVotes;
        }

        private static int GetWinnerIndexFromVotes(ElectionState state)
        {
            int candidateCount = state.ActiveCandidateCount;
            int winnerIndex = 0;
            int winnerVotes = state.GetCandidateVotes(0);
            for (int i = 1; i < candidateCount; i++)
            {
                int votes = state.GetCandidateVotes(i);
                if (votes > winnerVotes)
                {
                    winnerVotes = votes;
                    winnerIndex = i;
                }
            }

            return winnerIndex;
        }

        private static int GetRunnerUpIndex(ElectionState state, int winnerIndex)
        {
            int candidateCount = state.ActiveCandidateCount;
            int runnerUpIndex = -1;
            int runnerUpVotes = -1;
            for (int i = 0; i < candidateCount; i++)
            {
                if (i == winnerIndex)
                    continue;

                int votes = state.GetCandidateVotes(i);
                if (votes > runnerUpVotes)
                {
                    runnerUpVotes = votes;
                    runnerUpIndex = i;
                }
            }

            return runnerUpIndex;
        }

        private static int GetTotalVotes(ElectionState state)
        {
            int total = 0;
            int candidateCount = state.ActiveCandidateCount;
            for (int i = 0; i < candidateCount; i++)
                total += math.max(0, state.GetCandidateVotes(i));

            return total;
        }

        private string GetNamedResultText(ElectionState state)
        {
            int candidateCount = state.ActiveCandidateCount;
            string result = string.Empty;
            for (int i = 0; i < candidateCount; i++)
            {
                if (i > 0)
                    result += ", ";
                result += $"{GetCandidateChirpName(state, i)} {GetCandidateResultPercent(state, i)}%";
            }

            return result;
        }

        private static int GetCandidateResultPercent(ElectionState state, int candidateIndex)
        {
            int totalVotes = GetTotalVotes(state);
            if (totalVotes <= 0)
                return 0;

            return (int)math.round(math.max(0, state.GetCandidateVotes(candidateIndex)) * 100f / totalVotes);
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

        private static int GetVoteSeed(ElectionState state)
        {
            int seed = 0;
            int candidateCount = state.ActiveCandidateCount;
            for (int i = 0; i < candidateCount; i++)
                seed += state.GetCandidateVotes(i) * (17 + i * 14);

            return seed;
        }

        private bool RequestVictoryPartyTrip(Entity citizen, Entity venue, float durationMinutes, int priority)
        {
            if (citizen == Entity.Null ||
                venue == Entity.Null ||
                !EntityManager.Exists(citizen) ||
                !EntityManager.Exists(venue) ||
                !RealisticTripsBridge.CanRequestTrip(citizen, venue))
            {
                return false;
            }

            return RealisticTripsBridge.RequestElectionVictoryPartyTrip(citizen, venue, durationMinutes, priority);
        }

        private static Unity.Mathematics.Random CreateVictoryPartyRandom(Entity entity, int dayKey, int salt)
        {
            int value = math.abs(entity.Index * 73856093 ^ entity.Version * 19349663 ^ dayKey * 83492791 ^ salt);
            return Unity.Mathematics.Random.CreateFromIndex((uint)math.max(1, value));
        }

        private bool TryFindVictoryPartyVenue(out Entity venue)
        {
            if (TryFindCityHallVenue(out venue))
                return true;

            if (TryFindBestVenue(m_LandmarkQuery, out venue))
                return true;

            if (TryFindBestVenue(m_ParkQuery, out venue))
                return true;

            venue = Entity.Null;
            return false;
        }

        private bool TryFindCityHallVenue(out Entity venue)
        {
            venue = Entity.Null;
            if (m_CityHallQuery.IsEmptyIgnoreFilter)
                return false;

            Entity fallback = Entity.Null;
            using (NativeArray<Entity> entities = m_CityHallQuery.ToEntityArray(Allocator.Temp))
            {
                for (int i = 0; i < entities.Length; i++)
                {
                    Entity entity = entities[i];
                    if (fallback == Entity.Null)
                        fallback = entity;

                    string prefabName = GetPrefabName(entity);
                    if (!string.IsNullOrWhiteSpace(prefabName) &&
                        prefabName.IndexOf("City Hall", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        venue = entity;
                        return true;
                    }
                }
            }

            return false;
        }

        private bool TryFindBestVenue(EntityQuery query, out Entity venue)
        {
            venue = Entity.Null;
            if (query.IsEmptyIgnoreFilter)
                return false;

            int bestScore = int.MinValue;
            using (NativeArray<Entity> entities = query.ToEntityArray(Allocator.Temp))
            {
                for (int i = 0; i < entities.Length; i++)
                {
                    Entity entity = entities[i];
                    int score = GetVenueScore(entity);
                    if (venue == Entity.Null || score > bestScore)
                    {
                        venue = entity;
                        bestScore = score;
                    }
                }
            }

            return venue != Entity.Null;
        }

        private int GetVenueScore(Entity venue)
        {
            if (venue == Entity.Null ||
                !EntityManager.Exists(venue) ||
                !EntityManager.HasComponent<PrefabRef>(venue))
            {
                return 0;
            }

            Entity prefab = EntityManager.GetComponentData<PrefabRef>(venue).m_Prefab;
            int score = 0;
            if (EntityManager.HasComponent<AttractionData>(prefab))
                score += EntityManager.GetComponentData<AttractionData>(prefab).m_Attractiveness;

            if (EntityManager.HasComponent<ParkData>(prefab) && EntityManager.HasComponent<Game.Buildings.Park>(venue))
            {
                Game.Buildings.Park park = EntityManager.GetComponentData<Game.Buildings.Park>(venue);
                ParkData parkData = EntityManager.GetComponentData<ParkData>(prefab);
                score += parkData.m_MaintenancePool > 0
                    ? park.m_Maintenance * 100 / parkData.m_MaintenancePool
                    : 100;
            }

            return score;
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

            string prefabName = SanitizeName(GetPrefabName(building), fallback);
            return string.IsNullOrWhiteSpace(prefabName) ? fallback : prefabName;
        }

        private string GetPrefabName(Entity entity)
        {
            if (entity == Entity.Null ||
                !EntityManager.Exists(entity) ||
                !EntityManager.HasComponent<PrefabRef>(entity) ||
                m_PrefabSystem == null)
            {
                return string.Empty;
            }

            try
            {
                Entity prefab = EntityManager.GetComponentData<PrefabRef>(entity).m_Prefab;
                return m_PrefabSystem.GetPrefabName(prefab);
            }
            catch
            {
                return string.Empty;
            }
        }

        private static string SanitizeName(string name, string fallback)
        {
            if (string.IsNullOrWhiteSpace(name))
                return fallback ?? string.Empty;

            return name.Replace("\r", " ").Replace("\n", " ").Trim();
        }

        private bool IsValidVenue(Entity entity)
        {
            return entity != Entity.Null &&
                   EntityManager.Exists(entity) &&
                   EntityManager.HasComponent<Building>(entity) &&
                   !EntityManager.HasComponent<Deleted>(entity) &&
                   !EntityManager.HasComponent<Temp>(entity);
        }

        private bool IsValidChirpCitizen(Entity entity)
        {
            return entity != Entity.Null &&
                   EntityManager.Exists(entity) &&
                   EntityManager.HasComponent<Citizen>(entity) &&
                   !EntityManager.HasComponent<Deleted>(entity) &&
                   !EntityManager.HasComponent<Temp>(entity);
        }

        private void SyncElectionDaySundayOverride(ElectionState state, DateTime now)
        {
            if (!state.electionDayHolidayScheduled ||
                !TryGetElectionDayForBridge(state, out int electionYear, out int electionMonth, out int electionDay))
            {
                RealisticTripsBridge.ClearElectionDaySundayOverride();
                return;
            }

            ElectionUtility.GetCurrentCalendarDate(World, now, out int currentYear, out int currentMonth, out int currentDay);
            int compare = ElectionUtility.CompareCalendarDate(
                electionYear,
                electionMonth,
                electionDay,
                currentYear,
                currentMonth,
                currentDay);
            if (compare < 0)
            {
                RealisticTripsBridge.ClearElectionDaySundayOverride();
                return;
            }

            int daysPerMonth = RealisticTripsBridge.GetDaysPerMonth();
            int electionDayOfYear = (electionMonth - 1) * daysPerMonth + math.clamp(electionDay, 1, daysPerMonth);
            RealisticTripsBridge.SetElectionDaySundayOverride(electionYear, electionDayOfYear, true);
        }

        private void SyncElectionDaySpecialEventSuppression(ElectionState state, DateTime now)
        {
            if (!TryGetElectionDayForBridge(state, out int electionYear, out int electionMonth, out int electionDay))
            {
                RealisticTripsBridge.ClearElectionDaySpecialEventsSuppressed();
                return;
            }

            ElectionUtility.GetCurrentCalendarDate(World, now, out int currentYear, out int currentMonth, out int currentDay);
            int compare = ElectionUtility.CompareCalendarDate(
                electionYear,
                electionMonth,
                electionDay,
                currentYear,
                currentMonth,
                currentDay);
            if (compare < 0)
            {
                RealisticTripsBridge.ClearElectionDaySpecialEventsSuppressed();
                return;
            }

            int daysPerMonth = RealisticTripsBridge.GetDaysPerMonth();
            int electionDayOfYear = (electionMonth - 1) * daysPerMonth + math.clamp(electionDay, 1, daysPerMonth);
            RealisticTripsBridge.SetElectionDaySpecialEventsSuppressed(electionYear, electionDayOfYear, true);
        }

        private static bool TryGetElectionDayForBridge(ElectionState state, out int year, out int month, out int day)
        {
            year = 0;
            month = 0;
            day = 0;

            if (!state.HasCandidates || state.stage == ElectionCampaignStage.None)
                return false;

            if (state.stage == ElectionCampaignStage.Voting &&
                TryGetDateFromDayKey(state.electionDayKey, out year, out month, out day))
            {
                return true;
            }

            if (state.electionYear <= 0 || state.electionMonth <= 0)
                return false;

            year = state.electionYear;
            month = state.electionMonth;
            day = 1;
            return true;
        }

        private static bool TryGetDateFromDayKey(int dayKey, out int year, out int month, out int day)
        {
            year = dayKey / 10000;
            month = dayKey / 100 % 100;
            day = dayKey % 100;
            return year > 0 && month >= 1 && month <= 12 && day >= 1;
        }

        private bool TryPickCandidates(DateTime now, ElectionState state, Entity currentMayor, int requestedCandidateCount, Entity[] candidates)
        {
            requestedCandidateCount = ElectionState.NormalizeCandidateCount(requestedCandidateCount);
            if (candidates == null || candidates.Length < requestedCandidateCount)
                return false;

            for (int i = 0; i < candidates.Length; i++)
                candidates[i] = Entity.Null;

            using (NativeArray<Entity> entities = m_CandidateQuery.ToEntityArray(Allocator.Temp))
            using (NativeArray<Citizen> citizens = m_CandidateQuery.ToComponentDataArray<Citizen>(Allocator.Temp))
            {
                if (entities.Length < requestedCandidateCount)
                {
                    DebugLog($"Candidate selection failed: eligible query returned only {entities.Length} resident entities for requestedCandidateCount={requestedCandidateCount}.");
                    return false;
                }

                uint seed = (uint)math.max(1, now.Year * 10000 + now.Month * 100 + now.Day + (int)m_SimulationSystem.frameIndex);
                Unity.Mathematics.Random random = Unity.Mathematics.Random.CreateFromIndex(seed);

                for (int slot = 0; slot < requestedCandidateCount; slot++)
                    candidates[slot] = PickBestCampaignCandidate(state, entities, citizens, candidates, slot, currentMayor, ref random);

                DebugLog($"Candidate selection attempt complete: queryCount={entities.Length}, seed={seed}, currentMayor={FormatEntity(currentMayor)}, requestedCandidateCount={requestedCandidateCount}, selected={DescribeSelectedCandidates(candidates, requestedCandidateCount)}.");
            }

            for (int i = 0; i < requestedCandidateCount; i++)
            {
                if (candidates[i] == Entity.Null)
                    return false;
            }

            return true;
        }

        private Entity PickBestCampaignCandidate(
            ElectionState state,
            NativeArray<Entity> entities,
            NativeArray<Citizen> citizens,
            Entity[] selectedCandidates,
            int slot,
            Entity currentMayor,
            ref Unity.Mathematics.Random random)
        {
            Entity selected = Entity.Null;
            int bestScore = int.MinValue;
            int bestTieCount = 0;

            for (int i = 0; i < entities.Length; i++)
            {
                Entity candidate = entities[i];
                Citizen citizen = citizens[i];
                if (candidate == currentMayor ||
                    IsAlreadySelectedCandidate(selectedCandidates, slot, candidate) ||
                    !ElectionUtility.IsEligibleResident(EntityManager, candidate, citizen))
                {
                    continue;
                }

                int score =
                    random.NextInt(0, 80) +
                    GetCandidatePartyFitScore(state, slot, candidate, citizen) +
                    GetCandidateFieldDiversityScore(selectedCandidates, slot, candidate, citizen);

                if (score > bestScore)
                {
                    selected = candidate;
                    bestScore = score;
                    bestTieCount = 1;
                }
                else if (score == bestScore)
                {
                    bestTieCount++;
                    if (random.NextInt(bestTieCount) == 0)
                        selected = candidate;
                }
            }

            return selected;
        }

        private int GetCandidateFieldDiversityScore(Entity[] selectedCandidates, int selectedCount, Entity candidate, Citizen citizen)
        {
            if (selectedCount <= 0)
                return 0;

            int age = (int)citizen.GetAge();
            int education = citizen.GetEducationLevel();
            int workType = ElectionUtility.GetWorkType(EntityManager, candidate);
            int wealth = ElectionUtility.GetWealthBracket(EntityManager, candidate);
            bool hasCar = HasRegisteredCar(candidate);
            int score = 0;

            for (int i = 0; i < selectedCount; i++)
            {
                Entity other = selectedCandidates[i];
                if (other == Entity.Null || !EntityManager.Exists(other) || !EntityManager.HasComponent<Citizen>(other))
                    continue;

                Citizen otherCitizen = EntityManager.GetComponentData<Citizen>(other);
                int otherAge = (int)otherCitizen.GetAge();
                int otherEducation = otherCitizen.GetEducationLevel();
                int otherWorkType = ElectionUtility.GetWorkType(EntityManager, other);
                int otherWealth = ElectionUtility.GetWealthBracket(EntityManager, other);

                score += math.abs(age - otherAge) * 70;
                score += math.abs(education - otherEducation) * 35;
                score += math.abs(wealth - otherWealth) * 35;
                if (GetWorkTypeCategory(workType) != GetWorkTypeCategory(otherWorkType))
                    score += 60;
                if (hasCar != HasRegisteredCar(other))
                    score += 30;
            }

            return score;
        }

        private int GetCandidatePartyFitScore(ElectionState state, int partyIndex, Entity candidate, Citizen citizen)
        {
            if (!ArePartiesEnabled() || !ElectionState.IsPartyIndex(partyIndex))
                return 0;

            int age = (int)citizen.GetAge();
            int education = citizen.GetEducationLevel();
            int workType = ElectionUtility.GetWorkType(EntityManager, candidate);
            int wealth = ElectionUtility.GetWealthBracket(EntityManager, candidate);
            bool student = workType >= 30;
            bool worker = workType >= 10 && workType < 30;
            bool hasCar = HasRegisteredCar(candidate);
            int score = 0;

            for (int slot = 0; slot < ElectionPartyTags.TagsPerParty; slot++)
                score += GetCandidatePartyTagFitScore(state.GetPartyTagId(partyIndex, slot), age, education, wealth, worker, student, hasCar);

            return score;
        }

        private static int GetCandidatePartyTagFitScore(int tagId, int age, int education, int wealth, bool worker, bool student, bool hasCar)
        {
            switch (ElectionPartyTags.NormalizeId(tagId))
            {
                case ElectionPartyTags.ReformSlate:
                    return age <= (int)CitizenAge.Adult ? 25 : 0;
                case ElectionPartyTags.OrganizedMachine:
                case ElectionPartyTags.JobsFocused:
                    return worker ? 45 : 0;
                case ElectionPartyTags.TransitCoalition:
                    return hasCar ? -20 : 45;
                case ElectionPartyTags.CivilLiberties:
                    return student ? 45 : education >= 2 ? 30 : 0;
                case ElectionPartyTags.LocalRoots:
                    return wealth <= 2 ? 35 : -15;
                case ElectionPartyTags.StudentOutreach:
                    return student || age <= (int)CitizenAge.Adult ? 45 : -15;
                case ElectionPartyTags.BusinessFriendly:
                    return (wealth >= 3 ? 35 : 0) + (worker ? 20 : 0);
                case ElectionPartyTags.OldGuard:
                    return age >= (int)CitizenAge.Elderly ? 35 : -20;
                case ElectionPartyTags.Elitist:
                    return wealth >= 3 ? 30 : -25;
                case ElectionPartyTags.OutOfTouch:
                    return wealth >= 3 || age >= (int)CitizenAge.Elderly ? 20 : -15;
                case ElectionPartyTags.CivicTrust:
                case ElectionPartyTags.Pragmatic:
                    return education >= 2 ? 20 : 0;
                case ElectionPartyTags.Ideological:
                    return education >= 3 || student ? 15 : 0;
                default:
                    return 0;
            }
        }

        private static bool IsAlreadySelectedCandidate(Entity[] candidates, int selectedCount, Entity candidate)
        {
            for (int i = 0; i < selectedCount; i++)
            {
                if (candidates[i] == candidate)
                    return true;
            }

            return false;
        }

        private string DescribeSelectedCandidates(Entity[] candidates, int candidateCount)
        {
            string result = string.Empty;
            for (int i = 0; i < candidateCount; i++)
            {
                if (i > 0)
                    result += "; ";
                result += $"{i}:{DescribeEntity(candidates[i], ElectionState.GetCandidateFallbackName(i))}";
            }

            return result;
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
                        DebugLog($"Temporary mayor selection succeeded: queryCount={entities.Length}, seed={seed}, mayor={DescribeEntity(mayor, L("Lifecycle.Name.TemporaryMayor", "Temporary Mayor"))}.");
                        return true;
                    }
                }
            }

            DebugLog("Temporary mayor selection failed after random attempts.");
            return false;
        }

        private void RunPoll(ref ElectionState state, DateTime now)
        {
            ResetPollState(ref state);

            int population = GetPopulation();
            int targetSampleCount = GetGoodPollTargetSampleCount(population);
            uint seed = (uint)math.max(1, now.Year * 10000 + now.Month * 101 + now.Day + 3109);
            Unity.Mathematics.Random random = Unity.Mathematics.Random.CreateFromIndex(seed);
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
            int eligibleCount = 0;
            int sampleCount = 0;

            using (NativeArray<Entity> entities = m_CandidateQuery.ToEntityArray(Allocator.Temp))
            using (NativeArray<Citizen> citizens = m_CandidateQuery.ToComponentDataArray<Citizen>(Allocator.Temp))
            {
                List<PollSampleCandidate> eligibleCandidates = new List<PollSampleCandidate>(math.min(entities.Length, targetSampleCount));
                for (int i = 0; i < entities.Length; i++)
                {
                    if (!ElectionUtility.IsEligibleVoterResident(EntityManager, entities[i], citizens[i]))
                        continue;

                    float turnoutWeight = GetPollTurnoutWeight(
                        state,
                        entities[i],
                        citizens[i],
                        teenDailyTurnout,
                        adultDailyTurnout,
                        elderlyDailyTurnout);
                    float draw = math.max(0.000001f, random.NextFloat());
                    eligibleCandidates.Add(new PollSampleCandidate
                    {
                        citizenIndex = i,
                        priority = (float)(-Math.Log(draw) / turnoutWeight)
                    });
                }

                eligibleCount = eligibleCandidates.Count;
                sampleCount = math.min(targetSampleCount, eligibleCount);
                eligibleCandidates.Sort((left, right) => left.priority.CompareTo(right.priority));
                for (int i = 0; i < sampleCount; i++)
                {
                    int citizenIndex = eligibleCandidates[i].citizenIndex;

                    float undecidedChance = ElectionUtility.GetUndecidedProbability(EntityManager, entities[citizenIndex], citizens[citizenIndex], state);
                    int decision;
                    if (random.NextFloat() < undecidedChance)
                    {
                        decision = -1;
                    }
                    else
                    {
                        decision = ElectionUtility.PickVoteCandidate(EntityManager, entities[citizenIndex], citizens[citizenIndex], state, random.NextFloat());
                    }

                    AddPollSample(
                        ref state,
                        citizens[citizenIndex],
                        ElectionUtility.GetWealthBracket(EntityManager, entities[citizenIndex]),
                        decision);
                }
            }

            ApplyPollUndecidedFloor(ref state, sampleCount);
            DebugLog($"Poll simulation complete: date={ElectionUtility.FormatCurrentDate(World, now)}, population={population}, sampleRule=goodPoll(min={kGoodPollMinimumSampleCount}, populationShare=1/{kGoodPollPopulationShareDivisor}), targetSample={targetSampleCount}, eligibleResidents={eligibleCount}, actualSample={sampleCount}, seed={seed}, turnoutWeightByAge=teen:{teenDailyTurnout}/adult:{adultDailyTurnout}/elderly:{elderlyDailyTurnout}, pollVotes={FormatPollVoteTotals(state)}, undecided={state.pollUndecided}, donations={FormatDonationTotals(state)}.");
        }

        private static int GetGoodPollTargetSampleCount(int population)
        {
            if (population <= 0)
                return 1;

            int scaledSampleCount = (int)math.ceil(population / (float)kGoodPollPopulationShareDivisor);
            return math.min(population, math.max(kGoodPollMinimumSampleCount, scaledSampleCount));
        }

        private float GetPollTurnoutWeight(ElectionState state, Entity citizenEntity, Citizen citizen, int teenDailyTurnout, int adultDailyTurnout, int elderlyDailyTurnout)
        {
            int dailyTurnout = math.clamp(
                GetDailyTurnoutPercentForAge(citizen.GetAge(), teenDailyTurnout, adultDailyTurnout, elderlyDailyTurnout) +
                GetEducationDailyTurnoutBonusPercent(state, citizen.GetEducationLevel()) +
                ElectionUtility.GetTargetedTurnoutBonusPercent(EntityManager, citizenEntity, citizen, state),
                1,
                100);
            dailyTurnout = ElectionCandidateTags.ApplyTurnoutModifier(dailyTurnout, state);
            float weight = dailyTurnout * ElectionUtility.GetVotingTurnoutMultiplier(citizen);

            return math.max(0.01f, weight);
        }

        private static int GetDailyTurnoutPercentForAge(CitizenAge age, int teenDailyTurnout, int adultDailyTurnout, int elderlyDailyTurnout)
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

        private static void ApplyPollUndecidedFloor(ref ElectionState state, int sampleCount)
        {
            if (sampleCount < 6)
                return;

            int targetUndecided = math.max(1, (int)math.round(sampleCount * 0.04f));
            while (state.pollUndecided < targetUndecided)
            {
                int decision;
                decision = GetLargestPollVoteCandidateIndex(state);
                if (decision < 0)
                {
                    return;
                }

                state.SetCandidatePollVotes(decision, state.GetCandidatePollVotes(decision) - 1);
                state.pollUndecided++;
                MovePollBucketVoteToUndecided(ref state, decision);
            }
        }

        private static int GetLargestPollVoteCandidateIndex(ElectionState state)
        {
            int candidateCount = state.ActiveCandidateCount;
            int bestIndex = -1;
            int bestVotes = 0;
            for (int i = 0; i < candidateCount; i++)
            {
                int votes = state.GetCandidatePollVotes(i);
                if (votes > bestVotes)
                {
                    bestVotes = votes;
                    bestIndex = i;
                }
            }

            return bestIndex;
        }

        private static string FormatPollVoteTotals(ElectionState state)
        {
            int candidateCount = state.ActiveCandidateCount;
            string result = string.Empty;
            for (int i = 0; i < candidateCount; i++)
            {
                if (i > 0)
                    result += "/";
                result += state.GetCandidatePollVotes(i).ToString();
            }

            return result;
        }

        private static string FormatDonationTotals(ElectionState state)
        {
            int candidateCount = state.ActiveCandidateCount;
            string result = string.Empty;
            for (int i = 0; i < candidateCount; i++)
            {
                if (i > 0)
                    result += "/";
                result += state.GetCandidateDonation(i).ToString("n0");
            }

            return result;
        }

        private static void AddPollSample(ref ElectionState state, Citizen citizen, int income, int decision)
        {
            AddPollTotals(ref state, decision);
            AddAgePollBucket(ref state, citizen.GetAge(), decision);
            AddEducationPollBucket(ref state, citizen.GetEducationLevel(), decision);
            AddIncomePollBucket(ref state, income, decision);
        }

        private static void AddPollTotals(ref ElectionState state, int decision)
        {
            if (decision == 0)
                state.pollVotesA++;
            else if (decision == 1)
                state.pollVotesB++;
            else if (decision == 2)
                state.pollVotesC++;
            else if (decision == 3)
                state.pollVotesD++;
            else
                state.pollUndecided++;
        }

        private static void AddAgePollBucket(ref ElectionState state, CitizenAge age, int decision)
        {
            if (age == CitizenAge.Teen)
            {
                AddPollBucket(decision, ref state.pollTeenVotesA, ref state.pollTeenVotesB, ref state.pollTeenVotesC, ref state.pollTeenVotesD, ref state.pollTeenUndecided);
                return;
            }

            if (age == CitizenAge.Elderly)
            {
                AddPollBucket(decision, ref state.pollElderlyVotesA, ref state.pollElderlyVotesB, ref state.pollElderlyVotesC, ref state.pollElderlyVotesD, ref state.pollElderlyUndecided);
                return;
            }

            AddPollBucket(decision, ref state.pollAdultVotesA, ref state.pollAdultVotesB, ref state.pollAdultVotesC, ref state.pollAdultVotesD, ref state.pollAdultUndecided);
        }

        private static void AddEducationPollBucket(ref ElectionState state, int education, int decision)
        {
            switch (math.clamp(education, 0, 4))
            {
                case 0:
                    AddPollBucket(decision, ref state.pollEducation0VotesA, ref state.pollEducation0VotesB, ref state.pollEducation0VotesC, ref state.pollEducation0VotesD, ref state.pollEducation0Undecided);
                    break;
                case 1:
                    AddPollBucket(decision, ref state.pollEducation1VotesA, ref state.pollEducation1VotesB, ref state.pollEducation1VotesC, ref state.pollEducation1VotesD, ref state.pollEducation1Undecided);
                    break;
                case 2:
                    AddPollBucket(decision, ref state.pollEducation2VotesA, ref state.pollEducation2VotesB, ref state.pollEducation2VotesC, ref state.pollEducation2VotesD, ref state.pollEducation2Undecided);
                    break;
                case 3:
                    AddPollBucket(decision, ref state.pollEducation3VotesA, ref state.pollEducation3VotesB, ref state.pollEducation3VotesC, ref state.pollEducation3VotesD, ref state.pollEducation3Undecided);
                    break;
                default:
                    AddPollBucket(decision, ref state.pollEducation4VotesA, ref state.pollEducation4VotesB, ref state.pollEducation4VotesC, ref state.pollEducation4VotesD, ref state.pollEducation4Undecided);
                    break;
            }
        }

        private static void AddIncomePollBucket(ref ElectionState state, int income, int decision)
        {
            switch (math.clamp(income, 0, 4))
            {
                case 0:
                    AddPollBucket(decision, ref state.pollIncome0VotesA, ref state.pollIncome0VotesB, ref state.pollIncome0VotesC, ref state.pollIncome0VotesD, ref state.pollIncome0Undecided);
                    break;
                case 1:
                    AddPollBucket(decision, ref state.pollIncome1VotesA, ref state.pollIncome1VotesB, ref state.pollIncome1VotesC, ref state.pollIncome1VotesD, ref state.pollIncome1Undecided);
                    break;
                case 2:
                    AddPollBucket(decision, ref state.pollIncome2VotesA, ref state.pollIncome2VotesB, ref state.pollIncome2VotesC, ref state.pollIncome2VotesD, ref state.pollIncome2Undecided);
                    break;
                case 3:
                    AddPollBucket(decision, ref state.pollIncome3VotesA, ref state.pollIncome3VotesB, ref state.pollIncome3VotesC, ref state.pollIncome3VotesD, ref state.pollIncome3Undecided);
                    break;
                default:
                    AddPollBucket(decision, ref state.pollIncome4VotesA, ref state.pollIncome4VotesB, ref state.pollIncome4VotesC, ref state.pollIncome4VotesD, ref state.pollIncome4Undecided);
                    break;
            }
        }

        private static void AddPollBucket(int decision, ref int votesA, ref int votesB, ref int votesC, ref int votesD, ref int undecided)
        {
            if (decision == 0)
                votesA++;
            else if (decision == 1)
                votesB++;
            else if (decision == 2)
                votesC++;
            else if (decision == 3)
                votesD++;
            else
                undecided++;
        }

        private static void MovePollBucketVoteToUndecided(ref ElectionState state, int decision)
        {
            MoveAgePollBucketVoteToUndecided(ref state, decision);
            MoveEducationPollBucketVoteToUndecided(ref state, decision);
            MoveIncomePollBucketVoteToUndecided(ref state, decision);
        }

        private static void MoveAgePollBucketVoteToUndecided(ref ElectionState state, int decision)
        {
            if (TryMovePollBucketVoteToUndecided(decision, ref state.pollAdultVotesA, ref state.pollAdultVotesB, ref state.pollAdultVotesC, ref state.pollAdultVotesD, ref state.pollAdultUndecided))
                return;
            if (TryMovePollBucketVoteToUndecided(decision, ref state.pollElderlyVotesA, ref state.pollElderlyVotesB, ref state.pollElderlyVotesC, ref state.pollElderlyVotesD, ref state.pollElderlyUndecided))
                return;
            TryMovePollBucketVoteToUndecided(decision, ref state.pollTeenVotesA, ref state.pollTeenVotesB, ref state.pollTeenVotesC, ref state.pollTeenVotesD, ref state.pollTeenUndecided);
        }

        private static void MoveEducationPollBucketVoteToUndecided(ref ElectionState state, int decision)
        {
            if (TryMovePollBucketVoteToUndecided(decision, ref state.pollEducation2VotesA, ref state.pollEducation2VotesB, ref state.pollEducation2VotesC, ref state.pollEducation2VotesD, ref state.pollEducation2Undecided))
                return;
            if (TryMovePollBucketVoteToUndecided(decision, ref state.pollEducation3VotesA, ref state.pollEducation3VotesB, ref state.pollEducation3VotesC, ref state.pollEducation3VotesD, ref state.pollEducation3Undecided))
                return;
            if (TryMovePollBucketVoteToUndecided(decision, ref state.pollEducation4VotesA, ref state.pollEducation4VotesB, ref state.pollEducation4VotesC, ref state.pollEducation4VotesD, ref state.pollEducation4Undecided))
                return;
            if (TryMovePollBucketVoteToUndecided(decision, ref state.pollEducation1VotesA, ref state.pollEducation1VotesB, ref state.pollEducation1VotesC, ref state.pollEducation1VotesD, ref state.pollEducation1Undecided))
                return;
            TryMovePollBucketVoteToUndecided(decision, ref state.pollEducation0VotesA, ref state.pollEducation0VotesB, ref state.pollEducation0VotesC, ref state.pollEducation0VotesD, ref state.pollEducation0Undecided);
        }

        private static void MoveIncomePollBucketVoteToUndecided(ref ElectionState state, int decision)
        {
            if (TryMovePollBucketVoteToUndecided(decision, ref state.pollIncome2VotesA, ref state.pollIncome2VotesB, ref state.pollIncome2VotesC, ref state.pollIncome2VotesD, ref state.pollIncome2Undecided))
                return;
            if (TryMovePollBucketVoteToUndecided(decision, ref state.pollIncome3VotesA, ref state.pollIncome3VotesB, ref state.pollIncome3VotesC, ref state.pollIncome3VotesD, ref state.pollIncome3Undecided))
                return;
            if (TryMovePollBucketVoteToUndecided(decision, ref state.pollIncome1VotesA, ref state.pollIncome1VotesB, ref state.pollIncome1VotesC, ref state.pollIncome1VotesD, ref state.pollIncome1Undecided))
                return;
            if (TryMovePollBucketVoteToUndecided(decision, ref state.pollIncome4VotesA, ref state.pollIncome4VotesB, ref state.pollIncome4VotesC, ref state.pollIncome4VotesD, ref state.pollIncome4Undecided))
                return;
            TryMovePollBucketVoteToUndecided(decision, ref state.pollIncome0VotesA, ref state.pollIncome0VotesB, ref state.pollIncome0VotesC, ref state.pollIncome0VotesD, ref state.pollIncome0Undecided);
        }

        private static bool TryMovePollBucketVoteToUndecided(int decision, ref int votesA, ref int votesB, ref int votesC, ref int votesD, ref int undecided)
        {
            if (decision == 0)
                return TryMovePollBucketVoteToUndecided(ref votesA, ref undecided);
            if (decision == 1)
                return TryMovePollBucketVoteToUndecided(ref votesB, ref undecided);
            if (decision == 2)
                return TryMovePollBucketVoteToUndecided(ref votesC, ref undecided);
            if (decision == 3)
                return TryMovePollBucketVoteToUndecided(ref votesD, ref undecided);

            return false;
        }

        private static bool TryMovePollBucketVoteToUndecided(ref int decidedVotes, ref int undecided)
        {
            if (decidedVotes <= 0)
                return false;

            decidedVotes--;
            undecided++;
            return true;
        }

        private void CaptureCandidateProfile(Entity candidate, out int age, out int education, out int workType, out int wealth)
        {
            Citizen citizen = EntityManager.GetComponentData<Citizen>(candidate);
            age = (int)citizen.GetAge();
            education = citizen.GetEducationLevel();
            workType = ElectionUtility.GetWorkType(EntityManager, candidate);
            wealth = ElectionUtility.GetWealthBracket(EntityManager, candidate);
        }

        private string GetCandidateProfileIntro(ElectionState state, int candidateIndex)
        {
            Entity candidate = state.GetCandidate(candidateIndex);
            int age = state.GetCandidateAge(candidateIndex);
            int education = state.GetCandidateEducation(candidateIndex);
            int workType = state.GetCandidateWorkType(candidateIndex);
            int wealth = state.GetCandidateWealth(candidateIndex);
            return ElectionCandidateProfileUtility.BuildChirpIntro(EntityManager, candidate, age, education, workType, wealth);
        }

        private static int GetCandidateTagId(ElectionState state, int candidateIndex)
        {
            return state.GetCandidateTagId(candidateIndex);
        }

        private static ElectionEffectDefinition GetCandidateEffectDefinition(ElectionState state, int candidateIndex)
        {
            return GetCandidateEffectDefinition(
                state,
                candidateIndex,
                state.GetCandidateEffectId(candidateIndex),
                state.GetCandidateNegativeSoftened(candidateIndex));
        }

        private static ElectionEffectDefinition GetCandidateEffectDefinition(ElectionState state, int candidateIndex, int effectId, bool negativeSoftened)
        {
            int tagId = state.GetCandidateTagId(candidateIndex);
            float candidatePlatformScale = ElectionCandidateTags.GetPlatformEffectScale(tagId);
            float positiveScale = candidatePlatformScale;
            float negativeScale = (negativeSoftened ? 0.5f : 1f) * candidatePlatformScale;
            if (ArePartiesEnabled())
            {
                int partyIndex = state.GetCandidatePartyIndex(candidateIndex);
                positiveScale *= ElectionPartyTags.GetPositivePlatformScale(state, partyIndex);
                negativeScale *= ElectionPartyTags.GetNegativePlatformScale(state, partyIndex);
            }

            return ElectionEffects.Get(effectId, positiveScale, negativeScale);
        }

        private static ElectionEffectDefinition GetMayorEffectDefinition(ElectionState state)
        {
            int tagId = state.mayorTagId;
            float candidatePlatformScale = ElectionCandidateTags.GetPlatformEffectScale(tagId);
            float positiveScale = candidatePlatformScale;
            float negativeScale = (state.mayorNegativeSoftened ? 0.5f : 1f) * candidatePlatformScale;
            if (ArePartiesEnabled())
            {
                positiveScale *= ElectionPartyTags.GetPositivePlatformScale(state, state.mayorPartyIndex);
                negativeScale *= ElectionPartyTags.GetNegativePlatformScale(state, state.mayorPartyIndex);
            }

            return ElectionEffects.Get(state.mayorEffectId, positiveScale, negativeScale);
        }

        private int PickCandidateTag(Entity candidate, int age, int education, DateTime now, int salt, int excludedTagId)
        {
            int value = math.abs(candidate.Index * 1741 + candidate.Version * 313 + now.Year * 19 + now.Month * 223 + now.Day * 29 + salt + (int)m_SimulationSystem.frameIndex);
            Unity.Mathematics.Random random = Unity.Mathematics.Random.CreateFromIndex((uint)math.max(1, value));
            return ElectionCandidateTags.PickRandomId(ref random, age, education, excludedTagId);
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

        private bool TryAddCityMoney(int amount)
        {
            if (amount <= 0)
                return false;

            Entity city = m_CitySystem.City;
            if (city == Entity.Null || !EntityManager.HasComponent<PlayerMoney>(city))
                return false;

            PlayerMoney money = EntityManager.GetComponentData<PlayerMoney>(city);
            money.Add(amount);
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

        private int GetAchievedMilestoneLevel()
        {
            Entity city = m_CitySystem.City;
            if (city != Entity.Null && EntityManager.HasComponent<MilestoneLevel>(city))
                return math.max(0, EntityManager.GetComponentData<MilestoneLevel>(city).m_AchievedMilestone);

            return 0;
        }

        private int GetEligibleVoterCount()
        {
            int eligibleCount = 0;
            using (NativeArray<Entity> entities = m_CandidateQuery.ToEntityArray(Allocator.Temp))
            using (NativeArray<Citizen> citizens = m_CandidateQuery.ToComponentDataArray<Citizen>(Allocator.Temp))
            {
                for (int i = 0; i < entities.Length; i++)
                {
                    if (ElectionUtility.IsEligibleVoterResident(EntityManager, entities[i], citizens[i]))
                        eligibleCount++;
                }
            }

            return eligibleCount;
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

        private string GetCandidateChirpName(ElectionState state, int candidateIndex)
        {
            return state.IsActiveCandidateIndex(candidateIndex)
                ? GetCandidateChirpName(state.GetCandidate(candidateIndex))
                : L("Lifecycle.Name.Candidate", "the candidate");
        }

        private string GetCandidateChirpName(Entity candidate)
        {
            return GetEntityName(candidate, L("Lifecycle.Name.Candidate", "the candidate"));
        }

        private string GetCityName()
        {
            Entity city = m_CitySystem.City;
            if (city != Entity.Null && EntityManager.Exists(city))
            {
                string name = m_NameSystem?.GetRenderedLabelName(city);
                if (!string.IsNullOrWhiteSpace(name))
                    return name.Trim();
            }

            return L("Lifecycle.Name.City", "the city");
        }

        private void EnsureDistinctCampaignPortraits(ref ElectionState state)
        {
            if (!state.HasCandidates)
                return;

            int candidateCount = state.ActiveCandidateCount;
            int[] originalPortraits = new int[candidateCount];
            for (int i = 0; i < candidateCount; i++)
                originalPortraits[i] = state.GetCandidatePortraitIndex(i);

            int mayorPortraitIndex = GetBaseMayorPortraitIndex(state);
            bool changed = false;

            for (int i = 0; i < candidateCount; i++)
            {
                int portraitIndex = EnsureDistinctCampaignPortraitIndex(state, i, mayorPortraitIndex, 17 + i * 7901);
                state.SetCandidatePortraitIndex(i, portraitIndex);
                changed |= originalPortraits[i] != portraitIndex;
            }

            if (changed)
            {
                DebugLog($"Campaign portraits normalized: before={FormatIntList(originalPortraits)}, after={DescribeCandidatePortraits(state)}, mayor={mayorPortraitIndex}.");
            }
        }

        private int EnsureDistinctCampaignPortraitIndex(ElectionState state, int candidateIndex, int mayorPortraitIndex, int salt)
        {
            Entity candidate = state.GetCandidate(candidateIndex);
            int currentPortraitIndex = state.GetCandidatePortraitIndex(candidateIndex);
            if (currentPortraitIndex >= 0)
            {
                int normalizedPortraitIndex = CandidatePortraitCatalog.NormalizePortraitIndex(currentPortraitIndex);
                if (IsPortraitDistinctFromPreviousCampaignCandidates(state, candidateIndex, candidate, normalizedPortraitIndex, mayorPortraitIndex))
                    return normalizedPortraitIndex;
            }

            Entity previousCandidate = GetNearestPreviousCandidate(state, candidateIndex);
            int previousPortraitIndex = GetNearestPreviousCandidatePortraitIndex(state, candidateIndex);
            int picked = -1;
            for (int attempt = 0; attempt < 16; attempt++)
            {
                picked = PickDistinctPortraitIndex(
                    candidate,
                    salt + attempt * 104729,
                    state.mayor,
                    mayorPortraitIndex,
                    previousCandidate,
                    previousPortraitIndex);
                if (IsPortraitDistinctFromPreviousCampaignCandidates(state, candidateIndex, candidate, picked, mayorPortraitIndex))
                    return picked;
            }

            return picked >= 0 ? CandidatePortraitCatalog.NormalizePortraitIndex(picked) : CandidatePortraitCatalog.PickPortraitIndex(candidate, salt);
        }

        private bool IsPortraitDistinctFromPreviousCampaignCandidates(ElectionState state, int candidateIndex, Entity candidate, int portraitIndex, int mayorPortraitIndex)
        {
            if (CandidatePortraitCatalog.HasSamePortrait(EntityManager, candidate, portraitIndex, state.mayor, mayorPortraitIndex))
                return false;

            for (int i = 0; i < candidateIndex; i++)
            {
                if (CandidatePortraitCatalog.HasSamePortrait(EntityManager, candidate, portraitIndex, state.GetCandidate(i), state.GetCandidatePortraitIndex(i)))
                    return false;
            }

            return true;
        }

        private static Entity GetNearestPreviousCandidate(ElectionState state, int candidateIndex)
        {
            if (candidateIndex > 0)
                return state.GetCandidate(candidateIndex - 1);

            return Entity.Null;
        }

        private static int GetNearestPreviousCandidatePortraitIndex(ElectionState state, int candidateIndex)
        {
            if (candidateIndex > 0)
                return state.GetCandidatePortraitIndex(candidateIndex - 1);

            return -1;
        }

        private int EnsureDistinctPortraitIndex(
            Entity candidate,
            int currentPortraitIndex,
            int salt,
            Entity excludedCandidateA,
            int excludedPortraitIndexA,
            Entity excludedCandidateB,
            int excludedPortraitIndexB)
        {
            if (currentPortraitIndex >= 0)
            {
                int normalizedPortraitIndex = CandidatePortraitCatalog.NormalizePortraitIndex(currentPortraitIndex);
                if (!CandidatePortraitCatalog.HasSamePortrait(EntityManager, candidate, normalizedPortraitIndex, excludedCandidateA, excludedPortraitIndexA) &&
                    !CandidatePortraitCatalog.HasSamePortrait(EntityManager, candidate, normalizedPortraitIndex, excludedCandidateB, excludedPortraitIndexB))
                {
                    return normalizedPortraitIndex;
                }
            }

            return PickDistinctPortraitIndex(
                candidate,
                salt,
                excludedCandidateA,
                excludedPortraitIndexA,
                excludedCandidateB,
                excludedPortraitIndexB);
        }

        private int PickDistinctPortraitIndex(
            Entity candidate,
            int salt,
            Entity excludedCandidateA,
            int excludedPortraitIndexA,
            Entity excludedCandidateB,
            int excludedPortraitIndexB)
        {
            return CandidatePortraitCatalog.PickDifferentPortraitIndex(
                EntityManager,
                candidate,
                salt,
                excludedCandidateA,
                excludedPortraitIndexA,
                excludedCandidateB,
                excludedPortraitIndexB);
        }

        private static int GetBaseMayorPortraitIndex(ElectionState state)
        {
            return CandidatePortraitCatalog.PickPortraitIndex(state.mayor, 4241);
        }

        private int GetMayorPortraitIndex(ElectionState state)
        {
            Entity firstCandidate = state.GetCandidate(0);
            int firstPortraitIndex = state.GetCandidatePortraitIndex(0);
            Entity secondCandidate = state.GetCandidate(1);
            int secondPortraitIndex = state.GetCandidatePortraitIndex(1);
            int picked = -1;
            for (int attempt = 0; attempt < 16; attempt++)
            {
                picked = PickDistinctPortraitIndex(
                    state.mayor,
                    4241 + attempt * 104729,
                    firstCandidate,
                    firstPortraitIndex,
                    secondCandidate,
                    secondPortraitIndex);
                if (IsMayorPortraitDistinctFromCandidates(state, picked))
                    return picked;
            }

            return picked >= 0 ? CandidatePortraitCatalog.NormalizePortraitIndex(picked) : CandidatePortraitCatalog.PickPortraitIndex(state.mayor, 4241);
        }

        private bool IsMayorPortraitDistinctFromCandidates(ElectionState state, int portraitIndex)
        {
            int candidateCount = state.ActiveCandidateCount;
            for (int i = 0; i < candidateCount; i++)
            {
                if (CandidatePortraitCatalog.HasSamePortrait(EntityManager, state.mayor, portraitIndex, state.GetCandidate(i), state.GetCandidatePortraitIndex(i)))
                    return false;
            }

            return true;
        }

        private int GetPortraitIndexForCitizen(ElectionState state, Entity citizen)
        {
            int candidateCount = state.ActiveCandidateCount;
            for (int i = 0; i < candidateCount; i++)
            {
                if (citizen == state.GetCandidate(i) && state.GetCandidatePortraitIndex(i) >= 0)
                    return state.GetCandidatePortraitIndex(i);
            }

            if (citizen == state.mayor)
                return GetMayorPortraitIndex(state);

            return CandidatePortraitCatalog.PickPortraitIndex(citizen, 4241);
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
                $"state={DescribeState(state)}, candidates={DescribeCandidates(state)}. " +
                "Replacing only the invalid candidate slot and preserving the rest of the campaign.";

            if (report == m_LastInvalidCandidateReport)
                return;

            m_LastInvalidCandidateReport = report;
            DebugLog(report);
        }

        private string DescribeState(ElectionState state)
        {
            return $"version={state.version}, initialized={state.initialized}, stage={state.stage}, accelerated={state.acceleratedCycle}, " +
                   $"democraticTransitionCompleted={state.democraticTransitionCompleted}, " +
                   $"selection={FormatMaybeDate(state.selectionYear, state.selectionMonth)}, poll={FormatMaybeDate(state.pollYear, state.pollMonth)}, election={FormatMaybeDate(state.electionYear, state.electionMonth)}, " +
                   $"candidateCount={state.ActiveCandidateCount}, hasCandidates={state.HasCandidates}, tags={DescribeCandidateTags(state)}, standings={DescribeCandidateStandings(state)}, donations={FormatDonationTotals(state)}, pollVotes={FormatPollVoteTotals(state)}/{state.pollUndecided}, votes={FormatVoteTotals(state)}, mayor={FormatEntity(state.mayor)}, mayorTag={ElectionCandidateTags.Get(state.mayorTagId).Name}";
        }

        private static string FormatIntList(int[] values)
        {
            if (values == null || values.Length == 0)
                return string.Empty;

            string result = string.Empty;
            for (int i = 0; i < values.Length; i++)
            {
                if (i > 0)
                    result += "/";
                result += values[i].ToString();
            }

            return result;
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

        private static string L(string key, string fallback)
        {
            return ElectionLocalization.Translate(key, fallback);
        }

        private static string LF(string key, string fallback, params object[] args)
        {
            return ElectionLocalization.Format(key, fallback, args);
        }

        private void PostElectionChirp(string text, Entity target)
        {
            CustomChirpsBridge.PostChirp(text, DepartmentAccountBridge.CensusBureau, target, L("Lifecycle.Department.ElectionBoard", "Election Board"));
        }

        private void PostElectionChirpWithCandidates(string text, Entity candidateA, Entity candidateB)
        {
            if (IsValidChirpCitizen(candidateA) &&
                IsValidChirpCitizen(candidateB) &&
                CustomChirpsBridge.SupportsChirpWith2Targets() &&
                CustomChirpsBridge.PostChirpWith2Targets(text, DepartmentAccountBridge.CensusBureau, candidateA, candidateB, L("Lifecycle.Department.ElectionBoard", "Election Board")))
            {
                return;
            }

            PostElectionChirp(BuildCandidateLinkFallbackText(text, candidateA, candidateB), Entity.Null);
        }

        private string BuildCandidateLinkFallbackText(string text, Entity candidateA, Entity candidateB)
        {
            return (text ?? string.Empty)
                .Replace("{LINK_1}", GetCandidateChirpName(candidateA))
                .Replace("{LINK_2}", GetCandidateChirpName(candidateB));
        }

        private void PostElectionResultsChirp(
            string textWithCandidateAndVenueLinks,
            string textWithCandidateLinks,
            string textWithVenueLink,
            string fallbackText,
            Entity candidateA,
            Entity candidateB,
            Entity venue)
        {
            bool hasCandidateLinks = IsValidChirpCitizen(candidateA) && IsValidChirpCitizen(candidateB);
            bool hasVenueLink = venue != Entity.Null && IsValidVenue(venue);

            if (hasCandidateLinks &&
                hasVenueLink &&
                CustomChirpsBridge.SupportsChirpWith3Targets() &&
                CustomChirpsBridge.PostChirpWith3Targets(textWithCandidateAndVenueLinks, DepartmentAccountBridge.CensusBureau, candidateA, candidateB, venue, L("Lifecycle.Department.ElectionBoard", "Election Board")))
            {
                return;
            }

            if (hasCandidateLinks &&
                CustomChirpsBridge.SupportsChirpWith2Targets() &&
                CustomChirpsBridge.PostChirpWith2Targets(textWithCandidateLinks, DepartmentAccountBridge.CensusBureau, candidateA, candidateB, L("Lifecycle.Department.ElectionBoard", "Election Board")))
            {
                return;
            }

            if (hasVenueLink)
            {
                PostElectionChirp(textWithVenueLink, venue);
                return;
            }

            PostElectionChirp(fallbackText, Entity.Null);
        }

        private void ClearVoteTrips()
        {
            int cleared = m_VoteTripQuery.CalculateEntityCount();
            if (cleared > 0)
                EntityManager.RemoveComponent(m_VoteTripQuery, ComponentType.ReadWrite<ElectionVoteTrip>());

            if (cleared > 0)
                DebugLog($"Cleared {cleared} election vote trip markers.");
        }
    }
}
