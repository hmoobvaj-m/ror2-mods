using System;

using AttackRange.Utilities;

using R2API;
using RoR2;

using UnityEngine;
using UnityEngine.AddressableAssets;

namespace AttackRange.Content
{
    public static class RangeExtenderItem
    {
        public const string InternalName = "HMOOBVAJM_ATTACKRANGE_RANGEEXTENDER";

        public const string NameToken = "HMOOBVAJM_ATTACKRANGE_RANGEEXTENDER_NAME";
        public const string PickupToken = "HMOOBVAJM_ATTACKRANGE_RANGEEXTENDER_PICKUP";
        public const string DescriptionToken = "HMOOBVAJM_ATTACKRANGE_RANGEEXTENDER_DESCRIPTION";
        public const string LoreToken = "HMOOBVAJM_ATTACKRANGE_RANGEEXTENDER_LORE";

        public static ItemDef ItemDef { get; private set; }

        public static void Initialize()
        {
            if (ItemDef != null)
                return;

            ItemDef = ScriptableObject.CreateInstance<ItemDef>();

            ItemDef.name = InternalName;
            ItemDef.nameToken = NameToken;
            ItemDef.pickupToken = PickupToken;
            ItemDef.descriptionToken = DescriptionToken;
            ItemDef.loreToken = LoreToken;

            ItemTierDef tier2Def = Addressables.LoadAssetAsync<ItemTierDef>("RoR2/Base/Common/Tier2Def.asset").WaitForCompletion();
            RoR2PrivateFieldAccess.SetItemTierDef(ItemDef, tier2Def);

            ItemDef.pickupIconSprite = Addressables .LoadAssetAsync<Sprite>("RoR2/Base/Common/MiscIcons/texMysteryIcon.png").WaitForCompletion();

            #pragma warning disable CS0618
            ItemDef.pickupModelPrefab = Addressables.LoadAssetAsync<GameObject>("RoR2/Base/Mystery/PickupMystery.prefab").WaitForCompletion();
            #pragma warning restore CS0618

            ItemDef.canRemove = true;
            ItemDef.hidden = false;
            ItemDef.tags = new[]
            {
                ItemTag.Utility
            };

            ItemDisplayRuleDict displayRules = new ItemDisplayRuleDict(null);
            CustomItem customItem = new CustomItem(ItemDef, displayRules);

            if (!ItemAPI.Add(customItem)) { throw new InvalidOperationException($"Failed to register item {InternalName}."); }
        }
    }
}