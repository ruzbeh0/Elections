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

        public static int Count => 1;

        public static bool TryGet(int tierIndex, out ElectionDonationTier tier)
        {
            return TryGet(tierIndex, FixedDonationAmount, out tier);
        }

        public static bool TryGet(int tierIndex, int donationAmount, out ElectionDonationTier tier)
        {
            if (tierIndex >= 0 && tierIndex < Count)
            {
                tier = new ElectionDonationTier(NormalizeDonationAmount(donationAmount), BonusPerDonation);
                return true;
            }

            tier = default;
            return false;
        }

        public static float GetBonusForAmount(int amount)
        {
            return GetBonusForAmount(amount, FixedDonationAmount);
        }

        public static float GetBonusForAmount(int amount, int donationAmount)
        {
            int donationCount = amount / NormalizeDonationAmount(donationAmount);
            return donationCount * BonusPerDonation;
        }

        public static int NormalizeDonationAmount(int donationAmount)
        {
            return donationAmount > 0 ? donationAmount : FixedDonationAmount;
        }
    }
}
