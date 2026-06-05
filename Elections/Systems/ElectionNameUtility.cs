using Game.SceneFlow;
using Game.UI;
using System;
using System.Reflection;
using Unity.Entities;

namespace Elections.Systems
{
    internal static class ElectionNameUtility
    {
        private static MethodInfo s_GetCitizenName;
        private static FieldInfo s_NameTypeField;
        private static FieldInfo s_NameIdField;
        private static FieldInfo s_NameArgsField;

        public static string GetCitizenFullName(NameSystem nameSystem, EntityManager entityManager, Entity entity, string fallback)
        {
            if (nameSystem == null || entity == Entity.Null || !entityManager.Exists(entity))
                return fallback;

            try
            {
                if (nameSystem.TryGetCustomName(entity, out string customName))
                    return Sanitize(customName, fallback);
            }
            catch
            {
            }

            try
            {
                EnsureReflection(nameSystem);
                object name = s_GetCitizenName?.Invoke(nameSystem, new object[] { entity });
                string fullName = RenderName(name);
                if (!string.IsNullOrWhiteSpace(fullName))
                    return Sanitize(fullName, fallback);
            }
            catch
            {
            }

            try
            {
                return Sanitize(nameSystem.GetRenderedLabelName(entity), fallback);
            }
            catch
            {
                return fallback;
            }
        }

        private static void EnsureReflection(NameSystem nameSystem)
        {
            if (s_GetCitizenName != null && s_NameTypeField != null && s_NameIdField != null && s_NameArgsField != null)
                return;

            Type nameSystemType = nameSystem.GetType();
            s_GetCitizenName = nameSystemType.GetMethod("GetCitizenName", BindingFlags.Instance | BindingFlags.NonPublic);
            Type nameType = nameSystemType.GetNestedType("Name", BindingFlags.Public | BindingFlags.NonPublic);
            s_NameTypeField = nameType?.GetField("m_NameType", BindingFlags.Instance | BindingFlags.NonPublic);
            s_NameIdField = nameType?.GetField("m_NameID", BindingFlags.Instance | BindingFlags.NonPublic);
            s_NameArgsField = nameType?.GetField("m_NameArgs", BindingFlags.Instance | BindingFlags.NonPublic);
        }

        private static string RenderName(object name)
        {
            if (name == null || s_NameTypeField == null || s_NameIdField == null)
                return null;

            int nameType = Convert.ToInt32(s_NameTypeField.GetValue(name));
            string nameId = s_NameIdField.GetValue(name) as string;

            if (nameType == 0)
                return nameId;

            if (nameType == 1)
                return Localize(nameId);

            if (nameType != 2)
                return null;

            string[] args = s_NameArgsField?.GetValue(name) as string[];
            string format = Localize(nameId);
            string firstName = null;
            string lastName = null;

            if (args != null)
            {
                for (int i = 0; i + 1 < args.Length; i += 2)
                {
                    string key = args[i] ?? string.Empty;
                    string value = Localize(args[i + 1]);

                    if (key.Equals("FIRST_NAME", StringComparison.OrdinalIgnoreCase))
                        firstName = value;
                    else if (key.Equals("LAST_NAME", StringComparison.OrdinalIgnoreCase))
                        lastName = value;

                    format = ReplaceToken(format, key, value);
                }
            }

            if (!string.IsNullOrWhiteSpace(firstName) || !string.IsNullOrWhiteSpace(lastName))
            {
                if (string.IsNullOrWhiteSpace(format) ||
                    format == nameId ||
                    format.IndexOf("FIRST_NAME", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    format.IndexOf("LAST_NAME", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return $"{firstName} {lastName}".Trim();
                }
            }

            return format;
        }

        private static string ReplaceToken(string format, string key, string value)
        {
            if (string.IsNullOrEmpty(format) || string.IsNullOrEmpty(key))
                return format;

            return format
                .Replace("{" + key + "}", value ?? string.Empty)
                .Replace("{" + key.ToLowerInvariant() + "}", value ?? string.Empty)
                .Replace("{" + key.ToUpperInvariant() + "}", value ?? string.Empty);
        }

        private static string Localize(string id)
        {
            if (string.IsNullOrWhiteSpace(id))
                return string.Empty;

            try
            {
                if (GameManager.instance?.localizationManager?.activeDictionary != null &&
                    GameManager.instance.localizationManager.activeDictionary.TryGetValue(id, out string value) &&
                    !string.IsNullOrWhiteSpace(value))
                {
                    return value;
                }
            }
            catch
            {
            }

            return id;
        }

        private static string Sanitize(string value, string fallback)
        {
            if (string.IsNullOrWhiteSpace(value))
                return fallback;

            string sanitized = value.Trim();
            return string.IsNullOrWhiteSpace(sanitized) || sanitized == "Unknown" ? fallback : sanitized;
        }
    }
}
