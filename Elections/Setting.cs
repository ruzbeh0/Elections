using Colossal;
using Colossal.IO.AssetDatabase;
using Elections.Components;
using Elections.Systems;
using Game.Modding;
using Game.SceneFlow;
using Game.Settings;
using Game.UI;
using Game.UI.Widgets;
using System.Collections.Generic;
using Unity.Entities;

namespace Elections
{
    [FileLocation($"ModsSettings\\{nameof(Elections)}\\{nameof(Elections)}")]
    [SettingsUITabOrder(kGeneralSection, kCampaignSection, kVotingSection, kDebugSection)]
    [SettingsUIGroupOrder(kGeneralGroup, kOverlayGroup, kCandidateGroup, kPartyGroup, kTurnoutPresetGroup, kTurnoutGroup, kVotingHoursGroup, kDebugGroup)]
    [SettingsUIShowGroupName(kGeneralGroup, kOverlayGroup, kCandidateGroup, kPartyGroup, kTurnoutPresetGroup, kTurnoutGroup, kVotingHoursGroup, kDebugGroup)]
    public class Setting : ModSetting
    {
        public const string kGeneralSection = "General";
        public const string kCampaignSection = "Campaign";
        public const string kVotingSection = "Voting";
        public const string kDebugSection = "Debug";
        public const string kGeneralGroup = "General";
        public const string kOverlayGroup = "Overlay";
        public const string kCandidateGroup = "Candidates";
        public const string kPartyGroup = "PoliticalParties";
        public const string kTurnoutPresetGroup = "TurnoutPresets";
        public const string kTurnoutGroup = "DailyTurnout";
        public const string kVotingHoursGroup = "VotingHours";
        public const string kDebugGroup = "Debug";

        public enum VotingStartHourOption
        {
            Hour6 = 6,
            Hour7 = 7,
            Hour8 = 8,
            Hour9 = 9,
            Hour10 = 10
        }

        public enum VotingEndHourOption
        {
            Hour16 = 16,
            Hour17 = 17,
            Hour18 = 18
        }

        public enum TurnoutCountryPreset
        {
            Baseline = 0,
            EuropeanUnion = 2,
            Brazil = 25,
            Canada = 34,
            France = 62,
            Germany = 66,
            UK = 187,
            USA = 188
        }

        public Setting(IMod mod) : base(mod)
        {
            SetDefaults();
        }

        [SettingsUISection(kGeneralSection, kGeneralGroup)]
        [SettingsUISetter(typeof(Setting), nameof(SetEnableElections))]
        public bool EnableElections { get; set; }

        [SettingsUISection(kGeneralSection, kGeneralGroup)]
        [SettingsUISetter(typeof(Setting), nameof(SetUseUniversalModMenu))]
        public bool UseUniversalModMenu { get; set; }

        [SettingsUISlider(min = 100, max = 200, step = 5, scalarMultiplier = 1, unit = Unit.kPercentage)]
        [SettingsUISection(kGeneralSection, kOverlayGroup)]
        public int VotingSiteOverlayScalePercent { get; set; } = 120;

        [SettingsUISlider(min = ElectionState.MinCandidateCount, max = ElectionState.MaxCandidateCount, step = 1, scalarMultiplier = 1)]
        [SettingsUISection(kCampaignSection, kCandidateGroup)]
        public int CandidateCount { get; set; } = ElectionState.DefaultCandidateCount;

        [SettingsUISection(kCampaignSection, kPartyGroup)]
        public bool EnableParties { get; set; }

        [SettingsUISection(kCampaignSection, kCandidateGroup)]
        public bool EnableRunoffVoting { get; set; }

        [SettingsUISection(kVotingSection, kTurnoutPresetGroup)]
        public TurnoutCountryPreset AgeTurnoutCountryPreset { get; set; } = TurnoutCountryPreset.Baseline;

        [SettingsUIButton]
        [SettingsUISection(kVotingSection, kTurnoutPresetGroup)]
        public bool ApplyAgeTurnoutCountryPreset
        {
            set
            {
                if (!value)
                    return;

                ApplyAgeTurnoutPreset(AgeTurnoutCountryPreset);
            }
        }

        [SettingsUISlider(min = 1, max = 100, step = 1, scalarMultiplier = 1, unit = Unit.kPercentage)]
        [SettingsUISection(kVotingSection, kTurnoutGroup)]
        public int TeenDailyVotingTurnoutPercent { get; set; } = ElectionUtility.DefaultTeenDailyVotingTurnoutPercent;

        [SettingsUISlider(min = 1, max = 100, step = 1, scalarMultiplier = 1, unit = Unit.kPercentage)]
        [SettingsUISection(kVotingSection, kTurnoutGroup)]
        public int AdultDailyVotingTurnoutPercent { get; set; } = ElectionUtility.DefaultAdultDailyVotingTurnoutPercent;

        [SettingsUISlider(min = 1, max = 100, step = 1, scalarMultiplier = 1, unit = Unit.kPercentage)]
        [SettingsUISection(kVotingSection, kTurnoutGroup)]
        public int ElderlyDailyVotingTurnoutPercent { get; set; } = ElectionUtility.DefaultElderlyDailyVotingTurnoutPercent;

        [SettingsUISection(kVotingSection, kVotingHoursGroup)]
        public VotingStartHourOption ElectionVotingStartHour { get; set; } = VotingStartHourOption.Hour8;

        [SettingsUISection(kVotingSection, kVotingHoursGroup)]
        public VotingEndHourOption ElectionVotingEndHour { get; set; } = VotingEndHourOption.Hour17;

        [SettingsUISection(kDebugSection, kDebugGroup)]
        public bool EnableDebugLogging { get; set; }

        [SettingsUIButton]
        [SettingsUIConfirmation]
        [SettingsUISection(kDebugSection, kDebugGroup)]
        public bool ForceStartCampaign
        {
            set
            {
                if (!value)
                    return;

                var world = World.DefaultGameObjectInjectionWorld;
                var system = world?.GetExistingSystemManaged<ElectionLifecycleSystem>();
                system?.ForceStartCampaignFromSettings();
            }
        }

        [SettingsUIButton]
        [SettingsUIConfirmation]
        [SettingsUISection(kDebugSection, kDebugGroup)]
        public bool ForceElectionToday
        {
            set
            {
                if (!value)
                    return;

                var world = World.DefaultGameObjectInjectionWorld;
                var system = world?.GetExistingSystemManaged<ElectionLifecycleSystem>();
                system?.ForceElectionTodayFromSettings();
            }
        }

        [SettingsUIButton]
        [SettingsUIConfirmation]
        [SettingsUISection(kDebugSection, kDebugGroup)]
        public bool ForceGeneratePoll
        {
            set
            {
                if (!value)
                    return;

                var world = World.DefaultGameObjectInjectionWorld;
                var system = world?.GetExistingSystemManaged<ElectionLifecycleSystem>();
                system?.ForceGeneratePollFromSettings();
            }
        }

        public override void SetDefaults()
        {
            EnableElections = true;
            UseUniversalModMenu = false;
            VotingSiteOverlayScalePercent = 120;
            CandidateCount = ElectionState.DefaultCandidateCount;
            EnableParties = true;
            EnableRunoffVoting = false;
            AgeTurnoutCountryPreset = TurnoutCountryPreset.Baseline;
            TeenDailyVotingTurnoutPercent = ElectionUtility.DefaultTeenDailyVotingTurnoutPercent;
            AdultDailyVotingTurnoutPercent = ElectionUtility.DefaultAdultDailyVotingTurnoutPercent;
            ElderlyDailyVotingTurnoutPercent = ElectionUtility.DefaultElderlyDailyVotingTurnoutPercent;
            ElectionVotingStartHour = VotingStartHourOption.Hour8;
            ElectionVotingEndHour = VotingEndHourOption.Hour17;
            EnableDebugLogging = false;
        }

        public void SetUseUniversalModMenu(bool value)
        {
            var world = World.DefaultGameObjectInjectionWorld;
            var system = world?.GetExistingSystemManaged<ElectionUISystem>();
            system?.UpdateUseUniversalModMenu(value);
        }

        public void SetEnableElections(bool value)
        {
            var world = World.DefaultGameObjectInjectionWorld;
            var system = world?.GetExistingSystemManaged<ElectionUISystem>();
            system?.UpdateElectionsEnabled(value);
        }

        public void ApplyAgeTurnoutPreset(TurnoutCountryPreset preset)
        {
            // Rounded from published age turnout tables; adult values average available adult sub-bands.
            switch (preset)
            {
                case TurnoutCountryPreset.EuropeanUnion:
                    SetDailyTurnoutPercentages(36, 49, 58);
                    break;
                case TurnoutCountryPreset.Brazil:
                    SetDailyTurnoutPercentages(79, 84, 56);
                    break;
                case TurnoutCountryPreset.Canada:
                    SetDailyTurnoutPercentages(56, 67, 74);
                    break;
                case TurnoutCountryPreset.France:
                    SetDailyTurnoutPercentages(28, 52, 61);
                    break;
                case TurnoutCountryPreset.Germany:
                    SetDailyTurnoutPercentages(79, 83, 82);
                    break;
                case TurnoutCountryPreset.UK:
                    SetDailyTurnoutPercentages(37, 52, 74);
                    break;
                case TurnoutCountryPreset.USA:
                    SetDailyTurnoutPercentages(48, 65, 75);
                    break;
                default:
                    SetDailyTurnoutPercentages(
                        ElectionUtility.DefaultTeenDailyVotingTurnoutPercent,
                        ElectionUtility.DefaultAdultDailyVotingTurnoutPercent,
                        ElectionUtility.DefaultElderlyDailyVotingTurnoutPercent);
                    break;
            }
        }

        private void SetDailyTurnoutPercentages(int teen, int adult, int elderly)
        {
            TeenDailyVotingTurnoutPercent = ClampTurnoutPercent(teen);
            AdultDailyVotingTurnoutPercent = ClampTurnoutPercent(adult);
            ElderlyDailyVotingTurnoutPercent = ClampTurnoutPercent(elderly);
        }

        private static int ClampTurnoutPercent(int value)
        {
            if (value < 1)
                return 1;

            return value > 100 ? 100 : value;
        }
    }

    public class LocaleEN : IDictionarySource
    {
        private readonly Setting m_Setting;

        public LocaleEN(Setting setting)
        {
            m_Setting = setting;
        }

        public IEnumerable<KeyValuePair<string, string>> ReadEntries(IList<IDictionaryEntryError> errors, Dictionary<string, int> indexCounts)
        {
            var entries = new Dictionary<string, string>
            {
                { m_Setting.GetSettingsLocaleID(), "Elections" },
                { m_Setting.GetOptionTabLocaleID(Setting.kGeneralSection), "General" },
                { m_Setting.GetOptionTabLocaleID(Setting.kCampaignSection), "Campaign" },
                { m_Setting.GetOptionTabLocaleID(Setting.kVotingSection), "Voting" },
                { m_Setting.GetOptionTabLocaleID(Setting.kDebugSection), "Debug" },

                { m_Setting.GetOptionGroupLocaleID(Setting.kGeneralGroup), "General" },
                { m_Setting.GetOptionGroupLocaleID(Setting.kOverlayGroup), "Overlay" },
                { m_Setting.GetOptionGroupLocaleID(Setting.kCandidateGroup), "Candidates" },
                { m_Setting.GetOptionGroupLocaleID(Setting.kPartyGroup), "Political Parties" },
                { m_Setting.GetOptionGroupLocaleID(Setting.kTurnoutPresetGroup), "Turnout Presets" },
                { m_Setting.GetOptionGroupLocaleID(Setting.kTurnoutGroup), "Daily Turnout" },
                { m_Setting.GetOptionGroupLocaleID(Setting.kVotingHoursGroup), "Voting Hours" },
                { m_Setting.GetOptionGroupLocaleID(Setting.kDebugGroup), "Debug" },

                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.EnableElections)), "Enable Elections" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.EnableElections)), "Run the mayoral election cycle." },

                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.UseUniversalModMenu)), "Use Universal Mod Menu" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.UseUniversalModMenu)), "Show the Elections button in Universal Mod Menu instead of the top-left game overlay." },

                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.VotingSiteOverlayScalePercent)), "Voting Site Overlay Size" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.VotingSiteOverlayScalePercent)), "Size of the voting-site marker, ballot icon, and vote count. 100% = 1.0 scale, 120% is the default, and changes apply immediately." },

                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.CandidateCount)), "Candidates Per Race" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.CandidateCount)), "Number of candidates selected for new mayoral races. Existing active races keep the candidate count they started with. Default is 2." },

                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.EnableRunoffVoting)), "Enable Runoff Voting" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.EnableRunoffVoting)), "Require a second round when no candidate reaches 50% of the first-round vote. Disabled by default. Regular campaigns start one month earlier when enabled." },

                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.EnableParties)), "Enable Political Parties" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.EnableParties)), "Enable fictional political parties, persistent political party reputation, political party tags, and political party management. Disabled by default for compatibility with existing saves." },

                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.AgeTurnoutCountryPreset)), "Age Turnout Country" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.AgeTurnoutCountryPreset)), "Choose a real-world age turnout preset from countries with published age turnout data, plus the European Union." },

                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.ApplyAgeTurnoutCountryPreset)), "Apply Age Turnout Preset" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.ApplyAgeTurnoutCountryPreset)), "Copies the selected country's rounded age turnout values into the teen, adult, and elderly daily turnout sliders." },

                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.TeenDailyVotingTurnoutPercent)), "Teen Daily Turnout" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.TeenDailyVotingTurnoutPercent)), "Daily probability that an eligible teen resident votes. Default is 36%; the simulation divides this by the configured voting hours." },

                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.AdultDailyVotingTurnoutPercent)), "Adult Daily Turnout" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.AdultDailyVotingTurnoutPercent)), "Daily probability that an eligible adult resident votes. Default is 49%; the simulation divides this by the configured voting hours." },

                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.ElderlyDailyVotingTurnoutPercent)), "Elderly Daily Turnout" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.ElderlyDailyVotingTurnoutPercent)), "Daily probability that an eligible elderly resident votes. Default is 58%; the simulation divides this by the configured voting hours." },

                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.ElectionVotingStartHour)), "Voting Start Hour" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.ElectionVotingStartHour)), "Election day voting start time. Default is 8:00 AM." },

                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.ElectionVotingEndHour)), "Voting End Hour" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.ElectionVotingEndHour)), "Election day voting end time. Default is 5:00 PM. Results are announced at 8:00 PM." },

                { m_Setting.GetEnumValueLocaleID(Setting.VotingStartHourOption.Hour6), "6:00 AM" },
                { m_Setting.GetEnumValueLocaleID(Setting.VotingStartHourOption.Hour7), "7:00 AM" },
                { m_Setting.GetEnumValueLocaleID(Setting.VotingStartHourOption.Hour8), "8:00 AM" },
                { m_Setting.GetEnumValueLocaleID(Setting.VotingStartHourOption.Hour9), "9:00 AM" },
                { m_Setting.GetEnumValueLocaleID(Setting.VotingStartHourOption.Hour10), "10:00 AM" },

                { m_Setting.GetEnumValueLocaleID(Setting.VotingEndHourOption.Hour16), "4:00 PM" },
                { m_Setting.GetEnumValueLocaleID(Setting.VotingEndHourOption.Hour17), "5:00 PM" },
                { m_Setting.GetEnumValueLocaleID(Setting.VotingEndHourOption.Hour18), "6:00 PM" },

                { m_Setting.GetEnumValueLocaleID(Setting.TurnoutCountryPreset.Baseline), "Default" },
                { m_Setting.GetEnumValueLocaleID(Setting.TurnoutCountryPreset.EuropeanUnion), "European Union" },
                { m_Setting.GetEnumValueLocaleID(Setting.TurnoutCountryPreset.Brazil), "Brazil" },
                { m_Setting.GetEnumValueLocaleID(Setting.TurnoutCountryPreset.Canada), "Canada" },
                { m_Setting.GetEnumValueLocaleID(Setting.TurnoutCountryPreset.France), "France" },
                { m_Setting.GetEnumValueLocaleID(Setting.TurnoutCountryPreset.Germany), "Germany" },
                { m_Setting.GetEnumValueLocaleID(Setting.TurnoutCountryPreset.UK), "United Kingdom" },
                { m_Setting.GetEnumValueLocaleID(Setting.TurnoutCountryPreset.USA), "United States of America" },

                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.EnableDebugLogging)), "Enable Debug Logging" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.EnableDebugLogging)), "Write detailed Elections lifecycle, campaign, poll, voting, donation, mayor, and repair events to the game log." },

                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.ForceStartCampaign)), "Force Start Campaign" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.ForceStartCampaign)), "Debug action: immediately select candidates and announce the campaign." },
                { m_Setting.GetOptionWarningLocaleID(nameof(Setting.ForceStartCampaign)), "Start a new campaign now?" },

                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.ForceElectionToday)), "Force Election Today" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.ForceElectionToday)), "Debug action: begin voting today if candidates exist." },
                { m_Setting.GetOptionWarningLocaleID(nameof(Setting.ForceElectionToday)), "Begin election voting today?" },

                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.ForceGeneratePoll)), "Generate Poll" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.ForceGeneratePoll)), "Debug action: immediately generate and release poll results for the active race." },
                { m_Setting.GetOptionWarningLocaleID(nameof(Setting.ForceGeneratePoll)), "Generate a poll now?" }
            };
            AddElectionEntries(entries);
            return entries;
        }

        public static void AddElectionEntries(Dictionary<string, string> entries)
        {
            void Add(string key, string value) => entries[ElectionLocalization.ID(key)] = value;

            Add("UI.OpenPanel.Tooltip", "Open the Elections panel. Shows the current mayor, active race, poll status, and campaign donation controls before election day.");
            Add("UI.OpenPanel.Aria", "Open Elections");
            Add("UI.Panel.Aria", "Election panels");
            Add("UI.Pending", "Pending");
            Add("UI.Unknown", "Unknown");
            Add("UI.Unavailable", "Unavailable");
            Add("UI.Selected", "Selected");
            Add("UI.Save", "Save");
            Add("UI.Cancel", "Cancel");
            Add("UI.Close", "X");
            Add("UI.Empty", "Empty");
            Add("UI.Total", "Total");
            Add("UI.Each", "{amount} each");
            Add("UI.Date.January1", "January 1");

            Add("UI.Menu.VotingSites.Label", "View voting sites");
            Add("UI.Menu.VotingSites.Title", "Voting sites");
            Add("UI.Menu.VotingSites.Tooltip.Enabled", "Show voting locations on the map.");
            Add("UI.Menu.VotingSites.Tooltip.Disabled", "Voting locations unlock when Elections are enabled and the city reaches the minimum population.");
            Add("UI.Menu.Mayor.Label", "Current mayor panel");
            Add("UI.Menu.Mayor.Title", "Current mayor");
            Add("UI.Menu.Mayor.Tooltip", "Show the current mayor and mayoral actions.");
            Add("UI.Menu.Schedule.Label", "Election schedule");
            Add("UI.Menu.Schedule.Title", "Election schedule");
            Add("UI.Menu.Schedule.Tooltip", "Show election dates, voting hours, results time, and poll status.");
            Add("UI.Menu.Programs.Label", "Civic programs");
            Add("UI.Menu.Programs.Title", "Civic programs");
            Add("UI.Menu.Programs.Tooltip", "Fund turnout support programs before election day.");
            Add("UI.Menu.Legislation.Label", "Legislation");
            Add("UI.Menu.Legislation.Title", "Legislation");
            Add("UI.Menu.Legislation.Tooltip", "Pass or repeal persistent election legislation.");
            Add("UI.Menu.Candidates.Label", "Candidates");
            Add("UI.Menu.Candidates.Title", "Candidates");
            Add("UI.Menu.Candidates.Tooltip", "Show the mayoral candidates and campaign donation controls.");
            Add("UI.Menu.Parties.Label", "Parties");
            Add("UI.Menu.Parties.Title", "Parties");
            Add("UI.Menu.Parties.Tooltip.Enabled", "Manage fictional political parties, reputation, colors, and party tags.");
            Add("UI.Menu.Parties.Tooltip.Disabled", "Political parties are disabled in mod settings.");
            Add("UI.Menu.Residence.Label", "Mayor residence");
            Add("UI.Menu.Residence.Title", "Mayor residence");
            Add("UI.Menu.Residence.Tooltip", "Set the mayor's home and City Hall workplace from the selected building.");

            Add("UI.Notice.Disabled", "Elections are disabled in mod settings.");
            Add("UI.Notice.WaitingPopulation", "Elections will start when the city reaches {minimum} population. Current population: {current}.");
            Add("UI.Notice.NoMayor", "No mayor has been selected yet.");
            Add("UI.Notice.NoCandidates", "There are no candidates right now.");
            Add("UI.Notice.PartiesDisabled", "Parties are disabled in mod settings.");
            Add("UI.Notice.NoParties", "No parties are active right now.");

            Add("UI.VotingSites.Header.Tooltip", "Voting-site overlay status. On election day, map markers include live vote counts for each location.");
            Add("UI.VotingSites.Header", "Voting sites");
            Add("UI.VotingSites.Status.Visible.Tooltip", "Voting-site overlay is visible.");
            Add("UI.VotingSites.Status.Hidden.Tooltip", "Voting-site overlay is hidden.");
            Add("UI.VotingSites.Status.Visible", "Overlay visible");
            Add("UI.VotingSites.Status.Hidden", "Overlay hidden");
            Add("UI.VotingSites.Election.Tooltip", "Election date and voting window for the active mayoral race.");
            Add("UI.VotingSites.Election.Label", "Election");
            Add("UI.VotingSites.Markers.Tooltip", "Live vote counts appear only during the voting phase.");
            Add("UI.VotingSites.Markers.Label", "Map markers");
            Add("UI.VotingSites.Markers.Live", "Live results");
            Add("UI.VotingSites.Markers.Locations", "Voting locations");
            Add("UI.VotingSites.Button.Tooltip.Hide", "Hide voting locations on the map.");
            Add("UI.VotingSites.Button.Tooltip.Show", "Show voting locations on the map.");
            Add("UI.VotingSites.Button.Hide", "Hide voting sites");
            Add("UI.VotingSites.Button.Show", "View voting sites");

            Add("UI.Info.Phase", "Phase");
            Add("UI.Info.Poll", "Poll");
            Add("UI.Info.Election", "Election");
            Add("UI.Info.Results", "Results");
            Add("UI.Info.Inauguration", "Inauguration");
            Add("UI.Info.Current", "Current");
            Add("UI.Schedule.Header.Tooltip", "Election schedule and current lifecycle phase.");
            Add("UI.Schedule.Header", "Election schedule");
            Add("UI.Schedule.Summary.Tooltip", "Summary of the current election cycle and whether this is a regular or accelerated race.");
            Add("UI.Schedule.Phase.Tooltip", "The current phase of the election cycle.");
            Add("UI.Schedule.Poll.Tooltip", "The campaign poll is released at 08:00 on this date.");
            Add("UI.Schedule.Election.Tooltip", "Election day and voting window for the active mayoral race.");
            Add("UI.Schedule.Results.Tooltip", "Election results announcement date and time.");
            Add("UI.Schedule.Inauguration.Tooltip", "{name} is mayor-elect. The sitting mayor remains in office until this date.");
            Add("UI.Poll.Header.Tooltip", "Poll status. Before the poll is released this shows the scheduled release date; after release it shows sampled voter preferences.");
            Add("UI.Poll.Header.Results", "Current poll results");
            Add("UI.Poll.Header.Scheduled", "Poll scheduled");
            Add("UI.Poll.Sample.Tooltip", "Number of eligible residents sampled by the campaign poll.");
            Add("UI.Poll.Sample.Word", "sampled");
            Add("UI.Poll.Tabs.Aria", "Poll breakdown");
            Add("UI.Poll.Tab.Overall", "Overall");
            Add("UI.Poll.Tab.Age", "Age");
            Add("UI.Poll.Tab.Education", "Education");
            Add("UI.Poll.Tab.Income", "Income");
            Add("UI.Poll.Tab.Tooltip", "Show {label} poll results.");
            Add("UI.Poll.ReleaseNotice", "Poll releases on {date} at 08:00.");
            Add("UI.Poll.PendingNotice", "Poll date is pending.");
            Add("UI.Poll.Undecided", "Undecided");
            Add("UI.Poll.Readout.Tooltip", "Poll read based on the sample and margin of error.");
            Add("UI.Poll.Released", "Poll released");
            Add("UI.Poll.NoBreakdown", "No poll breakdown data is available.");
            Add("UI.Poll.Breakdown.Tooltip", "Poll read for this sampled group.");
            Add("UI.Poll.Row.Tooltip", "Poll support for {label}. This is based on the sampled residents, not final election turnout.");

            Add("UI.Programs.Fallback.ElectionDay", "Civic programs are unavailable on election day.");
            Add("UI.Programs.Fallback.UsedToday", "Only one civic program can be funded per day. Today's program is {program}.");
            Add("UI.Programs.Fallback.AlreadySelected", "already selected");
            Add("UI.Programs.Fallback.Closed", "Civic programs are available before election day once candidates are selected.");
            Add("UI.Programs.Header.Tooltip", "Civic programs can be funded once per day before election day.");
            Add("UI.Programs.Header", "Civic programs");
            Add("UI.Programs.Cost.Tooltip", "Each civic program costs half of the current campaign donation value.");
            Add("UI.Programs.Today.Tooltip", "The daily civic program slot refreshes on the next calendar day.");
            Add("UI.Programs.Today", "Today's program: {program}");
            Add("UI.Programs.Today.Selected", "selected");
            Add("UI.Programs.Status.HolidayScheduled", "Holiday scheduled");
            Add("UI.Programs.Status.NotScheduled", "Not scheduled");
            Add("UI.Programs.Status.CurrentBonus", "Current bonus +{percent}%");
            Add("UI.Programs.Status.Tooltip.Holiday", "Holiday scheduling status.");
            Add("UI.Programs.Status.Tooltip.Bonus", "Accumulated election turnout bonus for this voter group.");
            Add("UI.Programs.Button.Fund", "Fund");
            Add("UI.Programs.Button.Scheduled", "Scheduled");
            Add("UI.Programs.NoPrograms", "Civic programs are unavailable right now.");

            Add("UI.Legislation.Fallback.ElectionDay", "Election legislation is unavailable on election day.");
            Add("UI.Legislation.Fallback.UsedToday", "Only one election legislation action can be attempted per day.");
            Add("UI.Legislation.Fallback.Closed", "Election legislation can be changed before election day once candidates are selected.");
            Add("UI.Legislation.Header.Tooltip", "Legislation persists across future elections until it is repealed. Passing and repealing use city funds, can fail, and do not create corruption risk.");
            Add("UI.Legislation.Header", "Election legislation");
            Add("UI.Legislation.Button.Pass", "Pass");
            Add("UI.Legislation.Button.Repeal", "Repeal");
            Add("UI.Legislation.Active.Tooltip", "Active legislation remains in effect across elections.");
            Add("UI.Legislation.Active", "Active");
            Add("UI.Legislation.NoLegislation", "Election legislation is unavailable right now.");

            Add("UI.Residence.Header.Tooltip", "The assigned residence and office are enforced for the current mayor. If the target is full, another resident or worker is removed first.");
            Add("UI.Residence.Header", "Mayor assignments");
            Add("UI.Residence.Status.Assigned.Tooltip", "The mayor is assigned to both selected targets.");
            Add("UI.Residence.Status.Relocating.Tooltip", "The mayor will be moved to the selected targets during the next assignment update.");
            Add("UI.Residence.Status.Assigned", "Assigned");
            Add("UI.Residence.Status.Relocating", "Relocating");
            Add("UI.Residence.Selected.Tooltip.Exists", "Currently selected game building.");
            Add("UI.Residence.Selected.Tooltip.Empty", "No building is selected in the game UI.");
            Add("UI.Residence.Selected.Label", "Selected building");
            Add("UI.Residence.Tag.Home", "Home");
            Add("UI.Residence.Tag.CityHall", "City Hall");
            Add("UI.Residence.Home.Title", "Mayor home");
            Add("UI.Residence.Workplace.Title", "Mayor workplace");
            Add("UI.Residence.Target.Status.Assigned", "Mayor assigned");
            Add("UI.Residence.Target.Status.Pending", "Move pending");
            Add("UI.Residence.Target.Status.None", "No target");
            Add("UI.Residence.Target.Home.Tooltip", "Saved mayor residence target.");
            Add("UI.Residence.Target.Workplace.Tooltip", "Saved mayor workplace target.");
            Add("UI.Residence.Target.Assigned.Tooltip", "The mayor is already assigned here.");
            Add("UI.Residence.Target.Move.Tooltip", "The assignment system will move the mayor here.");
            Add("UI.Residence.Focus.Tooltip.Enabled", "Move the camera to the selected mayor target.");
            Add("UI.Residence.Focus.Tooltip.Disabled", "No target is available to focus.");
            Add("UI.Residence.Focus", "Focus");
            Add("UI.Residence.Use.Tooltip.Selected", "This building is already selected.");
            Add("UI.Residence.Use.Tooltip.Home", "Assign the mayor's household to this low-density residence.");
            Add("UI.Residence.Use.Tooltip.Workplace", "Assign the mayor to work at this City Hall.");
            Add("UI.Residence.Use.Tooltip.HomeInvalid", "Selected building must be a low-density residential building.");
            Add("UI.Residence.Use.Tooltip.WorkplaceInvalid", "Selected building must be a City Hall asset.");
            Add("UI.Residence.Use", "Use selected");
            Add("UI.Residence.Choose.Home", "Choose a residence");
            Add("UI.Residence.Choose.Workplace", "Choose a City Hall");
            Add("UI.Residence.Empty.Home", "No low-density residences found");
            Add("UI.Residence.Empty.Workplace", "No City Hall assets found");
            Add("UI.Residence.Dropdown.Home.Tooltip", "Choose the low-density residential building where the mayor should live.");
            Add("UI.Residence.Dropdown.Workplace.Tooltip", "Choose the City Hall asset where the mayor should work.");
            Add("UI.Residence.Dropdown.Limited", "More eligible buildings exist. Select one in the city to add it here.");

            Add("UI.Mayor.Pending", "Pending");
            Add("UI.Mayor.Action.PlatformMeeting.Title", "Platform meeting");
            Add("UI.Mayor.Action.PlatformMeeting.Description", "The mayor will attempt to convince a selected candidate to soften their platform.");
            Add("UI.Mayor.Action.Choose", "Choose");
            Add("UI.Mayor.Action.Bribe", "Bribe");
            Add("UI.Mayor.Action.Endorsement.Title", "Mayor endorsement");
            Add("UI.Mayor.Action.Endorsement.Description", "Spend city funds to have the mayor endorse a selected candidate.");
            Add("UI.Mayor.Action.Endorse", "Endorse");
            Add("UI.Mayor.Action.EndorsedStatus", "Endorsed {name}");
            Add("UI.Mayor.Action.CashAssistance.Title", "Cash Assistance");
            Add("UI.Mayor.Action.CashAssistance.FundedTitle", "Cash Assistance funded");
            Add("UI.Mayor.Action.CashAssistance.Description", "Spend city funds to raise turnout for struggling and modest-income residents.");
            Add("UI.Mayor.Action.Fund", "Fund");
            Add("UI.Mayor.Action.Tampering.Title", "Vote-count tampering");
            Add("UI.Mayor.Action.Tampering.Description", "Spend city funds to arrange a late election-day disruption for a selected candidate.");
            Add("UI.Mayor.Action.Tamper", "Tamper");
            Add("UI.Mayor.Action.PlannedFor", "Planned for {name}");
            Add("UI.Mayor.Unavailable.Endorsed", "The mayor already endorsed {name} this election cycle.");
            Add("UI.Mayor.Unavailable.CashAssistance", "Cash Assistance is already funded this election cycle.");
            Add("UI.Mayor.Unavailable.Tampering", "A vote-count operation is already planned for {name} this election cycle.");
            Add("UI.Mayor.Unavailable.ElectionDay", "Mayor campaign actions are unavailable on election day.");
            Add("UI.Mayor.Unavailable.MeetingPending", "The mayor is trying to schedule this candidate meeting.");
            Add("UI.Mayor.Unavailable.ScheduleBlocked", "The mayor's schedule is blocked after today's campaign action.");
            Add("UI.Mayor.Unavailable.WaitingPopulation", "Mayor campaign actions unlock when Elections start.");
            Add("UI.Mayor.Unavailable.NoCandidates", "There are no candidates right now.");
            Add("UI.Mayor.Unavailable.Closed", "Mayor campaign actions are available during the campaign before election day.");
            Add("UI.Mayor.Unavailable.Generic", "Mayor campaign action unavailable right now.");
            Add("UI.Mayor.Portrait.Tooltip.Temporary", "Temporary mayor selected from real citizens. This mayor applies no city effects and supervises the transition until an election is completed.");
            Add("UI.Mayor.Portrait.Tooltip.Current", "Current elected mayor and active mayoral platform.");
            Add("UI.Mayor.Name.Label.Tooltip", "The citizen currently serving as mayor. Click the name to move the camera to this citizen.");
            Add("UI.Mayor.Name.Label", "Current mayor");
            Add("UI.Mayor.Name.Tooltip", "Click the mayor name to move the camera to this citizen.");
            Add("UI.Mayor.PartyReputation.Tooltip", "Party reputation: {reputation}/100.");
            Add("UI.Mayor.Platform.Tooltip", "Current mayoral platform and city effect. Temporary transition mayors apply no city modifiers.");
            Add("UI.Mayor.Actions.Header.Tooltip", "Mayor campaign actions are available before election day.");
            Add("UI.Mayor.Actions.Header", "Mayor campaign actions");
            Add("UI.Mayor.Actions.Cost.Tooltip", "Each mayor campaign action uses the current mayor campaign action cost.");
            Add("UI.Mayor.Actions.Status.Tooltip", "Current state of this mayor campaign action.");
            Add("UI.Mayor.Picker.Close.Tooltip", "Close candidate picker.");
            Add("UI.Mayor.Picker.CandidateFallback", "Candidate");
            Add("UI.Mayor.Picker.NoPlatform", "No platform");
            Add("UI.Mayor.Picker.Empty", "No active candidates right now.");
            Add("UI.Mayor.Picker.Title.Bribe", "Choose platform meeting target");
            Add("UI.Mayor.Picker.Title.Endorse", "Choose endorsement target");
            Add("UI.Mayor.Picker.Title.Tamper", "Choose vote-count target");
            Add("UI.Mayor.Picker.Title.Candidate", "Choose candidate");

            Add("UI.Party.Save.Tooltip.Disabled", "No party changes to save.");
            Add("UI.Party.Save.Tooltip.Enabled", "Save this party name and color.");
            Add("UI.Party.Reputation.Tooltip", "Persistent party reputation from 0 to 100.");
            Add("UI.Party.Reputation.Label", "Reputation:");
            Add("UI.Party.Wins.Tooltip", "Total elections won by this party.");
            Add("UI.Party.Wins.Label", "Wins:");
            Add("UI.Party.Terms.Tooltip", "Current consecutive terms in power.");
            Add("UI.Party.Terms.Label", "Terms:");
            Add("UI.Party.Replace.Tooltip.Enabled", "Open the party tag editor.");
            Add("UI.Party.Replace.Tooltip.Disabled", "Party tag replacement is unavailable right now.");
            Add("UI.Party.Replace", "Replace Tags");
            Add("UI.Party.TagEditor.Save.Tooltip.Disabled", "Party tag replacement is unavailable right now.");
            Add("UI.Party.TagEditor.Save.Tooltip.Count", "Select exactly three party tags.");
            Add("UI.Party.TagEditor.Save.Tooltip.Total", "Party tag values must add up to zero.");
            Add("UI.Party.TagEditor.Save.Tooltip.Unchanged", "Choose a different party tag set.");
            Add("UI.Party.TagEditor.Save.Tooltip.Ready", "Save these party tags.");
            Add("UI.Party.TagEditor.Instructions", "Select three party tags. The total must be 0.");
            Add("UI.Party.TagEditor.Full.Tooltip", "Remove a selected tag before choosing another.");

            Add("UI.Candidate.Donation.Tooltip.NoCandidate", "No candidate is available for donations.");
            Add("UI.Candidate.Donation.Tooltip.UsedToday", "Only one campaign donation can be made per day.");
            Add("UI.Candidate.Donation.Tooltip.Closed", "Donations are available when an active campaign has selected candidates before election day.");
            Add("UI.Candidate.Donation.Tooltip.Ready", "Donate {amount} of city funds to support this candidate's campaign before election day.");
            Add("UI.Candidate.Portrait.Tooltip", "Candidate portrait. Candidates are selected from real adult residents.");
            Add("UI.Candidate.Name.Tooltip", "Click the candidate name to move the camera to this citizen.");
            Add("UI.Candidate.Bio.Tooltip", "Candidate background generated from the resident's current life in the city.");
            Add("UI.Candidate.DonationTotal.Tooltip", "Effective campaign support credited to this candidate during the current campaign. Some tags can change cost or campaign effect.");
            Add("UI.Candidate.DonationTotal", "Total donations");
            Add("UI.Candidate.Platform.Tooltip", "Candidate platform. The positive effect is shown first, followed by the tradeoff.");
            Add("UI.Candidate.Platform", "Candidate Platform");
            Add("UI.Candidate.DonationHeader.Tooltip", "City-funded campaign support. Donations are available while the mayoral race is active before election day.");
            Add("UI.Candidate.DonationHeader", "Campaign donation");
            Add("UI.Candidate.Donate", "Donate {amount}");
            Add("UI.Candidate.DonationsClosed.Help", "Donations open before election day once candidates are selected.");
            Add("UI.Candidate.DonationUsed.Help", "A campaign donation has already been made today.");
            Add("UI.Candidate.GenericArticle", "a candidate");
            Add("UI.Party.FallbackNumber", "Party {number}");

            Add("Panel.Stage.WaitingForResidents", "Waiting for residents");
            Add("Panel.Stage.NoElectionData", "No election data");
            Add("Panel.Stage.NoActiveCampaign", "No active campaign");
            Add("Panel.Stage.RunoffCandidatesSelected", "Runoff candidates selected");
            Add("Panel.Stage.CandidatesSelected", "Candidates selected");
            Add("Panel.Stage.RunoffPollReleased", "Runoff poll released");
            Add("Panel.Stage.PollReleased", "Poll released");
            Add("Panel.Stage.RunoffElectionDay", "Runoff election day");
            Add("Panel.Stage.ElectionDay", "Election day");
            Add("Panel.Stage.TransitionPeriod", "Transition period");
            Add("Panel.Stage.MayorTermActive", "Mayor term active");
            Add("Panel.Cycle.WaitingPopulation", "Elections start at {0:n0} population. Current population: {1:n0}.");
            Add("Panel.Cycle.NotInitialized", "The election system has not initialized yet.");
            Add("Panel.Cycle.RunoffRace", "Runoff mayoral race");
            Add("Panel.Cycle.RegularRunoffRace", "Regular mayoral race with runoff voting");
            Add("Panel.Cycle.AcceleratedRace", "Accelerated mayoral race");
            Add("Panel.Cycle.RegularRace", "Regular mayoral race");
            Add("Panel.Cycle.PendingMayorNoCurrent", "{0} takes office on {1}.");
            Add("Panel.Cycle.PendingMayorWithCurrent", "{0} takes office on {1}. {2} is serving as mayor until then.");
            Add("Panel.Cycle.MayorServing", "{0} is serving as mayor.");
            Add("Panel.Cycle.NoActiveCampaignSentence", "No active campaign.");
            Add("Panel.Candidate.EffectDescription", "If elected, this platform {0}.");
            Add("Panel.Candidate.NoPlatform", "No platform");
            Add("Panel.Candidate.EmptyDescription", "No candidate has been selected yet.");
            Add("Panel.Candidate.Fallback.0", "Candidate A");
            Add("Panel.Candidate.Fallback.1", "Candidate B");
            Add("Panel.Candidate.Fallback.2", "Candidate C");
            Add("Panel.Candidate.Fallback.3", "Candidate D");
            Add("Panel.Candidate.Fallback.Generic", "Candidate");
            Add("Panel.MayorElect", "Mayor-elect");
            Add("Panel.MayorElect.Article", "The mayor-elect");
            Add("Panel.Mayor.TemporaryDescription", "Temporary mayoral platform. No city modifiers are applied; this mayor supervises the transition until an elected mayor takes office.");
            Add("Panel.Mayor.CurrentDescription", "Current mayoral platform. It {0}.");
            Add("Panel.Mayor.NoHome", "No low-density residence selected");
            Add("Panel.Mayor.NoWorkplace", "No City Hall selected");
            Add("Panel.Mayor.NoBuilding", "No building selected");
            Add("Panel.Mayor.Residence", "Residence");
            Add("Panel.Mayor.CityHall", "City Hall");
            Add("Panel.Donation.CampaignSupport", "Campaign support");
            Add("Panel.Party.Replace.Disabled.Cycle", "Party tags can only be replaced outside the election cycle.");
            Add("Panel.Party.Replace.Disabled.NoYear", "Party tag replacement needs the current city year.");
            Add("Panel.Party.Replace.Disabled.UsedYear", "This party has already replaced its tags this year.");
            Add("Panel.Party.Fallback", "Party");
            Add("Panel.Party.Default.0", "Purple Civic Alliance");
            Add("Panel.Party.Default.1", "Green Future Coalition");
            Add("Panel.Party.Default.2", "Pink Liberty Party");
            Add("Panel.Party.Default.3", "Gold Prosperity League");
            Add("Panel.Poll.Age.Teens", "Teens");
            Add("Panel.Poll.Age.Adults", "Adults");
            Add("Panel.Poll.Age.Elderly", "Elderly");
            Add("Panel.Poll.Education.Uneducated", "Uneducated");
            Add("Panel.Poll.Education.PoorlyEducated", "Poorly educated");
            Add("Panel.Poll.Education.Educated", "Educated");
            Add("Panel.Poll.Education.WellEducated", "Well educated");
            Add("Panel.Poll.Education.HighlyEducated", "Highly educated");
            Add("Panel.Poll.Income.Struggling", "Struggling");
            Add("Panel.Poll.Income.Modest", "Modest income");
            Add("Panel.Poll.Income.Middle", "Middle income");
            Add("Panel.Poll.Income.Comfortable", "Comfortable");
            Add("Panel.Poll.Income.Wealthy", "Wealthy");
            Add("Panel.Support.Disabled.ElectionDay", "Civic programs are unavailable on election day.");
            Add("Panel.Support.Disabled.UsedToday", "Only one civic program can be funded per day. Today's program is {0}.");
            Add("Panel.Support.Disabled.HolidayScheduled", "Election day is already scheduled as a holiday.");
            Add("Panel.Support.Disabled.Closed", "Civic programs are available before election day once candidates are selected.");
            Add("Panel.Support.Disabled.Generic", "Civic program unavailable right now.");
            Add("Panel.Support.Fallback", "a civic program");
            Add("Panel.Legislation.Disabled.ElectionDay", "Election legislation is unavailable on election day.");
            Add("Panel.Legislation.Disabled.UsedToday", "Only one election legislation action can be attempted per day.");
            Add("Panel.Legislation.Disabled.Closed", "Election legislation can be changed before election day once candidates are selected.");

            Add("Lifecycle.Department.ElectionBoard", "Election Board");
            Add("Lifecycle.Department.Police", "Police Department");
            Add("Lifecycle.Department.FireRescue", "Fire & Rescue");
            Add("Lifecycle.Name.Candidate", "the candidate");
            Add("Lifecycle.Name.Mayor", "the mayor");
            Add("Lifecycle.Name.FormerMayor", "the former mayor");
            Add("Lifecycle.Name.City", "the city");
            Add("Lifecycle.Name.Field", "the field");
            Add("Lifecycle.Name.OpposingCampaign", "the opposing campaign");
            Add("Lifecycle.Name.MyVotingSite", "my voting site");
            Add("Lifecycle.Name.VotingSite", "a voting site");
            Add("Lifecycle.Name.ThatVotingSite", "that voting site");
            Add("Lifecycle.Name.CelebrationSite", "the celebration site");
            Add("Lifecycle.Name.TemporaryMayor", "Temporary Mayor");
            Add("Lifecycle.List.And", " and ");
            Add("Lifecycle.MinimumPopulation.StartCampaign", "The Election Board will not start a campaign until the city reaches {0:n0} population.");
            Add("Lifecycle.MinimumPopulation.StartVoting", "The Election Board will not start voting until the city reaches {0:n0} population.");
            Add("Lifecycle.MinimumPopulation.GeneratePoll", "The Election Board will not generate a poll until the city reaches {0:n0} population.");
            Add("Lifecycle.Poll.DebugUnavailableElectionDay", "Debug poll generation is unavailable on election day.");
            Add("Lifecycle.PartyTags.ReplaceOutsideCycle", "Party tags can only be replaced outside the election cycle.");
            Add("Lifecycle.PartyTags.AlreadyReplacedSetThisYear", "{0} already replaced party tags this year.");
            Add("Lifecycle.PartyTags.ChooseExactlyThree", "Choose exactly three party tags before saving.");
            Add("Lifecycle.PartyTags.MustBeUnique", "Party tags must be unique.");
            Add("Lifecycle.PartyTags.MustBalance", "Party tag values must add up to zero.");
            Add("Lifecycle.PartyTags.AlreadyHasSet", "{0} already has that party tag set.");
            Add("Lifecycle.PartyTags.ReplacedSet", "{0} replaced its party tags.");
            Add("Lifecycle.PartyTags.AlreadyReplacedTagThisYear", "{0} already replaced a party tag this year.");
            Add("Lifecycle.PartyTags.NoBalancedReplacement", "{0} has no balanced replacement available for that tag.");
            Add("Lifecycle.PartyTags.ReplacedTag", "{0} replaced {1} with {2}.");
            Add("Lifecycle.Donation.NoDate", "Campaign donations need the current city date and are unavailable right now.");
            Add("Lifecycle.Donation.NoRace", "Campaign donations are available while an active mayoral race has selected candidates.");
            Add("Lifecycle.Donation.ElectionDay", "Campaign donations are closed on election day.");
            Add("Lifecycle.Donation.Closed", "Campaign donations are available before election day while an active mayoral race has selected candidates.");
            Add("Lifecycle.Donation.UsedToday", "Only one campaign donation can be made per day.");
            Add("Lifecycle.Donation.InsufficientFunds", "The city does not have enough money to donate {0:n0}.");
            Add("Lifecycle.Donation.ThankYou.Support", "your campaign support totaling {0:n0}");
            Add("Lifecycle.Donation.ThankYou.Softened", " The campaign has softened its platform: {0} changed from {1} to {2}.");
            Add("Lifecycle.Donation.ThankYou.Message", "Thank you for {0}. Total donated to my campaign so far is {1:n0}.{2}");
            Add("Lifecycle.Support.Closed", "Civic programs are available before election day while an active mayoral race has selected candidates.");
            Add("Lifecycle.Support.UsedToday", "Only one civic program can be funded per day. Today's program is {0}.");
            Add("Lifecycle.Support.HolidayScheduled", "Election day is already scheduled as a holiday.");
            Add("Lifecycle.Support.InsufficientFunds", "The city does not have enough money to fund {0} for {1:n0}.");
            Add("Lifecycle.Support.Funded", "{0} funded for {1:n0}. {2}");
            Add("Lifecycle.Support.Outcome.Holiday", "Election day will be treated as a holiday for resident schedules.");
            Add("Lifecycle.Support.Outcome.Teen", "Teen election turnout bonus is now +{0}%.");
            Add("Lifecycle.Support.Outcome.Adult", "Adult election turnout bonus is now +{0}%.");
            Add("Lifecycle.Support.Outcome.Elderly", "Elderly election turnout bonus is now +{0}%.");
            Add("Lifecycle.Support.Outcome.Education", "Uneducated and poorly educated election turnout bonuses are now +{0}%.");
            Add("Lifecycle.Support.Outcome.LowIncome", "Struggling and modest-income resident turnout bonus is now +{0}%.");
            Add("Lifecycle.Support.Outcome.Transit", "Transit voucher turnout bonus is now +{0}% for eligible residents without cars.");
            Add("Lifecycle.Support.Outcome.CivicForums", "Educated resident turnout bonus is now +{0}%.");
            Add("Lifecycle.Support.Outcome.Generic", "The civic program is active.");
            Add("Lifecycle.Bribe.Closed", "Mayoral platform meetings are only available before election day while an active race has selected candidates.");
            Add("Lifecycle.Bribe.ScheduleBlocked", "The mayor's schedule is already reserved for a candidate platform meeting attempt.");
            Add("Lifecycle.Bribe.InsufficientFunds", "The city does not have enough money to fund a {0:n0} mayoral outreach effort.");
            Add("Lifecycle.Bribe.Travel.Mayor", "Heading to {{LINK_3}} to meet {{LINK_2}} about a campaign platform discussion.");
            Add("Lifecycle.Bribe.Travel.Candidate", "Heading to {{LINK_3}} to meet {{LINK_2}} about the campaign platform.");
            Add("Lifecycle.Bribe.Unable", "Election Board reports {0} tried to meet {1} for a campaign platform discussion, but no suitable time and leisure venue could be arranged within 24 in-game hours.");
            Add("Lifecycle.Endorse.Closed", "Mayoral endorsements are only available before election day while an active race has selected candidates.");
            Add("Lifecycle.Endorse.AlreadyUsed", "The mayor has already endorsed a candidate in this election cycle.");
            Add("Lifecycle.Endorse.InsufficientFunds", "The city does not have enough money to fund a {0:n0} mayoral endorsement effort.");
            Add("Lifecycle.MayorAction.ScheduleBlocked", "The mayor's schedule is already reserved for a campaign action today.");
            Add("Lifecycle.CashAssistance.Closed", "Cash Assistance is only available before election day while an active race has selected candidates.");
            Add("Lifecycle.CashAssistance.AlreadyFunded", "Cash Assistance has already been funded this election cycle.");
            Add("Lifecycle.CashAssistance.InsufficientFunds", "The city does not have enough money to fund a {0:n0} Cash Assistance operation.");
            Add("Lifecycle.CashAssistance.Funded", "Cash Assistance funded. Struggling and modest-income residents get +{0}% election turnout.");
            Add("Lifecycle.Tamper.Closed", "Vote-count tampering can only be arranged before election day while an active race has selected candidates.");
            Add("Lifecycle.Tamper.AlreadyPlanned", "A vote-count tampering operation is already planned for this election cycle.");
            Add("Lifecycle.Tamper.InsufficientFunds", "The city does not have enough money to fund a {0:n0} vote-count operation.");
            Add("Lifecycle.Legislation.Closed", "Election legislation can only be changed before election day while an active race has selected candidates.");
            Add("Lifecycle.Legislation.UsedToday", "Only one election legislation action can be attempted per day.");
            Add("Lifecycle.Legislation.AlreadyActive", "{0} is already in effect.");
            Add("Lifecycle.Legislation.AlreadyRepealed", "{0} is already repealed.");
            Add("Lifecycle.Legislation.InsufficientFunds", "The city does not have enough money to fund a {0:n0} legislation {1}.");
            Add("Lifecycle.Legislation.Action.Proposal", "proposal");
            Add("Lifecycle.Legislation.Action.Repeal", "repeal");
            Add("Lifecycle.Legislation.Outcome.Passed", "{0} passed after lobbying.");
            Add("Lifecycle.Legislation.Outcome.Repealed", "{0} was repealed after lobbying.");
            Add("Lifecycle.Legislation.Outcome.FailedPass", "{0} failed to pass after lobbying.");
            Add("Lifecycle.Legislation.Outcome.FailedRepeal", "The repeal of {0} failed after lobbying.");
            Add("Lifecycle.Legislation.Outcome.WithChance", "{0} Success chance was {1}% based on incumbent party reputation.");
            Add("Lifecycle.Replacement.InheritedDonation", " Existing campaign donations totaling {0:n0} remain with this campaign.");
            Add("Lifecycle.Replacement.Certified", "The Election Board says {0} can no longer participate because their resident record is no longer eligible. {1} is now certified for the mayoral race.{2} Poll: {3}. Election: {4}.");
            Add("Lifecycle.Replacement.NoEligible", "The Election Board found that {0} can no longer participate, but no eligible replacement candidate is currently available. The board will keep checking for an eligible resident.");
            Add("Lifecycle.Replacement.RevisedPoll.TwoLinks", "Because the candidate field changed, the Election Board has issued an updated campaign poll: {{LINK_1}} {0}%, {{LINK_2}} {1}%, undecided {2}% with a +/-{3}% margin of error. {4}.");
            Add("Lifecycle.Replacement.RevisedPoll.Named", "Because the candidate field changed, the Election Board has issued an updated campaign poll: {0}, undecided {1}% with a +/-{2}% margin of error. {3}.");
            Add("Lifecycle.TemporaryMayor.Intro", "I am {{LINK_1}}. I will serve as temporary mayor under the Democratic Transition platform: no new city policy changes, only supervising the election process until residents choose a mayor.");
            Add("Lifecycle.Initialize.Active", "Elections is active. The next regular mayoral campaign begins on {0}.");
            Add("Lifecycle.Party.Milestone", "{0} received credit for the city's new milestone. Party reputation +{1}.");
            Add("Lifecycle.PartyEvent.Negative.Major.0", "A corruption scandal damaged {0}. Party reputation -{1}.");
            Add("Lifecycle.PartyEvent.Negative.Major.1", "Investigators raised serious ethics questions around {0}. Party reputation -{1}.");
            Add("Lifecycle.PartyEvent.Negative.Major.2", "A donor influence scandal put {0} under pressure. Party reputation -{1}.");
            Add("Lifecycle.PartyEvent.Negative.Minor.0", "Affair allegations involving senior {0} figures hurt public trust. Party reputation -{1}.");
            Add("Lifecycle.PartyEvent.Negative.Minor.1", "{0} faced criticism over a donor disclosure mistake. Party reputation -{1}.");
            Add("Lifecycle.PartyEvent.Negative.Minor.2", "A minor ethics complaint made headlines for {0}. Party reputation -{1}.");
            Add("Lifecycle.PartyEvent.Negative.Minor.Incumbent", "Residents blamed {0} for a messy City Hall decision. Party reputation -{1}.");
            Add("Lifecycle.PartyEvent.Negative.Minor.Challenger", "{0} drew criticism for an unpopular campaign statement. Party reputation -{1}.");
            Add("Lifecycle.PartyEvent.Positive.0", "A clean audit improved trust in {0}. Party reputation +{1}.");
            Add("Lifecycle.PartyEvent.Positive.1", "{0} received praise for transparent public service work. Party reputation +{1}.");
            Add("Lifecycle.PartyEvent.Positive.Incumbent", "Residents credited {0} for a steady city response. Party reputation +{1}.");
            Add("Lifecycle.PartyEvent.Positive.Challenger", "{0} gained attention for a constructive policy proposal. Party reputation +{1}.");
            Add("Lifecycle.PartyEvent.Positive.3", "{0} benefited from positive local coverage. Party reputation +{1}.");
            Add("Lifecycle.Campaign.NoEligibleCandidates", "The election board could not find {0} eligible adult residents for the mayoral race.");
            Add("Lifecycle.Campaign.Started.TwoLinks", "The mayoral race has begun. The candidates are {{LINK_1}} and {{LINK_2}}. Poll: {0}. Election: {1}.");
            Add("Lifecycle.Campaign.Started.Named", "The mayoral race has begun with {0} candidates: {1}. Poll: {2}. Election: {3}.");
            Add("Lifecycle.Platform.CandidateIntro", "I am {{LINK_1}}, {0}. My platform {1}.");
            Add("Lifecycle.Poll.Release.TwoLinks", "Campaign poll released on {0} from {1:n0} sampled eligible residents: {{LINK_1}} {2}%, {{LINK_2}} {3}%, undecided {4}% with a +/-{5}% margin of error. {6}. Donations remain available in the Elections options panel.");
            Add("Lifecycle.Poll.Release.Named", "Campaign poll released on {0} from {1:n0} sampled eligible residents: {2}, undecided {3}% with a +/-{4}% margin of error. {5}. Donations remain available in the Elections options panel.");
            Add("Lifecycle.PollResponse.WithCta", "{0} Donations are open, and every contribution helps move this race.");
            Add("Lifecycle.PollResponse.DeadHeat", "The latest poll is a statistical dead heat against {0}.");
            Add("Lifecycle.PollResponse.Ahead", "The latest poll has us ahead of {0}, outside the +/-{1}% margin of error.");
            Add("Lifecycle.PollResponse.Behind", "The latest poll has us behind {0}, but undecided voters can still move this race.");
            Add("Lifecycle.PollResponse.Tied", "The latest poll has us tied with {0}.");
            Add("Lifecycle.Reminder.0", "Tomorrow is election day. Polls are open from {0}, and I am asking for your vote.");
            Add("Lifecycle.Reminder.1", "Tomorrow, residents choose the next mayor. My platform {0}, and every vote can shape the city.");
            Add("Lifecycle.Reminder.2", "Make a plan to vote tomorrow from {0}. This race is about {1}, and your voice matters.");
            Add("Lifecycle.Reminder.3", "Tomorrow's election is a choice: my platform {0}, while {1}'s platform {2}.");
            Add("Lifecycle.Reminder.4", "{0} still has to explain why their platform {1}. Tomorrow, voters can demand better.");
            Add("Lifecycle.Reminder.5", "One day remains before the election, and I am ready to serve. Please vote tomorrow from {0}.");
            Add("Lifecycle.StrictVotingId.Passed", "The stricter voting ID proposal passed. Election staff will apply the new verification rules for this mayoral race.");
            Add("Lifecycle.StrictVotingId.Failed", "The stricter voting ID proposal did not pass. Voting rules will stay unchanged for this mayoral race.");
            Add("Lifecycle.Endorse.Board", "The mayor endorsed {0} for mayor. Happy residents may give that endorsement extra weight in this election.");
            Add("Lifecycle.Endorse.Mayor", "I endorse {0} for mayor. Residents who are happy with the city's direction should know I trust them to carry this work forward.");
            Add("Lifecycle.PlatformMeeting.Board.Success", "{0} agreed to revise their platform after a mayoral meeting.");
            Add("Lifecycle.PlatformMeeting.Board.Failed", "{0}'s platform did not change after a mayoral meeting.");
            Add("Lifecycle.PlatformMeeting.Mayor.Success", "I met with {0} today to discuss their platform. We found enough common ground that they agreed to revise it. Their updated tradeoff is {1} {2}.");
            Add("Lifecycle.PlatformMeeting.Mayor.Failed", "I met with {0} today to discuss their platform. The conversation was constructive, but we still disagreed on key points, and I was not able to persuade them to revise it.");
            Add("Lifecycle.CorruptionInvestigation.Mayor", "Police confirm {0} is facing a corruption investigation after allegations of mayoral campaign bribery.");
            Add("Lifecycle.Election.Start.TwoLinks", "Election day has begun on {0} for {{LINK_1}} and {{LINK_2}}. Polls are open from {1} at education, welfare, administration, and postal buildings. You can watch voting in real time by clicking the Voting sites button to view the overlay. Results will be announced at {2}.");
            Add("Lifecycle.Election.Start.Named", "Election day has begun on {0} for {1}. Polls are open from {2} at education, welfare, administration, and postal buildings. You can watch voting in real time by clicking the Voting sites button to view the overlay. Results will be announced at {3}.");
            Add("Lifecycle.Election.VotingClosed", "All voting sites are now closed. Results of the election will be announced at {0}.");
            Add("Lifecycle.Tamper.Loss.Destroyed", "Fire & Rescue confirms {0} was destroyed. Election Board says all remaining ballots at that site were lost: {1}.");
            Add("Lifecycle.Tamper.Loss.Fire", "Fire crews are responding to a fire at {0}. Election Board says {1:n0} ballots were destroyed before they could be counted: {2}.");
            Add("Lifecycle.Tamper.Loss.NoBallots", "no ballots");
            Add("Lifecycle.Tamper.Protest", "Our campaign lost {0:n0} votes after the fire at {1}. This count cannot be trusted, and {2} should support a full investigation.");
            Add("Lifecycle.CorruptionArrest.Candidate.Singular", "Police confirm {{LINK_1}} has been arrested after an election-corruption investigation. Detectives linked the campaign to {0} suspicious mayoral campaign action.");
            Add("Lifecycle.CorruptionArrest.Candidate.Plural", "Police confirm {{LINK_1}} has been arrested after an election-corruption investigation. Detectives linked the campaign to {0} suspicious mayoral campaign actions.");
            Add("Lifecycle.CorruptionArrest.Mayor.Singular", "Police confirm {{LINK_1}} has been arrested after a mayoral bribery investigation. Detectives linked the former mayor to {0} suspicious campaign action.");
            Add("Lifecycle.CorruptionArrest.Mayor.Plural", "Police confirm {{LINK_1}} has been arrested after a mayoral bribery investigation. Detectives linked the former mayor to {0} suspicious campaign actions.");
            Add("Lifecycle.OutgoingMayor.FarewellDonation", "Thank you, {0}, for the time I was mayor. I am donating {1:n0} back to the city as I leave office.");
            Add("Lifecycle.OutgoingMayor.Farewell", "Thank you, {0}, for the time I was mayor. Serving this city has been an honor.");
            Add("Lifecycle.Runoff.VotesCounted", "{0:n0} votes counted");
            Add("Lifecycle.Runoff.NoVotesCounted", "no votes counted");
            Add("Lifecycle.Runoff.Started.TwoLinks", "No mayoral candidate reached 50% after {0}. {{LINK_1}} ({1}%) and {{LINK_2}} ({2}%) advance to a runoff. The runoff poll is {3} at 8 AM, and the final election is {4}. Current mayoral programs and legislation continue into the runoff.");
            Add("Lifecycle.Runoff.Started.Named", "No mayoral candidate reached 50% after {0}. {1} ({2}%) and {3} ({4}%) advance to a runoff. The runoff poll is {5} at 8 AM, and the final election is {6}. Current mayoral programs and legislation continue into the runoff.");
            Add("Lifecycle.Runoff.Endorsement.Bonus", " Our {0}% share of the first-round vote becomes a {1} runoff support bonus for their campaign.");
            Add("Lifecycle.Runoff.Endorsement.NoBonus", " We did not receive enough first-round votes to move the runoff math.");
            Add("Lifecycle.Runoff.Endorsement.Candidate", "We came up short with {0:n0} of {1:n0} votes. I am supporting {2} in the runoff.{3}");
            Add("Lifecycle.Runoff.Endorsement.Board", "{0} endorsed {1} in the runoff.{2}");
            Add("Lifecycle.Results.SupportersNamed", " Supporters are gathering at {0}.");
            Add("Lifecycle.Results.SupportersLink3", " Supporters are gathering at {{LINK_3}}.");
            Add("Lifecycle.Results.SupportersLink1", " Supporters are gathering at {{LINK_1}}.");
            Add("Lifecycle.Results.TurnoutDenominator.Eligible", "eligible voters ({0:n0} of {1:n0})");
            Add("Lifecycle.Results.TurnoutDenominator.Votes", "eligible voters ({0:n0} votes)");
            Add("Lifecycle.Results.Transition.Today", " The new mayor takes office today.");
            Add("Lifecycle.Results.Transition.Future", " The mayor-elect will take office on {0}; the current mayor remains in office until then.");
            Add("Lifecycle.Results.Intro", "Election results for {0} are final. {1} has been elected mayor. Turnout was {2}% of {3}.");
            Add("Lifecycle.Results.CandidateLinks", " Results: {{LINK_1}} {0}%, {{LINK_2}} {1}%.");
            Add("Lifecycle.Results.CandidateNames", " Results: {0}.");
            Add("Lifecycle.Results.Platform.NewMayor", "The new mayor's platform");
            Add("Lifecycle.Results.Platform.MayorElect", "The mayor-elect's platform");
            Add("Lifecycle.Results.PlatformText", " {0} {1}.{2}");
            Add("Lifecycle.PendingMayor.Inaugurated", "{0} takes office as mayor today. The election transition is complete, and the new mayoral platform is now active.");
            Add("Lifecycle.Victory.Transition.Future", " We take office on {0}.");
            Add("Lifecycle.Victory.Transition.Tomorrow", " Tomorrow we get to work.");
            Add("Lifecycle.Victory.Winner.AtVenue", "Thank you, {0}. We won tonight, and I am celebrating with supporters at {{LINK_2}}.{1}");
            Add("Lifecycle.Victory.Winner.NoVenue", "Thank you, {0}. We won tonight, and I am celebrating with supporters.{1}");
            Add("Lifecycle.Victory.Loser.Role.MayorElect", "as mayor-elect");
            Add("Lifecycle.Victory.Loser.Role.Mayor", "as mayor");
            Add("Lifecycle.Victory.Loser.Reject", "I do not accept tonight's result. The margin was {0:0.#}%, and our campaign is asking for a full review of the count.");
            Add("Lifecycle.Victory.Loser.Concede", "Tonight did not go our way. I congratulate {0} and wish them well {1}.");
            Add("Lifecycle.VotingTrips.MissingRealisticTrips", "Election voting trips require Realistic Trips. No residents can be sent to polling places through the current bridge.");
            Add("Lifecycle.Vote.IJustVoted", "I just voted at {0}. Polls are open until {1}; please make sure your voice is counted.");

            Add("Model.Profile.Bio", "{0} resident from a {1}, {2}, {3}{4}.");
            Add("Model.Profile.ChirpIntro", "{0} {1} {2} from a {3}");
            Add("Model.Profile.Article.A", "a");
            Add("Model.Profile.Article.An", "an");
            Add("Model.Profile.Car.With", " with a registered car");
            Add("Model.Profile.Car.Without", " without a registered car");
            Add("Model.Profile.Age.Elderly", "Elderly");
            Add("Model.Profile.Age.Adult", "Adult");
            Add("Model.Profile.Age.Resident", "Resident");
            Add("Model.Profile.Education.Uneducated", "Uneducated");
            Add("Model.Profile.Education.PoorlyEducated", "Poorly educated");
            Add("Model.Profile.Education.Educated", "Educated");
            Add("Model.Profile.Education.WellEducated", "Well educated");
            Add("Model.Profile.Education.HighlyEducated", "Highly educated");
            Add("Model.Profile.Work.Student", "Student");
            Add("Model.Profile.Work.Working", "Working resident");
            Add("Model.Profile.Work.NonWorking", "Non-working resident");
            Add("Model.Profile.Wealth.Struggling", "Struggling household");
            Add("Model.Profile.Wealth.Modest", "Modest-income household");
            Add("Model.Profile.Wealth.Middle", "Middle-income household");
            Add("Model.Profile.Wealth.Comfortable", "Comfortable household");
            Add("Model.Profile.Wealth.Wealthy", "Wealthy household");

            Add("Model.Poll.NoSample.Label", "No poll sample");
            Add("Model.Poll.NoSample.Description", "No eligible residents were sampled.");
            Add("Model.Poll.DeadHeat.Label", "Statistical dead heat");
            Add("Model.Poll.DeadHeat.TwoCandidateDescription", "{0} and {1} are within the +/-{2}% margin of error.");
            Add("Model.Poll.DeadHeat.MultiCandidateDescription", "The leading candidates are within the +/-{0}% margin of error.");
            Add("Model.Poll.NarrowEdge.Label", "{0} has a narrow edge");
            Add("Model.Poll.NarrowEdge.Description", "{0} leads, but the race is still close with a +/-{1}% margin of error.");
            Add("Model.Poll.OutsideMargin.Label", "{0} leads outside the margin");
            Add("Model.Poll.OutsideMargin.Description", "{0} leads by {1} points, outside the +/-{2}% margin of error.");
            Add("Model.Poll.NoCandidate", "No candidate");

            Add("Model.Platform.Name", "Mayoral Agenda");
            Add("Model.Platform.Description", "{0}, but {1}");
            Add("Model.Platform.Money.PositiveSentence", "{0} {1:n0} to {2}");
            Add("Model.Platform.Money.NegativeSentence", "{0} {1:n0} from {2}");
            Add("Model.Platform.PercentSentence", "{0} {1} by {2}");
            Add("Model.Platform.DoubleSentence", "doubles {0}");
            Add("Model.Platform.HalfSentence", "cuts {0} in half");
            Add("Model.Platform.NoPlatform", "No platform");
            Add("Model.Platform.NeutralSentence", "keeps city policy neutral");
            Add("Model.Platform.Transition.Name", "Democratic Transition");
            Add("Model.Platform.Transition.Description", "keeps city policy neutral while supervising the election process until residents elect a mayor");
            Add("Model.Platform.Verb.adds", "adds");
            Add("Model.Platform.Verb.lowers", "lowers");
            Add("Model.Platform.Verb.raises", "raises");
            Add("Model.Platform.Verb.removes", "removes");
            Add("Model.Platform.Verb.doubles", "doubles");
            Add("Model.Platform.Verb.reduces", "reduces");
            Add("Model.Platform.Verb.cuts", "cuts");
            Add("Model.Platform.Verb.increases", "increases");
            Add("Model.Platform.Money.Label", "City funds");
            Add("Model.Platform.Money.Target", "city funds");
            Add("Model.Platform.ImportCost.Label", "Import costs");
            Add("Model.Platform.ImportCost.Target", "import costs");
            Add("Model.Platform.ExportCost.Label", "Export costs");
            Add("Model.Platform.ExportCost.Target", "export costs");
            Add("Model.Platform.LoanInterest.Label", "Loan interest");
            Add("Model.Platform.LoanInterest.Target", "loan interest");
            Add("Model.Platform.BuildingLevelingCost.Label", "Building leveling cost");
            Add("Model.Platform.BuildingLevelingCost.Target", "building leveling costs");
            Add("Model.Platform.TaxiStartingFee.Label", "Taxi starting fee");
            Add("Model.Platform.TaxiStartingFee.Target", "taxi starting fees");
            Add("Model.Platform.CityServiceUpkeep.Label", "City service upkeep");
            Add("Model.Platform.CityServiceUpkeep.Target", "city service upkeep");
            Add("Model.Platform.CrimeProbability.Label", "Crime probability");
            Add("Model.Platform.CrimeProbability.Target", "crime probability");
            Add("Model.Platform.CrimeAccumulation.Label", "Crime accumulation");
            Add("Model.Platform.CrimeAccumulation.Target", "crime accumulation");
            Add("Model.Platform.DiseaseProbability.Label", "Disease probability");
            Add("Model.Platform.DiseaseProbability.Target", "disease probability");
            Add("Model.Platform.HospitalEfficiency.Label", "Hospital efficiency");
            Add("Model.Platform.HospitalEfficiency.Target", "hospital efficiency");
            Add("Model.Platform.PollutionHealthAffect.Label", "Pollution health impact");
            Add("Model.Platform.PollutionHealthAffect.Target", "pollution health impact");
            Add("Model.Platform.IndustrialAirPollution.Label", "Industrial air pollution");
            Add("Model.Platform.IndustrialAirPollution.Target", "industrial air pollution");
            Add("Model.Platform.IndustrialGroundPollution.Label", "Industrial ground pollution");
            Add("Model.Platform.IndustrialGroundPollution.Target", "industrial ground pollution");
            Add("Model.Platform.IndustrialGarbage.Label", "Industrial garbage");
            Add("Model.Platform.IndustrialGarbage.Target", "industrial garbage");
            Add("Model.Platform.IndustrialEfficiency.Label", "Industrial efficiency");
            Add("Model.Platform.IndustrialEfficiency.Target", "industrial efficiency");
            Add("Model.Platform.OfficeEfficiency.Label", "Office efficiency");
            Add("Model.Platform.OfficeEfficiency.Target", "office efficiency");
            Add("Model.Platform.IndustrialElectronicsEfficiency.Label", "Electronics efficiency");
            Add("Model.Platform.IndustrialElectronicsEfficiency.Target", "industrial electronics efficiency");
            Add("Model.Platform.OfficeSoftwareEfficiency.Label", "Software efficiency");
            Add("Model.Platform.OfficeSoftwareEfficiency.Target", "office software efficiency");
            Add("Model.Platform.IndustrialElectronicsDemand.Label", "Electronics demand");
            Add("Model.Platform.IndustrialElectronicsDemand.Target", "industrial electronics demand");
            Add("Model.Platform.OfficeSoftwareDemand.Label", "Software demand");
            Add("Model.Platform.OfficeSoftwareDemand.Target", "office software demand");
            Add("Model.Platform.Attractiveness.Label", "City attractiveness");
            Add("Model.Platform.Attractiveness.Target", "city attractiveness");
            Add("Model.Platform.Entertainment.Label", "Entertainment");
            Add("Model.Platform.Entertainment.Target", "entertainment");
            Add("Model.Platform.ParkEntertainment.Label", "Park entertainment");
            Add("Model.Platform.ParkEntertainment.Target", "park entertainment");
            Add("Model.Platform.CollegeGraduation.Label", "College graduation");
            Add("Model.Platform.CollegeGraduation.Target", "college graduation");
            Add("Model.Platform.UniversityGraduation.Label", "University graduation");
            Add("Model.Platform.UniversityGraduation.Target", "university graduation");
            Add("Model.Platform.UniversityInterest.Label", "University interest");
            Add("Model.Platform.UniversityInterest.Target", "university interest");
            Add("Model.Platform.AccumulatedXP.Label", "Accumulated XP");
            Add("Model.Platform.AccumulatedXP.Target", "accumulated XP");
            Add("Model.Platform.ResourceConsumption.Label", "Citizen resource consumption");
            Add("Model.Platform.ResourceConsumption.Target", "citizen resource consumption");

            AddCandidateTagEntries(Add);
            AddPartyTagEntries(Add);
            AddSupportProgramEntries(Add);
            AddLegislationEntries(Add);
        }

        private static void AddCandidateTagEntries(System.Action<string, string> add)
        {
            void Tag(int id, string name, string description)
            {
                add($"Model.CandidateTag.{id}.Name", name);
                add($"Model.CandidateTag.{id}.Description", description);
            }

            Tag(1, "Corrupt", "If elected, mayor campaign actions cost half as much.");
            Tag(2, "Honest", "If elected, mayor campaign actions cost twice as much.");
            Tag(3, "Humble beginnings", "+10% support from low-income residents.");
            Tag(4, "Controversial past", "-10% overall voter support.");
            Tag(5, "Scientist", "+10% support from highly educated residents.");
            Tag(6, "Frugal", "Campaign donations cost half as much for the same effect.");
            Tag(7, "Lavish", "Campaign donations cost twice as much for the same effect.");
            Tag(8, "Grassroots", "Campaign donations count 25% more.");
            Tag(9, "Fundraiser", "Campaign donations count 15% more, but low-income residents are less supportive.");
            Tag(10, "Poor speaker", "-5% overall voter support.");
            Tag(11, "Charismatic", "+5% overall voter support.");
            Tag(12, "Union organizer", "+10% support from workers.");
            Tag(13, "Student favorite", "+10% support from students and teen voters.");
            Tag(14, "Elder statesperson", "+10% support from elderly voters.");
            Tag(15, "Young reformer", "+8% support from teens and adults, -4% from elderly voters.");
            Tag(16, "Technocrat", "+8% support from well educated residents, -5% from uneducated residents.");
            Tag(17, "Populist", "+8% support from low-income residents, -5% from wealthy residents.");
            Tag(18, "Elite connections", "+8% support from wealthy residents, -5% from low-income residents.");
            Tag(19, "Transit advocate", "+10% support from residents without cars.");
            Tag(20, "Motorist advocate", "+10% support from residents with cars.");
            Tag(21, "Law and order", "+8% support from elderly and unhappy residents.");
            Tag(22, "Environmentalist", "+8% support from students and well educated residents.");
            Tag(23, "Business friendly", "+8% support from workers and wealthy residents.");
            Tag(24, "Neighborhood champion", "+6% support from low- and middle-income residents.");
            Tag(25, "Polarizing", "Election turnout increases by 15%.");
            Tag(26, "Revolutionary", "Doubles this candidate's positive and negative platform effects.");
            Tag(27, "Cautious", "Halves this candidate's positive and negative platform effects, but reduces overall voter support by 5%.");

            add("Model.CandidateTag.Description.HumbleBeginnings", "+{0}% support from low-income residents.");
            add("Model.CandidateTag.Description.ControversialPast", "-{0}% overall voter support.");
            add("Model.CandidateTag.Description.Scientist", "+{0}% support from highly educated residents.");
            add("Model.CandidateTag.Description.Fundraiser", "Campaign donations count 15% more, but low-income support drops by {0}%.");
            add("Model.CandidateTag.Description.PoorSpeaker", "-{0}% overall voter support.");
            add("Model.CandidateTag.Description.Charismatic", "+{0}% overall voter support.");
            add("Model.CandidateTag.Description.UnionOrganizer", "+{0}% support from workers.");
            add("Model.CandidateTag.Description.StudentFavorite", "+{0}% support from students and teen voters.");
            add("Model.CandidateTag.Description.ElderStatesperson", "+{0}% support from elderly voters.");
            add("Model.CandidateTag.Description.YoungReformer", "+{0}% support from teens and adults, -{1}% from elderly voters.");
            add("Model.CandidateTag.Description.Technocrat", "+{0}% support from well educated residents, -{1}% from uneducated residents.");
            add("Model.CandidateTag.Description.Populist", "+{0}% support from low-income residents, -{1}% from wealthy residents.");
            add("Model.CandidateTag.Description.EliteConnections", "+{0}% support from wealthy residents, -{1}% from low-income residents.");
            add("Model.CandidateTag.Description.TransitAdvocate", "+{0}% support from residents without cars.");
            add("Model.CandidateTag.Description.MotoristAdvocate", "+{0}% support from residents with cars.");
            add("Model.CandidateTag.Description.LawAndOrder", "+{0}% support from elderly and unhappy residents.");
            add("Model.CandidateTag.Description.Environmentalist", "+{0}% support from students and well educated residents.");
            add("Model.CandidateTag.Description.BusinessFriendly", "+{0}% support from workers and wealthy residents.");
            add("Model.CandidateTag.Description.NeighborhoodChampion", "+{0}% support from low- and middle-income residents.");
            add("Model.CandidateTag.Description.Polarizing", "Election turnout increases by {0}%.");
            add("Model.CandidateTag.Description.Cautious", "Halves this candidate's positive and negative platform effects, but reduces overall voter support by {0}%.");
        }

        private static void AddPartyTagEntries(System.Action<string, string> add)
        {
            void Tag(int id, string name, string description)
            {
                add($"Model.PartyTag.{id}.Name", name);
                add($"Model.PartyTag.{id}.Description", description);
            }

            Tag(1, "Civic Trust", "Corruption scandal chance -10 points and party scandal reputation loss is 5 points lighter.");
            Tag(2, "Reform Slate", "+8% support from unhappy voters when the party is not the incumbent.");
            Tag(3, "Organized Machine", "Campaign donations count 20% more.");
            Tag(4, "Transit Coalition", "+8% support from voters without cars, plus another +2% while Transit Vouchers are active.");
            Tag(5, "Civil Liberties", "+6% support from students and educated voters, and +8% from uneducated workers after strict voting ID passes.");
            Tag(6, "Local Roots", "+5% support from low- and middle-income residents.");
            Tag(7, "Pragmatic", "If elected, negative platform impacts are 15% softer.");
            Tag(8, "Student Outreach", "+5% support from teen and student voters.");
            Tag(9, "Jobs Focused", "+5% support from workers.");
            Tag(10, "Business Friendly", "+5% support from wealthy residents and workers.");
            Tag(11, "Unproven", "-4% general support until the party wins its first election.");
            Tag(12, "Ideological", "If elected, negative platform impacts are 10% stronger.");
            Tag(13, "Divided", "Campaign donations count 10% less and donation platform-softening chance is 10 points lower.");
            Tag(14, "Old Guard", "-5% support from teen and student voters.");
            Tag(15, "Overconfident", "If leading a released poll outside the margin of error, loses 4% election-day support.");
            Tag(16, "Complacent", "If incumbent, loses 5% general support.");
            Tag(17, "Elitist", "-8% support from low-income residents.");
            Tag(18, "Scandal Prone", "Suspicious campaign actions add one extra corruption risk step and scandal reputation loss is 5 points harsher.");
            Tag(19, "Disorganized", "Campaign donations count 20% less and mayor platform-meeting success chance is 15 points lower.");
            Tag(20, "Out of Touch", "-8% support from unhappy voters.");
        }

        private static void AddSupportProgramEntries(System.Action<string, string> add)
        {
            void Program(int id, string title, string description, string tooltip)
            {
                add($"Model.SupportProgram.{id}.Title", title);
                add($"Model.SupportProgram.{id}.Description", description);
                add($"Model.SupportProgram.{id}.Tooltip", tooltip);
            }

            Program(0, "Make election day a holiday", "Treats election day as a Sunday for resident schedules.", "Making election day a holiday gives residents more time to vote and can increase turnout.");
            Program(1, "Teen voter education", "Adds +{0}% teen election turnout.", "Fund a civic education campaign for teen voters. Each campaign adds +{0}% to teen election turnout.");
            Program(2, "Adult voter education", "Adds +{0}% adult election turnout.", "Fund a civic education campaign for adult voters. Each campaign adds +{0}% to adult election turnout.");
            Program(3, "Elderly voter education", "Adds +{0}% elderly election turnout.", "Fund a civic education campaign for elderly voters. Each campaign adds +{0}% to elderly election turnout.");
            Program(4, "Voter education program", "Adds +{0}% uneducated and poorly educated election turnout.", "Fund a voter education program for uneducated and poorly educated residents. Each program adds +{0}% to election turnout for those education groups.");
            Program(5, "Low-income voter outreach", "Adds +{0}% election turnout for struggling and modest-income residents.", "Fund direct voter outreach for struggling and modest-income residents. Each program adds +{0}% to election turnout for those income groups.");
            Program(6, "Transit vouchers", "Adds +{0}% election turnout for teens, elderly, and low-income residents without cars.", "Fund transit vouchers for teens, elderly residents, and struggling or modest-income residents who do not have a car. Each program adds +{0}% to election turnout for eligible residents.");
            Program(7, "Civic forums", "Adds +{0}% election turnout for educated, well educated, and highly educated residents.", "Fund public candidate forums, policy debates, and civic talks for educated residents. Each forum adds +{0}% to election turnout for educated, well educated, and highly educated residents.");
        }

        private static void AddLegislationEntries(System.Action<string, string> add)
        {
            void Law(int id, string title, string description, string tooltip)
            {
                add($"Model.Legislation.{id}.Title", title);
                add($"Model.Legislation.{id}.Description", description);
                add($"Model.Legislation.{id}.Tooltip", tooltip);
            }

            Law(0, "Voter Identification Ordinance", "{0}% turnout for uneducated workers.", "Requires additional voter identification checks. This reduces turnout among uneducated workers.");
            Law(1, "Property Owner Ballot Notification Act", "+{0}% wealthy turnout, +{1}% car-owner turnout, {2}% low-income turnout.", "Prioritizes mailed ballot notices through property and vehicle records. This raises turnout for wealthy residents and car owners, but can depress low-income turnout.");
            Law(2, "Youth Civic Registration Act", "+{0}% teen turnout, +{1}% student turnout, {2}% elderly turnout.", "Creates automatic civic registration for schools and youth services. This raises teen and student turnout, but slightly lowers elderly turnout.");
            Law(3, "Neighborhood Access Voting Act", "+{0}% low-income turnout, +{1}% no-car turnout, {2}% wealthy turnout.", "Prioritizes neighborhood access rules and local voting assistance. This raises low-income and no-car turnout, but can lower wealthy turnout.");
            Law(4, "Continuity of Governance Act", "+{0}% happy-resident turnout, +{1}% elderly turnout, {2}% unhappy-resident turnout while the incumbent party is running.", "Promotes stable government transition procedures. While the incumbent party is running, happy and elderly residents turn out more, while unhappy residents turn out less.");
        }

        public void Unload()
        {
        }
    }

    public class LocalePTBR : IDictionarySource
    {
        private readonly Setting m_Setting;

        public LocalePTBR(Setting setting)
        {
            m_Setting = setting;
        }

        public IEnumerable<KeyValuePair<string, string>> ReadEntries(IList<IDictionaryEntryError> errors, Dictionary<string, int> indexCounts)
        {
            var entries = new Dictionary<string, string>
            {
                { m_Setting.GetSettingsLocaleID(), "Eleições" },
                { m_Setting.GetOptionTabLocaleID(Setting.kGeneralSection), "Geral" },
                { m_Setting.GetOptionTabLocaleID(Setting.kCampaignSection), "Campanha" },
                { m_Setting.GetOptionTabLocaleID(Setting.kVotingSection), "Votacao" },
                { m_Setting.GetOptionTabLocaleID(Setting.kDebugSection), "Depuracao" },

                { m_Setting.GetOptionGroupLocaleID(Setting.kGeneralGroup), "Geral" },
                { m_Setting.GetOptionGroupLocaleID(Setting.kOverlayGroup), "Sobreposicao" },
                { m_Setting.GetOptionGroupLocaleID(Setting.kCandidateGroup), "Candidatos" },
                { m_Setting.GetOptionGroupLocaleID(Setting.kPartyGroup), "Partidos Politicos" },
                { m_Setting.GetOptionGroupLocaleID(Setting.kTurnoutPresetGroup), "Predefinicoes de Comparecimento" },
                { m_Setting.GetOptionGroupLocaleID(Setting.kTurnoutGroup), "Comparecimento Diario" },
                { m_Setting.GetOptionGroupLocaleID(Setting.kVotingHoursGroup), "Horario de Votacao" },
                { m_Setting.GetOptionGroupLocaleID(Setting.kDebugGroup), "Depuracao" },

                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.EnableElections)), "Ativar Eleições" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.EnableElections)), "Executa o ciclo de eleições para prefeito." },

                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.UseUniversalModMenu)), "Usar Universal Mod Menu" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.UseUniversalModMenu)), "Mostra o botão de Eleições no Universal Mod Menu em vez da sobreposição superior esquerda do jogo." },

                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.VotingSiteOverlayScalePercent)), "Tamanho da Sobreposicao dos Locais de Votacao" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.VotingSiteOverlayScalePercent)), "Tamanho do marcador do local de votacao, icone de urna e contagem de votos. 100% = escala 1.0, 120% e o padrao, e as alteracoes sao aplicadas imediatamente." },

                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.CandidateCount)), "Candidatos por Disputa" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.CandidateCount)), "Numero de candidatos selecionados para novas disputas de prefeito. Disputas ativas existentes mantem a quantidade de candidatos com que comecaram. O padrao e 2." },

                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.EnableRunoffVoting)), "Ativar Segundo Turno" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.EnableRunoffVoting)), "Exige um segundo turno quando nenhum candidato alcanca 50% dos votos do primeiro turno. Desativado por padrao. Campanhas regulares comecam um mes antes quando ativado." },

                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.EnableParties)), "Ativar Partidos Politicos" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.EnableParties)), "Ativa partidos politicos ficticios, reputacao persistente dos partidos politicos, etiquetas de partido politico e gerenciamento de partidos politicos. Desativado por padrao para compatibilidade com jogos existentes." },

                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.AgeTurnoutCountryPreset)), "Pais para Comparecimento por Idade" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.AgeTurnoutCountryPreset)), "Escolha uma predefinicao real de comparecimento por idade a partir de paises com dados publicados por idade, alem da Uniao Europeia." },

                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.ApplyAgeTurnoutCountryPreset)), "Aplicar Predefinicao por Idade" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.ApplyAgeTurnoutCountryPreset)), "Copia os valores arredondados do pais selecionado para os controles diarios de adolescentes, adultos e idosos." },

                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.TeenDailyVotingTurnoutPercent)), "Comparecimento Diário dos Adolescentes" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.TeenDailyVotingTurnoutPercent)), "Probabilidade diária de um adolescente elegível votar. O padrão é 36%; a simulação divide isso pelas horas de votação configuradas." },

                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.AdultDailyVotingTurnoutPercent)), "Comparecimento Diário dos Adultos" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.AdultDailyVotingTurnoutPercent)), "Probabilidade diária de um adulto elegível votar. O padrão é 49%; a simulação divide isso pelas horas de votação configuradas." },

                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.ElderlyDailyVotingTurnoutPercent)), "Comparecimento Diário dos Idosos" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.ElderlyDailyVotingTurnoutPercent)), "Probabilidade diária de um idoso elegível votar. O padrão é 58%; a simulação divide isso pelas horas de votação configuradas." },

                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.ElectionVotingStartHour)), "Hora de Início da Votação" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.ElectionVotingStartHour)), "Horário de início da votação no dia da eleição. O padrão é 8:00." },

                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.ElectionVotingEndHour)), "Hora de Encerramento da Votação" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.ElectionVotingEndHour)), "Horário de encerramento da votação no dia da eleição. O padrão é 17:00. Os resultados são anunciados às 20:00." },

                { m_Setting.GetEnumValueLocaleID(Setting.VotingStartHourOption.Hour6), "6:00" },
                { m_Setting.GetEnumValueLocaleID(Setting.VotingStartHourOption.Hour7), "7:00" },
                { m_Setting.GetEnumValueLocaleID(Setting.VotingStartHourOption.Hour8), "8:00" },
                { m_Setting.GetEnumValueLocaleID(Setting.VotingStartHourOption.Hour9), "9:00" },
                { m_Setting.GetEnumValueLocaleID(Setting.VotingStartHourOption.Hour10), "10:00" },

                { m_Setting.GetEnumValueLocaleID(Setting.VotingEndHourOption.Hour16), "16:00" },
                { m_Setting.GetEnumValueLocaleID(Setting.VotingEndHourOption.Hour17), "17:00" },
                { m_Setting.GetEnumValueLocaleID(Setting.VotingEndHourOption.Hour18), "18:00" },

                { m_Setting.GetEnumValueLocaleID(Setting.TurnoutCountryPreset.Baseline), "Padrao" },
                { m_Setting.GetEnumValueLocaleID(Setting.TurnoutCountryPreset.EuropeanUnion), "Uniao Europeia" },
                { m_Setting.GetEnumValueLocaleID(Setting.TurnoutCountryPreset.Brazil), "Brasil" },
                { m_Setting.GetEnumValueLocaleID(Setting.TurnoutCountryPreset.Canada), "Canada" },
                { m_Setting.GetEnumValueLocaleID(Setting.TurnoutCountryPreset.France), "Franca" },
                { m_Setting.GetEnumValueLocaleID(Setting.TurnoutCountryPreset.Germany), "Alemanha" },
                { m_Setting.GetEnumValueLocaleID(Setting.TurnoutCountryPreset.UK), "Reino Unido" },
                { m_Setting.GetEnumValueLocaleID(Setting.TurnoutCountryPreset.USA), "Estados Unidos da America" },

                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.EnableDebugLogging)), "Ativar Registro de Depuração" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.EnableDebugLogging)), "Grava detalhes do ciclo eleitoral, campanha, pesquisa, votação, doações, prefeito e reparos no log do jogo." },

                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.ForceStartCampaign)), "Forçar Início da Campanha" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.ForceStartCampaign)), "Ação de depuração: seleciona candidatos imediatamente e anuncia a campanha." },
                { m_Setting.GetOptionWarningLocaleID(nameof(Setting.ForceStartCampaign)), "Iniciar uma nova campanha agora?" },

                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.ForceElectionToday)), "Forçar Eleição Hoje" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.ForceElectionToday)), "Ação de depuração: inicia a votação hoje se houver candidatos." },
                { m_Setting.GetOptionWarningLocaleID(nameof(Setting.ForceElectionToday)), "Iniciar a votação da eleição hoje?" },

                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.ForceGeneratePoll)), "Gerar Pesquisa" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.ForceGeneratePoll)), "Ação de depuração: gera e divulga imediatamente os resultados da pesquisa para a disputa ativa." },
                { m_Setting.GetOptionWarningLocaleID(nameof(Setting.ForceGeneratePoll)), "Gerar uma pesquisa agora?" }
            };
            LocaleEN.AddElectionEntries(entries);
            AddElectionEntries(entries);
            return entries;
        }

        private static void AddElectionEntries(Dictionary<string, string> entries)
        {
            AddElectionTable(entries, @"
UI.OpenPanel.Tooltip	Abrir o painel de Eleições. Mostra o prefeito atual, a disputa ativa, o status da pesquisa e os controles de doação de campanha antes do dia da eleição.
UI.OpenPanel.Aria	Abrir Eleições
UI.Panel.Aria	Painéis de eleição
UI.Pending	Pendente
UI.Unknown	Desconhecido
UI.Unavailable	Indisponível
UI.Selected	Selecionado
UI.Save	Salvar
UI.Cancel	Cancelar
UI.Close	X
UI.Empty	Vazio
UI.Total	Total
UI.Each	{amount} cada
UI.Date.January1	1º de janeiro
UI.Menu.VotingSites.Label	Ver locais de votação
UI.Menu.VotingSites.Title	Locais de votação
UI.Menu.VotingSites.Tooltip.Enabled	Mostrar locais de votação no mapa.
UI.Menu.VotingSites.Tooltip.Disabled	Os locais de votação são liberados quando Eleições está ativado e a cidade alcança a população mínima.
UI.Menu.Mayor.Label	Painel do prefeito atual
UI.Menu.Mayor.Title	Prefeito atual
UI.Menu.Mayor.Tooltip	Mostrar o prefeito atual e as ações do prefeito.
UI.Menu.Schedule.Label	Cronograma eleitoral
UI.Menu.Schedule.Title	Cronograma eleitoral
UI.Menu.Schedule.Tooltip	Mostrar datas da eleição, horário de votação, horário dos resultados e status da pesquisa.
UI.Menu.Programs.Label	Programas cívicos
UI.Menu.Programs.Title	Programas cívicos
UI.Menu.Programs.Tooltip	Financiar programas de apoio ao comparecimento antes do dia da eleição.
UI.Menu.Legislation.Label	Legislação
UI.Menu.Legislation.Title	Legislação
UI.Menu.Legislation.Tooltip	Aprovar ou revogar legislação eleitoral persistente.
UI.Menu.Candidates.Label	Candidatos
UI.Menu.Candidates.Title	Candidatos
UI.Menu.Candidates.Tooltip	Mostrar os candidatos a prefeito e os controles de doação de campanha.
UI.Menu.Parties.Label	Partidos
UI.Menu.Parties.Title	Partidos
UI.Menu.Parties.Tooltip.Enabled	Gerenciar partidos políticos fictícios, reputação, cores e etiquetas de partido.
UI.Menu.Parties.Tooltip.Disabled	Partidos políticos estão desativados nas configurações do mod.
UI.Menu.Residence.Label	Residência do prefeito
UI.Menu.Residence.Title	Residência do prefeito
UI.Menu.Residence.Tooltip	Definir a casa e o local de trabalho na Prefeitura a partir do prédio selecionado.
UI.Notice.Disabled	Eleições estão desativadas nas configurações do mod.
UI.Notice.WaitingPopulation	Eleições começarão quando a cidade alcançar {minimum} habitantes. População atual: {current}.
UI.Notice.NoMayor	Nenhum prefeito foi selecionado ainda.
UI.Notice.NoCandidates	Não há candidatos no momento.
UI.Notice.PartiesDisabled	Partidos estão desativados nas configurações do mod.
UI.Notice.NoParties	Nenhum partido está ativo no momento.
UI.VotingSites.Header.Tooltip	Status da sobreposição dos locais de votação. No dia da eleição, os marcadores do mapa incluem contagens de votos ao vivo para cada local.
UI.VotingSites.Header	Locais de votação
UI.VotingSites.Status.Visible.Tooltip	A sobreposição dos locais de votação está visível.
UI.VotingSites.Status.Hidden.Tooltip	A sobreposição dos locais de votação está oculta.
UI.VotingSites.Status.Visible	Sobreposição visível
UI.VotingSites.Status.Hidden	Sobreposição oculta
UI.VotingSites.Election.Tooltip	Data da eleição e janela de votação da disputa ativa para prefeito.
UI.VotingSites.Election.Label	Eleição
UI.VotingSites.Markers.Tooltip	Contagens de votos ao vivo aparecem apenas durante a fase de votação.
UI.VotingSites.Markers.Label	Marcadores do mapa
UI.VotingSites.Markers.Live	Resultados ao vivo
UI.VotingSites.Markers.Locations	Locais de votação
UI.VotingSites.Button.Tooltip.Hide	Ocultar locais de votação no mapa.
UI.VotingSites.Button.Tooltip.Show	Mostrar locais de votação no mapa.
UI.VotingSites.Button.Hide	Ocultar locais de votação
UI.VotingSites.Button.Show	Ver locais de votação
UI.Info.Phase	Fase
UI.Info.Poll	Pesquisa
UI.Info.Election	Eleição
UI.Info.Results	Resultados
UI.Info.Inauguration	Posse
UI.Info.Current	Atual
UI.Schedule.Header.Tooltip	Cronograma eleitoral e fase atual do ciclo.
UI.Schedule.Header	Cronograma eleitoral
UI.Schedule.Summary.Tooltip	Resumo do ciclo eleitoral atual e se esta é uma disputa regular ou acelerada.
UI.Schedule.Phase.Tooltip	A fase atual do ciclo eleitoral.
UI.Schedule.Poll.Tooltip	A pesquisa de campanha é divulgada às 08:00 nesta data.
UI.Schedule.Election.Tooltip	Dia da eleição e janela de votação da disputa ativa para prefeito.
UI.Schedule.Results.Tooltip	Data e horário do anúncio dos resultados da eleição.
UI.Schedule.Inauguration.Tooltip	{name} é prefeito eleito. O prefeito em exercício permanece no cargo até esta data.
UI.Poll.Header.Tooltip	Status da pesquisa. Antes da divulgação, mostra a data prevista; depois, mostra as preferências dos eleitores amostrados.
UI.Poll.Header.Results	Resultados atuais da pesquisa
UI.Poll.Header.Scheduled	Pesquisa agendada
UI.Poll.Sample.Tooltip	Número de moradores elegíveis amostrados pela pesquisa de campanha.
UI.Poll.Sample.Word	amostrados
UI.Poll.Tabs.Aria	Detalhamento da pesquisa
UI.Poll.Tab.Overall	Geral
UI.Poll.Tab.Age	Idade
UI.Poll.Tab.Education	Educação
UI.Poll.Tab.Income	Renda
UI.Poll.Tab.Tooltip	Mostrar resultados da pesquisa por {label}.
UI.Poll.ReleaseNotice	A pesquisa será divulgada em {date} às 08:00.
UI.Poll.PendingNotice	A data da pesquisa está pendente.
UI.Poll.Undecided	Indecisos
UI.Poll.Readout.Tooltip	Leitura da pesquisa com base na amostra e na margem de erro.
UI.Poll.Released	Pesquisa divulgada
UI.Poll.NoBreakdown	Nenhum detalhamento da pesquisa está disponível.
UI.Poll.Breakdown.Tooltip	Leitura da pesquisa para este grupo amostrado.
UI.Poll.Row.Tooltip	Apoio na pesquisa para {label}. Isto se baseia nos moradores amostrados, não no comparecimento final da eleição.
UI.Programs.Fallback.ElectionDay	Programas cívicos ficam indisponíveis no dia da eleição.
UI.Programs.Fallback.UsedToday	Apenas um programa cívico pode ser financiado por dia. O programa de hoje é {program}.
UI.Programs.Fallback.AlreadySelected	já selecionado
UI.Programs.Fallback.Closed	Programas cívicos ficam disponíveis antes do dia da eleição assim que os candidatos forem selecionados.
UI.Programs.Header.Tooltip	Programas cívicos podem ser financiados uma vez por dia antes do dia da eleição.
UI.Programs.Header	Programas cívicos
UI.Programs.Cost.Tooltip	Cada programa cívico custa metade do valor atual de doação de campanha.
UI.Programs.Today.Tooltip	A vaga diária de programa cívico é renovada no próximo dia do calendário.
UI.Programs.Today	Programa de hoje: {program}
UI.Programs.Today.Selected	selecionado
UI.Programs.Status.HolidayScheduled	Feriado agendado
UI.Programs.Status.NotScheduled	Não agendado
UI.Programs.Status.CurrentBonus	Bônus atual +{percent}%
UI.Programs.Status.Tooltip.Holiday	Status do agendamento de feriado.
UI.Programs.Status.Tooltip.Bonus	Bônus acumulado de comparecimento eleitoral para este grupo de eleitores.
UI.Programs.Button.Fund	Financiar
UI.Programs.Button.Scheduled	Agendado
UI.Programs.NoPrograms	Programas cívicos estão indisponíveis no momento.
UI.Legislation.Fallback.ElectionDay	Legislação eleitoral fica indisponível no dia da eleição.
UI.Legislation.Fallback.UsedToday	Apenas uma ação de legislação eleitoral pode ser tentada por dia.
UI.Legislation.Fallback.Closed	A legislação eleitoral pode ser alterada antes do dia da eleição assim que os candidatos forem selecionados.
UI.Legislation.Header.Tooltip	A legislação persiste em eleições futuras até ser revogada. Aprovar e revogar usam fundos da cidade, podem falhar e não geram risco de corrupção.
UI.Legislation.Header	Legislação eleitoral
UI.Legislation.Button.Pass	Aprovar
UI.Legislation.Button.Repeal	Revogar
UI.Legislation.Active.Tooltip	Legislação ativa continua em vigor entre eleições.
UI.Legislation.Active	Ativa
UI.Legislation.NoLegislation	Legislação eleitoral está indisponível no momento.
UI.Residence.Header.Tooltip	A residência e o escritório atribuídos são aplicados ao prefeito atual. Se o destino estiver cheio, outro morador ou trabalhador é removido antes.
UI.Residence.Header	Atribuições do prefeito
UI.Residence.Status.Assigned.Tooltip	O prefeito está atribuído aos dois destinos selecionados.
UI.Residence.Status.Relocating.Tooltip	O prefeito será movido para os destinos selecionados na próxima atualização de atribuição.
UI.Residence.Status.Assigned	Atribuído
UI.Residence.Status.Relocating	Realocando
UI.Residence.Selected.Tooltip.Exists	Prédio do jogo selecionado atualmente.
UI.Residence.Selected.Tooltip.Empty	Nenhum prédio está selecionado na interface do jogo.
UI.Residence.Selected.Label	Prédio selecionado
UI.Residence.Tag.Home	Casa
UI.Residence.Tag.CityHall	Prefeitura
UI.Residence.Home.Title	Casa do prefeito
UI.Residence.Workplace.Title	Local de trabalho do prefeito
UI.Residence.Target.Status.Assigned	Prefeito atribuído
UI.Residence.Target.Status.Pending	Mudança pendente
UI.Residence.Target.Status.None	Sem destino
UI.Residence.Target.Home.Tooltip	Destino salvo da residência do prefeito.
UI.Residence.Target.Workplace.Tooltip	Destino salvo do local de trabalho do prefeito.
UI.Residence.Target.Assigned.Tooltip	O prefeito já está atribuído aqui.
UI.Residence.Target.Move.Tooltip	O sistema de atribuição moverá o prefeito para cá.
UI.Residence.Focus.Tooltip.Enabled	Mover a câmera para o destino selecionado do prefeito.
UI.Residence.Focus.Tooltip.Disabled	Nenhum destino está disponível para focar.
UI.Residence.Focus	Focar
UI.Residence.Use.Tooltip.Selected	Este prédio já está selecionado.
UI.Residence.Use.Tooltip.Home	Atribuir a família do prefeito a esta residência de baixa densidade.
UI.Residence.Use.Tooltip.Workplace	Atribuir o prefeito para trabalhar nesta Prefeitura.
UI.Residence.Use.Tooltip.HomeInvalid	O prédio selecionado deve ser residencial de baixa densidade.
UI.Residence.Use.Tooltip.WorkplaceInvalid	O prédio selecionado deve ser um ativo de Prefeitura.
UI.Residence.Use	Usar selecionado
UI.Residence.Choose.Home	Escolha uma residência
UI.Residence.Choose.Workplace	Escolha uma Prefeitura
UI.Residence.Empty.Home	Nenhuma residência de baixa densidade encontrada
UI.Residence.Empty.Workplace	Nenhum ativo de Prefeitura encontrado
UI.Residence.Dropdown.Home.Tooltip	Escolha o prédio residencial de baixa densidade onde o prefeito deve morar.
UI.Residence.Dropdown.Workplace.Tooltip	Escolha o ativo de Prefeitura onde o prefeito deve trabalhar.
UI.Residence.Dropdown.Limited	Há mais prédios elegíveis. Selecione um na cidade para adicioná-lo aqui.
UI.Mayor.Pending	Pendente
UI.Mayor.Action.PlatformMeeting.Title	Reunião de plataforma
UI.Mayor.Action.PlatformMeeting.Description	O prefeito tentará convencer um candidato selecionado a suavizar sua plataforma.
UI.Mayor.Action.Choose	Escolher
UI.Mayor.Action.Bribe	Subornar
UI.Mayor.Action.Endorsement.Title	Endosso do prefeito
UI.Mayor.Action.Endorsement.Description	Gaste fundos da cidade para que o prefeito endosse um candidato selecionado.
UI.Mayor.Action.Endorse	Endossar
UI.Mayor.Action.EndorsedStatus	Endossou {name}
UI.Mayor.Action.CashAssistance.Title	Auxílio em dinheiro
UI.Mayor.Action.CashAssistance.FundedTitle	Auxílio em dinheiro financiado
UI.Mayor.Action.CashAssistance.Description	Gaste fundos da cidade para aumentar o comparecimento de moradores em dificuldade e de renda modesta.
UI.Mayor.Action.Fund	Financiar
UI.Mayor.Action.Tampering.Title	Manipulação da contagem de votos
UI.Mayor.Action.Tampering.Description	Gaste fundos da cidade para organizar uma interrupção tardia no dia da eleição para um candidato selecionado.
UI.Mayor.Action.Tamper	Manipular
UI.Mayor.Action.PlannedFor	Planejado para {name}
UI.Mayor.Unavailable.Endorsed	O prefeito já endossou {name} neste ciclo eleitoral.
UI.Mayor.Unavailable.CashAssistance	O Auxílio em dinheiro já foi financiado neste ciclo eleitoral.
UI.Mayor.Unavailable.Tampering	Uma operação de contagem de votos já está planejada para {name} neste ciclo eleitoral.
UI.Mayor.Unavailable.ElectionDay	Ações de campanha do prefeito ficam indisponíveis no dia da eleição.
UI.Mayor.Unavailable.MeetingPending	O prefeito está tentando agendar esta reunião com o candidato.
UI.Mayor.Unavailable.ScheduleBlocked	A agenda do prefeito está bloqueada após a ação de campanha de hoje.
UI.Mayor.Unavailable.WaitingPopulation	Ações de campanha do prefeito são liberadas quando as Eleições começam.
UI.Mayor.Unavailable.NoCandidates	Não há candidatos no momento.
UI.Mayor.Unavailable.Closed	Ações de campanha do prefeito ficam disponíveis durante a campanha antes do dia da eleição.
UI.Mayor.Unavailable.Generic	Ação de campanha do prefeito indisponível no momento.
UI.Mayor.Portrait.Tooltip.Temporary	Prefeito temporário selecionado entre cidadãos reais. Este prefeito não aplica efeitos à cidade e supervisiona a transição até a conclusão de uma eleição.
UI.Mayor.Portrait.Tooltip.Current	Prefeito eleito atual e plataforma ativa do prefeito.
UI.Mayor.Name.Label.Tooltip	O cidadão que está servindo como prefeito. Clique no nome para mover a câmera até este cidadão.
UI.Mayor.Name.Label	Prefeito atual
UI.Mayor.Name.Tooltip	Clique no nome do prefeito para mover a câmera até este cidadão.
UI.Mayor.PartyReputation.Tooltip	Reputação do partido: {reputation}/100.
UI.Mayor.Platform.Tooltip	Plataforma atual do prefeito e efeito na cidade. Prefeitos temporários de transição não aplicam modificadores à cidade.
UI.Mayor.Actions.Header.Tooltip	Ações de campanha do prefeito ficam disponíveis antes do dia da eleição.
UI.Mayor.Actions.Header	Ações de campanha do prefeito
UI.Mayor.Actions.Cost.Tooltip	Cada ação de campanha do prefeito usa o custo atual de ação de campanha do prefeito.
UI.Mayor.Actions.Status.Tooltip	Estado atual desta ação de campanha do prefeito.
UI.Mayor.Picker.Close.Tooltip	Fechar seletor de candidato.
UI.Mayor.Picker.CandidateFallback	Candidato
UI.Mayor.Picker.NoPlatform	Sem plataforma
UI.Mayor.Picker.Empty	Nenhum candidato ativo no momento.
UI.Mayor.Picker.Title.Bribe	Escolha o alvo da reunião de plataforma
UI.Mayor.Picker.Title.Endorse	Escolha o alvo do endosso
UI.Mayor.Picker.Title.Tamper	Escolha o alvo da contagem de votos
UI.Mayor.Picker.Title.Candidate	Escolha o candidato
");
            AddElectionTable(entries, @"
UI.Party.Save.Tooltip.Disabled	Nenhuma alteração de partido para salvar.
UI.Party.Save.Tooltip.Enabled	Salvar este nome e cor do partido.
UI.Party.Reputation.Tooltip	Reputação persistente do partido de 0 a 100.
UI.Party.Reputation.Label	Reputação:
UI.Party.Wins.Tooltip	Total de eleições vencidas por este partido.
UI.Party.Wins.Label	Vitórias:
UI.Party.Terms.Tooltip	Mandatos consecutivos atuais no poder.
UI.Party.Terms.Label	Mandatos:
UI.Party.Replace.Tooltip.Enabled	Abrir o editor de etiquetas do partido.
UI.Party.Replace.Tooltip.Disabled	A substituição de etiquetas do partido está indisponível no momento.
UI.Party.Replace	Substituir etiquetas
UI.Party.TagEditor.Save.Tooltip.Disabled	A substituição de etiquetas do partido está indisponível no momento.
UI.Party.TagEditor.Save.Tooltip.Count	Selecione exatamente três etiquetas de partido.
UI.Party.TagEditor.Save.Tooltip.Total	Os valores das etiquetas de partido devem somar zero.
UI.Party.TagEditor.Save.Tooltip.Unchanged	Escolha um conjunto diferente de etiquetas de partido.
UI.Party.TagEditor.Save.Tooltip.Ready	Salvar estas etiquetas de partido.
UI.Party.TagEditor.Instructions	Selecione três etiquetas de partido. O total deve ser 0.
UI.Party.TagEditor.Full.Tooltip	Remova uma etiqueta selecionada antes de escolher outra.
UI.Candidate.Donation.Tooltip.NoCandidate	Nenhum candidato está disponível para doações.
UI.Candidate.Donation.Tooltip.UsedToday	Apenas uma doação de campanha pode ser feita por dia.
UI.Candidate.Donation.Tooltip.Closed	Doações ficam disponíveis quando uma campanha ativa tem candidatos selecionados antes do dia da eleição.
UI.Candidate.Donation.Tooltip.Ready	Doar {amount} dos fundos da cidade para apoiar a campanha deste candidato antes do dia da eleição.
UI.Candidate.Portrait.Tooltip	Retrato do candidato. Candidatos são selecionados entre moradores adultos reais.
UI.Candidate.Name.Tooltip	Clique no nome do candidato para mover a câmera até este cidadão.
UI.Candidate.Bio.Tooltip	Contexto do candidato gerado a partir da vida atual do morador na cidade.
UI.Candidate.DonationTotal.Tooltip	Apoio efetivo de campanha creditado a este candidato durante a campanha atual. Algumas etiquetas podem alterar custo ou efeito de campanha.
UI.Candidate.DonationTotal	Total de doações
UI.Candidate.Platform.Tooltip	Plataforma do candidato. O efeito positivo aparece primeiro, seguido da compensação.
UI.Candidate.Platform	Plataforma do candidato
UI.Candidate.DonationHeader.Tooltip	Apoio de campanha financiado pela cidade. Doações ficam disponíveis enquanto a disputa para prefeito estiver ativa antes do dia da eleição.
UI.Candidate.DonationHeader	Doação de campanha
UI.Candidate.Donate	Doar {amount}
UI.Candidate.DonationsClosed.Help	Doações abrem antes do dia da eleição assim que os candidatos forem selecionados.
UI.Candidate.DonationUsed.Help	Uma doação de campanha já foi feita hoje.
UI.Candidate.GenericArticle	um candidato
UI.Party.FallbackNumber	Partido {number}
Panel.Stage.WaitingForResidents	Aguardando moradores
Panel.Stage.NoElectionData	Sem dados eleitorais
Panel.Stage.NoActiveCampaign	Nenhuma campanha ativa
Panel.Stage.RunoffCandidatesSelected	Candidatos do segundo turno selecionados
Panel.Stage.CandidatesSelected	Candidatos selecionados
Panel.Stage.RunoffPollReleased	Pesquisa do segundo turno divulgada
Panel.Stage.PollReleased	Pesquisa divulgada
Panel.Stage.RunoffElectionDay	Dia da eleição do segundo turno
Panel.Stage.ElectionDay	Dia da eleição
Panel.Stage.TransitionPeriod	Período de transição
Panel.Stage.MayorTermActive	Mandato do prefeito ativo
Panel.Cycle.WaitingPopulation	Eleições começam com {0:n0} habitantes. População atual: {1:n0}.
Panel.Cycle.NotInitialized	O sistema eleitoral ainda não foi inicializado.
Panel.Cycle.RunoffRace	Disputa para prefeito em segundo turno
Panel.Cycle.RegularRunoffRace	Disputa regular para prefeito com segundo turno
Panel.Cycle.AcceleratedRace	Disputa acelerada para prefeito
Panel.Cycle.RegularRace	Disputa regular para prefeito
Panel.Cycle.PendingMayorNoCurrent	{0} toma posse em {1}.
Panel.Cycle.PendingMayorWithCurrent	{0} toma posse em {1}. {2} está servindo como prefeito até lá.
Panel.Cycle.MayorServing	{0} está servindo como prefeito.
Panel.Cycle.NoActiveCampaignSentence	Nenhuma campanha ativa.
Panel.Candidate.EffectDescription	Se eleito, esta plataforma {0}.
Panel.Candidate.NoPlatform	Sem plataforma
Panel.Candidate.EmptyDescription	Nenhum candidato foi selecionado ainda.
Panel.Candidate.Fallback.0	Candidato A
Panel.Candidate.Fallback.1	Candidato B
Panel.Candidate.Fallback.2	Candidato C
Panel.Candidate.Fallback.3	Candidato D
Panel.Candidate.Fallback.Generic	Candidato
Panel.MayorElect	Prefeito eleito
Panel.MayorElect.Article	O prefeito eleito
Panel.Mayor.TemporaryDescription	Plataforma temporária de prefeito. Nenhum modificador da cidade é aplicado; este prefeito supervisiona a transição até um prefeito eleito tomar posse.
Panel.Mayor.CurrentDescription	Plataforma atual do prefeito. Ela {0}.
Panel.Mayor.NoHome	Nenhuma residência de baixa densidade selecionada
Panel.Mayor.NoWorkplace	Nenhuma Prefeitura selecionada
Panel.Mayor.NoBuilding	Nenhum prédio selecionado
Panel.Mayor.Residence	Residência
Panel.Mayor.CityHall	Prefeitura
Panel.Donation.CampaignSupport	Apoio de campanha
Panel.Party.Replace.Disabled.Cycle	Etiquetas de partido só podem ser substituídas fora do ciclo eleitoral.
Panel.Party.Replace.Disabled.NoYear	A substituição de etiquetas de partido precisa do ano atual da cidade.
Panel.Party.Replace.Disabled.UsedYear	Este partido já substituiu suas etiquetas este ano.
Panel.Party.Fallback	Partido
Panel.Party.Default.0	Aliança Cívica Roxa
Panel.Party.Default.1	Coalizão Futuro Verde
Panel.Party.Default.2	Partido Liberdade Rosa
Panel.Party.Default.3	Liga Prosperidade Dourada
Panel.Poll.Age.Teens	Adolescentes
Panel.Poll.Age.Adults	Adultos
Panel.Poll.Age.Elderly	Idosos
Panel.Poll.Education.Uneducated	Sem instrução
Panel.Poll.Education.PoorlyEducated	Pouco instruído
Panel.Poll.Education.Educated	Instruído
Panel.Poll.Education.WellEducated	Bem instruído
Panel.Poll.Education.HighlyEducated	Altamente instruído
Panel.Poll.Income.Struggling	Em dificuldade
Panel.Poll.Income.Modest	Renda modesta
Panel.Poll.Income.Middle	Renda média
Panel.Poll.Income.Comfortable	Confortável
Panel.Poll.Income.Wealthy	Rico
Panel.Support.Disabled.ElectionDay	Programas cívicos ficam indisponíveis no dia da eleição.
Panel.Support.Disabled.UsedToday	Apenas um programa cívico pode ser financiado por dia. O programa de hoje é {0}.
Panel.Support.Disabled.HolidayScheduled	O dia da eleição já está agendado como feriado.
Panel.Support.Disabled.Closed	Programas cívicos ficam disponíveis antes do dia da eleição assim que os candidatos forem selecionados.
Panel.Support.Disabled.Generic	Programa cívico indisponível no momento.
Panel.Support.Fallback	um programa cívico
Panel.Legislation.Disabled.ElectionDay	Legislação eleitoral fica indisponível no dia da eleição.
Panel.Legislation.Disabled.UsedToday	Apenas uma ação de legislação eleitoral pode ser tentada por dia.
Panel.Legislation.Disabled.Closed	A legislação eleitoral pode ser alterada antes do dia da eleição assim que os candidatos forem selecionados.
Lifecycle.Department.ElectionBoard	Junta Eleitoral
Lifecycle.Department.Police	Departamento de Polícia
Lifecycle.Department.FireRescue	Bombeiros e Resgate
Lifecycle.Name.Candidate	o candidato
Lifecycle.Name.Mayor	o prefeito
Lifecycle.Name.FormerMayor	o ex-prefeito
Lifecycle.Name.City	a cidade
Lifecycle.Name.Field	o grupo de candidatos
Lifecycle.Name.OpposingCampaign	a campanha adversária
Lifecycle.Name.MyVotingSite	meu local de votação
Lifecycle.Name.VotingSite	um local de votação
Lifecycle.Name.ThatVotingSite	aquele local de votação
Lifecycle.Name.CelebrationSite	o local da comemoração
Lifecycle.Name.TemporaryMayor	Prefeito temporário
Lifecycle.List.And	 e 
Lifecycle.MinimumPopulation.StartCampaign	A Junta Eleitoral não iniciará uma campanha até a cidade alcançar {0:n0} habitantes.
Lifecycle.MinimumPopulation.StartVoting	A Junta Eleitoral não iniciará a votação até a cidade alcançar {0:n0} habitantes.
Lifecycle.MinimumPopulation.GeneratePoll	A Junta Eleitoral não gerará uma pesquisa até a cidade alcançar {0:n0} habitantes.
Lifecycle.Poll.DebugUnavailableElectionDay	A geração de pesquisa de depuração fica indisponível no dia da eleição.
Lifecycle.PartyTags.ReplaceOutsideCycle	Etiquetas de partido só podem ser substituídas fora do ciclo eleitoral.
Lifecycle.PartyTags.AlreadyReplacedSetThisYear	{0} já substituiu etiquetas de partido este ano.
Lifecycle.PartyTags.ChooseExactlyThree	Escolha exatamente três etiquetas de partido antes de salvar.
Lifecycle.PartyTags.MustBeUnique	As etiquetas de partido devem ser únicas.
Lifecycle.PartyTags.MustBalance	Os valores das etiquetas de partido devem somar zero.
Lifecycle.PartyTags.AlreadyHasSet	{0} já tem esse conjunto de etiquetas de partido.
Lifecycle.PartyTags.ReplacedSet	{0} substituiu suas etiquetas de partido.
Lifecycle.PartyTags.AlreadyReplacedTagThisYear	{0} já substituiu uma etiqueta de partido este ano.
Lifecycle.PartyTags.NoBalancedReplacement	{0} não tem substituição equilibrada disponível para essa etiqueta.
Lifecycle.PartyTags.ReplacedTag	{0} substituiu {1} por {2}.
Lifecycle.Donation.NoDate	Doações de campanha precisam da data atual da cidade e estão indisponíveis agora.
Lifecycle.Donation.NoRace	Doações de campanha ficam disponíveis enquanto uma disputa ativa para prefeito tem candidatos selecionados.
Lifecycle.Donation.ElectionDay	Doações de campanha estão encerradas no dia da eleição.
Lifecycle.Donation.Closed	Doações de campanha ficam disponíveis antes do dia da eleição enquanto uma disputa ativa para prefeito tem candidatos selecionados.
Lifecycle.Donation.UsedToday	Apenas uma doação de campanha pode ser feita por dia.
Lifecycle.Donation.InsufficientFunds	A cidade não tem dinheiro suficiente para doar {0:n0}.
Lifecycle.Donation.ThankYou.Support	seu apoio de campanha totalizando {0:n0}
Lifecycle.Donation.ThankYou.Softened	 A campanha suavizou sua plataforma: {0} mudou de {1} para {2}.
Lifecycle.Donation.ThankYou.Message	Obrigado por {0}. O total doado à minha campanha até agora é {1:n0}.{2}
Lifecycle.Support.Closed	Programas cívicos ficam disponíveis antes do dia da eleição enquanto uma disputa ativa para prefeito tem candidatos selecionados.
Lifecycle.Support.UsedToday	Apenas um programa cívico pode ser financiado por dia. O programa de hoje é {0}.
Lifecycle.Support.HolidayScheduled	O dia da eleição já está agendado como feriado.
Lifecycle.Support.InsufficientFunds	A cidade não tem dinheiro suficiente para financiar {0} por {1:n0}.
Lifecycle.Support.Funded	{0} financiado por {1:n0}. {2}
Lifecycle.Support.Outcome.Holiday	O dia da eleição será tratado como feriado para os horários dos moradores.
Lifecycle.Support.Outcome.Teen	O bônus de comparecimento eleitoral dos adolescentes agora é +{0}%.
Lifecycle.Support.Outcome.Adult	O bônus de comparecimento eleitoral dos adultos agora é +{0}%.
Lifecycle.Support.Outcome.Elderly	O bônus de comparecimento eleitoral dos idosos agora é +{0}%.
Lifecycle.Support.Outcome.Education	Os bônus de comparecimento eleitoral de moradores sem instrução e pouco instruídos agora são +{0}%.
Lifecycle.Support.Outcome.LowIncome	O bônus de comparecimento de moradores em dificuldade e de renda modesta agora é +{0}%.
Lifecycle.Support.Outcome.Transit	O bônus de comparecimento por vale-transporte agora é +{0}% para moradores elegíveis sem carro.
Lifecycle.Support.Outcome.CivicForums	O bônus de comparecimento de moradores instruídos agora é +{0}%.
Lifecycle.Support.Outcome.Generic	O programa cívico está ativo.
Lifecycle.Bribe.Closed	Reuniões de plataforma do prefeito ficam disponíveis apenas antes do dia da eleição enquanto uma disputa ativa tiver candidatos selecionados.
Lifecycle.Bribe.ScheduleBlocked	A agenda do prefeito já está reservada para uma tentativa de reunião de plataforma com candidato.
Lifecycle.Bribe.InsufficientFunds	A cidade não tem dinheiro suficiente para financiar uma ação de aproximação do prefeito de {0:n0}.
Lifecycle.Bribe.Travel.Mayor	A caminho de {{LINK_3}} para encontrar {{LINK_2}} e discutir uma plataforma de campanha.
Lifecycle.Bribe.Travel.Candidate	A caminho de {{LINK_3}} para encontrar {{LINK_2}} sobre a plataforma de campanha.
Lifecycle.Bribe.Unable	A Junta Eleitoral informa que {0} tentou encontrar {1} para uma discussão de plataforma de campanha, mas nenhum horário e local de lazer adequado pôde ser organizado em 24 horas de jogo.
Lifecycle.Endorse.Closed	Endossos do prefeito ficam disponíveis apenas antes do dia da eleição enquanto uma disputa ativa tiver candidatos selecionados.
Lifecycle.Endorse.AlreadyUsed	O prefeito já endossou um candidato neste ciclo eleitoral.
Lifecycle.Endorse.InsufficientFunds	A cidade não tem dinheiro suficiente para financiar um esforço de endosso do prefeito de {0:n0}.
Lifecycle.MayorAction.ScheduleBlocked	A agenda do prefeito já está reservada para uma ação de campanha hoje.
Lifecycle.CashAssistance.Closed	Auxílio em dinheiro só fica disponível antes do dia da eleição enquanto uma disputa ativa tiver candidatos selecionados.
Lifecycle.CashAssistance.AlreadyFunded	O Auxílio em dinheiro já foi financiado neste ciclo eleitoral.
Lifecycle.CashAssistance.InsufficientFunds	A cidade não tem dinheiro suficiente para financiar uma operação de Auxílio em dinheiro de {0:n0}.
Lifecycle.CashAssistance.Funded	Auxílio em dinheiro financiado. Moradores em dificuldade e de renda modesta recebem +{0}% de comparecimento eleitoral.
Lifecycle.Tamper.Closed	A manipulação da contagem de votos só pode ser organizada antes do dia da eleição enquanto uma disputa ativa tiver candidatos selecionados.
Lifecycle.Tamper.AlreadyPlanned	Uma operação de manipulação da contagem de votos já está planejada para este ciclo eleitoral.
Lifecycle.Tamper.InsufficientFunds	A cidade não tem dinheiro suficiente para financiar uma operação de contagem de votos de {0:n0}.
Lifecycle.Legislation.Closed	A legislação eleitoral só pode ser alterada antes do dia da eleição enquanto uma disputa ativa tiver candidatos selecionados.
Lifecycle.Legislation.UsedToday	Apenas uma ação de legislação eleitoral pode ser tentada por dia.
Lifecycle.Legislation.AlreadyActive	{0} já está em vigor.
Lifecycle.Legislation.AlreadyRepealed	{0} já foi revogada.
Lifecycle.Legislation.InsufficientFunds	A cidade não tem dinheiro suficiente para financiar uma {1} de legislação de {0:n0}.
Lifecycle.Legislation.Action.Proposal	proposta
Lifecycle.Legislation.Action.Repeal	revogação
Lifecycle.Legislation.Outcome.Passed	{0} foi aprovada após lobby.
Lifecycle.Legislation.Outcome.Repealed	{0} foi revogada após lobby.
Lifecycle.Legislation.Outcome.FailedPass	{0} não foi aprovada após lobby.
Lifecycle.Legislation.Outcome.FailedRepeal	A revogação de {0} falhou após lobby.
Lifecycle.Legislation.Outcome.WithChance	{0} A chance de sucesso era {1}% com base na reputação do partido incumbente.
Lifecycle.Replacement.InheritedDonation	 As doações de campanha existentes, totalizando {0:n0}, permanecem com esta campanha.
Lifecycle.Replacement.Certified	A Junta Eleitoral informa que {0} não pode mais participar porque seu registro de morador não é mais elegível. {1} agora está certificado para a disputa para prefeito.{2} Pesquisa: {3}. Eleição: {4}.
Lifecycle.Replacement.NoEligible	A Junta Eleitoral constatou que {0} não pode mais participar, mas nenhum candidato substituto elegível está disponível no momento. A junta continuará procurando um morador elegível.
Lifecycle.Replacement.RevisedPoll.TwoLinks	Como a lista de candidatos mudou, a Junta Eleitoral emitiu uma pesquisa de campanha atualizada: {{LINK_1}} {0}%, {{LINK_2}} {1}%, indecisos {2}% com margem de erro de +/-{3}%. {4}.
Lifecycle.Replacement.RevisedPoll.Named	Como a lista de candidatos mudou, a Junta Eleitoral emitiu uma pesquisa de campanha atualizada: {0}, indecisos {1}% com margem de erro de +/-{2}%. {3}.
Lifecycle.TemporaryMayor.Intro	Eu sou {{LINK_1}}. Servirei como prefeito temporário sob a plataforma de Transição Democrática: sem novas mudanças de política municipal, apenas supervisionando o processo eleitoral até que os moradores escolham um prefeito.
Lifecycle.Initialize.Active	Eleições está ativo. A próxima campanha regular para prefeito começa em {0}.
Lifecycle.Party.Milestone	{0} recebeu crédito pelo novo marco da cidade. Reputação do partido +{1}.
Lifecycle.PartyEvent.Negative.Major.0	Um escândalo de corrupção prejudicou {0}. Reputação do partido -{1}.
Lifecycle.PartyEvent.Negative.Major.1	Investigadores levantaram sérias questões éticas envolvendo {0}. Reputação do partido -{1}.
Lifecycle.PartyEvent.Negative.Major.2	Um escândalo de influência de doadores colocou {0} sob pressão. Reputação do partido -{1}.
Lifecycle.PartyEvent.Negative.Minor.0	Alegações de caso envolvendo figuras importantes de {0} abalaram a confiança pública. Reputação do partido -{1}.
Lifecycle.PartyEvent.Negative.Minor.1	{0} enfrentou críticas por um erro de divulgação de doadores. Reputação do partido -{1}.
Lifecycle.PartyEvent.Negative.Minor.2	Uma reclamação ética menor virou manchete para {0}. Reputação do partido -{1}.
Lifecycle.PartyEvent.Negative.Minor.Incumbent	Moradores culparam {0} por uma decisão confusa na Prefeitura. Reputação do partido -{1}.
Lifecycle.PartyEvent.Negative.Minor.Challenger	{0} recebeu críticas por uma declaração de campanha impopular. Reputação do partido -{1}.
Lifecycle.PartyEvent.Positive.0	Uma auditoria limpa aumentou a confiança em {0}. Reputação do partido +{1}.
Lifecycle.PartyEvent.Positive.1	{0} recebeu elogios por trabalho público transparente. Reputação do partido +{1}.
Lifecycle.PartyEvent.Positive.Incumbent	Moradores creditaram {0} por uma resposta estável da cidade. Reputação do partido +{1}.
Lifecycle.PartyEvent.Positive.Challenger	{0} ganhou atenção por uma proposta de política construtiva. Reputação do partido +{1}.
Lifecycle.PartyEvent.Positive.3	{0} se beneficiou de cobertura local positiva. Reputação do partido +{1}.
Lifecycle.Campaign.NoEligibleCandidates	A junta eleitoral não conseguiu encontrar {0} moradores adultos elegíveis para a disputa para prefeito.
Lifecycle.Campaign.Started.TwoLinks	A disputa para prefeito começou. Os candidatos são {{LINK_1}} e {{LINK_2}}. Pesquisa: {0}. Eleição: {1}.
Lifecycle.Campaign.Started.Named	A disputa para prefeito começou com {0} candidatos: {1}. Pesquisa: {2}. Eleição: {3}.
Lifecycle.Platform.CandidateIntro	Eu sou {{LINK_1}}, {0}. Minha plataforma {1}.
Lifecycle.Poll.Release.TwoLinks	Pesquisa de campanha divulgada em {0} com {1:n0} moradores elegíveis amostrados: {{LINK_1}} {2}%, {{LINK_2}} {3}%, indecisos {4}% com margem de erro de +/-{5}%. {6}. Doações continuam disponíveis no painel de opções de Eleições.
Lifecycle.Poll.Release.Named	Pesquisa de campanha divulgada em {0} com {1:n0} moradores elegíveis amostrados: {2}, indecisos {3}% com margem de erro de +/-{4}%. {5}. Doações continuam disponíveis no painel de opções de Eleições.
Lifecycle.PollResponse.WithCta	{0} Doações estão abertas, e cada contribuição ajuda a mover esta disputa.
Lifecycle.PollResponse.DeadHeat	A pesquisa mais recente indica empate técnico contra {0}.
Lifecycle.PollResponse.Ahead	A pesquisa mais recente nos coloca à frente de {0}, fora da margem de erro de +/-{1}%.
Lifecycle.PollResponse.Behind	A pesquisa mais recente nos coloca atrás de {0}, mas eleitores indecisos ainda podem mover esta disputa.
Lifecycle.PollResponse.Tied	A pesquisa mais recente nos coloca empatados com {0}.
Lifecycle.Reminder.0	Amanhã é dia de eleição. As urnas ficam abertas de {0}, e peço seu voto.
Lifecycle.Reminder.1	Amanhã, moradores escolhem o próximo prefeito. Minha plataforma {0}, e cada voto pode moldar a cidade.
Lifecycle.Reminder.2	Planeje votar amanhã entre {0}. Esta disputa é sobre {1}, e sua voz importa.
Lifecycle.Reminder.3	A eleição de amanhã é uma escolha: minha plataforma {0}, enquanto a plataforma de {1} {2}.
Lifecycle.Reminder.4	{0} ainda precisa explicar por que sua plataforma {1}. Amanhã, os eleitores podem exigir algo melhor.
Lifecycle.Reminder.5	Falta um dia para a eleição, e estou pronto para servir. Por favor, vote amanhã entre {0}.
Lifecycle.StrictVotingId.Passed	A proposta de identificação eleitoral mais rígida foi aprovada. A equipe eleitoral aplicará as novas regras de verificação nesta disputa para prefeito.
Lifecycle.StrictVotingId.Failed	A proposta de identificação eleitoral mais rígida não foi aprovada. As regras de votação permanecerão inalteradas nesta disputa para prefeito.
Lifecycle.Endorse.Board	O prefeito endossou {0} para prefeito. Moradores satisfeitos podem dar peso extra a esse endosso nesta eleição.
Lifecycle.Endorse.Mayor	Endosso {0} para prefeito. Moradores satisfeitos com os rumos da cidade devem saber que confio nessa pessoa para levar este trabalho adiante.
Lifecycle.PlatformMeeting.Board.Success	{0} aceitou revisar sua plataforma após uma reunião com o prefeito.
Lifecycle.PlatformMeeting.Board.Failed	A plataforma de {0} não mudou após uma reunião com o prefeito.
Lifecycle.PlatformMeeting.Mayor.Success	Encontrei {0} hoje para discutir sua plataforma. Encontramos pontos em comum suficientes para que aceitasse revisá-la. A contrapartida atualizada é {1} {2}.
Lifecycle.PlatformMeeting.Mayor.Failed	Encontrei {0} hoje para discutir sua plataforma. A conversa foi construtiva, mas ainda discordamos em pontos-chave, e não consegui persuadir a revisão.
Lifecycle.CorruptionInvestigation.Mayor	A polícia confirma que {0} enfrenta uma investigação de corrupção após alegações de suborno em campanha para prefeito.
Lifecycle.Election.Start.TwoLinks	O dia da eleição começou em {0} para {{LINK_1}} e {{LINK_2}}. As urnas ficam abertas de {1} em prédios de educação, assistência social, administração e correios. Você pode acompanhar a votação em tempo real clicando no botão Locais de votação para ver a sobreposição. Os resultados serão anunciados às {2}.
Lifecycle.Election.Start.Named	O dia da eleição começou em {0} para {1}. As urnas ficam abertas de {2} em prédios de educação, assistência social, administração e correios. Você pode acompanhar a votação em tempo real clicando no botão Locais de votação para ver a sobreposição. Os resultados serão anunciados às {3}.
Lifecycle.Election.VotingClosed	Todos os locais de votação estão fechados. Os resultados da eleição serão anunciados às {0}.
Lifecycle.Tamper.Loss.Destroyed	Bombeiros e Resgate confirma que {0} foi destruído. A Junta Eleitoral informa que todas as cédulas restantes nesse local foram perdidas: {1}.
Lifecycle.Tamper.Loss.Fire	Equipes de bombeiros estão respondendo a um incêndio em {0}. A Junta Eleitoral informa que {1:n0} cédulas foram destruídas antes de serem contadas: {2}.
Lifecycle.Tamper.Loss.NoBallots	nenhuma cédula
Lifecycle.Tamper.Protest	Nossa campanha perdeu {0:n0} votos após o incêndio em {1}. Esta contagem não é confiável, e {2} deve apoiar uma investigação completa.
Lifecycle.CorruptionArrest.Candidate.Singular	A polícia confirma que {{LINK_1}} foi preso após uma investigação de corrupção eleitoral. Detetives ligaram a campanha a {0} ação suspeita de campanha do prefeito.
Lifecycle.CorruptionArrest.Candidate.Plural	A polícia confirma que {{LINK_1}} foi preso após uma investigação de corrupção eleitoral. Detetives ligaram a campanha a {0} ações suspeitas de campanha do prefeito.
Lifecycle.CorruptionArrest.Mayor.Singular	A polícia confirma que {{LINK_1}} foi preso após uma investigação de suborno do prefeito. Detetives ligaram o ex-prefeito a {0} ação suspeita de campanha.
Lifecycle.CorruptionArrest.Mayor.Plural	A polícia confirma que {{LINK_1}} foi preso após uma investigação de suborno do prefeito. Detetives ligaram o ex-prefeito a {0} ações suspeitas de campanha.
Lifecycle.OutgoingMayor.FarewellDonation	Obrigado, {0}, pelo tempo em que fui prefeito. Estou doando {1:n0} de volta à cidade ao deixar o cargo.
Lifecycle.OutgoingMayor.Farewell	Obrigado, {0}, pelo tempo em que fui prefeito. Servir esta cidade foi uma honra.
Lifecycle.Runoff.VotesCounted	{0:n0} votos contados
Lifecycle.Runoff.NoVotesCounted	nenhum voto contado
Lifecycle.Runoff.Started.TwoLinks	Nenhum candidato a prefeito alcançou 50% após {0}. {{LINK_1}} ({1}%) e {{LINK_2}} ({2}%) avançam para um segundo turno. A pesquisa do segundo turno será em {3} às 8h, e a eleição final será em {4}. Programas e legislação atuais para prefeito continuam no segundo turno.
Lifecycle.Runoff.Started.Named	Nenhum candidato a prefeito alcançou 50% após {0}. {1} ({2}%) e {3} ({4}%) avançam para um segundo turno. A pesquisa do segundo turno será em {5} às 8h, e a eleição final será em {6}. Programas e legislação atuais para prefeito continuam no segundo turno.
Lifecycle.Runoff.Endorsement.Bonus	 Nossa fatia de {0}% dos votos do primeiro turno vira um bônus de apoio de segundo turno de {1} para a campanha.
Lifecycle.Runoff.Endorsement.NoBonus	 Não recebemos votos suficientes no primeiro turno para alterar a matemática do segundo turno.
Lifecycle.Runoff.Endorsement.Candidate	Ficamos para trás com {0:n0} de {1:n0} votos. Estou apoiando {2} no segundo turno.{3}
Lifecycle.Runoff.Endorsement.Board	{0} endossou {1} no segundo turno.{2}
Lifecycle.Results.SupportersNamed	 Apoiadores estão se reunindo em {0}.
Lifecycle.Results.SupportersLink3	 Apoiadores estão se reunindo em {{LINK_3}}.
Lifecycle.Results.SupportersLink1	 Apoiadores estão se reunindo em {{LINK_1}}.
Lifecycle.Results.TurnoutDenominator.Eligible	eleitores elegíveis ({0:n0} de {1:n0})
Lifecycle.Results.TurnoutDenominator.Votes	eleitores elegíveis ({0:n0} votos)
Lifecycle.Results.Transition.Today	 O novo prefeito toma posse hoje.
Lifecycle.Results.Transition.Future	 O prefeito eleito tomará posse em {0}; o prefeito atual permanece no cargo até lá.
Lifecycle.Results.Intro	Os resultados da eleição de {0} são finais. {1} foi eleito prefeito. O comparecimento foi de {2}% de {3}.
Lifecycle.Results.CandidateLinks	 Resultados: {{LINK_1}} {0}%, {{LINK_2}} {1}%.
Lifecycle.Results.CandidateNames	 Resultados: {0}.
Lifecycle.Results.Platform.NewMayor	A plataforma do novo prefeito
Lifecycle.Results.Platform.MayorElect	A plataforma do prefeito eleito
Lifecycle.Results.PlatformText	 {0} {1}.{2}
Lifecycle.PendingMayor.Inaugurated	{0} toma posse como prefeito hoje. A transição eleitoral está concluída, e a nova plataforma do prefeito está ativa.
Lifecycle.Victory.Transition.Future	 Tomamos posse em {0}.
Lifecycle.Victory.Transition.Tomorrow	 Amanhã começamos o trabalho.
Lifecycle.Victory.Winner.AtVenue	Obrigado, {0}. Vencemos hoje à noite, e estou comemorando com apoiadores em {{LINK_2}}.{1}
Lifecycle.Victory.Winner.NoVenue	Obrigado, {0}. Vencemos hoje à noite, e estou comemorando com apoiadores.{1}
Lifecycle.Victory.Loser.Role.MayorElect	como prefeito eleito
Lifecycle.Victory.Loser.Role.Mayor	como prefeito
Lifecycle.Victory.Loser.Reject	Não aceito o resultado desta noite. A margem foi de {0:0.#}%, e nossa campanha está pedindo uma revisão completa da contagem.
Lifecycle.Victory.Loser.Concede	Esta noite não foi favorável para nós. Parabenizo {0} e desejo sucesso {1}.
Lifecycle.VotingTrips.MissingRealisticTrips	Viagens de votação eleitoral exigem Realistic Trips. Nenhum morador pode ser enviado aos locais de votação pela ponte atual.
Lifecycle.Vote.IJustVoted	Acabei de votar em {0}. As urnas ficam abertas até {1}; por favor, faça sua voz ser contada.
Model.Profile.Bio	Morador {0} de uma família {1}, {2}, {3}{4}.
Model.Profile.ChirpIntro	{0} {1} {2} de uma família {3}
Model.Profile.Article.A	um
Model.Profile.Article.An	um
Model.Profile.Car.With	 com carro registrado
Model.Profile.Car.Without	 sem carro registrado
Model.Profile.Age.Elderly	Idoso
Model.Profile.Age.Adult	Adulto
Model.Profile.Age.Resident	Morador
Model.Profile.Education.Uneducated	Sem instrução
Model.Profile.Education.PoorlyEducated	Pouco instruído
Model.Profile.Education.Educated	Instruído
Model.Profile.Education.WellEducated	Bem instruído
Model.Profile.Education.HighlyEducated	Altamente instruído
Model.Profile.Work.Student	Estudante
Model.Profile.Work.Working	Morador trabalhador
Model.Profile.Work.NonWorking	Morador sem trabalho
Model.Profile.Wealth.Struggling	em dificuldade
Model.Profile.Wealth.Modest	de renda modesta
Model.Profile.Wealth.Middle	de renda média
Model.Profile.Wealth.Comfortable	confortável
Model.Profile.Wealth.Wealthy	rica
Model.Poll.NoSample.Label	Sem amostra de pesquisa
Model.Poll.NoSample.Description	Nenhum morador elegível foi amostrado.
Model.Poll.DeadHeat.Label	Empate técnico
Model.Poll.DeadHeat.TwoCandidateDescription	{0} e {1} estão dentro da margem de erro de +/-{2}%.
Model.Poll.DeadHeat.MultiCandidateDescription	Os candidatos líderes estão dentro da margem de erro de +/-{0}%.
Model.Poll.NarrowEdge.Label	{0} tem uma pequena vantagem
Model.Poll.NarrowEdge.Description	{0} lidera, mas a disputa ainda está apertada com margem de erro de +/-{1}%.
Model.Poll.OutsideMargin.Label	{0} lidera fora da margem
Model.Poll.OutsideMargin.Description	{0} lidera por {1} pontos, fora da margem de erro de +/-{2}%.
Model.Poll.NoCandidate	Sem candidato
Model.Platform.Name	Agenda do prefeito
Model.Platform.Description	{0}, mas {1}
Model.Platform.Money.PositiveSentence	{0} {1:n0} a {2}
Model.Platform.Money.NegativeSentence	{0} {1:n0} de {2}
Model.Platform.PercentSentence	{0} {1} em {2}
Model.Platform.DoubleSentence	dobra {0}
Model.Platform.HalfSentence	reduz {0} pela metade
Model.Platform.NoPlatform	Sem plataforma
Model.Platform.NeutralSentence	mantém a política da cidade neutra
Model.Platform.Transition.Name	Transição Democrática
Model.Platform.Transition.Description	mantém a política da cidade neutra enquanto supervisiona o processo eleitoral até que os moradores elejam um prefeito
Model.Platform.Verb.adds	adiciona
Model.Platform.Verb.lowers	baixa
Model.Platform.Verb.raises	aumenta
Model.Platform.Verb.removes	remove
Model.Platform.Verb.doubles	dobra
Model.Platform.Verb.reduces	reduz
Model.Platform.Verb.cuts	corta
Model.Platform.Verb.increases	aumenta
Model.Platform.Money.Label	Fundos da cidade
Model.Platform.Money.Target	fundos da cidade
Model.Platform.ImportCost.Label	Custos de importação
Model.Platform.ImportCost.Target	custos de importação
Model.Platform.ExportCost.Label	Custos de exportação
Model.Platform.ExportCost.Target	custos de exportação
Model.Platform.LoanInterest.Label	Juros de empréstimo
Model.Platform.LoanInterest.Target	juros de empréstimo
Model.Platform.BuildingLevelingCost.Label	Custo de nivelamento de prédios
Model.Platform.BuildingLevelingCost.Target	custos de nivelamento de prédios
Model.Platform.TaxiStartingFee.Label	Taxa inicial de táxi
Model.Platform.TaxiStartingFee.Target	taxas iniciais de táxi
Model.Platform.CityServiceUpkeep.Label	Manutenção dos serviços da cidade
Model.Platform.CityServiceUpkeep.Target	manutenção dos serviços da cidade
Model.Platform.CrimeProbability.Label	Probabilidade de crime
Model.Platform.CrimeProbability.Target	probabilidade de crime
Model.Platform.CrimeAccumulation.Label	Acúmulo de criminalidade
Model.Platform.CrimeAccumulation.Target	acúmulo de criminalidade
Model.Platform.DiseaseProbability.Label	Probabilidade de doença
Model.Platform.DiseaseProbability.Target	probabilidade de doença
Model.Platform.HospitalEfficiency.Label	Eficiência hospitalar
Model.Platform.HospitalEfficiency.Target	eficiência hospitalar
Model.Platform.PollutionHealthAffect.Label	Impacto da poluição na saúde
Model.Platform.PollutionHealthAffect.Target	impacto da poluição na saúde
Model.Platform.IndustrialAirPollution.Label	Poluição do ar industrial
Model.Platform.IndustrialAirPollution.Target	poluição do ar industrial
Model.Platform.IndustrialGroundPollution.Label	Poluição do solo industrial
Model.Platform.IndustrialGroundPollution.Target	poluição do solo industrial
Model.Platform.IndustrialGarbage.Label	Lixo industrial
Model.Platform.IndustrialGarbage.Target	lixo industrial
Model.Platform.IndustrialEfficiency.Label	Eficiência industrial
Model.Platform.IndustrialEfficiency.Target	eficiência industrial
Model.Platform.OfficeEfficiency.Label	Eficiência de escritórios
Model.Platform.OfficeEfficiency.Target	eficiência de escritórios
Model.Platform.IndustrialElectronicsEfficiency.Label	Eficiência de eletrônicos
Model.Platform.IndustrialElectronicsEfficiency.Target	eficiência de eletrônicos industriais
Model.Platform.OfficeSoftwareEfficiency.Label	Eficiência de software
Model.Platform.OfficeSoftwareEfficiency.Target	eficiência de software de escritórios
Model.Platform.IndustrialElectronicsDemand.Label	Demanda por eletrônicos
Model.Platform.IndustrialElectronicsDemand.Target	demanda por eletrônicos industriais
Model.Platform.OfficeSoftwareDemand.Label	Demanda por software
Model.Platform.OfficeSoftwareDemand.Target	demanda por software de escritórios
Model.Platform.Attractiveness.Label	Atratividade da cidade
Model.Platform.Attractiveness.Target	atratividade da cidade
Model.Platform.Entertainment.Label	Entretenimento
Model.Platform.Entertainment.Target	entretenimento
Model.Platform.ParkEntertainment.Label	Entretenimento dos parques
Model.Platform.ParkEntertainment.Target	entretenimento dos parques
Model.Platform.CollegeGraduation.Label	Formatura em faculdades
Model.Platform.CollegeGraduation.Target	formatura em faculdades
Model.Platform.UniversityGraduation.Label	Formatura universitária
Model.Platform.UniversityGraduation.Target	formatura universitária
Model.Platform.UniversityInterest.Label	Interesse em universidades
Model.Platform.UniversityInterest.Target	interesse em universidades
Model.Platform.AccumulatedXP.Label	XP acumulado
Model.Platform.AccumulatedXP.Target	XP acumulado
Model.Platform.ResourceConsumption.Label	Consumo de recursos dos cidadãos
Model.Platform.ResourceConsumption.Target	consumo de recursos dos cidadãos
Model.CandidateTag.1.Name	Corrupto
Model.CandidateTag.1.Description	Se eleito, ações de campanha do prefeito custam metade.
Model.CandidateTag.2.Name	Honesto
Model.CandidateTag.2.Description	Se eleito, ações de campanha do prefeito custam o dobro.
Model.CandidateTag.3.Name	Origem humilde
Model.CandidateTag.3.Description	+10% de apoio de moradores de baixa renda.
Model.CandidateTag.4.Name	Passado controverso
Model.CandidateTag.4.Description	-10% de apoio geral dos eleitores.
Model.CandidateTag.5.Name	Cientista
Model.CandidateTag.5.Description	+10% de apoio de moradores altamente instruídos.
Model.CandidateTag.6.Name	Econômico
Model.CandidateTag.6.Description	Doações de campanha custam metade pelo mesmo efeito.
Model.CandidateTag.7.Name	Extravagante
Model.CandidateTag.7.Description	Doações de campanha custam o dobro pelo mesmo efeito.
Model.CandidateTag.8.Name	De base comunitária
Model.CandidateTag.8.Description	Doações de campanha contam 25% mais.
Model.CandidateTag.9.Name	Arrecadador
Model.CandidateTag.9.Description	Doações de campanha contam 15% mais, mas moradores de baixa renda dão menos apoio.
Model.CandidateTag.10.Name	Mau orador
Model.CandidateTag.10.Description	-5% de apoio geral dos eleitores.
Model.CandidateTag.11.Name	Carismático
Model.CandidateTag.11.Description	+5% de apoio geral dos eleitores.
Model.CandidateTag.12.Name	Organizador sindical
Model.CandidateTag.12.Description	+10% de apoio de trabalhadores.
Model.CandidateTag.13.Name	Preferido dos estudantes
Model.CandidateTag.13.Description	+10% de apoio de estudantes e eleitores adolescentes.
Model.CandidateTag.14.Name	Estadista experiente
Model.CandidateTag.14.Description	+10% de apoio de eleitores idosos.
Model.CandidateTag.15.Name	Jovem reformista
Model.CandidateTag.15.Description	+8% de apoio de adolescentes e adultos, -4% de eleitores idosos.
Model.CandidateTag.16.Name	Tecnocrata
Model.CandidateTag.16.Description	+8% de apoio de moradores bem instruídos, -5% de moradores sem instrução.
Model.CandidateTag.17.Name	Populista
Model.CandidateTag.17.Description	+8% de apoio de moradores de baixa renda, -5% de moradores ricos.
Model.CandidateTag.18.Name	Conexões de elite
Model.CandidateTag.18.Description	+8% de apoio de moradores ricos, -5% de moradores de baixa renda.
Model.CandidateTag.19.Name	Defensor do transporte público
Model.CandidateTag.19.Description	+10% de apoio de moradores sem carro.
Model.CandidateTag.20.Name	Defensor dos motoristas
Model.CandidateTag.20.Description	+10% de apoio de moradores com carro.
Model.CandidateTag.21.Name	Lei e ordem
Model.CandidateTag.21.Description	+8% de apoio de idosos e moradores insatisfeitos.
Model.CandidateTag.22.Name	Ambientalista
Model.CandidateTag.22.Description	+8% de apoio de estudantes e moradores bem instruídos.
Model.CandidateTag.23.Name	Pró-negócios
Model.CandidateTag.23.Description	+8% de apoio de trabalhadores e moradores ricos.
Model.CandidateTag.24.Name	Defensor dos bairros
Model.CandidateTag.24.Description	+6% de apoio de moradores de baixa e média renda.
Model.CandidateTag.25.Name	Polarizador
Model.CandidateTag.25.Description	O comparecimento eleitoral aumenta em 15%.
Model.CandidateTag.26.Name	Revolucionário
Model.CandidateTag.26.Description	Dobra os efeitos positivos e negativos da plataforma deste candidato.
Model.CandidateTag.27.Name	Cauteloso
Model.CandidateTag.27.Description	Reduz pela metade os efeitos positivos e negativos da plataforma deste candidato, mas reduz o apoio geral dos eleitores em 5%.
Model.CandidateTag.Description.HumbleBeginnings	+{0}% de apoio de moradores de baixa renda.
Model.CandidateTag.Description.ControversialPast	-{0}% de apoio geral dos eleitores.
Model.CandidateTag.Description.Scientist	+{0}% de apoio de moradores altamente instruídos.
Model.CandidateTag.Description.Fundraiser	Doações de campanha contam 15% mais, mas o apoio de baixa renda cai {0}%.
Model.CandidateTag.Description.PoorSpeaker	-{0}% de apoio geral dos eleitores.
Model.CandidateTag.Description.Charismatic	+{0}% de apoio geral dos eleitores.
Model.CandidateTag.Description.UnionOrganizer	+{0}% de apoio de trabalhadores.
Model.CandidateTag.Description.StudentFavorite	+{0}% de apoio de estudantes e eleitores adolescentes.
Model.CandidateTag.Description.ElderStatesperson	+{0}% de apoio de eleitores idosos.
Model.CandidateTag.Description.YoungReformer	+{0}% de apoio de adolescentes e adultos, -{1}% de eleitores idosos.
Model.CandidateTag.Description.Technocrat	+{0}% de apoio de moradores bem instruídos, -{1}% de moradores sem instrução.
Model.CandidateTag.Description.Populist	+{0}% de apoio de moradores de baixa renda, -{1}% de moradores ricos.
Model.CandidateTag.Description.EliteConnections	+{0}% de apoio de moradores ricos, -{1}% de moradores de baixa renda.
Model.CandidateTag.Description.TransitAdvocate	+{0}% de apoio de moradores sem carro.
Model.CandidateTag.Description.MotoristAdvocate	+{0}% de apoio de moradores com carro.
Model.CandidateTag.Description.LawAndOrder	+{0}% de apoio de idosos e moradores insatisfeitos.
Model.CandidateTag.Description.Environmentalist	+{0}% de apoio de estudantes e moradores bem instruídos.
Model.CandidateTag.Description.BusinessFriendly	+{0}% de apoio de trabalhadores e moradores ricos.
Model.CandidateTag.Description.NeighborhoodChampion	+{0}% de apoio de moradores de baixa e média renda.
Model.CandidateTag.Description.Polarizing	O comparecimento eleitoral aumenta em {0}%.
Model.CandidateTag.Description.Cautious	Reduz pela metade os efeitos positivos e negativos da plataforma deste candidato, mas reduz o apoio geral dos eleitores em {0}%.
Model.PartyTag.1.Name	Confiança Cívica
Model.PartyTag.1.Description	Chance de escândalo de corrupção -10 pontos e a perda de reputação do partido por escândalo é 5 pontos menor.
Model.PartyTag.2.Name	Chapa Reformista
Model.PartyTag.2.Description	+8% de apoio de eleitores insatisfeitos quando o partido não é incumbente.
Model.PartyTag.3.Name	Máquina Organizada
Model.PartyTag.3.Description	Doações de campanha contam 20% mais.
Model.PartyTag.4.Name	Coalizão do Transporte Público
Model.PartyTag.4.Description	+8% de apoio de eleitores sem carro, mais +2% enquanto Vale-transporte estiver ativo.
Model.PartyTag.5.Name	Liberdades Civis
Model.PartyTag.5.Description	+6% de apoio de estudantes e eleitores instruídos, e +8% de trabalhadores sem instrução após a aprovação da identificação eleitoral rígida.
Model.PartyTag.6.Name	Raízes Locais
Model.PartyTag.6.Description	+5% de apoio de moradores de baixa e média renda.
Model.PartyTag.7.Name	Pragmático
Model.PartyTag.7.Description	Se eleito, impactos negativos da plataforma são 15% mais suaves.
Model.PartyTag.8.Name	Alcance Estudantil
Model.PartyTag.8.Description	+5% de apoio de eleitores adolescentes e estudantes.
Model.PartyTag.9.Name	Foco em Empregos
Model.PartyTag.9.Description	+5% de apoio de trabalhadores.
Model.PartyTag.10.Name	Pró-negócios
Model.PartyTag.10.Description	+5% de apoio de moradores ricos e trabalhadores.
Model.PartyTag.11.Name	Não testado
Model.PartyTag.11.Description	-4% de apoio geral até o partido vencer sua primeira eleição.
Model.PartyTag.12.Name	Ideológico
Model.PartyTag.12.Description	Se eleito, impactos negativos da plataforma são 10% mais fortes.
Model.PartyTag.13.Name	Dividido
Model.PartyTag.13.Description	Doações de campanha contam 10% menos e a chance de suavizar plataforma por doação é 10 pontos menor.
Model.PartyTag.14.Name	Velha guarda
Model.PartyTag.14.Description	-5% de apoio de eleitores adolescentes e estudantes.
Model.PartyTag.15.Name	Excesso de confiança
Model.PartyTag.15.Description	Se liderar uma pesquisa divulgada fora da margem de erro, perde 4% de apoio no dia da eleição.
Model.PartyTag.16.Name	Complacente
Model.PartyTag.16.Description	Se for incumbente, perde 5% de apoio geral.
Model.PartyTag.17.Name	Elitista
Model.PartyTag.17.Description	-8% de apoio de moradores de baixa renda.
Model.PartyTag.18.Name	Propenso a escândalos
Model.PartyTag.18.Description	Ações de campanha suspeitas adicionam uma etapa extra de risco de corrupção e a perda de reputação por escândalo é 5 pontos mais dura.
Model.PartyTag.19.Name	Desorganizado
Model.PartyTag.19.Description	Doações de campanha contam 20% menos e a chance de sucesso de reunião de plataforma do prefeito é 15 pontos menor.
Model.PartyTag.20.Name	Fora de sintonia
Model.PartyTag.20.Description	-8% de apoio de eleitores insatisfeitos.
Model.SupportProgram.0.Title	Tornar o dia da eleição um feriado
Model.SupportProgram.0.Description	Trata o dia da eleição como domingo para os horários dos moradores.
Model.SupportProgram.0.Tooltip	Tornar o dia da eleição um feriado dá aos moradores mais tempo para votar e pode aumentar o comparecimento.
Model.SupportProgram.1.Title	Educação eleitoral para adolescentes
Model.SupportProgram.1.Description	Adiciona +{0}% ao comparecimento eleitoral de adolescentes.
Model.SupportProgram.1.Tooltip	Financie uma campanha de educação cívica para eleitores adolescentes. Cada campanha adiciona +{0}% ao comparecimento eleitoral de adolescentes.
Model.SupportProgram.2.Title	Educação eleitoral para adultos
Model.SupportProgram.2.Description	Adiciona +{0}% ao comparecimento eleitoral de adultos.
Model.SupportProgram.2.Tooltip	Financie uma campanha de educação cívica para eleitores adultos. Cada campanha adiciona +{0}% ao comparecimento eleitoral de adultos.
Model.SupportProgram.3.Title	Educação eleitoral para idosos
Model.SupportProgram.3.Description	Adiciona +{0}% ao comparecimento eleitoral de idosos.
Model.SupportProgram.3.Tooltip	Financie uma campanha de educação cívica para eleitores idosos. Cada campanha adiciona +{0}% ao comparecimento eleitoral de idosos.
Model.SupportProgram.4.Title	Programa de educação eleitoral
Model.SupportProgram.4.Description	Adiciona +{0}% ao comparecimento eleitoral de moradores sem instrução e pouco instruídos.
Model.SupportProgram.4.Tooltip	Financie um programa de educação eleitoral para moradores sem instrução e pouco instruídos. Cada programa adiciona +{0}% ao comparecimento eleitoral desses grupos de escolaridade.
Model.SupportProgram.5.Title	Contato com eleitores de baixa renda
Model.SupportProgram.5.Description	Adiciona +{0}% ao comparecimento eleitoral de moradores em dificuldade e de renda modesta.
Model.SupportProgram.5.Tooltip	Financie contato direto com eleitores em dificuldade e de renda modesta. Cada programa adiciona +{0}% ao comparecimento eleitoral desses grupos de renda.
Model.SupportProgram.6.Title	Vale-transporte
Model.SupportProgram.6.Description	Adiciona +{0}% ao comparecimento eleitoral de adolescentes, idosos e moradores de baixa renda sem carro.
Model.SupportProgram.6.Tooltip	Financie vales-transporte para adolescentes, idosos e moradores em dificuldade ou de renda modesta que não têm carro. Cada programa adiciona +{0}% ao comparecimento eleitoral dos moradores elegíveis.
Model.SupportProgram.7.Title	Fóruns cívicos
Model.SupportProgram.7.Description	Adiciona +{0}% ao comparecimento eleitoral de moradores instruídos, bem instruídos e altamente instruídos.
Model.SupportProgram.7.Tooltip	Financie fóruns públicos de candidatos, debates de políticas e palestras cívicas para moradores instruídos. Cada fórum adiciona +{0}% ao comparecimento eleitoral de moradores instruídos, bem instruídos e altamente instruídos.
Model.Legislation.0.Title	Portaria de Identificação do Eleitor
Model.Legislation.0.Description	{0}% de comparecimento de trabalhadores sem instrução.
Model.Legislation.0.Tooltip	Exige verificações adicionais de identificação eleitoral. Isso reduz o comparecimento de trabalhadores sem instrução.
Model.Legislation.1.Title	Lei de Notificação Eleitoral a Proprietários
Model.Legislation.1.Description	+{0}% de comparecimento de ricos, +{1}% de comparecimento de proprietários de carro, {2}% de comparecimento de baixa renda.
Model.Legislation.1.Tooltip	Prioriza avisos eleitorais por correio usando registros de propriedade e veículos. Isso aumenta o comparecimento de moradores ricos e proprietários de carro, mas pode reduzir o comparecimento de baixa renda.
Model.Legislation.2.Title	Lei de Registro Cívico Juvenil
Model.Legislation.2.Description	+{0}% de comparecimento de adolescentes, +{1}% de comparecimento de estudantes, {2}% de comparecimento de idosos.
Model.Legislation.2.Tooltip	Cria registro cívico automático para escolas e serviços juvenis. Isso aumenta o comparecimento de adolescentes e estudantes, mas reduz levemente o comparecimento de idosos.
Model.Legislation.3.Title	Lei de Votação de Acesso aos Bairros
Model.Legislation.3.Description	+{0}% de comparecimento de baixa renda, +{1}% de comparecimento de moradores sem carro, {2}% de comparecimento de ricos.
Model.Legislation.3.Tooltip	Prioriza regras de acesso de bairro e assistência local à votação. Isso aumenta o comparecimento de baixa renda e de moradores sem carro, mas pode reduzir o comparecimento de ricos.
Model.Legislation.4.Title	Lei de Continuidade de Governo
Model.Legislation.4.Description	+{0}% de comparecimento de moradores satisfeitos, +{1}% de comparecimento de idosos, {2}% de comparecimento de moradores insatisfeitos enquanto o partido incumbente estiver concorrendo.
Model.Legislation.4.Tooltip	Promove procedimentos de transição de governo estáveis. Enquanto o partido incumbente estiver concorrendo, moradores satisfeitos e idosos comparecem mais, enquanto moradores insatisfeitos comparecem menos.
");
        }

        private static void AddElectionTable(Dictionary<string, string> entries, string table)
        {
            foreach (string rawLine in table.Split(new[] { '\r', '\n' }, System.StringSplitOptions.RemoveEmptyEntries))
            {
                string line = rawLine.TrimStart();
                int separator = line.IndexOf('\t');
                if (separator <= 0)
                    continue;

                string key = line.Substring(0, separator);
                string value = line.Substring(separator + 1);
                entries[ElectionLocalization.ID(key)] = value;
            }
        }

        public void Unload()
        {
        }
    }
}
