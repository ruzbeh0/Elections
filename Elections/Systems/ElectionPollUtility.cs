using Unity.Mathematics;

namespace Elections.Systems
{
    internal struct ElectionPollSummary
    {
        public int Total;
        public int PercentA;
        public int PercentB;
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
            int total = math.max(0, votesA + votesB + undecided);
            int divisor = math.max(1, total);
            int percentA = total > 0 ? (int)math.round(votesA * 100f / divisor) : 0;
            int percentB = total > 0 ? (int)math.round(votesB * 100f / divisor) : 0;
            int percentUndecided = total > 0 ? (int)math.round(undecided * 100f / divisor) : 0;
            int marginOfError = total > 0 ? (int)math.clamp(math.ceil(98f / math.sqrt(total)), 1f, 50f) : 0;
            int margin = math.abs(percentA - percentB);
            int leaderIndex = percentA > percentB ? 0 : percentB > percentA ? 1 : -1;
            bool withinMargin = total == 0 || leaderIndex < 0 || margin <= marginOfError;

            string leaderName = leaderIndex == 0 ? nameA : nameB;
            string label;
            string description;
            if (total == 0)
            {
                label = "No poll sample";
                description = "No eligible residents were sampled.";
            }
            else if (withinMargin)
            {
                label = "Statistical dead heat";
                description = $"{nameA} and {nameB} are within the +/-{marginOfError}% margin of error.";
            }
            else if (margin <= marginOfError + 4)
            {
                label = $"{leaderName} has a narrow edge";
                description = $"{leaderName} leads, but the race is still close with a +/-{marginOfError}% margin of error.";
            }
            else
            {
                label = $"{leaderName} leads outside the margin";
                description = $"{leaderName} leads by {margin} points, outside the +/-{marginOfError}% margin of error.";
            }

            return new ElectionPollSummary
            {
                Total = total,
                PercentA = percentA,
                PercentB = percentB,
                PercentUndecided = percentUndecided,
                MarginOfError = marginOfError,
                LeaderIndex = leaderIndex,
                WithinMargin = withinMargin,
                Label = label,
                Description = description
            };
        }
    }
}
