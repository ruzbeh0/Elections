import { type ModRegistrar } from "cs2/modding";
import { bindValue, trigger, useValue } from "cs2/api";
import { Button, Icon, Tooltip } from "cs2/ui";
import { type ReactElement, useMemo, useState } from "react";

import icon from "images/elections.svg";
import mod from "../mod.json";
import styles from "./mods/ElectionsMenu.module.scss";

type DonationTier = {
  index: number;
  amount: number;
  bonusPercent: number;
  label: string;
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
  effectName: string;
  effectDescription: string;
  platformImpacts: PlatformImpact[];
  donationAmount: number;
  donationBonusPercent: number;
  donated: boolean;
};

type Poll = {
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
  bribeUsedToday: boolean;
  bribeCost: number;
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
  mayorPlatformImpacts: PlatformImpact[];
  mayorTemporary: boolean;
};

const emptyCandidate = (index: number, name: string): Candidate => ({
  index,
  exists: false,
  name,
  portrait: "",
  canFocus: false,
  bio: "",
  effectName: "No platform",
  effectDescription: "No candidate has been selected yet.",
  platformImpacts: [],
  donationAmount: 0,
  donationBonusPercent: 0,
  donated: false,
});

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
  bribeUsedToday: false,
  bribeCost: 5000000,
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
  },
  donationTiers: [],
  candidateA: emptyCandidate(0, "Candidate A"),
  candidateB: emptyCandidate(1, "Candidate B"),
  mayorName: "",
  mayorPortrait: "",
  mayorCanFocus: false,
  mayorEffectName: "",
  mayorEffectDescription: "",
  mayorPlatformImpacts: [],
  mayorTemporary: false,
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

export const register: ModRegistrar = (moduleRegistry) => {
  moduleRegistry.append("GameTopLeft", ElectionsTopLeft);
  moduleRegistry.append("UniversalModMenu", ElectionsUniversalModMenu);
};

function ElectionsTopLeft(): ReactElement {
  const useUniversalModMenu = useValue(useUniversalModMenu$);

  return <>{!useUniversalModMenu && <ElectionsMenu />}</>;
}

function ElectionsUniversalModMenu(): ReactElement {
  const useUniversalModMenu = useValue(useUniversalModMenu$);

  return <>{useUniversalModMenu && <ElectionsMenu />}</>;
}

function ElectionsMenu(): ReactElement {
  const [open, setOpen] = useState(false);
  const panel = normalizePanel(useValue(panel$));
  const showVotingLocations = useValue(showVotingLocations$);
  const candidates = useMemo(
    () => [panel.candidateA ?? defaultPanel.candidateA, panel.candidateB ?? defaultPanel.candidateB],
    [panel.candidateA, panel.candidateB]
  );

  return (
    <div className={styles.root}>
      <Tooltip tooltip="Open the Elections panel. Shows the current mayor, active race, poll status, and campaign donation controls.">
        <Button
          variant="floating"
          selected={open}
          aria-label="Open Elections"
          onSelect={() => {
            if (open) {
              trigger(mod.id, "setShowVotingLocations", false);
            }
            setOpen(!open);
          }}
        >
          <Icon src={icon} tinted={true} className={styles.icon} />
        </Button>
      </Tooltip>

      {open && (
        <div draggable className={styles.panel}>
          <header className={styles.header}>
            <Tooltip tooltip="Current election stage. Candidate selection, poll release, and election day are managed by the Elections lifecycle.">
              <div className={styles.titleBlock}>
                <span className={styles.title}>Elections</span>
                <span className={styles.subtitle}>{panel.stageLabel}</span>
              </div>
            </Tooltip>
            <div className={styles.headerActions}>
              <Tooltip tooltip={panel.enabled
                ? panel.waitingForPopulation
                  ? `Voting sites unlock when the city reaches ${formatAmount(panel.minimumPopulation)} population.`
                  : "Highlight voting locations. During election day, each marker also shows votes cast at that location."
                : "Voting locations are available when Elections are enabled."}
              >
                <button
                  type="button"
                  disabled={!panel.enabled || panel.waitingForPopulation}
                  aria-label="Highlight voting locations"
                  aria-pressed={showVotingLocations}
                  className={`${styles.votingLocationsButton} ${showVotingLocations ? styles.votingLocationsButtonActive : ""}`}
                  onClick={() => {
                    if (panel.enabled && !panel.waitingForPopulation) {
                      trigger(mod.id, "setShowVotingLocations", !showVotingLocations);
                    }
                  }}
                >
                  <BallotBoxIcon />
                  <span className={styles.votingLocationsButtonLabel}>Voting sites</span>
                </button>
              </Tooltip>
            </div>
          </header>

          {!panel.enabled && (
            <div className={styles.notice}>Elections are disabled in mod settings.</div>
          )}
          {panel.enabled && panel.waitingForPopulation && (
            <div className={styles.notice}>
              Elections will start when the city reaches {formatAmount(panel.minimumPopulation)} population. Current population: {formatAmount(panel.currentPopulation)}.
            </div>
          )}

          <section className={styles.topLevel}>
            {panel.mayorName && (
              <MayorSection panel={panel} />
            )}
            <ScheduleSection panel={panel} />
          </section>

          <section className={styles.candidateGrid}>
            {candidates.map((candidate) => (
              <CandidateCard
                key={candidate.index}
                candidate={candidate}
                donationTiers={panel.donationTiers ?? []}
                donationsOpen={panel.donationsOpen}
              />
            ))}
          </section>
        </div>
      )}
    </div>
  );
}

function BallotBoxIcon(): ReactElement {
  return (
    <svg className={styles.ballotIcon} viewBox="0 0 24 24" aria-hidden="true">
      <path d="M5 11h14l-1 10H6L5 11Z" />
      <path d="M8 11l1.2-3h9.6l1.2 3" />
      <path d="M9 15h6" />
      <path d="M11 7.4 15.8 5l1.5 3.1-4.8 2.4Z" />
      <path d="M4 7.3c1.8-.2 3.2.2 4.4 1.3l2.1 1.9" />
      <path d="M8.3 8.6 10 7.1" />
    </svg>
  );
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
              <strong className={styles.pollSample}>{formatAmount(panel.poll.sampleSize)} sampled</strong>
            </Tooltip>
          )}
        </div>

        {panel.pollReleased ? (
          <>
            <Tooltip tooltip={panel.poll.resultDescription || "Poll read based on the sample and margin of error."}>
              <div className={styles.pollReadout}>
                <strong>{panel.poll.resultLabel || "Poll released"}</strong>
                <span>+/-{panel.poll.marginOfError}% margin of error</span>
              </div>
            </Tooltip>
            <div className={styles.pollRows}>
              <PollRow
                label={panel.candidateA.name}
                value={panel.poll.percentA}
                color={candidateAColor}
              />
              <PollRow
                label={panel.candidateB.name}
                value={panel.poll.percentB}
                color={candidateBColor}
              />
              <PollRow
                label="Undecided"
                value={panel.poll.percentUndecided}
                color={undecidedColor}
              />
            </div>
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

function MayorSection(props: { panel: ElectionPanel }): ReactElement {
  const { panel } = props;
  const [bribeTarget, setBribeTarget] = useState(0);
  const [bribeMenuOpen, setBribeMenuOpen] = useState(false);
  const selectedBribeTarget = bribeTarget === 1 ? 1 : 0;
  const bribeOptions = [
    `Convince ${panel.candidateA?.name || "Candidate A"} to change their platform`,
    `Convince ${panel.candidateB?.name || "Candidate B"} to change their platform`,
  ];

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
      <Tooltip tooltip="Current mayoral platform and city effect. Temporary transition mayors apply no city modifiers.">
        <div className={styles.mayorPlatform}>
          <span>{panel.mayorEffectName}</span>
          <PlatformImpactList impacts={panel.mayorPlatformImpacts ?? []} />
          <p>{panel.mayorEffectDescription}</p>
        </div>
      </Tooltip>
      {panel.bribesOpen && (
        <div className={styles.bribeBlock}>
          <Tooltip tooltip="Choose which candidate the mayor should pressure into changing platform. The bribe costs city funds and can be attempted once per in-game day.">
            <div className={styles.bribeDropdown}>
              <Button
                variant="flat"
                className={panel.canBribe ? styles.bribeSelectButton : `${styles.bribeSelectButton} ${styles.bribeButtonDisabled}`}
                disabled={!panel.canBribe}
                aria-disabled={!panel.canBribe}
                onSelect={() => {
                  if (panel.canBribe) {
                    setBribeMenuOpen((value) => !value);
                  }
                }}
              >
                <span>{bribeOptions[selectedBribeTarget]}</span>
              </Button>
              {bribeMenuOpen && panel.canBribe && (
                <div className={styles.bribeMenu}>
                  {bribeOptions.map((option, index) => (
                    <Button
                      variant="flat"
                      className={styles.bribeOptionButton}
                      selected={index === selectedBribeTarget}
                      key={`${option}-${index}`}
                      onSelect={() => {
                        setBribeTarget(index);
                        setBribeMenuOpen(false);
                      }}
                    >
                      <span>{option}</span>
                    </Button>
                  ))}
                </div>
              )}
            </div>
          </Tooltip>
          <Tooltip tooltip="Spend city funds to attempt the selected mayoral bribe. Success chance is 25%.">
            <Button
              variant="flat"
              className={panel.canBribe ? styles.bribeButton : `${styles.bribeButton} ${styles.bribeButtonDisabled}`}
              disabled={!panel.canBribe}
              aria-disabled={!panel.canBribe}
              onSelect={() => {
                if (panel.canBribe) {
                  setBribeMenuOpen(false);
                  trigger(mod.id, "bribeMayor", selectedBribeTarget);
                }
              }}
            >
              <strong>Bribe {formatAmount(panel.bribeCost || 5000000)}</strong>
            </Button>
          </Tooltip>
          {panel.bribeUsedToday && (
            <div className={styles.helpText}>Candidate meeting attempted today.</div>
          )}
        </div>
      )}
    </section>
  );
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

      <Tooltip tooltip="Total city funds donated to this candidate during the current campaign.">
        <div className={styles.donationTotal}>
          <span>Total donated</span>
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
          <Tooltip tooltip="City-funded campaign support. Donations are available while the mayoral race is active and candidates have been selected.">
            <span>Campaign donation</span>
          </Tooltip>
        </div>
        {donationTier && (
          <Tooltip
            tooltip={`Donate ${formatAmount(donationTier.amount)} of city funds to support this candidate's campaign.`}
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
              <strong>Donate {formatAmount(donationTier.amount)}</strong>
            </Button>
          </Tooltip>
        )}
        {!donationsOpen && (
          <Tooltip tooltip="Donations are available when an active campaign has selected candidates.">
            <div className={styles.helpText}>Donations open when candidates are selected.</div>
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

  return {
    ...defaultPanel,
    ...source,
    poll,
    candidateA,
    candidateB,
    donationTiers: Array.isArray(source.donationTiers) ? source.donationTiers : [],
    mayorPlatformImpacts: Array.isArray(source.mayorPlatformImpacts) ? source.mayorPlatformImpacts : [],
  };
}

export default register;
