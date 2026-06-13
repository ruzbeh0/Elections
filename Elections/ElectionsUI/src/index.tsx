import { type BalloonDirection, type Color, type FocusKey } from "cs2/bindings";
import { type ModRegistrar, type ModuleRegistry } from "cs2/modding";
import { bindValue, trigger, useValue } from "cs2/api";
import { Button, Dropdown, DropdownItem, DropdownToggle, Icon, Tooltip } from "cs2/ui";
import { type ReactElement, useEffect, useMemo, useState } from "react";
import engine from "cohtml/cohtml";

import icon from "images/elections.svg";
import mod from "../mod.json";
import styles from "./mods/ElectionsMenu.module.scss";

const localePrefix = "Elections.";

function t(key: string, fallback: string, args?: Record<string, string | number>): string {
  const id = `${localePrefix}${key}`;
  let value = fallback;

  try {
    const translated = engine.translate(id);
    if (translated && translated !== id) {
      value = translated;
    }
  } catch {
    value = fallback;
  }

  if (args) {
    Object.entries(args).forEach(([name, raw]) => {
      value = value.replace(new RegExp(`\\{${name}\\}`, "g"), String(raw));
    });
  }

  return value;
}

type DonationTier = {
  index: number;
  amount: number;
  bonusPercent: number;
  label: string;
};

type SupportProgram = {
  index: number;
  title: string;
  description: string;
  tooltip: string;
  cost: number;
  bonusPercent: number;
  currentBonusPercent: number;
  active: boolean;
  canRun: boolean;
  disabledReason: string;
};

type Legislation = {
  index: number;
  title: string;
  description: string;
  tooltip: string;
  cost: number;
  active: boolean;
  passChancePercent: number;
  canPass: boolean;
  canRepeal: boolean;
  disabledReason: string;
};

type PlatformImpact = {
  value: string;
  label: string;
  positive: boolean;
};

type Candidate = {
  index: number;
  exists: boolean;
  name: string;
  partyIndex: number;
  partyName: string;
  partyColor: string;
  partyReputation: number;
  portrait: string;
  canFocus: boolean;
  bio: string;
  tagName: string;
  tagDescription: string;
  tagTone: string;
  effectName: string;
  effectDescription: string;
  platformImpacts: PlatformImpact[];
  donationAmount: number;
  donationCost: number;
  donationBonusPercent: number;
  donated: boolean;
};

type PartyTag = {
  slot: number;
  id: number;
  name: string;
  description: string;
  value: number;
  tone: string;
};

type Party = {
  index: number;
  name: string;
  color: string;
  reputation: number;
  consecutiveTerms: number;
  wins: number;
  lastTagReplacementYear: number;
  canReplaceTag: boolean;
  replacementDisabledReason: string;
  tags: PartyTag[];
};

type ColorFieldProps = {
  focusKey?: FocusKey;
  disabled?: boolean;
  value?: Color;
  className?: string;
  alpha?: boolean;
  popupDirection?: BalloonDirection;
  onChange?: (color: Color) => void;
};

type ColorFieldComponent = (props: ColorFieldProps) => ReactElement;

const colorFieldPath = "game-ui/common/input/color-picker/color-field/color-field.tsx";
let moduleRegistryForVanillaComponents: ModuleRegistry | undefined;
let resolvedColorField: ColorFieldComponent | undefined;

type PollCandidateResult = {
  index: number;
  name: string;
  votes: number;
  percent: number;
};

type PollResult = {
  sampleSize: number;
  votesA: number;
  votesB: number;
  votesC: number;
  votesD: number;
  undecided: number;
  percentA: number;
  percentB: number;
  percentC: number;
  percentD: number;
  percentUndecided: number;
  marginOfError: number;
  leaderIndex: number;
  withinMargin: boolean;
  resultLabel: string;
  resultDescription: string;
  candidateResults: PollCandidateResult[];
};

type PollBreakdown = PollResult & {
  key: string;
  label: string;
};

type Poll = PollResult & {
  ageGroups: PollBreakdown[];
  educationGroups: PollBreakdown[];
  incomeGroups: PollBreakdown[];
};

type MayorBuildingTarget = {
  exists: boolean;
  name: string;
  entityLabel: string;
  capacity: number;
  occupants: number;
  currentName: string;
  currentEntityLabel: string;
  currentExists: boolean;
  atTarget: boolean;
  canFocus: boolean;
};

type MayorBuildingChoice = {
  index: number;
  name: string;
  entityLabel: string;
  entityIndex: number;
  entityVersion: number;
  capacity: number;
  occupants: number;
  selected: boolean;
};

type MayorSelectedBuilding = {
  exists: boolean;
  name: string;
  entityLabel: string;
  canBeHome: boolean;
  canBeWorkplace: boolean;
  isHomeTarget: boolean;
  isWorkplaceTarget: boolean;
};

type ElectionPanel = {
  enabled: boolean;
  partiesEnabled: boolean;
  hasState: boolean;
  waitingForPopulation: boolean;
  currentPopulation: number;
  minimumPopulation: number;
  populationReady: boolean;
  currentDate: string;
  stage: string;
  stageLabel: string;
  cycleLabel: string;
  runoffEnabledForCycle: boolean;
  runoffActive: boolean;
  runoffOriginalCandidateCount: number;
  pendingMayorName: string;
  pendingMayorInaugurationDate: string;
  pollReleased: boolean;
  donationsOpen: boolean;
  canDonate: boolean;
  donationUsedToday: boolean;
  bribesOpen: boolean;
  canBribe: boolean;
  canEndorse: boolean;
  endorsementUsed: boolean;
  endorsedCandidateIndex: number;
  canTamper: boolean;
  voteTamperingScheduled: boolean;
  voteTamperingCandidateIndex: number;
  bribeUsedToday: boolean;
  bribeBlocked: boolean;
  bribeMeetingPending: boolean;
  bribeCost: number;
  cashAssistanceTurnoutBonusPercent: number;
  supportProgramsOpen: boolean;
  supportProgramUsedToday: boolean;
  supportProgramUsedTodayLabel: string;
  supportProgramCost: number;
  supportPrograms: SupportProgram[];
  legislationOpen: boolean;
  legislationActionUsedToday: boolean;
  legislationCost: number;
  legislationPassChancePercent: number;
  legislation: Legislation[];
  pollDate: string;
  electionDate: string;
  votingStartTime: string;
  votingEndTime: string;
  resultsTime: string;
  poll: Poll;
  donationTiers: DonationTier[];
  candidates: Candidate[];
  candidateA: Candidate;
  candidateB: Candidate;
  parties: Party[];
  partyTags: PartyTag[];
  mayorName: string;
  mayorPartyIndex: number;
  mayorPartyName: string;
  mayorPartyColor: string;
  mayorPartyReputation: number;
  mayorPortrait: string;
  mayorCanFocus: boolean;
  mayorEffectName: string;
  mayorEffectDescription: string;
  mayorTagName: string;
  mayorTagDescription: string;
  mayorTagTone: string;
  mayorPlatformImpacts: PlatformImpact[];
  mayorTemporary: boolean;
  mayorHome: MayorBuildingTarget;
  mayorWorkplace: MayorBuildingTarget;
  mayorHomeChoices: MayorBuildingChoice[];
  mayorWorkplaceChoices: MayorBuildingChoice[];
  mayorHomeChoicesLimited: boolean;
  mayorWorkplaceChoicesLimited: boolean;
  mayorSelectedBuilding: MayorSelectedBuilding;
};

type PanelSection = "votingSites" | "mayor" | "residence" | "schedule" | "programs" | "legislation" | "candidates" | "parties";
type CandidateTargetActionKind = "bribe" | "endorse" | "tamper";
type MayorActionKind = CandidateTargetActionKind | "cashAssistance";
type MayorCampaignAction = {
  kind: MayorActionKind;
  title: string;
  description: string;
  buttonLabel: string;
  targetButtonLabel?: string;
  status: string;
  disabled: boolean;
  requiresCandidate: boolean;
};

const emptyCandidate = (index: number, name: string): Candidate => ({
  index,
  exists: false,
  name,
  partyIndex: -1,
  partyName: "",
  partyColor: "",
  partyReputation: 0,
  portrait: "",
  canFocus: false,
  bio: "",
  tagName: "",
  tagDescription: "",
  tagTone: "Neutral",
  effectName: t("Panel.Candidate.NoPlatform", "No platform"),
  effectDescription: t("Panel.Candidate.EmptyDescription", "No candidate has been selected yet."),
  platformImpacts: [],
  donationAmount: 0,
  donationCost: 0,
  donationBonusPercent: 0,
  donated: false,
});

const emptyPartyTag = (slot = -1): PartyTag => ({
  slot,
  id: 0,
  name: "",
  description: "",
  value: 0,
  tone: "Neutral",
});

const emptyParty = (index: number): Party => ({
  index,
  name: t("UI.Party.FallbackNumber", "Party {number}", { number: index + 1 }),
  color: "",
  reputation: 50,
  consecutiveTerms: 0,
  wins: 0,
  lastTagReplacementYear: 0,
  canReplaceTag: false,
  replacementDisabledReason: "",
  tags: [],
});

const emptyMayorBuildingTarget = (name: string): MayorBuildingTarget => ({
  exists: false,
  name,
  entityLabel: "Entity.Null",
  capacity: 0,
  occupants: 0,
  currentName: t("UI.Unknown", "Unknown"),
  currentEntityLabel: "Entity.Null",
  currentExists: false,
  atTarget: false,
  canFocus: false,
});

const emptyMayorSelectedBuilding: MayorSelectedBuilding = {
  exists: false,
  name: t("Panel.Mayor.NoBuilding", "No building selected"),
  entityLabel: "Entity.Null",
  canBeHome: false,
  canBeWorkplace: false,
  isHomeTarget: false,
  isWorkplaceTarget: false,
};

const defaultPanel: ElectionPanel = {
  enabled: true,
  partiesEnabled: false,
  hasState: false,
  waitingForPopulation: false,
  currentPopulation: 0,
  minimumPopulation: 1500,
  populationReady: true,
  currentDate: "",
  stage: "None",
  stageLabel: t("Panel.Stage.NoElectionData", "No election data"),
  cycleLabel: t("Panel.Cycle.NotInitialized", "The election system has not initialized yet."),
  runoffEnabledForCycle: false,
  runoffActive: false,
  runoffOriginalCandidateCount: 0,
  pendingMayorName: "",
  pendingMayorInaugurationDate: "",
  pollReleased: false,
  donationsOpen: false,
  canDonate: false,
  donationUsedToday: false,
  bribesOpen: false,
  canBribe: false,
  canEndorse: false,
  endorsementUsed: false,
  endorsedCandidateIndex: -1,
  canTamper: false,
  voteTamperingScheduled: false,
  voteTamperingCandidateIndex: -1,
  bribeUsedToday: false,
  bribeBlocked: false,
  bribeMeetingPending: false,
  bribeCost: 5000000,
  cashAssistanceTurnoutBonusPercent: 0,
  supportProgramsOpen: false,
  supportProgramUsedToday: false,
  supportProgramUsedTodayLabel: "",
  supportProgramCost: 500000,
  supportPrograms: [],
  legislationOpen: false,
  legislationActionUsedToday: false,
  legislationCost: 2500000,
  legislationPassChancePercent: 50,
  legislation: [],
  pollDate: "",
  electionDate: "",
  votingStartTime: "08:00",
  votingEndTime: "17:00",
  resultsTime: "20:00",
  poll: {
    sampleSize: 0,
    votesA: 0,
    votesB: 0,
    votesC: 0,
    votesD: 0,
    undecided: 0,
    percentA: 0,
    percentB: 0,
    percentC: 0,
    percentD: 0,
    percentUndecided: 0,
    marginOfError: 0,
    leaderIndex: -1,
    withinMargin: false,
    resultLabel: "",
    resultDescription: "",
    candidateResults: [],
    ageGroups: [],
    educationGroups: [],
    incomeGroups: [],
  },
  donationTiers: [],
  candidates: [],
  candidateA: emptyCandidate(0, getCandidateFallbackName(0)),
  candidateB: emptyCandidate(1, getCandidateFallbackName(1)),
  parties: [],
  partyTags: [],
  mayorName: "",
  mayorPartyIndex: -1,
  mayorPartyName: "",
  mayorPartyColor: "",
  mayorPartyReputation: 0,
  mayorPortrait: "",
  mayorCanFocus: false,
  mayorEffectName: "",
  mayorEffectDescription: "",
  mayorTagName: "",
  mayorTagDescription: "",
  mayorTagTone: "Neutral",
  mayorPlatformImpacts: [],
  mayorTemporary: false,
  mayorHome: emptyMayorBuildingTarget(t("Panel.Mayor.NoHome", "No low-density residence selected")),
  mayorWorkplace: emptyMayorBuildingTarget(t("Panel.Mayor.NoWorkplace", "No City Hall selected")),
  mayorHomeChoices: [],
  mayorWorkplaceChoices: [],
  mayorHomeChoicesLimited: false,
  mayorWorkplaceChoicesLimited: false,
  mayorSelectedBuilding: emptyMayorSelectedBuilding,
};

const panel$ = bindValue<ElectionPanel>(mod.id, "panel", defaultPanel);
const useUniversalModMenu$ = bindValue<boolean>(
  mod.id,
  "useUniversalModMenu",
  false
);
const showVotingLocations$ = bindValue<boolean>(
  mod.id,
  "showVotingLocations",
  false
);

const candidateColors = ["#b16cff", "#62d26f", "#d8a720", "#ff6fb3"];
const candidateShadowColors = [
  "rgba(177, 108, 255, 0.18)",
  "rgba(98, 210, 111, 0.16)",
  "rgba(216, 167, 32, 0.18)",
  "rgba(255, 111, 179, 0.18)",
];
const undecidedColor = "#aeb8c8";

type ElectionsMenuState = {
  open: boolean;
  activeSection: PanelSection;
};

const defaultMenuState: ElectionsMenuState = {
  open: false,
  activeSection: "votingSites",
};

let menuState = defaultMenuState;
const menuStateListeners = new Set<() => void>();

function subscribeToMenuState(listener: () => void): () => void {
  menuStateListeners.add(listener);
  return () => menuStateListeners.delete(listener);
}

function getMenuStateSnapshot(): ElectionsMenuState {
  return menuState;
}

function setMenuState(update: Partial<ElectionsMenuState>): void {
  menuState = {
    ...menuState,
    ...update,
  };

  menuStateListeners.forEach((listener) => listener());
}

function useElectionsMenuState(): ElectionsMenuState {
  const [snapshot, setSnapshot] = useState(getMenuStateSnapshot);

  useEffect(() => subscribeToMenuState(() => {
    setSnapshot(getMenuStateSnapshot());
  }), []);

  return snapshot;
}

function hasActiveCandidateField(panel: ElectionPanel): boolean {
  const candidates = getActiveCandidates(panel);
  return panel.enabled &&
    !panel.waitingForPopulation &&
    candidates.length >= 2 &&
    (panel.stage === "CandidatesSelected" || panel.stage === "PollReleased" || panel.stage === "Voting");
}

function getCandidateFallbackName(index: number): string {
  switch (index) {
    case 0:
      return t("Panel.Candidate.Fallback.0", "Candidate A");
    case 1:
      return t("Panel.Candidate.Fallback.1", "Candidate B");
    case 2:
      return t("Panel.Candidate.Fallback.2", "Candidate C");
    case 3:
      return t("Panel.Candidate.Fallback.3", "Candidate D");
    default:
      return t("Panel.Candidate.Fallback.Generic", "Candidate");
  }
}

function normalizeCandidate(candidate: Partial<Candidate> | undefined | null, index: number): Candidate {
  const rawIndex = candidate?.index;
  const candidateIndex = typeof rawIndex === "number" ? rawIndex : index;
  const rawPlatformImpacts = candidate?.platformImpacts;
  const platformImpacts = Array.isArray(rawPlatformImpacts) ? rawPlatformImpacts : [];
  return {
    ...emptyCandidate(candidateIndex, getCandidateFallbackName(candidateIndex)),
    ...(candidate ?? {}),
    index: candidateIndex,
    platformImpacts,
  };
}

function normalizePartyTag(tag: Partial<PartyTag> | undefined | null, slot: number): PartyTag {
  return {
    ...emptyPartyTag(slot),
    ...(tag ?? {}),
    slot: typeof tag?.slot === "number" ? tag.slot : slot,
  };
}

function normalizeParty(party: Partial<Party> | undefined | null, index: number): Party {
  const rawTags = party?.tags;
  return {
    ...emptyParty(index),
    ...(party ?? {}),
    index: typeof party?.index === "number" ? party.index : index,
    tags: Array.isArray(rawTags) ? rawTags.map((tag, slot) => normalizePartyTag(tag, slot)) : [],
  };
}

function getActiveCandidates(panel: ElectionPanel): Candidate[] {
  const listedCandidates = Array.isArray(panel.candidates)
    ? panel.candidates.map((candidate, index) => normalizeCandidate(candidate, index))
    : [];

  if (listedCandidates.some((candidate) => candidate.exists)) {
    return listedCandidates.filter((candidate) => candidate.exists);
  }

  return [
    normalizeCandidate(panel.candidateA, 0),
    normalizeCandidate(panel.candidateB, 1),
  ].filter((candidate) => candidate.exists);
}

function getCandidateRows(candidates: Candidate[], rowSize: number): Candidate[][] {
  const rows: Candidate[][] = [];
  const safeRowSize = Math.max(1, rowSize);

  for (let index = 0; index < candidates.length; index += safeRowSize) {
    rows.push(candidates.slice(index, index + safeRowSize));
  }

  return rows;
}

function getCandidateNameByIndex(candidates: Candidate[], index: number): string {
  return candidates.find((candidate) => candidate.index === index)?.name || t("UI.Candidate.GenericArticle", "a candidate");
}

function getCandidateColor(index: number): string {
  return candidateColors[Math.abs(index) % candidateColors.length];
}

function getCandidateDisplayColor(candidate: Candidate): string {
  return normalizeHexColor(candidate.partyColor) || getCandidateColor(candidate.index);
}

function getCandidateBorderColor(candidate: Candidate): string {
  return getCandidateDisplayColor(candidate);
}

function getCandidateShadowColor(candidate: Candidate): string {
  return hexToRgba(getCandidateDisplayColor(candidate), 0.18) || candidateShadowColors[Math.abs(candidate.index) % candidateShadowColors.length];
}

function normalizeHexColor(color: string | undefined | null): string {
  const raw = (color ?? "").trim();
  const match = raw.match(/^#?[0-9a-fA-F]{6}$/);
  if (!match) {
    return "";
  }

  const value = raw.startsWith("#") ? raw.slice(1) : raw;
  return `#${value.toUpperCase()}`;
}

function hexToRgba(color: string, alpha: number): string {
  const normalized = normalizeHexColor(color);
  if (!normalized) {
    return "";
  }

  const value = normalized.slice(1);
  const red = Number.parseInt(value.slice(0, 2), 16);
  const green = Number.parseInt(value.slice(2, 4), 16);
  const blue = Number.parseInt(value.slice(4, 6), 16);
  return `rgba(${red}, ${green}, ${blue}, ${Math.max(0, Math.min(1, alpha))})`;
}

function hexToColor(color: string): Color {
  const normalized = normalizeHexColor(color) || candidateColors[0];
  const value = normalized.slice(1);
  return {
    r: Number.parseInt(value.slice(0, 2), 16) / 255,
    g: Number.parseInt(value.slice(2, 4), 16) / 255,
    b: Number.parseInt(value.slice(4, 6), 16) / 255,
    a: 1,
  };
}

function colorToHex(color: Color | undefined | null): string {
  const toHex = (value: number | undefined) => {
    const channel = Number.isFinite(value) ? value as number : 0;
    return Math.round(Math.max(0, Math.min(1, channel)) * 255)
      .toString(16)
      .padStart(2, "0");
  };

  return `#${toHex(color?.r)}${toHex(color?.g)}${toHex(color?.b)}`.toUpperCase();
}

function resolveVanillaColorField(moduleRegistry?: ModuleRegistry): ColorFieldComponent | undefined {
  if (!resolvedColorField) {
    const registry = moduleRegistry ?? moduleRegistryForVanillaComponents;
    resolvedColorField = registry?.registry.get(colorFieldPath)?.ColorField as ColorFieldComponent | undefined;
  }

  return resolvedColorField;
}

export const register: ModRegistrar = (moduleRegistry) => {
  moduleRegistryForVanillaComponents = moduleRegistry;
  resolveVanillaColorField(moduleRegistry);
  moduleRegistry.append("GameTopLeft", ElectionsTopLeft);
  moduleRegistry.append("UniversalModMenu", ElectionsUniversalModMenu);
};

function ElectionsTopLeft(): ReactElement {
  const useUniversalModMenu = useValue(useUniversalModMenu$);

  return (
    <>
      {!useUniversalModMenu && (
        <div className={styles.root}>
          <ElectionsMenuButton />
        </div>
      )}
      <ElectionsDetachedPanelHost />
    </>
  );
}

function ElectionsUniversalModMenu(): ReactElement {
  const useUniversalModMenu = useValue(useUniversalModMenu$);

  return (
    <>
      {useUniversalModMenu && (
        <div className={styles.root}>
          <ElectionsMenuButton />
        </div>
      )}
    </>
  );
}

function ElectionsDetachedPanelHost(): ReactElement {
  return (
    <div className={`${styles.root} ${styles.detachedRoot}`}>
      <ElectionsMenuPanel />
    </div>
  );
}

function ElectionsMenuButton(): ReactElement {
  const { open } = useElectionsMenuState();

  return (
    <Tooltip tooltip={t("UI.OpenPanel.Tooltip", "Open the Elections panel. Shows the current mayor, active race, poll status, and campaign donation controls before election day.")}>
      <Button
        variant="floating"
        selected={open}
        aria-label={t("UI.OpenPanel.Aria", "Open Elections")}
        onSelect={() => {
          if (open) {
            trigger(mod.id, "setShowVotingLocations", false);
          }
          setMenuState({ open: !open });
        }}
      >
        <Icon src={icon} tinted={false} className={styles.icon} />
      </Button>
    </Tooltip>
  );
}

function ElectionsMenuPanel(): ReactElement | null {
  const { open, activeSection } = useElectionsMenuState();
  const panel = normalizePanel(useValue(panel$));
  const showVotingLocations = useValue(showVotingLocations$);
  const [partyEditorOpen, setPartyEditorOpen] = useState(false);
  const candidates = useMemo(
    () => getActiveCandidates(panel),
    [panel.candidates, panel.candidateA, panel.candidateB]
  );

  useEffect(() => {
    if ((activeSection !== "parties" || !panel.partiesEnabled) && partyEditorOpen) {
      setPartyEditorOpen(false);
    }
  }, [activeSection, panel.partiesEnabled, partyEditorOpen]);

  if (!open) {
    return null;
  }

  const votingSitesEnabled = panel.enabled && !panel.waitingForPopulation;
  const menuItems: Array<{ section: PanelSection; label: string; title: string; tooltip: string; disabled?: boolean }> = [
    {
      section: "votingSites",
      label: t("UI.Menu.VotingSites.Label", "View voting sites"),
      title: t("UI.Menu.VotingSites.Title", "Voting sites"),
      tooltip: votingSitesEnabled
        ? t("UI.Menu.VotingSites.Tooltip.Enabled", "Show voting locations on the map.")
        : t("UI.Menu.VotingSites.Tooltip.Disabled", "Voting locations unlock when Elections are enabled and the city reaches the minimum population."),
      disabled: !votingSitesEnabled,
    },
    {
      section: "mayor",
      label: t("UI.Menu.Mayor.Label", "Current mayor panel"),
      title: t("UI.Menu.Mayor.Title", "Current mayor"),
      tooltip: t("UI.Menu.Mayor.Tooltip", "Show the current mayor and mayoral actions."),
    },
    {
      section: "schedule",
      label: t("UI.Menu.Schedule.Label", "Election schedule"),
      title: t("UI.Menu.Schedule.Title", "Election schedule"),
      tooltip: t("UI.Menu.Schedule.Tooltip", "Show election dates, voting hours, results time, and poll status."),
    },
    {
      section: "programs",
      label: t("UI.Menu.Programs.Label", "Civic programs"),
      title: t("UI.Menu.Programs.Title", "Civic programs"),
      tooltip: t("UI.Menu.Programs.Tooltip", "Fund turnout support programs before election day."),
    },
    {
      section: "legislation",
      label: t("UI.Menu.Legislation.Label", "Legislation"),
      title: t("UI.Menu.Legislation.Title", "Legislation"),
      tooltip: t("UI.Menu.Legislation.Tooltip", "Pass or repeal persistent election legislation."),
    },
    {
      section: "candidates",
      label: t("UI.Menu.Candidates.Label", "Candidates"),
      title: t("UI.Menu.Candidates.Title", "Candidates"),
      tooltip: t("UI.Menu.Candidates.Tooltip", "Show the mayoral candidates and campaign donation controls."),
    },
    {
      section: "parties",
      label: t("UI.Menu.Parties.Label", "Parties"),
      title: t("UI.Menu.Parties.Title", "Parties"),
      tooltip: panel.partiesEnabled
        ? t("UI.Menu.Parties.Tooltip.Enabled", "Manage fictional political parties, reputation, colors, and party tags.")
        : t("UI.Menu.Parties.Tooltip.Disabled", "Political parties are disabled in mod settings."),
      disabled: !panel.partiesEnabled,
    },
    {
      section: "residence",
      label: t("UI.Menu.Residence.Label", "Mayor residence"),
      title: t("UI.Menu.Residence.Title", "Mayor residence"),
      tooltip: t("UI.Menu.Residence.Tooltip", "Set the mayor's home and City Hall workplace from the selected building."),
    },
  ];
  const activeItem = menuItems.find((item) => item.section === activeSection) ?? menuItems[0];
  const subPanelClass = activeSection === "candidates"
    ? styles.subPanelWide
    : activeSection === "parties"
      ? partyEditorOpen
        ? styles.subPanelPartiesEditing
        : styles.subPanelWide
    : activeSection === "mayor"
      ? styles.subPanelMayor
    : activeSection === "residence"
      ? styles.subPanelMedium
    : activeSection === "schedule"
      ? styles.subPanelMedium
    : activeSection === "programs"
      ? styles.subPanelPrograms
    : activeSection === "legislation"
      ? styles.subPanelPrograms
      : styles.subPanelNarrow;

  return (
    <div className={styles.menuCluster}>
      <nav draggable className={styles.sideMenu} aria-label={t("UI.Panel.Aria", "Election panels")}>
        {menuItems.map((item) => (
          <Tooltip tooltip={item.tooltip} key={item.section}>
            <button
              type="button"
              disabled={item.disabled}
              aria-label={item.label}
              aria-pressed={activeSection === item.section}
              className={`${styles.sideMenuButton} ${activeSection === item.section ? styles.sideMenuButtonActive : ""}`}
              onClick={() => {
                if (item.disabled) {
                  return;
                }

                setMenuState({ activeSection: item.section });
                if (item.section === "votingSites" && !showVotingLocations) {
                  trigger(mod.id, "setShowVotingLocations", true);
                }
              }}
            >
              <PanelMenuIcon section={item.section} />
            </button>
          </Tooltip>
        ))}
      </nav>

      <section draggable className={`${styles.subPanel} ${subPanelClass}`}>
        <header className={styles.subPanelHeader}>
          <div className={styles.titleBlock}>
            <span className={styles.title}>{activeItem.title}</span>
          </div>
        </header>

        <main className={styles.subPanelContent}>
          <PanelNotices panel={panel} />
          <ActivePanelContent
            activeSection={activeSection}
            panel={panel}
            candidates={candidates}
            showVotingLocations={showVotingLocations}
            onPartyEditorOpenChange={setPartyEditorOpen}
          />
        </main>
      </section>
    </div>
  );
}

function PanelMenuIcon(props: { section: PanelSection }): ReactElement {
  if (props.section === "votingSites") {
    return <img src={icon} className={`${styles.sideMenuIcon} ${styles.sideMenuImageIcon}`} alt="" />;
  }

  if (props.section === "mayor") {
    return (
      <svg className={`${styles.sideMenuIcon} ${styles.sideMenuGlyphIcon}`} viewBox="0 0 24 24" aria-hidden="true">
        <path d="M7 20h10" />
        <path d="M8 17h8l1-9-3 2.4L12 5l-2 5.4L7 8l1 9Z" />
        <path d="M9 13h6" />
      </svg>
    );
  }

  if (props.section === "residence") {
    return (
      <svg className={`${styles.sideMenuIcon} ${styles.sideMenuGlyphIcon}`} viewBox="0 0 24 24" aria-hidden="true">
        <path d="M4 11.5 12 5l8 6.5" />
        <path d="M6.5 10.5V20h11V10.5" />
        <path d="M10 20v-5h4v5" />
        <path d="M9 8.5h6" />
      </svg>
    );
  }

  if (props.section === "schedule") {
    return (
      <svg className={`${styles.sideMenuIcon} ${styles.sideMenuGlyphIcon}`} viewBox="0 0 24 24" aria-hidden="true">
        <path d="M7 3v4" />
        <path d="M17 3v4" />
        <path d="M4.5 8h15" />
        <path d="M6 5h12a1.5 1.5 0 0 1 1.5 1.5v12A1.5 1.5 0 0 1 18 20H6a1.5 1.5 0 0 1-1.5-1.5v-12A1.5 1.5 0 0 1 6 5Z" />
        <path d="M8 12h2" />
        <path d="M12 12h2" />
        <path d="M8 16h2" />
        <path d="M12 16h2" />
      </svg>
    );
  }

  if (props.section === "programs") {
    return (
      <svg className={`${styles.sideMenuIcon} ${styles.sideMenuGlyphIcon}`} viewBox="0 0 24 24" aria-hidden="true">
        <path d="M4 12h3l7-4v8l-7-4H4Z" />
        <path d="M14 9.5c2 0 3 1.1 3 2.5s-1 2.5-3 2.5" />
        <path d="M7 12v5a1.5 1.5 0 0 0 1.5 1.5H10" />
        <path d="M19 9a5 5 0 0 1 0 6" />
      </svg>
    );
  }

  if (props.section === "legislation") {
    return (
      <svg className={`${styles.sideMenuIcon} ${styles.sideMenuGlyphIcon}`} viewBox="0 0 24 24" aria-hidden="true">
        <path d="M7 3.5h8l3 3V20H7a1.5 1.5 0 0 1-1.5-1.5V5A1.5 1.5 0 0 1 7 3.5Z" />
        <path d="M15 3.5V7h3" />
        <path d="M8.5 11h7" />
        <path d="M8.5 14h7" />
        <path d="M8.5 17h5" />
      </svg>
    );
  }

  if (props.section === "parties") {
    return (
      <svg className={`${styles.sideMenuIcon} ${styles.sideMenuGlyphIcon}`} viewBox="0 0 24 24" aria-hidden="true">
        <path d="M5 20V5" />
        <path d="M5 5h11l-1.5 3L16 11H5" />
        <path d="M8 15h9" />
        <path d="M8 18h6" />
      </svg>
    );
  }

  return (
    <svg className={`${styles.sideMenuIcon} ${styles.sideMenuGlyphIcon}`} viewBox="0 0 24 24" aria-hidden="true">
      <path d="M8 11a3 3 0 1 0 0-6 3 3 0 0 0 0 6Z" />
      <path d="M16 11a3 3 0 1 0 0-6 3 3 0 0 0 0 6Z" />
      <path d="M4 20v-2.2C4 15.7 5.7 14 7.8 14h.4c2.1 0 3.8 1.7 3.8 3.8V20" />
      <path d="M12 20v-2.2c0-2.1 1.7-3.8 3.8-3.8h.4c2.1 0 3.8 1.7 3.8 3.8V20" />
    </svg>
  );
}

function PanelNotices(props: { panel: ElectionPanel }): ReactElement {
  const { panel } = props;

  return (
    <>
      {!panel.enabled && (
        <div className={styles.notice}>{t("UI.Notice.Disabled", "Elections are disabled in mod settings.")}</div>
      )}
      {panel.enabled && panel.waitingForPopulation && (
        <div className={styles.notice}>
          {t("UI.Notice.WaitingPopulation", "Elections will start when the city reaches {minimum} population. Current population: {current}.", {
            minimum: formatAmount(panel.minimumPopulation),
            current: formatAmount(panel.currentPopulation),
          })}
        </div>
      )}
    </>
  );
}

function ActivePanelContent(props: {
  activeSection: PanelSection;
  panel: ElectionPanel;
  candidates: Candidate[];
  showVotingLocations: boolean;
  onPartyEditorOpenChange: (open: boolean) => void;
}): ReactElement {
  const { activeSection, panel, candidates, showVotingLocations, onPartyEditorOpenChange } = props;

  if (activeSection === "mayor") {
    return panel.mayorName ? (
      <MayorSection panel={panel} candidates={candidates} />
    ) : (
      <div className={styles.notice}>{t("UI.Notice.NoMayor", "No mayor has been selected yet.")}</div>
    );
  }

  if (activeSection === "residence") {
    return <MayorResidenceSection panel={panel} />;
  }

  if (activeSection === "schedule") {
    return <ScheduleSection panel={panel} candidates={candidates} />;
  }

  if (activeSection === "programs") {
    return <SupportProgramsPanel panel={panel} />;
  }

  if (activeSection === "legislation") {
    return <LegislationPanel panel={panel} />;
  }

  if (activeSection === "candidates") {
    const candidateRows = getCandidateRows(candidates, 2);

    if (!hasActiveCandidateField(panel)) {
      return <div className={styles.notice}>{t("UI.Notice.NoCandidates", "There are no candidates right now.")}</div>;
    }

    return (
      <section className={styles.candidateStack}>
        {candidateRows.map((row, rowIndex) => (
          <div className={styles.candidateRow} key={`candidate-row-${rowIndex}`}>
            {row.map((candidate) => (
              <div className={styles.candidateSlot} key={candidate.index}>
                <CandidateCard
                  candidate={candidate}
                  donationTiers={panel.donationTiers ?? []}
                  donationsOpen={panel.donationsOpen}
                  canDonate={panel.canDonate}
                  donationUsedToday={panel.donationUsedToday}
                />
              </div>
            ))}
            {row.length < 2 && (
              <div className={`${styles.candidateSlot} ${styles.candidateSlotEmpty}`} aria-hidden="true" />
            )}
          </div>
        ))}
      </section>
    );
  }

  if (activeSection === "parties") {
    return panel.partiesEnabled ? (
      <PartiesSection panel={panel} onEditorOpenChange={onPartyEditorOpenChange} />
    ) : (
      <div className={styles.notice}>{t("UI.Notice.PartiesDisabled", "Parties are disabled in mod settings.")}</div>
    );
  }

  return (
    <VotingSitesPanel
      panel={panel}
      showVotingLocations={showVotingLocations}
    />
  );
}

function VotingSitesPanel(props: {
  panel: ElectionPanel;
  showVotingLocations: boolean;
}): ReactElement {
  const { panel, showVotingLocations } = props;
  const canShowLocations = panel.enabled && !panel.waitingForPopulation;
  const electionDay = panel.stage === "Voting";

  return (
    <section className={styles.votingSitesPanel}>
      <div className={styles.sectionHeader}>
        <Tooltip tooltip={t("UI.VotingSites.Header.Tooltip", "Voting-site overlay status. On election day, map markers include live vote counts for each location.")}>
          <span>{t("UI.VotingSites.Header", "Voting sites")}</span>
        </Tooltip>
        <Tooltip tooltip={showVotingLocations ? t("UI.VotingSites.Status.Visible.Tooltip", "Voting-site overlay is visible.") : t("UI.VotingSites.Status.Hidden.Tooltip", "Voting-site overlay is hidden.")}>
          <strong>{showVotingLocations ? t("UI.VotingSites.Status.Visible", "Overlay visible") : t("UI.VotingSites.Status.Hidden", "Overlay hidden")}</strong>
        </Tooltip>
      </div>

      <div className={styles.votingSitesSummary}>
        <Tooltip tooltip={t("UI.VotingSites.Election.Tooltip", "Election date and voting window for the active mayoral race.")}>
          <div className={styles.votingSitesStatus}>
            <span>{t("UI.VotingSites.Election.Label", "Election")}</span>
            <strong>
              {panel.electionDate
                ? `${panel.electionDate} ${panel.votingStartTime || "08:00"}-${panel.votingEndTime || "17:00"}`
                : t("UI.Pending", "Pending")}
            </strong>
          </div>
        </Tooltip>
        <Tooltip tooltip={t("UI.VotingSites.Markers.Tooltip", "Live vote counts appear only during the voting phase.")}>
          <div className={styles.votingSitesStatus}>
            <span>{t("UI.VotingSites.Markers.Label", "Map markers")}</span>
            <strong>{electionDay ? t("UI.VotingSites.Markers.Live", "Live results") : t("UI.VotingSites.Markers.Locations", "Voting locations")}</strong>
          </div>
        </Tooltip>
      </div>

      <Tooltip tooltip={canShowLocations
        ? showVotingLocations
          ? t("UI.VotingSites.Button.Tooltip.Hide", "Hide voting locations on the map.")
          : t("UI.VotingSites.Button.Tooltip.Show", "Show voting locations on the map.")
        : t("UI.Menu.VotingSites.Tooltip.Disabled", "Voting locations unlock when Elections are enabled and the city reaches the minimum population.")}
      >
        <button
          type="button"
          disabled={!canShowLocations}
          aria-pressed={showVotingLocations}
          className={`${styles.votingLocationsButton} ${showVotingLocations ? styles.votingLocationsButtonActive : ""}`}
          onClick={() => {
            if (canShowLocations) {
              trigger(mod.id, "setShowVotingLocations", !showVotingLocations);
            }
          }}
        >
          <BallotBoxIcon />
          <span className={styles.votingLocationsButtonLabel}>
            {showVotingLocations ? t("UI.VotingSites.Button.Hide", "Hide voting sites") : t("UI.VotingSites.Button.Show", "View voting sites")}
          </span>
        </button>
      </Tooltip>
    </section>
  );
}

function BallotBoxIcon(): ReactElement {
  return <img src={icon} className={styles.ballotIcon} alt="" />;
}

function Info(props: { label: string; value: string; tooltip: string }): ReactElement {
  return (
    <Tooltip tooltip={props.tooltip}>
      <div className={styles.infoItem}>
        <span>{props.label}</span>
        <strong>{props.value}</strong>
      </div>
    </Tooltip>
  );
}

function ScheduleSection(props: { panel: ElectionPanel; candidates: Candidate[] }): ReactElement {
  const { panel, candidates } = props;
  const [pollTab, setPollTab] = useState<"overall" | "age" | "education" | "income">("overall");
  const votingWindow = panel.votingStartTime && panel.votingEndTime
    ? `${panel.votingStartTime}-${panel.votingEndTime}`
    : t("UI.Pending", "Pending");
  const electionSchedule = panel.electionDate
    ? `${panel.electionDate} ${votingWindow}`
    : t("UI.Pending", "Pending");
  const resultsSchedule = panel.electionDate
    ? `${panel.electionDate} ${panel.resultsTime || "20:00"}`
    : panel.resultsTime || "20:00";

  return (
    <section className={styles.scheduleCard}>
      <div className={styles.sectionHeader}>
        <Tooltip tooltip={t("UI.Schedule.Header.Tooltip", "Election schedule and current lifecycle phase.")}>
          <span>{t("UI.Schedule.Header", "Election schedule")}</span>
        </Tooltip>
      </div>

      <Tooltip tooltip={t("UI.Schedule.Summary.Tooltip", "Summary of the current election cycle and whether this is a regular or accelerated race.")}>
        <div className={styles.summaryText}>{panel.cycleLabel}</div>
      </Tooltip>

      <div className={styles.metaGrid}>
        <Info
          label={t("UI.Info.Phase", "Phase")}
          value={panel.stageLabel || t("UI.Pending", "Pending")}
          tooltip={t("UI.Schedule.Phase.Tooltip", "The current phase of the election cycle.")}
        />
        <Info
          label={t("UI.Info.Poll", "Poll")}
          value={panel.pollDate ? `${panel.pollDate} 08:00` : t("UI.Pending", "Pending")}
          tooltip={t("UI.Schedule.Poll.Tooltip", "The campaign poll is released at 08:00 on this date.")}
        />
        <Info
          label={t("UI.Info.Election", "Election")}
          value={electionSchedule}
          tooltip={t("UI.Schedule.Election.Tooltip", "Election day and voting window for the active mayoral race.")}
        />
        <Info
          label={t("UI.Info.Results", "Results")}
          value={resultsSchedule}
          tooltip={t("UI.Schedule.Results.Tooltip", "Election results announcement date and time.")}
        />
        {panel.pendingMayorName && (
          <Info
            label={t("UI.Info.Inauguration", "Inauguration")}
            value={panel.pendingMayorInaugurationDate || t("UI.Date.January1", "January 1")}
            tooltip={t("UI.Schedule.Inauguration.Tooltip", "{name} is mayor-elect. The sitting mayor remains in office until this date.", { name: panel.pendingMayorName })}
          />
        )}
      </div>

      <div className={styles.pollBlock}>
        <div className={styles.pollHeader}>
          <Tooltip tooltip={t("UI.Poll.Header.Tooltip", "Poll status. Before the poll is released this shows the scheduled release date; after release it shows sampled voter preferences.")}>
            <span>{panel.pollReleased ? t("UI.Poll.Header.Results", "Current poll results") : t("UI.Poll.Header.Scheduled", "Poll scheduled")}</span>
          </Tooltip>
          {panel.pollReleased && (
            <Tooltip tooltip={t("UI.Poll.Sample.Tooltip", "Number of eligible residents sampled by the campaign poll.")}>
              <strong className={styles.pollSample}>
                <SampleSizeLabel value={panel.poll.sampleSize} />
              </strong>
            </Tooltip>
          )}
        </div>

        {panel.pollReleased ? (
          <>
            <div className={styles.pollTabs} role="tablist" aria-label={t("UI.Poll.Tabs.Aria", "Poll breakdown")}>
              {[
                { key: "overall" as const, label: t("UI.Poll.Tab.Overall", "Overall") },
                { key: "age" as const, label: t("UI.Poll.Tab.Age", "Age") },
                { key: "education" as const, label: t("UI.Poll.Tab.Education", "Education") },
                { key: "income" as const, label: t("UI.Poll.Tab.Income", "Income") },
              ].map((tab) => (
                <Tooltip tooltip={t("UI.Poll.Tab.Tooltip", "Show {label} poll results.", { label: tab.label.toLowerCase() })} key={tab.key}>
                  <button
                    type="button"
                    role="tab"
                    aria-selected={pollTab === tab.key}
                    className={`${styles.pollTabButton} ${pollTab === tab.key ? styles.pollTabButtonActive : ""}`}
                    onClick={() => setPollTab(tab.key)}
                  >
                    {tab.label}
                  </button>
                </Tooltip>
              ))}
            </div>
            {pollTab === "overall" && (
              <PollResultView
                poll={panel.poll}
                candidates={candidates}
              />
            )}
            {pollTab === "age" && (
              <PollBreakdownList
                groups={panel.poll.ageGroups}
                candidates={candidates}
              />
            )}
            {pollTab === "education" && (
              <PollBreakdownList
                groups={panel.poll.educationGroups}
                candidates={candidates}
              />
            )}
            {pollTab === "income" && (
              <PollBreakdownList
                groups={panel.poll.incomeGroups}
                candidates={candidates}
              />
            )}
          </>
        ) : (
          <div className={styles.notice}>
            {panel.pollDate
              ? t("UI.Poll.ReleaseNotice", "Poll releases on {date} at 08:00.", { date: panel.pollDate })
              : t("UI.Poll.PendingNotice", "Poll date is pending.")}
          </div>
        )}
      </div>
    </section>
  );
}

function PollRows(props: {
  poll: PollResult;
  candidates: Candidate[];
}): ReactElement {
  const { poll, candidates } = props;
  const candidateResults = getPollCandidateResults(poll, candidates);

  return (
    <div className={styles.pollRows}>
      {candidateResults.map((result) => {
        const candidate = candidates.find((item) => item.index === result.index);
        return (
          <PollRow
            key={result.index}
            label={result.name}
            value={result.percent}
            color={candidate ? getCandidateDisplayColor(candidate) : getCandidateColor(result.index)}
          />
        );
      })}
      <PollRow
        label={t("UI.Poll.Undecided", "Undecided")}
        value={poll.percentUndecided}
        color={undecidedColor}
      />
    </div>
  );
}

function SampleSizeLabel(props: { value: number }): ReactElement {
  return (
    <span className={styles.sampleSizeLabel}>
      <span>{formatAmount(props.value)}</span>
      <span className={styles.sampleSizeWord}>{t("UI.Poll.Sample.Word", "sampled")}</span>
    </span>
  );
}

function PollResultView(props: {
  poll: PollResult;
  candidates: Candidate[];
}): ReactElement {
  const { poll, candidates } = props;

  return (
    <>
      <Tooltip tooltip={poll.resultDescription || t("UI.Poll.Readout.Tooltip", "Poll read based on the sample and margin of error.")}>
        <div className={styles.pollReadout}>
          <strong>{poll.resultLabel || t("UI.Poll.Released", "Poll released")}</strong>
          <span className={styles.pollReadoutMeta}>
            <SampleSizeLabel value={poll.sampleSize} />
            <span className={styles.pollMetaSeparator}>|</span>
            <span>+/-{poll.marginOfError}%</span>
          </span>
        </div>
      </Tooltip>
      <PollRows
        poll={poll}
        candidates={candidates}
      />
    </>
  );
}

function PollBreakdownList(props: {
  groups: PollBreakdown[];
  candidates: Candidate[];
}): ReactElement {
  const groups = Array.isArray(props.groups) ? props.groups : [];

  return (
    <div className={styles.pollBreakdownList}>
      {groups.map((group) => (
        <PollBreakdownCard
          key={group.key || group.label}
          group={group}
          candidates={props.candidates}
        />
      ))}
      {!groups.length && (
        <div className={styles.notice}>{t("UI.Poll.NoBreakdown", "No poll breakdown data is available.")}</div>
      )}
    </div>
  );
}

function PollBreakdownCard(props: {
  group: PollBreakdown;
  candidates: Candidate[];
}): ReactElement {
  const { group, candidates } = props;

  return (
    <article className={styles.pollBreakdownCard}>
      <Tooltip tooltip={group.resultDescription || t("UI.Poll.Breakdown.Tooltip", "Poll read for this sampled group.")}>
        <div className={styles.pollBreakdownHeader}>
          <span className={styles.pollBreakdownTitle}>{group.label}</span>
          <span className={styles.pollBreakdownMeta}>
            <SampleSizeLabel value={group.sampleSize} />
            <span className={styles.pollMetaSeparator}>|</span>
            <span>+/-{group.marginOfError}%</span>
          </span>
        </div>
      </Tooltip>
      <PollRows
        poll={group}
        candidates={candidates}
      />
    </article>
  );
}

function SupportProgramsPanel(props: { panel: ElectionPanel }): ReactElement {
  const { panel } = props;
  const programs = Array.isArray(panel.supportPrograms) ? panel.supportPrograms : [];
  const fallbackReason = panel.stage === "Voting"
    ? t("UI.Programs.Fallback.ElectionDay", "Civic programs are unavailable on election day.")
    : panel.supportProgramUsedToday
      ? t("UI.Programs.Fallback.UsedToday", "Only one civic program can be funded per day. Today's program is {program}.", {
        program: panel.supportProgramUsedTodayLabel || t("UI.Programs.Fallback.AlreadySelected", "already selected"),
      })
      : t("UI.Programs.Fallback.Closed", "Civic programs are available before election day once candidates are selected.");

  return (
    <section className={styles.supportProgramsPanel}>
      <div className={styles.sectionHeader}>
        <Tooltip tooltip={t("UI.Programs.Header.Tooltip", "Civic programs can be funded once per day before election day.")}>
          <span>{t("UI.Programs.Header", "Civic programs")}</span>
        </Tooltip>
        <Tooltip tooltip={t("UI.Programs.Cost.Tooltip", "Each civic program costs half of the current campaign donation value.")}>
          <strong>{t("UI.Each", "{amount} each", { amount: formatAmount(panel.supportProgramCost) })}</strong>
        </Tooltip>
      </div>

      {panel.supportProgramUsedToday && (
        <Tooltip tooltip={t("UI.Programs.Today.Tooltip", "The daily civic program slot refreshes on the next calendar day.")}>
          <div className={styles.notice}>
            {t("UI.Programs.Today", "Today's program: {program}", {
              program: panel.supportProgramUsedTodayLabel || t("UI.Programs.Today.Selected", "selected"),
            })}
          </div>
        </Tooltip>
      )}

      <div className={styles.supportProgramList}>
        {programs.map((program) => {
          const status = program.index === 0
            ? program.active ? t("UI.Programs.Status.HolidayScheduled", "Holiday scheduled") : t("UI.Programs.Status.NotScheduled", "Not scheduled")
            : t("UI.Programs.Status.CurrentBonus", "Current bonus +{percent}%", { percent: program.currentBonusPercent || 0 });
          const tooltip = program.canRun
            ? program.tooltip
            : program.disabledReason || program.tooltip || fallbackReason;

          return (
            <article
              className={`${styles.supportProgramCard} ${program.active ? styles.supportProgramCardActive : ""}`}
              key={program.index}
            >
              <div className={styles.supportProgramInfo}>
                <Tooltip tooltip={program.tooltip || program.description}>
                  <div>
                    <span className={styles.supportProgramTitle}>{program.title}</span>
                    <span className={styles.supportProgramDescription}>{program.description}</span>
                  </div>
                </Tooltip>
                <Tooltip tooltip={program.index === 0 ? t("UI.Programs.Status.Tooltip.Holiday", "Holiday scheduling status.") : t("UI.Programs.Status.Tooltip.Bonus", "Accumulated election turnout bonus for this voter group.")}>
                  <strong className={styles.supportProgramStatus}>{status}</strong>
                </Tooltip>
              </div>
              <Tooltip tooltip={tooltip}>
                <Button
                  variant="flat"
                  className={program.canRun ? styles.supportProgramButton : `${styles.supportProgramButton} ${styles.supportProgramButtonDisabled}`}
                  disabled={!program.canRun}
                  aria-disabled={!program.canRun}
                  onSelect={() => {
                    if (program.canRun) {
                      trigger(mod.id, "runSupportProgram", program.index);
                    }
                  }}
                >
                  <strong>{program.canRun ? t("UI.Programs.Button.Fund", "Fund") : program.active && program.index === 0 ? t("UI.Programs.Button.Scheduled", "Scheduled") : t("UI.Unavailable", "Unavailable")}</strong>
                </Button>
              </Tooltip>
            </article>
          );
        })}
      </div>

      {!programs.length && (
        <div className={styles.notice}>{t("UI.Programs.NoPrograms", "Civic programs are unavailable right now.")}</div>
      )}
      {!panel.supportProgramsOpen && programs.length > 0 && (
        <div className={styles.helpText}>{fallbackReason}</div>
      )}
    </section>
  );
}

function LegislationPanel(props: { panel: ElectionPanel }): ReactElement {
  const { panel } = props;
  const legislation = Array.isArray(panel.legislation) ? panel.legislation : [];
  const fallbackReason = panel.stage === "Voting"
    ? t("UI.Legislation.Fallback.ElectionDay", "Election legislation is unavailable on election day.")
    : panel.legislationActionUsedToday
      ? t("UI.Legislation.Fallback.UsedToday", "Only one election legislation action can be attempted per day.")
    : t("UI.Legislation.Fallback.Closed", "Election legislation can be changed before election day once candidates are selected.");

  return (
    <section className={styles.legislationPanel}>
      <div className={styles.sectionHeader}>
        <Tooltip tooltip={t("UI.Legislation.Header.Tooltip", "Legislation persists across future elections until it is repealed. Passing and repealing use city funds, can fail, and do not create corruption risk.")}>
          <span>{t("UI.Legislation.Header", "Election legislation")}</span>
        </Tooltip>
      </div>

      <div className={styles.legislationList}>
        {legislation.map((law) => {
          const actionEnabled = law.active ? law.canRepeal : law.canPass;
          const buttonLabel = law.active ? t("UI.Legislation.Button.Repeal", "Repeal") : t("UI.Legislation.Button.Pass", "Pass");
          const tooltip = actionEnabled
            ? law.tooltip || law.description
            : law.disabledReason || fallbackReason;

          return (
            <article
              className={`${styles.legislationCard} ${law.active ? styles.legislationCardActive : ""}`}
              key={law.index}
            >
              <div className={styles.legislationInfo}>
                <Tooltip tooltip={law.tooltip || law.description}>
                  <div>
                    <span className={styles.legislationTitle}>{law.title}</span>
                    <span className={styles.legislationDescription}>{law.description}</span>
                  </div>
                </Tooltip>
                {law.active && (
                  <div className={styles.legislationMetaRow}>
                    <Tooltip tooltip={t("UI.Legislation.Active.Tooltip", "Active legislation remains in effect across elections.")}>
                      <strong className={styles.legislationStatusActive}>{t("UI.Legislation.Active", "Active")}</strong>
                    </Tooltip>
                  </div>
                )}
              </div>
              <Tooltip tooltip={tooltip}>
                <Button
                  variant="flat"
                  className={actionEnabled ? styles.legislationButton : `${styles.legislationButton} ${styles.legislationButtonDisabled}`}
                  disabled={!actionEnabled}
                  aria-disabled={!actionEnabled}
                  onSelect={() => {
                    if (actionEnabled) {
                      trigger(mod.id, "setLegislation", law.index, !law.active);
                    }
                  }}
                >
                  <strong>{actionEnabled ? buttonLabel : t("UI.Unavailable", "Unavailable")}</strong>
                </Button>
              </Tooltip>
            </article>
          );
        })}
      </div>

      {!legislation.length && (
        <div className={styles.notice}>{t("UI.Legislation.NoLegislation", "Election legislation is unavailable right now.")}</div>
      )}
      {!panel.legislationOpen && legislation.length > 0 && (
        <div className={styles.helpText}>{fallbackReason}</div>
      )}
    </section>
  );
}

function MayorResidenceSection(props: { panel: ElectionPanel }): ReactElement {
  const { panel } = props;
  const home = panel.mayorHome ?? defaultPanel.mayorHome;
  const workplace = panel.mayorWorkplace ?? defaultPanel.mayorWorkplace;
  const homeChoices = Array.isArray(panel.mayorHomeChoices) ? panel.mayorHomeChoices : defaultPanel.mayorHomeChoices;
  const workplaceChoices = Array.isArray(panel.mayorWorkplaceChoices) ? panel.mayorWorkplaceChoices : defaultPanel.mayorWorkplaceChoices;
  const selected = panel.mayorSelectedBuilding ?? defaultPanel.mayorSelectedBuilding;

  return (
    <section className={styles.residenceCard}>
      <div className={styles.sectionHeader}>
        <Tooltip tooltip={t("UI.Residence.Header.Tooltip", "The assigned residence and office are enforced for the current mayor. If the target is full, another resident or worker is removed first.")}>
          <span>{t("UI.Residence.Header", "Mayor assignments")}</span>
        </Tooltip>
        <Tooltip tooltip={home.atTarget && workplace.atTarget ? t("UI.Residence.Status.Assigned.Tooltip", "The mayor is assigned to both selected targets.") : t("UI.Residence.Status.Relocating.Tooltip", "The mayor will be moved to the selected targets during the next assignment update.")}>
          <strong>{home.atTarget && workplace.atTarget ? t("UI.Residence.Status.Assigned", "Assigned") : t("UI.Residence.Status.Relocating", "Relocating")}</strong>
        </Tooltip>
      </div>

      <Tooltip tooltip={selected.exists ? t("UI.Residence.Selected.Tooltip.Exists", "Currently selected game building.") : t("UI.Residence.Selected.Tooltip.Empty", "No building is selected in the game UI.")}>
        <div className={styles.selectedBuildingBox}>
          <span>{t("UI.Residence.Selected.Label", "Selected building")}</span>
          <strong>{selected.name}</strong>
          {selected.exists && (
            <div className={styles.selectedBuildingTags}>
              <span className={selected.canBeHome ? styles.targetTagActive : styles.targetTagMuted}>{t("UI.Residence.Tag.Home", "Home")}</span>
              <span className={selected.canBeWorkplace ? styles.targetTagActive : styles.targetTagMuted}>{t("UI.Residence.Tag.CityHall", "City Hall")}</span>
            </div>
          )}
        </div>
      </Tooltip>

      <MayorTargetCard
        kind="home"
        title={t("UI.Residence.Home.Title", "Mayor home")}
        target={home}
        choices={homeChoices}
        choicesLimited={panel.mayorHomeChoicesLimited}
        selected={selected}
        canUseSelected={selected.canBeHome}
        alreadySelected={selected.isHomeTarget}
        choiceTrigger="setMayorHome"
        focusTrigger="focusMayorHome"
        useTrigger="useSelectedMayorHome"
      />

      <MayorTargetCard
        kind="workplace"
        title={t("UI.Residence.Workplace.Title", "Mayor workplace")}
        target={workplace}
        choices={workplaceChoices}
        choicesLimited={panel.mayorWorkplaceChoicesLimited}
        selected={selected}
        canUseSelected={selected.canBeWorkplace}
        alreadySelected={selected.isWorkplaceTarget}
        choiceTrigger="setMayorWorkplace"
        focusTrigger="focusMayorWorkplace"
        useTrigger="useSelectedMayorWorkplace"
      />
    </section>
  );
}

function MayorTargetCard(props: {
  kind: "home" | "workplace";
  title: string;
  target: MayorBuildingTarget;
  choices: MayorBuildingChoice[];
  choicesLimited: boolean;
  selected: MayorSelectedBuilding;
  canUseSelected: boolean;
  alreadySelected: boolean;
  choiceTrigger: string;
  focusTrigger: string;
  useTrigger: string;
}): ReactElement {
  const { kind, title, target, choices, choicesLimited, selected, canUseSelected, alreadySelected, choiceTrigger, focusTrigger, useTrigger } = props;
  const status = target.atTarget
    ? t("UI.Residence.Target.Status.Assigned", "Mayor assigned")
    : target.exists
      ? t("UI.Residence.Target.Status.Pending", "Move pending")
      : t("UI.Residence.Target.Status.None", "No target");
  const useDisabled = !selected.exists || !canUseSelected || alreadySelected;
  const useTooltip = alreadySelected
    ? t("UI.Residence.Use.Tooltip.Selected", "This building is already selected.")
    : canUseSelected
      ? kind === "home"
        ? t("UI.Residence.Use.Tooltip.Home", "Assign the mayor's household to this low-density residence.")
        : t("UI.Residence.Use.Tooltip.Workplace", "Assign the mayor to work at this City Hall.")
      : kind === "home"
        ? t("UI.Residence.Use.Tooltip.HomeInvalid", "Selected building must be a low-density residential building.")
        : t("UI.Residence.Use.Tooltip.WorkplaceInvalid", "Selected building must be a City Hall asset.");

  return (
    <article className={styles.residenceTargetCard}>
      <div className={styles.residenceTargetHeader}>
        <div className={styles.residenceTargetTitle}>
          <Tooltip tooltip={kind === "home" ? t("UI.Residence.Target.Home.Tooltip", "Saved mayor residence target.") : t("UI.Residence.Target.Workplace.Tooltip", "Saved mayor workplace target.")}>
            <span>{title}</span>
          </Tooltip>
          <Tooltip tooltip={target.entityLabel}>
            <strong>{target.name}</strong>
          </Tooltip>
        </div>
        <Tooltip tooltip={target.atTarget ? t("UI.Residence.Target.Assigned.Tooltip", "The mayor is already assigned here.") : t("UI.Residence.Target.Move.Tooltip", "The assignment system will move the mayor here.")}>
          <span className={target.atTarget ? styles.targetTagActive : styles.targetTagPending}>{status}</span>
        </Tooltip>
      </div>

      <MayorChoiceDropdown
        kind={kind}
        target={target}
        choices={choices}
        choicesLimited={choicesLimited}
        choiceTrigger={choiceTrigger}
      />

      <div className={styles.residenceMetaGrid}>
        <Info
          label={t("UI.Info.Current", "Current")}
          value={target.currentExists ? target.currentName : t("UI.Unknown", "Unknown")}
          tooltip={target.currentEntityLabel}
        />
      </div>

      <div className={styles.residenceActionRow}>
        <Tooltip tooltip={target.canFocus ? t("UI.Residence.Focus.Tooltip.Enabled", "Move the camera to the selected mayor target.") : t("UI.Residence.Focus.Tooltip.Disabled", "No target is available to focus.")}>
          <Button
            variant="flat"
            className={target.canFocus ? styles.residenceActionButton : `${styles.residenceActionButton} ${styles.bribeButtonDisabled}`}
            disabled={!target.canFocus}
            aria-disabled={!target.canFocus}
            onSelect={() => {
              if (target.canFocus) {
                trigger(mod.id, focusTrigger);
              }
            }}
          >
            <strong>{t("UI.Residence.Focus", "Focus")}</strong>
          </Button>
        </Tooltip>
        <Tooltip tooltip={useTooltip}>
          <Button
            variant="flat"
            className={!useDisabled ? styles.residenceActionButton : `${styles.residenceActionButton} ${styles.bribeButtonDisabled}`}
            disabled={useDisabled}
            aria-disabled={useDisabled}
            onSelect={() => {
              if (!useDisabled) {
                trigger(mod.id, useTrigger);
              }
            }}
          >
            <strong>{alreadySelected ? t("UI.Selected", "Selected") : t("UI.Residence.Use", "Use selected")}</strong>
          </Button>
        </Tooltip>
      </div>
    </article>
  );
}

function MayorChoiceDropdown(props: {
  kind: "home" | "workplace";
  target: MayorBuildingTarget;
  choices: MayorBuildingChoice[];
  choicesLimited: boolean;
  choiceTrigger: string;
}): ReactElement {
  const { kind, target, choices, choicesLimited, choiceTrigger } = props;
  const label = target.exists ? target.name : kind === "home" ? t("UI.Residence.Choose.Home", "Choose a residence") : t("UI.Residence.Choose.Workplace", "Choose a City Hall");
  const emptyLabel = kind === "home"
    ? t("UI.Residence.Empty.Home", "No low-density residences found")
    : t("UI.Residence.Empty.Workplace", "No City Hall assets found");
  const tooltip = kind === "home"
    ? t("UI.Residence.Dropdown.Home.Tooltip", "Choose the low-density residential building where the mayor should live.")
    : t("UI.Residence.Dropdown.Workplace.Tooltip", "Choose the City Hall asset where the mayor should work.");

  if (choices.length === 0) {
    return (
      <Tooltip tooltip={emptyLabel}>
        <div className={styles.residenceDropdownUnavailable}>{emptyLabel}</div>
      </Tooltip>
    );
  }

  return (
    <Dropdown
      content={(
        <div className={styles.residenceDropdownMenu}>
          {choices.map((choice) => {
            return (
              <DropdownItem<number>
                key={`${choice.entityIndex}:${choice.entityVersion}`}
                value={choice.index}
                selected={choice.selected}
                className={`${styles.residenceDropdownItem} ${choice.selected ? styles.residenceDropdownItemSelected : ""}`}
                onChange={() => {
                  trigger(mod.id, choiceTrigger, choice.entityIndex, choice.entityVersion);
                }}
              >
                <span className={styles.residenceDropdownItemText}>{choice.name}</span>
              </DropdownItem>
            );
          })}
          {choicesLimited && (
            <div className={styles.residenceDropdownHint}>
              {t("UI.Residence.Dropdown.Limited", "More eligible buildings exist. Select one in the city to add it here.")}
            </div>
          )}
        </div>
      )}
    >
      <DropdownToggle className={styles.residenceDropdownToggle} tooltip={tooltip}>
        <span>{label}</span>
      </DropdownToggle>
    </Dropdown>
  );
}

function MayorSection(props: { panel: ElectionPanel; candidates: Candidate[] }): ReactElement {
  const { panel, candidates } = props;
  const [pickerActionKind, setPickerActionKind] = useState<CandidateTargetActionKind | null>(null);
  const activeCandidateField = hasActiveCandidateField(panel);
  const candidateTargets = candidates.filter((candidate): candidate is Candidate => Boolean(candidate?.exists));
  const endorsedName = getCandidateNameByIndex(candidateTargets, panel.endorsedCandidateIndex);
  const tamperBeneficiaryName = getCandidateNameByIndex(candidateTargets, panel.voteTamperingCandidateIndex);
  const bribeStatus = panel.bribeMeetingPending
    ? t("UI.Mayor.Pending", "Pending")
    : "";
  const mayorActions: MayorCampaignAction[] = [
    {
      kind: "bribe" as const,
      title: t("UI.Mayor.Action.PlatformMeeting.Title", "Platform meeting"),
      description: t("UI.Mayor.Action.PlatformMeeting.Description", "The mayor will attempt to convince a selected candidate to soften their platform."),
      buttonLabel: t("UI.Mayor.Action.Choose", "Choose"),
      targetButtonLabel: t("UI.Mayor.Action.Bribe", "Bribe"),
      status: bribeStatus,
      disabled: !panel.canBribe,
      requiresCandidate: true,
    },
    {
      kind: "endorse" as const,
      title: t("UI.Mayor.Action.Endorsement.Title", "Mayor endorsement"),
      description: t("UI.Mayor.Action.Endorsement.Description", "Spend city funds to have the mayor endorse a selected candidate."),
      buttonLabel: t("UI.Mayor.Action.Choose", "Choose"),
      targetButtonLabel: t("UI.Mayor.Action.Endorse", "Endorse"),
      status: panel.endorsementUsed ? t("UI.Mayor.Action.EndorsedStatus", "Endorsed {name}", { name: endorsedName }) : "",
      disabled: !panel.canEndorse || panel.endorsementUsed,
      requiresCandidate: true,
    },
    {
      kind: "cashAssistance" as const,
      title: panel.cashAssistanceTurnoutBonusPercent > 0
        ? t("UI.Mayor.Action.CashAssistance.FundedTitle", "Cash Assistance funded")
        : t("UI.Mayor.Action.CashAssistance.Title", "Cash Assistance"),
      description: t("UI.Mayor.Action.CashAssistance.Description", "Spend city funds to raise turnout for struggling and modest-income residents."),
      buttonLabel: t("UI.Mayor.Action.Fund", "Fund"),
      status: panel.cashAssistanceTurnoutBonusPercent > 0 ? `+${panel.cashAssistanceTurnoutBonusPercent}%` : "",
      disabled: !panel.canBribe || panel.cashAssistanceTurnoutBonusPercent > 0,
      requiresCandidate: false,
    },
    {
      kind: "tamper" as const,
      title: t("UI.Mayor.Action.Tampering.Title", "Vote-count tampering"),
      description: t("UI.Mayor.Action.Tampering.Description", "Spend city funds to arrange a late election-day disruption for a selected candidate."),
      buttonLabel: t("UI.Mayor.Action.Choose", "Choose"),
      targetButtonLabel: t("UI.Mayor.Action.Tamper", "Tamper"),
      status: panel.voteTamperingScheduled ? t("UI.Mayor.Action.PlannedFor", "Planned for {name}", { name: tamperBeneficiaryName }) : "",
      disabled: !panel.canTamper || panel.voteTamperingScheduled,
      requiresCandidate: true,
    },
  ];
  const getActionUnavailableReason = (kind: "bribe" | "endorse" | "cashAssistance" | "tamper"): string => kind === "endorse" && panel.endorsementUsed
    ? t("UI.Mayor.Unavailable.Endorsed", "The mayor already endorsed {name} this election cycle.", { name: endorsedName })
    : kind === "cashAssistance" && panel.cashAssistanceTurnoutBonusPercent > 0
      ? t("UI.Mayor.Unavailable.CashAssistance", "Cash Assistance is already funded this election cycle.")
    : kind === "tamper" && panel.voteTamperingScheduled
      ? t("UI.Mayor.Unavailable.Tampering", "A vote-count operation is already planned for {name} this election cycle.", { name: tamperBeneficiaryName })
    : panel.stage === "Voting"
    ? t("UI.Mayor.Unavailable.ElectionDay", "Mayor campaign actions are unavailable on election day.")
    : panel.bribeMeetingPending
      ? t("UI.Mayor.Unavailable.MeetingPending", "The mayor is trying to schedule this candidate meeting.")
      : panel.bribeBlocked || panel.bribeUsedToday
        ? t("UI.Mayor.Unavailable.ScheduleBlocked", "The mayor's schedule is blocked after today's campaign action.")
        : panel.waitingForPopulation
          ? t("UI.Mayor.Unavailable.WaitingPopulation", "Mayor campaign actions unlock when Elections start.")
          : !activeCandidateField
          ? t("UI.Mayor.Unavailable.NoCandidates", "There are no candidates right now.")
          : !panel.donationsOpen
            ? t("UI.Mayor.Unavailable.Closed", "Mayor campaign actions are available during the campaign before election day.")
            : t("UI.Mayor.Unavailable.Generic", "Mayor campaign action unavailable right now.");
  const runCandidateAction = (kind: CandidateTargetActionKind, candidateIndex: number) => {
    trigger(
      mod.id,
      kind === "endorse"
        ? "endorseCandidate"
        : kind === "tamper"
          ? "tamperVotes"
          : "bribeMayor",
      candidateIndex
    );
    setPickerActionKind(null);
  };

  return (
    <section className={styles.mayorCard}>
      <div className={styles.mayorHeader}>
        <Tooltip tooltip={panel.mayorTemporary
          ? t("UI.Mayor.Portrait.Tooltip.Temporary", "Temporary mayor selected from real citizens. This mayor applies no city effects and supervises the transition until an election is completed.")
          : t("UI.Mayor.Portrait.Tooltip.Current", "Current elected mayor and active mayoral platform.")}
        >
          <img
            className={styles.mayorPortrait}
            src={panel.mayorPortrait || icon}
            alt=""
          />
        </Tooltip>
        <div className={styles.mayorTitle}>
          <Tooltip tooltip={t("UI.Mayor.Name.Label.Tooltip", "The citizen currently serving as mayor. Click the name to move the camera to this citizen.")}>
            <span>{t("UI.Mayor.Name.Label", "Current mayor")}</span>
          </Tooltip>
          <Tooltip tooltip={t("UI.Mayor.Name.Tooltip", "Click the mayor name to move the camera to this citizen.")}>
            <Button
              variant="flat"
              className={styles.mayorNameLink}
              disabled={!panel.mayorCanFocus}
              onSelect={() => {
                if (panel.mayorCanFocus) {
                  trigger(mod.id, "focusMayor");
                }
              }}
            >
              {panel.mayorName}
            </Button>
          </Tooltip>
          {panel.mayorPartyName && (
            <Tooltip tooltip={t("UI.Mayor.PartyReputation.Tooltip", "Party reputation: {reputation}/100.", { reputation: panel.mayorPartyReputation })}>
              <div className={styles.candidatePartyLine}>
                <span className={styles.partySwatchSmall} style={{ backgroundColor: normalizeHexColor(panel.mayorPartyColor) || getCandidateColor(panel.mayorPartyIndex) }} />
                <span>{panel.mayorPartyName}</span>
              </div>
            </Tooltip>
          )}
        </div>
      </div>
      <Tooltip tooltip={t("UI.Mayor.Platform.Tooltip", "Current mayoral platform and city effect. Temporary transition mayors apply no city modifiers.")}>
        <div className={styles.mayorPlatform}>
          <span>{panel.mayorEffectName}</span>
          <PlatformImpactList impacts={panel.mayorPlatformImpacts ?? []} />
        </div>
      </Tooltip>
      <div className={styles.bribeBlock}>
        <div className={styles.sectionHeader}>
          <Tooltip tooltip={t("UI.Mayor.Actions.Header.Tooltip", "Mayor campaign actions are available before election day.")}>
            <span>{t("UI.Mayor.Actions.Header", "Mayor campaign actions")}</span>
          </Tooltip>
          <Tooltip tooltip={t("UI.Mayor.Actions.Cost.Tooltip", "Each mayor campaign action uses the current mayor campaign action cost.")}>
            <strong>{t("UI.Each", "{amount} each", { amount: formatAmount(panel.bribeCost || 5000000) })}</strong>
          </Tooltip>
        </div>
        <div className={styles.mayorActionList}>
          {mayorActions.map((action) => {
            const actionEnabled = activeCandidateField && !action.disabled;
            const disabledReason = getActionUnavailableReason(action.kind);
            const tooltip = actionEnabled
              ? action.description
              : disabledReason;
            const isPicking = pickerActionKind === action.kind;
            let picker: ReactElement | null = null;
            if (isPicking && isCandidateTargetAction(action.kind)) {
              const targetActionKind = action.kind;
              picker = (
                <CandidateActionPicker
                  action={action}
                  candidates={candidateTargets}
                  onClose={() => setPickerActionKind(null)}
                  onSelect={(candidateIndex) => runCandidateAction(targetActionKind, candidateIndex)}
                />
              );
            }

            return (
              <div className={styles.mayorActionGroup} key={action.kind}>
                <article className={`${styles.mayorActionCard} ${isPicking ? styles.mayorActionCardActive : ""}`}>
                  <div className={styles.mayorActionInfo}>
                    <Tooltip tooltip={action.description}>
                      <div>
                        <span className={styles.mayorActionTitle}>{action.title}</span>
                        <span className={styles.mayorActionDescription}>{action.description}</span>
                      </div>
                    </Tooltip>
                    {action.status && (
                      <Tooltip tooltip={t("UI.Mayor.Actions.Status.Tooltip", "Current state of this mayor campaign action.")}>
                        <strong className={styles.mayorActionStatus}>{action.status}</strong>
                      </Tooltip>
                    )}
                  </div>
                  <Tooltip tooltip={tooltip}>
                    <Button
                      variant="flat"
                      className={actionEnabled ? styles.mayorActionButton : `${styles.mayorActionButton} ${styles.bribeButtonDisabled}`}
                      disabled={!actionEnabled}
                      aria-disabled={!actionEnabled}
                      onSelect={() => {
                        if (actionEnabled) {
                          if (action.requiresCandidate && isCandidateTargetAction(action.kind)) {
                            setPickerActionKind(isPicking ? null : action.kind);
                          } else if (action.kind === "cashAssistance") {
                            trigger(mod.id, "cashAssistance");
                            setPickerActionKind(null);
                          }
                        }
                      }}
                    >
                      <strong>{actionEnabled ? action.buttonLabel : t("UI.Unavailable", "Unavailable")}</strong>
                    </Button>
                  </Tooltip>
                </article>
                {picker}
              </div>
            );
          })}
        </div>
      </div>
    </section>
  );
}

function isCandidateTargetAction(kind: MayorActionKind): kind is CandidateTargetActionKind {
  return kind === "bribe" || kind === "endorse" || kind === "tamper";
}

function CandidateActionPicker(props: {
  action: MayorCampaignAction;
  candidates: Candidate[];
  onClose: () => void;
  onSelect: (candidateIndex: number) => void;
}): ReactElement {
  const { action, candidates, onClose, onSelect } = props;

  return (
    <div className={styles.candidatePickerPanel} role="dialog">
      <div className={styles.candidatePickerHeader}>
        <div>
          <span className={styles.candidatePickerTitle}>{getCandidatePickerTitle(action.kind)}</span>
          <span className={styles.candidatePickerDescription}>{action.description}</span>
        </div>
        <Tooltip tooltip={t("UI.Mayor.Picker.Close.Tooltip", "Close candidate picker.")}>
          <Button
            variant="flat"
            className={styles.candidatePickerClose}
            onSelect={onClose}
          >
            <strong>{t("UI.Close", "X")}</strong>
          </Button>
        </Tooltip>
      </div>
      <div className={styles.candidatePickerList}>
        {candidates.length > 0 ? candidates.map((candidate) => (
          <article className={styles.candidatePickerCard} key={candidate.index}>
            <img
              className={styles.candidatePickerPortrait}
              src={candidate.portrait || icon}
              alt=""
            />
            <div className={styles.candidatePickerInfo}>
              <span className={styles.candidatePickerName}>{candidate.name}</span>
              <span className={styles.candidatePickerMeta}>
                {candidate.tagName || t("UI.Mayor.Picker.CandidateFallback", "Candidate")} - {candidate.effectName || t("UI.Mayor.Picker.NoPlatform", "No platform")}
              </span>
            </div>
            <Button
              variant="flat"
              className={styles.candidatePickerButton}
              onSelect={() => onSelect(candidate.index)}
            >
              <strong>{action.targetButtonLabel || action.buttonLabel}</strong>
            </Button>
          </article>
        )) : (
          <div className={styles.candidatePickerEmpty}>{t("UI.Mayor.Picker.Empty", "No active candidates right now.")}</div>
        )}
      </div>
    </div>
  );
}

function getCandidatePickerTitle(kind: MayorActionKind): string {
  switch (kind) {
    case "bribe":
      return t("UI.Mayor.Picker.Title.Bribe", "Choose platform meeting target");
    case "endorse":
      return t("UI.Mayor.Picker.Title.Endorse", "Choose endorsement target");
    case "tamper":
      return t("UI.Mayor.Picker.Title.Tamper", "Choose vote-count target");
    default:
      return t("UI.Mayor.Picker.Title.Candidate", "Choose candidate");
  }
}

function PartiesSection(props: { panel: ElectionPanel; onEditorOpenChange: (open: boolean) => void }): ReactElement {
  const parties = Array.isArray(props.panel.parties) ? props.panel.parties : [];
  const partyTags = Array.isArray(props.panel.partyTags) ? props.panel.partyTags : [];
  const [editingPartyIndex, setEditingPartyIndex] = useState<number | null>(null);
  const editingParty = editingPartyIndex === null
    ? undefined
    : parties.find((party) => party.index === editingPartyIndex);
  const editorOpen = !!editingParty;

  useEffect(() => {
    if (editingPartyIndex !== null && !parties.some((party) => party.index === editingPartyIndex)) {
      setEditingPartyIndex(null);
    }
  }, [editingPartyIndex, parties]);

  useEffect(() => {
    props.onEditorOpenChange(editorOpen);
  }, [editorOpen, props.onEditorOpenChange]);

  useEffect(() => () => props.onEditorOpenChange(false), [props.onEditorOpenChange]);

  if (parties.length === 0) {
    return <div className={styles.notice}>{t("UI.Notice.NoParties", "No parties are active right now.")}</div>;
  }

  return (
    <section className={editorOpen ? `${styles.partyPanel} ${styles.partyPanelEditing}` : styles.partyPanel}>
      <div className={styles.partyGrid}>
        {parties.map((party) => (
          <PartyCard party={party} key={party.index} onEditTags={setEditingPartyIndex} />
        ))}
      </div>
      {editingParty && (
        <PartyTagEditorPanel
          party={editingParty}
          partyTags={partyTags}
          onCancel={() => setEditingPartyIndex(null)}
        />
      )}
    </section>
  );
}

function PartyCard(props: { party: Party; onEditTags: (partyIndex: number) => void }): ReactElement {
  const { party } = props;
  const ColorField = resolveVanillaColorField();
  const [name, setName] = useState(party.name);
  const [color, setColor] = useState(normalizeHexColor(party.color) || getCandidateColor(party.index));
  const displayColor = normalizeHexColor(color) || getCandidateColor(party.index);

  useEffect(() => {
    setName(party.name);
    setColor(normalizeHexColor(party.color) || getCandidateColor(party.index));
  }, [party.index, party.name, party.color]);

  const saveDisabled = name.trim() === party.name && normalizeHexColor(color) === normalizeHexColor(party.color);

  return (
    <article className={styles.partyCard} style={{ borderColor: displayColor, boxShadow: `inset 0 0 0 1rem ${hexToRgba(displayColor, 0.18)}` }}>
      <div className={styles.partyCardHeader}>
        <div className={styles.partyColorPickerFrame} style={{ borderColor: hexToRgba(displayColor, 0.6) }}>
          {ColorField ? (
            <ColorField
              value={hexToColor(displayColor)}
              alpha={false}
              className={styles.partyColorField}
              onChange={(nextColor) => setColor(colorToHex(nextColor))}
            />
          ) : (
            <input
              className={styles.partyColorFallbackInput}
              value={color}
              maxLength={7}
              onChange={(event) => setColor(event.currentTarget.value)}
            />
          )}
        </div>
        <div className={styles.partyTitleBlock}>
          <input
            className={styles.partyNameInput}
            value={name}
            maxLength={48}
            onChange={(event) => setName(event.currentTarget.value)}
          />
        </div>
        <Tooltip tooltip={saveDisabled ? t("UI.Party.Save.Tooltip.Disabled", "No party changes to save.") : t("UI.Party.Save.Tooltip.Enabled", "Save this party name and color.")}>
          <Button
            variant="flat"
            className={saveDisabled ? `${styles.partySaveButton} ${styles.partySaveButtonDisabled}` : styles.partySaveButton}
            disabled={saveDisabled}
            onSelect={() => {
              if (saveDisabled) {
                return;
              }

              trigger(mod.id, "renameParty", party.index, name.trim());
              trigger(mod.id, "setPartyColor", party.index, normalizeHexColor(color) || displayColor);
            }}
          >
            <strong>{t("UI.Save", "Save")}</strong>
          </Button>
        </Tooltip>
      </div>

      <div className={styles.partyStatsLine}>
        <Tooltip tooltip={t("UI.Party.Reputation.Tooltip", "Persistent party reputation from 0 to 100.")}>
          <span><strong>{t("UI.Party.Reputation.Label", "Reputation:")}</strong> {party.reputation}</span>
        </Tooltip>
        <Tooltip tooltip={t("UI.Party.Wins.Tooltip", "Total elections won by this party.")}>
          <span><strong>{t("UI.Party.Wins.Label", "Wins:")}</strong> {party.wins}</span>
        </Tooltip>
        <Tooltip tooltip={t("UI.Party.Terms.Tooltip", "Current consecutive terms in power.")}>
          <span><strong>{t("UI.Party.Terms.Label", "Terms:")}</strong> {party.consecutiveTerms}</span>
        </Tooltip>
      </div>

      <div className={styles.partyTagList}>
        {party.tags.map((tag) => (
          <div className={styles.partyTagRow} key={`${party.index}-${tag.id}`}>
            <PartyTagBadge tag={tag} />
          </div>
        ))}
      </div>

      <Tooltip tooltip={party.canReplaceTag ? t("UI.Party.Replace.Tooltip.Enabled", "Open the party tag editor.") : party.replacementDisabledReason || t("UI.Party.Replace.Tooltip.Disabled", "Party tag replacement is unavailable right now.")}>
        <Button
          variant="flat"
          className={party.canReplaceTag ? styles.partyReplaceAllButton : `${styles.partyReplaceAllButton} ${styles.partySaveButtonDisabled}`}
          disabled={!party.canReplaceTag}
          onSelect={() => {
            if (party.canReplaceTag) {
              props.onEditTags(party.index);
            }
          }}
        >
          <strong>{t("UI.Party.Replace", "Replace Tags")}</strong>
        </Button>
      </Tooltip>
    </article>
  );
}

function PartyTagEditorPanel(props: { party: Party; partyTags: PartyTag[]; onCancel: () => void }): ReactElement {
  const { party, partyTags } = props;
  const availableTags = partyTags.filter((tag) => tag.id > 0);
  const initialTagIds = party.tags.map((tag) => tag.id).filter((id) => id > 0);
  const [selectedIds, setSelectedIds] = useState<number[]>(initialTagIds);
  const selectedTags = selectedIds
    .map((id) => findPartyTagById(availableTags, id))
    .filter((tag): tag is PartyTag => !!tag);
  const selectedTotal = selectedTags.reduce((total, tag) => total + tag.value, 0);
  const unchanged = haveSameTagSet(selectedIds, initialTagIds);
  const canSave = party.canReplaceTag && selectedIds.length === 3 && selectedTotal === 0 && !unchanged;
  const saveTooltip = !party.canReplaceTag
    ? party.replacementDisabledReason || t("UI.Party.TagEditor.Save.Tooltip.Disabled", "Party tag replacement is unavailable right now.")
    : selectedIds.length !== 3
      ? t("UI.Party.TagEditor.Save.Tooltip.Count", "Select exactly three party tags.")
      : selectedTotal !== 0
        ? t("UI.Party.TagEditor.Save.Tooltip.Total", "Party tag values must add up to zero.")
        : unchanged
          ? t("UI.Party.TagEditor.Save.Tooltip.Unchanged", "Choose a different party tag set.")
          : t("UI.Party.TagEditor.Save.Tooltip.Ready", "Save these party tags.");

  useEffect(() => {
    setSelectedIds(initialTagIds);
  }, [party.index]);

  const toggleTag = (tag: PartyTag) => {
    setSelectedIds((current) => {
      if (current.includes(tag.id)) {
        return current.filter((id) => id !== tag.id);
      }

      if (current.length >= 3) {
        return current;
      }

      return [...current, tag.id];
    });
  };

  return (
    <section className={styles.partyTagEditorPanel}>
      <div className={styles.partyTagEditorHeader}>
        <div>
          <strong>{party.name}</strong>
          <span>{t("UI.Party.TagEditor.Instructions", "Select three party tags. The total must be 0.")}</span>
        </div>
        <div className={styles.partyTagEditorTotal}>
          <span>{t("UI.Total", "Total")}</span>
          <strong className={tagTotalClass(selectedTotal)}>{formatTagValue(selectedTotal)}</strong>
        </div>
      </div>

      <div className={styles.partyTagEditorSelectedList}>
        {selectedTags.map((tag) => (
          <Button
            variant="flat"
            className={`${styles.partySelectedTagButton} ${tagValueClass(tag.value)}`}
            key={`selected-${tag.id}`}
            onSelect={() => toggleTag(tag)}
          >
            <span>{tag.name}</span>
            <strong>{formatTagValue(tag.value)}</strong>
          </Button>
        ))}
        {Array.from({ length: Math.max(0, 3 - selectedTags.length) }).map((_, index) => (
          <div className={styles.partySelectedTagPlaceholder} key={`placeholder-${index}`}>
            {t("UI.Empty", "Empty")}
          </div>
        ))}
      </div>

      <div className={styles.partyTagOptionGrid}>
        {availableTags.map((tag) => {
          const selected = selectedIds.includes(tag.id);
          const full = selectedIds.length >= 3 && !selected;
          return (
            <Tooltip tooltip={full ? t("UI.Party.TagEditor.Full.Tooltip", "Remove a selected tag before choosing another.") : tag.description || tag.name} key={tag.id}>
              <Button
                variant="flat"
                className={`${styles.partyTagOption} ${tagValueClass(tag.value)} ${selected ? styles.partyTagOptionSelected : ""} ${full ? styles.partyTagOptionBlocked : ""}`}
                onSelect={() => {
                  if (!full) {
                    toggleTag(tag);
                  }
                }}
              >
                <span>{tag.name}</span>
                <strong>{formatTagValue(tag.value)}</strong>
              </Button>
            </Tooltip>
          );
        })}
      </div>

      <div className={styles.partyTagEditorActions}>
        <Button variant="flat" className={styles.partyTagEditorButton} onSelect={props.onCancel}>
          <strong>{t("UI.Cancel", "Cancel")}</strong>
        </Button>
        <Tooltip tooltip={saveTooltip}>
          <Button
            variant="flat"
            className={canSave ? styles.partyTagEditorButton : `${styles.partyTagEditorButton} ${styles.partySaveButtonDisabled}`}
            disabled={!canSave}
            onSelect={() => {
              if (canSave) {
                trigger(mod.id, "setPartyTags", party.index, selectedIds.join(","));
                props.onCancel();
              }
            }}
          >
            <strong>{t("UI.Save", "Save")}</strong>
          </Button>
        </Tooltip>
      </div>
    </section>
  );
}

function PartyTagBadge(props: { tag: PartyTag }): ReactElement {
  const { tag } = props;
  const valueText = formatTagValue(tag.value);
  return (
    <Tooltip tooltip={tag.description || tag.name}>
      <div className={`${styles.partyTagBadge} ${tagToneClass(tag.tone)}`}>
        <span>{tag.name}</span>
        <strong>{valueText}</strong>
      </div>
    </Tooltip>
  );
}

function findPartyTagById(tags: PartyTag[], id: number): PartyTag | undefined {
  return tags.find((tag) => tag.id === id);
}

function formatTagValue(value: number): string {
  return value > 0 ? `+${value}` : `${value}`;
}

function haveSameTagSet(first: number[], second: number[]): boolean {
  if (first.length !== second.length) {
    return false;
  }

  return first.every((id) => second.includes(id));
}

function tagValueClass(value: number): string {
  if (value > 0) {
    return styles.partyTagValuePositive;
  }

  if (value < 0) {
    return styles.partyTagValueNegative;
  }

  return styles.partyTagValueNeutral;
}

function tagTotalClass(value: number): string {
  if (value === 0) {
    return styles.partyTagTotalBalanced;
  }

  return value > 0 ? styles.partyTagTotalPositive : styles.partyTagTotalNegative;
}

function CandidateCard(props: {
  candidate: Candidate;
  donationTiers: DonationTier[];
  donationsOpen: boolean;
  canDonate: boolean;
  donationUsedToday: boolean;
}): ReactElement {
  const { candidate, donationTiers, donationsOpen, donationUsedToday } = props;
  const canDonate = props.canDonate && candidate.exists;
  const tiers = Array.isArray(donationTiers) ? donationTiers : [];
  const donationTier = tiers[0];
  const donationCost = candidate.donationCost || donationTier?.amount || 0;
  const displayColor = getCandidateDisplayColor(candidate);
  const donationTooltip = !candidate.exists
    ? t("UI.Candidate.Donation.Tooltip.NoCandidate", "No candidate is available for donations.")
    : donationUsedToday
      ? t("UI.Candidate.Donation.Tooltip.UsedToday", "Only one campaign donation can be made per day.")
      : !donationsOpen
        ? t("UI.Candidate.Donation.Tooltip.Closed", "Donations are available when an active campaign has selected candidates before election day.")
        : t("UI.Candidate.Donation.Tooltip.Ready", "Donate {amount} of city funds to support this candidate's campaign before election day.", { amount: formatAmount(donationCost) });

  return (
    <article
      className={styles.candidateCard}
      style={{
        borderColor: getCandidateBorderColor(candidate),
        boxShadow: `inset 0 0 0 1rem ${getCandidateShadowColor(candidate)}`,
      }}
    >
      <div className={styles.candidateHeader}>
        <Tooltip tooltip={t("UI.Candidate.Portrait.Tooltip", "Candidate portrait. Candidates are selected from real adult residents.")}>
          <img
            className={styles.portrait}
            src={candidate.portrait || icon}
            alt=""
          />
        </Tooltip>
        <div className={styles.candidateTitle}>
          <Tooltip tooltip={t("UI.Candidate.Name.Tooltip", "Click the candidate name to move the camera to this citizen.")}>
            <Button
              variant="flat"
              className={styles.candidateNameLink}
              disabled={!candidate.canFocus}
              onSelect={() => {
                if (candidate.canFocus) {
                  trigger(mod.id, "focusCandidate", candidate.index);
                }
              }}
            >
              {candidate.name}
            </Button>
          </Tooltip>
          {candidate.bio && (
            <Tooltip tooltip={t("UI.Candidate.Bio.Tooltip", "Candidate background generated from the resident's current life in the city.")}>
              <div className={styles.candidateBio}>{candidate.bio}</div>
            </Tooltip>
          )}
          {candidate.partyName && (
            <Tooltip tooltip={t("UI.Mayor.PartyReputation.Tooltip", "Party reputation: {reputation}/100.", { reputation: candidate.partyReputation })}>
              <div className={styles.candidatePartyLine}>
                <span className={styles.partySwatchSmall} style={{ backgroundColor: displayColor }} />
                <span>{candidate.partyName}</span>
              </div>
            </Tooltip>
          )}
        </div>
      </div>

      {candidate.tagName && (
        <Tooltip tooltip={candidate.tagDescription || candidate.tagName}>
          <div className={`${styles.candidateTag} ${tagToneClass(candidate.tagTone)}`}>
            <span>{candidate.tagName}</span>
            <strong>{candidate.tagDescription}</strong>
          </div>
        </Tooltip>
      )}

      <Tooltip tooltip={t("UI.Candidate.DonationTotal.Tooltip", "Effective campaign support credited to this candidate during the current campaign. Some tags can change cost or campaign effect.")}>
        <div className={styles.donationTotal}>
          <span>{t("UI.Candidate.DonationTotal", "Total donations")}</span>
          <strong>{formatAmount(candidate.donationAmount)}</strong>
        </div>
      </Tooltip>

      <Tooltip tooltip={t("UI.Candidate.Platform.Tooltip", "Candidate platform. The positive effect is shown first, followed by the tradeoff.")}>
        <div className={styles.platform}>
          <span>{t("UI.Candidate.Platform", "Candidate Platform")}</span>
          <PlatformImpactList impacts={candidate.platformImpacts ?? []} />
        </div>
      </Tooltip>

      <div className={styles.donationBlock}>
        <div className={styles.donationHeader}>
          <Tooltip tooltip={t("UI.Candidate.DonationHeader.Tooltip", "City-funded campaign support. Donations are available while the mayoral race is active before election day.")}>
            <span>{t("UI.Candidate.DonationHeader", "Campaign donation")}</span>
          </Tooltip>
        </div>
        {donationTier && (
          <Tooltip
            tooltip={donationTooltip}
          >
            <Button
              variant="flat"
              className={canDonate ? styles.tierButton : `${styles.tierButton} ${styles.tierButtonDisabled}`}
              disabled={!canDonate}
              aria-disabled={!canDonate}
              onSelect={() => {
                if (canDonate) {
                  trigger(mod.id, "donate", candidate.index, donationTier.index);
                }
              }}
            >
              <strong>{t("UI.Candidate.Donate", "Donate {amount}", { amount: formatAmount(donationCost) })}</strong>
            </Button>
          </Tooltip>
        )}
        {!donationsOpen && (
          <Tooltip tooltip={t("UI.Candidate.Donation.Tooltip.Closed", "Donations are available when an active campaign has selected candidates before election day.")}>
            <div className={styles.helpText}>{t("UI.Candidate.DonationsClosed.Help", "Donations open before election day once candidates are selected.")}</div>
          </Tooltip>
        )}
        {donationsOpen && donationUsedToday && (
          <Tooltip tooltip={t("UI.Candidate.Donation.Tooltip.UsedToday", "Only one campaign donation can be made per day.")}>
            <div className={styles.helpText}>{t("UI.Candidate.DonationUsed.Help", "A campaign donation has already been made today.")}</div>
          </Tooltip>
        )}
      </div>
    </article>
  );
}

function PlatformImpactList(props: { impacts: PlatformImpact[] }): ReactElement {
  const impacts = Array.isArray(props.impacts) ? props.impacts : [];

  return (
    <div className={styles.platformImpactList}>
      {impacts.map((impact, index) => (
        <div className={styles.platformImpact} key={`${impact.label}-${index}`}>
          <strong className={impact.positive ? styles.platformValuePositive : styles.platformValueNegative}>
            {impact.value}
          </strong>
          <span>{impact.label}</span>
        </div>
      ))}
    </div>
  );
}

function tagToneClass(tone: string): string {
  if (tone === "Advantage") {
    return styles.candidateTagPositive;
  }

  if (tone === "Disadvantage") {
    return styles.candidateTagNegative;
  }

  if (tone === "Mixed") {
    return styles.candidateTagMixed;
  }

  return styles.candidateTagNeutral;
}

function getPollCandidateResults(poll: PollResult, candidates: Candidate[]): PollCandidateResult[] {
  const sourceResults = Array.isArray(poll.candidateResults) ? poll.candidateResults : [];
  const activeCandidates = Array.isArray(candidates) ? candidates : [];

  if (activeCandidates.length > 0) {
    return activeCandidates.map((candidate) => {
      const result = sourceResults.find((item) => item.index === candidate.index);
      return {
        index: candidate.index,
        name: candidate.name || result?.name || getCandidateFallbackName(candidate.index),
        votes: result?.votes ?? getLegacyPollVotes(poll, candidate.index),
        percent: result?.percent ?? getLegacyPollPercent(poll, candidate.index),
      };
    });
  }

  if (sourceResults.length > 0) {
    return sourceResults;
  }

  return [
    {
      index: 0,
      name: getCandidateFallbackName(0),
      votes: poll.votesA,
      percent: poll.percentA,
    },
    {
      index: 1,
      name: getCandidateFallbackName(1),
      votes: poll.votesB,
      percent: poll.percentB,
    },
  ];
}

function getLegacyPollVotes(poll: PollResult, candidateIndex: number): number {
  switch (candidateIndex) {
    case 0:
      return poll.votesA;
    case 1:
      return poll.votesB;
    case 2:
      return poll.votesC;
    case 3:
      return poll.votesD;
    default:
      return 0;
  }
}

function getLegacyPollPercent(poll: PollResult, candidateIndex: number): number {
  switch (candidateIndex) {
    case 0:
      return poll.percentA;
    case 1:
      return poll.percentB;
    case 2:
      return poll.percentC;
    case 3:
      return poll.percentD;
    default:
      return 0;
  }
}

function PollRow(props: {
  label: string;
  value: number;
  color: string;
}): ReactElement {
  const width = Math.max(0, Math.min(100, props.value));

  return (
    <div className={styles.pollRow}>
      <Tooltip tooltip={t("UI.Poll.Row.Tooltip", "Poll support for {label}. This is based on the sampled residents, not final election turnout.", { label: props.label })}>
        <div className={styles.pollRowTop}>
          <div className={styles.pollCandidate}>
            <span className={styles.pollSwatch} style={{ backgroundColor: props.color }} />
            <span className={styles.pollName}>{props.label}</span>
          </div>
          <div className={styles.pollStats}>
            <div className={styles.pollPercent}>
              <div className={styles.pollPercentNumber}>{props.value}</div>
              <div className={styles.pollPercentSymbol}>%</div>
            </div>
          </div>
        </div>
      </Tooltip>
      <div className={styles.pollTrack}>
        <div className={styles.pollFill} style={{ width: `${width}%`, backgroundColor: props.color }} />
      </div>
    </div>
  );
}

function formatAmount(value: number): string {
  return Math.round(value || 0)
    .toString()
    .replace(/\B(?=(\d{3})+(?!\d))/g, ",");
}

function normalizePanel(panel: ElectionPanel | undefined | null): ElectionPanel {
  const source = panel ?? defaultPanel;
  const candidateA = normalizeCandidate(source.candidateA ?? defaultPanel.candidateA, 0);
  const candidateB = normalizeCandidate(source.candidateB ?? defaultPanel.candidateB, 1);
  const candidates = Array.isArray(source.candidates) && source.candidates.length > 0
    ? source.candidates.map((candidate, index) => normalizeCandidate(candidate, index))
    : [candidateA, candidateB];
  const parties = Array.isArray(source.parties)
    ? source.parties.map((party, index) => normalizeParty(party, index))
    : [];
  const partyTags = Array.isArray(source.partyTags)
    ? source.partyTags.map((tag, index) => normalizePartyTag(tag, index))
    : [];
  const poll = {
    ...defaultPanel.poll,
    ...(source.poll ?? {}),
  };
  poll.candidateResults = Array.isArray(poll.candidateResults) ? poll.candidateResults : [];
  poll.ageGroups = Array.isArray(poll.ageGroups) ? poll.ageGroups : [];
  poll.educationGroups = Array.isArray(poll.educationGroups) ? poll.educationGroups : [];
  poll.incomeGroups = Array.isArray(poll.incomeGroups) ? poll.incomeGroups : [];

  return {
    ...defaultPanel,
    ...source,
    poll,
    candidates,
    candidateA,
    candidateB,
    parties,
    partyTags,
    mayorHome: {
      ...defaultPanel.mayorHome,
      ...(source.mayorHome ?? {}),
    },
    mayorWorkplace: {
      ...defaultPanel.mayorWorkplace,
      ...(source.mayorWorkplace ?? {}),
    },
    mayorHomeChoices: Array.isArray(source.mayorHomeChoices) ? source.mayorHomeChoices : [],
    mayorWorkplaceChoices: Array.isArray(source.mayorWorkplaceChoices) ? source.mayorWorkplaceChoices : [],
    mayorHomeChoicesLimited: !!source.mayorHomeChoicesLimited,
    mayorWorkplaceChoicesLimited: !!source.mayorWorkplaceChoicesLimited,
    mayorSelectedBuilding: {
      ...defaultPanel.mayorSelectedBuilding,
      ...(source.mayorSelectedBuilding ?? {}),
    },
    donationTiers: Array.isArray(source.donationTiers) ? source.donationTiers : [],
    supportPrograms: Array.isArray(source.supportPrograms) ? source.supportPrograms : [],
    legislation: Array.isArray(source.legislation) ? source.legislation : [],
    mayorPlatformImpacts: Array.isArray(source.mayorPlatformImpacts) ? source.mayorPlatformImpacts : [],
  };
}

export default register;
