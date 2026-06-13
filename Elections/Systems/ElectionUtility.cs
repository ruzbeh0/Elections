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
        private const float VoteScoreSensitivity = 0.85f;
        private const float CandidateAffinityContrastScale = 1.8f;
        private const float IssueWeightContrastScale = 1.35f;

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
            return GetVoteProbabilityForCandidate(entityManager, voter, voterData, state, 0);
        }

        public static int PickVoteCandidate(EntityManager entityManager, Entity voter, Citizen voterData, ElectionState state, float randomValue)
        {
            int candidateCount = state.ActiveCandidateCount;
            if (candidateCount <= 0)
                return -1;

            VoterProfile profile = CreateVoterProfile(entityManager, voter, voterData);
            float totalWeight = 0f;
            float weightA = 0f;
            float weightB = 0f;
            float weightC = 0f;
            float weightD = 0f;

            for (int i = 0; i < candidateCount; i++)
            {
                float weight = GetCandidateVoteWeight(profile, voter, state, i);
                totalWeight += weight;
                switch (i)
                {
                    case 0:
                        weightA = weight;
                        break;
                    case 1:
                        weightB = weight;
                        break;
                    case 2:
                        weightC = weight;
                        break;
                    case 3:
                        weightD = weight;
                        break;
                }
            }

            if (totalWeight <= 0f)
                return 0;

            float threshold = math.saturate(randomValue) * totalWeight;
            float cumulative = 0f;
            for (int i = 0; i < candidateCount; i++)
            {
                cumulative += i == 0 ? weightA : i == 1 ? weightB : i == 2 ? weightC : weightD;
                if (threshold <= cumulative)
                    return i;
            }

            return candidateCount - 1;
        }

        public static float GetVoteProbabilityForCandidate(EntityManager entityManager, Entity voter, Citizen voterData, ElectionState state, int candidateIndex)
        {
            int candidateCount = state.ActiveCandidateCount;
            if (candidateIndex < 0 || candidateIndex >= candidateCount)
                return 0f;

            VoterProfile profile = CreateVoterProfile(entityManager, voter, voterData);
            float targetWeight = 0f;
            float totalWeight = 0f;
            for (int i = 0; i < candidateCount; i++)
            {
                float weight = GetCandidateVoteWeight(profile, voter, state, i);
                if (i == candidateIndex)
                    targetWeight = weight;
                totalWeight += weight;
            }

            return totalWeight > 0f ? math.clamp(targetWeight / totalWeight, 0.02f, 0.98f) : 0f;
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

            bonus += GetLegislationTurnoutBonusPercent(entityManager, citizenEntity, citizen, state);

            return bonus;
        }

        private static int GetLegislationTurnoutBonusPercent(EntityManager entityManager, Entity citizenEntity, Citizen citizen, ElectionState state)
        {
            int bonus = 0;
            int education = citizen.GetEducationLevel();
            int wealth = GetWealthBracket(entityManager, citizenEntity);
            CitizenAge age = citizen.GetAge();
            bool worker = entityManager.HasComponent<Worker>(citizenEntity);
            bool student = entityManager.HasComponent<Student>(citizenEntity);
            bool hasCar = entityManager.HasComponent<CarKeeper>(citizenEntity);

            if (state.HasLegislation(ElectionLegislationType.VoterIdentification) &&
                education <= 0 &&
                worker)
            {
                bonus += ElectionLegislation.VoterIdentificationUneducatedWorkerDelta;
            }

            if (state.HasLegislation(ElectionLegislationType.PropertyOwnerBallotNotification))
            {
                if (wealth >= 3)
                    bonus += ElectionLegislation.PropertyOwnerWealthyDelta;
                if (hasCar)
                    bonus += ElectionLegislation.PropertyOwnerCarDelta;
                if (wealth <= 1)
                    bonus += ElectionLegislation.PropertyOwnerLowIncomeDelta;
            }

            if (state.HasLegislation(ElectionLegislationType.YouthCivicRegistration))
            {
                if (age == CitizenAge.Teen)
                    bonus += ElectionLegislation.YouthTeenDelta;
                if (student)
                    bonus += ElectionLegislation.YouthStudentDelta;
                if (age == CitizenAge.Elderly)
                    bonus += ElectionLegislation.YouthElderlyDelta;
            }

            if (state.HasLegislation(ElectionLegislationType.NeighborhoodAccessVoting))
            {
                if (wealth <= 1)
                    bonus += ElectionLegislation.NeighborhoodLowIncomeDelta;
                if (!hasCar)
                    bonus += ElectionLegislation.NeighborhoodNoCarDelta;
                if (wealth >= 3)
                    bonus += ElectionLegislation.NeighborhoodWealthyDelta;
            }

            if (state.HasLegislation(ElectionLegislationType.ContinuityOfGovernance) &&
                HasActiveIncumbentPartyCandidate(state))
            {
                int happiness = GetCitizenHappiness(citizen);
                if (happiness >= 60)
                    bonus += ElectionLegislation.ContinuityHappyDelta;
                if (age == CitizenAge.Elderly)
                    bonus += ElectionLegislation.ContinuityElderlyDelta;
                if (happiness < 45)
                    bonus += ElectionLegislation.ContinuityUnhappyDelta;
            }

            return bonus;
        }

        private static bool HasActiveIncumbentPartyCandidate(ElectionState state)
        {
            if (!ElectionState.IsPartyIndex(state.mayorPartyIndex))
                return false;

            int candidateCount = state.ActiveCandidateCount;
            for (int i = 0; i < candidateCount; i++)
            {
                if (state.GetCandidatePartyIndex(i) == state.mayorPartyIndex)
                    return true;
            }

            return false;
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
            return math.clamp(0.2f - margin * 0.55f - donationGap * 0.25f, 0.06f, 0.22f) * 0.5f;
        }

        public static float GetUndecidedProbability(EntityManager entityManager, Entity voter, Citizen voterData, ElectionState state)
        {
            int candidateCount = state.ActiveCandidateCount;
            if (candidateCount <= 0)
                return 0.11f;

            VoterProfile profile = CreateVoterProfile(entityManager, voter, voterData);
            float totalWeight = 0f;
            float topWeight = 0f;
            float secondWeight = 0f;
            float topDonationBonus = 0f;
            float secondDonationBonus = 0f;
            for (int i = 0; i < candidateCount; i++)
            {
                float weight = GetCandidateVoteWeight(profile, voter, state, i);
                float donationBonus = ElectionDonationTiers.GetBonusForAmount(state.GetCandidateDonation(i), state.campaignDonationAmount);
                totalWeight += weight;
                if (weight > topWeight)
                {
                    secondWeight = topWeight;
                    secondDonationBonus = topDonationBonus;
                    topWeight = weight;
                    topDonationBonus = donationBonus;
                }
                else if (weight > secondWeight)
                {
                    secondWeight = weight;
                    secondDonationBonus = donationBonus;
                }
            }

            if (totalWeight <= 0f)
                return 0.11f;

            float margin = (topWeight - secondWeight) / totalWeight;
            float donationGap = math.abs(topDonationBonus - secondDonationBonus);
            return math.clamp(0.22f - margin * 0.65f - donationGap * 0.25f, 0.06f, 0.24f) * 0.5f;
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

        private static float CenteredSimilarity(int a, int b, int maxDistance, float neutralSimilarity)
        {
            return (Similarity(a, b, maxDistance) - neutralSimilarity) * CandidateAffinityContrastScale;
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
            return CenteredSimilarity(profile.age, candidateAge, 3, 0.55f) * 0.7f +
                   CenteredSimilarity(profile.education, candidateEducation, 4, 0.55f) * 0.65f +
                   CenteredSimilarity(profile.wealth, candidateWealth, 4, 0.55f) * 0.6f +
                   CenteredSimilarity(profile.workType, candidateWorkType, 35, 0.5f) * 0.25f;
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

            ElectionEffectDefinition mayorEffect = GetMayorEffectDefinition(state);
            float similarity = 0f;
            if (effect.PositiveImpact.Key == mayorEffect.PositiveImpact.Key)
                similarity += 0.5f;
            if (effect.NegativeImpact.Key == mayorEffect.NegativeImpact.Key)
                similarity += 0.5f;

            float mood = math.clamp((profile.happiness - 50f) / 50f, -1f, 1f);
            return mood * (similarity - 0.35f) * 0.20f;
        }

        private static float GetEndorsementInfluence(VoterProfile profile, ElectionState state, int candidateIndex)
        {
            int endorsedIndex = state.mayorEndorsementCandidateIndex;
            if (endorsedIndex != candidateIndex)
                return 0f;

            Entity endorsedCandidate = state.GetCandidate(candidateIndex);
            if (endorsedCandidate == Entity.Null || state.mayorEndorsementCandidate != endorsedCandidate)
                return 0f;

            float happyTrust = math.saturate((profile.happiness - 55f) / 45f);
            return happyTrust * 0.05f;
        }

        private static float CandidateTagVoteMultiplier(VoterProfile profile, ElectionState state, int candidateIndex)
        {
            int tagId = state.GetCandidateTagId(candidateIndex);
            int multiplier = ElectionCandidateTags.GetCampaignVoteEffectMultiplier(state, candidateIndex, tagId);
            float modifier = ElectionCandidateTags.GetVoteProbabilityBonus(
                tagId,
                profile.age,
                profile.education,
                profile.wealth,
                profile.happiness,
                profile.worker,
                profile.student,
                profile.hasCar) * multiplier;

            return PercentModifierToVoteMultiplier(modifier);
        }

        private static float PartyVoteMultiplier(VoterProfile profile, ElectionState state, int candidateIndex)
        {
            if (!(Mod.m_Setting?.EnableParties ?? false))
                return 1f;

            int partyIndex = state.GetCandidatePartyIndex(candidateIndex);
            if (!ElectionState.IsPartyIndex(partyIndex))
                return 1f;

            float modifier = ElectionPartyTags.GetReputationVoteBonus(state.GetPartyReputation(partyIndex));
            bool isIncumbentParty = partyIndex == state.mayorPartyIndex;
            bool hasWonBefore = state.GetPartyWins(partyIndex) > 0;
            bool pollLeadOutsideMargin = CandidateLeadsReleasedPollOutsideMargin(state, candidateIndex);
            bool transitVoucherActive = state.transitVoucherTurnoutBonusPercent > 0;

            for (int slot = 0; slot < ElectionPartyTags.TagsPerParty; slot++)
            {
                modifier += ElectionPartyTags.GetVoteProbabilityBonus(
                    state.GetPartyTagId(partyIndex, slot),
                    profile.age,
                    profile.education,
                    profile.wealth,
                    profile.happiness,
                    profile.worker,
                    profile.student,
                    profile.hasCar,
                    state.strictVotingIdLawPassed,
                    transitVoucherActive,
                    isIncumbentParty,
                    hasWonBefore,
                    pollLeadOutsideMargin);
            }

            return PercentModifierToVoteMultiplier(modifier);
        }

        private static float CampaignStandingVoteMultiplier(ElectionState state, int candidateIndex)
        {
            return PercentModifierToVoteMultiplier(state.GetCandidateSupportModifierPercent(candidateIndex) / 100f);
        }

        private static float PercentModifierToVoteMultiplier(float modifier)
        {
            return math.max(0.05f, 1f + modifier);
        }

        private static float ImpactPreference(VoterProfile profile, ElectionEffectImpact impact)
        {
            if (string.IsNullOrEmpty(impact.Key))
                return 0f;

            float direction = impact.Positive ? 1f : -1f;
            float magnitude = ImpactMagnitude(impact);
            float issueWeight = ContrastIssueWeight(IssueWeight(profile, impact.Key));
            return direction * magnitude * issueWeight;
        }

        private static float ContrastIssueWeight(float weight)
        {
            return math.max(0.25f, 1f + (weight - 1f) * IssueWeightContrastScale);
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

        private static float StableVoterCandidateLean(Entity voter, int candidateIndex)
        {
            uint mixed = Mix((uint)math.max(1, math.abs(voter.Index * 73856093 + voter.Version * 19349663 + (candidateIndex + 1) * 83492791)));
            return (mixed % 2001) / 1000f - 1f;
        }

        private static float GetCandidateVoteWeight(VoterProfile profile, Entity voter, ElectionState state, int candidateIndex)
        {
            ElectionEffectDefinition effect = GetCandidateEffectDefinition(state, candidateIndex);
            float score =
                CandidateAffinity(
                    profile,
                    state.GetCandidateAge(candidateIndex),
                    state.GetCandidateEducation(candidateIndex),
                    state.GetCandidateWorkType(candidateIndex),
                    state.GetCandidateWealth(candidateIndex)) +
                PlatformPreference(profile, effect) +
                ContinuityPreference(profile, effect, state) +
                GetEndorsementInfluence(profile, state, candidateIndex) +
                StableVoterCandidateLean(voter, candidateIndex) * 0.035f;

            float donationBonus = math.clamp(
                ElectionDonationTiers.GetBonusForAmount(state.GetCandidateDonation(candidateIndex), state.campaignDonationAmount),
                0f,
                0.24f);
            float baseWeight = math.exp(score * VoteScoreSensitivity + donationBonus * 2.5f);
            float supportMultiplier =
                CampaignStandingVoteMultiplier(state, candidateIndex) *
                CandidateTagVoteMultiplier(profile, state, candidateIndex) *
                PartyVoteMultiplier(profile, state, candidateIndex);

            return math.max(0.01f, baseWeight * supportMultiplier);
        }

        private static ElectionEffectDefinition GetCandidateEffectDefinition(ElectionState state, int candidateIndex)
        {
            int tagId = state.GetCandidateTagId(candidateIndex);
            float candidatePlatformScale = ElectionCandidateTags.GetPlatformEffectScale(tagId);
            float positiveScale = candidatePlatformScale;
            float negativeScale = (state.GetCandidateNegativeSoftened(candidateIndex) ? 0.5f : 1f) * candidatePlatformScale;
            if (Mod.m_Setting?.EnableParties ?? false)
            {
                int partyIndex = state.GetCandidatePartyIndex(candidateIndex);
                positiveScale *= ElectionPartyTags.GetPositivePlatformScale(state, partyIndex);
                negativeScale *= ElectionPartyTags.GetNegativePlatformScale(state, partyIndex);
            }

            return ElectionEffects.Get(state.GetCandidateEffectId(candidateIndex), positiveScale, negativeScale);
        }

        private static ElectionEffectDefinition GetMayorEffectDefinition(ElectionState state)
        {
            int tagId = state.mayorTagId;
            float candidatePlatformScale = ElectionCandidateTags.GetPlatformEffectScale(tagId);
            float positiveScale = candidatePlatformScale;
            float negativeScale = (state.mayorNegativeSoftened ? 0.5f : 1f) * candidatePlatformScale;
            if (Mod.m_Setting?.EnableParties ?? false)
            {
                positiveScale *= ElectionPartyTags.GetPositivePlatformScale(state, state.mayorPartyIndex);
                negativeScale *= ElectionPartyTags.GetNegativePlatformScale(state, state.mayorPartyIndex);
            }

            return ElectionEffects.Get(state.mayorEffectId, positiveScale, negativeScale);
        }

        private static bool CandidateLeadsReleasedPollOutsideMargin(ElectionState state, int candidateIndex)
        {
            if (state.stage != ElectionCampaignStage.PollReleased && state.stage != ElectionCampaignStage.Voting)
                return false;

            ElectionPollSummary summary = ElectionPollUtility.BuildSummary(
                state.pollVotesA,
                state.pollVotesB,
                state.pollVotesC,
                state.pollVotesD,
                state.pollUndecided,
                state.ActiveCandidateCount,
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty);
            return summary.LeaderIndex == candidateIndex && !summary.WithinMargin;
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
