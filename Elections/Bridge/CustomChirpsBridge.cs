using System;
using System.Reflection;
using Unity.Entities;

namespace Elections.Bridge
{
    internal enum DepartmentAccountBridge
    {
        Electricity,
        FireRescue,
        Roads,
        Water,
        Communications,
        Police,
        PropertyAssessmentOffice,
        Post,
        BusinessNews,
        CensusBureau,
        ParkAndRec,
        EnvironmentalProtectionAgency,
        Healthcare,
        LivingStandardsAssociation,
        Garbage,
        TourismBoard,
        Transportation,
        Education
    }

    internal static class CustomChirpsBridge
    {
        private static bool s_Resolved;
        private static Type s_ApiType;
        private static Type s_DepartmentEnumType;
        private static MethodInfo s_PostChirp;
        private static MethodInfo s_PostChirpWith2Targets;
        private static MethodInfo s_PostChirpWith3Targets;
        private static MethodInfo s_PostChirpFromEntity;
        private static MethodInfo s_PostChirpFromEntityWith2Targets;
        private static MethodInfo s_PostChirpFromEntityWith3Targets;
        private static MethodInfo s_PostLargeChirpFromEntityWithPortraitImage;
        private static MethodInfo s_PostLargeChirpFromEntityWithPortraitImage2Targets;
        private static MethodInfo s_PostLargeChirpFromEntityWithPortraitImage3Targets;
        private static bool s_LoggedFailure;

        public static bool IsAvailable
        {
            get
            {
                EnsureResolve();
                return s_ApiType != null && s_DepartmentEnumType != null && s_PostChirp != null;
            }
        }

        public static bool PostChirp(string text, DepartmentAccountBridge department, Entity targetEntity, string senderName)
        {
            EnsureResolve();
            if (s_PostChirp == null || s_DepartmentEnumType == null)
                return false;

            try
            {
                object realDepartment = Enum.Parse(s_DepartmentEnumType, department.ToString(), false);
                s_PostChirp.Invoke(null, new object[] { text ?? string.Empty, realDepartment, targetEntity, senderName });
                return true;
            }
            catch (Exception ex)
            {
                if (!s_LoggedFailure)
                {
                    s_LoggedFailure = true;
                    Mod.log.Warn($"CustomChirps invocation failed: {ex.Message}");
                }

                return false;
            }
        }

        public static bool PostChirpWith2Targets(string text, DepartmentAccountBridge department, Entity targetEntity, Entity targetEntity2, string senderName)
        {
            EnsureResolve();
            if (s_PostChirpWith2Targets == null || s_DepartmentEnumType == null)
                return PostChirp(text, department, targetEntity, senderName);

            try
            {
                object realDepartment = Enum.Parse(s_DepartmentEnumType, department.ToString(), false);
                s_PostChirpWith2Targets.Invoke(null, new object[] { text ?? string.Empty, realDepartment, targetEntity, targetEntity2, senderName });
                return true;
            }
            catch (Exception ex)
            {
                LogFailureOnce(ex);
                return false;
            }
        }

        public static bool SupportsChirpWith2Targets()
        {
            EnsureResolve();
            return s_PostChirpWith2Targets != null && s_DepartmentEnumType != null;
        }

        public static bool PostChirpWith3Targets(string text, DepartmentAccountBridge department, Entity targetEntity, Entity targetEntity2, Entity targetEntity3, string senderName)
        {
            EnsureResolve();
            if (s_PostChirpWith3Targets == null || s_DepartmentEnumType == null)
                return PostChirpWith2Targets(text, department, targetEntity, targetEntity2, senderName);

            try
            {
                object realDepartment = Enum.Parse(s_DepartmentEnumType, department.ToString(), false);
                s_PostChirpWith3Targets.Invoke(null, new object[] { text ?? string.Empty, realDepartment, targetEntity, targetEntity2, targetEntity3, senderName });
                return true;
            }
            catch (Exception ex)
            {
                LogFailureOnce(ex);
                return false;
            }
        }

        public static bool SupportsChirpWith3Targets()
        {
            EnsureResolve();
            return s_PostChirpWith3Targets != null && s_DepartmentEnumType != null;
        }

        public static bool PostChirpFromEntity(string text, Entity citizenSenderEntity, Entity targetEntity, string senderName)
        {
            EnsureResolve();
            if (s_PostChirpFromEntity == null)
                return false;

            try
            {
                s_PostChirpFromEntity.Invoke(null, new object[] { text ?? string.Empty, citizenSenderEntity, targetEntity, senderName });
                return true;
            }
            catch (Exception ex)
            {
                LogFailureOnce(ex);
                return false;
            }
        }

        public static bool PostChirpFromEntityWith2Targets(string text, Entity citizenSenderEntity, Entity targetEntity, Entity targetEntity2, string senderName)
        {
            EnsureResolve();
            if (s_PostChirpFromEntityWith2Targets == null)
                return PostChirpFromEntity(text, citizenSenderEntity, targetEntity, senderName);

            try
            {
                s_PostChirpFromEntityWith2Targets.Invoke(null, new object[] { text ?? string.Empty, citizenSenderEntity, targetEntity, targetEntity2, senderName });
                return true;
            }
            catch (Exception ex)
            {
                LogFailureOnce(ex);
                return false;
            }
        }

        public static bool PostChirpFromEntityWith3Targets(string text, Entity citizenSenderEntity, Entity targetEntity, Entity targetEntity2, Entity targetEntity3, string senderName)
        {
            EnsureResolve();
            if (s_PostChirpFromEntityWith3Targets == null)
                return PostChirpFromEntityWith2Targets(text, citizenSenderEntity, targetEntity, targetEntity2, senderName);

            try
            {
                s_PostChirpFromEntityWith3Targets.Invoke(null, new object[] { text ?? string.Empty, citizenSenderEntity, targetEntity, targetEntity2, targetEntity3, senderName });
                return true;
            }
            catch (Exception ex)
            {
                LogFailureOnce(ex);
                return false;
            }
        }

        public static bool PostLargeChirpFromEntityWithPortraitImage(string text, Entity citizenSenderEntity, Entity targetEntity, string portraitImageSource, string senderName)
        {
            EnsureResolve();
            if (s_PostLargeChirpFromEntityWithPortraitImage == null)
                return PostChirpFromEntity(text, citizenSenderEntity, targetEntity, senderName);

            try
            {
                s_PostLargeChirpFromEntityWithPortraitImage.Invoke(null, new object[] { text ?? string.Empty, citizenSenderEntity, targetEntity, portraitImageSource ?? string.Empty, senderName });
                return true;
            }
            catch (Exception ex)
            {
                LogFailureOnce(ex);
                return false;
            }
        }

        public static bool PostLargeChirpFromEntityWithPortraitImage(string text, Entity citizenSenderEntity, Entity targetEntity, Entity targetEntity2, string portraitImageSource, string senderName)
        {
            EnsureResolve();
            if (s_PostLargeChirpFromEntityWithPortraitImage2Targets == null)
                return PostLargeChirpFromEntityWithPortraitImage(text, citizenSenderEntity, targetEntity, portraitImageSource, senderName);

            try
            {
                s_PostLargeChirpFromEntityWithPortraitImage2Targets.Invoke(null, new object[] { text ?? string.Empty, citizenSenderEntity, targetEntity, targetEntity2, portraitImageSource ?? string.Empty, senderName });
                return true;
            }
            catch (Exception ex)
            {
                LogFailureOnce(ex);
                return false;
            }
        }

        public static bool PostLargeChirpFromEntityWithPortraitImage(string text, Entity citizenSenderEntity, Entity targetEntity, Entity targetEntity2, Entity targetEntity3, string portraitImageSource, string senderName)
        {
            EnsureResolve();
            if (s_PostLargeChirpFromEntityWithPortraitImage3Targets == null)
                return PostLargeChirpFromEntityWithPortraitImage(text, citizenSenderEntity, targetEntity, targetEntity2, portraitImageSource, senderName);

            try
            {
                s_PostLargeChirpFromEntityWithPortraitImage3Targets.Invoke(null, new object[] { text ?? string.Empty, citizenSenderEntity, targetEntity, targetEntity2, targetEntity3, portraitImageSource ?? string.Empty, senderName });
                return true;
            }
            catch (Exception ex)
            {
                LogFailureOnce(ex);
                return false;
            }
        }

        private static void EnsureResolve()
        {
            if (s_Resolved)
                return;

            s_Resolved = true;
            s_ApiType = Type.GetType("CustomChirps.Systems.CustomChirpApiSystem, CustomChirps") ??
                        FindType("CustomChirps.Systems.CustomChirpApiSystem");
            s_DepartmentEnumType = Type.GetType("CustomChirps.Systems.DepartmentAccount, CustomChirps") ??
                                   FindType("CustomChirps.Systems.DepartmentAccount");

            if (s_ApiType != null && s_DepartmentEnumType != null)
            {
                s_PostChirp = s_ApiType.GetMethod(
                    "PostChirp",
                    BindingFlags.Public | BindingFlags.Static,
                    null,
                    new[] { typeof(string), s_DepartmentEnumType, typeof(Entity), typeof(string) },
                    null);
                s_PostChirpWith2Targets = s_ApiType.GetMethod(
                    "PostChirpWith2Targets",
                    BindingFlags.Public | BindingFlags.Static,
                    null,
                    new[] { typeof(string), s_DepartmentEnumType, typeof(Entity), typeof(Entity), typeof(string) },
                    null);
                s_PostChirpWith3Targets = s_ApiType.GetMethod(
                    "PostChirpWith3Targets",
                    BindingFlags.Public | BindingFlags.Static,
                    null,
                    new[] { typeof(string), s_DepartmentEnumType, typeof(Entity), typeof(Entity), typeof(Entity), typeof(string) },
                    null);
            }

            if (s_ApiType != null)
            {
                s_PostChirpFromEntity = s_ApiType.GetMethod(
                    "PostChirpFromEntity",
                    BindingFlags.Public | BindingFlags.Static,
                    null,
                    new[] { typeof(string), typeof(Entity), typeof(Entity), typeof(string) },
                    null);
                s_PostChirpFromEntityWith2Targets = s_ApiType.GetMethod(
                    "PostChirpFromEntityWith2Targets",
                    BindingFlags.Public | BindingFlags.Static,
                    null,
                    new[] { typeof(string), typeof(Entity), typeof(Entity), typeof(Entity), typeof(string) },
                    null);
                s_PostChirpFromEntityWith3Targets = s_ApiType.GetMethod(
                    "PostChirpFromEntityWith3Targets",
                    BindingFlags.Public | BindingFlags.Static,
                    null,
                    new[] { typeof(string), typeof(Entity), typeof(Entity), typeof(Entity), typeof(Entity), typeof(string) },
                    null);
                s_PostLargeChirpFromEntityWithPortraitImage = s_ApiType.GetMethod(
                    "PostLargeChirpFromEntityWithPortraitImage",
                    BindingFlags.Public | BindingFlags.Static,
                    null,
                    new[] { typeof(string), typeof(Entity), typeof(Entity), typeof(string), typeof(string) },
                    null);
                s_PostLargeChirpFromEntityWithPortraitImage2Targets = s_ApiType.GetMethod(
                    "PostLargeChirpFromEntityWithPortraitImage",
                    BindingFlags.Public | BindingFlags.Static,
                    null,
                    new[] { typeof(string), typeof(Entity), typeof(Entity), typeof(Entity), typeof(string), typeof(string) },
                    null);
                s_PostLargeChirpFromEntityWithPortraitImage3Targets = s_ApiType.GetMethod(
                    "PostLargeChirpFromEntityWithPortraitImage3Targets",
                    BindingFlags.Public | BindingFlags.Static,
                    null,
                    new[] { typeof(string), typeof(Entity), typeof(Entity), typeof(Entity), typeof(Entity), typeof(string), typeof(string) },
                    null);
            }
        }

        private static void LogFailureOnce(Exception ex)
        {
            if (s_LoggedFailure)
                return;

            s_LoggedFailure = true;
            Mod.log.Warn($"CustomChirps invocation failed: {ex.Message}");
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
