using Unity.Mathematics;

namespace Elections.Systems
{
    internal struct ElectionPollSummary
    {
        public int Total;
        public int PercentA;
        public int PercentB;
        public int PercentC;
        public int PercentD;
        public int PercentUndecided;
        public int MarginOfError;
        public int LeaderIndex;
        public bool WithinMargin;
        public string Label;
        public string Description;
    }

    internal static class ElectionPollUtility
    {
        public static ElectionPollSummary BuildSummary(
            int votesA,
            int votesB,
            int undecided,
            string nameA,
            string nameB)
        {
            return BuildSummary(votesA, votesB, 0, 0, undecided, 2, nameA, nameB, string.Empty, string.Empty);
        }

        public static ElectionPollSummary BuildSummary(
            int votesA,
            int votesB,
            int votesC,
            int votesD,
            int undecided,
            int candidateCount,
            string nameA,
            string nameB,
            string nameC,
            string nameD)
        {
            candidateCount = math.clamp(candidateCount, 2, 4);
            int total = math.max(0, votesA + votesB + (candidateCount > 2 ? votesC : 0) + (candidateCount > 3 ? votesD : 0) + undecided);
            int divisor = math.max(1, total);
            int percentA = total > 0 ? (int)math.round(votesA * 100f / divisor) : 0;
            int percentB = total > 0 ? (int)math.round(votesB * 100f / divisor) : 0;
            int percentC = total > 0 && candidateCount > 2 ? (int)math.round(votesC * 100f / divisor) : 0;
            int percentD = total > 0 && candidateCount > 3 ? (int)math.round(votesD * 100f / divisor) : 0;
            int percentUndecided = total > 0 ? (int)math.round(undecided * 100f / divisor) : 0;
            int marginOfError = total > 0 ? (int)math.clamp(math.ceil(98f / math.sqrt(total)), 1f, 50f) : 0;
            int leaderIndex = -1;
            int leaderPercent = -1;
            int secondPercent = -1;
            bool tieForLead = false;
            for (int i = 0; i < candidateCount; i++)
            {
                int percent = i == 0 ? percentA : i == 1 ? percentB : i == 2 ? percentC : percentD;
                if (percent > leaderPercent)
                {
                    secondPercent = leaderPercent;
                    leaderPercent = percent;
                    leaderIndex = i;
                    tieForLead = false;
                }
                else if (percent == leaderPercent)
                {
                    tieForLead = true;
                }
                else if (percent > secondPercent)
                {
                    secondPercent = percent;
                }
            }

            if (tieForLead)
                leaderIndex = -1;

            int margin = leaderIndex >= 0 ? math.max(0, leaderPercent - math.max(0, secondPercent)) : 0;
            bool withinMargin = total == 0 || leaderIndex < 0 || margin <= marginOfError;

            string leaderName = GetCandidateName(leaderIndex, nameA, nameB, nameC, nameD);
            string label;
            string description;
            if (total == 0)
            {
                label = ElectionLocalization.Translate("Model.Poll.NoSample.Label", "No poll sample");
                description = ElectionLocalization.Translate("Model.Poll.NoSample.Description", "No eligible residents were sampled.");
            }
            else if (withinMargin)
            {
                label = ElectionLocalization.Translate("Model.Poll.DeadHeat.Label", "Statistical dead heat");
                description = candidateCount == 2
                    ? ElectionLocalization.Format("Model.Poll.DeadHeat.TwoCandidateDescription", "{0} and {1} are within the +/-{2}% margin of error.", nameA, nameB, marginOfError)
                    : ElectionLocalization.Format("Model.Poll.DeadHeat.MultiCandidateDescription", "The leading candidates are within the +/-{0}% margin of error.", marginOfError);
            }
            else if (margin <= marginOfError + 4)
            {
                label = ElectionLocalization.Format("Model.Poll.NarrowEdge.Label", "{0} has a narrow edge", leaderName);
                description = ElectionLocalization.Format("Model.Poll.NarrowEdge.Description", "{0} leads, but the race is still close with a +/-{1}% margin of error.", leaderName, marginOfError);
            }
            else
            {
                label = ElectionLocalization.Format("Model.Poll.OutsideMargin.Label", "{0} leads outside the margin", leaderName);
                description = ElectionLocalization.Format("Model.Poll.OutsideMargin.Description", "{0} leads by {1} points, outside the +/-{2}% margin of error.", leaderName, margin, marginOfError);
            }

            return new ElectionPollSummary
            {
                Total = total,
                PercentA = percentA,
                PercentB = percentB,
                PercentC = percentC,
                PercentD = percentD,
                PercentUndecided = percentUndecided,
                MarginOfError = marginOfError,
                LeaderIndex = leaderIndex,
                WithinMargin = withinMargin,
                Label = label,
                Description = description
            };
        }

        private static string GetCandidateName(int index, string nameA, string nameB, string nameC, string nameD)
        {
            switch (index)
            {
                case 0:
                    return nameA;
                case 1:
                    return nameB;
                case 2:
                    return nameC;
                case 3:
                    return nameD;
                default:
                    return ElectionLocalization.Translate("Model.Poll.NoCandidate", "No candidate");
            }
        }
    }
}
