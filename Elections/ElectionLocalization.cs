using Game.SceneFlow;
using System;

namespace Elections
{
    internal static class ElectionLocalization
    {
        public const string Prefix = "Elections.";

        public static string ID(string key)
        {
            return Prefix + key;
        }

        public static string Translate(string key, string fallback)
        {
            string id = ID(key);
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

            return fallback ?? string.Empty;
        }

        public static string Format(string key, string fallback, params object[] args)
        {
            string format = Translate(key, fallback);
            try
            {
                return string.Format(format, args);
            }
            catch (FormatException)
            {
                try
                {
                    return string.Format(fallback ?? string.Empty, args);
                }
                catch (FormatException)
                {
                    return fallback ?? string.Empty;
                }
            }
        }
    }
}
