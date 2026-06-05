using Colossal;
using Colossal.IO.AssetDatabase;
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
    [FileLocation(nameof(Elections))]
    [SettingsUIGroupOrder(kGeneralGroup, kVotingGroup, kCampaignGroup, kDebugGroup)]
    [SettingsUIShowGroupName(kGeneralGroup, kVotingGroup, kCampaignGroup, kDebugGroup)]
    public class Setting : ModSetting
    {
        public const string kSection = "Main";
        public const string kGeneralGroup = "General";
        public const string kVotingGroup = "Voting";
        public const string kCampaignGroup = "Campaign";
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

        public Setting(IMod mod) : base(mod)
        {
        }

        [SettingsUISection(kSection, kGeneralGroup)]
        public bool EnableElections { get; set; }

        [SettingsUISection(kSection, kGeneralGroup)]
        public bool ElectionDayActsLikeSunday { get; set; }

        [SettingsUISection(kSection, kGeneralGroup)]
        [SettingsUISetter(typeof(Setting), nameof(SetUseUniversalModMenu))]
        public bool UseUniversalModMenu { get; set; }

        [SettingsUISlider(min = 1, max = 10, step = 1, scalarMultiplier = 1, unit = Unit.kPercentage)]
        [SettingsUISection(kSection, kVotingGroup)]
        public int PollSamplePercent { get; set; }

        [SettingsUISlider(min = 1, max = 100, step = 1, scalarMultiplier = 1, unit = Unit.kPercentage)]
        [SettingsUISection(kSection, kVotingGroup)]
        public int TeenHourlyVotingAttemptPercent { get; set; } = 50;

        [SettingsUISlider(min = 1, max = 100, step = 1, scalarMultiplier = 1, unit = Unit.kPercentage)]
        [SettingsUISection(kSection, kVotingGroup)]
        public int HourlyVotingAttemptPercent { get; set; } = 75;

        [SettingsUISlider(min = 1, max = 100, step = 1, scalarMultiplier = 1, unit = Unit.kPercentage)]
        [SettingsUISection(kSection, kVotingGroup)]
        public int ElderlyHourlyVotingAttemptPercent { get; set; } = 30;

        [SettingsUISection(kSection, kVotingGroup)]
        public VotingStartHourOption ElectionVotingStartHour { get; set; } = VotingStartHourOption.Hour8;

        [SettingsUISection(kSection, kVotingGroup)]
        public VotingEndHourOption ElectionVotingEndHour { get; set; } = VotingEndHourOption.Hour17;

        [SettingsUISection(kSection, kDebugGroup)]
        public bool EnableDebugLogging { get; set; }

        [SettingsUIButton]
        [SettingsUIConfirmation]
        [SettingsUISection(kSection, kDebugGroup)]
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
        [SettingsUISection(kSection, kDebugGroup)]
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

        public override void SetDefaults()
        {
            EnableElections = true;
            ElectionDayActsLikeSunday = true;
            UseUniversalModMenu = false;
            PollSamplePercent = 2;
            TeenHourlyVotingAttemptPercent = 50;
            HourlyVotingAttemptPercent = 75;
            ElderlyHourlyVotingAttemptPercent = 30;
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
            return new Dictionary<string, string>
            {
                { m_Setting.GetSettingsLocaleID(), "Elections" },
                { m_Setting.GetOptionTabLocaleID(Setting.kSection), "Main" },

                { m_Setting.GetOptionGroupLocaleID(Setting.kGeneralGroup), "General" },
                { m_Setting.GetOptionGroupLocaleID(Setting.kVotingGroup), "Voting" },
                { m_Setting.GetOptionGroupLocaleID(Setting.kCampaignGroup), "Campaign" },
                { m_Setting.GetOptionGroupLocaleID(Setting.kDebugGroup), "Debug" },

                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.EnableElections)), "Enable Elections" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.EnableElections)), "Run the mayoral election cycle." },

                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.ElectionDayActsLikeSunday)), "Election Day Sunday Mode" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.ElectionDayActsLikeSunday)), "When enabled, Realistic Trips treats election day as a Sunday for work, school, leisure, demand, and citizen schedule probabilities." },

                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.UseUniversalModMenu)), "Use Universal Mod Menu" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.UseUniversalModMenu)), "Show the Elections button in Universal Mod Menu instead of the top-left game overlay." },

                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.PollSamplePercent)), "Poll Sample Percent" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.PollSamplePercent)), "Approximate share of total city population represented in the campaign poll. Default is 2% and the maximum is 10%." },

                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.TeenHourlyVotingAttemptPercent)), "Teen Voting Percent" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.TeenHourlyVotingAttemptPercent)), "Approximate hourly chance that an available teen resident attempts to go vote during election hours. Default is 50%." },

                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.HourlyVotingAttemptPercent)), "Adult Voting Percent" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.HourlyVotingAttemptPercent)), "Approximate hourly chance that an available adult resident attempts to go vote during election hours. Default is 75%." },

                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.ElderlyHourlyVotingAttemptPercent)), "Elderly Voting Percent" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.ElderlyHourlyVotingAttemptPercent)), "Approximate hourly chance that an available elderly resident attempts to go vote during election hours. Default is 30%." },

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

                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.EnableDebugLogging)), "Enable Debug Logging" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.EnableDebugLogging)), "Write detailed Elections lifecycle, campaign, poll, voting, donation, mayor, and repair events to the game log." },

                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.ForceStartCampaign)), "Force Start Campaign" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.ForceStartCampaign)), "Debug action: immediately select candidates and announce the campaign." },
                { m_Setting.GetOptionWarningLocaleID(nameof(Setting.ForceStartCampaign)), "Start a new campaign now?" },

                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.ForceElectionToday)), "Force Election Today" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.ForceElectionToday)), "Debug action: begin voting today if candidates exist." },
                { m_Setting.GetOptionWarningLocaleID(nameof(Setting.ForceElectionToday)), "Begin election voting today?" }
            };
        }

        public void Unload()
        {
        }
    }
}
