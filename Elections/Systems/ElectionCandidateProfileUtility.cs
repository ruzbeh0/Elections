using Game.Citizens;
using Unity.Entities;

namespace Elections.Systems
{
    internal static class ElectionCandidateProfileUtility
    {
        public static string BuildBio(EntityManager entityManager, Entity candidate, int age, int education, int workType, int wealth)
        {
            string ageText = GetAgeLabel(age);
            string educationText = GetEducationLabel(education).ToLowerInvariant();
            string wealthText = GetWealthLabel(wealth).ToLowerInvariant();
            string workText = GetWorkLabel(workType).ToLowerInvariant();
            string carText = HasCar(entityManager, candidate) ? " with a registered car" : " without a registered car";
            return $"{ageText} resident from a {wealthText}, {educationText}, {workText}{carText}.";
        }

        public static string BuildChirpIntro(EntityManager entityManager, Entity candidate, int age, int education, int workType, int wealth)
        {
            string ageText = GetAgeLabel(age).ToLowerInvariant();
            string wealthText = GetWealthLabel(wealth).ToLowerInvariant();
            string workText = GetWorkLabel(workType).ToLowerInvariant();
            string article = StartsWithVowel(ageText) ? "an" : "a";
            return $"{article} {ageText} {workText} from a {wealthText}";
        }

        private static bool HasCar(EntityManager entityManager, Entity candidate)
        {
            return candidate != Entity.Null &&
                   entityManager.Exists(candidate) &&
                   entityManager.HasComponent<CarKeeper>(candidate);
        }

        private static string GetAgeLabel(int age)
        {
            if (age == (int)CitizenAge.Elderly)
                return "Elderly";

            if (age == (int)CitizenAge.Adult)
                return "Adult";

            return "Resident";
        }

        private static string GetEducationLabel(int education)
        {
            switch (education)
            {
                case 0:
                    return "Uneducated";
                case 1:
                    return "Poorly educated";
                case 2:
                    return "Educated";
                case 3:
                    return "Well educated";
                default:
                    return "Highly educated";
            }
        }

        private static string GetWorkLabel(int workType)
        {
            if (workType >= 30)
                return "Student";

            if (workType >= 10)
                return "Working resident";

            return "Non-working resident";
        }

        private static string GetWealthLabel(int wealth)
        {
            switch (wealth)
            {
                case 0:
                    return "Struggling household";
                case 1:
                    return "Modest-income household";
                case 2:
                    return "Middle-income household";
                case 3:
                    return "Comfortable household";
                default:
                    return "Wealthy household";
            }
        }

        private static bool StartsWithVowel(string value)
        {
            return !string.IsNullOrEmpty(value) &&
                   (value[0] == 'a' ||
                    value[0] == 'e' ||
                    value[0] == 'i' ||
                    value[0] == 'o' ||
                    value[0] == 'u');
        }
    }
}
