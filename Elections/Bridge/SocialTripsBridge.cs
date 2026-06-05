using System;
using System.Reflection;
using Unity.Entities;

namespace Elections.Bridge
{
    internal static class SocialTripsBridge
    {
        private delegate bool TryQueueElectionVoteChirpDelegate(Entity voter, Entity pollingPlace, string candidateName, int chosenCandidate, int electionDayKey);

        private static bool s_Resolved;
        private static Type s_BridgeType;
        private static TryQueueElectionVoteChirpDelegate s_TryQueueElectionVoteChirp;
        private static bool s_LoggedFailure;

        public static bool TryQueueElectionVoteChirp(Entity voter, Entity pollingPlace, string candidateName, int chosenCandidate, int electionDayKey)
        {
            EnsureResolve();
            if (s_TryQueueElectionVoteChirp == null)
                return false;

            try
            {
                return s_TryQueueElectionVoteChirp(voter, pollingPlace, candidateName ?? string.Empty, chosenCandidate, electionDayKey);
            }
            catch (Exception ex)
            {
                if (!s_LoggedFailure)
                {
                    s_LoggedFailure = true;
                    Mod.log.Warn($"SocialTrips election chirp bridge failed: {ex.Message}");
                }

                return false;
            }
        }

        private static void EnsureResolve()
        {
            if (s_Resolved && s_TryQueueElectionVoteChirp != null)
                return;

            s_Resolved = true;
            s_BridgeType = Type.GetType("SocialTrips.Bridge.SocialTripsMacroBridge, SocialTrips") ??
                           FindType("SocialTrips.Bridge.SocialTripsMacroBridge");
            if (s_BridgeType == null)
            {
                s_Resolved = false;
                return;
            }

            MethodInfo method = s_BridgeType.GetMethod(
                "TryQueueElectionVoteChirp",
                BindingFlags.Public | BindingFlags.Static,
                null,
                new[] { typeof(Entity), typeof(Entity), typeof(string), typeof(int), typeof(int) },
                null);
            s_TryQueueElectionVoteChirp = method == null
                ? null
                : Delegate.CreateDelegate(typeof(TryQueueElectionVoteChirpDelegate), method, false) as TryQueueElectionVoteChirpDelegate;

            if (s_TryQueueElectionVoteChirp == null)
                s_Resolved = false;
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
