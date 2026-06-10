using Elections.Components;
using Elections.Bridge;
using Elections.Models;
using Game.Citizens;
using Game.Economy;
using Unity.Entities;
using Unity.Mathematics;

namespace Elections.Systems
{
    internal static class ElectionUtility
    {
        public const int MinVotingStartHour = 6;
        public const int MaxVotingStartHour = 10;
        public const int DefaultVotingStartHour = 8;
        public const int MinVotingEndHour = 16;
        public const int MaxVotingEndHour = 18;
        public const int DefaultVotingEndHour = 17;
        public const int ResultsAnnouncementHour = 20;

        public const int DefaultVotingStartMinute = DefaultVotingStartHour * 60;
        public const int DefaultVotingEndMinute = DefaultVotingEndHour * 60;
        public const int ResultsAnnouncementMinute = ResultsAnnouncementHour * 60;
        public const int DefaultTeenDailyVotingTurnoutPercent = 36;
        public const int DefaultAdultDailyVotingTurnoutPercent = 49;
        public const int DefaultElderlyDailyVotingTurnoutPercent = 58;
        public const int StrictVotingIdUneducatedWorkerTurnoutPenaltyPercent = 20;

        private struct VoterProfile
        {
            public int age;
            public int education;
            public int workType;
            public int wealth;
            public int happiness;
            public bool elderly;
            public bool student;
            public bool worker;
            public bool hasCar;
        }

        public static bool IsEligibleResident(EntityManager entityManager, Entity citizenEntity, Citizen citizen)
        {
            if (citizen.GetAge() != CitizenAge.Adult && citizen.GetAge() != CitizenAge.Elderly)
                return false;

            return IsResident(entityManager, citizenEntity, citizen);
        }

        public static bool IsEligibleVoterResident(EntityManager entityManager, Entity citizenEntity, Citizen citizen)
        {
            CitizenAge age = citizen.GetAge();
            if (age != CitizenAge.Teen && age != CitizenAge.Adult && age != CitizenAge.Elderly)
                return false;

            return IsResident(entityManager, citizenEntity, citizen);
        }

        private static bool IsResident(EntityManager entityManager, Entity citizenEntity, Citizen citizen)
        {
            if ((citizen.m_State & CitizenFlags.Tourist) != 0 ||
                (citizen.m_State & CitizenFlags.Commuter) != 0)
                return false;

            return entityManager.HasComponent<HouseholdMember>(citizenEntity);
        }

        public static int GetWealthBracket(EntityManager entityManager, Entity citizenEntity)
        {
            if (!entityManager.HasComponent<HouseholdMember>(citizenEntity))
                return 0;

            Entity household = entityManager.GetComponentData<HouseholdMember>(citizenEntity).m_Household;
            if (household == Entity.Null || !entityManager.HasBuffer<Resources>(household))
                return 0;

            int money = EconomyUtils.GetResources(Resource.Money, entityManager.GetBuffer<Resources>(household));
            if (money < 5000)
                return 0;
            if (money < 25000)
                return 1;
            if (money < 100000)
                return 2;
            if (money < 500000)
                return 3;
            return 4;
        }

        public static int GetWorkType(EntityManager entityManager, Entity citizenEntity)
        {
            if (entityManager.HasComponent<Worker>(citizenEntity))
                return 10 + entityManager.GetComponentData<Worker>(citizenEntity).m_Level;

            if (entityManager.HasComponent<Student>(citizenEntity))
                return 30 + entityManager.GetComponentData<Student>(citizenEntity).m_Level;

            return 0;
        }

        public static float GetVoteProbabilityForA(EntityManager entityManager, Entity voter, Citizen voterData, ElectionState state)
        {
            VoterProfile profile = CreateVoterProfile(entityManager, voter, voterData);
            ElectionEffectDefinition candidateAEffect = ElectionEffects.Get(state.candidateAEffectId, state.candidateANegativeSoftened, state.candidateATagId);
            ElectionEffectDefinition candidateBEffect = ElectionEffects.Get(state.candidateBEffectId, state.candidateBNegativeSoftened, state.candidateBTagId);

            float scoreA = CandidateAffinity(profile, state.candidateAAge, state.candidateAEducation, state.candidateAWorkType, state.candidateAWealth) +
                           PlatformPreference(profile, candidateAEffect) +
                           ContinuityPreference(profile, candidateAEffect, state);
            float scoreB = CandidateAffinity(profile, state.candidateBAge, state.candidateBEducation, state.candidateBWorkType, state.candidateBWealth) +
                           PlatformPreference(profile, candidateBEffect) +
                           ContinuityPreference(profile, candidateBEffect, state);

            float donationInfluence = math.clamp(
                ElectionDonationTiers.GetBonusForAmount(state.donationA, state.campaignDonationAmount) -
                ElectionDonationTiers.GetBonusForAmount(state.donationB, state.campaignDonationAmount),
                -0.12f,
                0.12f);

            float endorsementInfluence = GetEndorsementInfluence(profile, state);
            float candidateTagInfluence =
                CandidateTagPreference(profile, state.candidateATagId) -
                CandidateTagPreference(profile, state.candidateBTagId);
            float personalLean = StableVoterLean(voter) * 0.035f;
            return math.clamp(0.5f + (scoreA - scoreB) * 0.09f + donationInfluence + endorsementInfluence + candidateTagInfluence + personalLean, 0.08f, 0.92f);
        }

        public static int GetTargetedTurnoutBonusPercent(EntityManager entityManager, Entity citizenEntity, Citizen citizen, ElectionState state)
        {
            int bonus = 0;
            if (IsLowIncomeResident(entityManager, citizenEntity))
            {
                bonus += state.lowIncomeTurnoutBonusPercent;
                bonus += state.cashAssistanceTurnoutBonusPercent;
            }

            if (IsTransitVoucherEligible(entityManager, citizenEntity, citizen))
                bonus += state.transitVoucherTurnoutBonusPercent;

            return bonus;
        }

        public static bool IsLowIncomeResident(EntityManager entityManager, Entity citizenEntity)
        {
            return GetWealthBracket(entityManager, citizenEntity) <= 1;
        }

        public static bool IsTransitVoucherEligible(EntityManager entityManager, Entity citizenEntity, Citizen citizen)
        {
            if (entityManager.HasComponent<CarKeeper>(citizenEntity))
                return false;

            CitizenAge age = citizen.GetAge();
            return age == CitizenAge.Teen ||
                   age == CitizenAge.Elderly ||
                   IsLowIncomeResident(entityManager, citizenEntity);
        }

        public static float GetVotingTurnoutMultiplier(Citizen citizen)
        {
            int happiness = GetCitizenHappiness(citizen);
            if (happiness > 70)
                return 1.04f;
            if (happiness > 55)
                return 1.0f;
            if (happiness > 40)
                return 1.05f;
            if (happiness > 25)
                return 1.08f;

            return 0.94f;
        }

        public static float GetUndecidedProbability(float probabilityA, ElectionState state)
        {
            float margin = math.abs(probabilityA - 0.5f);
            float donationGap = math.abs(
                ElectionDonationTiers.GetBonusForAmount(state.donationA, state.campaignDonationAmount) -
                ElectionDonationTiers.GetBonusForAmount(state.donationB, state.campaignDonationAmount));
            return math.clamp(0.2f - margin * 0.55f - donationGap * 0.25f, 0.06f, 0.22f);
        }

        public static int DayKey(int year, int month, int day)
        {
            return year * 10000 + month * 100 + day;
        }

        public static int CalendarYear(System.DateTime dateTime)
        {
            return dateTime.Year;
        }

        public static int CalendarMonth(System.DateTime dateTime)
        {
            GetCalendarDate(dateTime, out _, out int month, out _);
            return month;
        }

        public static int CurrentCalendarMonth(World world, System.DateTime dateTime)
        {
            GetCurrentCalendarDate(world, dateTime, out _, out int month, out _);
            return month;
        }

        public static int CalendarDay(System.DateTime dateTime)
        {
            GetCalendarDate(dateTime, out _, out _, out int day);
            return day;
        }

        public static int CalendarDayKey(System.DateTime dateTime)
        {
            GetCalendarDate(dateTime, out int year, out int month, out int day);
            return DayKey(year, month, day);
        }

        public static int CurrentCalendarDayKey(World world, System.DateTime dateTime)
        {
            GetCurrentCalendarDate(world, dateTime, out int year, out int month, out int day);
            return DayKey(year, month, day);
        }

        public static void GetCalendarDate(System.DateTime dateTime, out int year, out int month, out int day)
        {
            int daysPerMonth = math.max(1, RealisticTripsBridge.GetDaysPerMonth());
            int zeroBasedDayOfYear = math.max(0, dateTime.DayOfYear - 1);
            year = dateTime.Year;
            month = math.clamp(zeroBasedDayOfYear / daysPerMonth + 1, 1, 12);
            day = zeroBasedDayOfYear % daysPerMonth + 1;
        }

        public static void GetCurrentCalendarDate(World world, System.DateTime dateTime, out int year, out int month, out int day)
        {
            if (RealisticTripsBridge.TryGetDisplayedCalendarDate(world, out year, out month, out day))
                return;

            GetCalendarDate(dateTime, out year, out month, out day);
        }

        public static void AddCalendarMonths(int year, int month, int delta, out int resultYear, out int resultMonth)
        {
            int zeroBased = (year * 12) + math.clamp(month, 1, 12) - 1 + delta;
            resultYear = zeroBased / 12;
            resultMonth = zeroBased % 12 + 1;
        }

        public static void GetPreviousCalendarDate(int year, int month, int day, out int resultYear, out int resultMonth, out int resultDay)
        {
            if (day > 1)
            {
                resultYear = year;
                resultMonth = math.clamp(month, 1, 12);
                resultDay = day - 1;
                return;
            }

            AddCalendarMonths(year, month, -1, out resultYear, out resultMonth);
            resultDay = math.max(1, RealisticTripsBridge.GetDaysPerMonth());
        }

        public static int CompareCalendarDate(int yearA, int monthA, int dayA, int yearB, int monthB, int dayB)
        {
            return DayKey(yearA, monthA, dayA).CompareTo(DayKey(yearB, monthB, dayB));
        }

        public static string FormatDate(int year, int month, int day)
        {
            if (month < 1 || month > 12)
                return $"{year}";

            return $"{day:00}/{month:00}/{year}";
        }

        public static string FormatDate(System.DateTime dateTime)
        {
            GetCalendarDate(dateTime, out int year, out int month, out int day);
            return FormatDate(year, month, day);
        }

        public static string FormatCurrentDate(World world, System.DateTime dateTime)
        {
            GetCurrentCalendarDate(world, dateTime, out int year, out int month, out int day);
            return FormatDate(year, month, day);
        }

        public static int MinuteOfDay(System.DateTime dateTime)
        {
            return dateTime.Hour * 60 + dateTime.Minute;
        }

        public static int GetConfiguredVotingStartMinute()
        {
            int hour = Mod.m_Setting != null
                ? (int)Mod.m_Setting.ElectionVotingStartHour
                : DefaultVotingStartHour;
            if (hour < MinVotingStartHour || hour > MaxVotingStartHour)
                hour = DefaultVotingStartHour;

            return hour * 60;
        }

        public static int GetConfiguredVotingEndMinute()
        {
            int hour = Mod.m_Setting != null
                ? (int)Mod.m_Setting.ElectionVotingEndHour
                : DefaultVotingEndHour;
            if (hour < MinVotingEndHour || hour > MaxVotingEndHour)
                hour = DefaultVotingEndHour;

            return hour * 60;
        }

        public static int NormalizeVotingStartMinute(int minuteOfDay)
        {
            if (minuteOfDay <= 0)
                return DefaultVotingStartMinute;

            int hour = minuteOfDay / 60;
            if (hour < MinVotingStartHour || hour > MaxVotingStartHour)
                return DefaultVotingStartMinute;

            return hour * 60;
        }

        public static int NormalizeVotingEndMinute(int minuteOfDay)
        {
            if (minuteOfDay <= 0)
                return DefaultVotingEndMinute;

            int hour = minuteOfDay / 60;
            if (hour < MinVotingEndHour || hour > MaxVotingEndHour)
                return DefaultVotingEndMinute;

            return hour * 60;
        }

        public static string FormatClockTime(int minuteOfDay)
        {
            int minute = math.clamp(minuteOfDay, 0, 23 * 60 + 59);
            return $"{minute / 60:00}:{minute % 60:00}";
        }

        public static string FormatHourText(int minuteOfDay)
        {
            int hour = math.clamp(minuteOfDay / 60, 0, 23);
            string suffix = hour < 12 ? "AM" : "PM";
            int displayHour = hour % 12;
            if (displayHour == 0)
                displayHour = 12;

            return $"{displayHour} {suffix}";
        }

        public static bool IsAtOrAfter(System.DateTime now, int dayKey, int minuteOfDay)
        {
            int currentDayKey = CalendarDayKey(now);
            if (currentDayKey != dayKey)
                return currentDayKey > dayKey;

            return MinuteOfDay(now) >= minuteOfDay;
        }

        private static float Similarity(int a, int b, int maxDistance)
        {
            int distance = math.abs(a - b);
            return 1f - math.saturate(distance / (float)math.max(1, maxDistance));
        }

        private static VoterProfile CreateVoterProfile(EntityManager entityManager, Entity voter, Citizen voterData)
        {
            CitizenAge age = voterData.GetAge();
            bool worker = entityManager.HasComponent<Worker>(voter);
            bool student = entityManager.HasComponent<Student>(voter);
            return new VoterProfile
            {
                age = (int)age,
                education = voterData.GetEducationLevel(),
                workType = GetWorkType(entityManager, voter),
                wealth = GetWealthBracket(entityManager, voter),
                happiness = GetCitizenHappiness(voterData),
                elderly = age == CitizenAge.Elderly,
                student = student,
                worker = worker,
                hasCar = entityManager.HasComponent<CarKeeper>(voter)
            };
        }

        private static int GetCitizenHappiness(Citizen citizen)
        {
            return (citizen.m_WellBeing + citizen.m_Health) / 2;
        }

        private static float CandidateAffinity(VoterProfile profile, int candidateAge, int candidateEducation, int candidateWorkType, int candidateWealth)
        {
            return Similarity(profile.age, candidateAge, 3) * 0.45f +
                   Similarity(profile.education, candidateEducation, 4) * 0.4f +
                   Similarity(profile.workType, candidateWorkType, 35) * 0.35f +
                   Similarity(profile.wealth, candidateWealth, 4) * 0.3f;
        }

        private static float PlatformPreference(VoterProfile profile, ElectionEffectDefinition effect)
        {
            float issueIntensity = 1f + math.saturate((55f - profile.happiness) / 55f) * 0.25f;
            return (ImpactPreference(profile, effect.PositiveImpact) +
                    ImpactPreference(profile, effect.NegativeImpact)) * issueIntensity;
        }

        private static float ContinuityPreference(VoterProfile profile, ElectionEffectDefinition effect, ElectionState state)
        {
            if (state.mayorEffectId <= 0)
                return 0f;

            ElectionEffectDefinition mayorEffect = ElectionEffects.Get(state.mayorEffectId, state.mayorNegativeSoftened, state.mayorTagId);
            float similarity = 0f;
            if (effect.PositiveImpact.Key == mayorEffect.PositiveImpact.Key)
                similarity += 0.5f;
            if (effect.NegativeImpact.Key == mayorEffect.NegativeImpact.Key)
                similarity += 0.5f;

            float mood = math.clamp((profile.happiness - 50f) / 50f, -1f, 1f);
            return mood * (similarity - 0.35f) * 0.20f;
        }

        private static float GetEndorsementInfluence(VoterProfile profile, ElectionState state)
        {
            int endorsedIndex = state.mayorEndorsementCandidateIndex;
            if (endorsedIndex != 0 && endorsedIndex != 1)
                return 0f;

            Entity endorsedCandidate = endorsedIndex == 0 ? state.candidateA : state.candidateB;
            if (endorsedCandidate == Entity.Null || state.mayorEndorsementCandidate != endorsedCandidate)
                return 0f;

            float happyTrust = math.saturate((profile.happiness - 55f) / 45f);
            float bonus = happyTrust * 0.05f;
            return endorsedIndex == 0 ? bonus : -bonus;
        }

        private static float CandidateTagPreference(VoterProfile profile, int tagId)
        {
            return ElectionCandidateTags.GetVoteProbabilityBonus(
                tagId,
                profile.age,
                profile.education,
                profile.wealth,
                profile.happiness,
                profile.worker,
                profile.student,
                profile.hasCar);
        }

        private static float ImpactPreference(VoterProfile profile, ElectionEffectImpact impact)
        {
            if (string.IsNullOrEmpty(impact.Key))
                return 0f;

            float direction = impact.Positive ? 1f : -1f;
            float magnitude = ImpactMagnitude(impact);
            float issueWeight = IssueWeight(profile, impact.Key);
            return direction * magnitude * issueWeight;
        }

        private static float ImpactMagnitude(ElectionEffectImpact impact)
        {
            if (impact.MoneyDelta != 0)
                return math.clamp(math.abs(impact.MoneyDelta) / 350000f, 0.2f, 1f) * 0.28f;

            if (impact.ResourceConsumptionMultiplier > 0f && math.abs(impact.ResourceConsumptionMultiplier - 1f) > 0.0001f)
                return math.clamp(math.abs(impact.ResourceConsumptionMultiplier - 1f) / 0.1f, 0.2f, 1f) * 0.32f;

            if (impact.AccumulatedXpMultiplier > 0f && math.abs(impact.AccumulatedXpMultiplier - 1f) > 0.0001f)
                return math.clamp(math.abs(impact.AccumulatedXpMultiplier - 1f), 0.2f, 1f) * 0.32f;

            return math.clamp(math.abs(impact.Mul) / 0.1f, 0.2f, 1f) * 0.32f;
        }

        private static float IssueWeight(VoterProfile profile, string key)
        {
            switch (key)
            {
                case "Money":
                    return profile.wealth <= 1 ? 1.25f : 0.85f;
                case "TaxiStartingFee":
                    return profile.hasCar ? 0.7f : 1.35f;
                case "ImportCost":
                case "ExportCost":
                case "LoanInterest":
                case "BuildingLevelingCost":
                case "AccumulatedXP":
                    return profile.worker || profile.wealth >= 3 ? 1.15f : 0.85f;
                case "CityServiceUpkeep":
                    return profile.wealth <= 1 ? 1.1f : 0.95f;
                case "CrimeProbability":
                case "CrimeAccumulation":
                    return profile.elderly || profile.wealth <= 1 ? 1.35f : 1.05f;
                case "DiseaseProbability":
                case "HospitalEfficiency":
                case "PollutionHealthAffect":
                    return profile.elderly ? 1.45f : 1.05f;
                case "IndustrialAirPollution":
                case "IndustrialGroundPollution":
                case "IndustrialGarbage":
                case "ResourceConsumption":
                    return profile.education >= 3 || profile.student || profile.elderly ? 1.25f : 0.95f;
                case "IndustrialEfficiency":
                case "OfficeEfficiency":
                case "IndustrialElectronicsEfficiency":
                case "OfficeSoftwareEfficiency":
                case "IndustrialElectronicsDemand":
                case "OfficeSoftwareDemand":
                    return profile.worker || profile.education >= 3 || profile.wealth >= 3 ? 1.2f : 0.8f;
                case "CollegeGraduation":
                case "UniversityGraduation":
                case "UniversityInterest":
                    return profile.student || profile.education >= 2 ? 1.35f : 0.75f;
                case "Attractiveness":
                case "Entertainment":
                case "ParkEntertainment":
                    return profile.elderly ? 0.9f : 1.05f;
                default:
                    return 1f;
            }
        }

        private static float StableVoterLean(Entity voter)
        {
            uint mixed = Mix((uint)math.max(1, math.abs(voter.Index * 73856093 + voter.Version * 19349663)));
            return (mixed % 2001) / 1000f - 1f;
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
    }
}
