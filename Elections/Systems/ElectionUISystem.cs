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
            AddBinding(new TriggerBinding<int>(Mod.Id, "bribeMayor", BribeMayor));
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

        private void BribeMayor(int candidateIndex)
        {
            m_LifecycleSystem?.BribeMayor(candidateIndex);
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
                writer.PropertyName("bribeUsedToday"); writer.Write(false);
                writer.PropertyName("bribeCost"); writer.Write(ElectionLifecycleSystem.BribeAmount);
                writer.PropertyName("pollDate"); writer.Write(string.Empty);
                writer.PropertyName("electionDate"); writer.Write(string.Empty);
                WriteScheduleTimes(writer);
                WriteEmptyPoll(writer);
                WriteDonationTiers(writer);
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
            bool donationsOpen = !waitingForPopulationState && IsDonationStage(state.stage) && state.HasCandidates;
            int dayKey = hasDateTime ? ElectionUtility.CurrentCalendarDayKey(World, now) : 0;
            bool bribesOpen = hasDateTime && donationsOpen && state.mayor != Entity.Null;
            bool bribeUsedToday = hasDateTime && state.bribeDayKey == dayKey;

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
            writer.PropertyName("canBribe"); writer.Write(bribesOpen && !bribeUsedToday);
            writer.PropertyName("bribeUsedToday"); writer.Write(bribeUsedToday);
            writer.PropertyName("bribeCost"); writer.Write(ElectionLifecycleSystem.BribeAmount);
            writer.PropertyName("pollDate"); writer.Write(FormatScheduledDate(state.pollYear, state.pollMonth));
            writer.PropertyName("electionDate"); writer.Write(FormatScheduledDate(state.electionYear, state.electionMonth));
            WriteScheduleTimes(writer, state);
            WritePoll(writer, state, pollReleased);
            WriteDonationTiers(writer);
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
            int donationBonusPercent = (int)math.round(ElectionDonationTiers.GetBonusForAmount(donationAmount) * 100f);
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

        private static int GetMayorPortraitIndex(ElectionState state)
        {
            if (state.mayor == state.candidateA && state.candidateAPortraitIndex >= 0)
                return state.candidateAPortraitIndex;

            if (state.mayor == state.candidateB && state.candidateBPortraitIndex >= 0)
                return state.candidateBPortraitIndex;

            return CandidatePortraitCatalog.PickPortraitIndex(state.mayor, 4241);
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

        private static void WriteDonationTiers(IJsonWriter writer)
        {
            writer.PropertyName("donationTiers");
            writer.ArrayBegin(ElectionDonationTiers.Count);

            for (int i = 0; i < ElectionDonationTiers.Count; i++)
            {
                ElectionDonationTiers.TryGet(i, out ElectionDonationTier tier);
                writer.TypeBegin("ElectionDonationTier");
                writer.PropertyName("index"); writer.Write(i);
                writer.PropertyName("amount"); writer.Write(tier.Amount);
                writer.PropertyName("bonusPercent"); writer.Write((int)math.round(tier.Bonus * 100f));
                writer.PropertyName("label"); writer.Write(GetDonationTierLabel(i));
                writer.TypeEnd();
            }

            writer.ArrayEnd();
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
                   stage == ElectionCampaignStage.PollReleased ||
                   stage == ElectionCampaignStage.Voting;
        }

        private static string FormatScheduledDate(int year, int month)
        {
            return year <= 0 || month <= 0 ? string.Empty : ElectionUtility.FormatDate(year, month, 1);
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

        private string GetEntityName(Entity entity, string fallback)
        {
            return ElectionNameUtility.GetCitizenFullName(m_NameSystem, EntityManager, entity, fallback);
        }
    }
}
