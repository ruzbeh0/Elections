using Game.Citizens;
using Unity.Mathematics;

namespace Elections.Models
{
    internal enum ElectionCandidateTagTone
    {
        Neutral = 0,
        Advantage = 1,
        Disadvantage = 2,
        Mixed = 3
    }

    internal readonly struct ElectionCandidateTagDefinition
    {
        public ElectionCandidateTagDefinition(int id, string name, string description, ElectionCandidateTagTone tone)
        {
            Id = id;
            Name = name;
            Description = description;
            Tone = tone;
        }

        public int Id { get; }
        public string Name { get; }
        public string Description { get; }
        public ElectionCandidateTagTone Tone { get; }
    }

    internal static class ElectionCandidateTags
    {
        public const int None = 0;
        public const int Corrupt = 1;
        public const int Honest = 2;
        public const int HumbleBeginnings = 3;
        public const int ControversialPast = 4;
        public const int Scientist = 5;
        public const int Frugal = 6;
        public const int Lavish = 7;
        public const int Grassroots = 8;
        public const int Fundraiser = 9;
        public const int PoorSpeaker = 10;
        public const int Charismatic = 11;
        public const int UnionOrganizer = 12;
        public const int StudentFavorite = 13;
        public const int ElderStatesperson = 14;
        public const int YoungReformer = 15;
        public const int Technocrat = 16;
        public const int Populist = 17;
        public const int EliteConnections = 18;
        public const int TransitAdvocate = 19;
        public const int MotoristAdvocate = 20;
        public const int LawAndOrder = 21;
        public const int Environmentalist = 22;
        public const int BusinessFriendly = 23;
        public const int NeighborhoodChampion = 24;
        public const int Polarizing = 25;

        public const int TagChancePercent = 75;
        public const int Count = 25;

        private static readonly ElectionCandidateTagDefinition s_None = new ElectionCandidateTagDefinition(
            None,
            string.Empty,
            string.Empty,
            ElectionCandidateTagTone.Neutral);

        private static readonly ElectionCandidateTagDefinition[] s_Definitions =
        {
            s_None,
            new ElectionCandidateTagDefinition(Corrupt, "Corrupt", "If elected, mayor campaign actions cost half as much.", ElectionCandidateTagTone.Mixed),
            new ElectionCandidateTagDefinition(Honest, "Honest", "If elected, mayor campaign actions cost twice as much.", ElectionCandidateTagTone.Disadvantage),
            new ElectionCandidateTagDefinition(HumbleBeginnings, "Humble beginnings", "+10% support from low-income residents.", ElectionCandidateTagTone.Advantage),
            new ElectionCandidateTagDefinition(ControversialPast, "Controversial past", "-10% overall voter support.", ElectionCandidateTagTone.Disadvantage),
            new ElectionCandidateTagDefinition(Scientist, "Scientist", "+10% support from highly educated residents.", ElectionCandidateTagTone.Advantage),
            new ElectionCandidateTagDefinition(Frugal, "Frugal", "Campaign donations cost half as much for the same effect.", ElectionCandidateTagTone.Advantage),
            new ElectionCandidateTagDefinition(Lavish, "Lavish", "Campaign donations cost twice as much for the same effect.", ElectionCandidateTagTone.Disadvantage),
            new ElectionCandidateTagDefinition(Grassroots, "Grassroots", "Campaign donations count 25% more.", ElectionCandidateTagTone.Advantage),
            new ElectionCandidateTagDefinition(Fundraiser, "Fundraiser", "Campaign donations count 15% more, but low-income residents are less supportive.", ElectionCandidateTagTone.Mixed),
            new ElectionCandidateTagDefinition(PoorSpeaker, "Poor speaker", "-5% overall voter support.", ElectionCandidateTagTone.Disadvantage),
            new ElectionCandidateTagDefinition(Charismatic, "Charismatic", "+5% overall voter support.", ElectionCandidateTagTone.Advantage),
            new ElectionCandidateTagDefinition(UnionOrganizer, "Union organizer", "+10% support from workers.", ElectionCandidateTagTone.Advantage),
            new ElectionCandidateTagDefinition(StudentFavorite, "Student favorite", "+10% support from students and teen voters.", ElectionCandidateTagTone.Advantage),
            new ElectionCandidateTagDefinition(ElderStatesperson, "Elder statesperson", "+10% support from elderly voters.", ElectionCandidateTagTone.Advantage),
            new ElectionCandidateTagDefinition(YoungReformer, "Young reformer", "+8% support from teens and adults, -4% from elderly voters.", ElectionCandidateTagTone.Mixed),
            new ElectionCandidateTagDefinition(Technocrat, "Technocrat", "+8% support from well educated residents, -5% from uneducated residents.", ElectionCandidateTagTone.Mixed),
            new ElectionCandidateTagDefinition(Populist, "Populist", "+8% support from low-income residents, -5% from wealthy residents.", ElectionCandidateTagTone.Mixed),
            new ElectionCandidateTagDefinition(EliteConnections, "Elite connections", "+8% support from wealthy residents, -5% from low-income residents.", ElectionCandidateTagTone.Mixed),
            new ElectionCandidateTagDefinition(TransitAdvocate, "Transit advocate", "+10% support from residents without cars.", ElectionCandidateTagTone.Advantage),
            new ElectionCandidateTagDefinition(MotoristAdvocate, "Motorist advocate", "+10% support from residents with cars.", ElectionCandidateTagTone.Advantage),
            new ElectionCandidateTagDefinition(LawAndOrder, "Law and order", "+8% support from elderly and unhappy residents.", ElectionCandidateTagTone.Advantage),
            new ElectionCandidateTagDefinition(Environmentalist, "Environmentalist", "+8% support from students and well educated residents.", ElectionCandidateTagTone.Advantage),
            new ElectionCandidateTagDefinition(BusinessFriendly, "Business friendly", "+8% support from workers and wealthy residents.", ElectionCandidateTagTone.Advantage),
            new ElectionCandidateTagDefinition(NeighborhoodChampion, "Neighborhood champion", "+6% support from low- and middle-income residents.", ElectionCandidateTagTone.Advantage),
            new ElectionCandidateTagDefinition(Polarizing, "Polarizing", "Election turnout increases by 15%.", ElectionCandidateTagTone.Mixed)
        };

        public static ElectionCandidateTagDefinition Get(int tagId)
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

        public static int PickRandomId(
            ref Unity.Mathematics.Random random,
            int candidateAge,
            int candidateEducation,
            int excludedTagId)
        {
            if (random.NextInt(100) >= TagChancePercent)
                return None;

            excludedTagId = NormalizeId(excludedTagId);
            int selected = None;
            int eligibleCount = 0;
            for (int tagId = 1; tagId <= Count; tagId++)
            {
                if (tagId == excludedTagId ||
                    !CanAssign(tagId, candidateAge, candidateEducation))
                {
                    continue;
                }

                eligibleCount++;
                if (random.NextInt(eligibleCount) == 0)
                    selected = tagId;
            }

            return selected;
        }

        public static bool CanAssign(int tagId, int candidateAge, int candidateEducation)
        {
            switch (NormalizeId(tagId))
            {
                case Scientist:
                    return candidateEducation >= 4;
                case ElderStatesperson:
                    return candidateAge == (int)CitizenAge.Elderly;
                case YoungReformer:
                    return candidateAge == (int)CitizenAge.Adult;
                default:
                    return true;
            }
        }

        public static float GetVoteProbabilityBonus(
            int tagId,
            int voterAge,
            int voterEducation,
            int voterWealth,
            int voterHappiness,
            bool voterWorker,
            bool voterStudent,
            bool voterHasCar)
        {
            switch (NormalizeId(tagId))
            {
                case HumbleBeginnings:
                    return voterWealth <= 1 ? 0.10f : 0f;
                case ControversialPast:
                    return -0.10f;
                case Scientist:
                    return voterEducation >= 4 ? 0.10f : 0f;
                case Fundraiser:
                    return voterWealth <= 1 ? -0.05f : 0f;
                case PoorSpeaker:
                    return -0.05f;
                case Charismatic:
                    return 0.05f;
                case UnionOrganizer:
                    return voterWorker ? 0.10f : 0f;
                case StudentFavorite:
                    return voterStudent || voterAge == (int)CitizenAge.Teen ? 0.10f : 0f;
                case ElderStatesperson:
                    return voterAge == (int)CitizenAge.Elderly ? 0.10f : 0f;
                case YoungReformer:
                    return voterAge == (int)CitizenAge.Elderly ? -0.04f : 0.08f;
                case Technocrat:
                    if (voterEducation >= 3)
                        return 0.08f;
                    return voterEducation <= 0 ? -0.05f : 0f;
                case Populist:
                    if (voterWealth <= 1)
                        return 0.08f;
                    return voterWealth >= 3 ? -0.05f : 0f;
                case EliteConnections:
                    if (voterWealth >= 3)
                        return 0.08f;
                    return voterWealth <= 1 ? -0.05f : 0f;
                case TransitAdvocate:
                    return voterHasCar ? 0f : 0.10f;
                case MotoristAdvocate:
                    return voterHasCar ? 0.10f : 0f;
                case LawAndOrder:
                    return voterAge == (int)CitizenAge.Elderly || voterHappiness < 45 ? 0.08f : 0f;
                case Environmentalist:
                    return voterStudent || voterEducation >= 3 ? 0.08f : 0f;
                case BusinessFriendly:
                    return voterWorker || voterWealth >= 3 ? 0.08f : 0f;
                case NeighborhoodChampion:
                    return voterWealth <= 2 ? 0.06f : 0f;
                default:
                    return 0f;
            }
        }

        public static int GetDonationCost(int tagId, int baseAmount)
        {
            switch (NormalizeId(tagId))
            {
                case Frugal:
                    return ScaleMoney(baseAmount, 0.5f);
                case Lavish:
                    return ScaleMoney(baseAmount, 2f);
                default:
                    return math.max(1, baseAmount);
            }
        }

        public static int GetDonationCredit(int tagId, int baseAmount)
        {
            switch (NormalizeId(tagId))
            {
                case Grassroots:
                    return ScaleMoney(baseAmount, 1.25f);
                case Fundraiser:
                    return ScaleMoney(baseAmount, 1.15f);
                default:
                    return math.max(1, baseAmount);
            }
        }

        public static int GetMayorActionCost(int mayorTagId, int baseAmount)
        {
            switch (NormalizeId(mayorTagId))
            {
                case Corrupt:
                    return ScaleMoney(baseAmount, 0.5f);
                case Honest:
                    return ScaleMoney(baseAmount, 2f);
                default:
                    return math.max(1, baseAmount);
            }
        }

        public static int ApplyTurnoutModifier(int dailyTurnoutPercent, int candidateATagId, int candidateBTagId)
        {
            if (NormalizeId(candidateATagId) == Polarizing ||
                NormalizeId(candidateBTagId) == Polarizing)
            {
                return math.clamp(ScaleMoney(dailyTurnoutPercent, 1.15f), 1, 100);
            }

            return math.clamp(dailyTurnoutPercent, 1, 100);
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
