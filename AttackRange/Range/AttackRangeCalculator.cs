using System;

namespace AttackRange.Range
{
    public static class AttackRangeCalculator
    {
        private const float FirstStackBonus = 0.25f;
        private const float AdditionalStackBonus = 0.15f;
        private const float MaximumBonus = 1.00f;

        public static float GetMultiplier(int itemCount)
        {
            if (itemCount <= 0)
                return 1f;
            

            float uncappedBonus = FirstStackBonus + ((itemCount - 1) * AdditionalStackBonus);

            float cappedBonus = Math.Min(uncappedBonus, MaximumBonus);

            return 1f + cappedBonus;
        }
    }
}