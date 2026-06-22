using Colossal.UI.Binding;
using Elections.Components;
using Elections.Models;
using Elections.Bridge;
using Game;
using Game.Buildings;
using Game.Prefabs;
using Game.Rendering;
using Game.UI;
using Game.UI.InGame;
using System;
using System.Collections.Generic;
using Unity.Entities;
using Unity.Mathematics;

namespace Elections.Systems
{
    public partial class ElectionUISystem : UISystemBase
    {
        private const int kMayorChoiceCacheRefreshUpdates = 8;

        private EntityQuery m_StateQuery;
        private NameSystem m_NameSystem;
        private CameraUpdateSystem m_CameraUpdateSystem;
        private ElectionLifecycleSystem m_LifecycleSystem;
        private MayorWorkplaceSystem m_MayorWorkplaceSystem;
        private PrefabSystem m_PrefabSystem;
        private SelectedInfoUISystem m_SelectedInfoUISystem;
        private readonly List<Entity> m_MayorHomeChoices = new List<Entity>();
        private readonly List<Entity> m_MayorWorkplaceChoices = new List<Entity>();
        private bool m_MayorChoiceCacheValid;
        private bool m_MayorHomeChoicesLimited;
        private bool m_MayorWorkplaceChoicesLimited;
        private int m_UpdateSerial;
        private int m_MayorChoiceCacheUpdateSerial;
        private Entity m_CachedChoiceMayor;
        private Entity m_CachedChoiceTargetHome;
        private Entity m_CachedChoiceTargetWorkplace;
        private Entity m_CachedChoiceCurrentHome;
        private Entity m_CachedChoiceCurrentWorkplace;
        private Entity m_CachedChoiceSelectedBuilding;
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
            m_MayorWorkplaceSystem = World.GetOrCreateSystemManaged<MayorWorkplaceSystem>();
            m_PrefabSystem = World.GetOrCreateSystemManaged<PrefabSystem>();
            m_SelectedInfoUISystem = World.GetOrCreateSystemManaged<SelectedInfoUISystem>();
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
            AddBinding(new TriggerBinding<int, string>(Mod.Id, "renameParty", RenameParty));
            AddBinding(new TriggerBinding<int, string>(Mod.Id, "setPartyColor", SetPartyColor));
            AddBinding(new TriggerBinding<int, string>(Mod.Id, "setPartyTags", SetPartyTags));
            AddBinding(new TriggerBinding<int, int>(Mod.Id, "donate", Donate));
            AddBinding(new TriggerBinding<int>(Mod.Id, "runSupportProgram", RunSupportProgram));
            AddBinding(new TriggerBinding<int>(Mod.Id, "bribeMayor", BribeMayor));
            AddBinding(new TriggerBinding<int>(Mod.Id, "endorseCandidate", EndorseCandidate));
            AddBinding(new TriggerBinding(Mod.Id, "cashAssistance", CashAssistance));
            AddBinding(new TriggerBinding<int>(Mod.Id, "tamperVotes", TamperVotes));
            AddBinding(new TriggerBinding<int, bool>(Mod.Id, "setLegislation", SetLegislation));
            AddBinding(new TriggerBinding<bool>(Mod.Id, "setShowVotingLocations", SetShowVotingLocations));
            AddBinding(new TriggerBinding(Mod.Id, "useSelectedMayorHome", UseSelectedMayorHome));
            AddBinding(new TriggerBinding(Mod.Id, "useSelectedMayorWorkplace", UseSelectedMayorWorkplace));
            AddBinding(new TriggerBinding<int, int>(Mod.Id, "setMayorHome", SetMayorHome));
            AddBinding(new TriggerBinding<int, int>(Mod.Id, "setMayorWorkplace", SetMayorWorkplace));
            AddBinding(new TriggerBinding(Mod.Id, "focusMayorHome", FocusMayorHome));
            AddBinding(new TriggerBinding(Mod.Id, "focusMayorWorkplace", FocusMayorWorkplace));
        }

        protected override void OnUpdate()
        {
            base.OnUpdate();
            m_UpdateSerial++;

            if (ShowVotingLocations && !(Mod.m_Setting?.EnableElections ?? false))
                SetShowVotingLocations(false);

            m_PanelBinding?.Update();
        }

        public void UpdateUseUniversalModMenu(bool value)
        {
            m_UseUniversalModMenuBinding?.Update(value);
        }

        public void UpdateElectionsEnabled(bool value)
        {
            if (!value && ShowVotingLocations)
                SetShowVotingLocations(false);

            m_PanelBinding?.Update();
        }

        private void Donate(int candidateIndex, int tierIndex)
        {
            m_LifecycleSystem?.Donate(candidateIndex, tierIndex);
            m_PanelBinding?.Update();
        }

        private void RenameParty(int partyIndex, string name)
        {
            m_LifecycleSystem?.RenameParty(partyIndex, name);
            m_PanelBinding?.Update();
        }

        private void SetPartyColor(int partyIndex, string color)
        {
            m_LifecycleSystem?.SetPartyColor(partyIndex, color);
            m_PanelBinding?.Update();
        }

        private void SetPartyTags(int partyIndex, string tagIds)
        {
            m_LifecycleSystem?.SetPartyTags(partyIndex, tagIds);
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

        private void CashAssistance()
        {
            m_LifecycleSystem?.CashAssistance();
            m_PanelBinding?.Update();
        }

        private void TamperVotes(int candidateIndex)
        {
            m_LifecycleSystem?.ScheduleVoteTampering(candidateIndex);
            m_PanelBinding?.Update();
        }

        private void SetLegislation(int legislationIndex, bool active)
        {
            m_LifecycleSystem?.SetElectionLegislation(legislationIndex, active);
            m_PanelBinding?.Update();
        }

        private void SetShowVotingLocations(bool value)
        {
            ShowVotingLocations = value && (Mod.m_Setting?.EnableElections ?? false);
            m_ShowVotingLocationsBinding?.Update(ShowVotingLocations);
        }

        private void UseSelectedMayorHome()
        {
            Entity selected = m_SelectedInfoUISystem != null ? m_SelectedInfoUISystem.selectedEntity : Entity.Null;
            if (m_MayorWorkplaceSystem != null && m_MayorWorkplaceSystem.TrySetMayorHome(selected))
            {
                InvalidateMayorChoiceCache();
                m_PanelBinding?.Update();
            }
        }

        private void UseSelectedMayorWorkplace()
        {
            Entity selected = m_SelectedInfoUISystem != null ? m_SelectedInfoUISystem.selectedEntity : Entity.Null;
            if (m_MayorWorkplaceSystem != null && m_MayorWorkplaceSystem.TrySetMayorWorkplace(selected))
            {
                InvalidateMayorChoiceCache();
                m_PanelBinding?.Update();
            }
        }

        private void SetMayorHome(int entityIndex, int entityVersion)
        {
            Entity home = new Entity
            {
                Index = entityIndex,
                Version = entityVersion
            };

            if (m_MayorWorkplaceSystem != null && m_MayorWorkplaceSystem.TrySetMayorHome(home))
            {
                InvalidateMayorChoiceCache();
                m_PanelBinding?.Update();
            }
        }

        private void SetMayorWorkplace(int entityIndex, int entityVersion)
        {
            Entity workplace = new Entity
            {
                Index = entityIndex,
                Version = entityVersion
            };

            if (m_MayorWorkplaceSystem != null && m_MayorWorkplaceSystem.TrySetMayorWorkplace(workplace))
            {
                InvalidateMayorChoiceCache();
                m_PanelBinding?.Update();
            }
        }

        private void FocusCandidate(int candidateIndex)
        {
            if (!TryGetPreparedState(out ElectionState state))
                return;

            Entity candidate = state.IsActiveCandidateIndex(candidateIndex) ? state.GetCandidate(candidateIndex) : Entity.Null;
            NavigateTo(candidate);
        }

        private void FocusMayor()
        {
            if (!TryGetPreparedState(out ElectionState state))
                return;

            NavigateTo(state.mayor);
        }

        private void FocusMayorHome()
        {
            if (!TryGetPreparedState(out ElectionState state) || m_MayorWorkplaceSystem == null)
                return;

            NavigateTo(m_MayorWorkplaceSystem.GetEffectiveMayorHome(state));
        }

        private void FocusMayorWorkplace()
        {
            if (!TryGetPreparedState(out ElectionState state) || m_MayorWorkplaceSystem == null)
                return;

            NavigateTo(m_MayorWorkplaceSystem.GetEffectiveMayorWorkplace(state));
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
            bool realisticTripsReady = hasDateTime && RealisticTripsBridge.IsAvailable;
            string currentDate = hasDateTime ? ElectionUtility.FormatCurrentDate(World, now) : string.Empty;
            bool electionsEnabled = Mod.m_Setting?.EnableElections ?? false;
            bool partiesEnabled = Mod.m_Setting?.EnableParties ?? false;
            int currentPopulation = m_LifecycleSystem?.GetCurrentPopulationForUI() ?? 0;
            bool populationReady = currentPopulation >= ElectionLifecycleSystem.MinimumPopulation;

            writer.TypeBegin("ElectionPanel");
            writer.PropertyName("enabled"); writer.Write(electionsEnabled);
            writer.PropertyName("partiesEnabled"); writer.Write(partiesEnabled);
            writer.PropertyName("realisticTripsReady"); writer.Write(realisticTripsReady);
            writer.PropertyName("currentDate"); writer.Write(currentDate);
            writer.PropertyName("currentPopulation"); writer.Write(currentPopulation);
            writer.PropertyName("minimumPopulation"); writer.Write(ElectionLifecycleSystem.MinimumPopulation);
            writer.PropertyName("populationReady"); writer.Write(populationReady);

            if (!TryGetPreparedState(out ElectionState state))
            {
                bool waitingForRealisticTrips = electionsEnabled && !realisticTripsReady;
                bool waitingForPopulation = electionsEnabled && realisticTripsReady && !populationReady;
                writer.PropertyName("hasState"); writer.Write(false);
                writer.PropertyName("waitingForPopulation"); writer.Write(waitingForPopulation);
                writer.PropertyName("stage"); writer.Write("None");
                writer.PropertyName("stageLabel"); writer.Write(waitingForRealisticTrips
                    ? L("Panel.Stage.WaitingForRealisticTrips", "Waiting for Realistic Trips")
                    : waitingForPopulation
                    ? L("Panel.Stage.WaitingForResidents", "Waiting for residents")
                    : L("Panel.Stage.NoElectionData", "No election data"));
                writer.PropertyName("cycleLabel"); writer.Write(waitingForRealisticTrips
                    ? L("Panel.Cycle.WaitingRealisticTrips", "Elections require Realistic Trips to be installed, enabled, and loaded before the city date and voting trips are available.")
                    : waitingForPopulation
                    ? LF("Panel.Cycle.WaitingPopulation", "Elections start at {0:n0} population. Current population: {1:n0}.", ElectionLifecycleSystem.MinimumPopulation, currentPopulation)
                    : L("Panel.Cycle.NotInitialized", "The election system has not initialized yet."));
                writer.PropertyName("runoffEnabledForCycle"); writer.Write(false);
                writer.PropertyName("runoffActive"); writer.Write(false);
                writer.PropertyName("runoffOriginalCandidateCount"); writer.Write(0);
                writer.PropertyName("pendingMayorName"); writer.Write(string.Empty);
                writer.PropertyName("pendingMayorInaugurationDate"); writer.Write(string.Empty);
                writer.PropertyName("pollReleased"); writer.Write(false);
                writer.PropertyName("donationsOpen"); writer.Write(false);
                writer.PropertyName("canDonate"); writer.Write(false);
                writer.PropertyName("donationUsedToday"); writer.Write(false);
                writer.PropertyName("bribesOpen"); writer.Write(false);
                writer.PropertyName("canBribe"); writer.Write(false);
                writer.PropertyName("canEndorse"); writer.Write(false);
                writer.PropertyName("endorsementUsed"); writer.Write(false);
                writer.PropertyName("endorsedCandidateIndex"); writer.Write(-1);
                writer.PropertyName("canTamper"); writer.Write(false);
                writer.PropertyName("voteTamperingScheduled"); writer.Write(false);
                writer.PropertyName("voteTamperingCandidateIndex"); writer.Write(-1);
                writer.PropertyName("bribeUsedToday"); writer.Write(false);
                writer.PropertyName("bribeBlocked"); writer.Write(false);
                writer.PropertyName("bribeMeetingPending"); writer.Write(false);
                writer.PropertyName("bribeCost"); writer.Write(ElectionLifecycleSystem.BribeAmount);
                writer.PropertyName("cashAssistanceTurnoutBonusPercent"); writer.Write(0);
                WriteSupportProgramPanel(writer, default(ElectionState), false, false, false);
                WriteLegislationPanel(writer, default(ElectionState), false, false, false);
                writer.PropertyName("pollDate"); writer.Write(string.Empty);
                writer.PropertyName("electionDate"); writer.Write(string.Empty);
                WriteScheduleTimes(writer);
                WriteEmptyPoll(writer);
                WriteDonationTiers(writer, ElectionDonationTiers.FixedDonationAmount);
                writer.PropertyName("candidates");
                writer.ArrayBegin(0);
                writer.ArrayEnd();
                writer.PropertyName("candidateA"); WriteEmptyCandidate(writer, 0, CandidateFallbackName(0));
                writer.PropertyName("candidateB"); WriteEmptyCandidate(writer, 1, CandidateFallbackName(1));
                writer.PropertyName("parties");
                writer.ArrayBegin(0);
                writer.ArrayEnd();
                writer.PropertyName("partyTags");
                WritePartyTagDefinitions(writer);
                WriteEmptyMayor(writer);
                WriteMayorResidence(writer, default(ElectionState), false);
                writer.TypeEnd();
                return;
            }

            bool pollReleased = HasPollResults(state);
            bool waitingForRealisticTripsState = electionsEnabled && !realisticTripsReady;
            bool waitingForPopulationState = electionsEnabled &&
                realisticTripsReady &&
                !populationReady &&
                !state.HasCandidates &&
                state.stage == ElectionCampaignStage.None;
            bool electionDay = hasDateTime && IsElectionDay(state, now);
            bool donationsOpen = hasDateTime && !waitingForPopulationState && IsDonationStage(state.stage) && state.HasCandidates && !electionDay;
            bool donationUsedToday = hasDateTime &&
                state.donationDayKey == ElectionUtility.CurrentCalendarDayKey(World, now);
            bool bribesOpen = hasDateTime && donationsOpen && state.mayor != Entity.Null;
            bool bribeMeetingPending = hasDateTime &&
                state.IsActiveCandidateIndex(state.bribeMeetingCandidateIndex) &&
                state.bribeMeetingDeadlineTicks > 0;
            bool bribeBlocked = hasDateTime &&
                (bribeMeetingPending || state.bribeBlockedUntilTicks > now.Ticks);
            bool bribeUsedToday = bribeBlocked;
            bool endorsementUsed = state.IsActiveCandidateIndex(state.mayorEndorsementCandidateIndex);
            bool voteTamperingScheduled = state.IsActiveCandidateIndex(state.voteTamperingCandidateIndex);
            bool supportProgramsOpen = hasDateTime && !waitingForPopulationState && IsDonationStage(state.stage) && state.HasCandidates && !electionDay;
            bool legislationOpen = supportProgramsOpen;
            bool supportProgramUsedToday = hasDateTime &&
                state.supportProgramDayKey == ElectionUtility.CurrentCalendarDayKey(World, now);
            bool legislationActionUsedToday = hasDateTime &&
                state.legislationActionDayKey == ElectionUtility.CurrentCalendarDayKey(World, now);

            writer.PropertyName("hasState"); writer.Write(true);
            writer.PropertyName("waitingForPopulation"); writer.Write(waitingForPopulationState);
            writer.PropertyName("stage"); writer.Write(state.stage.ToString());
            writer.PropertyName("stageLabel"); writer.Write(waitingForRealisticTripsState
                ? L("Panel.Stage.WaitingForRealisticTrips", "Waiting for Realistic Trips")
                : waitingForPopulationState ? L("Panel.Stage.WaitingForResidents", "Waiting for residents") : GetStageLabel(state, state.HasCandidates));
            writer.PropertyName("cycleLabel"); writer.Write(waitingForRealisticTripsState
                ? L("Panel.Cycle.WaitingRealisticTrips", "Elections require Realistic Trips to be installed, enabled, and loaded before the city date and voting trips are available.")
                : waitingForPopulationState
                ? LF("Panel.Cycle.WaitingPopulation", "Elections start at {0:n0} population. Current population: {1:n0}.", ElectionLifecycleSystem.MinimumPopulation, currentPopulation)
                : GetCycleLabel(state));
            writer.PropertyName("runoffEnabledForCycle"); writer.Write(state.runoffEnabledForCycle);
            writer.PropertyName("runoffActive"); writer.Write(state.runoffActive);
            writer.PropertyName("runoffOriginalCandidateCount"); writer.Write(state.runoffOriginalCandidateCount);
            writer.PropertyName("pendingMayorName"); writer.Write(state.pendingMayor != Entity.Null ? GetEntityName(state.pendingMayor, L("Panel.MayorElect", "Mayor-elect")) : string.Empty);
            writer.PropertyName("pendingMayorInaugurationDate"); writer.Write(state.pendingMayor != Entity.Null && state.pendingMayorTermYear > 0 ? ElectionUtility.FormatDate(state.pendingMayorTermYear, 1, 1) : string.Empty);
            writer.PropertyName("pollReleased"); writer.Write(pollReleased);
            writer.PropertyName("donationsOpen"); writer.Write(donationsOpen);
            writer.PropertyName("canDonate"); writer.Write(donationsOpen && !donationUsedToday);
            writer.PropertyName("donationUsedToday"); writer.Write(donationUsedToday);
            writer.PropertyName("bribesOpen"); writer.Write(bribesOpen);
            writer.PropertyName("canBribe"); writer.Write(bribesOpen && !bribeBlocked);
            writer.PropertyName("canEndorse"); writer.Write(bribesOpen && !bribeBlocked && !endorsementUsed);
            writer.PropertyName("endorsementUsed"); writer.Write(endorsementUsed);
            writer.PropertyName("endorsedCandidateIndex"); writer.Write(endorsementUsed ? state.mayorEndorsementCandidateIndex : -1);
            writer.PropertyName("canTamper"); writer.Write(bribesOpen && !bribeBlocked && !voteTamperingScheduled);
            writer.PropertyName("voteTamperingScheduled"); writer.Write(voteTamperingScheduled);
            writer.PropertyName("voteTamperingCandidateIndex"); writer.Write(voteTamperingScheduled ? state.voteTamperingCandidateIndex : -1);
            writer.PropertyName("bribeUsedToday"); writer.Write(bribeUsedToday);
            writer.PropertyName("bribeBlocked"); writer.Write(bribeBlocked);
            writer.PropertyName("bribeMeetingPending"); writer.Write(bribeMeetingPending);
            writer.PropertyName("bribeCost"); writer.Write(GetCampaignBribeAmount(state));
            writer.PropertyName("cashAssistanceTurnoutBonusPercent"); writer.Write(state.cashAssistanceTurnoutBonusPercent);
            WriteSupportProgramPanel(writer, state, supportProgramsOpen, supportProgramUsedToday, electionDay);
            WriteLegislationPanel(writer, state, legislationOpen, electionDay, legislationActionUsedToday);
            writer.PropertyName("pollDate"); writer.Write(FormatScheduledDate(state.pollYear, state.pollMonth));
            writer.PropertyName("electionDate"); writer.Write(FormatElectionDate(state));
            WriteScheduleTimes(writer, state);
            WritePoll(writer, state, pollReleased);
            WriteDonationTiers(writer, state.campaignDonationAmount);
            writer.PropertyName("candidates");
            WriteCandidates(writer, state);
            writer.PropertyName("candidateA");
            if (state.HasCandidates)
                WriteCandidate(writer, state, 0);
            else
                WriteEmptyCandidate(writer, 0, CandidateFallbackName(0));

            writer.PropertyName("candidateB");
            if (state.HasCandidates)
                WriteCandidate(writer, state, 1);
            else
                WriteEmptyCandidate(writer, 1, CandidateFallbackName(1));
            writer.PropertyName("parties");
            WriteParties(writer, state, hasDateTime, now);
            writer.PropertyName("partyTags");
            WritePartyTagDefinitions(writer);
            WriteMayor(writer, state);
            WriteMayorResidence(writer, state, true);
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

        private void WriteCandidates(IJsonWriter writer, ElectionState state)
        {
            int candidateCount = state.HasCandidates ? state.ActiveCandidateCount : 0;
            writer.ArrayBegin(candidateCount);
            for (int i = 0; i < candidateCount; i++)
                WriteCandidate(writer, state, i);
            writer.ArrayEnd();
        }

        private void WriteCandidate(IJsonWriter writer, ElectionState state, int index)
        {
            Entity candidate = state.GetCandidate(index);
            int effectId = state.GetCandidateEffectId(index);
            int portraitIndex = state.GetCandidatePortraitIndex(index);
            int tagId = state.GetCandidateTagId(index);
            int donationAmount = state.GetCandidateDonation(index);
            ElectionEffectDefinition effect = GetCandidateEffectDefinition(state, index);
            int donationBonusPercent = (int)math.round(ElectionDonationTiers.GetBonusForAmount(donationAmount, state.campaignDonationAmount) * 100f);
            ElectionDonationTiers.TryGet(0, state.campaignDonationAmount, out ElectionDonationTier donationTier);
            int donationCost = ElectionCandidateTags.GetDonationCost(tagId, donationTier.Amount);
            int tagVoteEffectMultiplier = ElectionCandidateTags.GetCampaignVoteEffectMultiplier(state, index, tagId);
            ElectionCandidateTagDefinition tag = ElectionCandidateTags.Get(tagId);
            bool canFocus = candidate != Entity.Null && EntityManager.Exists(candidate);
            bool partiesEnabled = Mod.m_Setting?.EnableParties ?? false;
            int partyIndex = state.GetCandidatePartyIndex(index);

            writer.TypeBegin("ElectionCandidate");
            writer.PropertyName("index"); writer.Write(index);
            writer.PropertyName("exists"); writer.Write(candidate != Entity.Null);
            writer.PropertyName("name"); writer.Write(GetEntityName(candidate, CandidateFallbackName(index)));
            writer.PropertyName("partyIndex"); writer.Write(partiesEnabled ? partyIndex : -1);
            writer.PropertyName("partyName"); writer.Write(partiesEnabled ? state.GetPartyName(partyIndex) : string.Empty);
            writer.PropertyName("partyColor"); writer.Write(partiesEnabled ? FormatPartyColor(state.GetPartyColor(partyIndex)) : string.Empty);
            writer.PropertyName("partyReputation"); writer.Write(partiesEnabled ? state.GetPartyReputation(partyIndex) : 0);
            writer.PropertyName("portrait"); writer.Write(CandidatePortraitCatalog.GetPortraitImageSource(EntityManager, candidate, portraitIndex));
            writer.PropertyName("canFocus"); writer.Write(canFocus);
            writer.PropertyName("bio"); writer.Write(GetCandidateBio(state, index));
            writer.PropertyName("tagName"); writer.Write(tag.Name);
            writer.PropertyName("tagDescription"); writer.Write(ElectionCandidateTags.GetDescription(tagId, tagVoteEffectMultiplier));
            writer.PropertyName("tagTone"); writer.Write(tag.Tone.ToString());
            writer.PropertyName("effectName"); writer.Write(effect.Name);
            writer.PropertyName("effectDescription"); writer.Write(LF("Panel.Candidate.EffectDescription", "If elected, this platform {0}.", effect.Description));
            writer.PropertyName("platformImpacts"); WritePlatformImpacts(writer, effect);
            writer.PropertyName("donationAmount"); writer.Write(donationAmount);
            writer.PropertyName("donationCost"); writer.Write(donationCost);
            writer.PropertyName("donationBonusPercent"); writer.Write(donationBonusPercent);
            writer.PropertyName("donated"); writer.Write(donationAmount > 0);
            writer.TypeEnd();
        }

        private void WriteParties(IJsonWriter writer, ElectionState state, bool hasDateTime, DateTime now)
        {
            if (!(Mod.m_Setting?.EnableParties ?? false))
            {
                writer.ArrayBegin(0);
                writer.ArrayEnd();
                return;
            }

            int partyCount = GetPartyCountForUI(state);
            int currentYear = 0;
            if (hasDateTime)
                ElectionUtility.GetCurrentCalendarDate(World, now, out currentYear, out _, out _);

            writer.ArrayBegin(partyCount);
            for (int i = 0; i < partyCount; i++)
                WriteParty(writer, state, i, currentYear);
            writer.ArrayEnd();
        }

        private void WriteParty(IJsonWriter writer, ElectionState state, int partyIndex, int currentYear)
        {
            bool canReplaceTag = state.stage == ElectionCampaignStage.None &&
                                 currentYear > 0 &&
                                 state.GetPartyLastTagReplacementYear(partyIndex) != currentYear;
            string disabledReason = state.stage != ElectionCampaignStage.None
                ? L("Panel.Party.Replace.Disabled.Cycle", "Party tags can only be replaced outside the election cycle.")
                : currentYear <= 0
                    ? L("Panel.Party.Replace.Disabled.NoYear", "Party tag replacement needs the current city year.")
                    : state.GetPartyLastTagReplacementYear(partyIndex) == currentYear
                        ? L("Panel.Party.Replace.Disabled.UsedYear", "This party has already replaced its tags this year.")
                        : string.Empty;

            writer.TypeBegin("ElectionParty");
            writer.PropertyName("index"); writer.Write(partyIndex);
            writer.PropertyName("name"); writer.Write(state.GetPartyName(partyIndex));
            writer.PropertyName("color"); writer.Write(FormatPartyColor(state.GetPartyColor(partyIndex)));
            writer.PropertyName("reputation"); writer.Write(state.GetPartyReputation(partyIndex));
            writer.PropertyName("consecutiveTerms"); writer.Write(state.GetPartyConsecutiveTerms(partyIndex));
            writer.PropertyName("wins"); writer.Write(state.GetPartyWins(partyIndex));
            writer.PropertyName("lastTagReplacementYear"); writer.Write(state.GetPartyLastTagReplacementYear(partyIndex));
            writer.PropertyName("canReplaceTag"); writer.Write(canReplaceTag);
            writer.PropertyName("replacementDisabledReason"); writer.Write(disabledReason);
            writer.PropertyName("tags");
            writer.ArrayBegin(ElectionPartyTags.TagsPerParty);
            for (int slot = 0; slot < ElectionPartyTags.TagsPerParty; slot++)
                WritePartyTag(writer, ElectionPartyTags.Get(state.GetPartyTagId(partyIndex, slot)), slot);
            writer.ArrayEnd();
            writer.TypeEnd();
        }

        private static void WritePartyTagDefinitions(IJsonWriter writer)
        {
            writer.ArrayBegin(ElectionPartyTags.Count);
            for (int tagId = 1; tagId <= ElectionPartyTags.Count; tagId++)
                WritePartyTag(writer, ElectionPartyTags.Get(tagId), -1);
            writer.ArrayEnd();
        }

        private static void WritePartyTag(IJsonWriter writer, ElectionPartyTagDefinition tag, int slot)
        {
            writer.TypeBegin("ElectionPartyTag");
            writer.PropertyName("slot"); writer.Write(slot);
            writer.PropertyName("id"); writer.Write(tag.Id);
            writer.PropertyName("name"); writer.Write(tag.Name);
            writer.PropertyName("description"); writer.Write(tag.Description);
            writer.PropertyName("value"); writer.Write(tag.Value);
            writer.PropertyName("tone"); writer.Write(tag.Tone.ToString());
            writer.TypeEnd();
        }

        private static int GetPartyCountForUI(ElectionState state)
        {
            if (state.HasCandidates)
                return state.ActiveCandidateCount;

            int configuredCount = Mod.m_Setting?.CandidateCount ?? ElectionState.DefaultCandidateCount;
            return ElectionState.NormalizeCandidateCount(state.candidateCount > 0 ? state.candidateCount : configuredCount);
        }

        private static string FormatPartyColor(int color)
        {
            color &= 0xffffff;
            return $"#{color:x6}";
        }

        private static void WriteEmptyCandidate(IJsonWriter writer, int index, string fallbackName)
        {
            writer.TypeBegin("ElectionCandidate");
            writer.PropertyName("index"); writer.Write(index);
            writer.PropertyName("exists"); writer.Write(false);
            writer.PropertyName("name"); writer.Write(fallbackName);
            writer.PropertyName("partyIndex"); writer.Write(-1);
            writer.PropertyName("partyName"); writer.Write(string.Empty);
            writer.PropertyName("partyColor"); writer.Write(string.Empty);
            writer.PropertyName("partyReputation"); writer.Write(0);
            writer.PropertyName("portrait"); writer.Write(string.Empty);
            writer.PropertyName("canFocus"); writer.Write(false);
            writer.PropertyName("bio"); writer.Write(string.Empty);
            writer.PropertyName("tagName"); writer.Write(string.Empty);
            writer.PropertyName("tagDescription"); writer.Write(string.Empty);
            writer.PropertyName("tagTone"); writer.Write(ElectionCandidateTagTone.Neutral.ToString());
            writer.PropertyName("effectName"); writer.Write(L("Panel.Candidate.NoPlatform", "No platform"));
            writer.PropertyName("effectDescription"); writer.Write(L("Panel.Candidate.EmptyDescription", "No candidate has been selected yet."));
            writer.PropertyName("platformImpacts"); WriteEmptyPlatformImpacts(writer);
            writer.PropertyName("donationAmount"); writer.Write(0);
            writer.PropertyName("donationCost"); writer.Write(0);
            writer.PropertyName("donationBonusPercent"); writer.Write(0);
            writer.PropertyName("donated"); writer.Write(false);
            writer.TypeEnd();
        }

        private void WriteMayor(IJsonWriter writer, ElectionState state)
        {
            ElectionCandidateTagDefinition tag = ElectionCandidateTags.Get(state.mayorTagId);
            ElectionEffectDefinition effect = GetMayorEffectDefinition(state);
            bool canFocus = state.mayor != Entity.Null && EntityManager.Exists(state.mayor);
            string mayorName = GetEntityName(state.mayor, string.Empty);
            bool partiesEnabled = Mod.m_Setting?.EnableParties ?? false;

            writer.PropertyName("mayorName"); writer.Write(mayorName);
            writer.PropertyName("mayorPartyIndex"); writer.Write(partiesEnabled ? state.mayorPartyIndex : -1);
            writer.PropertyName("mayorPartyName"); writer.Write(partiesEnabled && ElectionState.IsPartyIndex(state.mayorPartyIndex) ? state.GetPartyName(state.mayorPartyIndex) : string.Empty);
            writer.PropertyName("mayorPartyColor"); writer.Write(partiesEnabled && ElectionState.IsPartyIndex(state.mayorPartyIndex) ? FormatPartyColor(state.GetPartyColor(state.mayorPartyIndex)) : string.Empty);
            writer.PropertyName("mayorPartyReputation"); writer.Write(partiesEnabled && ElectionState.IsPartyIndex(state.mayorPartyIndex) ? state.GetPartyReputation(state.mayorPartyIndex) : 0);
            writer.PropertyName("mayorPortrait"); writer.Write(canFocus
                ? CandidatePortraitCatalog.GetPortraitImageSource(EntityManager, state.mayor, GetMayorPortraitIndex(state))
                : string.Empty);
            writer.PropertyName("mayorCanFocus"); writer.Write(canFocus);
            writer.PropertyName("mayorEffectName"); writer.Write(effect.Name);
            writer.PropertyName("mayorEffectDescription"); writer.Write(state.mayorEffectId == 0
                ? L("Panel.Mayor.TemporaryDescription", "Temporary mayoral platform. No city modifiers are applied; this mayor supervises the transition until an elected mayor takes office.")
                : LF("Panel.Mayor.CurrentDescription", "Current mayoral platform. It {0}.", effect.Description));
            writer.PropertyName("mayorTagName"); writer.Write(tag.Name);
            writer.PropertyName("mayorTagDescription"); writer.Write(tag.Description);
            writer.PropertyName("mayorTagTone"); writer.Write(tag.Tone.ToString());
            writer.PropertyName("mayorPlatformImpacts");
            if (state.mayorEffectId == 0)
                WriteEmptyPlatformImpacts(writer);
            else
                WritePlatformImpacts(writer, effect);
            writer.PropertyName("mayorTemporary"); writer.Write(state.mayorEffectId == 0 && state.mayor != Entity.Null);
        }

        private static ElectionEffectDefinition GetCandidateEffectDefinition(ElectionState state, int candidateIndex)
        {
            int tagId = state.GetCandidateTagId(candidateIndex);
            float candidatePlatformScale = ElectionCandidateTags.GetPlatformEffectScale(tagId);
            float positiveScale = candidatePlatformScale;
            float negativeScale = (state.GetCandidateNegativeSoftened(candidateIndex) ? 0.5f : 1f) * candidatePlatformScale;
            if (Mod.m_Setting?.EnableParties ?? false)
            {
                int partyIndex = state.GetCandidatePartyIndex(candidateIndex);
                positiveScale *= ElectionPartyTags.GetPositivePlatformScale(state, partyIndex);
                negativeScale *= ElectionPartyTags.GetNegativePlatformScale(state, partyIndex);
            }

            return ElectionEffects.Get(state.GetCandidateEffectId(candidateIndex), positiveScale, negativeScale);
        }

        private static ElectionEffectDefinition GetMayorEffectDefinition(ElectionState state)
        {
            int tagId = state.mayorTagId;
            float candidatePlatformScale = ElectionCandidateTags.GetPlatformEffectScale(tagId);
            float positiveScale = candidatePlatformScale;
            float negativeScale = (state.mayorNegativeSoftened ? 0.5f : 1f) * candidatePlatformScale;
            if (Mod.m_Setting?.EnableParties ?? false)
            {
                positiveScale *= ElectionPartyTags.GetPositivePlatformScale(state, state.mayorPartyIndex);
                negativeScale *= ElectionPartyTags.GetNegativePlatformScale(state, state.mayorPartyIndex);
            }

            return ElectionEffects.Get(state.mayorEffectId, positiveScale, negativeScale);
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
                picked = CandidatePortraitCatalog.PickDifferentPortraitIndex(
                    EntityManager,
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

        private static void WriteEmptyMayor(IJsonWriter writer)
        {
            writer.PropertyName("mayorName"); writer.Write(string.Empty);
            writer.PropertyName("mayorPartyIndex"); writer.Write(-1);
            writer.PropertyName("mayorPartyName"); writer.Write(string.Empty);
            writer.PropertyName("mayorPartyColor"); writer.Write(string.Empty);
            writer.PropertyName("mayorPartyReputation"); writer.Write(0);
            writer.PropertyName("mayorPortrait"); writer.Write(string.Empty);
            writer.PropertyName("mayorCanFocus"); writer.Write(false);
            writer.PropertyName("mayorEffectName"); writer.Write(string.Empty);
            writer.PropertyName("mayorEffectDescription"); writer.Write(string.Empty);
            writer.PropertyName("mayorTagName"); writer.Write(string.Empty);
            writer.PropertyName("mayorTagDescription"); writer.Write(string.Empty);
            writer.PropertyName("mayorTagTone"); writer.Write(ElectionCandidateTagTone.Neutral.ToString());
            writer.PropertyName("mayorPlatformImpacts"); WriteEmptyPlatformImpacts(writer);
            writer.PropertyName("mayorTemporary"); writer.Write(false);
        }

        private void WriteMayorResidence(IJsonWriter writer, ElectionState state, bool hasState)
        {
            Entity targetHome = hasState && m_MayorWorkplaceSystem != null
                ? m_MayorWorkplaceSystem.GetEffectiveMayorHome(state)
                : Entity.Null;
            Entity targetWorkplace = hasState && m_MayorWorkplaceSystem != null
                ? m_MayorWorkplaceSystem.GetEffectiveMayorWorkplace(state)
                : Entity.Null;
            Entity currentHome = hasState && m_MayorWorkplaceSystem != null
                ? m_MayorWorkplaceSystem.GetCurrentMayorHome(state.mayor)
                : Entity.Null;
            Entity currentWorkplace = hasState && m_MayorWorkplaceSystem != null
                ? m_MayorWorkplaceSystem.GetCurrentMayorWorkplace(state.mayor)
                : Entity.Null;
            Entity selectedBuilding = m_SelectedInfoUISystem != null ? m_SelectedInfoUISystem.selectedEntity : Entity.Null;
            bool selectedCanBeHome = m_MayorWorkplaceSystem != null && m_MayorWorkplaceSystem.IsValidMayorHome(selectedBuilding);
            bool selectedCanBeWorkplace = m_MayorWorkplaceSystem != null && m_MayorWorkplaceSystem.IsValidMayorWorkplace(selectedBuilding);

            if (hasState && m_MayorWorkplaceSystem != null)
            {
                EnsureMayorChoiceCache(state.mayor, targetHome, targetWorkplace, currentHome, currentWorkplace, selectedBuilding, state);
            }
            else
            {
                InvalidateMayorChoiceCache();
            }

            writer.PropertyName("mayorHome");
            WriteMayorBuildingTarget(
                writer,
                targetHome,
                currentHome,
                L("Panel.Mayor.NoHome", "No low-density residence selected"),
                targetHome != Entity.Null ? m_MayorWorkplaceSystem.GetHomeCapacity(targetHome) : 0,
                targetHome != Entity.Null ? m_MayorWorkplaceSystem.GetHomeOccupantCount(targetHome) : 0);

            writer.PropertyName("mayorWorkplace");
            WriteMayorBuildingTarget(
                writer,
                targetWorkplace,
                currentWorkplace,
                L("Panel.Mayor.NoWorkplace", "No City Hall selected"),
                targetWorkplace != Entity.Null ? m_MayorWorkplaceSystem.GetWorkplaceCapacity(targetWorkplace) : 0,
                targetWorkplace != Entity.Null ? m_MayorWorkplaceSystem.GetWorkplaceOccupantCount(targetWorkplace) : 0);

            writer.PropertyName("mayorSelectedBuilding");
            writer.TypeBegin("MayorSelectedBuilding");
            writer.PropertyName("exists"); writer.Write(selectedBuilding != Entity.Null && EntityManager.Exists(selectedBuilding));
            writer.PropertyName("name"); writer.Write(GetBuildingLabel(selectedBuilding, L("Panel.Mayor.NoBuilding", "No building selected")));
            writer.PropertyName("entityLabel"); writer.Write(FormatEntity(selectedBuilding));
            writer.PropertyName("canBeHome"); writer.Write(selectedCanBeHome);
            writer.PropertyName("canBeWorkplace"); writer.Write(selectedCanBeWorkplace);
            writer.PropertyName("isHomeTarget"); writer.Write(selectedBuilding != Entity.Null && selectedBuilding == targetHome);
            writer.PropertyName("isWorkplaceTarget"); writer.Write(selectedBuilding != Entity.Null && selectedBuilding == targetWorkplace);
            writer.TypeEnd();

            writer.PropertyName("mayorHomeChoices");
            WriteMayorBuildingChoices(writer, m_MayorHomeChoices, targetHome, true);
            writer.PropertyName("mayorWorkplaceChoices");
            WriteMayorBuildingChoices(writer, m_MayorWorkplaceChoices, targetWorkplace, false);
            writer.PropertyName("mayorHomeChoicesLimited"); writer.Write(m_MayorHomeChoicesLimited);
            writer.PropertyName("mayorWorkplaceChoicesLimited"); writer.Write(m_MayorWorkplaceChoicesLimited);
        }

        private void EnsureMayorChoiceCache(
            Entity mayor,
            Entity targetHome,
            Entity targetWorkplace,
            Entity currentHome,
            Entity currentWorkplace,
            Entity selectedBuilding,
            ElectionState state)
        {
            if (m_MayorChoiceCacheValid &&
                m_UpdateSerial - m_MayorChoiceCacheUpdateSerial < kMayorChoiceCacheRefreshUpdates &&
                m_CachedChoiceMayor == mayor &&
                m_CachedChoiceTargetHome == targetHome &&
                m_CachedChoiceTargetWorkplace == targetWorkplace &&
                m_CachedChoiceCurrentHome == currentHome &&
                m_CachedChoiceCurrentWorkplace == currentWorkplace &&
                m_CachedChoiceSelectedBuilding == selectedBuilding)
            {
                return;
            }

            m_MayorHomeChoices.Clear();
            m_MayorWorkplaceChoices.Clear();
            m_MayorHomeChoicesLimited = m_MayorWorkplaceSystem.BuildMayorHomeChoices(m_MayorHomeChoices, state, currentHome, selectedBuilding);
            m_MayorWorkplaceChoicesLimited = m_MayorWorkplaceSystem.BuildMayorWorkplaceChoices(m_MayorWorkplaceChoices, state, currentWorkplace, selectedBuilding);
            m_MayorChoiceCacheValid = true;
            m_MayorChoiceCacheUpdateSerial = m_UpdateSerial;
            m_CachedChoiceMayor = mayor;
            m_CachedChoiceTargetHome = targetHome;
            m_CachedChoiceTargetWorkplace = targetWorkplace;
            m_CachedChoiceCurrentHome = currentHome;
            m_CachedChoiceCurrentWorkplace = currentWorkplace;
            m_CachedChoiceSelectedBuilding = selectedBuilding;
        }

        private void InvalidateMayorChoiceCache()
        {
            m_MayorChoiceCacheValid = false;
            m_MayorHomeChoices.Clear();
            m_MayorWorkplaceChoices.Clear();
            m_MayorHomeChoicesLimited = false;
            m_MayorWorkplaceChoicesLimited = false;
            m_CachedChoiceMayor = Entity.Null;
            m_CachedChoiceTargetHome = Entity.Null;
            m_CachedChoiceTargetWorkplace = Entity.Null;
            m_CachedChoiceCurrentHome = Entity.Null;
            m_CachedChoiceCurrentWorkplace = Entity.Null;
            m_CachedChoiceSelectedBuilding = Entity.Null;
        }

        private void WriteMayorBuildingTarget(
            IJsonWriter writer,
            Entity target,
            Entity current,
            string emptyTargetName,
            int capacity,
            int occupants)
        {
            bool targetExists = target != Entity.Null && EntityManager.Exists(target);
            bool currentExists = current != Entity.Null && EntityManager.Exists(current);

            writer.TypeBegin("MayorBuildingTarget");
            writer.PropertyName("exists"); writer.Write(targetExists);
            writer.PropertyName("name"); writer.Write(GetBuildingLabel(target, emptyTargetName));
            writer.PropertyName("entityLabel"); writer.Write(FormatEntity(target));
            writer.PropertyName("capacity"); writer.Write(capacity);
            writer.PropertyName("occupants"); writer.Write(occupants);
            writer.PropertyName("currentName"); writer.Write(GetBuildingLabel(current, L("UI.Unknown", "Unknown")));
            writer.PropertyName("currentEntityLabel"); writer.Write(FormatEntity(current));
            writer.PropertyName("currentExists"); writer.Write(currentExists);
            writer.PropertyName("atTarget"); writer.Write(targetExists && currentExists && target == current);
            writer.PropertyName("canFocus"); writer.Write(targetExists);
            writer.TypeEnd();
        }

        private void WriteMayorBuildingChoices(IJsonWriter writer, List<Entity> choices, Entity selectedTarget, bool homeChoices)
        {
            writer.ArrayBegin(choices.Count);
            for (int i = 0; i < choices.Count; i++)
            {
                Entity choice = choices[i];
                int capacity = homeChoices
                    ? m_MayorWorkplaceSystem.GetHomeCapacity(choice)
                    : m_MayorWorkplaceSystem.GetWorkplaceCapacity(choice);
                int occupants = homeChoices
                    ? m_MayorWorkplaceSystem.GetHomeOccupantCount(choice)
                    : m_MayorWorkplaceSystem.GetWorkplaceOccupantCount(choice);

                writer.TypeBegin("MayorBuildingChoice");
                writer.PropertyName("index"); writer.Write(i);
                writer.PropertyName("name"); writer.Write(GetBuildingLabel(choice, homeChoices ? L("Panel.Mayor.Residence", "Residence") : L("Panel.Mayor.CityHall", "City Hall")));
                writer.PropertyName("entityLabel"); writer.Write(FormatEntity(choice));
                writer.PropertyName("entityIndex"); writer.Write(choice.Index);
                writer.PropertyName("entityVersion"); writer.Write(choice.Version);
                writer.PropertyName("capacity"); writer.Write(capacity);
                writer.PropertyName("occupants"); writer.Write(occupants);
                writer.PropertyName("selected"); writer.Write(selectedTarget != Entity.Null && choice == selectedTarget);
                writer.TypeEnd();
            }
            writer.ArrayEnd();
        }

        private string GetBuildingLabel(Entity entity, string fallback)
        {
            if (entity == Entity.Null || !EntityManager.Exists(entity))
                return fallback;

            string label = m_NameSystem != null ? m_NameSystem.GetRenderedLabelName(entity) : string.Empty;
            if (string.IsNullOrWhiteSpace(label) || label.IndexOf("Assets.", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                if (EntityManager.HasComponent<PrefabRef>(entity))
                {
                    PrefabRef prefabRef = EntityManager.GetComponentData<PrefabRef>(entity);
                    label = m_PrefabSystem.GetPrefabName(prefabRef.m_Prefab);
                }
            }

            if (string.IsNullOrWhiteSpace(label))
                label = fallback;

            return label;
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
            string nameA = GetEntityName(state.candidateA, CandidateFallbackName(0));
            string nameB = GetEntityName(state.candidateB, CandidateFallbackName(1));
            string nameC = GetEntityName(state.candidateC, CandidateFallbackName(2));
            string nameD = GetEntityName(state.candidateD, CandidateFallbackName(3));
            ElectionPollSummary summary = ElectionPollUtility.BuildSummary(
                pollReleased ? state.pollVotesA : 0,
                pollReleased ? state.pollVotesB : 0,
                pollReleased ? state.pollVotesC : 0,
                pollReleased ? state.pollVotesD : 0,
                pollReleased ? state.pollUndecided : 0,
                state.ActiveCandidateCount,
                nameA,
                nameB,
                nameC,
                nameD);

            writer.PropertyName("poll");
            writer.TypeBegin("ElectionPoll");
            writer.PropertyName("sampleSize"); writer.Write(summary.Total);
            writer.PropertyName("votesA"); writer.Write(pollReleased ? state.pollVotesA : 0);
            writer.PropertyName("votesB"); writer.Write(pollReleased ? state.pollVotesB : 0);
            writer.PropertyName("votesC"); writer.Write(pollReleased ? state.pollVotesC : 0);
            writer.PropertyName("votesD"); writer.Write(pollReleased ? state.pollVotesD : 0);
            writer.PropertyName("undecided"); writer.Write(pollReleased ? state.pollUndecided : 0);
            writer.PropertyName("percentA"); writer.Write(pollReleased ? summary.PercentA : 0);
            writer.PropertyName("percentB"); writer.Write(pollReleased ? summary.PercentB : 0);
            writer.PropertyName("percentC"); writer.Write(pollReleased ? summary.PercentC : 0);
            writer.PropertyName("percentD"); writer.Write(pollReleased ? summary.PercentD : 0);
            writer.PropertyName("percentUndecided"); writer.Write(pollReleased ? summary.PercentUndecided : 0);
            writer.PropertyName("marginOfError"); writer.Write(pollReleased ? summary.MarginOfError : 0);
            writer.PropertyName("leaderIndex"); writer.Write(pollReleased ? summary.LeaderIndex : -1);
            writer.PropertyName("withinMargin"); writer.Write(pollReleased && summary.WithinMargin);
            writer.PropertyName("resultLabel"); writer.Write(pollReleased ? summary.Label : string.Empty);
            writer.PropertyName("resultDescription"); writer.Write(pollReleased ? summary.Description : string.Empty);
            writer.PropertyName("candidateResults");
            WritePollCandidateResults(
                writer,
                state.ActiveCandidateCount,
                summary,
                pollReleased ? state.pollVotesA : 0,
                pollReleased ? state.pollVotesB : 0,
                pollReleased ? state.pollVotesC : 0,
                pollReleased ? state.pollVotesD : 0,
                nameA,
                nameB,
                nameC,
                nameD);
            writer.PropertyName("ageGroups");
            writer.ArrayBegin(3);
            WritePollBreakdown(writer, "teens", L("Panel.Poll.Age.Teens", "Teens"), pollReleased ? state.pollTeenVotesA : 0, pollReleased ? state.pollTeenVotesB : 0, pollReleased ? state.pollTeenVotesC : 0, pollReleased ? state.pollTeenVotesD : 0, pollReleased ? state.pollTeenUndecided : 0, state.ActiveCandidateCount, nameA, nameB, nameC, nameD);
            WritePollBreakdown(writer, "adults", L("Panel.Poll.Age.Adults", "Adults"), pollReleased ? state.pollAdultVotesA : 0, pollReleased ? state.pollAdultVotesB : 0, pollReleased ? state.pollAdultVotesC : 0, pollReleased ? state.pollAdultVotesD : 0, pollReleased ? state.pollAdultUndecided : 0, state.ActiveCandidateCount, nameA, nameB, nameC, nameD);
            WritePollBreakdown(writer, "elderly", L("Panel.Poll.Age.Elderly", "Elderly"), pollReleased ? state.pollElderlyVotesA : 0, pollReleased ? state.pollElderlyVotesB : 0, pollReleased ? state.pollElderlyVotesC : 0, pollReleased ? state.pollElderlyVotesD : 0, pollReleased ? state.pollElderlyUndecided : 0, state.ActiveCandidateCount, nameA, nameB, nameC, nameD);
            writer.ArrayEnd();
            writer.PropertyName("educationGroups");
            writer.ArrayBegin(5);
            WritePollBreakdown(writer, "education0", L("Panel.Poll.Education.Uneducated", "Uneducated"), pollReleased ? state.pollEducation0VotesA : 0, pollReleased ? state.pollEducation0VotesB : 0, pollReleased ? state.pollEducation0VotesC : 0, pollReleased ? state.pollEducation0VotesD : 0, pollReleased ? state.pollEducation0Undecided : 0, state.ActiveCandidateCount, nameA, nameB, nameC, nameD);
            WritePollBreakdown(writer, "education1", L("Panel.Poll.Education.PoorlyEducated", "Poorly educated"), pollReleased ? state.pollEducation1VotesA : 0, pollReleased ? state.pollEducation1VotesB : 0, pollReleased ? state.pollEducation1VotesC : 0, pollReleased ? state.pollEducation1VotesD : 0, pollReleased ? state.pollEducation1Undecided : 0, state.ActiveCandidateCount, nameA, nameB, nameC, nameD);
            WritePollBreakdown(writer, "education2", L("Panel.Poll.Education.Educated", "Educated"), pollReleased ? state.pollEducation2VotesA : 0, pollReleased ? state.pollEducation2VotesB : 0, pollReleased ? state.pollEducation2VotesC : 0, pollReleased ? state.pollEducation2VotesD : 0, pollReleased ? state.pollEducation2Undecided : 0, state.ActiveCandidateCount, nameA, nameB, nameC, nameD);
            WritePollBreakdown(writer, "education3", L("Panel.Poll.Education.WellEducated", "Well educated"), pollReleased ? state.pollEducation3VotesA : 0, pollReleased ? state.pollEducation3VotesB : 0, pollReleased ? state.pollEducation3VotesC : 0, pollReleased ? state.pollEducation3VotesD : 0, pollReleased ? state.pollEducation3Undecided : 0, state.ActiveCandidateCount, nameA, nameB, nameC, nameD);
            WritePollBreakdown(writer, "education4", L("Panel.Poll.Education.HighlyEducated", "Highly educated"), pollReleased ? state.pollEducation4VotesA : 0, pollReleased ? state.pollEducation4VotesB : 0, pollReleased ? state.pollEducation4VotesC : 0, pollReleased ? state.pollEducation4VotesD : 0, pollReleased ? state.pollEducation4Undecided : 0, state.ActiveCandidateCount, nameA, nameB, nameC, nameD);
            writer.ArrayEnd();
            writer.PropertyName("incomeGroups");
            if (pollReleased && HasIncomePollBreakdown(state))
            {
                writer.ArrayBegin(5);
                WritePollBreakdown(writer, "income0", L("Panel.Poll.Income.Struggling", "Struggling"), state.pollIncome0VotesA, state.pollIncome0VotesB, state.pollIncome0VotesC, state.pollIncome0VotesD, state.pollIncome0Undecided, state.ActiveCandidateCount, nameA, nameB, nameC, nameD);
                WritePollBreakdown(writer, "income1", L("Panel.Poll.Income.Modest", "Modest income"), state.pollIncome1VotesA, state.pollIncome1VotesB, state.pollIncome1VotesC, state.pollIncome1VotesD, state.pollIncome1Undecided, state.ActiveCandidateCount, nameA, nameB, nameC, nameD);
                WritePollBreakdown(writer, "income2", L("Panel.Poll.Income.Middle", "Middle income"), state.pollIncome2VotesA, state.pollIncome2VotesB, state.pollIncome2VotesC, state.pollIncome2VotesD, state.pollIncome2Undecided, state.ActiveCandidateCount, nameA, nameB, nameC, nameD);
                WritePollBreakdown(writer, "income3", L("Panel.Poll.Income.Comfortable", "Comfortable"), state.pollIncome3VotesA, state.pollIncome3VotesB, state.pollIncome3VotesC, state.pollIncome3VotesD, state.pollIncome3Undecided, state.ActiveCandidateCount, nameA, nameB, nameC, nameD);
                WritePollBreakdown(writer, "income4", L("Panel.Poll.Income.Wealthy", "Wealthy"), state.pollIncome4VotesA, state.pollIncome4VotesB, state.pollIncome4VotesC, state.pollIncome4VotesD, state.pollIncome4Undecided, state.ActiveCandidateCount, nameA, nameB, nameC, nameD);
                writer.ArrayEnd();
            }
            else
            {
                writer.ArrayBegin(0);
                writer.ArrayEnd();
            }
            writer.TypeEnd();
        }

        private static void WriteEmptyPoll(IJsonWriter writer)
        {
            writer.PropertyName("poll");
            writer.TypeBegin("ElectionPoll");
            writer.PropertyName("sampleSize"); writer.Write(0);
            writer.PropertyName("votesA"); writer.Write(0);
            writer.PropertyName("votesB"); writer.Write(0);
            writer.PropertyName("votesC"); writer.Write(0);
            writer.PropertyName("votesD"); writer.Write(0);
            writer.PropertyName("undecided"); writer.Write(0);
            writer.PropertyName("percentA"); writer.Write(0);
            writer.PropertyName("percentB"); writer.Write(0);
            writer.PropertyName("percentC"); writer.Write(0);
            writer.PropertyName("percentD"); writer.Write(0);
            writer.PropertyName("percentUndecided"); writer.Write(0);
            writer.PropertyName("marginOfError"); writer.Write(0);
            writer.PropertyName("leaderIndex"); writer.Write(-1);
            writer.PropertyName("withinMargin"); writer.Write(false);
            writer.PropertyName("resultLabel"); writer.Write(string.Empty);
            writer.PropertyName("resultDescription"); writer.Write(string.Empty);
            writer.PropertyName("candidateResults"); writer.ArrayBegin(0); writer.ArrayEnd();
            writer.PropertyName("ageGroups"); writer.ArrayBegin(0); writer.ArrayEnd();
            writer.PropertyName("educationGroups"); writer.ArrayBegin(0); writer.ArrayEnd();
            writer.PropertyName("incomeGroups"); writer.ArrayBegin(0); writer.ArrayEnd();
            writer.TypeEnd();
        }

        private static void WritePollBreakdown(IJsonWriter writer, string key, string label, int votesA, int votesB, int votesC, int votesD, int undecided, int candidateCount, string nameA, string nameB, string nameC, string nameD)
        {
            ElectionPollSummary summary = ElectionPollUtility.BuildSummary(votesA, votesB, votesC, votesD, undecided, candidateCount, nameA, nameB, nameC, nameD);

            writer.TypeBegin("ElectionPollBreakdown");
            writer.PropertyName("key"); writer.Write(key);
            writer.PropertyName("label"); writer.Write(label);
            writer.PropertyName("sampleSize"); writer.Write(summary.Total);
            writer.PropertyName("votesA"); writer.Write(votesA);
            writer.PropertyName("votesB"); writer.Write(votesB);
            writer.PropertyName("votesC"); writer.Write(votesC);
            writer.PropertyName("votesD"); writer.Write(votesD);
            writer.PropertyName("undecided"); writer.Write(undecided);
            writer.PropertyName("percentA"); writer.Write(summary.PercentA);
            writer.PropertyName("percentB"); writer.Write(summary.PercentB);
            writer.PropertyName("percentC"); writer.Write(summary.PercentC);
            writer.PropertyName("percentD"); writer.Write(summary.PercentD);
            writer.PropertyName("percentUndecided"); writer.Write(summary.PercentUndecided);
            writer.PropertyName("marginOfError"); writer.Write(summary.MarginOfError);
            writer.PropertyName("leaderIndex"); writer.Write(summary.LeaderIndex);
            writer.PropertyName("withinMargin"); writer.Write(summary.WithinMargin);
            writer.PropertyName("resultLabel"); writer.Write(summary.Label);
            writer.PropertyName("resultDescription"); writer.Write(summary.Description);
            writer.PropertyName("candidateResults");
            WritePollCandidateResults(writer, candidateCount, summary, votesA, votesB, votesC, votesD, nameA, nameB, nameC, nameD);
            writer.TypeEnd();
        }

        private static void WritePollCandidateResults(IJsonWriter writer, int candidateCount, ElectionPollSummary summary, int votesA, int votesB, int votesC, int votesD, string nameA, string nameB, string nameC, string nameD)
        {
            writer.ArrayBegin(candidateCount);
            for (int i = 0; i < candidateCount; i++)
            {
                writer.TypeBegin("ElectionPollCandidateResult");
                writer.PropertyName("index"); writer.Write(i);
                writer.PropertyName("name"); writer.Write(GetPollCandidateName(i, nameA, nameB, nameC, nameD));
                writer.PropertyName("votes"); writer.Write(GetPollCandidateVotes(i, votesA, votesB, votesC, votesD));
                writer.PropertyName("percent"); writer.Write(GetPollCandidatePercent(summary, i));
                writer.TypeEnd();
            }
            writer.ArrayEnd();
        }

        private static string GetPollCandidateName(int index, string nameA, string nameB, string nameC, string nameD)
        {
            switch (index)
            {
                case 0:
                    return nameA;
                case 1:
                    return nameB;
                case 2:
                    return nameC;
                case 3:
                    return nameD;
                default:
                    return CandidateFallbackName(index);
            }
        }

        private static int GetPollCandidateVotes(int index, int votesA, int votesB, int votesC, int votesD)
        {
            switch (index)
            {
                case 0:
                    return votesA;
                case 1:
                    return votesB;
                case 2:
                    return votesC;
                case 3:
                    return votesD;
                default:
                    return 0;
            }
        }

        private static int GetPollCandidatePercent(ElectionPollSummary summary, int index)
        {
            switch (index)
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

        private string GetCandidateBio(ElectionState state, int candidateIndex)
        {
            Entity candidate = state.GetCandidate(candidateIndex);
            int age = state.GetCandidateAge(candidateIndex);
            int education = state.GetCandidateEducation(candidateIndex);
            int workType = state.GetCandidateWorkType(candidateIndex);
            int wealth = state.GetCandidateWealth(candidateIndex);
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
                case ElectionSupportProgramType.LowIncomeVoterOutreach:
                    return state.lowIncomeTurnoutBonusPercent > 0;
                case ElectionSupportProgramType.TransitVouchers:
                    return state.transitVoucherTurnoutBonusPercent > 0;
                case ElectionSupportProgramType.CivicForums:
                    return state.civicForumTurnoutBonusPercent > 0;
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
                case ElectionSupportProgramType.LowIncomeVoterOutreach:
                    return state.lowIncomeTurnoutBonusPercent;
                case ElectionSupportProgramType.TransitVouchers:
                    return state.transitVoucherTurnoutBonusPercent;
                case ElectionSupportProgramType.CivicForums:
                    return state.civicForumTurnoutBonusPercent;
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
                return L("Panel.Support.Disabled.ElectionDay", "Civic programs are unavailable on election day.");

            if (supportProgramUsedToday)
                return LF("Panel.Support.Disabled.UsedToday", "Only one civic program can be funded per day. Today's program is {0}.", GetSupportProgramLabel(state.supportProgramIdToday));

            if (type == ElectionSupportProgramType.ElectionDayHoliday && state.electionDayHolidayScheduled)
                return L("Panel.Support.Disabled.HolidayScheduled", "Election day is already scheduled as a holiday.");

            if (!supportProgramsOpen)
                return L("Panel.Support.Disabled.Closed", "Civic programs are available before election day once candidates are selected.");

            return L("Panel.Support.Disabled.Generic", "Civic program unavailable right now.");
        }

        private static string GetSupportProgramLabel(int programIndex)
        {
            return ElectionSupportPrograms.TryGet(programIndex, out ElectionSupportProgramDefinition program)
                ? program.Title
                : L("Panel.Support.Fallback", "a civic program");
        }

        private static void WriteLegislationPanel(IJsonWriter writer, ElectionState state, bool legislationOpen, bool electionDay, bool legislationActionUsedToday)
        {
            int campaignActionCost = state.campaignBribeAmount > 0
                ? state.campaignBribeAmount
                : ElectionLifecycleSystem.BribeAmount;
            int cost = ElectionLegislation.GetCost(campaignActionCost);
            int chancePercent = ElectionLegislation.GetActionChancePercent(state);
            writer.PropertyName("legislationOpen"); writer.Write(legislationOpen);
            writer.PropertyName("legislationActionUsedToday"); writer.Write(legislationActionUsedToday);
            writer.PropertyName("legislationCost"); writer.Write(cost);
            writer.PropertyName("legislationPassChancePercent"); writer.Write(chancePercent);
            writer.PropertyName("legislation");
            writer.ArrayBegin(ElectionLegislation.Count);

            for (int i = 0; i < ElectionLegislation.Count; i++)
            {
                if (!ElectionLegislation.TryGet(i, out ElectionLegislationDefinition legislation))
                    continue;

                bool active = state.HasLegislation(legislation.Type);
                writer.TypeBegin("ElectionLegislation");
                writer.PropertyName("index"); writer.Write(legislation.Index);
                writer.PropertyName("title"); writer.Write(legislation.Title);
                writer.PropertyName("description"); writer.Write(legislation.Description);
                writer.PropertyName("tooltip"); writer.Write(legislation.Tooltip);
                writer.PropertyName("cost"); writer.Write(cost);
                writer.PropertyName("active"); writer.Write(active);
                writer.PropertyName("passChancePercent"); writer.Write(chancePercent);
                writer.PropertyName("canPass"); writer.Write(legislationOpen && !legislationActionUsedToday && !active);
                writer.PropertyName("canRepeal"); writer.Write(legislationOpen && !legislationActionUsedToday && active);
                writer.PropertyName("disabledReason"); writer.Write(GetLegislationDisabledReason(legislationOpen, electionDay, legislationActionUsedToday));
                writer.TypeEnd();
            }

            writer.ArrayEnd();
        }

        private static string GetLegislationDisabledReason(bool legislationOpen, bool electionDay, bool legislationActionUsedToday)
        {
            if (electionDay)
                return L("Panel.Legislation.Disabled.ElectionDay", "Election legislation is unavailable on election day.");

            if (legislationActionUsedToday)
                return L("Panel.Legislation.Disabled.UsedToday", "Only one election legislation action can be attempted per day.");

            if (!legislationOpen)
                return L("Panel.Legislation.Disabled.Closed", "Election legislation can be changed before election day once candidates are selected.");

            return string.Empty;
        }

        private static bool HasPollResults(ElectionState state)
        {
            return state.pollVotesA + state.pollVotesB + state.pollVotesC + state.pollVotesD + state.pollUndecided > 0 ||
                   state.stage == ElectionCampaignStage.PollReleased ||
                   state.stage == ElectionCampaignStage.Voting;
        }

        private static bool HasIncomePollBreakdown(ElectionState state)
        {
            return state.pollIncome0VotesA + state.pollIncome0VotesB + state.pollIncome0VotesC + state.pollIncome0VotesD + state.pollIncome0Undecided +
                   state.pollIncome1VotesA + state.pollIncome1VotesB + state.pollIncome1VotesC + state.pollIncome1VotesD + state.pollIncome1Undecided +
                   state.pollIncome2VotesA + state.pollIncome2VotesB + state.pollIncome2VotesC + state.pollIncome2VotesD + state.pollIncome2Undecided +
                   state.pollIncome3VotesA + state.pollIncome3VotesB + state.pollIncome3VotesC + state.pollIncome3VotesD + state.pollIncome3Undecided +
                   state.pollIncome4VotesA + state.pollIncome4VotesB + state.pollIncome4VotesC + state.pollIncome4VotesD + state.pollIncome4Undecided > 0;
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

        private static string GetStageLabel(ElectionState state, bool hasCandidates)
        {
            if (!hasCandidates)
                return L("Panel.Stage.NoActiveCampaign", "No active campaign");

            switch (state.stage)
            {
                case ElectionCampaignStage.CandidatesSelected:
                    return state.runoffActive ? L("Panel.Stage.RunoffCandidatesSelected", "Runoff candidates selected") : L("Panel.Stage.CandidatesSelected", "Candidates selected");
                case ElectionCampaignStage.PollReleased:
                    return state.runoffActive ? L("Panel.Stage.RunoffPollReleased", "Runoff poll released") : L("Panel.Stage.PollReleased", "Poll released");
                case ElectionCampaignStage.Voting:
                    return state.runoffActive ? L("Panel.Stage.RunoffElectionDay", "Runoff election day") : L("Panel.Stage.ElectionDay", "Election day");
                default:
                    if (state.pendingMayor != Entity.Null && !state.pendingMayorInaugurated)
                        return L("Panel.Stage.TransitionPeriod", "Transition period");
                    return L("Panel.Stage.MayorTermActive", "Mayor term active");
            }
        }

        private string GetCycleLabel(ElectionState state)
        {
            if (state.runoffActive)
                return L("Panel.Cycle.RunoffRace", "Runoff mayoral race");

            if (state.HasCandidates)
                return state.runoffEnabledForCycle ? L("Panel.Cycle.RegularRunoffRace", "Regular mayoral race with runoff voting") :
                    state.acceleratedCycle ? L("Panel.Cycle.AcceleratedRace", "Accelerated mayoral race") : L("Panel.Cycle.RegularRace", "Regular mayoral race");

            if (state.pendingMayor != Entity.Null && !state.pendingMayorInaugurated)
            {
                string pendingMayorName = GetEntityName(state.pendingMayor, L("Panel.MayorElect.Article", "The mayor-elect"));
                string inaugurationDate = state.pendingMayorTermYear > 0 ? ElectionUtility.FormatDate(state.pendingMayorTermYear, 1, 1) : L("UI.Date.January1", "January 1");
                string currentMayorName = GetEntityName(state.mayor, string.Empty);
                return string.IsNullOrWhiteSpace(currentMayorName)
                    ? LF("Panel.Cycle.PendingMayorNoCurrent", "{0} takes office on {1}.", pendingMayorName, inaugurationDate)
                    : LF("Panel.Cycle.PendingMayorWithCurrent", "{0} takes office on {1}. {2} is serving as mayor until then.", pendingMayorName, inaugurationDate, currentMayorName);
            }

            string mayorName = GetEntityName(state.mayor, string.Empty);
            if (!string.IsNullOrWhiteSpace(mayorName))
                return LF("Panel.Cycle.MayorServing", "{0} is serving as mayor.", mayorName);

            return L("Panel.Cycle.NoActiveCampaignSentence", "No active campaign.");
        }

        private static string GetDonationTierLabel(int index)
        {
            return L("Panel.Donation.CampaignSupport", "Campaign support");
        }

        private static int GetCampaignBribeAmount(ElectionState state)
        {
            int baseAmount = state.campaignBribeAmount > 0 ? state.campaignBribeAmount : ElectionLifecycleSystem.BribeAmount;
            return ElectionCandidateTags.GetMayorActionCost(state.mayorTagId, baseAmount);
        }

        private string GetEntityName(Entity entity, string fallback)
        {
            return ElectionNameUtility.GetCitizenFullName(m_NameSystem, EntityManager, entity, fallback);
        }

        private static string FormatEntity(Entity entity)
        {
            if (entity == Entity.Null)
                return "Entity.Null";

            return $"Entity({entity.Index}:{entity.Version})";
        }

        private static string CandidateFallbackName(int index)
        {
            string key = index >= 0 && index <= 3
                ? $"Panel.Candidate.Fallback.{index}"
                : "Panel.Candidate.Fallback.Generic";
            return L(key, ElectionState.GetCandidateFallbackName(index));
        }

        private static string L(string key, string fallback)
        {
            return ElectionLocalization.Translate(key, fallback);
        }

        private static string LF(string key, string fallback, params object[] args)
        {
            return ElectionLocalization.Format(key, fallback, args);
        }
    }
}
