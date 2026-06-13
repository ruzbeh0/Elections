using Elections.Components;
using System;
using Unity.Mathematics;

namespace Elections.Models
{
    public enum ElectionLegislationType
    {
        VoterIdentification = 0,
        PropertyOwnerBallotNotification = 1,
        YouthCivicRegistration = 2,
        NeighborhoodAccessVoting = 3,
        ContinuityOfGovernance = 4
    }

    internal readonly struct ElectionLegislationDefinition
    {
        private readonly string m_Title;
        private readonly string m_Description;
        private readonly string m_Tooltip;

        public ElectionLegislationDefinition(ElectionLegislationType type, string title, string description, string tooltip)
        {
            Type = type;
            m_Title = title;
            m_Description = description;
            m_Tooltip = tooltip;
        }

        public ElectionLegislationType Type { get; }
        public int Index => (int)Type;
        public string Title => ElectionLocalization.Translate($"Model.Legislation.{Index}.Title", m_Title);
        public string Description => FormatDescription();
        public string Tooltip => ElectionLocalization.Translate($"Model.Legislation.{Index}.Tooltip", m_Tooltip);

        private string FormatDescription()
        {
            switch (Type)
            {
                case ElectionLegislationType.VoterIdentification:
                    return ElectionLocalization.Format($"Model.Legislation.{Index}.Description", m_Description, ElectionLegislation.VoterIdentificationUneducatedWorkerDelta);
                case ElectionLegislationType.PropertyOwnerBallotNotification:
                    return ElectionLocalization.Format($"Model.Legislation.{Index}.Description", m_Description, ElectionLegislation.PropertyOwnerWealthyDelta, ElectionLegislation.PropertyOwnerCarDelta, ElectionLegislation.PropertyOwnerLowIncomeDelta);
                case ElectionLegislationType.YouthCivicRegistration:
                    return ElectionLocalization.Format($"Model.Legislation.{Index}.Description", m_Description, ElectionLegislation.YouthTeenDelta, ElectionLegislation.YouthStudentDelta, ElectionLegislation.YouthElderlyDelta);
                case ElectionLegislationType.NeighborhoodAccessVoting:
                    return ElectionLocalization.Format($"Model.Legislation.{Index}.Description", m_Description, ElectionLegislation.NeighborhoodLowIncomeDelta, ElectionLegislation.NeighborhoodNoCarDelta, ElectionLegislation.NeighborhoodWealthyDelta);
                case ElectionLegislationType.ContinuityOfGovernance:
                    return ElectionLocalization.Format($"Model.Legislation.{Index}.Description", m_Description, ElectionLegislation.ContinuityHappyDelta, ElectionLegislation.ContinuityElderlyDelta, ElectionLegislation.ContinuityUnhappyDelta);
                default:
                    return ElectionLocalization.Translate($"Model.Legislation.{Index}.Description", m_Description);
            }
        }
    }

    internal static class ElectionLegislation
    {
        public const int Count = 5;
        public const int VoterIdentificationUneducatedWorkerDelta = -30;
        public const int PropertyOwnerWealthyDelta = 16;
        public const int PropertyOwnerCarDelta = 8;
        public const int PropertyOwnerLowIncomeDelta = -10;
        public const int YouthTeenDelta = 20;
        public const int YouthStudentDelta = 12;
        public const int YouthElderlyDelta = -8;
        public const int NeighborhoodLowIncomeDelta = 16;
        public const int NeighborhoodNoCarDelta = 10;
        public const int NeighborhoodWealthyDelta = -10;
        public const int ContinuityHappyDelta = 15;
        public const int ContinuityElderlyDelta = 8;
        public const int ContinuityUnhappyDelta = -12;

        public static bool TryGet(int index, out ElectionLegislationDefinition definition)
        {
            switch ((ElectionLegislationType)index)
            {
                case ElectionLegislationType.VoterIdentification:
                    definition = new ElectionLegislationDefinition(
                        ElectionLegislationType.VoterIdentification,
                        "Voter Identification Ordinance",
                        $"{VoterIdentificationUneducatedWorkerDelta}% turnout for uneducated workers.",
                        "Requires additional voter identification checks. This reduces turnout among uneducated workers.");
                    return true;
                case ElectionLegislationType.PropertyOwnerBallotNotification:
                    definition = new ElectionLegislationDefinition(
                        ElectionLegislationType.PropertyOwnerBallotNotification,
                        "Property Owner Ballot Notification Act",
                        $"+{PropertyOwnerWealthyDelta}% wealthy turnout, +{PropertyOwnerCarDelta}% car-owner turnout, {PropertyOwnerLowIncomeDelta}% low-income turnout.",
                        "Prioritizes mailed ballot notices through property and vehicle records. This raises turnout for wealthy residents and car owners, but can depress low-income turnout.");
                    return true;
                case ElectionLegislationType.YouthCivicRegistration:
                    definition = new ElectionLegislationDefinition(
                        ElectionLegislationType.YouthCivicRegistration,
                        "Youth Civic Registration Act",
                        $"+{YouthTeenDelta}% teen turnout, +{YouthStudentDelta}% student turnout, {YouthElderlyDelta}% elderly turnout.",
                        "Creates automatic civic registration for schools and youth services. This raises teen and student turnout, but slightly lowers elderly turnout.");
                    return true;
                case ElectionLegislationType.NeighborhoodAccessVoting:
                    definition = new ElectionLegislationDefinition(
                        ElectionLegislationType.NeighborhoodAccessVoting,
                        "Neighborhood Access Voting Act",
                        $"+{NeighborhoodLowIncomeDelta}% low-income turnout, +{NeighborhoodNoCarDelta}% no-car turnout, {NeighborhoodWealthyDelta}% wealthy turnout.",
                        "Prioritizes neighborhood access rules and local voting assistance. This raises low-income and no-car turnout, but can lower wealthy turnout.");
                    return true;
                case ElectionLegislationType.ContinuityOfGovernance:
                    definition = new ElectionLegislationDefinition(
                        ElectionLegislationType.ContinuityOfGovernance,
                        "Continuity of Governance Act",
                        $"+{ContinuityHappyDelta}% happy-resident turnout, +{ContinuityElderlyDelta}% elderly turnout, {ContinuityUnhappyDelta}% unhappy-resident turnout while the incumbent party is running.",
                        "Promotes stable government transition procedures. While the incumbent party is running, happy and elderly residents turn out more, while unhappy residents turn out less.");
                    return true;
                default:
                    definition = default;
                    return false;
            }
        }

        public static int GetCost(int campaignActionCost)
        {
            return Math.Max(1, campaignActionCost / 2);
        }

        public static int GetActionChancePercent(ElectionState state)
        {
            if (!ElectionState.IsPartyIndex(state.mayorPartyIndex))
                return ElectionPartyTags.DefaultReputation;

            int reputation = state.GetPartyReputation(state.mayorPartyIndex);
            return math.clamp(reputation <= 0 ? ElectionPartyTags.DefaultReputation : reputation, 0, 100);
        }

        public static int GetFlag(ElectionLegislationType type)
        {
            return 1 << (int)type;
        }

        public static int NormalizeFlags(int flags)
        {
            int mask = 0;
            for (int i = 0; i < Count; i++)
                mask |= 1 << i;
            return flags & mask;
        }

        public static bool IsActive(int flags, ElectionLegislationType type)
        {
            return (NormalizeFlags(flags) & GetFlag(type)) != 0;
        }

        public static int SetActive(int flags, ElectionLegislationType type, bool active)
        {
            int flag = GetFlag(type);
            flags = NormalizeFlags(flags);
            return active ? flags | flag : flags & ~flag;
        }
    }
}
