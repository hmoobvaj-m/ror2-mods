using AttackRange.Content;

using RoR2;

namespace AttackRange.Range
{
    public static class RangeExtenderService
    {
        public static bool TryGetRangeMultiplier(
            CharacterBody body,
            out int itemCount,
            out float rangeMultiplier)
        {
            itemCount = 0;
            rangeMultiplier = 1f;

            if (!body || !body.inventory)
                return false;

            ItemDef itemDef = RangeExtenderItem.ItemDef;

            if (itemDef == null)
                return false;

            if (itemDef.itemIndex == ItemIndex.None)
                return false;

            itemCount = body.inventory.GetItemCountEffective(
                itemDef.itemIndex);

            if (itemCount <= 0)
                return false;

            rangeMultiplier =
                AttackRangeCalculator.GetMultiplier(itemCount);

            return true;
        }
    }
}