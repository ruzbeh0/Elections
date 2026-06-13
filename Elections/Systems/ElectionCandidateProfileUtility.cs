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
            string carText = HasCar(entityManager, candidate)
                ? ElectionLocalization.Translate("Model.Profile.Car.With", " with a registered car")
                : ElectionLocalization.Translate("Model.Profile.Car.Without", " without a registered car");
            return ElectionLocalization.Format("Model.Profile.Bio", "{0} resident from a {1}, {2}, {3}{4}.", ageText, wealthText, educationText, workText, carText);
        }

        public static string BuildChirpIntro(EntityManager entityManager, Entity candidate, int age, int education, int workType, int wealth)
        {
            string ageText = GetAgeLabel(age).ToLowerInvariant();
            string wealthText = GetWealthLabel(wealth).ToLowerInvariant();
            string workText = GetWorkLabel(workType).ToLowerInvariant();
            string article = StartsWithVowel(ageText)
                ? ElectionLocalization.Translate("Model.Profile.Article.An", "an")
                : ElectionLocalization.Translate("Model.Profile.Article.A", "a");
            return ElectionLocalization.Format("Model.Profile.ChirpIntro", "{0} {1} {2} from a {3}", article, ageText, workText, wealthText);
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
                return ElectionLocalization.Translate("Model.Profile.Age.Elderly", "Elderly");

            if (age == (int)CitizenAge.Adult)
                return ElectionLocalization.Translate("Model.Profile.Age.Adult", "Adult");

            return ElectionLocalization.Translate("Model.Profile.Age.Resident", "Resident");
        }

        private static string GetEducationLabel(int education)
        {
            switch (education)
            {
                case 0:
                    return ElectionLocalization.Translate("Model.Profile.Education.Uneducated", "Uneducated");
                case 1:
                    return ElectionLocalization.Translate("Model.Profile.Education.PoorlyEducated", "Poorly educated");
                case 2:
                    return ElectionLocalization.Translate("Model.Profile.Education.Educated", "Educated");
                case 3:
                    return ElectionLocalization.Translate("Model.Profile.Education.WellEducated", "Well educated");
                default:
                    return ElectionLocalization.Translate("Model.Profile.Education.HighlyEducated", "Highly educated");
            }
        }

        private static string GetWorkLabel(int workType)
        {
            if (workType >= 30)
                return ElectionLocalization.Translate("Model.Profile.Work.Student", "Student");

            if (workType >= 10)
                return ElectionLocalization.Translate("Model.Profile.Work.Working", "Working resident");

            return ElectionLocalization.Translate("Model.Profile.Work.NonWorking", "Non-working resident");
        }

        private static string GetWealthLabel(int wealth)
        {
            switch (wealth)
            {
                case 0:
                    return ElectionLocalization.Translate("Model.Profile.Wealth.Struggling", "Struggling household");
                case 1:
                    return ElectionLocalization.Translate("Model.Profile.Wealth.Modest", "Modest-income household");
                case 2:
                    return ElectionLocalization.Translate("Model.Profile.Wealth.Middle", "Middle-income household");
                case 3:
                    return ElectionLocalization.Translate("Model.Profile.Wealth.Comfortable", "Comfortable household");
                default:
                    return ElectionLocalization.Translate("Model.Profile.Wealth.Wealthy", "Wealthy household");
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
