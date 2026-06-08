using Colossal.UI.Binding;
using Elections.Components;
using Elections.Models;
using Elections.Bridge;
using Game;
using Game.Rendering;
using Game.UI;
using System;
using Unity.Entities;
using Unity.Mathematics;

namespace Elections.Systems
{
    public partial class ElectionUISystem : UISystemBase
    {
        private EntityQuery m_StateQuery;
        private NameSystem m_NameSystem;
        private CameraUpdateSystem m_CameraUpdateSystem;
        private ElectionLifecycleSystem m_LifecycleSystem;
        private RawValueBinding m_PanelBinding;
        private ValueBinding<bool> m_UseUniversalModMenuBinding;
        private ValueBinding<bool> m_ShowVotingLocationsBinding;

        public static bool ShowVotingLocations { get; private set; }

        public override GameMode gameMode => GameMode.Game;

        public override int GetUpdateInterval(SystemUpdatePhase phase)
        {
            return 16;
        }

        protected override void OnCreate()
        {
            base.OnCreate();

            m_StateQuery = GetEntityQuery(ComponentType.ReadOnly<ElectionState>());
            m_NameSystem = World.GetExistingSystemManaged<NameSystem>();
            m_CameraUpdateSystem = World.GetOrCreateSystemManaged<CameraUpdateSystem>();
            m_LifecycleSystem = World.GetOrCreateSystemManaged<ElectionLifecycleSystem>();
            ShowVotingLocations = false;

            AddBinding(m_PanelBinding = new RawValueBinding(Mod.Id, "panel", WritePanel));
            AddBinding(m_UseUniversalModMenuBinding = new ValueBinding<bool>(
                Mod.Id,
                "useUniversalModMenu",
                Mod.m_Setting?.UseUniversalModMenu ?? false));
            AddBinding(m_ShowVotingLocationsBinding = new ValueBinding<bool>(
                Mod.Id,
                "showVotingLocations",
                ShowVotingLocations));
            AddBinding(new TriggerBinding<int>(Mod.Id, "focusCandidate", FocusCandidate));
            AddBinding(new TriggerBinding(Mod.Id, "focusMayor", FocusMayor));
            AddBinding(new TriggerBinding<int, int>(Mod.Id, "donate", Donate));
            AddBinding(new TriggerBinding<int>(Mod.Id, "runSupportProgram", RunSupportProgram));
            AddBinding(new TriggerBinding<int>(Mod.Id, "bribeMayor", BribeMayor));
            AddBinding(new TriggerBinding<int>(Mod.Id, "endorseCandidate", EndorseCandidate));
            AddBinding(new TriggerBinding<int>(Mod.Id, "tamperVotes", TamperVotes));
            AddBinding(new TriggerBinding(Mod.Id, "proposeVotingIdLaw", ProposeVotingIdLaw));
            AddBinding(new TriggerBinding<bool>(Mod.Id, "setShowVotingLocations", SetShowVotingLocations));
        }

        protected override void OnUpdate()
        {
            base.OnUpdate();

            if (ShowVotingLocations && !(Mod.m_Setting?.EnableElections ?? false))
                SetShowVotingLocations(false);

            m_PanelBinding?.Update();
        }

        public void UpdateUseUniversalModMenu(bool value)
        {
            m_UseUniversalModMenuBinding?.Update(value);
        }

        private void Donate(int candidateIndex, int tierIndex)
        {
            m_LifecycleSystem?.Donate(candidateIndex, tierIndex);
            m_PanelBinding?.Update();
        }

        private void RunSupportProgram(int programIndex)
        {
            m_LifecycleSystem?.RunSupportProgram(programIndex);
            m_PanelBinding?.Update();
        }

        private void BribeMayor(int candidateIndex)
        {
            m_LifecycleSystem?.BribeMayor(candidateIndex);
            m_PanelBinding?.Update();
        }

        private void EndorseCandidate(int candidateIndex)
        {
            m_LifecycleSystem?.EndorseCandidate(candidateIndex);
            m_PanelBinding?.Update();
        }

        private void TamperVotes(int candidateIndex)
        {
            m_LifecycleSystem?.ScheduleVoteTampering(candidateIndex);
            m_PanelBinding?.Update();
        }

        private void ProposeVotingIdLaw()
        {
            m_LifecycleSystem?.ProposeStrictVotingIdLaw();
            m_PanelBinding?.Update();
        }

        private void SetShowVotingLocations(bool value)
        {
            ShowVotingLocations = value && (Mod.m_Setting?.EnableElections ?? false);
            m_ShowVotingLocationsBinding?.Update(ShowVotingLocations);
        }

        private void FocusCandidate(int candidateIndex)
        {
            if (!TryGetPreparedState(out ElectionState state))
                return;

            Entity candidate = candidateIndex == 0 ? state.candidateA : state.candidateB;
            NavigateTo(candidate);
        }

        private void FocusMayor()
        {
            if (!TryGetPreparedState(out ElectionState state))
                return;

            NavigateTo(state.mayor);
        }

        private void NavigateTo(Entity entity)
        {
            if (m_CameraUpdateSystem?.orbitCameraController == null ||
                entity == Entity.Null ||
                !EntityManager.Exists(entity))
            {
                return;
            }

            m_CameraUpdateSystem.orbitCameraController.followedEntity = entity;
            m_CameraUpdateSystem.orbitCameraController.TryMatchPosition(m_CameraUpdateSystem.activeCameraController);
            m_CameraUpdateSystem.activeCameraController = m_CameraUpdateSystem.orbitCameraController;
        }

        private void WritePanel(IJsonWriter writer)
        {
            DateTime now = default;
            bool hasDateTime = RealisticTripsBridge.TryGetCurrentDateTime(out now);
            string currentDate = hasDateTime ? ElectionUtility.FormatCurrentDate(World, now) : string.Empty;
            bool electionsEnabled = Mod.m_Setting?.EnableElections ?? false;
            int currentPopulation = m_LifecycleSystem?.GetCurrentPopulationForUI() ?? 0;
            bool populationReady = currentPopulation >= ElectionLifecycleSystem.MinimumPopulation;

            writer.TypeBegin("ElectionPanel");
            writer.PropertyName("enabled"); writer.Write(electionsEnabled);
            writer.PropertyName("currentDate"); writer.Write(currentDate);
            writer.PropertyName("currentPopulation"); writer.Write(currentPopulation);
            writer.PropertyName("minimumPopulation"); writer.Write(ElectionLifecycleSystem.MinimumPopulation);
            writer.PropertyName("populationReady"); writer.Write(populationReady);

            if (!TryGetPreparedState(out ElectionState state))
            {
                bool waitingForPopulation = electionsEnabled && !populationReady;
                writer.PropertyName("hasState"); writer.Write(false);
                writer.PropertyName("waitingForPopulation"); writer.Write(waitingForPopulation);
                writer.PropertyName("stage"); writer.Write("None");
                writer.PropertyName("stageLabel"); writer.Write(waitingForPopulation ? "Waiting for residents" : "No election data");
                writer.PropertyName("cycleLabel"); writer.Write(waitingForPopulation
                    ? $"Elections start at {ElectionLifecycleSystem.MinimumPopulation:n0} population. Current population: {currentPopulation:n0}."
                    : "The election system has not initialized yet.");
                writer.PropertyName("pollReleased"); writer.Write(false);
                writer.PropertyName("donationsOpen"); writer.Write(false);
                writer.PropertyName("bribesOpen"); writer.Write(false);
                writer.PropertyName("canBribe"); writer.Write(false);
                writer.PropertyName("canEndorse"); writer.Write(false);
                writer.PropertyName("endorsementUsed"); writer.Write(false);
                writer.PropertyName("endorsedCandidateIndex"); writer.Write(-1);
                writer.PropertyName("canTamper"); writer.Write(false);
                writer.PropertyName("voteTamperingScheduled"); writer.Write(false);
                writer.PropertyName("voteTamperingCandidateIndex"); writer.Write(-1);
                writer.PropertyName("canProposeVotingId"); writer.Write(false);
                writer.PropertyName("votingIdLawPassed"); writer.Write(false);
                writer.PropertyName("votingIdProposalPending"); writer.Write(false);
                writer.PropertyName("bribeUsedToday"); writer.Write(false);
                writer.PropertyName("bribeBlocked"); writer.Write(false);
                writer.PropertyName("bribeMeetingPending"); writer.Write(false);
                writer.PropertyName("bribeCost"); writer.Write(ElectionLifecycleSystem.BribeAmount);
                WriteSupportProgramPanel(writer, default(ElectionState), false, false, false);
                writer.PropertyName("pollDate"); writer.Write(string.Empty);
                writer.PropertyName("electionDate"); writer.Write(string.Empty);
                WriteScheduleTimes(writer);
                WriteEmptyPoll(writer);
                WriteDonationTiers(writer, ElectionDonationTiers.FixedDonationAmount);
                writer.PropertyName("candidateA"); WriteEmptyCandidate(writer, 0, "Candidate A");
                writer.PropertyName("candidateB"); WriteEmptyCandidate(writer, 1, "Candidate B");
                WriteEmptyMayor(writer);
                writer.TypeEnd();
                return;
            }

            bool pollReleased = HasPollResults(state);
            bool waitingForPopulationState = electionsEnabled &&
                !populationReady &&
                !state.HasCandidates &&
                state.stage == ElectionCampaignStage.None;
            bool electionDay = hasDateTime && IsElectionDay(state, now);
            bool donationsOpen = !waitingForPopulationState && IsDonationStage(state.stage) && state.HasCandidates && !electionDay;
            bool bribesOpen = hasDateTime && donationsOpen && state.mayor != Entity.Null;
            bool bribeMeetingPending = hasDateTime &&
                (state.bribeMeetingCandidateIndex == 0 || state.bribeMeetingCandidateIndex == 1) &&
                state.bribeMeetingDeadlineTicks > 0;
            bool bribeBlocked = hasDateTime &&
                (bribeMeetingPending || state.bribeBlockedUntilTicks > now.Ticks);
            bool bribeUsedToday = bribeBlocked;
            bool endorsementUsed = state.mayorEndorsementCandidateIndex == 0 ||
                state.mayorEndorsementCandidateIndex == 1;
            bool voteTamperingScheduled = state.voteTamperingCandidateIndex == 0 ||
                state.voteTamperingCandidateIndex == 1;
            bool votingIdProposalPending = state.strictVotingIdProposalPending &&
                !state.strictVotingIdChirpSent &&
                state.strictVotingIdChirpUtcTicks > 0;
            bool supportProgramsOpen = hasDateTime && !waitingForPopulationState && IsDonationStage(state.stage) && state.HasCandidates && !electionDay;
            bool supportProgramUsedToday = hasDateTime &&
                state.supportProgramDayKey == ElectionUtility.CurrentCalendarDayKey(World, now);

            writer.PropertyName("hasState"); writer.Write(true);
            writer.PropertyName("waitingForPopulation"); writer.Write(waitingForPopulationState);
            writer.PropertyName("stage"); writer.Write(state.stage.ToString());
            writer.PropertyName("stageLabel"); writer.Write(waitingForPopulationState ? "Waiting for residents" : GetStageLabel(state.stage, state.HasCandidates));
            writer.PropertyName("cycleLabel"); writer.Write(waitingForPopulationState
                ? $"Elections start at {ElectionLifecycleSystem.MinimumPopulation:n0} population. Current population: {currentPopulation:n0}."
                : GetCycleLabel(state));
            writer.PropertyName("pollReleased"); writer.Write(pollReleased);
            writer.PropertyName("donationsOpen"); writer.Write(donationsOpen);
            writer.PropertyName("bribesOpen"); writer.Write(bribesOpen);
            writer.PropertyName("canBribe"); writer.Write(bribesOpen && !bribeBlocked);
            writer.PropertyName("canEndorse"); writer.Write(bribesOpen && !bribeBlocked && !endorsementUsed);
            writer.PropertyName("endorsementUsed"); writer.Write(endorsementUsed);
            writer.PropertyName("endorsedCandidateIndex"); writer.Write(endorsementUsed ? state.mayorEndorsementCandidateIndex : -1);
            writer.PropertyName("canTamper"); writer.Write(bribesOpen && !bribeBlocked && !voteTamperingScheduled);
            writer.PropertyName("voteTamperingScheduled"); writer.Write(voteTamperingScheduled);
            writer.PropertyName("voteTamperingCandidateIndex"); writer.Write(voteTamperingScheduled ? state.voteTamperingCandidateIndex : -1);
            writer.PropertyName("canProposeVotingId"); writer.Write(bribesOpen && !bribeBlocked && !state.strictVotingIdLawPassed && !votingIdProposalPending);
            writer.PropertyName("votingIdLawPassed"); writer.Write(state.strictVotingIdLawPassed);
            writer.PropertyName("votingIdProposalPending"); writer.Write(votingIdProposalPending);
            writer.PropertyName("bribeUsedToday"); writer.Write(bribeUsedToday);
            writer.PropertyName("bribeBlocked"); writer.Write(bribeBlocked);
            writer.PropertyName("bribeMeetingPending"); writer.Write(bribeMeetingPending);
            writer.PropertyName("bribeCost"); writer.Write(GetCampaignBribeAmount(state));
            WriteSupportProgramPanel(writer, state, supportProgramsOpen, supportProgramUsedToday, electionDay);
            writer.PropertyName("pollDate"); writer.Write(FormatScheduledDate(state.pollYear, state.pollMonth));
            writer.PropertyName("electionDate"); writer.Write(FormatElectionDate(state));
            WriteScheduleTimes(writer, state);
            WritePoll(writer, state, pollReleased);
            WriteDonationTiers(writer, state.campaignDonationAmount);
            writer.PropertyName("candidateA");
            if (state.HasCandidates)
                WriteCandidate(writer, state, true);
            else
                WriteEmptyCandidate(writer, 0, "Candidate A");

            writer.PropertyName("candidateB");
            if (state.HasCandidates)
                WriteCandidate(writer, state, false);
            else
                WriteEmptyCandidate(writer, 1, "Candidate B");
            WriteMayor(writer, state);
            writer.TypeEnd();
        }

        private bool TryGetPreparedState(out ElectionState state)
        {
            if (m_LifecycleSystem != null && m_LifecycleSystem.TryGetStateForUI(out state))
                return true;

            if (!m_StateQuery.IsEmptyIgnoreFilter)
            {
                state = m_StateQuery.GetSingleton<ElectionState>();
                return true;
            }

            state = default;
            return false;
        }

        private void WriteCandidate(IJsonWriter writer, ElectionState state, bool candidateA)
        {
            Entity candidate = candidateA ? state.candidateA : state.candidateB;
            int index = candidateA ? 0 : 1;
            int effectId = candidateA ? state.candidateAEffectId : state.candidateBEffectId;
            int portraitIndex = candidateA ? state.candidateAPortraitIndex : state.candidateBPortraitIndex;
            int donationAmount = candidateA ? state.donationA : state.donationB;
            ElectionEffectDefinition effect = ElectionEffects.Get(effectId, candidateA ? state.candidateANegativeSoftened : state.candidateBNegativeSoftened);
            int donationBonusPercent = (int)math.round(ElectionDonationTiers.GetBonusForAmount(donationAmount, state.campaignDonationAmount) * 100f);
            bool canFocus = candidate != Entity.Null && EntityManager.Exists(candidate);

            writer.TypeBegin("ElectionCandidate");
            writer.PropertyName("index"); writer.Write(index);
            writer.PropertyName("exists"); writer.Write(candidate != Entity.Null);
            writer.PropertyName("name"); writer.Write(GetEntityName(candidate, candidateA ? "Candidate A" : "Candidate B"));
            writer.PropertyName("portrait"); writer.Write(CandidatePortraitCatalog.GetPortraitImageSource(EntityManager, candidate, portraitIndex));
            writer.PropertyName("canFocus"); writer.Write(canFocus);
            writer.PropertyName("bio"); writer.Write(GetCandidateBio(state, candidateA));
            writer.PropertyName("effectName"); writer.Write(effect.Name);
            writer.PropertyName("effectDescription"); writer.Write($"If elected, this platform {effect.Description}.");
            writer.PropertyName("platformImpacts"); WritePlatformImpacts(writer, effect);
            writer.PropertyName("donationAmount"); writer.Write(donationAmount);
            writer.PropertyName("donationBonusPercent"); writer.Write(donationBonusPercent);
            writer.PropertyName("donated"); writer.Write(donationAmount > 0);
            writer.TypeEnd();
        }

        private static void WriteEmptyCandidate(IJsonWriter writer, int index, string fallbackName)
        {
            writer.TypeBegin("ElectionCandidate");
            writer.PropertyName("index"); writer.Write(index);
            writer.PropertyName("exists"); writer.Write(false);
            writer.PropertyName("name"); writer.Write(fallbackName);
            writer.PropertyName("portrait"); writer.Write(string.Empty);
            writer.PropertyName("canFocus"); writer.Write(false);
            writer.PropertyName("bio"); writer.Write(string.Empty);
            writer.PropertyName("effectName"); writer.Write("No platform");
            writer.PropertyName("effectDescription"); writer.Write("No candidate has been selected yet.");
            writer.PropertyName("platformImpacts"); WriteEmptyPlatformImpacts(writer);
            writer.PropertyName("donationAmount"); writer.Write(0);
            writer.PropertyName("donationBonusPercent"); writer.Write(0);
            writer.PropertyName("donated"); writer.Write(false);
            writer.TypeEnd();
        }

        private void WriteMayor(IJsonWriter writer, ElectionState state)
        {
            ElectionEffectDefinition effect = ElectionEffects.Get(state.mayorEffectId, state.mayorNegativeSoftened);
            bool canFocus = state.mayor != Entity.Null && EntityManager.Exists(state.mayor);
            string mayorName = GetEntityName(state.mayor, string.Empty);

            writer.PropertyName("mayorName"); writer.Write(mayorName);
            writer.PropertyName("mayorPortrait"); writer.Write(canFocus
                ? CandidatePortraitCatalog.GetPortraitImageSource(EntityManager, state.mayor, GetMayorPortraitIndex(state))
                : string.Empty);
            writer.PropertyName("mayorCanFocus"); writer.Write(canFocus);
            writer.PropertyName("mayorEffectName"); writer.Write(effect.Name);
            writer.PropertyName("mayorEffectDescription"); writer.Write(state.mayorEffectId == 0
                ? "Temporary mayoral platform. No city modifiers are applied; this mayor supervises the transition until an elected mayor takes office."
                : $"Current mayoral platform. It {effect.Description}.");
            writer.PropertyName("mayorPlatformImpacts");
            if (state.mayorEffectId == 0)
                WriteEmptyPlatformImpacts(writer);
            else
                WritePlatformImpacts(writer, effect);
            writer.PropertyName("mayorTemporary"); writer.Write(state.mayorEffectId == 0 && state.mayor != Entity.Null);
        }

        private int GetMayorPortraitIndex(ElectionState state)
        {
            return CandidatePortraitCatalog.PickDifferentPortraitIndex(
                EntityManager,
                state.mayor,
                4241,
                state.candidateA,
                state.candidateAPortraitIndex,
                state.candidateB,
                state.candidateBPortraitIndex);
        }

        private static void WriteEmptyMayor(IJsonWriter writer)
        {
            writer.PropertyName("mayorName"); writer.Write(string.Empty);
            writer.PropertyName("mayorPortrait"); writer.Write(string.Empty);
            writer.PropertyName("mayorCanFocus"); writer.Write(false);
            writer.PropertyName("mayorEffectName"); writer.Write(string.Empty);
            writer.PropertyName("mayorEffectDescription"); writer.Write(string.Empty);
            writer.PropertyName("mayorPlatformImpacts"); WriteEmptyPlatformImpacts(writer);
            writer.PropertyName("mayorTemporary"); writer.Write(false);
        }

        private static void WritePlatformImpacts(IJsonWriter writer, ElectionEffectDefinition effect)
        {
            writer.ArrayBegin(2);
            WritePlatformImpact(writer, effect.PositiveImpact);
            WritePlatformImpact(writer, effect.NegativeImpact);
            writer.ArrayEnd();
        }

        private static void WriteEmptyPlatformImpacts(IJsonWriter writer)
        {
            writer.ArrayBegin(0);
            writer.ArrayEnd();
        }

        private static void WritePlatformImpact(IJsonWriter writer, ElectionEffectImpact impact)
        {
            writer.TypeBegin("ElectionPlatformImpact");
            writer.PropertyName("value"); writer.Write(impact.ValueText);
            writer.PropertyName("label"); writer.Write(impact.Label);
            writer.PropertyName("positive"); writer.Write(impact.Positive);
            writer.TypeEnd();
        }

        private void WritePoll(IJsonWriter writer, ElectionState state, bool pollReleased)
        {
            string nameA = GetEntityName(state.candidateA, "Candidate A");
            string nameB = GetEntityName(state.candidateB, "Candidate B");
            ElectionPollSummary summary = ElectionPollUtility.BuildSummary(
                pollReleased ? state.pollVotesA : 0,
                pollReleased ? state.pollVotesB : 0,
                pollReleased ? state.pollUndecided : 0,
                nameA,
                nameB);

            writer.PropertyName("poll");
            writer.TypeBegin("ElectionPoll");
            writer.PropertyName("sampleSize"); writer.Write(summary.Total);
            writer.PropertyName("votesA"); writer.Write(pollReleased ? state.pollVotesA : 0);
            writer.PropertyName("votesB"); writer.Write(pollReleased ? state.pollVotesB : 0);
            writer.PropertyName("undecided"); writer.Write(pollReleased ? state.pollUndecided : 0);
            writer.PropertyName("percentA"); writer.Write(pollReleased ? summary.PercentA : 0);
            writer.PropertyName("percentB"); writer.Write(pollReleased ? summary.PercentB : 0);
            writer.PropertyName("percentUndecided"); writer.Write(pollReleased ? summary.PercentUndecided : 0);
            writer.PropertyName("marginOfError"); writer.Write(pollReleased ? summary.MarginOfError : 0);
            writer.PropertyName("leaderIndex"); writer.Write(pollReleased ? summary.LeaderIndex : -1);
            writer.PropertyName("withinMargin"); writer.Write(pollReleased && summary.WithinMargin);
            writer.PropertyName("resultLabel"); writer.Write(pollReleased ? summary.Label : string.Empty);
            writer.PropertyName("resultDescription"); writer.Write(pollReleased ? summary.Description : string.Empty);
            writer.PropertyName("ageGroups");
            writer.ArrayBegin(3);
            WritePollBreakdown(writer, "teens", "Teens", pollReleased ? state.pollTeenVotesA : 0, pollReleased ? state.pollTeenVotesB : 0, pollReleased ? state.pollTeenUndecided : 0, nameA, nameB);
            WritePollBreakdown(writer, "adults", "Adults", pollReleased ? state.pollAdultVotesA : 0, pollReleased ? state.pollAdultVotesB : 0, pollReleased ? state.pollAdultUndecided : 0, nameA, nameB);
            WritePollBreakdown(writer, "elderly", "Elderly", pollReleased ? state.pollElderlyVotesA : 0, pollReleased ? state.pollElderlyVotesB : 0, pollReleased ? state.pollElderlyUndecided : 0, nameA, nameB);
            writer.ArrayEnd();
            writer.PropertyName("educationGroups");
            writer.ArrayBegin(5);
            WritePollBreakdown(writer, "education0", "Uneducated", pollReleased ? state.pollEducation0VotesA : 0, pollReleased ? state.pollEducation0VotesB : 0, pollReleased ? state.pollEducation0Undecided : 0, nameA, nameB);
            WritePollBreakdown(writer, "education1", "Poorly educated", pollReleased ? state.pollEducation1VotesA : 0, pollReleased ? state.pollEducation1VotesB : 0, pollReleased ? state.pollEducation1Undecided : 0, nameA, nameB);
            WritePollBreakdown(writer, "education2", "Educated", pollReleased ? state.pollEducation2VotesA : 0, pollReleased ? state.pollEducation2VotesB : 0, pollReleased ? state.pollEducation2Undecided : 0, nameA, nameB);
            WritePollBreakdown(writer, "education3", "Well educated", pollReleased ? state.pollEducation3VotesA : 0, pollReleased ? state.pollEducation3VotesB : 0, pollReleased ? state.pollEducation3Undecided : 0, nameA, nameB);
            WritePollBreakdown(writer, "education4", "Highly educated", pollReleased ? state.pollEducation4VotesA : 0, pollReleased ? state.pollEducation4VotesB : 0, pollReleased ? state.pollEducation4Undecided : 0, nameA, nameB);
            writer.ArrayEnd();
            writer.TypeEnd();
        }

        private static void WriteEmptyPoll(IJsonWriter writer)
        {
            writer.PropertyName("poll");
            writer.TypeBegin("ElectionPoll");
            writer.PropertyName("sampleSize"); writer.Write(0);
            writer.PropertyName("votesA"); writer.Write(0);
            writer.PropertyName("votesB"); writer.Write(0);
            writer.PropertyName("undecided"); writer.Write(0);
            writer.PropertyName("percentA"); writer.Write(0);
            writer.PropertyName("percentB"); writer.Write(0);
            writer.PropertyName("percentUndecided"); writer.Write(0);
            writer.PropertyName("marginOfError"); writer.Write(0);
            writer.PropertyName("leaderIndex"); writer.Write(-1);
            writer.PropertyName("withinMargin"); writer.Write(false);
            writer.PropertyName("resultLabel"); writer.Write(string.Empty);
            writer.PropertyName("resultDescription"); writer.Write(string.Empty);
            writer.PropertyName("ageGroups"); writer.ArrayBegin(0); writer.ArrayEnd();
            writer.PropertyName("educationGroups"); writer.ArrayBegin(0); writer.ArrayEnd();
            writer.TypeEnd();
        }

        private static void WritePollBreakdown(IJsonWriter writer, string key, string label, int votesA, int votesB, int undecided, string nameA, string nameB)
        {
            ElectionPollSummary summary = ElectionPollUtility.BuildSummary(votesA, votesB, undecided, nameA, nameB);

            writer.TypeBegin("ElectionPollBreakdown");
            writer.PropertyName("key"); writer.Write(key);
            writer.PropertyName("label"); writer.Write(label);
            writer.PropertyName("sampleSize"); writer.Write(summary.Total);
            writer.PropertyName("votesA"); writer.Write(votesA);
            writer.PropertyName("votesB"); writer.Write(votesB);
            writer.PropertyName("undecided"); writer.Write(undecided);
            writer.PropertyName("percentA"); writer.Write(summary.PercentA);
            writer.PropertyName("percentB"); writer.Write(summary.PercentB);
            writer.PropertyName("percentUndecided"); writer.Write(summary.PercentUndecided);
            writer.PropertyName("marginOfError"); writer.Write(summary.MarginOfError);
            writer.PropertyName("leaderIndex"); writer.Write(summary.LeaderIndex);
            writer.PropertyName("withinMargin"); writer.Write(summary.WithinMargin);
            writer.PropertyName("resultLabel"); writer.Write(summary.Label);
            writer.PropertyName("resultDescription"); writer.Write(summary.Description);
            writer.TypeEnd();
        }

        private string GetCandidateBio(ElectionState state, bool candidateA)
        {
            Entity candidate = candidateA ? state.candidateA : state.candidateB;
            int age = candidateA ? state.candidateAAge : state.candidateBAge;
            int education = candidateA ? state.candidateAEducation : state.candidateBEducation;
            int workType = candidateA ? state.candidateAWorkType : state.candidateBWorkType;
            int wealth = candidateA ? state.candidateAWealth : state.candidateBWealth;
            return ElectionCandidateProfileUtility.BuildBio(EntityManager, candidate, age, education, workType, wealth);
        }

        private static void WriteDonationTiers(IJsonWriter writer, int campaignDonationAmount)
        {
            writer.PropertyName("donationTiers");
            writer.ArrayBegin(ElectionDonationTiers.Count);

            for (int i = 0; i < ElectionDonationTiers.Count; i++)
            {
                ElectionDonationTiers.TryGet(i, campaignDonationAmount, out ElectionDonationTier tier);
                writer.TypeBegin("ElectionDonationTier");
                writer.PropertyName("index"); writer.Write(i);
                writer.PropertyName("amount"); writer.Write(tier.Amount);
                writer.PropertyName("bonusPercent"); writer.Write((int)math.round(tier.Bonus * 100f));
                writer.PropertyName("label"); writer.Write(GetDonationTierLabel(i));
                writer.TypeEnd();
            }

            writer.ArrayEnd();
        }

        private static void WriteSupportProgramPanel(IJsonWriter writer, ElectionState state, bool supportProgramsOpen, bool supportProgramUsedToday, bool electionDay)
        {
            int cost = ElectionSupportPrograms.GetCost(state.campaignDonationAmount);
            writer.PropertyName("supportProgramsOpen"); writer.Write(supportProgramsOpen);
            writer.PropertyName("supportProgramUsedToday"); writer.Write(supportProgramUsedToday);
            writer.PropertyName("supportProgramUsedTodayLabel"); writer.Write(supportProgramUsedToday ? GetSupportProgramLabel(state.supportProgramIdToday) : string.Empty);
            writer.PropertyName("supportProgramCost"); writer.Write(cost);
            writer.PropertyName("supportPrograms");
            writer.ArrayBegin(ElectionSupportPrograms.Count);

            for (int i = 0; i < ElectionSupportPrograms.Count; i++)
            {
                if (!ElectionSupportPrograms.TryGet(i, out ElectionSupportProgramDefinition program))
                    continue;

                bool active = IsSupportProgramActive(state, program.Type);
                int currentBonusPercent = GetSupportProgramCurrentBonusPercent(state, program.Type);
                bool canRun = supportProgramsOpen &&
                    !supportProgramUsedToday &&
                    !(program.Type == ElectionSupportProgramType.ElectionDayHoliday && state.electionDayHolidayScheduled);
                string disabledReason = canRun
                    ? string.Empty
                    : GetSupportProgramDisabledReason(state, program.Type, supportProgramsOpen, supportProgramUsedToday, electionDay);

                writer.TypeBegin("ElectionSupportProgram");
                writer.PropertyName("index"); writer.Write(program.Index);
                writer.PropertyName("title"); writer.Write(program.Title);
                writer.PropertyName("description"); writer.Write(program.Description);
                writer.PropertyName("tooltip"); writer.Write(program.Tooltip);
                writer.PropertyName("cost"); writer.Write(cost);
                writer.PropertyName("bonusPercent"); writer.Write(GetSupportProgramBonusPercent(program.Type));
                writer.PropertyName("currentBonusPercent"); writer.Write(currentBonusPercent);
                writer.PropertyName("active"); writer.Write(active);
                writer.PropertyName("canRun"); writer.Write(canRun);
                writer.PropertyName("disabledReason"); writer.Write(disabledReason);
                writer.TypeEnd();
            }

            writer.ArrayEnd();
        }

        private static bool IsSupportProgramActive(ElectionState state, ElectionSupportProgramType type)
        {
            switch (type)
            {
                case ElectionSupportProgramType.ElectionDayHoliday:
                    return state.electionDayHolidayScheduled;
                case ElectionSupportProgramType.TeenVoterEducation:
                    return state.teenTurnoutBonusPercent > 0;
                case ElectionSupportProgramType.AdultVoterEducation:
                    return state.adultTurnoutBonusPercent > 0;
                case ElectionSupportProgramType.ElderlyVoterEducation:
                    return state.elderlyTurnoutBonusPercent > 0;
                case ElectionSupportProgramType.VoterEducation:
                    return state.uneducatedTurnoutBonusPercent > 0 ||
                           state.educatedTurnoutBonusPercent > 0;
                default:
                    return false;
            }
        }

        private static int GetSupportProgramCurrentBonusPercent(ElectionState state, ElectionSupportProgramType type)
        {
            switch (type)
            {
                case ElectionSupportProgramType.TeenVoterEducation:
                    return state.teenTurnoutBonusPercent;
                case ElectionSupportProgramType.AdultVoterEducation:
                    return state.adultTurnoutBonusPercent;
                case ElectionSupportProgramType.ElderlyVoterEducation:
                    return state.elderlyTurnoutBonusPercent;
                case ElectionSupportProgramType.VoterEducation:
                    return math.max(state.uneducatedTurnoutBonusPercent, state.educatedTurnoutBonusPercent);
                default:
                    return 0;
            }
        }

        private static int GetSupportProgramBonusPercent(ElectionSupportProgramType type)
        {
            return ElectionSupportPrograms.GetBonusPercent(type);
        }

        private static string GetSupportProgramDisabledReason(ElectionState state, ElectionSupportProgramType type, bool supportProgramsOpen, bool supportProgramUsedToday, bool electionDay)
        {
            if (electionDay)
                return "Civic programs are unavailable on election day.";

            if (supportProgramUsedToday)
                return $"Only one civic program can be funded per day. Today's program is {GetSupportProgramLabel(state.supportProgramIdToday)}.";

            if (type == ElectionSupportProgramType.ElectionDayHoliday && state.electionDayHolidayScheduled)
                return "Election day is already scheduled as a holiday.";

            if (!supportProgramsOpen)
                return "Civic programs are available before election day once candidates are selected.";

            return "Civic program unavailable right now.";
        }

        private static string GetSupportProgramLabel(int programIndex)
        {
            return ElectionSupportPrograms.TryGet(programIndex, out ElectionSupportProgramDefinition program)
                ? program.Title
                : "a civic program";
        }

        private static bool HasPollResults(ElectionState state)
        {
            return state.pollVotesA + state.pollVotesB + state.pollUndecided > 0 ||
                   state.stage == ElectionCampaignStage.PollReleased ||
                   state.stage == ElectionCampaignStage.Voting;
        }

        private static bool IsDonationStage(ElectionCampaignStage stage)
        {
            return stage == ElectionCampaignStage.CandidatesSelected ||
                   stage == ElectionCampaignStage.PollReleased;
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

        private static string FormatElectionDate(ElectionState state)
        {
            if (state.stage == ElectionCampaignStage.Voting &&
                TryGetDateFromDayKey(state.electionDayKey, out int year, out int month, out int day))
            {
                return ElectionUtility.FormatDate(year, month, day);
            }

            return FormatScheduledDate(state.electionYear, state.electionMonth);
        }

        private static string FormatScheduledDate(int year, int month)
        {
            return year <= 0 || month <= 0 ? string.Empty : ElectionUtility.FormatDate(year, month, 1);
        }

        private static bool TryGetDateFromDayKey(int dayKey, out int year, out int month, out int day)
        {
            year = dayKey / 10000;
            month = dayKey / 100 % 100;
            day = dayKey % 100;
            return year > 0 && month >= 1 && month <= 12 && day >= 1;
        }

        private static void WriteScheduleTimes(IJsonWriter writer)
        {
            WriteScheduleTimes(
                writer,
                ElectionUtility.GetConfiguredVotingStartMinute(),
                ElectionUtility.GetConfiguredVotingEndMinute(),
                ElectionUtility.ResultsAnnouncementMinute);
        }

        private static void WriteScheduleTimes(IJsonWriter writer, ElectionState state)
        {
            int votingStartMinute = state.stage == ElectionCampaignStage.Voting
                ? ElectionUtility.NormalizeVotingStartMinute(state.votingStartMinute)
                : ElectionUtility.GetConfiguredVotingStartMinute();
            int votingEndMinute = state.stage == ElectionCampaignStage.Voting
                ? ElectionUtility.NormalizeVotingEndMinute(state.votingEndMinute)
                : ElectionUtility.GetConfiguredVotingEndMinute();
            int resultsMinute = state.stage == ElectionCampaignStage.Voting && state.resultsAnnouncementMinute > 0
                ? state.resultsAnnouncementMinute
                : ElectionUtility.ResultsAnnouncementMinute;

            WriteScheduleTimes(writer, votingStartMinute, votingEndMinute, resultsMinute);
        }

        private static void WriteScheduleTimes(IJsonWriter writer, int votingStartMinute, int votingEndMinute, int resultsMinute)
        {
            writer.PropertyName("votingStartTime"); writer.Write(ElectionUtility.FormatClockTime(votingStartMinute));
            writer.PropertyName("votingEndTime"); writer.Write(ElectionUtility.FormatClockTime(votingEndMinute));
            writer.PropertyName("resultsTime"); writer.Write(ElectionUtility.FormatClockTime(resultsMinute));
        }

        private static string GetStageLabel(ElectionCampaignStage stage, bool hasCandidates)
        {
            if (!hasCandidates)
                return "No active campaign";

            switch (stage)
            {
                case ElectionCampaignStage.CandidatesSelected:
                    return "Candidates selected";
                case ElectionCampaignStage.PollReleased:
                    return "Poll released";
                case ElectionCampaignStage.Voting:
                    return "Election day";
                default:
                    return "Mayor term active";
            }
        }

        private string GetCycleLabel(ElectionState state)
        {
            if (state.HasCandidates)
                return state.acceleratedCycle ? "Accelerated mayoral race" : "Regular mayoral race";

            string mayorName = GetEntityName(state.mayor, string.Empty);
            if (!string.IsNullOrWhiteSpace(mayorName))
                return $"{mayorName} is serving as mayor.";

            return "No active campaign.";
        }

        private static string GetDonationTierLabel(int index)
        {
            return "Campaign support";
        }

        private static int GetCampaignBribeAmount(ElectionState state)
        {
            return state.campaignBribeAmount > 0 ? state.campaignBribeAmount : ElectionLifecycleSystem.BribeAmount;
        }

        private string GetEntityName(Entity entity, string fallback)
        {
            return ElectionNameUtility.GetCitizenFullName(m_NameSystem, EntityManager, entity, fallback);
        }
    }
}
