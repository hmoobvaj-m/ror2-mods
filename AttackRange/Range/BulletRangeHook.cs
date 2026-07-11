using AttackRange.Content;
using RoR2;
using UnityEngine;

namespace AttackRange.Range
{
    public static class BulletRangeHooks
    {
        private static bool initialized;

        public static void Initialize()
        {
            if (initialized)
                return;
            

            On.RoR2.BulletAttack.Fire += BulletAttack_Fire;
            initialized = true;
        }

        public static void Dispose()
        {
            if (!initialized)
                return;
            

            On.RoR2.BulletAttack.Fire -= BulletAttack_Fire;
            initialized = false;
        }

        private static void BulletAttack_Fire(
            On.RoR2.BulletAttack.orig_Fire orig,
            BulletAttack self)
        {
            float originalMaxDistance = self.maxDistance;

            try
            {
                int itemCount = GetItemCount(self.owner);
                if (itemCount > 0)
                {
                    float rangeMultiplier = AttackRangeCalculator.GetMultiplier(itemCount);
                    self.maxDistance = originalMaxDistance * rangeMultiplier;
                }
                orig(self);
            }

            finally {  self.maxDistance = originalMaxDistance; }
        }

        private static int GetItemCount(GameObject owner)
        {
            if (!owner)
                return 0;
            
            if (RangeExtenderItem.ItemDef == null)
                return 0;

            CharacterBody ownerBody = owner.GetComponent<CharacterBody>();

            if (!ownerBody) { ownerBody = owner.GetComponentInParent<CharacterBody>(); }

            if (!ownerBody || !ownerBody.inventory)
                return 0;
            
            return ownerBody.inventory.GetItemCountEffective(RangeExtenderItem.ItemDef.itemIndex);
        }
    }
}