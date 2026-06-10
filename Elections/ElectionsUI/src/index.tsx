import { type ModRegistrar } from "cs2/modding";
import { bindValue, trigger, useValue } from "cs2/api";
import { Button, Dropdown, DropdownItem, DropdownToggle, Icon, Tooltip } from "cs2/ui";
import { type ReactElement, useEffect, useMemo, useState } from "react";

import icon from "images/elections.svg";
import mod from "../mod.json";
import styles from "./mods/ElectionsMenu.module.scss";

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

type PlatformImpact = {
  value: string;
  label: string;
  positive: boolean;
};

type Candidate = {
  index: number;
  exists: boolean;
  name: string;
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

type PollResult = {
  sampleSize: number;
  votesA: number;
  votesB: number;
  undecided: number;
  percentA: number;
  percentB: number;
  percentUndecided: number;
  marginOfError: number;
  leaderIndex: number;
  withinMargin: boolean;
  resultLabel: string;
  resultDescription: string;
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
  hasState: boolean;
  waitingForPopulation: boolean;
  currentPopulation: number;
  minimumPopulation: number;
  populationReady: boolean;
  currentDate: string;
  stage: string;
  stageLabel: string;
  cycleLabel: string;
  pollReleased: boolean;
  donationsOpen: boolean;
  bribesOpen: boolean;
  canBribe: boolean;
  canEndorse: boolean;
  endorsementUsed: boolean;
  endorsedCandidateIndex: number;
  canTamper: boolean;
  voteTamperingScheduled: boolean;
  voteTamperingCandidateIndex: number;
  canProposeVotingId: boolean;
  votingIdLawPassed: boolean;
  votingIdProposalPending: boolean;
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
  pollDate: string;
  electionDate: string;
  votingStartTime: string;
  votingEndTime: string;
  resultsTime: string;
  poll: Poll;
  donationTiers: DonationTier[];
  candidateA: Candidate;
  candidateB: Candidate;
  mayorName: string;
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

type PanelSection = "votingSites" | "mayor" | "residence" | "schedule" | "programs" | "candidates";
type CandidateTargetActionKind = "bribe" | "endorse" | "tamper";
type MayorActionKind = CandidateTargetActionKind | "cashAssistance" | "votingId";
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
  portrait: "",
  canFocus: false,
  bio: "",
  tagName: "",
  tagDescription: "",
  tagTone: "Neutral",
  effectName: "No platform",
  effectDescription: "No candidate has been selected yet.",
  platformImpacts: [],
  donationAmount: 0,
  donationCost: 0,
  donationBonusPercent: 0,
  donated: false,
});

const emptyMayorBuildingTarget = (name: string): MayorBuildingTarget => ({
  exists: false,
  name,
  entityLabel: "Entity.Null",
  capacity: 0,
  occupants: 0,
  currentName: "Unknown",
  currentEntityLabel: "Entity.Null",
  currentExists: false,
  atTarget: false,
  canFocus: false,
});

const emptyMayorSelectedBuilding: MayorSelectedBuilding = {
  exists: false,
  name: "No building selected",
  entityLabel: "Entity.Null",
  canBeHome: false,
  canBeWorkplace: false,
  isHomeTarget: false,
  isWorkplaceTarget: false,
};

const defaultPanel: ElectionPanel = {
  enabled: true,
  hasState: false,
  waitingForPopulation: false,
  currentPopulation: 0,
  minimumPopulation: 1000,
  populationReady: true,
  currentDate: "",
  stage: "None",
  stageLabel: "No election data",
  cycleLabel: "The election system has not initialized yet.",
  pollReleased: false,
  donationsOpen: false,
  bribesOpen: false,
  canBribe: false,
  canEndorse: false,
  endorsementUsed: false,
  endorsedCandidateIndex: -1,
  canTamper: false,
  voteTamperingScheduled: false,
  voteTamperingCandidateIndex: -1,
  canProposeVotingId: false,
  votingIdLawPassed: false,
  votingIdProposalPending: false,
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
  pollDate: "",
  electionDate: "",
  votingStartTime: "08:00",
  votingEndTime: "17:00",
  resultsTime: "20:00",
  poll: {
    sampleSize: 0,
    votesA: 0,
    votesB: 0,
    undecided: 0,
    percentA: 0,
    percentB: 0,
    percentUndecided: 0,
    marginOfError: 0,
    leaderIndex: -1,
    withinMargin: false,
    resultLabel: "",
    resultDescription: "",
    ageGroups: [],
    educationGroups: [],
    incomeGroups: [],
  },
  donationTiers: [],
  candidateA: emptyCandidate(0, "Candidate A"),
  candidateB: emptyCandidate(1, "Candidate B"),
  mayorName: "",
  mayorPortrait: "",
  mayorCanFocus: false,
  mayorEffectName: "",
  mayorEffectDescription: "",
  mayorTagName: "",
  mayorTagDescription: "",
  mayorTagTone: "Neutral",
  mayorPlatformImpacts: [],
  mayorTemporary: false,
  mayorHome: emptyMayorBuildingTarget("No low-density residence selected"),
  mayorWorkplace: emptyMayorBuildingTarget("No City Hall selected"),
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

const candidateAColor = "#b16cff";
const candidateBColor = "#62d26f";
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
  return panel.enabled &&
    !panel.waitingForPopulation &&
    panel.candidateA?.exists &&
    panel.candidateB?.exists &&
    (panel.stage === "CandidatesSelected" || panel.stage === "PollReleased" || panel.stage === "Voting");
}

export const register: ModRegistrar = (moduleRegistry) => {
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
    <Tooltip tooltip="Open the Elections panel. Shows the current mayor, active race, poll status, and campaign donation controls before election day.">
      <Button
        variant="floating"
        selected={open}
        aria-label="Open Elections"
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
  const candidates = useMemo(
    () => [panel.candidateA ?? defaultPanel.candidateA, panel.candidateB ?? defaultPanel.candidateB],
    [panel.candidateA, panel.candidateB]
  );

  if (!open) {
    return null;
  }

  const votingSitesEnabled = panel.enabled && !panel.waitingForPopulation;
  const menuItems: Array<{ section: PanelSection; label: string; title: string; tooltip: string; disabled?: boolean }> = [
    {
      section: "votingSites",
      label: "View voting sites",
      title: "Voting sites",
      tooltip: votingSitesEnabled
        ? "Show voting locations on the map."
        : "Voting locations unlock when Elections are enabled and the city reaches the minimum population.",
      disabled: !votingSitesEnabled,
    },
    {
      section: "mayor",
      label: "Current mayor panel",
      title: "Current mayor",
      tooltip: "Show the current mayor and mayoral actions.",
    },
    {
      section: "residence",
      label: "Mayor residence",
      title: "Mayor residence",
      tooltip: "Set the mayor's home and City Hall workplace from the selected building.",
    },
    {
      section: "schedule",
      label: "Election schedule",
      title: "Election schedule",
      tooltip: "Show election dates, voting hours, results time, and poll status.",
    },
    {
      section: "programs",
      label: "Civic programs",
      title: "Civic programs",
      tooltip: "Fund turnout support programs before election day.",
    },
    {
      section: "candidates",
      label: "Candidates",
      title: "Candidates",
      tooltip: "Show the mayoral candidates and campaign donation controls.",
    },
  ];
  const activeItem = menuItems.find((item) => item.section === activeSection) ?? menuItems[0];
  const subPanelClass = activeSection === "candidates"
    ? styles.subPanelWide
    : activeSection === "mayor"
      ? styles.subPanelMayor
    : activeSection === "residence"
      ? styles.subPanelMedium
    : activeSection === "schedule"
      ? styles.subPanelMedium
    : activeSection === "programs"
      ? styles.subPanelPrograms
      : styles.subPanelNarrow;

  return (
    <div className={styles.menuCluster}>
      <nav draggable className={styles.sideMenu} aria-label="Election panels">
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
        <div className={styles.notice}>Elections are disabled in mod settings.</div>
      )}
      {panel.enabled && panel.waitingForPopulation && (
        <div className={styles.notice}>
          Elections will start when the city reaches {formatAmount(panel.minimumPopulation)} population. Current population: {formatAmount(panel.currentPopulation)}.
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
}): ReactElement {
  const { activeSection, panel, candidates, showVotingLocations } = props;

  if (activeSection === "mayor") {
    return panel.mayorName ? (
      <MayorSection panel={panel} />
    ) : (
      <div className={styles.notice}>No mayor has been selected yet.</div>
    );
  }

  if (activeSection === "residence") {
    return <MayorResidenceSection panel={panel} />;
  }

  if (activeSection === "schedule") {
    return <ScheduleSection panel={panel} />;
  }

  if (activeSection === "programs") {
    return <SupportProgramsPanel panel={panel} />;
  }

  if (activeSection === "candidates") {
    if (!hasActiveCandidateField(panel)) {
      return <div className={styles.notice}>There are no candidates right now.</div>;
    }

    return (
      <section className={styles.candidateStack}>
        {candidates.map((candidate) => (
          <CandidateCard
            key={candidate.index}
            candidate={candidate}
            donationTiers={panel.donationTiers ?? []}
            donationsOpen={panel.donationsOpen}
          />
        ))}
      </section>
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
        <Tooltip tooltip="Voting-site overlay status. On election day, map markers include live vote counts for each location.">
          <span>Voting sites</span>
        </Tooltip>
        <Tooltip tooltip={showVotingLocations ? "Voting-site overlay is visible." : "Voting-site overlay is hidden."}>
          <strong>{showVotingLocations ? "Overlay visible" : "Overlay hidden"}</strong>
        </Tooltip>
      </div>

      <div className={styles.votingSitesSummary}>
        <Tooltip tooltip="Election date and voting window for the active mayoral race.">
          <div className={styles.votingSitesStatus}>
            <span>Election</span>
            <strong>
              {panel.electionDate
                ? `${panel.electionDate} ${panel.votingStartTime || "08:00"}-${panel.votingEndTime || "17:00"}`
                : "Pending"}
            </strong>
          </div>
        </Tooltip>
        <Tooltip tooltip="Live vote counts appear only during the voting phase.">
          <div className={styles.votingSitesStatus}>
            <span>Map markers</span>
            <strong>{electionDay ? "Live results" : "Voting locations"}</strong>
          </div>
        </Tooltip>
      </div>

      <Tooltip tooltip={canShowLocations
        ? showVotingLocations
          ? "Hide voting locations on the map."
          : "Show voting locations on the map."
        : "Voting locations unlock when Elections are enabled and the city reaches the minimum population."}
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
            {showVotingLocations ? "Hide voting sites" : "View voting sites"}
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

function ScheduleSection(props: { panel: ElectionPanel }): ReactElement {
  const { panel } = props;
  const [pollTab, setPollTab] = useState<"overall" | "age" | "education" | "income">("overall");
  const votingWindow = panel.votingStartTime && panel.votingEndTime
    ? `${panel.votingStartTime}-${panel.votingEndTime}`
    : "Pending";
  const electionSchedule = panel.electionDate
    ? `${panel.electionDate} ${votingWindow}`
    : "Pending";
  const resultsSchedule = panel.electionDate
    ? `${panel.electionDate} ${panel.resultsTime || "20:00"}`
    : panel.resultsTime || "20:00";

  return (
    <section className={styles.scheduleCard}>
      <div className={styles.sectionHeader}>
        <Tooltip tooltip="Election schedule and current lifecycle phase.">
          <span>Election schedule</span>
        </Tooltip>
      </div>

      <Tooltip tooltip="Summary of the current election cycle and whether this is a regular or accelerated race.">
        <div className={styles.summaryText}>{panel.cycleLabel}</div>
      </Tooltip>

      <div className={styles.metaGrid}>
        <Info
          label="Phase"
          value={panel.stageLabel || "Pending"}
          tooltip="The current phase of the election cycle."
        />
        <Info
          label="Poll"
          value={panel.pollDate ? `${panel.pollDate} 08:00` : "Pending"}
          tooltip="The campaign poll is released at 08:00 on this date."
        />
        <Info
          label="Election"
          value={electionSchedule}
          tooltip="Election day and voting window for the active mayoral race."
        />
        <Info
          label="Results"
          value={resultsSchedule}
          tooltip="Election results announcement date and time."
        />
      </div>

      <div className={styles.pollBlock}>
        <div className={styles.pollHeader}>
          <Tooltip tooltip="Poll status. Before the poll is released this shows the scheduled release date; after release it shows sampled voter preferences.">
            <span>{panel.pollReleased ? "Current poll results" : "Poll scheduled"}</span>
          </Tooltip>
          {panel.pollReleased && (
            <Tooltip tooltip="Number of eligible residents sampled by the campaign poll.">
              <strong className={styles.pollSample}>
                <SampleSizeLabel value={panel.poll.sampleSize} />
              </strong>
            </Tooltip>
          )}
        </div>

        {panel.pollReleased ? (
          <>
            <div className={styles.pollTabs} role="tablist" aria-label="Poll breakdown">
              {[
                { key: "overall" as const, label: "Overall" },
                { key: "age" as const, label: "Age" },
                { key: "education" as const, label: "Education" },
                { key: "income" as const, label: "Income" },
              ].map((tab) => (
                <Tooltip tooltip={`Show ${tab.label.toLowerCase()} poll results.`} key={tab.key}>
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
                candidateAName={panel.candidateA.name}
                candidateBName={panel.candidateB.name}
              />
            )}
            {pollTab === "age" && (
              <PollBreakdownList
                groups={panel.poll.ageGroups}
                candidateAName={panel.candidateA.name}
                candidateBName={panel.candidateB.name}
              />
            )}
            {pollTab === "education" && (
              <PollBreakdownList
                groups={panel.poll.educationGroups}
                candidateAName={panel.candidateA.name}
                candidateBName={panel.candidateB.name}
              />
            )}
            {pollTab === "income" && (
              <PollBreakdownList
                groups={panel.poll.incomeGroups}
                candidateAName={panel.candidateA.name}
                candidateBName={panel.candidateB.name}
              />
            )}
          </>
        ) : (
          <div className={styles.notice}>
            {panel.pollDate
              ? `Poll releases on ${panel.pollDate} at 08:00.`
              : "Poll date is pending."}
          </div>
        )}
      </div>
    </section>
  );
}

function PollRows(props: {
  poll: PollResult;
  candidateAName: string;
  candidateBName: string;
}): ReactElement {
  const { poll, candidateAName, candidateBName } = props;

  return (
    <div className={styles.pollRows}>
      <PollRow
        label={candidateAName}
        value={poll.percentA}
        color={candidateAColor}
      />
      <PollRow
        label={candidateBName}
        value={poll.percentB}
        color={candidateBColor}
      />
      <PollRow
        label="Undecided"
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
      <span className={styles.sampleSizeWord}>sampled</span>
    </span>
  );
}

function PollResultView(props: {
  poll: PollResult;
  candidateAName: string;
  candidateBName: string;
}): ReactElement {
  const { poll, candidateAName, candidateBName } = props;

  return (
    <>
      <Tooltip tooltip={poll.resultDescription || "Poll read based on the sample and margin of error."}>
        <div className={styles.pollReadout}>
          <strong>{poll.resultLabel || "Poll released"}</strong>
          <span className={styles.pollReadoutMeta}>
            <SampleSizeLabel value={poll.sampleSize} />
            <span className={styles.pollMetaSeparator}>|</span>
            <span>+/-{poll.marginOfError}%</span>
          </span>
        </div>
      </Tooltip>
      <PollRows
        poll={poll}
        candidateAName={candidateAName}
        candidateBName={candidateBName}
      />
    </>
  );
}

function PollBreakdownList(props: {
  groups: PollBreakdown[];
  candidateAName: string;
  candidateBName: string;
}): ReactElement {
  const groups = Array.isArray(props.groups) ? props.groups : [];

  return (
    <div className={styles.pollBreakdownList}>
      {groups.map((group) => (
        <PollBreakdownCard
          key={group.key || group.label}
          group={group}
          candidateAName={props.candidateAName}
          candidateBName={props.candidateBName}
        />
      ))}
      {!groups.length && (
        <div className={styles.notice}>No poll breakdown data is available.</div>
      )}
    </div>
  );
}

function PollBreakdownCard(props: {
  group: PollBreakdown;
  candidateAName: string;
  candidateBName: string;
}): ReactElement {
  const { group, candidateAName, candidateBName } = props;

  return (
    <article className={styles.pollBreakdownCard}>
      <Tooltip tooltip={group.resultDescription || "Poll read for this sampled group."}>
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
        candidateAName={candidateAName}
        candidateBName={candidateBName}
      />
    </article>
  );
}

function SupportProgramsPanel(props: { panel: ElectionPanel }): ReactElement {
  const { panel } = props;
  const programs = Array.isArray(panel.supportPrograms) ? panel.supportPrograms : [];
  const fallbackReason = panel.stage === "Voting"
    ? "Civic programs are unavailable on election day."
    : panel.supportProgramUsedToday
      ? `Only one civic program can be funded per day. Today's program is ${panel.supportProgramUsedTodayLabel || "already selected"}.`
      : "Civic programs are available before election day once candidates are selected.";

  return (
    <section className={styles.supportProgramsPanel}>
      <div className={styles.sectionHeader}>
        <Tooltip tooltip="Civic programs can be funded once per day before election day.">
          <span>Civic programs</span>
        </Tooltip>
        <Tooltip tooltip="Each civic program costs half of the current campaign donation value.">
          <strong>{formatAmount(panel.supportProgramCost)} each</strong>
        </Tooltip>
      </div>

      {panel.supportProgramUsedToday && (
        <Tooltip tooltip="The daily civic program slot refreshes on the next calendar day.">
          <div className={styles.notice}>Today's program: {panel.supportProgramUsedTodayLabel || "selected"}</div>
        </Tooltip>
      )}

      <div className={styles.supportProgramList}>
        {programs.map((program) => {
          const status = program.index === 0
            ? program.active ? "Holiday scheduled" : "Not scheduled"
            : `Current bonus +${program.currentBonusPercent || 0}%`;
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
                <Tooltip tooltip={program.index === 0 ? "Holiday scheduling status." : "Accumulated election turnout bonus for this voter group."}>
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
                  <strong>{program.canRun ? "Fund" : program.active && program.index === 0 ? "Scheduled" : "Unavailable"}</strong>
                </Button>
              </Tooltip>
            </article>
          );
        })}
      </div>

      {!programs.length && (
        <div className={styles.notice}>Civic programs are unavailable right now.</div>
      )}
      {!panel.supportProgramsOpen && programs.length > 0 && (
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
        <Tooltip tooltip="The assigned residence and office are enforced for the current mayor. If the target is full, another resident or worker is removed first.">
          <span>Mayor assignments</span>
        </Tooltip>
        <Tooltip tooltip={home.atTarget && workplace.atTarget ? "The mayor is assigned to both selected targets." : "The mayor will be moved to the selected targets during the next assignment update."}>
          <strong>{home.atTarget && workplace.atTarget ? "Assigned" : "Relocating"}</strong>
        </Tooltip>
      </div>

      <Tooltip tooltip={selected.exists ? "Currently selected game building." : "No building is selected in the game UI."}>
        <div className={styles.selectedBuildingBox}>
          <span>Selected building</span>
          <strong>{selected.name}</strong>
          {selected.exists && (
            <div className={styles.selectedBuildingTags}>
              <span className={selected.canBeHome ? styles.targetTagActive : styles.targetTagMuted}>Home</span>
              <span className={selected.canBeWorkplace ? styles.targetTagActive : styles.targetTagMuted}>City Hall</span>
            </div>
          )}
        </div>
      </Tooltip>

      <MayorTargetCard
        kind="home"
        title="Mayor home"
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
        title="Mayor workplace"
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
    ? "Mayor assigned"
    : target.exists
      ? "Move pending"
      : "No target";
  const useDisabled = !selected.exists || !canUseSelected || alreadySelected;
  const useTooltip = alreadySelected
    ? "This building is already selected."
    : canUseSelected
      ? kind === "home"
        ? "Assign the mayor's household to this low-density residence."
        : "Assign the mayor to work at this City Hall."
      : kind === "home"
        ? "Selected building must be a low-density residential building."
        : "Selected building must be a City Hall asset.";

  return (
    <article className={styles.residenceTargetCard}>
      <div className={styles.residenceTargetHeader}>
        <div className={styles.residenceTargetTitle}>
          <Tooltip tooltip={kind === "home" ? "Saved mayor residence target." : "Saved mayor workplace target."}>
            <span>{title}</span>
          </Tooltip>
          <Tooltip tooltip={target.entityLabel}>
            <strong>{target.name}</strong>
          </Tooltip>
        </div>
        <Tooltip tooltip={target.atTarget ? "The mayor is already assigned here." : "The assignment system will move the mayor here."}>
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
          label="Current"
          value={target.currentExists ? target.currentName : "Unknown"}
          tooltip={target.currentEntityLabel}
        />
      </div>

      <div className={styles.residenceActionRow}>
        <Tooltip tooltip={target.canFocus ? "Move the camera to the selected mayor target." : "No target is available to focus."}>
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
            <strong>Focus</strong>
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
            <strong>{alreadySelected ? "Selected" : "Use selected"}</strong>
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
  const label = target.exists ? target.name : kind === "home" ? "Choose a residence" : "Choose a City Hall";
  const emptyLabel = kind === "home"
    ? "No low-density residences found"
    : "No City Hall assets found";
  const tooltip = kind === "home"
    ? "Choose the low-density residential building where the mayor should live."
    : "Choose the City Hall asset where the mayor should work.";

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
              More eligible buildings exist. Select one in the city to add it here.
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

function MayorSection(props: { panel: ElectionPanel }): ReactElement {
  const { panel } = props;
  const [pickerActionKind, setPickerActionKind] = useState<CandidateTargetActionKind | null>(null);
  const activeCandidateField = hasActiveCandidateField(panel);
  const candidateAName = panel.candidateA?.name || "Candidate A";
  const candidateBName = panel.candidateB?.name || "Candidate B";
  const candidateTargets = [panel.candidateA, panel.candidateB].filter((candidate): candidate is Candidate => Boolean(candidate?.exists));
  const endorsedName = panel.endorsedCandidateIndex === 0
    ? candidateAName
    : panel.endorsedCandidateIndex === 1
      ? candidateBName
      : "a candidate";
  const tamperBeneficiaryName = panel.voteTamperingCandidateIndex === 0
    ? candidateAName
    : panel.voteTamperingCandidateIndex === 1
      ? candidateBName
      : "a candidate";
  const bribeStatus = panel.bribeMeetingPending
    ? "Pending"
    : "";
  const votingIdStatus = panel.votingIdProposalPending
    ? "Pending"
    : panel.votingIdLawPassed
      ? "Passed"
      : "";
  const mayorActions: MayorCampaignAction[] = [
    {
      kind: "bribe" as const,
      title: "Platform meeting",
      description: "The mayor will attempt to convince a selected candidate to soften their platform.",
      buttonLabel: "Choose",
      targetButtonLabel: "Bribe",
      status: bribeStatus,
      disabled: !panel.canBribe,
      requiresCandidate: true,
    },
    {
      kind: "endorse" as const,
      title: "Mayor endorsement",
      description: "Spend city funds to have the mayor endorse a selected candidate.",
      buttonLabel: "Choose",
      targetButtonLabel: "Endorse",
      status: panel.endorsementUsed ? `Endorsed ${endorsedName}` : "",
      disabled: !panel.canEndorse || panel.endorsementUsed,
      requiresCandidate: true,
    },
    {
      kind: "cashAssistance" as const,
      title: panel.cashAssistanceTurnoutBonusPercent > 0
        ? "Cash Assistance funded"
        : "Cash Assistance",
      description: "Spend city funds to raise turnout for struggling and modest-income residents.",
      buttonLabel: "Fund",
      status: panel.cashAssistanceTurnoutBonusPercent > 0 ? `+${panel.cashAssistanceTurnoutBonusPercent}%` : "",
      disabled: !panel.canBribe || panel.cashAssistanceTurnoutBonusPercent > 0,
      requiresCandidate: false,
    },
    {
      kind: "tamper" as const,
      title: "Vote-count tampering",
      description: "Spend city funds to arrange a late election-day disruption for a selected candidate.",
      buttonLabel: "Choose",
      targetButtonLabel: "Tamper",
      status: panel.voteTamperingScheduled ? `Planned for ${tamperBeneficiaryName}` : "",
      disabled: !panel.canTamper || panel.voteTamperingScheduled,
      requiresCandidate: true,
    },
    {
      kind: "votingId" as const,
      title: "Strict voting ID requirements",
      description: "It would reduce turnout for uneducated workers.",
      buttonLabel: "Propose",
      status: votingIdStatus,
      disabled: !panel.canProposeVotingId || panel.votingIdLawPassed || panel.votingIdProposalPending,
      requiresCandidate: false,
    },
  ];
  const getActionUnavailableReason = (kind: "bribe" | "endorse" | "cashAssistance" | "tamper" | "votingId"): string => kind === "endorse" && panel.endorsementUsed
    ? `The mayor already endorsed ${endorsedName} this election cycle.`
    : kind === "cashAssistance" && panel.cashAssistanceTurnoutBonusPercent > 0
      ? "Cash Assistance is already funded this election cycle."
    : kind === "tamper" && panel.voteTamperingScheduled
      ? `A vote-count operation is already planned for ${tamperBeneficiaryName} this election cycle.`
    : kind === "votingId" && panel.votingIdLawPassed
      ? "The stricter voting ID proposal has already passed for this election cycle."
    : kind === "votingId" && panel.votingIdProposalPending
      ? "The mayor is waiting for the voting ID proposal outcome."
    : panel.stage === "Voting"
    ? "Mayor campaign actions are unavailable on election day."
    : panel.bribeMeetingPending
      ? "The mayor is trying to schedule this candidate meeting."
      : panel.bribeBlocked || panel.bribeUsedToday
        ? "The mayor's schedule is blocked after today's campaign action."
        : panel.waitingForPopulation
          ? "Mayor campaign actions unlock when Elections start."
          : !activeCandidateField
          ? "There are no candidates right now."
          : !panel.donationsOpen
            ? "Mayor campaign actions are available during the campaign before election day."
            : "Mayor campaign action unavailable right now.";
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
          ? "Temporary mayor selected from real citizens. This mayor applies no city effects and supervises the transition until an election is completed."
          : "Current elected mayor and active mayoral platform."}
        >
          <img
            className={styles.mayorPortrait}
            src={panel.mayorPortrait || icon}
            alt=""
          />
        </Tooltip>
        <div className={styles.mayorTitle}>
          <Tooltip tooltip="The citizen currently serving as mayor. Click the name to move the camera to this citizen.">
            <span>Current mayor</span>
          </Tooltip>
          <Tooltip tooltip="Click the mayor name to move the camera to this citizen.">
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
        </div>
      </div>
      {panel.mayorTagName && (
        <Tooltip tooltip={panel.mayorTagDescription || panel.mayorTagName}>
          <div className={`${styles.candidateTag} ${tagToneClass(panel.mayorTagTone)}`}>
            <span>{panel.mayorTagName}</span>
            <strong>{panel.mayorTagDescription}</strong>
          </div>
        </Tooltip>
      )}
      <Tooltip tooltip="Current mayoral platform and city effect. Temporary transition mayors apply no city modifiers.">
        <div className={styles.mayorPlatform}>
          <span>{panel.mayorEffectName}</span>
          <PlatformImpactList impacts={panel.mayorPlatformImpacts ?? []} />
          <p>{panel.mayorEffectDescription}</p>
        </div>
      </Tooltip>
      <div className={styles.bribeBlock}>
        <div className={styles.sectionHeader}>
          <Tooltip tooltip="Mayor campaign actions are available before election day.">
            <span>Mayor campaign actions</span>
          </Tooltip>
          <Tooltip tooltip="Each mayor campaign action uses the current mayor campaign action cost.">
            <strong>{formatAmount(panel.bribeCost || 5000000)} each</strong>
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
                      <Tooltip tooltip="Current state of this mayor campaign action.">
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
                          } else if (action.kind === "votingId") {
                            trigger(mod.id, "proposeVotingIdLaw");
                            setPickerActionKind(null);
                          } else if (action.kind === "cashAssistance") {
                            trigger(mod.id, "cashAssistance");
                            setPickerActionKind(null);
                          }
                        }
                      }}
                    >
                      <strong>{actionEnabled ? action.buttonLabel : "Unavailable"}</strong>
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
        <Tooltip tooltip="Close candidate picker.">
          <Button
            variant="flat"
            className={styles.candidatePickerClose}
            onSelect={onClose}
          >
            <strong>X</strong>
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
                {candidate.tagName || "Candidate"} - {candidate.effectName || "No platform"}
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
          <div className={styles.candidatePickerEmpty}>No active candidates right now.</div>
        )}
      </div>
    </div>
  );
}

function getCandidatePickerTitle(kind: MayorActionKind): string {
  switch (kind) {
    case "bribe":
      return "Choose platform meeting target";
    case "endorse":
      return "Choose endorsement target";
    case "tamper":
      return "Choose vote-count target";
    default:
      return "Choose candidate";
  }
}

function CandidateCard(props: {
  candidate: Candidate;
  donationTiers: DonationTier[];
  donationsOpen: boolean;
}): ReactElement {
  const { candidate, donationTiers, donationsOpen } = props;
  const canDonate = donationsOpen && candidate.exists;
  const tiers = Array.isArray(donationTiers) ? donationTiers : [];
  const donationTier = tiers[0];
  const donationCost = candidate.donationCost || donationTier?.amount || 0;

  return (
    <article className={`${styles.candidateCard} ${candidate.index === 0 ? styles.candidateCardA : styles.candidateCardB}`}>
      <div className={styles.candidateHeader}>
        <Tooltip tooltip="Candidate portrait. Candidates are selected from real adult residents.">
          <img
            className={styles.portrait}
            src={candidate.portrait || icon}
            alt=""
          />
        </Tooltip>
        <div className={styles.candidateTitle}>
          <Tooltip tooltip="Click the candidate name to move the camera to this citizen.">
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
            <Tooltip tooltip="Candidate background generated from the resident's current life in the city.">
              <div className={styles.candidateBio}>{candidate.bio}</div>
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

      <Tooltip tooltip="Effective campaign support credited to this candidate during the current campaign. Some tags can change cost or campaign effect.">
        <div className={styles.donationTotal}>
          <span>Total donations</span>
          <strong>{formatAmount(candidate.donationAmount)}</strong>
        </div>
      </Tooltip>

      <Tooltip tooltip="Candidate platform. The positive effect is shown first, followed by the tradeoff.">
        <div className={styles.platform}>
          <span>Candidate Platform</span>
          <PlatformImpactList impacts={candidate.platformImpacts ?? []} />
        </div>
      </Tooltip>

      <div className={styles.donationBlock}>
        <div className={styles.donationHeader}>
          <Tooltip tooltip="City-funded campaign support. Donations are available while the mayoral race is active before election day.">
            <span>Campaign donation</span>
          </Tooltip>
        </div>
        {donationTier && (
          <Tooltip
            tooltip={`Donate ${formatAmount(donationCost)} of city funds to support this candidate's campaign before election day.`}
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
              <strong>Donate {formatAmount(donationCost)}</strong>
            </Button>
          </Tooltip>
        )}
        {!donationsOpen && (
          <Tooltip tooltip="Donations are available when an active campaign has selected candidates before election day.">
            <div className={styles.helpText}>Donations open before election day once candidates are selected.</div>
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

function PollRow(props: {
  label: string;
  value: number;
  color: string;
}): ReactElement {
  const width = Math.max(0, Math.min(100, props.value));

  return (
    <div className={styles.pollRow}>
      <Tooltip tooltip={`Poll support for ${props.label}. This is based on the sampled residents, not final election turnout.`}>
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
  const candidateA = {
    ...defaultPanel.candidateA,
    ...(source.candidateA ?? {}),
  };
  const candidateB = {
    ...defaultPanel.candidateB,
    ...(source.candidateB ?? {}),
  };
  const poll = {
    ...defaultPanel.poll,
    ...(source.poll ?? {}),
  };
  poll.ageGroups = Array.isArray(poll.ageGroups) ? poll.ageGroups : [];
  poll.educationGroups = Array.isArray(poll.educationGroups) ? poll.educationGroups : [];
  poll.incomeGroups = Array.isArray(poll.incomeGroups) ? poll.incomeGroups : [];

  return {
    ...defaultPanel,
    ...source,
    poll,
    candidateA,
    candidateB,
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
    mayorPlatformImpacts: Array.isArray(source.mayorPlatformImpacts) ? source.mayorPlatformImpacts : [],
  };
}

export default register;
