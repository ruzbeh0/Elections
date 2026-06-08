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
        public const int MinimumPopulation = 1000;

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
        private const int kVoteTamperingMinBeforePollCloseMinutes = 30;
        private const int kVoteTamperingMaxBeforePollCloseMinutes = 180;
        private const float kVoteTamperingFireIntensity = 0.85f;
        private const int kStrictVotingIdPassChancePercent = 60;
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

        private struct PollSampleCandidate
        {
            public int citizenIndex;
            public float priority;
        }

        private struct PollingPlaceVoteTally
        {
            public int votesA;
            public int votesB;
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

                    if (voteTrip.chosenCandidate != 0 && voteTrip.chosenCandidate != 1)
                        continue;

                    tallies.TryGetValue(voteTrip.pollingPlace, out PollingPlaceVoteTally tally);
                    if (voteTrip.chosenCandidate == 0)
                        tally.votesA++;
                    else
                        tally.votesB++;

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

                    if (voteTrip.chosenCandidate == 0)
                        result.votesA++;
                    else if (voteTrip.chosenCandidate == 1)
                        result.votesB++;
                }

                tally[0] = result;
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

        public void ForceGeneratePollFromSettings()
        {
            Entity stateEntity = EnsureStateEntity();
            ElectionState state = EntityManager.GetComponentData<ElectionState>(stateEntity);
            if (!RealisticTripsBridge.TryGetCurrentDateTime(out DateTime now))
                return;

            DebugLog("ForceGeneratePoll setting button pressed.");
            if (!HasMinimumPopulation("force generate poll"))
            {
                PostElectionChirp($"The Election Board will not generate a poll until the city reaches {MinimumPopulation:n0} population.", Entity.Null);
                return;
            }

            PrepareStateForCurrentDate(ref state, now);
            if (state.stage == ElectionCampaignStage.Voting || IsElectionDay(state, now))
            {
                PostElectionChirp("Debug poll generation is unavailable on election day.", Entity.Null);
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

            if (!IsActiveCampaignStage(state.stage) || !state.HasCandidates)
            {
                DebugLog($"Donation rejected: no active race with candidates. candidateIndex={candidateIndex}, tierIndex={tierIndex}, state={DescribeState(state)}");
                PostElectionChirp("Campaign donations are available while an active mayoral race has selected candidates.", Entity.Null);
                EntityManager.SetComponentData(stateEntity, state);
                return;
            }

            if (state.stage == ElectionCampaignStage.Voting ||
                (hasDateTime && IsElectionDay(state, now)))
            {
                DebugLog($"Donation rejected: donations are closed on election day. candidateIndex={candidateIndex}, tierIndex={tierIndex}, state={DescribeState(state)}");
                PostElectionChirp("Campaign donations are closed on election day.", Entity.Null);
                EntityManager.SetComponentData(stateEntity, state);
                return;
            }

            if (!IsDonationOpenStage(state.stage))
            {
                DebugLog($"Donation rejected: donations are not open in stage {state.stage}. candidateIndex={candidateIndex}, tierIndex={tierIndex}, state={DescribeState(state)}");
                PostElectionChirp("Campaign donations are available before election day while an active mayoral race has selected candidates.", Entity.Null);
                EntityManager.SetComponentData(stateEntity, state);
                return;
            }

            if (candidateIndex != 0 && candidateIndex != 1)
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
                PostElectionChirp("Civic programs are available before election day while an active mayoral race has selected candidates.", Entity.Null);
                EntityManager.SetComponentData(stateEntity, state);
                return;
            }

            int dayKey = ElectionUtility.CurrentCalendarDayKey(World, now);
            if (state.supportProgramDayKey == dayKey)
            {
                string usedLabel = GetSupportProgramLabel(state.supportProgramIdToday);
                DebugLog($"Support program rejected: already funded today. requested={program.Title}, usedToday={usedLabel}, dayKey={dayKey}.");
                PostElectionChirp($"Only one civic program can be funded per day. Today's program is {usedLabel}.", Entity.Null);
                EntityManager.SetComponentData(stateEntity, state);
                return;
            }

            if (program.Type == ElectionSupportProgramType.ElectionDayHoliday &&
                state.electionDayHolidayScheduled)
            {
                DebugLog("Support program rejected: election day holiday is already scheduled.");
                PostElectionChirp("Election day is already scheduled as a holiday.", Entity.Null);
                EntityManager.SetComponentData(stateEntity, state);
                return;
            }

            int amount = ElectionSupportPrograms.GetCost(state.campaignDonationAmount);
            if (!TrySpendCityMoney(amount))
            {
                DebugLog($"Support program rejected: city could not spend {amount:n0} for {program.Title}.");
                PostElectionChirp($"The city does not have enough money to fund {program.Title} for {amount:n0}.", Entity.Null);
                EntityManager.SetComponentData(stateEntity, state);
                return;
            }

            ApplySupportProgram(ref state, program.Type);
            state.supportProgramDayKey = dayKey;
            state.supportProgramIdToday = program.Index;
            SyncElectionDaySundayOverride(state, now);

            string outcome = GetSupportProgramOutcome(state, program.Type);
            DebugLog($"Support program funded: program={program.Title}, cost={amount:n0}, dayKey={dayKey}, outcome={outcome}.");
            PostElectionChirp($"{program.Title} funded for {amount:n0}. {outcome}", Entity.Null);
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
                PostElectionChirp("Mayoral platform meetings are only available before election day while an active race has selected candidates.", Entity.Null);
                EntityManager.SetComponentData(stateEntity, state);
                return;
            }

            if (candidateIndex != 0 && candidateIndex != 1)
            {
                DebugLog($"Bribe rejected: invalid candidateIndex={candidateIndex}.");
                EntityManager.SetComponentData(stateEntity, state);
                return;
            }

            if (HasPendingBribeMeeting(state) || state.bribeBlockedUntilTicks > now.Ticks)
            {
                DebugLog($"Bribe rejected: mayor schedule is blocked until {new DateTime(state.bribeBlockedUntilTicks):O}.");
                PostElectionChirp("The mayor's schedule is already reserved for a candidate platform meeting attempt.", state.mayor);
                EntityManager.SetComponentData(stateEntity, state);
                return;
            }

            Entity candidate = candidateIndex == 0 ? state.candidateA : state.candidateB;
            string candidateName = GetEntityName(candidate, candidateIndex == 0 ? "Candidate A" : "Candidate B");
            string mayorName = GetEntityName(state.mayor, "the mayor");
            int bribeAmount = GetCampaignBribeAmount(state);

            if (!TrySpendCityMoney(bribeAmount))
            {
                DebugLog($"Bribe rejected: city could not spend {bribeAmount:n0} for target={candidateName}.");
                PostElectionChirp($"The city does not have enough money to fund a {bribeAmount:n0} mayoral outreach effort.", Entity.Null);
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
                PostElectionChirp("Mayoral endorsements are only available before election day while an active race has selected candidates.", Entity.Null);
                EntityManager.SetComponentData(stateEntity, state);
                return;
            }

            if (candidateIndex != 0 && candidateIndex != 1)
            {
                DebugLog($"Endorsement rejected: invalid candidateIndex={candidateIndex}.");
                EntityManager.SetComponentData(stateEntity, state);
                return;
            }

            if (state.mayorEndorsementCandidateIndex == 0 || state.mayorEndorsementCandidateIndex == 1)
            {
                DebugLog($"Endorsement rejected: mayor already endorsed candidateIndex={state.mayorEndorsementCandidateIndex}, candidate={FormatEntity(state.mayorEndorsementCandidate)}.");
                PostElectionChirp("The mayor has already endorsed a candidate in this election cycle.", state.mayor);
                EntityManager.SetComponentData(stateEntity, state);
                return;
            }

            if (HasPendingBribeMeeting(state) || state.bribeBlockedUntilTicks > now.Ticks)
            {
                DebugLog($"Endorsement rejected: mayor schedule is blocked until {new DateTime(state.bribeBlockedUntilTicks):O}.");
                PostElectionChirp("The mayor's schedule is already reserved for a campaign action today.", state.mayor);
                EntityManager.SetComponentData(stateEntity, state);
                return;
            }

            Entity candidate = candidateIndex == 0 ? state.candidateA : state.candidateB;
            string candidateName = GetEntityName(candidate, candidateIndex == 0 ? "Candidate A" : "Candidate B");
            string mayorName = GetEntityName(state.mayor, "the mayor");
            int bribeAmount = GetCampaignBribeAmount(state);

            if (!TrySpendCityMoney(bribeAmount))
            {
                DebugLog($"Endorsement rejected: city could not spend {bribeAmount:n0} for target={candidateName}.");
                PostElectionChirp($"The city does not have enough money to fund a {bribeAmount:n0} mayoral endorsement effort.", Entity.Null);
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
                PostElectionChirp("Vote-count tampering can only be arranged before election day while an active race has selected candidates.", Entity.Null);
                EntityManager.SetComponentData(stateEntity, state);
                return;
            }

            if (beneficiaryCandidateIndex != 0 && beneficiaryCandidateIndex != 1)
            {
                DebugLog($"Vote tampering rejected: invalid beneficiaryCandidateIndex={beneficiaryCandidateIndex}.");
                EntityManager.SetComponentData(stateEntity, state);
                return;
            }

            if (state.voteTamperingCandidateIndex == 0 || state.voteTamperingCandidateIndex == 1)
            {
                DebugLog($"Vote tampering rejected: an operation is already planned for candidateIndex={state.voteTamperingCandidateIndex}, candidate={FormatEntity(state.voteTamperingCandidate)}.");
                PostElectionChirp("A vote-count tampering operation is already planned for this election cycle.", Entity.Null);
                EntityManager.SetComponentData(stateEntity, state);
                return;
            }

            if (HasPendingBribeMeeting(state) || state.bribeBlockedUntilTicks > now.Ticks)
            {
                DebugLog($"Vote tampering rejected: mayor schedule is blocked until {new DateTime(state.bribeBlockedUntilTicks):O}.");
                PostElectionChirp("The mayor's schedule is already reserved for a campaign action today.", state.mayor);
                EntityManager.SetComponentData(stateEntity, state);
                return;
            }

            Entity beneficiary = beneficiaryCandidateIndex == 0 ? state.candidateA : state.candidateB;
            string beneficiaryName = GetEntityName(beneficiary, beneficiaryCandidateIndex == 0 ? "Candidate A" : "Candidate B");
            int bribeAmount = GetCampaignBribeAmount(state);

            if (!TrySpendCityMoney(bribeAmount))
            {
                DebugLog($"Vote tampering rejected: city could not spend {bribeAmount:n0} for beneficiary={beneficiaryName}.");
                PostElectionChirp($"The city does not have enough money to fund a {bribeAmount:n0} vote-count operation.", Entity.Null);
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
            Entity stateEntity = EnsureStateEntity();
            ElectionState state = EntityManager.GetComponentData<ElectionState>(stateEntity);
            if (!RealisticTripsBridge.TryGetCurrentDateTime(out DateTime now))
                return;

            PrepareStateForCurrentDate(ref state, now);
            ProcessPendingBribeMeeting(ref state, now);

            if (!IsDonationOpenStage(state.stage) || !state.HasCandidates || IsElectionDay(state, now))
            {
                DebugLog($"Strict voting ID proposal rejected: no active race with candidates. state={DescribeState(state)}");
                PostElectionChirp("Voting ID legislation can only be proposed before election day while an active race has selected candidates.", Entity.Null);
                EntityManager.SetComponentData(stateEntity, state);
                return;
            }

            if (state.strictVotingIdLawPassed)
            {
                DebugLog("Strict voting ID proposal rejected: law has already passed this election cycle.");
                PostElectionChirp("The stricter voting ID legislation has already passed for this election cycle.", state.mayor);
                EntityManager.SetComponentData(stateEntity, state);
                return;
            }

            if (state.strictVotingIdProposalPending)
            {
                DebugLog($"Strict voting ID proposal rejected: pending outcome due at {new DateTime(state.strictVotingIdChirpUtcTicks):O} UTC.");
                PostElectionChirp("The mayor is already waiting on the voting ID legislation outcome.", state.mayor);
                EntityManager.SetComponentData(stateEntity, state);
                return;
            }

            if (HasPendingBribeMeeting(state) || state.bribeBlockedUntilTicks > now.Ticks)
            {
                DebugLog($"Strict voting ID proposal rejected: mayor schedule is blocked until {new DateTime(state.bribeBlockedUntilTicks):O}.");
                PostElectionChirp("The mayor's schedule is already reserved for a campaign action today.", state.mayor);
                EntityManager.SetComponentData(stateEntity, state);
                return;
            }

            int bribeAmount = GetCampaignBribeAmount(state);
            if (!TrySpendCityMoney(bribeAmount))
            {
                DebugLog($"Strict voting ID proposal rejected: city could not spend {bribeAmount:n0}.");
                PostElectionChirp($"The city does not have enough money to fund a {bribeAmount:n0} voting ID legislation proposal.", Entity.Null);
                EntityManager.SetComponentData(stateEntity, state);
                return;
            }

            RegisterMayorBribe(ref state, bribeAmount);
            Unity.Mathematics.Random random = CreateCampaignRandom(
                now,
                82111 + state.electionDayKey + state.candidateAEffectId * 17 + state.candidateBEffectId * 31);
            bool passed = random.NextInt(100) < kStrictVotingIdPassChancePercent;
            state.bribeDayKey = ElectionUtility.CurrentCalendarDayKey(World, now);
            state.bribeBlockedUntilTicks = GetRestOfDayBlockTicks(now);
            ResetBribeMeetingState(ref state);
            state.strictVotingIdProposalPending = true;
            state.strictVotingIdProposalPassed = passed;
            state.strictVotingIdChirpUtcTicks = DateTime.UtcNow.AddMinutes(1).Ticks;
            state.strictVotingIdChirpSent = false;

            DebugLog($"Strict voting ID proposal scheduled: cost={bribeAmount:n0}, passed={passed}, chirpDue={new DateTime(state.strictVotingIdChirpUtcTicks):O} UTC, blockedUntil={new DateTime(state.bribeBlockedUntilTicks):O}.");
            EntityManager.SetComponentData(stateEntity, state);
        }

        private static long GetRestOfDayBlockTicks(DateTime now)
        {
            return now.Date.AddDays(1).Ticks;
        }

        private static void RegisterCandidateCorruptionRisk(ref ElectionState state, int candidateIndex)
        {
            if (candidateIndex == 0)
                state.candidateACorruptionRiskSteps = math.min(kCorruptionRiskMaxSteps, state.candidateACorruptionRiskSteps + 1);
            else if (candidateIndex == 1)
                state.candidateBCorruptionRiskSteps = math.min(kCorruptionRiskMaxSteps, state.candidateBCorruptionRiskSteps + 1);
        }

        private static int GetCandidateCorruptionRiskSteps(ElectionState state, int candidateIndex)
        {
            return candidateIndex == 0 ? state.candidateACorruptionRiskSteps : state.candidateBCorruptionRiskSteps;
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
            Entity candidate = candidateIndex == 0 ? state.candidateA : candidateIndex == 1 ? state.candidateB : Entity.Null;
            string candidateName = GetEntityName(candidate, "the candidate");
            string mayorName = GetEntityName(state.mayor, "the mayor");

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
            return (state.bribeMeetingCandidateIndex == 0 || state.bribeMeetingCandidateIndex == 1) &&
                   state.bribeMeetingDeadlineTicks > 0;
        }

        private bool TryScheduleBribeMeeting(ref ElectionState state, DateTime now)
        {
            if (!RealisticTripsBridge.IsAvailable)
                return false;

            int candidateIndex = state.bribeMeetingCandidateIndex;
            Entity candidate = candidateIndex == 0 ? state.candidateA : candidateIndex == 1 ? state.candidateB : Entity.Null;
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
                91337 + candidateIndex * 307 + state.candidateAEffectId + state.candidateBEffectId + (int)(state.bribeMeetingDeadlineTicks % 104729));
            if (random.NextInt(100) < kBribeMeetingSuccessChancePercent)
            {
                ConvinceCandidateToChangePlatform(ref state, now, candidateIndex, candidateName, mayorName);
            }
            else
            {
                DebugLog($"Bribe meeting completed without platform change: mayor={DescribeEntity(state.mayor, mayorName)}, target={DescribeEntity(candidate, candidateName)}, venue={FormatEntity(state.bribeMeetingVenue)}, amount={GetCampaignBribeAmount(state):n0}.");
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
            string senderName = GetEntityName(sender, useMayorSender ? "the mayor" : "the candidate");
            int portraitIndex = useMayorSender
                ? GetMayorPortraitIndex(state)
                : candidateIndex == 0 ? state.candidateAPortraitIndex : state.candidateBPortraitIndex;
            string portraitImageSource = CandidatePortraitCatalog.GetPortraitImageSource(EntityManager, sender, portraitIndex);
            string text = useMayorSender
                ? "Heading to {LINK_3} to meet {LINK_2} about a campaign platform discussion."
                : "Heading to {LINK_3} to meet {LINK_2} about the campaign platform.";

            bool posted = IsValidChirpCitizen(target2) &&
                CustomChirpsBridge.PostLargeChirpFromEntityWithPortraitImage(text, sender, target1, target2, target3, portraitImageSource, senderName);
            if (!posted)
                CustomChirpsBridge.PostChirpFromEntityWith3Targets(text, sender, target1, target2, target3, senderName);
        }

        private void PostBribeMeetingUnableChirp(ElectionState state, Entity candidate, string mayorName, string candidateName)
        {
            bool hasMayorLink = IsValidChirpCitizen(state.mayor);
            bool hasCandidateLink = IsValidChirpCitizen(candidate);
            if (hasMayorLink && hasCandidateLink)
            {
                CustomChirpsBridge.PostChirpWith2Targets(
                    "Election Board reports {LINK_1} tried to meet {LINK_2} for a campaign platform discussion, but no suitable time and leisure venue could be arranged within 24 in-game hours.",
                    DepartmentAccountBridge.CensusBureau,
                    state.mayor,
                    candidate,
                    "Election Board");
                return;
            }

            Entity target = hasMayorLink ? state.mayor : hasCandidateLink ? candidate : Entity.Null;
            string subject = hasMayorLink ? "{LINK_1}" : mayorName;
            string targetText = hasCandidateLink ? "{LINK_1}" : candidateName;
            PostElectionChirp(
                $"Election Board reports {subject} tried to meet {targetText} for a campaign platform discussion, but no suitable time and leisure venue could be arranged within 24 in-game hours.",
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
                stage = ElectionCampaignStage.None,
                lastProcessedDayKey = 0,
                appliedEffectId = 0,
                appliedModifierType1 = -1,
                appliedModifierType2 = -1,
                campaignDonationAmount = ElectionDonationTiers.FixedDonationAmount,
                campaignBribeAmount = BribeAmount,
                corruptionInvestigationMayor = Entity.Null,
                mayorBribeRecipient = Entity.Null,
                outgoingMayor = Entity.Null,
                bribeMeetingCandidateIndex = -1,
                mayorEndorsementCandidateIndex = -1,
                mayorEndorsementCandidate = Entity.Null,
                voteTamperingCandidateIndex = -1,
                voteTamperingCandidate = Entity.Null,
                voteTamperingPollingPlace = Entity.Null,
                supportProgramIdToday = -1
            };
            ResetVictoryPartyState(ref state);
            ResetBribeMeetingState(ref state, true);
            ResetMayorEndorsementState(ref state);
            ResetVoteTamperingState(ref state);
            ResetCandidateCorruptionRiskState(ref state);
            ResetMayorBribeTrackingState(ref state);
            ResetOutgoingMayorState(ref state);
            ResetSupportProgramState(ref state);
            ResetStrictVotingIdState(ref state);
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

            EnsureActiveElectionTiming(ref state);
            EnsureCampaignCosts(ref state);
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
            EnsureTemporaryMayor(ref state, now);
            RepairLegacyMayorEffectId(ref state, now);

            if (IsActiveCampaignStage(state.stage) && !HasValidCandidatePair(state))
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
            return state.campaignBribeAmount > 0 ? state.campaignBribeAmount : BribeAmount;
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
            }
        }

        private static string GetSupportProgramOutcome(ElectionState state, ElectionSupportProgramType type)
        {
            switch (type)
            {
                case ElectionSupportProgramType.ElectionDayHoliday:
                    return "Election day will be treated as a holiday for resident schedules.";
                case ElectionSupportProgramType.TeenVoterEducation:
                    return $"Teen election turnout bonus is now +{state.teenTurnoutBonusPercent}%.";
                case ElectionSupportProgramType.AdultVoterEducation:
                    return $"Adult election turnout bonus is now +{state.adultTurnoutBonusPercent}%.";
                case ElectionSupportProgramType.ElderlyVoterEducation:
                    return $"Elderly election turnout bonus is now +{state.elderlyTurnoutBonusPercent}%.";
                case ElectionSupportProgramType.VoterEducation:
                    return $"Uneducated and poorly educated election turnout bonuses are now +{math.max(state.uneducatedTurnoutBonusPercent, state.educatedTurnoutBonusPercent)}%.";
                default:
                    return "The civic program is active.";
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
            state.voteTamperingProtestChirpUtcTicks = 0;
            state.voteTamperingProtestChirpSent = false;
        }

        private static void ResetCandidateCorruptionRiskState(ref ElectionState state)
        {
            state.candidateACorruptionRiskSteps = 0;
            state.candidateBCorruptionRiskSteps = 0;
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
            state.supportProgramBalanceVersion = kSupportProgramBalanceVersion;
        }

        private static void ResetStrictVotingIdState(ref ElectionState state)
        {
            state.strictVotingIdLawPassed = false;
            state.strictVotingIdProposalPending = false;
            state.strictVotingIdProposalPassed = false;
            state.strictVotingIdChirpUtcTicks = 0;
            state.strictVotingIdChirpSent = false;
        }

        private static void ResetPollState(ref ElectionState state)
        {
            state.pollVotesA = 0;
            state.pollVotesB = 0;
            state.pollUndecided = 0;
            state.pollTeenVotesA = 0;
            state.pollTeenVotesB = 0;
            state.pollTeenUndecided = 0;
            state.pollAdultVotesA = 0;
            state.pollAdultVotesB = 0;
            state.pollAdultUndecided = 0;
            state.pollElderlyVotesA = 0;
            state.pollElderlyVotesB = 0;
            state.pollElderlyUndecided = 0;
            state.pollEducation0VotesA = 0;
            state.pollEducation0VotesB = 0;
            state.pollEducation0Undecided = 0;
            state.pollEducation1VotesA = 0;
            state.pollEducation1VotesB = 0;
            state.pollEducation1Undecided = 0;
            state.pollEducation2VotesA = 0;
            state.pollEducation2VotesB = 0;
            state.pollEducation2Undecided = 0;
            state.pollEducation3VotesA = 0;
            state.pollEducation3VotesB = 0;
            state.pollEducation3Undecided = 0;
            state.pollEducation4VotesA = 0;
            state.pollEducation4VotesB = 0;
            state.pollEducation4Undecided = 0;
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

        private bool HasValidCandidatePair(ElectionState state)
        {
            return state.candidateA != state.candidateB &&
                   state.candidateA != state.mayor &&
                   state.candidateB != state.mayor &&
                   IsValidResidentEntity(state.candidateA) &&
                   IsValidResidentEntity(state.candidateB);
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
            bool candidateAValid = IsValidResidentEntity(state.candidateA) &&
                                   state.candidateA != state.mayor &&
                                   state.candidateA != state.candidateB;
            bool candidateBValid = IsValidResidentEntity(state.candidateB) &&
                                   state.candidateB != state.mayor &&
                                   state.candidateB != state.candidateA;
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

            if (!TryPickReplacementCandidate(now, oldCandidate, otherCandidate, state.mayor, candidateIndex, out Entity replacement))
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
                state.candidateAPortraitIndex = PickDistinctPortraitIndex(
                    replacement,
                    17,
                    state.mayor,
                    GetBaseMayorPortraitIndex(state),
                    state.candidateB,
                    state.candidateBPortraitIndex);
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
                state.candidateBPortraitIndex = PickDistinctPortraitIndex(
                    replacement,
                    7919,
                    state.mayor,
                    GetBaseMayorPortraitIndex(state),
                    state.candidateA,
                    state.candidateAPortraitIndex);
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

        private bool TryPickReplacementCandidate(DateTime now, Entity excludedCandidate, Entity otherCandidate, Entity currentMayor, int candidateIndex, out Entity replacement)
        {
            replacement = Entity.Null;

            using (NativeArray<Entity> entities = m_CandidateQuery.ToEntityArray(Allocator.Temp))
            using (NativeArray<Citizen> citizens = m_CandidateQuery.ToComponentDataArray<Citizen>(Allocator.Temp))
            {
                uint seed = (uint)math.max(1, now.Year * 10000 + now.Month * 100 + now.Day + (int)m_SimulationSystem.frameIndex + 7001 + candidateIndex * 101);
                Unity.Mathematics.Random random = Unity.Mathematics.Random.CreateFromIndex(seed);
                int eligibleCount = 0;
                for (int i = 0; i < entities.Length; i++)
                {
                    Entity candidate = entities[i];
                    if (candidate == excludedCandidate ||
                        candidate == otherCandidate ||
                        candidate == currentMayor ||
                        !ElectionUtility.IsEligibleResident(EntityManager, candidate, citizens[i]))
                    {
                        continue;
                    }

                    eligibleCount++;
                    if (random.NextInt(eligibleCount) == 0)
                        replacement = candidate;
                }

                if (eligibleCount == 0 || replacement == Entity.Null)
                {
                    DebugLog($"Replacement candidate selection failed: slot={candidateIndex}, queryCount={entities.Length}, excluded={FormatEntity(excludedCandidate)}, otherCandidate={FormatEntity(otherCandidate)}, currentMayor={FormatEntity(currentMayor)}.");
                    return false;
                }

                DebugLog($"Replacement candidate selected: slot={candidateIndex}, eligibleCount={eligibleCount}, seed={seed}, replacement={DescribeEntity(replacement, "Replacement Candidate")}.");
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

            ProcessCandidateCorruptionArrestCheck(ref state, now, dayKey);

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

            if (!TryPickCandidates(now, state.mayor, out Entity candidateA, out Entity candidateB))
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
            SetCampaignCostsFromMonthlyBalance(ref state);
            state.candidateANegativeSoftened = false;
            state.candidateBNegativeSoftened = false;
            state.candidateASoftenAttempted = false;
            state.candidateBSoftenAttempted = false;
            ResetPollState(ref state);
            state.candidateAPollResponseChirpSent = true;
            state.candidateBPollResponseChirpSent = true;
            state.candidateAPollResponseChirpUtcTicks = 0;
            state.candidateBPollResponseChirpUtcTicks = 0;
            state.voteRequests = 0;
            state.voteArrivals = 0;
            state.votesA = 0;
            state.votesB = 0;
            ResetVictoryPartyState(ref state);
            ResetBribeMeetingState(ref state, true);
            ResetMayorEndorsementState(ref state);
            ResetVoteTamperingState(ref state);
            ResetCandidateCorruptionRiskState(ref state);
            ResetMayorBribeTrackingState(ref state);
            ResetOutgoingMayorState(ref state);
            ResetSupportProgramState(ref state);
            ResetStrictVotingIdState(ref state);
            int mayorPortraitIndex = GetBaseMayorPortraitIndex(state);
            state.candidateAPortraitIndex = PickDistinctPortraitIndex(candidateA, 17, state.mayor, mayorPortraitIndex, Entity.Null, -1);
            state.candidateBPortraitIndex = PickDistinctPortraitIndex(candidateB, 7919, state.mayor, mayorPortraitIndex, candidateA, state.candidateAPortraitIndex);
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
            DebugLog($"Campaign started: reason={reason}, date={ElectionUtility.FormatCurrentDate(World, now)}, accelerated={accelerated}, A={DescribeEntity(candidateA, "Candidate A")}, B={DescribeEntity(candidateB, "Candidate B")}, effects={state.candidateAEffectId}/{state.candidateBEffectId}, portraits={state.candidateAPortraitIndex}/{state.candidateBPortraitIndex}, donationAmount={state.campaignDonationAmount:n0}, bribeAmount={state.campaignBribeAmount:n0}, poll={pollDate}, election={electionDate}, state={DescribeState(state)}.");

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
            string text = $"I am {{LINK_1}}, {profileIntro}. My platform {effect.Description}.";
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

            ElectionPollSummary summary = ElectionPollUtility.BuildSummary(
                state.pollVotesA,
                state.pollVotesB,
                state.pollUndecided,
                GetEntityName(state.candidateA, "Candidate A"),
                GetEntityName(state.candidateB, "Candidate B"));
            bool hasOpponentLink = IsValidChirpCitizen(opponent);
            string resultComment = BuildPollResponseComment(ownVotes, opponentVotes, summary, hasOpponentLink ? "{LINK_2}" : opponentName);
            string fallbackResultComment = hasOpponentLink
                ? BuildPollResponseComment(ownVotes, opponentVotes, summary, opponentName)
                : resultComment;

            string text = $"{resultComment} Donations are open, and every contribution helps move this race.";
            string fallbackText = $"{fallbackResultComment} Donations are open, and every contribution helps move this race.";

            bool posted = hasOpponentLink &&
                CustomChirpsBridge.PostLargeChirpFromEntityWithPortraitImage(text, candidate, candidate, opponent, portraitImageSource, name);
            if (!posted && !CustomChirpsBridge.PostLargeChirpFromEntityWithPortraitImage(fallbackText, candidate, candidate, portraitImageSource, name))
                CustomChirpsBridge.PostChirpFromEntity(fallbackText, candidate, candidate, name);
        }

        private static string BuildPollResponseComment(int ownVotes, int opponentVotes, ElectionPollSummary summary, string opponentReference)
        {
            if (summary.WithinMargin)
                return $"The latest poll is a statistical dead heat against {opponentReference}.";
            if (ownVotes > opponentVotes)
                return $"The latest poll has us ahead of {opponentReference}, outside the +/-{summary.MarginOfError}% margin of error.";
            if (ownVotes < opponentVotes)
                return $"The latest poll has us behind {opponentReference}, but undecided voters can still move this race.";

            return $"The latest poll has us tied with {opponentReference}.";
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
            bool hasOpponentLink = IsValidChirpCitizen(opponent);
            string text = PickElectionReminderMessage(state, candidateA, now, profileIntro, hasOpponentLink ? "{LINK_2}" : opponentName, effect, opponentEffect);
            string fallbackText = hasOpponentLink
                ? PickElectionReminderMessage(state, candidateA, now, profileIntro, opponentName, effect, opponentEffect)
                : text;

            DebugLog($"Election reminder chirp posted: candidate={DescribeEntity(candidate, fallbackName)}, opponent={DescribeEntity(opponent, opponentFallbackName)}, effectId={effectId}, opponentEffectId={opponentEffectId}, portraitIndex={portraitIndex}.");
            bool posted = hasOpponentLink &&
                CustomChirpsBridge.PostLargeChirpFromEntityWithPortraitImage(text, candidate, candidate, opponent, portraitImageSource, name);
            if (!posted && !CustomChirpsBridge.PostLargeChirpFromEntityWithPortraitImage(fallbackText, candidate, candidate, portraitImageSource, name))
                CustomChirpsBridge.PostChirpFromEntity(fallbackText, candidate, candidate, name);
        }

        private string PickElectionReminderMessage(
            ElectionState state,
            bool candidateA,
            DateTime now,
            string profileIntro,
            string opponentReference,
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
                    return $"Tomorrow is election day. Polls are open from {votingWindow}, and I am asking for your vote.";
                case 1:
                    return $"Tomorrow, residents choose the next mayor. My platform {effect.PositiveImpact.Sentence}, and every vote can shape the city.";
                case 2:
                    return $"Make a plan to vote tomorrow from {votingWindow}. This race is about {effect.PositiveImpact.Label.ToLowerInvariant()}, and your voice matters.";
                case 3:
                    return $"Tomorrow's election is a choice: my platform {effect.PositiveImpact.Sentence}, while {opponentReference}'s platform {opponentEffect.NegativeImpact.Sentence}.";
                case 4:
                    return $"{opponentReference} still has to explain why their platform {opponentEffect.NegativeImpact.Sentence}. Tomorrow, voters can demand better.";
                default:
                    return $"One day remains before the election, and I am ready to serve. Please vote tomorrow from {votingWindow}.";
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
            string donationText = $"your campaign support totaling {amount:n0}";
            string softeningText = softenedPlatform
                ? $" The campaign has softened its platform: {softenedLabel} changed from {softenedPreviousValue} to {softenedCurrentValue}."
                : string.Empty;
            string text = $"Thank you for {donationText}. Total donated to my campaign so far is {totalDonation:n0}.{softeningText}";
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
            if (candidateIndex != 0 && candidateIndex != 1)
            {
                state.mayorEndorsementChirpSent = true;
                state.mayorEndorsementChirpUtcTicks = 0;
                return;
            }

            Entity candidate = candidateIndex == 0 ? state.candidateA : state.candidateB;
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
                state.strictVotingIdLawPassed = true;

            PostStrictVotingIdOutcomeChirp(state, state.strictVotingIdProposalPassed);
            state.strictVotingIdProposalPending = false;
            state.strictVotingIdProposalPassed = false;
            state.strictVotingIdChirpSent = true;
            state.strictVotingIdChirpUtcTicks = 0;
        }

        private void PostStrictVotingIdOutcomeChirp(ElectionState state, bool passed)
        {
            Entity mayor = state.mayor;
            string mayorName = GetEntityName(mayor, "the mayor");
            string text = passed
                ? "The stricter voting ID proposal passed. Election staff will apply the new verification rules for this mayoral race."
                : "The stricter voting ID proposal did not pass. Voting rules will stay unchanged for this mayoral race.";

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
            string mayorName = GetEntityName(mayor, "the mayor");
            string candidateName = GetEntityName(candidate, candidateIndex == 0 ? "Candidate A" : "Candidate B");
            bool hasCandidateLink = IsValidChirpCitizen(candidate);

            if (!IsValidChirpCitizen(mayor))
            {
                string candidateReference = hasCandidateLink ? "{LINK_1}" : candidateName;
                PostElectionChirp(
                    $"The mayor endorsed {candidateReference} for mayor. Happy residents may give that endorsement extra weight in this election.",
                    hasCandidateLink ? candidate : Entity.Null);
                DebugLog($"Mayor endorsement chirp posted by Election Board fallback: mayor={DescribeEntity(mayor, mayorName)}, candidate={DescribeEntity(candidate, candidateName)}.");
                return;
            }

            string portraitImageSource = CandidatePortraitCatalog.GetPortraitImageSource(
                EntityManager,
                mayor,
                GetMayorPortraitIndex(state));
            string linkedCandidateReference = hasCandidateLink ? "{LINK_2}" : candidateName;
            string text = $"I endorse {linkedCandidateReference} for mayor. Residents who are happy with the city's direction should know I trust them to carry this work forward.";
            string fallbackText = $"I endorse {candidateName} for mayor. Residents who are happy with the city's direction should know I trust them to carry this work forward.";

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
                DebugLog($"Platform softening attempt failed: candidateIndex={candidateIndex}, totalDonation={totalDonation:n0}, threshold={donationSofteningThreshold:n0}.");
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
                        ? $"{candidateReference} agreed to revise their platform after a mayoral meeting."
                        : $"{candidateReference}'s platform did not change after a mayoral meeting.",
                    hasCandidateLink ? candidate : Entity.Null);
                return;
            }

            string portraitImageSource = CandidatePortraitCatalog.GetPortraitImageSource(
                EntityManager,
                mayor,
                GetMayorPortraitIndex(state));
            string linkedCandidateReference = hasCandidateLink ? "{LINK_2}" : candidateName;
            string text = succeeded
                ? $"I met with {linkedCandidateReference} today to discuss their platform. We found enough common ground that they agreed to revise it. Their updated tradeoff is {newEffect.NegativeImpact.ValueText} {newEffect.NegativeImpact.Label}."
                : $"I met with {linkedCandidateReference} today to discuss their platform. The conversation was constructive, but we still disagreed on key points, and I was not able to persuade them to revise it.";
            string fallbackText = succeeded
                ? $"I met with {candidateName} today to discuss their platform. We found enough common ground that they agreed to revise it. Their updated tradeoff is {newEffect.NegativeImpact.ValueText} {newEffect.NegativeImpact.Label}."
                : $"I met with {candidateName} today to discuss their platform. The conversation was constructive, but we still disagreed on key points, and I was not able to persuade them to revise it.";

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
            string mayorName = GetEntityName(mayor, "the mayor");
            bool hasMayorLink = IsValidChirpCitizen(mayor);
            CustomChirpsBridge.PostChirp(
                $"Police confirm {(hasMayorLink ? "{LINK_1}" : mayorName)} is facing a corruption investigation after allegations of mayoral campaign bribery.",
                DepartmentAccountBridge.Police,
                hasMayorLink ? mayor : Entity.Null,
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
            state.votingClosedChirpSent = false;
            ResetVictoryPartyState(ref state);
            ResetBribeMeetingState(ref state, true);
            SyncElectionDaySundayOverride(state, now);
            SyncElectionDaySpecialEventSuppression(state, now);
            string votingWindow = FormatVotingWindow(state.votingStartMinute, state.votingEndMinute);
            string resultsTime = ElectionUtility.FormatHourText(state.resultsAnnouncementMinute);
            DebugLog($"Election started: date={ElectionUtility.FormatCurrentDate(World, now)}, electionDayKey={state.electionDayKey}, votingWindow={ElectionUtility.FormatClockTime(state.votingStartMinute)}-{ElectionUtility.FormatClockTime(state.votingEndMinute)}, results={ElectionUtility.FormatClockTime(state.resultsAnnouncementMinute)}, holidayScheduled={state.electionDayHolidayScheduled}, A={DescribeEntity(state.candidateA, "Candidate A")}, B={DescribeEntity(state.candidateB, "Candidate B")}.");

            PostElectionChirpWithCandidates(
                $"Election day has begun on {ElectionUtility.FormatCurrentDate(World, now)} for {{LINK_1}} and {{LINK_2}}. Polls are open from {votingWindow} at education, welfare, administration, and postal buildings. You can watch voting in real time by clicking the Voting sites button to view the overlay. Results will be announced at {resultsTime}.",
                state.candidateA,
                state.candidateB);
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
                $"All voting sites are now closed. Results of the election will be announced at {resultsTime}.",
                Entity.Null);
            DebugLog($"Voting closed chirp posted: date={ElectionUtility.FormatCurrentDate(World, now)}, electionDayKey={state.electionDayKey}, votingEnd={ElectionUtility.FormatClockTime(state.votingEndMinute)}, results={ElectionUtility.FormatClockTime(GetResultsAnnouncementMinute(state))}.");
        }

        private void ProcessVoteTampering(ref ElectionState state, DateTime now)
        {
            if (state.stage != ElectionCampaignStage.Voting ||
                !state.HasCandidates ||
                state.voteTamperingCandidateIndex != 0 && state.voteTamperingCandidateIndex != 1 ||
                state.voteTamperingCandidate == Entity.Null)
            {
                return;
            }

            if (state.voteTamperingCandidate != (state.voteTamperingCandidateIndex == 0 ? state.candidateA : state.candidateB))
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
                    65171 + state.voteTamperingCandidateIndex * 917 + state.electionDayKey + tally.votesA * 31 + tally.votesB * 43);
                StartPollingPlaceFire(pollingPlace, now);
                CalculateInitialTamperingLoss(state.voteTamperingCandidateIndex, tally, ref random, out int lostA, out int lostB);
                ApplyVoteLoss(ref state, lostA, lostB);
                state.voteTamperingPollingPlace = pollingPlace;
                state.voteTamperingFireStarted = true;
                state.voteTamperingLostVotesA += lostA;
                state.voteTamperingLostVotesB += lostB;
                PostVoteTamperingLossChirp(state, pollingPlace, lostA, lostB, destroyed: false);
                ScheduleVoteTamperingProtestChirp(ref state);
                DebugLog($"Vote tampering fire started: beneficiaryIndex={state.voteTamperingCandidateIndex}, pollingPlace={FormatEntity(pollingPlace)}, tallyA={tally.votesA}, tallyB={tally.votesB}, lostA={lostA}, lostB={lostB}, scheduledMinute={state.voteTamperingScheduledMinute}, date={ElectionUtility.FormatCurrentDate(World, now)} {now:HH:mm}.");
            }

            if (state.voteTamperingResolved ||
                state.voteTamperingPollingPlace == Entity.Null ||
                !EntityManager.Exists(state.voteTamperingPollingPlace) ||
                !EntityManager.HasComponent<Destroyed>(state.voteTamperingPollingPlace))
            {
                return;
            }

            PollingPlaceVoteTally currentTally = TallyVotesForPollingPlace(state.electionDayKey, state.voteTamperingPollingPlace);
            int remainingLostA = math.max(0, currentTally.votesA - state.voteTamperingLostVotesA);
            int remainingLostB = math.max(0, currentTally.votesB - state.voteTamperingLostVotesB);
            ApplyVoteLoss(ref state, remainingLostA, remainingLostB);
            state.voteTamperingLostVotesA += remainingLostA;
            state.voteTamperingLostVotesB += remainingLostB;
            state.voteTamperingResolved = true;

            if (remainingLostA + remainingLostB > 0)
            {
                PostVoteTamperingLossChirp(state, state.voteTamperingPollingPlace, remainingLostA, remainingLostB, destroyed: true);
                ScheduleVoteTamperingProtestChirp(ref state);
            }

            DebugLog($"Vote tampering polling place destroyed: pollingPlace={FormatEntity(state.voteTamperingPollingPlace)}, remainingLostA={remainingLostA}, remainingLostB={remainingLostB}, totalLostA={state.voteTamperingLostVotesA}, totalLostB={state.voteTamperingLostVotesB}.");
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
                        int margin = state.voteTamperingCandidateIndex == 0
                            ? tally.votesB - tally.votesA
                            : tally.votesA - tally.votesB;
                        int opponentVotes = state.voteTamperingCandidateIndex == 0 ? tally.votesB : tally.votesA;
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

        private static void CalculateInitialTamperingLoss(int beneficiaryIndex, PollingPlaceVoteTally tally, ref Unity.Mathematics.Random random, out int lostA, out int lostB)
        {
            lostA = 0;
            lostB = 0;
            int opponentVotes = beneficiaryIndex == 0 ? tally.votesB : tally.votesA;
            if (opponentVotes <= 0)
                return;

            int minLoss = math.max(1, (int)math.floor(opponentVotes * 0.12f));
            int maxLoss = math.max(minLoss, (int)math.ceil(opponentVotes * 0.35f));
            int lost = random.NextInt(minLoss, math.min(opponentVotes, maxLoss) + 1);
            if (beneficiaryIndex == 0)
                lostB = lost;
            else
                lostA = lost;
        }

        private static void ApplyVoteLoss(ref ElectionState state, int lostA, int lostB)
        {
            state.votesA = math.max(0, state.votesA - math.max(0, lostA));
            state.votesB = math.max(0, state.votesB - math.max(0, lostB));
        }

        private void PostVoteTamperingLossChirp(ElectionState state, Entity pollingPlace, int lostA, int lostB, bool destroyed)
        {
            int total = math.max(0, lostA) + math.max(0, lostB);
            if (total <= 0)
                return;

            string locationName = GetBuildingName(pollingPlace, "a voting site");
            string text = destroyed
                ? $"Fire & Rescue confirms {{LINK_1}} was destroyed. Election Board says all remaining ballots at that site were lost: {{LINK_2}} -{lostA:n0}, {{LINK_3}} -{lostB:n0}."
                : $"Fire crews are responding to a fire at {{LINK_1}}. Election Board says {total:n0} ballots were destroyed before they could be counted: {{LINK_2}} -{lostA:n0}, {{LINK_3}} -{lostB:n0}.";
            string fallbackText = destroyed
                ? $"Fire & Rescue confirms {locationName} was destroyed. Election Board says all remaining ballots at that site were lost: {GetEntityName(state.candidateA, "Candidate A")} -{lostA:n0}, {GetEntityName(state.candidateB, "Candidate B")} -{lostB:n0}."
                : $"Fire crews are responding to a fire at {locationName}. Election Board says {total:n0} ballots were destroyed before they could be counted: {GetEntityName(state.candidateA, "Candidate A")} -{lostA:n0}, {GetEntityName(state.candidateB, "Candidate B")} -{lostB:n0}.";

            bool posted = IsValidVenue(pollingPlace) &&
                IsValidChirpCitizen(state.candidateA) &&
                IsValidChirpCitizen(state.candidateB) &&
                CustomChirpsBridge.PostChirpWith3Targets(text, DepartmentAccountBridge.FireRescue, pollingPlace, state.candidateA, state.candidateB, "Fire & Rescue");
            if (!posted)
                CustomChirpsBridge.PostChirp(fallbackText, DepartmentAccountBridge.FireRescue, IsValidVenue(pollingPlace) ? pollingPlace : Entity.Null, "Fire & Rescue");
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

            int affectedIndex;
            if (state.voteTamperingLostVotesA > state.voteTamperingLostVotesB)
                affectedIndex = 0;
            else if (state.voteTamperingLostVotesB > state.voteTamperingLostVotesA)
                affectedIndex = 1;
            else
                affectedIndex = state.voteTamperingCandidateIndex == 0 ? 1 : 0;

            Entity affected = affectedIndex == 0 ? state.candidateA : state.candidateB;
            Entity opponent = affectedIndex == 0 ? state.candidateB : state.candidateA;
            if (!IsValidChirpCitizen(affected))
            {
                state.voteTamperingProtestChirpSent = true;
                state.voteTamperingProtestChirpUtcTicks = 0;
                return;
            }

            string fallbackName = affectedIndex == 0 ? "Candidate A" : "Candidate B";
            string affectedName = GetEntityName(affected, fallbackName);
            string opponentName = GetEntityName(opponent, affectedIndex == 0 ? "Candidate B" : "Candidate A");
            int portraitIndex = affectedIndex == 0 ? state.candidateAPortraitIndex : state.candidateBPortraitIndex;
            string portraitImageSource = CandidatePortraitCatalog.GetPortraitImageSource(EntityManager, affected, portraitIndex);
            int lost = affectedIndex == 0 ? state.voteTamperingLostVotesA : state.voteTamperingLostVotesB;
            string locationName = GetBuildingName(state.voteTamperingPollingPlace, "that voting site");
            bool hasOpponentLink = IsValidChirpCitizen(opponent);
            bool hasLocationLink = IsValidVenue(state.voteTamperingPollingPlace);
            string locationReference = hasLocationLink ? "{LINK_3}" : locationName;
            string opponentReference = hasOpponentLink ? "{LINK_2}" : opponentName;
            string text = $"Our campaign lost {lost:n0} votes after the fire at {locationReference}. This count cannot be trusted, and {opponentReference} should support a full investigation.";
            string fallbackText = $"Our campaign lost {lost:n0} votes after the fire at {locationName}. This count cannot be trusted, and {opponentName} should support a full investigation.";

            bool posted = hasOpponentLink && hasLocationLink &&
                CustomChirpsBridge.PostLargeChirpFromEntityWithPortraitImage(text, affected, affected, opponent, state.voteTamperingPollingPlace, portraitImageSource, affectedName);
            if (!posted && hasOpponentLink)
            {
                string textWithOpponent = $"Our campaign lost {lost:n0} votes after the fire at {locationName}. This count cannot be trusted, and {{LINK_2}} should support a full investigation.";
                posted = CustomChirpsBridge.PostLargeChirpFromEntityWithPortraitImage(textWithOpponent, affected, affected, opponent, portraitImageSource, affectedName);
            }

            if (!posted && !CustomChirpsBridge.PostLargeChirpFromEntityWithPortraitImage(fallbackText, affected, affected, portraitImageSource, affectedName))
                CustomChirpsBridge.PostChirpFromEntity(fallbackText, affected, affected, affectedName);

            state.voteTamperingProtestChirpSent = true;
            state.voteTamperingProtestChirpUtcTicks = 0;
            DebugLog($"Vote tampering protest chirp posted: affected={DescribeEntity(affected, fallbackName)}, lost={lost}, opponent={DescribeEntity(opponent, opponentName)}, pollingPlace={FormatEntity(state.voteTamperingPollingPlace)}.");
        }

        private void ProcessCandidateCorruptionArrestCheck(ref ElectionState state, DateTime now, int dayKey)
        {
            if (state.corruptionArrestCheckCompleted ||
                state.electionDayKey <= 0 ||
                dayKey <= state.electionDayKey ||
                state.stage == ElectionCampaignStage.Voting ||
                !state.HasCandidates)
            {
                return;
            }

            state.corruptionArrestCheckCompleted = true;
            TryArrestCandidateForElectionCorruption(ref state, now, 0);
            TryArrestCandidateForElectionCorruption(ref state, now, 1);

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

            int chancePercent = math.min(kCorruptionRiskMaxSteps * kCorruptionRiskStepPercent, riskSteps * kCorruptionRiskStepPercent);
            Unity.Mathematics.Random random = CreateCampaignRandom(
                now,
                98317 + candidateIndex * 2003 + state.electionDayKey * 17 + riskSteps * 101);
            if (random.NextInt(100) >= chancePercent)
            {
                DebugLog($"Election corruption arrest check cleared: candidateIndex={candidateIndex}, riskSteps={riskSteps}, chance={chancePercent}%.");
                return;
            }

            Entity candidate = candidateIndex == 0 ? state.candidateA : state.candidateB;
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

            string fallbackName = candidateIndex == 0 ? "Candidate A" : "Candidate B";
            string candidateName = GetEntityName(candidate, fallbackName);
            CustomChirpsBridge.PostChirp(
                $"Police confirm {{LINK_1}} has been arrested after an election-corruption investigation. Detectives linked the campaign to {riskSteps} suspicious mayoral campaign action{(riskSteps == 1 ? string.Empty : "s")}.",
                DepartmentAccountBridge.Police,
                candidate,
                "Police Department");
            DebugLog($"Election corruption arrest applied: candidate={DescribeEntity(candidate, candidateName)}, riskSteps={riskSteps}, chance={chancePercent}%, jailTime={criminal.m_JailTime}, flags={criminal.m_Flags}.");
        }

        private bool TryArrestOutgoingMayorForBribery(ref ElectionState state, DateTime now)
        {
            Entity mayor = state.outgoingMayor;
            int riskSteps = GetOutgoingMayorBribeRiskSteps(state);
            if (mayor == Entity.Null || riskSteps <= 0)
                return false;

            int chancePercent = math.min(kCorruptionRiskMaxSteps * kCorruptionRiskStepPercent, riskSteps * kCorruptionRiskStepPercent);
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

            string mayorName = GetEntityName(mayor, "the former mayor");
            CustomChirpsBridge.PostChirp(
                $"Police confirm {{LINK_1}} has been arrested after a mayoral bribery investigation. Detectives linked the former mayor to {riskSteps} suspicious campaign action{(riskSteps == 1 ? string.Empty : "s")}.",
                DepartmentAccountBridge.Police,
                mayor,
                "Police Department");
            DebugLog($"Outgoing mayor bribery arrest applied: mayor={DescribeEntity(mayor, mayorName)}, bribeTotal={state.outgoingMayorBribeTotal:n0}, riskSteps={riskSteps}, chance={chancePercent}%, jailTime={criminal.m_JailTime}, flags={criminal.m_Flags}.");
            return true;
        }

        private void PostOutgoingMayorFarewellChirp(ref ElectionState state, DateTime now)
        {
            Entity mayor = state.outgoingMayor;
            if (mayor == Entity.Null)
                return;

            string mayorName = GetEntityName(mayor, "the former mayor");
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
                ? $"Thank you, {cityName}, for the time I was mayor. I am donating {donation:n0} back to the city as I leave office."
                : $"Thank you, {cityName}, for the time I was mayor. Serving this city has been an honor.";
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

            int bribeAmount = GetCampaignBribeAmount(state);
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
                state.victoryPartyWinnerIndex != 0 && state.victoryPartyWinnerIndex != 1)
            {
                int winnerIndex = GetElectionWinnerIndex(state);
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
                    DebugLog($"Election victory party skipped: no City Hall, landmark, or park venue was available. winnerIndex={winnerIndex}, votesA={state.votesA}, votesB={state.votesB}.");
                    return;
                }

                state.victoryPartyVenue = venue;
                int tripRequestCap = GetVictoryPartyTripRequestCap();
                DebugLog($"Election victory party scheduling started: date={ElectionUtility.FormatCurrentDate(World, now)} {now:HH:mm}, winnerIndex={winnerIndex}, venue={GetBuildingName(venue, "the celebration site")} {FormatEntity(venue)}, maxRequests={tripRequestCap}, batchLimit={kVictoryPartyBatchTripLimit}, batchIntervalMinutes={kVictoryPartyBatchIntervalMinutes}.");
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
            Entity winner = winnerIndexForBatch == 0 ? state.candidateA : state.candidateB;
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

            string venueName = GetBuildingName(venueEntity, "the celebration site");
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
            ResetMayorBribeTrackingState(ref state);
        }

        private void CompleteElection(ref ElectionState state, DateTime now)
        {
            EnsureActiveElectionTiming(ref state);

            int winnerIndex = GetElectionWinnerIndex(state);

            Entity winner = winnerIndex == 0 ? state.candidateA : state.candidateB;
            int effectId = winnerIndex == 0 ? state.candidateAEffectId : state.candidateBEffectId;
            bool winnerNegativeSoftened = winnerIndex == 0 ? state.candidateANegativeSoftened : state.candidateBNegativeSoftened;
            string winnerName = GetEntityName(winner, winnerIndex == 0 ? "Candidate A" : "Candidate B");
            int population = GetPopulation();
            int eligibleVoters = GetEligibleVoterCount();
            int totalVotes = math.max(0, state.votesA + state.votesB);
            int turnoutPct = eligibleVoters > 0 ? (int)math.round(totalVotes * 100f / eligibleVoters) : 0;
            Entity previousMayor = state.mayor;
            CaptureOutgoingMayorForPostElection(ref state, winner);

            state.mayor = winner;
            state.mayorEffectId = effectId;
            state.mayorNegativeSoftened = winnerNegativeSoftened;
            ElectionUtility.GetCurrentCalendarDate(World, now, out int currentYear, out _, out _);
            state.mayorEffectTermYear = state.mayorTermYear != 0 ? state.mayorTermYear : currentYear;
            state.mayorMoneyApplied = false;
            state.stage = ElectionCampaignStage.None;
            state.victoryPartyWinnerIndex = winnerIndex;
            state.victoryPartyElectionDayKey = state.electionDayKey;
            if (state.victoryPartyVenue == Entity.Null && TryFindVictoryPartyVenue(out Entity resultVenue))
                state.victoryPartyVenue = resultVenue;
            state.victoryWinnerChirpSent = false;
            state.victoryLoserChirpSent = false;
            state.victoryWinnerChirpUtcTicks = DateTime.UtcNow.AddMinutes(1).Ticks;
            state.victoryLoserChirpUtcTicks = DateTime.UtcNow.AddMinutes(2).Ticks;
            ResetBribeMeetingState(ref state, true);
            DebugLog($"Election completed: date={ElectionUtility.FormatCurrentDate(World, now)}, winnerIndex={winnerIndex}, winner={DescribeEntity(winner, winnerName)}, previousMayor={FormatEntity(previousMayor)}, outgoingMayor={FormatEntity(state.outgoingMayor)}, outgoingMayorBribeTotal={state.outgoingMayorBribeTotal:n0}, votesA={state.votesA}, votesB={state.votesB}, voteRequests={state.voteRequests}, voteArrivals={state.voteArrivals}, population={population}, eligibleVoters={eligibleVoters}, turnoutPct={turnoutPct}, effectId={effectId}.");

            ElectionEffectDefinition effect = ElectionEffects.Get(effectId, winnerNegativeSoftened);
            Entity announcementVenue = IsValidVenue(state.victoryPartyVenue) ? state.victoryPartyVenue : Entity.Null;
            string candidateAName = GetEntityName(state.candidateA, "Candidate A");
            string candidateBName = GetEntityName(state.candidateB, "Candidate B");
            string namedPartyText = announcementVenue != Entity.Null
                ? $" Supporters are gathering at {GetBuildingName(announcementVenue, "the celebration site")}."
                : string.Empty;
            string linkedPartyText = announcementVenue != Entity.Null
                ? " Supporters are gathering at {LINK_3}."
                : string.Empty;
            string venueOnlyLinkedPartyText = announcementVenue != Entity.Null
                ? " Supporters are gathering at {LINK_1}."
                : string.Empty;
            string turnoutDenominatorText = eligibleVoters > 0
                ? $"eligible voters ({totalVotes:n0} of {eligibleVoters:n0})"
                : $"eligible voters ({totalVotes:n0} votes)";
            string resultsIntro = $"Election results for {ElectionUtility.FormatCurrentDate(World, now)} are final. {winnerName} has been elected mayor. Turnout was {turnoutPct}% of {turnoutDenominatorText}.";
            string linkedCandidateResults = $" Results: {{LINK_1}} {state.votesA:n0}, {{LINK_2}} {state.votesB:n0}.";
            string namedCandidateResults = $" Results: {candidateAName} {state.votesA:n0}, {candidateBName} {state.votesB:n0}.";
            string platformText = $" The new mayor's platform {effect.Description}.";
            PostElectionResultsChirp(
                $"{resultsIntro}{linkedCandidateResults}{platformText}{linkedPartyText}",
                $"{resultsIntro}{linkedCandidateResults}{platformText}{namedPartyText}",
                $"{resultsIntro}{namedCandidateResults}{platformText}{venueOnlyLinkedPartyText}",
                $"{resultsIntro}{namedCandidateResults}{platformText}{namedPartyText}",
                state.candidateA,
                state.candidateB,
                announcementVenue);
            DebugLog($"Scheduled victory result chirps: winnerDue={new DateTime(state.victoryWinnerChirpUtcTicks):O} UTC, loserDue={new DateTime(state.victoryLoserChirpUtcTicks):O} UTC, venue={FormatEntity(state.victoryPartyVenue)}.");

            ClearVoteTrips();
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
            int winnerIndex = state.victoryPartyWinnerIndex == 0 || state.victoryPartyWinnerIndex == 1
                ? state.victoryPartyWinnerIndex
                : GetElectionWinnerIndex(state);
            Entity winner = winnerIndex == 0 ? state.candidateA : state.candidateB;
            if (!IsValidChirpCitizen(winner))
            {
                DebugLog($"Skipped winner victory chirp: winner entity is not available ({FormatEntity(winner)}).");
                return;
            }

            string fallbackName = winnerIndex == 0 ? "Candidate A" : "Candidate B";
            string name = GetEntityName(winner, fallbackName);
            int portraitIndex = winnerIndex == 0 ? state.candidateAPortraitIndex : state.candidateBPortraitIndex;
            string portraitImageSource = CandidatePortraitCatalog.GetPortraitImageSource(EntityManager, winner, portraitIndex);
            Entity venue = IsValidVenue(state.victoryPartyVenue) ? state.victoryPartyVenue : Entity.Null;
            string cityName = GetCityName();
            string text = venue != Entity.Null
                ? $"Thank you, {cityName}. We won tonight, and I am celebrating with supporters at {{LINK_2}}. Tomorrow we get to work."
                : $"Thank you, {cityName}. We won tonight, and I am celebrating with supporters. Tomorrow we get to work.";
            string fallbackText = $"Thank you, {cityName}. We won tonight, and I am celebrating with supporters. Tomorrow we get to work.";

            bool posted = venue != Entity.Null &&
                CustomChirpsBridge.PostLargeChirpFromEntityWithPortraitImage(text, winner, winner, venue, portraitImageSource, name);
            if (!posted && !CustomChirpsBridge.PostLargeChirpFromEntityWithPortraitImage(fallbackText, winner, winner, portraitImageSource, name))
                CustomChirpsBridge.PostChirpFromEntity(fallbackText, winner, winner, name);

            DebugLog($"Winner victory chirp posted: winner={DescribeEntity(winner, fallbackName)}, venue={FormatEntity(venue)}.");
        }

        private void PostLoserResultChirp(ElectionState state)
        {
            int winnerIndex = state.victoryPartyWinnerIndex == 0 || state.victoryPartyWinnerIndex == 1
                ? state.victoryPartyWinnerIndex
                : GetElectionWinnerIndex(state);
            int loserIndex = winnerIndex == 0 ? 1 : 0;
            Entity loser = loserIndex == 0 ? state.candidateA : state.candidateB;
            Entity winner = winnerIndex == 0 ? state.candidateA : state.candidateB;
            if (!IsValidChirpCitizen(loser))
            {
                DebugLog($"Skipped loser result chirp: loser entity is not available ({FormatEntity(loser)}).");
                return;
            }

            string loserFallbackName = loserIndex == 0 ? "Candidate A" : "Candidate B";
            string winnerFallbackName = winnerIndex == 0 ? "Candidate A" : "Candidate B";
            string loserName = GetEntityName(loser, loserFallbackName);
            string winnerName = GetEntityName(winner, winnerFallbackName);
            int portraitIndex = loserIndex == 0 ? state.candidateAPortraitIndex : state.candidateBPortraitIndex;
            string portraitImageSource = CandidatePortraitCatalog.GetPortraitImageSource(EntityManager, loser, portraitIndex);
            float marginPct = GetResultMarginPercent(state);
            bool closeRace = marginPct <= 5f;
            int rejectionChance = closeRace ? 70 : 30;
            Unity.Mathematics.Random random = Unity.Mathematics.Random.CreateFromIndex((uint)math.max(1, state.electionDayKey * 397 + state.votesA * 17 + state.votesB * 31 + 2027));
            bool rejectsResult = random.NextInt(100) < rejectionChance;
            string text = rejectsResult
                ? $"I do not accept tonight's result. The margin was {marginPct:0.#}%, and our campaign is asking for a full review of the count."
                : "Tonight did not go our way. I congratulate {LINK_1} and wish them well as mayor.";
            string fallbackText = rejectsResult
                ? text
                : $"Tonight did not go our way. I congratulate {winnerName} and wish them well as mayor.";

            bool posted = !rejectsResult &&
                IsValidChirpCitizen(winner) &&
                CustomChirpsBridge.PostLargeChirpFromEntityWithPortraitImage(text, loser, winner, portraitImageSource, loserName);
            if (!posted && !CustomChirpsBridge.PostLargeChirpFromEntityWithPortraitImage(fallbackText, loser, Entity.Null, portraitImageSource, loserName))
                CustomChirpsBridge.PostChirpFromEntity(fallbackText, loser, Entity.Null, loserName);

            DebugLog($"Loser result chirp posted: loser={DescribeEntity(loser, loserFallbackName)}, winner={DescribeEntity(winner, winnerFallbackName)}, closeRace={closeRace}, marginPct={marginPct:0.##}, rejectionChance={rejectionChance}, rejectsResult={rejectsResult}, winnerName={winnerName}.");
        }

        private int GetElectionWinnerIndex(ElectionState state)
        {
            if (state.votesA > state.votesB)
                return 0;

            if (state.votesB > state.votesA)
                return 1;

            return (state.electionDayKey + state.voteArrivals) % 2;
        }

        private static float GetResultMarginPercent(ElectionState state)
        {
            int totalVotes = math.max(0, state.votesA + state.votesB);
            if (totalVotes == 0)
                return 0f;

            return math.abs(state.votesA - state.votesB) * 100f / totalVotes;
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

        private bool TryPickCandidates(DateTime now, Entity currentMayor, out Entity candidateA, out Entity candidateB)
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
                    if (entities[index] != currentMayor &&
                        ElectionUtility.IsEligibleResident(EntityManager, entities[index], citizens[index]))
                    {
                        candidateA = entities[index];
                    }
                }

                for (int attempt = 0; attempt < entities.Length * 4 && candidateB == Entity.Null; attempt++)
                {
                    int index = random.NextInt(entities.Length);
                    if (entities[index] != candidateA &&
                        entities[index] != currentMayor &&
                        ElectionUtility.IsEligibleResident(EntityManager, entities[index], citizens[index]))
                    {
                        candidateB = entities[index];
                    }
                }

                DebugLog($"Candidate selection attempt complete: queryCount={entities.Length}, seed={seed}, currentMayor={FormatEntity(currentMayor)}, candidateA={DescribeEntity(candidateA, "Candidate A")}, candidateB={DescribeEntity(candidateB, "Candidate B")}.");
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
            ResetPollState(ref state);

            int samplePercent = math.clamp(Mod.m_Setting?.PollSamplePercent ?? 2, 1, 10);
            int population = GetPopulation();
            int targetSampleCount = math.max(1, (int)math.ceil(population * samplePercent / 100f));
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

                    float probabilityA = ElectionUtility.GetVoteProbabilityForA(EntityManager, entities[citizenIndex], citizens[citizenIndex], state);
                    float undecidedChance = ElectionUtility.GetUndecidedProbability(probabilityA, state);
                    int decision;
                    if (random.NextFloat() < undecidedChance)
                    {
                        decision = -1;
                    }
                    else if (random.NextFloat() < probabilityA)
                    {
                        decision = 0;
                    }
                    else
                    {
                        decision = 1;
                    }

                    AddPollSample(ref state, citizens[citizenIndex], decision);
                }
            }

            ApplyPollUndecidedFloor(ref state, sampleCount);
            DebugLog($"Poll simulation complete: date={ElectionUtility.FormatCurrentDate(World, now)}, population={population}, samplePercent={samplePercent}, targetSample={targetSampleCount}, eligibleResidents={eligibleCount}, actualSample={sampleCount}, seed={seed}, turnoutWeightByAge=teen:{teenDailyTurnout}/adult:{adultDailyTurnout}/elderly:{elderlyDailyTurnout}, votesA={state.pollVotesA}, votesB={state.pollVotesB}, undecided={state.pollUndecided}, donationA={state.donationA:n0}, donationB={state.donationB:n0}.");
        }

        private float GetPollTurnoutWeight(ElectionState state, Entity citizenEntity, Citizen citizen, int teenDailyTurnout, int adultDailyTurnout, int elderlyDailyTurnout)
        {
            int dailyTurnout = math.clamp(
                GetDailyTurnoutPercentForAge(citizen.GetAge(), teenDailyTurnout, adultDailyTurnout, elderlyDailyTurnout) +
                GetEducationDailyTurnoutBonusPercent(state, citizen.GetEducationLevel()),
                1,
                100);
            float weight = dailyTurnout * ElectionUtility.GetVotingTurnoutMultiplier(citizen);
            if (state.strictVotingIdLawPassed &&
                citizen.GetEducationLevel() <= 0 &&
                EntityManager.HasComponent<Worker>(citizenEntity))
            {
                weight *= 1f - ElectionUtility.StrictVotingIdUneducatedWorkerTurnoutPenaltyPercent / 100f;
            }

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
                    return 0;
            }
        }

        private static void ApplyPollUndecidedFloor(ref ElectionState state, int sampleCount)
        {
            if (sampleCount < 6)
                return;

            int targetUndecided = math.max(1, (int)math.round(sampleCount * 0.08f));
            while (state.pollUndecided < targetUndecided)
            {
                int decision;
                if (state.pollVotesA >= state.pollVotesB && state.pollVotesA > 0)
                {
                    state.pollVotesA--;
                    decision = 0;
                }
                else if (state.pollVotesB > 0)
                {
                    state.pollVotesB--;
                    decision = 1;
                }
                else if (state.pollVotesA > 0)
                {
                    state.pollVotesA--;
                    decision = 0;
                }
                else
                {
                    return;
                }

                state.pollUndecided++;
                MovePollBucketVoteToUndecided(ref state, decision);
            }
        }

        private static void AddPollSample(ref ElectionState state, Citizen citizen, int decision)
        {
            AddPollTotals(ref state, decision);
            AddAgePollBucket(ref state, citizen.GetAge(), decision);
            AddEducationPollBucket(ref state, citizen.GetEducationLevel(), decision);
        }

        private static void AddPollTotals(ref ElectionState state, int decision)
        {
            if (decision == 0)
                state.pollVotesA++;
            else if (decision == 1)
                state.pollVotesB++;
            else
                state.pollUndecided++;
        }

        private static void AddAgePollBucket(ref ElectionState state, CitizenAge age, int decision)
        {
            if (age == CitizenAge.Teen)
            {
                AddPollBucket(decision, ref state.pollTeenVotesA, ref state.pollTeenVotesB, ref state.pollTeenUndecided);
                return;
            }

            if (age == CitizenAge.Elderly)
            {
                AddPollBucket(decision, ref state.pollElderlyVotesA, ref state.pollElderlyVotesB, ref state.pollElderlyUndecided);
                return;
            }

            AddPollBucket(decision, ref state.pollAdultVotesA, ref state.pollAdultVotesB, ref state.pollAdultUndecided);
        }

        private static void AddEducationPollBucket(ref ElectionState state, int education, int decision)
        {
            switch (math.clamp(education, 0, 4))
            {
                case 0:
                    AddPollBucket(decision, ref state.pollEducation0VotesA, ref state.pollEducation0VotesB, ref state.pollEducation0Undecided);
                    break;
                case 1:
                    AddPollBucket(decision, ref state.pollEducation1VotesA, ref state.pollEducation1VotesB, ref state.pollEducation1Undecided);
                    break;
                case 2:
                    AddPollBucket(decision, ref state.pollEducation2VotesA, ref state.pollEducation2VotesB, ref state.pollEducation2Undecided);
                    break;
                case 3:
                    AddPollBucket(decision, ref state.pollEducation3VotesA, ref state.pollEducation3VotesB, ref state.pollEducation3Undecided);
                    break;
                default:
                    AddPollBucket(decision, ref state.pollEducation4VotesA, ref state.pollEducation4VotesB, ref state.pollEducation4Undecided);
                    break;
            }
        }

        private static void AddPollBucket(int decision, ref int votesA, ref int votesB, ref int undecided)
        {
            if (decision == 0)
                votesA++;
            else if (decision == 1)
                votesB++;
            else
                undecided++;
        }

        private static void MovePollBucketVoteToUndecided(ref ElectionState state, int decision)
        {
            MoveAgePollBucketVoteToUndecided(ref state, decision);
            MoveEducationPollBucketVoteToUndecided(ref state, decision);
        }

        private static void MoveAgePollBucketVoteToUndecided(ref ElectionState state, int decision)
        {
            if (decision == 0)
            {
                if (TryMovePollBucketVoteToUndecided(ref state.pollAdultVotesA, ref state.pollAdultUndecided))
                    return;
                if (TryMovePollBucketVoteToUndecided(ref state.pollElderlyVotesA, ref state.pollElderlyUndecided))
                    return;
                TryMovePollBucketVoteToUndecided(ref state.pollTeenVotesA, ref state.pollTeenUndecided);
                return;
            }

            if (TryMovePollBucketVoteToUndecided(ref state.pollAdultVotesB, ref state.pollAdultUndecided))
                return;
            if (TryMovePollBucketVoteToUndecided(ref state.pollElderlyVotesB, ref state.pollElderlyUndecided))
                return;
            TryMovePollBucketVoteToUndecided(ref state.pollTeenVotesB, ref state.pollTeenUndecided);
        }

        private static void MoveEducationPollBucketVoteToUndecided(ref ElectionState state, int decision)
        {
            if (decision == 0)
            {
                if (TryMovePollBucketVoteToUndecided(ref state.pollEducation2VotesA, ref state.pollEducation2Undecided))
                    return;
                if (TryMovePollBucketVoteToUndecided(ref state.pollEducation3VotesA, ref state.pollEducation3Undecided))
                    return;
                if (TryMovePollBucketVoteToUndecided(ref state.pollEducation4VotesA, ref state.pollEducation4Undecided))
                    return;
                if (TryMovePollBucketVoteToUndecided(ref state.pollEducation1VotesA, ref state.pollEducation1Undecided))
                    return;
                TryMovePollBucketVoteToUndecided(ref state.pollEducation0VotesA, ref state.pollEducation0Undecided);
                return;
            }

            if (TryMovePollBucketVoteToUndecided(ref state.pollEducation2VotesB, ref state.pollEducation2Undecided))
                return;
            if (TryMovePollBucketVoteToUndecided(ref state.pollEducation3VotesB, ref state.pollEducation3Undecided))
                return;
            if (TryMovePollBucketVoteToUndecided(ref state.pollEducation4VotesB, ref state.pollEducation4Undecided))
                return;
            if (TryMovePollBucketVoteToUndecided(ref state.pollEducation1VotesB, ref state.pollEducation1Undecided))
                return;
            TryMovePollBucketVoteToUndecided(ref state.pollEducation0VotesB, ref state.pollEducation0Undecided);
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

        private string GetCityName()
        {
            Entity city = m_CitySystem.City;
            if (city != Entity.Null && EntityManager.Exists(city))
            {
                string name = m_NameSystem?.GetRenderedLabelName(city);
                if (!string.IsNullOrWhiteSpace(name))
                    return name.Trim();
            }

            return "the city";
        }

        private void EnsureDistinctCampaignPortraits(ref ElectionState state)
        {
            if (!state.HasCandidates)
                return;

            int originalA = state.candidateAPortraitIndex;
            int originalB = state.candidateBPortraitIndex;
            int mayorPortraitIndex = GetBaseMayorPortraitIndex(state);

            state.candidateAPortraitIndex = EnsureDistinctPortraitIndex(
                state.candidateA,
                state.candidateAPortraitIndex,
                17,
                state.mayor,
                mayorPortraitIndex,
                Entity.Null,
                -1);
            state.candidateBPortraitIndex = EnsureDistinctPortraitIndex(
                state.candidateB,
                state.candidateBPortraitIndex,
                7919,
                state.mayor,
                mayorPortraitIndex,
                state.candidateA,
                state.candidateAPortraitIndex);

            if (originalA != state.candidateAPortraitIndex || originalB != state.candidateBPortraitIndex)
            {
                DebugLog($"Campaign portraits normalized: A {originalA}->{state.candidateAPortraitIndex}, B {originalB}->{state.candidateBPortraitIndex}, mayor={mayorPortraitIndex}.");
            }
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
            return PickDistinctPortraitIndex(
                state.mayor,
                4241,
                state.candidateA,
                state.candidateAPortraitIndex,
                state.candidateB,
                state.candidateBPortraitIndex);
        }

        private int GetPortraitIndexForCitizen(ElectionState state, Entity citizen)
        {
            if (citizen == state.candidateA && state.candidateAPortraitIndex >= 0)
                return state.candidateAPortraitIndex;

            if (citizen == state.candidateB && state.candidateBPortraitIndex >= 0)
                return state.candidateBPortraitIndex;

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
                CustomChirpsBridge.PostChirpWith3Targets(textWithCandidateAndVenueLinks, DepartmentAccountBridge.CensusBureau, candidateA, candidateB, venue, "Election Board"))
            {
                return;
            }

            if (hasCandidateLinks &&
                CustomChirpsBridge.SupportsChirpWith2Targets() &&
                CustomChirpsBridge.PostChirpWith2Targets(textWithCandidateLinks, DepartmentAccountBridge.CensusBureau, candidateA, candidateB, "Election Board"))
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
