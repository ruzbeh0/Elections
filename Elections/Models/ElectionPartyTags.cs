using Elections.Components;
using Game.Citizens;
using Unity.Mathematics;

namespace Elections.Models
{
    internal readonly struct ElectionPartyTagDefinition
    {
        private readonly string m_Name;
        private readonly string m_Description;

        public ElectionPartyTagDefinition(int id, string name, string description, int value, ElectionCandidateTagTone tone)
        {
            Id = id;
            m_Name = name;
            m_Description = description;
            Value = value;
            Tone = tone;
        }

        public int Id { get; }
        public string Name => Id == ElectionPartyTags.None ? string.Empty : ElectionLocalization.Translate($"Model.PartyTag.{Id}.Name", m_Name);
        public string Description => Id == ElectionPartyTags.None ? string.Empty : ElectionLocalization.Translate($"Model.PartyTag.{Id}.Description", m_Description);
        public int Value { get; }
        public ElectionCandidateTagTone Tone { get; }
    }

    internal static class ElectionPartyTags
    {
        public const int None = 0;
        public const int CivicTrust = 1;
        public const int ReformSlate = 2;
        public const int OrganizedMachine = 3;
        public const int TransitCoalition = 4;
        public const int CivilLiberties = 5;
        public const int LocalRoots = 6;
        public const int Pragmatic = 7;
        public const int StudentOutreach = 8;
        public const int JobsFocused = 9;
        public const int BusinessFriendly = 10;
        public const int Unproven = 11;
        public const int Ideological = 12;
        public const int Divided = 13;
        public const int OldGuard = 14;
        public const int Overconfident = 15;
        public const int Complacent = 16;
        public const int Elitist = 17;
        public const int ScandalProne = 18;
        public const int Disorganized = 19;
        public const int OutOfTouch = 20;

        public const int Count = 20;
        public const int TagsPerParty = 3;
        public const int DefaultReputation = 50;
        public const int MinReputation = 0;
        public const int MaxReputation = 100;
        public const int NonWinnerReputationGainPool = 10;

        public const int PurpleColor = 0xb16cff;
        public const int GreenColor = 0x62d26f;
        public const int PinkColor = 0xff6fb3;
        public const int GoldColor = 0xd8a720;

        private static readonly ElectionPartyTagDefinition s_None = new ElectionPartyTagDefinition(
            None,
            string.Empty,
            string.Empty,
            0,
            ElectionCandidateTagTone.Neutral);

        private static readonly ElectionPartyTagDefinition[] s_Definitions =
        {
            s_None,
            new ElectionPartyTagDefinition(CivicTrust, "Civic Trust", "Corruption scandal chance -10 points and party scandal reputation loss is 5 points lighter.", 2, ElectionCandidateTagTone.Advantage),
            new ElectionPartyTagDefinition(ReformSlate, "Reform Slate", "+8% support from unhappy voters when the party is not the incumbent.", 2, ElectionCandidateTagTone.Advantage),
            new ElectionPartyTagDefinition(OrganizedMachine, "Organized Machine", "Campaign donations count 20% more.", 2, ElectionCandidateTagTone.Advantage),
            new ElectionPartyTagDefinition(TransitCoalition, "Transit Coalition", "+8% support from voters without cars, plus another +2% while Transit Vouchers are active.", 2, ElectionCandidateTagTone.Advantage),
            new ElectionPartyTagDefinition(CivilLiberties, "Civil Liberties", "+6% support from students and educated voters, and +8% from uneducated workers after strict voting ID passes.", 2, ElectionCandidateTagTone.Advantage),
            new ElectionPartyTagDefinition(LocalRoots, "Local Roots", "+5% support from low- and middle-income residents.", 1, ElectionCandidateTagTone.Advantage),
            new ElectionPartyTagDefinition(Pragmatic, "Pragmatic", "If elected, negative platform impacts are 15% softer.", 1, ElectionCandidateTagTone.Advantage),
            new ElectionPartyTagDefinition(StudentOutreach, "Student Outreach", "+5% support from teen and student voters.", 1, ElectionCandidateTagTone.Advantage),
            new ElectionPartyTagDefinition(JobsFocused, "Jobs Focused", "+5% support from workers.", 1, ElectionCandidateTagTone.Advantage),
            new ElectionPartyTagDefinition(BusinessFriendly, "Business Friendly", "+5% support from wealthy residents and workers.", 1, ElectionCandidateTagTone.Advantage),
            new ElectionPartyTagDefinition(Unproven, "Unproven", "-4% general support until the party wins its first election.", -1, ElectionCandidateTagTone.Disadvantage),
            new ElectionPartyTagDefinition(Ideological, "Ideological", "If elected, negative platform impacts are 10% stronger.", -1, ElectionCandidateTagTone.Disadvantage),
            new ElectionPartyTagDefinition(Divided, "Divided", "Campaign donations count 10% less and donation platform-softening chance is 10 points lower.", -1, ElectionCandidateTagTone.Disadvantage),
            new ElectionPartyTagDefinition(OldGuard, "Old Guard", "-5% support from teen and student voters.", -1, ElectionCandidateTagTone.Disadvantage),
            new ElectionPartyTagDefinition(Overconfident, "Overconfident", "If leading a released poll outside the margin of error, loses 4% election-day support.", -1, ElectionCandidateTagTone.Disadvantage),
            new ElectionPartyTagDefinition(Complacent, "Complacent", "If incumbent, loses 5% general support.", -2, ElectionCandidateTagTone.Disadvantage),
            new ElectionPartyTagDefinition(Elitist, "Elitist", "-8% support from low-income residents.", -2, ElectionCandidateTagTone.Disadvantage),
            new ElectionPartyTagDefinition(ScandalProne, "Scandal Prone", "Suspicious campaign actions add one extra corruption risk step and scandal reputation loss is 5 points harsher.", -2, ElectionCandidateTagTone.Disadvantage),
            new ElectionPartyTagDefinition(Disorganized, "Disorganized", "Campaign donations count 20% less and mayor platform-meeting success chance is 15 points lower.", -2, ElectionCandidateTagTone.Disadvantage),
            new ElectionPartyTagDefinition(OutOfTouch, "Out of Touch", "-8% support from unhappy voters.", -2, ElectionCandidateTagTone.Disadvantage)
        };

        public static ElectionPartyTagDefinition Get(int tagId)
        {
            tagId = NormalizeId(tagId);
            return tagId == None ? s_None : s_Definitions[tagId];
        }

        public static int NormalizeId(int tagId)
        {
            return tagId >= None && tagId <= Count ? tagId : None;
        }

        public static bool HasTag(int tagId)
        {
            return NormalizeId(tagId) != None;
        }

        public static string GetDefaultName(int partyIndex)
        {
            switch (partyIndex)
            {
                case 0:
                    return ElectionLocalization.Translate("Panel.Party.Default.0", "Purple Civic Alliance");
                case 1:
                    return ElectionLocalization.Translate("Panel.Party.Default.1", "Green Future Coalition");
                case 2:
                    return ElectionLocalization.Translate("Panel.Party.Default.2", "Pink Liberty Party");
                case 3:
                    return ElectionLocalization.Translate("Panel.Party.Default.3", "Gold Prosperity League");
                default:
                    return ElectionLocalization.Translate("Panel.Party.Fallback", "Party");
            }
        }

        public static int GetDefaultColor(int partyIndex)
        {
            switch (partyIndex)
            {
                case 0:
                    return PurpleColor;
                case 1:
                    return GreenColor;
                case 2:
                    return PinkColor;
                case 3:
                    return GoldColor;
                default:
                    return PurpleColor;
            }
        }

        public static int GetDefaultTagId(int partyIndex, int slotIndex)
        {
            switch (partyIndex)
            {
                case 0:
                    return slotIndex == 0 ? LocalRoots : slotIndex == 1 ? Pragmatic : Complacent;
                case 1:
                    return slotIndex == 0 ? ReformSlate : slotIndex == 1 ? Unproven : Ideological;
                case 2:
                    return slotIndex == 0 ? CivilLiberties : slotIndex == 1 ? Divided : Overconfident;
                case 3:
                    return slotIndex == 0 ? BusinessFriendly : slotIndex == 1 ? JobsFocused : Elitist;
                default:
                    return None;
            }
        }

        public static int ClampReputation(int reputation)
        {
            return math.clamp(reputation, MinReputation, MaxReputation);
        }

        public static int GetValue(int tagId)
        {
            return Get(tagId).Value;
        }

        public static bool HasPartyTag(ElectionState state, int partyIndex, int tagId)
        {
            tagId = NormalizeId(tagId);
            if (tagId == None || !ElectionState.IsCandidateIndex(partyIndex))
                return false;

            for (int i = 0; i < TagsPerParty; i++)
            {
                if (NormalizeId(state.GetPartyTagId(partyIndex, i)) == tagId)
                    return true;
            }

            return false;
        }

        public static int GetTagValueSum(ElectionState state, int partyIndex)
        {
            int sum = 0;
            for (int i = 0; i < TagsPerParty; i++)
                sum += GetValue(state.GetPartyTagId(partyIndex, i));
            return sum;
        }

        public static float GetReputationVoteBonus(int reputation)
        {
            return math.clamp((ClampReputation(reputation) - 50f) / 50f * 0.08f, -0.08f, 0.08f);
        }

        public static float GetVoteProbabilityBonus(
            int tagId,
            int voterAge,
            int voterEducation,
            int voterWealth,
            int voterHappiness,
            bool voterWorker,
            bool voterStudent,
            bool voterHasCar,
            bool strictVotingIdLawPassed,
            bool transitVoucherActive,
            bool isIncumbentParty,
            bool hasWonBefore,
            bool pollLeadOutsideMargin)
        {
            switch (NormalizeId(tagId))
            {
                case ReformSlate:
                    return !isIncumbentParty && voterHappiness < 45 ? 0.08f : 0f;
                case TransitCoalition:
                    return voterHasCar ? 0f : transitVoucherActive ? 0.10f : 0.08f;
                case CivilLiberties:
                    if (strictVotingIdLawPassed && voterWorker && voterEducation <= 1)
                        return 0.08f;
                    return voterStudent || voterEducation >= 3 ? 0.06f : 0f;
                case LocalRoots:
                    return voterWealth <= 2 ? 0.05f : 0f;
                case StudentOutreach:
                    return voterStudent || voterAge == (int)CitizenAge.Teen ? 0.05f : 0f;
                case JobsFocused:
                    return voterWorker ? 0.05f : 0f;
                case BusinessFriendly:
                    return voterWorker || voterWealth >= 3 ? 0.05f : 0f;
                case Unproven:
                    return hasWonBefore ? 0f : -0.04f;
                case Complacent:
                    return isIncumbentParty ? -0.05f : 0f;
                case OldGuard:
                    return voterStudent || voterAge == (int)CitizenAge.Teen ? -0.05f : 0f;
                case Overconfident:
                    return pollLeadOutsideMargin ? -0.04f : 0f;
                case Elitist:
                    return voterWealth <= 1 ? -0.08f : 0f;
                case OutOfTouch:
                    return voterHappiness < 45 ? -0.08f : 0f;
                default:
                    return 0f;
            }
        }

        public static int ApplyDonationCredit(ElectionState state, int partyIndex, int baseAmount)
        {
            float multiplier = 1f;
            if (HasPartyTag(state, partyIndex, OrganizedMachine))
                multiplier *= 1.2f;
            if (HasPartyTag(state, partyIndex, Divided))
                multiplier *= 0.9f;
            if (HasPartyTag(state, partyIndex, Disorganized))
                multiplier *= 0.8f;

            return ScaleMoney(baseAmount, multiplier);
        }

        public static float GetPositivePlatformScale(ElectionState state, int partyIndex)
        {
            return 1f;
        }

        public static float GetNegativePlatformScale(ElectionState state, int partyIndex)
        {
            float scale = 1f;
            if (HasPartyTag(state, partyIndex, Pragmatic))
                scale *= 0.85f;
            if (HasPartyTag(state, partyIndex, Ideological))
                scale *= 1.1f;
            return scale;
        }

        public static int GetSofteningChanceDelta(ElectionState state, int partyIndex)
        {
            return HasPartyTag(state, partyIndex, Divided) ? -10 : 0;
        }

        public static int GetBribeMeetingSuccessChanceDelta(ElectionState state, int partyIndex)
        {
            return HasPartyTag(state, partyIndex, Disorganized) ? -15 : 0;
        }

        public static int GetCorruptionRiskStepBonus(ElectionState state, int partyIndex)
        {
            return HasPartyTag(state, partyIndex, ScandalProne) ? 1 : 0;
        }

        public static int GetCorruptionChanceDelta(ElectionState state, int partyIndex)
        {
            return HasPartyTag(state, partyIndex, CivicTrust) ? -10 : 0;
        }

        public static int GetScandalReputationDelta(ElectionState state, int partyIndex)
        {
            int delta = -10;
            if (HasPartyTag(state, partyIndex, CivicTrust))
                delta += 5;
            if (HasPartyTag(state, partyIndex, ScandalProne))
                delta -= 5;
            return delta;
        }

        public static int PickReplacementTag(
            ElectionState state,
            int partyIndex,
            int slotIndex,
            ref Unity.Mathematics.Random random)
        {
            int currentTagId = NormalizeId(state.GetPartyTagId(partyIndex, slotIndex));
            int value = GetValue(currentTagId);
            int selected = currentTagId;
            int eligibleCount = 0;

            for (int tagId = 1; tagId <= Count; tagId++)
            {
                if (tagId == currentTagId ||
                    GetValue(tagId) != value ||
                    HasPartyTag(state, partyIndex, tagId))
                {
                    continue;
                }

                eligibleCount++;
                if (random.NextInt(eligibleCount) == 0)
                    selected = tagId;
            }

            return selected;
        }

        private static int ScaleMoney(int value, float multiplier)
        {
            long scaled = (long)math.round(math.max(1, value) * multiplier);
            if (scaled <= 0)
                return 1;

            return scaled > int.MaxValue ? int.MaxValue : (int)scaled;
        }
    }
}
