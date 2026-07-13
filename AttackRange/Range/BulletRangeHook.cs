using System;
using System.Collections.Generic;

using AttackRange.Content;

using BepInEx.Logging;

using RoR2;

namespace AttackRange.Range
{
    public static class HuntressTrackingRangeHooks
    {
        private static readonly HashSet<string> LoggedConfigurations = new HashSet<string>();

        private static bool initialized;
        private static ManualLogSource logger;

        public static void Initialize(ManualLogSource pluginLogger)
        {
            if (initialized)
                return;

            if (pluginLogger == null) { throw new ArgumentNullException(nameof(pluginLogger)); }

            logger = pluginLogger;

            On.RoR2.HuntressTracker.FixedUpdate += HuntressTracker_FixedUpdate;
            initialized = true;

            logger.LogInfo("Huntress tracking range hook initialized.");
        }

        public static void Dispose()
        {
            if (!initialized)
                return;

            On.RoR2.HuntressTracker.FixedUpdate -= HuntressTracker_FixedUpdate;

            LoggedConfigurations.Clear();
            logger = null;
            initialized = false;
        }

        private static void HuntressTracker_FixedUpdate(
            On.RoR2.HuntressTracker.orig_FixedUpdate orig,
            HuntressTracker self)
        {
            CharacterBody body = self.GetComponent<CharacterBody>();

            if (!IsSupportedHuntress(body))
            {
                orig(self);
                return;
            }

            int itemCount = GetItemCount(body);

            if (itemCount <= 0)
            {
                orig(self);
                return;
            }

            float originalTrackingDistance = self.maxTrackingDistance;
            float rangeMultiplier = AttackRangeCalculator.GetMultiplier(itemCount);
            float modifiedTrackingDistance = originalTrackingDistance * rangeMultiplier;

            try
            {
                self.maxTrackingDistance = modifiedTrackingDistance;
                LogTrackingRangeModification(body, itemCount, originalTrackingDistance, rangeMultiplier, modifiedTrackingDistance);
                orig(self);
            }

            finally
            {
                self.maxTrackingDistance = originalTrackingDistance;
            }
        }

        private static bool IsSupportedHuntress(CharacterBody body)
        {
            if (!body)
                return false;

            BodyIndex huntressBodyIndex = BodyCatalog.FindBodyIndex("HuntressBody");

            return huntressBodyIndex != BodyIndex.None && body.bodyIndex == huntressBodyIndex;
        }

        private static int GetItemCount(CharacterBody body)
        {
            if (!body || !body.inventory)
                return 0;

            if (RangeExtenderItem.ItemDef == null)
                return 0;

            if (RangeExtenderItem.ItemDef.itemIndex == ItemIndex.None)
                return 0;

            return body.inventory.GetItemCountEffective(RangeExtenderItem.ItemDef.itemIndex);
        }

        private static void LogTrackingRangeModification(
            CharacterBody body,
            int itemCount,
            float originalTrackingDistance,
            float rangeMultiplier,
            float modifiedTrackingDistance)
        {
            string bodyName = body ? body.name : "Unknown";

            string configurationKey = $"{bodyName}|" + $"{itemCount}|" + $"{originalTrackingDistance:R}|" + $"{modifiedTrackingDistance:R}";

            if (!LoggedConfigurations.Add(configurationKey))
                return;

            logger.LogInfo($"Huntress tracking range modified: " + $"body={bodyName}, " + $"stacks={itemCount}, " + $"original={originalTrackingDistance:0.###}, " + $"multiplier={rangeMultiplier:0.###}, " + $"modified={modifiedTrackingDistance:0.###}");
        }
    }
}