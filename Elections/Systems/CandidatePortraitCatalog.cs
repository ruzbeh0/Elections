using Game.Citizens;
using System;
using System.Collections.Generic;
using System.IO;
using Unity.Entities;

namespace Elections.Systems
{
    internal static class CandidatePortraitCatalog
    {
        private const string FallbackSourceRoot = "coui://ui-mods/Portraits/";
        private const string DataUriPrefix = "data:image/jpeg;base64,";
        private const int SkinVariantCount = 5;
        private const int PortraitCount = 20;

        private static readonly Dictionary<string, string> s_DataUriByFileName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private static readonly HashSet<string> s_LoggedMissingFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public static void WarmupCache()
        {
            string portraitDirectory = GetPortraitDirectory();
            if (string.IsNullOrWhiteSpace(portraitDirectory) || !Directory.Exists(portraitDirectory))
                return;

            try
            {
                string[] files = Directory.GetFiles(portraitDirectory, "*.jpg", SearchOption.TopDirectoryOnly);
                for (int i = 0; i < files.Length; i++)
                {
                    string fileName = Path.GetFileName(files[i]);
                    if (!s_DataUriByFileName.ContainsKey(fileName))
                        TryCacheDataUri(fileName, files[i], out _);
                }
            }
            catch (Exception ex)
            {
                Mod.log.Warn($"Unable to warm Elections portrait cache: {ex.Message}");
            }
        }

        public static int PickPortraitIndex(Entity candidate, int salt)
        {
            int value = Math.Abs(candidate.Index * 397 ^ candidate.Version * 37 ^ salt);
            return value % PortraitCount;
        }

        public static int PickPortraitIndexFromKey(string key, int salt)
        {
            unchecked
            {
                int hash = 17;
                string value = key ?? string.Empty;
                for (int i = 0; i < value.Length; i++)
                    hash = hash * 31 + value[i];

                hash ^= salt;
                return (hash & int.MaxValue) % PortraitCount;
            }
        }

        public static string GetPortraitImageSource(EntityManager entityManager, Entity candidate, int portraitIndex)
        {
            string ageKey = portraitIndex < 10 ? "young" : "adult";
            string genderKey = portraitIndex % 10 < 5 ? "f" : "m";
            int skinIndex = portraitIndex % SkinVariantCount;

            if (candidate != Entity.Null &&
                entityManager.Exists(candidate) &&
                entityManager.HasComponent<Citizen>(candidate))
            {
                Citizen citizen = entityManager.GetComponentData<Citizen>(candidate);
                genderKey = (citizen.m_State & CitizenFlags.Male) != CitizenFlags.None ? "m" : "f";
                skinIndex = Math.Abs(citizen.m_PseudoRandom) % SkinVariantCount;

                int randomAgeVariant = Math.Abs((citizen.m_PseudoRandom >> 4) + portraitIndex) % 2;
                ageKey = randomAgeVariant == 0 ? "adult" : "young";
            }

            string fileName = $"candidate_{ageKey}_{genderKey}_s{skinIndex}.jpg";
            if (TryGetDataUri(fileName, out string dataUri))
                return dataUri;

            return $"{FallbackSourceRoot}{fileName}";
        }

        public static void ClearCache()
        {
            s_DataUriByFileName.Clear();
            s_LoggedMissingFiles.Clear();
        }

        private static bool TryGetDataUri(string fileName, out string dataUri)
        {
            if (s_DataUriByFileName.TryGetValue(fileName, out dataUri))
                return true;

            string portraitDirectory = GetPortraitDirectory();
            if (string.IsNullOrWhiteSpace(portraitDirectory))
            {
                dataUri = null;
                return false;
            }

            return TryCacheDataUri(fileName, Path.Combine(portraitDirectory, fileName), out dataUri);
        }

        private static bool TryCacheDataUri(string fileName, string filePath, out string dataUri)
        {
            if (!File.Exists(filePath))
            {
                if (s_LoggedMissingFiles.Add(fileName))
                    Mod.log.Warn($"Elections portrait file not found: {filePath}");

                dataUri = null;
                return false;
            }

            try
            {
                byte[] bytes = File.ReadAllBytes(filePath);
                dataUri = DataUriPrefix + Convert.ToBase64String(bytes);
                s_DataUriByFileName[fileName] = dataUri;
                return true;
            }
            catch (Exception ex)
            {
                if (s_LoggedMissingFiles.Add(fileName))
                    Mod.log.Warn($"Unable to read Elections portrait file {filePath}: {ex.Message}");

                dataUri = null;
                return false;
            }
        }

        private static string GetPortraitDirectory()
        {
            string modDirectory = Mod.ModDirectory;
            if (string.IsNullOrWhiteSpace(modDirectory))
                modDirectory = Path.GetDirectoryName(typeof(Mod).Assembly.Location);

            return string.IsNullOrWhiteSpace(modDirectory) ? null : Path.Combine(modDirectory, "Portraits");
        }
    }
}
