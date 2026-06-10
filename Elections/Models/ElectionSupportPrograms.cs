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
        TransitVouchers = 6
    }

    internal readonly struct ElectionSupportProgramDefinition
    {
        public ElectionSupportProgramDefinition(ElectionSupportProgramType type, string title, string description, string tooltip)
        {
            Type = type;
            Title = title;
            Description = description;
            Tooltip = tooltip;
        }

        public ElectionSupportProgramType Type { get; }
        public int Index => (int)Type;
        public string Title { get; }
        public string Description { get; }
        public string Tooltip { get; }
    }

    internal static class ElectionSupportPrograms
    {
        public const int Count = 7;
        public const int LegacyTurnoutProgramDailyBonusPercent = 10;
        public const int TeenTurnoutProgramDailyBonusPercent = 30;
        public const int AdultTurnoutProgramDailyBonusPercent = 10;
        public const int ElderlyTurnoutProgramDailyBonusPercent = 15;
        public const int EducationTurnoutProgramDailyBonusPercent = 10;
        public const int LowIncomeTurnoutProgramDailyBonusPercent = 10;
        public const int TransitVoucherTurnoutProgramDailyBonusPercent = 5;

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
                default:
                    return 0;
            }
        }
    }
}
