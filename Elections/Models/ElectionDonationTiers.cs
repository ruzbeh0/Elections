namespace Elections.Models
{
    internal readonly struct ElectionDonationTier
    {
        public ElectionDonationTier(int amount, float bonus)
        {
            Amount = amount;
            Bonus = bonus;
        }

        public int Amount { get; }
        public float Bonus { get; }
    }

    internal static class ElectionDonationTiers
    {
        public const int FixedDonationAmount = 1000000;
        public const float BonusPerDonation = 0.02f;

        private static readonly ElectionDonationTier[] s_Tiers =
        {
            new ElectionDonationTier(FixedDonationAmount, BonusPerDonation)
        };

        public static int Count => s_Tiers.Length;

        public static bool TryGet(int tierIndex, out ElectionDonationTier tier)
        {
            if (tierIndex >= 0 && tierIndex < s_Tiers.Length)
            {
                tier = s_Tiers[tierIndex];
                return true;
            }

            tier = default;
            return false;
        }

        public static float GetBonusForAmount(int amount)
        {
            int donationCount = amount / FixedDonationAmount;
            return donationCount * BonusPerDonation;
        }
    }
}
