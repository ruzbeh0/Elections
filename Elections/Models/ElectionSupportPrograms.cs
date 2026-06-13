using System;

namespace Elections.Models
{
    internal enum ElectionSupportProgramType
    {
        ElectionDayHoliday = 0,
        TeenVoterEducation = 1,
        AdultVoterEducation = 2,
        ElderlyVoterEducation = 3,
        VoterEducation = 4,
        LowIncomeVoterOutreach = 5,
        TransitVouchers = 6,
        CivicForums = 7
    }

    internal readonly struct ElectionSupportProgramDefinition
    {
        private readonly string m_Title;
        private readonly string m_Description;
        private readonly string m_Tooltip;

        public ElectionSupportProgramDefinition(ElectionSupportProgramType type, string title, string description, string tooltip)
        {
            Type = type;
            m_Title = title;
            m_Description = description;
            m_Tooltip = tooltip;
        }

        public ElectionSupportProgramType Type { get; }
        public int Index => (int)Type;
        public string Title => ElectionLocalization.Translate($"Model.SupportProgram.{Index}.Title", m_Title);
        public string Description => Format("Description", m_Description);
        public string Tooltip => Format("Tooltip", m_Tooltip);

        private string Format(string suffix, string fallback)
        {
            int bonus = ElectionSupportPrograms.GetBonusPercent(Type);
            return bonus > 0
                ? ElectionLocalization.Format($"Model.SupportProgram.{Index}.{suffix}", fallback, bonus)
                : ElectionLocalization.Translate($"Model.SupportProgram.{Index}.{suffix}", fallback);
        }
    }

    internal static class ElectionSupportPrograms
    {
        public const int Count = 8;
        public const int LegacyTurnoutProgramDailyBonusPercent = 10;
        public const int TeenTurnoutProgramDailyBonusPercent = 30;
        public const int AdultTurnoutProgramDailyBonusPercent = 10;
        public const int ElderlyTurnoutProgramDailyBonusPercent = 15;
        public const int EducationTurnoutProgramDailyBonusPercent = 10;
        public const int LowIncomeTurnoutProgramDailyBonusPercent = 10;
        public const int TransitVoucherTurnoutProgramDailyBonusPercent = 5;
        public const int CivicForumTurnoutProgramDailyBonusPercent = 5;

        public static bool TryGet(int index, out ElectionSupportProgramDefinition definition)
        {
            switch ((ElectionSupportProgramType)index)
            {
                case ElectionSupportProgramType.ElectionDayHoliday:
                    definition = new ElectionSupportProgramDefinition(
                        ElectionSupportProgramType.ElectionDayHoliday,
                        "Make election day a holiday",
                        "Treats election day as a Sunday for resident schedules.",
                        "Making election day a holiday gives residents more time to vote and can increase turnout.");
                    return true;
                case ElectionSupportProgramType.TeenVoterEducation:
                    definition = new ElectionSupportProgramDefinition(
                        ElectionSupportProgramType.TeenVoterEducation,
                        "Teen voter education",
                        $"Adds +{TeenTurnoutProgramDailyBonusPercent}% teen election turnout.",
                        $"Fund a civic education campaign for teen voters. Each campaign adds +{TeenTurnoutProgramDailyBonusPercent}% to teen election turnout.");
                    return true;
                case ElectionSupportProgramType.AdultVoterEducation:
                    definition = new ElectionSupportProgramDefinition(
                        ElectionSupportProgramType.AdultVoterEducation,
                        "Adult voter education",
                        $"Adds +{AdultTurnoutProgramDailyBonusPercent}% adult election turnout.",
                        $"Fund a civic education campaign for adult voters. Each campaign adds +{AdultTurnoutProgramDailyBonusPercent}% to adult election turnout.");
                    return true;
                case ElectionSupportProgramType.ElderlyVoterEducation:
                    definition = new ElectionSupportProgramDefinition(
                        ElectionSupportProgramType.ElderlyVoterEducation,
                        "Elderly voter education",
                        $"Adds +{ElderlyTurnoutProgramDailyBonusPercent}% elderly election turnout.",
                        $"Fund a civic education campaign for elderly voters. Each campaign adds +{ElderlyTurnoutProgramDailyBonusPercent}% to elderly election turnout.");
                    return true;
                case ElectionSupportProgramType.VoterEducation:
                    definition = new ElectionSupportProgramDefinition(
                        ElectionSupportProgramType.VoterEducation,
                        "Voter education program",
                        $"Adds +{EducationTurnoutProgramDailyBonusPercent}% uneducated and poorly educated election turnout.",
                        $"Fund a voter education program for uneducated and poorly educated residents. Each program adds +{EducationTurnoutProgramDailyBonusPercent}% to election turnout for those education groups.");
                    return true;
                case ElectionSupportProgramType.LowIncomeVoterOutreach:
                    definition = new ElectionSupportProgramDefinition(
                        ElectionSupportProgramType.LowIncomeVoterOutreach,
                        "Low-income voter outreach",
                        $"Adds +{LowIncomeTurnoutProgramDailyBonusPercent}% election turnout for struggling and modest-income residents.",
                        $"Fund direct voter outreach for struggling and modest-income residents. Each program adds +{LowIncomeTurnoutProgramDailyBonusPercent}% to election turnout for those income groups.");
                    return true;
                case ElectionSupportProgramType.TransitVouchers:
                    definition = new ElectionSupportProgramDefinition(
                        ElectionSupportProgramType.TransitVouchers,
                        "Transit vouchers",
                        $"Adds +{TransitVoucherTurnoutProgramDailyBonusPercent}% election turnout for teens, elderly, and low-income residents without cars.",
                        $"Fund transit vouchers for teens, elderly residents, and struggling or modest-income residents who do not have a car. Each program adds +{TransitVoucherTurnoutProgramDailyBonusPercent}% to election turnout for eligible residents.");
                    return true;
                case ElectionSupportProgramType.CivicForums:
                    definition = new ElectionSupportProgramDefinition(
                        ElectionSupportProgramType.CivicForums,
                        "Civic forums",
                        $"Adds +{CivicForumTurnoutProgramDailyBonusPercent}% election turnout for educated, well educated, and highly educated residents.",
                        $"Fund public candidate forums, policy debates, and civic talks for educated residents. Each forum adds +{CivicForumTurnoutProgramDailyBonusPercent}% to election turnout for educated, well educated, and highly educated residents.");
                    return true;
                default:
                    definition = default;
                    return false;
            }
        }

        public static int GetCost(int campaignDonationAmount)
        {
            return Math.Max(1, ElectionDonationTiers.NormalizeDonationAmount(campaignDonationAmount) / 2);
        }

        public static int GetBonusPercent(ElectionSupportProgramType type)
        {
            switch (type)
            {
                case ElectionSupportProgramType.TeenVoterEducation:
                    return TeenTurnoutProgramDailyBonusPercent;
                case ElectionSupportProgramType.AdultVoterEducation:
                    return AdultTurnoutProgramDailyBonusPercent;
                case ElectionSupportProgramType.ElderlyVoterEducation:
                    return ElderlyTurnoutProgramDailyBonusPercent;
                case ElectionSupportProgramType.VoterEducation:
                    return EducationTurnoutProgramDailyBonusPercent;
                case ElectionSupportProgramType.LowIncomeVoterOutreach:
                    return LowIncomeTurnoutProgramDailyBonusPercent;
                case ElectionSupportProgramType.TransitVouchers:
                    return TransitVoucherTurnoutProgramDailyBonusPercent;
                case ElectionSupportProgramType.CivicForums:
                    return CivicForumTurnoutProgramDailyBonusPercent;
                default:
                    return 0;
            }
        }
    }
}
