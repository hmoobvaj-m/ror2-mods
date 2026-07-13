namespace AttackRange.Range
{
    public static class AttackRangeCalculator
    {
        private const float RangeBonusPerStack = 0.15f;

        public static float GetMultiplier(int itemCount)
        {
            if (itemCount <= 0)
                return 1f;

            return 1f + (itemCount * RangeBonusPerStack);
        }
    }
}