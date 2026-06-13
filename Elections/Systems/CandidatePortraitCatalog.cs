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
        private const int BasePortraitCount = 20;
        private const int ExtraPortraitCount = 40;
        private const int PortraitCount = BasePortraitCount + ExtraPortraitCount;
        private static readonly bool[] s_ExtraPortraitIsMale =
        {
            true, false, true, false, true, false, true, false, true, false,
            true, false, false, true, false, true, false, true, false, true,
            true, false, true, true, true, false, true, false, false, true,
            true, false, true, false, false, false, true, false, true, false
        };

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

        public static int PickDifferentPortraitIndex(
            EntityManager entityManager,
            Entity candidate,
            int salt,
            Entity excludedCandidateA,
            int excludedPortraitIndexA,
            Entity excludedCandidateB,
            int excludedPortraitIndexB)
        {
            int startIndex = PickPortraitIndex(candidate, salt);
            for (int offset = 0; offset < PortraitCount; offset++)
            {
                int portraitIndex = NormalizePortraitIndex(startIndex + offset);
                if (!HasSamePortrait(entityManager, candidate, portraitIndex, excludedCandidateA, excludedPortraitIndexA) &&
                    !HasSamePortrait(entityManager, candidate, portraitIndex, excludedCandidateB, excludedPortraitIndexB))
                {
                    return portraitIndex;
                }
            }

            return startIndex;
        }

        public static string GetPortraitImageSource(EntityManager entityManager, Entity candidate, int portraitIndex)
        {
            return GetPortraitImageSource(GetPortraitFileName(entityManager, candidate, portraitIndex));
        }

        public static string GetPortraitFileName(EntityManager entityManager, Entity candidate, int portraitIndex)
        {
            portraitIndex = NormalizePortraitIndex(portraitIndex);
            if (portraitIndex >= BasePortraitCount)
            {
                int extraIndex = portraitIndex - BasePortraitCount;
                if (TryGetCitizenIsMale(entityManager, candidate, out bool isMale))
                    extraIndex = PickGenderMatchedExtraPortraitIndex(extraIndex, isMale);

                return $"candidate_extra_{extraIndex:00}.jpg";
            }

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

            return $"candidate_{ageKey}_{genderKey}_s{skinIndex}.jpg";
        }

        private static bool TryGetCitizenIsMale(EntityManager entityManager, Entity candidate, out bool isMale)
        {
            isMale = false;
            if (candidate == Entity.Null ||
                !entityManager.Exists(candidate) ||
                !entityManager.HasComponent<Citizen>(candidate))
            {
                return false;
            }

            Citizen citizen = entityManager.GetComponentData<Citizen>(candidate);
            isMale = (citizen.m_State & CitizenFlags.Male) != CitizenFlags.None;
            return true;
        }

        private static int PickGenderMatchedExtraPortraitIndex(int extraIndex, bool isMale)
        {
            extraIndex = NormalizeExtraPortraitIndex(extraIndex);
            for (int offset = 0; offset < ExtraPortraitCount; offset++)
            {
                int candidateIndex = (extraIndex + offset) % ExtraPortraitCount;
                if (candidateIndex < s_ExtraPortraitIsMale.Length &&
                    s_ExtraPortraitIsMale[candidateIndex] == isMale)
                {
                    return candidateIndex;
                }
            }

            return extraIndex;
        }

        public static bool HasSamePortrait(
            EntityManager entityManager,
            Entity candidate,
            int portraitIndex,
            Entity excludedCandidate,
            int excludedPortraitIndex)
        {
            if (excludedCandidate == Entity.Null || excludedPortraitIndex < 0)
                return false;

            string fileName = GetPortraitFileName(entityManager, candidate, portraitIndex);
            string excludedFileName = GetPortraitFileName(entityManager, excludedCandidate, excludedPortraitIndex);
            return string.Equals(fileName, excludedFileName, StringComparison.OrdinalIgnoreCase);
        }

        public static int NormalizePortraitIndex(int portraitIndex)
        {
            return (portraitIndex & int.MaxValue) % PortraitCount;
        }

        private static int NormalizeExtraPortraitIndex(int extraIndex)
        {
            return (extraIndex & int.MaxValue) % ExtraPortraitCount;
        }

        private static string GetPortraitImageSource(string fileName)
        {
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
