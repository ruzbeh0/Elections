using Game.Common;
using Game.Prefabs;
using Game.Simulation;
using System;
using System.Reflection;
using Unity.Entities;
using UnityEngine;

namespace Elections.Bridge
{
    internal static class RealisticTripsBridge
    {
        private delegate bool CanRequestSocialTripDelegate(Entity citizen, Entity targetBuilding);
        private delegate bool IsCitizenOutsideWorkHoursDelegate(Entity citizen);
        private delegate bool RequestSocialTripDelegate(Entity citizen, Entity targetBuilding, Entity hostCitizen, int tripType, float durationMinutes, int priority);
        private delegate DateTime GetCurrentDateTimeDelegate();
        private delegate void SetMayorResourceConsumptionMultiplierDelegate(int effectId, float multiplier);
        private delegate void ClearMayorResourceConsumptionMultiplierDelegate(int effectId);
        private delegate void SetElectionDaySundayOverrideDelegate(int year, int dayOfYear, bool enabled);
        private delegate void ClearElectionDaySundayOverrideDelegate();
        private delegate void SetElectionDaySpecialEventsSuppressedDelegate(int year, int dayOfYear, bool enabled);
        private delegate void ClearElectionDaySpecialEventsSuppressedDelegate();

        private static bool s_Resolved;
        private static Type s_BridgeType;
        private static CanRequestSocialTripDelegate s_CanRequestSocialTrip;
        private static IsCitizenOutsideWorkHoursDelegate s_IsCitizenOutsideWorkHours;
        private static RequestSocialTripDelegate s_RequestSocialTrip;
        private static GetCurrentDateTimeDelegate s_GetCurrentDateTime;
        private static bool s_PolicyResolved;
        private static Type s_PolicyBridgeType;
        private static SetMayorResourceConsumptionMultiplierDelegate s_SetMayorResourceConsumptionMultiplier;
        private static ClearMayorResourceConsumptionMultiplierDelegate s_ClearMayorResourceConsumptionMultiplier;
        private static SetElectionDaySundayOverrideDelegate s_SetElectionDaySundayOverride;
        private static ClearElectionDaySundayOverrideDelegate s_ClearElectionDaySundayOverride;
        private static SetElectionDaySpecialEventsSuppressedDelegate s_SetElectionDaySpecialEventsSuppressed;
        private static ClearElectionDaySpecialEventsSuppressedDelegate s_ClearElectionDaySpecialEventsSuppressed;
        private static Type s_ModType;
        private static Type s_TimeSystemType;
        private static FieldInfo s_SettingField;
        private static FieldInfo s_TicksPerDayField;
        private static PropertyInfo s_DaysPerMonthProperty;
        private static PropertyInfo s_SlowTimeFactorProperty;
        private static bool s_LoggedMissingTime;

        public static bool IsAvailable
        {
            get
            {
                EnsureResolve();
                return s_RequestSocialTrip != null;
            }
        }

        public static bool CanRequestTrip(Entity citizen, Entity targetBuilding)
        {
            EnsureResolve();
            if (s_CanRequestSocialTrip == null)
                return IsAvailable;

            try
            {
                return s_CanRequestSocialTrip(citizen, targetBuilding);
            }
            catch (Exception ex)
            {
                Mod.log.Warn($"RealisticTrips CanRequestSocialTrip failed: {ex.Message}");
                return false;
            }
        }

        public static bool IsCitizenOutsideWorkHours(Entity citizen)
        {
            EnsureResolve();
            if (s_IsCitizenOutsideWorkHours == null)
                return true;

            try
            {
                return s_IsCitizenOutsideWorkHours(citizen);
            }
            catch (Exception ex)
            {
                Mod.log.Warn($"RealisticTrips IsCitizenOutsideWorkHours failed: {ex.Message}");
                return false;
            }
        }

        public static bool RequestVotingTrip(Entity citizen, Entity targetBuilding, float durationMinutes, int priority)
        {
            EnsureResolve();
            if (s_RequestSocialTrip == null)
                return false;

            try
            {
                return s_RequestSocialTrip(citizen, targetBuilding, Entity.Null, 1001, durationMinutes, priority);
            }
            catch (Exception ex)
            {
                Mod.log.Warn($"RealisticTrips RequestSocialTrip failed: {ex.Message}");
                return false;
            }
        }

        public static bool RequestElectionVictoryPartyTrip(Entity citizen, Entity targetBuilding, float durationMinutes, int priority)
        {
            EnsureResolve();
            if (s_RequestSocialTrip == null)
                return false;

            try
            {
                return s_RequestSocialTrip(citizen, targetBuilding, Entity.Null, 1002, durationMinutes, priority);
            }
            catch (Exception ex)
            {
                Mod.log.Warn($"RealisticTrips RequestElectionVictoryPartyTrip failed: {ex.Message}");
                return false;
            }
        }

        public static bool RequestBribeMeetingTrip(Entity citizen, Entity targetBuilding, Entity hostCitizen, float durationMinutes, int priority)
        {
            EnsureResolve();
            if (s_RequestSocialTrip == null)
                return false;

            try
            {
                return s_RequestSocialTrip(citizen, targetBuilding, hostCitizen, 1003, durationMinutes, priority);
            }
            catch (Exception ex)
            {
                Mod.log.Warn($"RealisticTrips RequestBribeMeetingTrip failed: {ex.Message}");
                return false;
            }
        }

        public static void SetMayorResourceConsumptionMultiplier(int effectId, float multiplier)
        {
            EnsurePolicyResolve();
            if (s_SetMayorResourceConsumptionMultiplier == null)
                return;

            try
            {
                s_SetMayorResourceConsumptionMultiplier(effectId, multiplier);
            }
            catch (Exception ex)
            {
                Mod.log.Warn($"RealisticTrips SetMayorResourceConsumptionMultiplier failed: {ex.Message}");
            }
        }

        public static void ClearMayorResourceConsumptionMultiplier(int effectId)
        {
            EnsurePolicyResolve();
            if (s_ClearMayorResourceConsumptionMultiplier == null)
                return;

            try
            {
                s_ClearMayorResourceConsumptionMultiplier(effectId);
            }
            catch (Exception ex)
            {
                Mod.log.Warn($"RealisticTrips ClearMayorResourceConsumptionMultiplier failed: {ex.Message}");
            }
        }

        public static void SetElectionDaySundayOverride(int year, int dayOfYear, bool enabled)
        {
            EnsurePolicyResolve();
            if (s_SetElectionDaySundayOverride == null)
                return;

            try
            {
                s_SetElectionDaySundayOverride(year, dayOfYear, enabled);
            }
            catch (Exception ex)
            {
                Mod.log.Warn($"RealisticTrips SetElectionDaySundayOverride failed: {ex.Message}");
            }
        }

        public static void ClearElectionDaySundayOverride()
        {
            EnsurePolicyResolve();
            if (s_ClearElectionDaySundayOverride == null)
                return;

            try
            {
                s_ClearElectionDaySundayOverride();
            }
            catch (Exception ex)
            {
                Mod.log.Warn($"RealisticTrips ClearElectionDaySundayOverride failed: {ex.Message}");
            }
        }

        public static void SetElectionDaySpecialEventsSuppressed(int year, int dayOfYear, bool enabled)
        {
            EnsurePolicyResolve();
            if (s_SetElectionDaySpecialEventsSuppressed == null)
                return;

            try
            {
                s_SetElectionDaySpecialEventsSuppressed(year, dayOfYear, enabled);
            }
            catch (Exception ex)
            {
                Mod.log.Warn($"RealisticTrips SetElectionDaySpecialEventsSuppressed failed: {ex.Message}");
            }
        }

        public static void ClearElectionDaySpecialEventsSuppressed()
        {
            EnsurePolicyResolve();
            if (s_ClearElectionDaySpecialEventsSuppressed == null)
                return;

            try
            {
                s_ClearElectionDaySpecialEventsSuppressed();
            }
            catch (Exception ex)
            {
                Mod.log.Warn($"RealisticTrips ClearElectionDaySpecialEventsSuppressed failed: {ex.Message}");
            }
        }

        public static DateTime GetCurrentDateTime(World world)
        {
            if (TryGetCurrentDateTime(out DateTime dateTime))
                return dateTime;

            TimeSystem timeSystem = world?.GetExistingSystemManaged<TimeSystem>();
            return timeSystem != null ? timeSystem.GetCurrentDateTime() : DateTime.UtcNow;
        }

        public static bool TryGetCurrentDateTime(out DateTime dateTime)
        {
            EnsureResolve();
            if (s_GetCurrentDateTime != null)
            {
                try
                {
                    dateTime = s_GetCurrentDateTime();
                    return true;
                }
                catch (Exception ex)
                {
                    Mod.log.Warn($"RealisticTrips GetCurrentDateTime failed: {ex.Message}");
                }
            }

            if (!s_LoggedMissingTime)
            {
                s_LoggedMissingTime = true;
                Mod.log.Warn("RealisticTrips time bridge is unavailable; Elections simulation updates are paused.");
            }

            dateTime = default;
            return false;
        }

        public static int GetDaysPerMonth()
        {
            EnsureResolve();

            try
            {
                if (s_SettingField == null || s_DaysPerMonthProperty == null)
                    return 1;

                object setting = s_SettingField.GetValue(null);
                if (setting == null)
                    return 1;

                object value = s_DaysPerMonthProperty.GetValue(setting, null);
                if (value is int daysPerMonth)
                    return Math.Max(1, daysPerMonth);
            }
            catch (Exception ex)
            {
                Mod.log.Warn($"RealisticTrips daysPerMonth lookup failed: {ex.Message}");
            }

            return 1;
        }

        public static bool TryGetDisplayedCalendarDate(World world, out int year, out int month, out int day)
        {
            year = 0;
            month = 0;
            day = 0;

            EnsureResolve();

            try
            {
                world = world ?? World.DefaultGameObjectInjectionWorld;
                if (world == null)
                    return false;

                SimulationSystem simulationSystem = world.GetExistingSystemManaged<SimulationSystem>();
                if (simulationSystem == null)
                    return false;

                EntityManager entityManager = world.EntityManager;
                using (EntityQuery timeDataQuery = entityManager.CreateEntityQuery(ComponentType.ReadOnly<TimeData>()))
                using (EntityQuery timeSettingsQuery = entityManager.CreateEntityQuery(ComponentType.ReadOnly<TimeSettingsData>()))
                {
                    if (timeDataQuery.IsEmptyIgnoreFilter)
                        return false;

                    TimeData timeData = timeDataQuery.GetSingleton<TimeData>();
                    TimeSettingsData timeSettingsData = timeSettingsQuery.IsEmptyIgnoreFilter
                        ? new TimeSettingsData { m_DaysPerYear = 12 }
                        : timeSettingsQuery.GetSingleton<TimeSettingsData>();

                    int ticksPerDay = GetTicksPerDay();
                    if (ticksPerDay <= 0)
                        return false;

                    int daysPerYear = Math.Max(1, timeSettingsData.m_DaysPerYear);
                    int daysPerMonth = GetDaysPerMonth();
                    int epochTicks =
                        Mathf.RoundToInt(timeData.TimeOffset * ticksPerDay) +
                        Mathf.RoundToInt(timeData.GetDateOffset(timeSettingsData.m_DaysPerYear) *
                                         ticksPerDay *
                                         (float)timeSettingsData.m_DaysPerYear);

                    int n = epochTicks + GetDisplayedTicks(simulationSystem, timeData);
                    int totalElapsedDays = (int)Math.Floor((float)n / ticksPerDay);

                    year = timeData.m_StartingYear + (int)Math.Floor((float)totalElapsedDays / daysPerYear);

                    int dayOfYear = totalElapsedDays % daysPerYear;
                    if (dayOfYear < 0)
                        dayOfYear += daysPerYear;

                    month = dayOfYear / daysPerMonth + 1;
                    day = dayOfYear % daysPerMonth + 1;

                    if (month > 12)
                        month = 12;

                    return true;
                }
            }
            catch (Exception ex)
            {
                Mod.log.Warn($"RealisticTrips displayed calendar lookup failed: {ex.Message}");
                return false;
            }
        }

        private static void EnsureResolve()
        {
            if (s_Resolved && s_RequestSocialTrip != null)
                return;

            s_Resolved = true;
            s_BridgeType = Type.GetType("Time2Work.Bridge.SocialTripsBridge, Time2Work") ??
                           FindType("Time2Work.Bridge.SocialTripsBridge");
            s_ModType = Type.GetType("Time2Work.Mod, Time2Work") ??
                        FindType("Time2Work.Mod");
            s_TimeSystemType = Type.GetType("Time2Work.Time2WorkTimeSystem, Time2Work") ??
                               FindType("Time2Work.Time2WorkTimeSystem");

            if (s_BridgeType == null)
            {
                s_Resolved = false;
                return;
            }

            s_CanRequestSocialTrip = CreateDelegate<CanRequestSocialTripDelegate>(
                "CanRequestSocialTrip",
                new[] { typeof(Entity), typeof(Entity) });
            s_IsCitizenOutsideWorkHours = CreateDelegate<IsCitizenOutsideWorkHoursDelegate>(
                "IsCitizenOutsideWorkHours",
                new[] { typeof(Entity) });
            s_RequestSocialTrip = CreateDelegate<RequestSocialTripDelegate>(
                "RequestSocialTrip",
                new[] { typeof(Entity), typeof(Entity), typeof(Entity), typeof(int), typeof(float), typeof(int) });
            s_GetCurrentDateTime = CreateDelegate<GetCurrentDateTimeDelegate>("GetCurrentDateTime", Type.EmptyTypes);

            if (s_ModType != null)
            {
                s_SettingField = s_ModType.GetField("m_Setting", BindingFlags.Public | BindingFlags.Static);
                Type settingType = s_SettingField?.FieldType;
                s_DaysPerMonthProperty = settingType?.GetProperty("daysPerMonth", BindingFlags.Public | BindingFlags.Instance);
                s_SlowTimeFactorProperty = settingType?.GetProperty("slow_time_factor", BindingFlags.Public | BindingFlags.Instance);
            }

            s_TicksPerDayField = s_TimeSystemType?.GetField("kTicksPerDay", BindingFlags.Public | BindingFlags.Static);

            if (s_RequestSocialTrip == null)
                s_Resolved = false;
        }

        private static int GetTicksPerDay()
        {
            EnsureResolve();

            try
            {
                if (s_TicksPerDayField?.GetValue(null) is int ticksPerDay && ticksPerDay > 0)
                    return ticksPerDay;
            }
            catch (Exception ex)
            {
                Mod.log.Warn($"RealisticTrips ticks-per-day lookup failed: {ex.Message}");
            }

            return Math.Max(1, (int)Math.Floor(GetSlowTimeFactor() * TimeSystem.kTicksPerDay));
        }

        private static void EnsurePolicyResolve()
        {
            if (s_PolicyResolved &&
                (s_SetMayorResourceConsumptionMultiplier != null ||
                 s_SetElectionDaySundayOverride != null ||
                 s_SetElectionDaySpecialEventsSuppressed != null))
            {
                return;
            }

            s_PolicyResolved = true;
            s_PolicyBridgeType = Type.GetType("Time2Work.Bridge.ElectionsBridge, Time2Work") ??
                                 FindType("Time2Work.Bridge.ElectionsBridge");

            if (s_PolicyBridgeType == null)
            {
                s_PolicyResolved = false;
                return;
            }

            s_SetMayorResourceConsumptionMultiplier = CreatePolicyDelegate<SetMayorResourceConsumptionMultiplierDelegate>(
                "SetMayorResourceConsumptionMultiplier",
                new[] { typeof(int), typeof(float) });
            s_ClearMayorResourceConsumptionMultiplier = CreatePolicyDelegate<ClearMayorResourceConsumptionMultiplierDelegate>(
                "ClearMayorResourceConsumptionMultiplier",
                new[] { typeof(int) });
            s_SetElectionDaySundayOverride = CreatePolicyDelegate<SetElectionDaySundayOverrideDelegate>(
                "SetElectionDaySundayOverride",
                new[] { typeof(int), typeof(int), typeof(bool) });
            s_ClearElectionDaySundayOverride = CreatePolicyDelegate<ClearElectionDaySundayOverrideDelegate>(
                "ClearElectionDaySundayOverride",
                Type.EmptyTypes);
            s_SetElectionDaySpecialEventsSuppressed = CreatePolicyDelegate<SetElectionDaySpecialEventsSuppressedDelegate>(
                "SetElectionDaySpecialEventsSuppressed",
                new[] { typeof(int), typeof(int), typeof(bool) });
            s_ClearElectionDaySpecialEventsSuppressed = CreatePolicyDelegate<ClearElectionDaySpecialEventsSuppressedDelegate>(
                "ClearElectionDaySpecialEventsSuppressed",
                Type.EmptyTypes);

            if (s_SetMayorResourceConsumptionMultiplier == null &&
                s_SetElectionDaySundayOverride == null &&
                s_SetElectionDaySpecialEventsSuppressed == null)
            {
                s_PolicyResolved = false;
            }
        }

        private static int GetDisplayedTicks(SimulationSystem simulationSystem, TimeData timeData)
        {
            float num = 182.044449f * GetSlowTimeFactor();
            return Mathf.FloorToInt(
                Mathf.Floor((float)(simulationSystem.frameIndex - timeData.m_FirstFrame) / num) * num);
        }

        private static float GetSlowTimeFactor()
        {
            EnsureResolve();

            try
            {
                if (s_SettingField == null || s_SlowTimeFactorProperty == null)
                    return 1f;

                object setting = s_SettingField.GetValue(null);
                if (setting == null)
                    return 1f;

                object value = s_SlowTimeFactorProperty.GetValue(setting, null);
                if (value is float slowTimeFactor && slowTimeFactor > 0f)
                    return slowTimeFactor;
            }
            catch (Exception ex)
            {
                Mod.log.Warn($"RealisticTrips slow_time_factor lookup failed: {ex.Message}");
            }

            return 1f;
        }

        private static T CreateDelegate<T>(string methodName, Type[] parameterTypes) where T : class
        {
            MethodInfo method = s_BridgeType.GetMethod(methodName, BindingFlags.Public | BindingFlags.Static, null, parameterTypes, null);
            if (method == null)
                return null;

            return Delegate.CreateDelegate(typeof(T), method, false) as T;
        }

        private static T CreatePolicyDelegate<T>(string methodName, Type[] parameterTypes) where T : class
        {
            MethodInfo method = s_PolicyBridgeType.GetMethod(methodName, BindingFlags.Public | BindingFlags.Static, null, parameterTypes, null);
            if (method == null)
                return null;

            return Delegate.CreateDelegate(typeof(T), method, false) as T;
        }

        private static Type FindType(string fullName)
        {
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    Type type = assembly.GetType(fullName, false);
                    if (type != null)
                        return type;
                }
                catch
                {
                }
            }

            return null;
        }
    }
}
