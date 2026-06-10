using Game.City;
using System;
using System.Globalization;

namespace Elections.Models
{
    internal enum ElectionEffectImpactKind
    {
        CityModifier,
        Money,
        RealisticTripsResourceConsumption,
        AccumulatedXp
    }

    internal struct ElectionEffectImpact
    {
        public string Key;
        public string Label;
        public string ValueText;
        public string Sentence;
        public bool Positive;
        public int MoneyDelta;
        public CityModifierType ModifierType;
        public float Add;
        public float Mul;
        public float ResourceConsumptionMultiplier;
        public float AccumulatedXpMultiplier;
    }

    internal struct ElectionEffectDefinition
    {
        public int Id;
        public string Name;
        public string Description;
        public string PlatformKey;
        public ElectionEffectImpact PositiveImpact;
        public ElectionEffectImpact NegativeImpact;
        public int MoneyDelta;
        public CityModifierType ModifierType1;
        public float Add1;
        public float Mul1;
        public CityModifierType ModifierType2;
        public float Add2;
        public float Mul2;
        public float ResourceConsumptionMultiplier;
        public float AccumulatedXpMultiplier;
    }

    internal static class ElectionEffects
    {
        private const int kGeneratedEffectBase = 1000;
        private static readonly int[] kMildNormal = { 3, 5, 8, 10 };
        private static readonly int[] kEducation = { 3, 5, 8 };
        private static readonly int[] kResourceConsumption = { 5, 8, 10 };
        private static readonly int[] kMoneyGain = { 100000, 150000, 250000, 350000 };
        private static readonly int[] kMoneyLoss = { 50000, 100000, 150000, 200000 };

        private struct PlatformOption
        {
            public string Key;
            public string Label;
            public string SentenceTarget;
            public ElectionEffectImpactKind Kind;
            public CityModifierType ModifierType;
            public int Direction;
            public string Verb;
            public int[] Amounts;
        }

        private static readonly PlatformOption[] kPositiveOptions =
        {
            Money("Money", "City funds", "city funds", 1, "adds", kMoneyGain),
            Modifier("ImportCost", "Import costs", "import costs", CityModifierType.ImportCost, -1, "lowers", kMildNormal),
            Modifier("ExportCost", "Export costs", "export costs", CityModifierType.ExportCost, -1, "lowers", kMildNormal),
            Modifier("LoanInterest", "Loan interest", "loan interest", CityModifierType.LoanInterest, -1, "lowers", kMildNormal),
            Modifier("BuildingLevelingCost", "Building leveling cost", "building leveling costs", CityModifierType.BuildingLevelingCost, -1, "lowers", kMildNormal),
            Modifier("TaxiStartingFee", "Taxi starting fee", "taxi starting fees", CityModifierType.TaxiStartingFee, -1, "lowers", kMildNormal),
            Modifier("CityServiceUpkeep", "City service upkeep", "city service upkeep", CityModifierType.CityServiceBuildingBaseUpkeepCost, -1, "lowers", kMildNormal),
            Modifier("CrimeProbability", "Crime probability", "crime probability", CityModifierType.CrimeProbability, -1, "lowers", kMildNormal),
            Modifier("CrimeAccumulation", "Crime accumulation", "crime accumulation", CityModifierType.CrimeAccumulation, -1, "lowers", kMildNormal),
            Modifier("DiseaseProbability", "Disease probability", "disease probability", CityModifierType.DiseaseProbability, -1, "lowers", kMildNormal),
            Modifier("HospitalEfficiency", "Hospital efficiency", "hospital efficiency", CityModifierType.HospitalEfficiency, 1, "raises", kMildNormal),
            Modifier("PollutionHealthAffect", "Pollution health impact", "pollution health impact", CityModifierType.PollutionHealthAffect, -1, "lowers", kMildNormal),
            Modifier("IndustrialAirPollution", "Industrial air pollution", "industrial air pollution", CityModifierType.IndustrialAirPollution, -1, "lowers", kMildNormal),
            Modifier("IndustrialGroundPollution", "Industrial ground pollution", "industrial ground pollution", CityModifierType.IndustrialGroundPollution, -1, "lowers", kMildNormal),
            Modifier("IndustrialGarbage", "Industrial garbage", "industrial garbage", CityModifierType.IndustrialGarbage, -1, "lowers", kMildNormal),
            Modifier("IndustrialEfficiency", "Industrial efficiency", "industrial efficiency", CityModifierType.IndustrialEfficiency, 1, "raises", kMildNormal),
            Modifier("OfficeEfficiency", "Office efficiency", "office efficiency", CityModifierType.OfficeEfficiency, 1, "raises", kMildNormal),
            Modifier("IndustrialElectronicsEfficiency", "Electronics efficiency", "industrial electronics efficiency", CityModifierType.IndustrialElectronicsEfficiency, 1, "raises", kMildNormal),
            Modifier("OfficeSoftwareEfficiency", "Software efficiency", "office software efficiency", CityModifierType.OfficeSoftwareEfficiency, 1, "raises", kMildNormal),
            Modifier("IndustrialElectronicsDemand", "Electronics demand", "industrial electronics demand", CityModifierType.IndustrialElectronicsDemand, 1, "raises", kMildNormal),
            Modifier("OfficeSoftwareDemand", "Software demand", "office software demand", CityModifierType.OfficeSoftwareDemand, 1, "raises", kMildNormal),
            Modifier("Attractiveness", "City attractiveness", "city attractiveness", CityModifierType.Attractiveness, 1, "raises", kMildNormal),
            Modifier("Entertainment", "Entertainment", "entertainment", CityModifierType.Entertainment, 1, "raises", kMildNormal),
            Modifier("ParkEntertainment", "Park entertainment", "park entertainment", CityModifierType.ParkEntertainment, 1, "raises", kMildNormal),
            Modifier("CollegeGraduation", "College graduation", "college graduation", CityModifierType.CollegeGraduation, 1, "raises", kEducation),
            Modifier("UniversityGraduation", "University graduation", "university graduation", CityModifierType.UniversityGraduation, 1, "raises", kEducation),
            Modifier("UniversityInterest", "University interest", "university interest", CityModifierType.UniversityInterest, 1, "raises", kMildNormal),
            AccumulatedXp("AccumulatedXP", "Accumulated XP", "accumulated XP", 1, "doubles", new[] { 100 }),
            ResourceConsumption("ResourceConsumption", "Citizen resource consumption", "citizen resource consumption", -1, "reduces", kResourceConsumption)
        };

        private static readonly PlatformOption[] kNegativeOptions =
        {
            Money("Money", "City funds", "city funds", -1, "removes", kMoneyLoss),
            Modifier("ImportCost", "Import costs", "import costs", CityModifierType.ImportCost, 1, "raises", kMildNormal),
            Modifier("ExportCost", "Export costs", "export costs", CityModifierType.ExportCost, 1, "raises", kMildNormal),
            Modifier("LoanInterest", "Loan interest", "loan interest", CityModifierType.LoanInterest, 1, "raises", kMildNormal),
            Modifier("BuildingLevelingCost", "Building leveling cost", "building leveling costs", CityModifierType.BuildingLevelingCost, 1, "raises", kMildNormal),
            Modifier("TaxiStartingFee", "Taxi starting fee", "taxi starting fees", CityModifierType.TaxiStartingFee, 1, "raises", kMildNormal),
            Modifier("CityServiceUpkeep", "City service upkeep", "city service upkeep", CityModifierType.CityServiceBuildingBaseUpkeepCost, 1, "raises", kMildNormal),
            Modifier("CrimeProbability", "Crime probability", "crime probability", CityModifierType.CrimeProbability, 1, "raises", kMildNormal),
            Modifier("CrimeAccumulation", "Crime accumulation", "crime accumulation", CityModifierType.CrimeAccumulation, 1, "raises", kMildNormal),
            Modifier("DiseaseProbability", "Disease probability", "disease probability", CityModifierType.DiseaseProbability, 1, "raises", kMildNormal),
            Modifier("HospitalEfficiency", "Hospital efficiency", "hospital efficiency", CityModifierType.HospitalEfficiency, -1, "lowers", kMildNormal),
            Modifier("PollutionHealthAffect", "Pollution health impact", "pollution health impact", CityModifierType.PollutionHealthAffect, 1, "raises", kMildNormal),
            Modifier("IndustrialAirPollution", "Industrial air pollution", "industrial air pollution", CityModifierType.IndustrialAirPollution, 1, "raises", kMildNormal),
            Modifier("IndustrialGroundPollution", "Industrial ground pollution", "industrial ground pollution", CityModifierType.IndustrialGroundPollution, 1, "raises", kMildNormal),
            Modifier("IndustrialGarbage", "Industrial garbage", "industrial garbage", CityModifierType.IndustrialGarbage, 1, "raises", kMildNormal),
            Modifier("IndustrialEfficiency", "Industrial efficiency", "industrial efficiency", CityModifierType.IndustrialEfficiency, -1, "lowers", kMildNormal),
            Modifier("OfficeEfficiency", "Office efficiency", "office efficiency", CityModifierType.OfficeEfficiency, -1, "lowers", kMildNormal),
            Modifier("IndustrialElectronicsEfficiency", "Electronics efficiency", "industrial electronics efficiency", CityModifierType.IndustrialElectronicsEfficiency, -1, "lowers", kMildNormal),
            Modifier("OfficeSoftwareEfficiency", "Software efficiency", "office software efficiency", CityModifierType.OfficeSoftwareEfficiency, -1, "lowers", kMildNormal),
            Modifier("IndustrialElectronicsDemand", "Electronics demand", "industrial electronics demand", CityModifierType.IndustrialElectronicsDemand, -1, "lowers", kMildNormal),
            Modifier("OfficeSoftwareDemand", "Software demand", "office software demand", CityModifierType.OfficeSoftwareDemand, -1, "lowers", kMildNormal),
            Modifier("Attractiveness", "City attractiveness", "city attractiveness", CityModifierType.Attractiveness, -1, "lowers", kMildNormal),
            Modifier("Entertainment", "Entertainment", "entertainment", CityModifierType.Entertainment, -1, "lowers", kMildNormal),
            Modifier("ParkEntertainment", "Park entertainment", "park entertainment", CityModifierType.ParkEntertainment, -1, "lowers", kMildNormal),
            Modifier("CollegeGraduation", "College graduation", "college graduation", CityModifierType.CollegeGraduation, -1, "lowers", kEducation),
            Modifier("UniversityGraduation", "University graduation", "university graduation", CityModifierType.UniversityGraduation, -1, "lowers", kEducation),
            Modifier("UniversityInterest", "University interest", "university interest", CityModifierType.UniversityInterest, -1, "lowers", kMildNormal),
            AccumulatedXp("AccumulatedXP", "Accumulated XP", "accumulated XP", -1, "cuts", new[] { 50 }),
            ResourceConsumption("ResourceConsumption", "Citizen resource consumption", "citizen resource consumption", 1, "increases", kResourceConsumption)
        };

        public static int CreateRandomId(int seed)
        {
            uint mixed = Mix((uint)Math.Max(1, seed));
            return kGeneratedEffectBase + (int)(mixed & 0x3fffffff);
        }

        public static bool IsGeneratedId(int id)
        {
            return id >= kGeneratedEffectBase;
        }

        public static ElectionEffectDefinition Get(int id)
        {
            return Get(id, 1f, 1f);
        }

        public static ElectionEffectDefinition Get(int id, bool negativeSoftened)
        {
            return Get(id, 1f, negativeSoftened ? 0.5f : 1f);
        }

        public static ElectionEffectDefinition Get(int id, bool negativeSoftened, int candidateTagId)
        {
            float platformScale = ElectionCandidateTags.GetPlatformEffectScale(candidateTagId);
            return Get(id, platformScale, (negativeSoftened ? 0.5f : 1f) * platformScale);
        }

        public static ElectionEffectDefinition Get(int id, float negativeScale)
        {
            return Get(id, 1f, negativeScale);
        }

        public static ElectionEffectDefinition Get(int id, float positiveScale, float negativeScale)
        {
            if (id == 0)
                return CreateTransitionEffect();

            uint seed = Mix((uint)Math.Max(1, id));
            float positiveAmountScale = Math.Max(0f, positiveScale);
            float negativeAmountScale = Math.Max(0f, negativeScale);
            PlatformOption positiveOption = kPositiveOptions[Pick(ref seed, kPositiveOptions.Length)];
            PlatformOption negativeOption = kNegativeOptions[PickDifferentKey(ref seed, kNegativeOptions, positiveOption.Key)];
            ElectionEffectImpact positive = BuildImpact(ref seed, positiveOption, true, positiveAmountScale);
            ElectionEffectImpact negative = BuildImpact(ref seed, negativeOption, false, negativeAmountScale);

            return BuildDefinition(id, positive, negative);
        }

        public static bool HasSamePlatform(int firstId, int secondId)
        {
            if (firstId == secondId)
                return true;

            return Get(firstId).PlatformKey == Get(secondId).PlatformKey;
        }

        private static ElectionEffectDefinition BuildDefinition(int id, ElectionEffectImpact positive, ElectionEffectImpact negative)
        {
            ElectionEffectDefinition definition = new ElectionEffectDefinition
            {
                Id = id,
                Name = "Mayoral Agenda",
                Description = $"{positive.Sentence}, but {negative.Sentence}",
                PlatformKey = $"{positive.Key}:{positive.ValueText}|{negative.Key}:{negative.ValueText}",
                PositiveImpact = positive,
                NegativeImpact = negative,
                MoneyDelta = positive.MoneyDelta + negative.MoneyDelta,
                ModifierType1 = (CityModifierType)(-1),
                ModifierType2 = (CityModifierType)(-1),
                ResourceConsumptionMultiplier = positive.ResourceConsumptionMultiplier * negative.ResourceConsumptionMultiplier,
                AccumulatedXpMultiplier = positive.AccumulatedXpMultiplier * negative.AccumulatedXpMultiplier
            };

            AddModifierImpact(ref definition, positive);
            AddModifierImpact(ref definition, negative);
            return definition;
        }

        private static void AddModifierImpact(ref ElectionEffectDefinition definition, ElectionEffectImpact impact)
        {
            if ((int)impact.ModifierType < 0 || (impact.Add == 0f && impact.Mul == 0f))
                return;

            if ((int)definition.ModifierType1 < 0)
            {
                definition.ModifierType1 = impact.ModifierType;
                definition.Add1 = impact.Add;
                definition.Mul1 = impact.Mul;
                return;
            }

            definition.ModifierType2 = impact.ModifierType;
            definition.Add2 = impact.Add;
            definition.Mul2 = impact.Mul;
        }

        private static ElectionEffectImpact BuildImpact(ref uint seed, PlatformOption option, bool positive, float amountScale)
        {
            int amount = option.Amounts[Pick(ref seed, option.Amounts.Length)];
            float effectiveAmount = amount * Math.Max(0f, amountScale);
            ElectionEffectImpact impact = new ElectionEffectImpact
            {
                Key = option.Key,
                Label = option.Label,
                Positive = positive,
                ModifierType = (CityModifierType)(-1),
                Add = 0f,
                ResourceConsumptionMultiplier = 1f,
                AccumulatedXpMultiplier = 1f
            };

            if (option.Kind == ElectionEffectImpactKind.Money)
            {
                int effectiveMoneyAmount = (int)Math.Round(effectiveAmount);
                int signedMoneyAmount = effectiveMoneyAmount * option.Direction;
                impact.MoneyDelta = signedMoneyAmount;
                impact.ValueText = FormatSignedMoney(signedMoneyAmount);
                impact.Sentence = option.Direction > 0
                    ? $"{option.Verb} {effectiveMoneyAmount:n0} to {option.SentenceTarget}"
                    : $"{option.Verb} {effectiveMoneyAmount:n0} from {option.SentenceTarget}";
                return impact;
            }

            float signedPercent = effectiveAmount * option.Direction;
            float delta = signedPercent / 100f;

            if (option.Kind == ElectionEffectImpactKind.AccumulatedXp)
            {
                impact.AccumulatedXpMultiplier = Math.Max(0f, 1f + delta);
                impact.ValueText = FormatMultiplier(impact.AccumulatedXpMultiplier);
                if (option.Direction > 0 && Math.Abs(effectiveAmount - 100f) < 0.05f)
                    impact.Sentence = $"doubles {option.SentenceTarget}";
                else if (option.Direction < 0 && Math.Abs(effectiveAmount - 50f) < 0.05f)
                    impact.Sentence = $"cuts {option.SentenceTarget} in half";
                else
                    impact.Sentence = $"{(option.Direction > 0 ? "raises" : "lowers")} {option.SentenceTarget} by {FormatUnsignedPercent(effectiveAmount)}";
                return impact;
            }

            impact.ValueText = FormatSignedPercent(signedPercent);
            impact.Sentence = $"{option.Verb} {option.SentenceTarget} by {FormatUnsignedPercent(effectiveAmount)}";

            if (option.Kind == ElectionEffectImpactKind.RealisticTripsResourceConsumption)
            {
                impact.ResourceConsumptionMultiplier = Math.Max(0.01f, 1f + delta);
                return impact;
            }

            impact.ModifierType = option.ModifierType;
            impact.Mul = delta;
            return impact;
        }

        private static ElectionEffectDefinition CreateTransitionEffect()
        {
            ElectionEffectImpact neutral = new ElectionEffectImpact
            {
                Key = "None",
                Label = "No platform",
                ValueText = string.Empty,
                Sentence = "keeps city policy neutral",
                ModifierType = (CityModifierType)(-1),
                ResourceConsumptionMultiplier = 1f,
                AccumulatedXpMultiplier = 1f
            };

            return new ElectionEffectDefinition
            {
                Id = 0,
                Name = "Democratic Transition",
                Description = "keeps city policy neutral while supervising the election process until residents elect a mayor",
                PlatformKey = "None",
                PositiveImpact = neutral,
                NegativeImpact = neutral,
                ModifierType1 = (CityModifierType)(-1),
                ModifierType2 = (CityModifierType)(-1),
                ResourceConsumptionMultiplier = 1f
            };
        }

        private static PlatformOption Modifier(string key, string label, string sentenceTarget, CityModifierType modifierType, int direction, string verb, int[] amounts)
        {
            return new PlatformOption
            {
                Key = key,
                Label = label,
                SentenceTarget = sentenceTarget,
                Kind = ElectionEffectImpactKind.CityModifier,
                ModifierType = modifierType,
                Direction = direction,
                Verb = verb,
                Amounts = amounts
            };
        }

        private static PlatformOption Money(string key, string label, string sentenceTarget, int direction, string verb, int[] amounts)
        {
            return new PlatformOption
            {
                Key = key,
                Label = label,
                SentenceTarget = sentenceTarget,
                Kind = ElectionEffectImpactKind.Money,
                ModifierType = (CityModifierType)(-1),
                Direction = direction,
                Verb = verb,
                Amounts = amounts
            };
        }

        private static PlatformOption ResourceConsumption(string key, string label, string sentenceTarget, int direction, string verb, int[] amounts)
        {
            return new PlatformOption
            {
                Key = key,
                Label = label,
                SentenceTarget = sentenceTarget,
                Kind = ElectionEffectImpactKind.RealisticTripsResourceConsumption,
                ModifierType = (CityModifierType)(-1),
                Direction = direction,
                Verb = verb,
                Amounts = amounts
            };
        }

        private static PlatformOption AccumulatedXp(string key, string label, string sentenceTarget, int direction, string verb, int[] amounts)
        {
            return new PlatformOption
            {
                Key = key,
                Label = label,
                SentenceTarget = sentenceTarget,
                Kind = ElectionEffectImpactKind.AccumulatedXp,
                ModifierType = (CityModifierType)(-1),
                Direction = direction,
                Verb = verb,
                Amounts = amounts
            };
        }

        private static int PickDifferentKey(ref uint seed, PlatformOption[] options, string excludedKey)
        {
            int index = Pick(ref seed, options.Length);
            if (options[index].Key != excludedKey)
                return index;

            int offset = Pick(ref seed, options.Length - 1) + 1;
            for (int i = 0; i < options.Length; i++)
            {
                int candidate = (index + offset + i) % options.Length;
                if (options[candidate].Key != excludedKey)
                    return candidate;
            }

            return index;
        }

        private static int Pick(ref uint seed, int count)
        {
            seed = Mix(seed + 0x9e3779b9u);
            return (int)(seed % (uint)Math.Max(1, count));
        }

        private static uint Mix(uint value)
        {
            value ^= value >> 16;
            value *= 0x7feb352du;
            value ^= value >> 15;
            value *= 0x846ca68bu;
            value ^= value >> 16;
            return value;
        }

        private static string FormatSignedPercent(float value)
        {
            if (value > 0f)
                return $"+{FormatPercentMagnitude(value)}%";

            if (value < 0f)
                return $"-{FormatPercentMagnitude(-value)}%";

            return "0%";
        }

        private static string FormatUnsignedPercent(float value)
        {
            return $"{FormatPercentMagnitude(Math.Abs(value))}%";
        }

        private static string FormatPercentMagnitude(float value)
        {
            float rounded = (float)Math.Round(value);
            if (Math.Abs(value - rounded) < 0.05f)
                return ((int)rounded).ToString(CultureInfo.InvariantCulture);

            return value.ToString("0.#", CultureInfo.InvariantCulture);
        }

        private static string FormatSignedMoney(int value)
        {
            return value > 0 ? $"+{value:n0}" : $"{value:n0}";
        }

        private static string FormatMultiplier(float value)
        {
            float rounded = (float)Math.Round(value);
            if (Math.Abs(value - rounded) < 0.05f)
                return $"x{((int)rounded).ToString(CultureInfo.InvariantCulture)}";

            return $"x{value.ToString("0.##", CultureInfo.InvariantCulture)}";
        }
    }
}
