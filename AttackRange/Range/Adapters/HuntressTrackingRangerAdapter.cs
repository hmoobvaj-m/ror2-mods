using System;
using System.Collections.Generic;

using BepInEx.Logging;

using RoR2;

namespace AttackRange.Range.Adapters
{
    public static class HuntressTrackerRangeAdapter
    {
        private static readonly HashSet<string> LoggedConfigurations =
            new HashSet<string>(StringComparer.Ordinal);

        private static bool initialized;
        private static ManualLogSource logger;

        public static void Initialize(
            ManualLogSource pluginLogger)
        {
            if (initialized)
                return;

            if (pluginLogger == null) { throw new ArgumentNullException(nameof(pluginLogger)); }

            logger = pluginLogger;

            On.RoR2.HuntressTracker.FixedUpdate += HuntressTracker_FixedUpdate;

            initialized = true;

            logger.LogInfo(
                "HuntressTracker range adapter initialized.");
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

        private static void HuntressTracker_FixedUpdate(On.RoR2.HuntressTracker.orig_FixedUpdate orig, HuntressTracker self)
        {
            CharacterBody body = self.GetComponent<CharacterBody>();

            if (!body) { body = self.GetComponentInParent<CharacterBody>(); }

            if (!RangeExtenderService.TryGetRangeMultiplier(body, out int itemCount, out float rangeMultiplier))
            {
                orig(self);
                return;
            }

            float originalTrackingDistance = self.maxTrackingDistance;

            if (originalTrackingDistance <= 0f)
            {
                orig(self);
                return;
            }

            float modifiedTrackingDistance = originalTrackingDistance * rangeMultiplier;

            try
            {
                self.maxTrackingDistance = modifiedTrackingDistance;

                LogRangeModification(body, itemCount, originalTrackingDistance, rangeMultiplier, modifiedTrackingDistance);
                orig(self);
            }
            finally
            {
                self.maxTrackingDistance =
                    originalTrackingDistance;
            }
        }

        private static void LogRangeModification( CharacterBody body, int itemCount, float originalTrackingDistance, float rangeMultiplier, float modifiedTrackingDistance)
        {
            string bodyName = body ? body.name : "Unknown";

            string configurationKey = $"{bodyName}|" + $"{itemCount}|" + $"{originalTrackingDistance:R}|" + $"{modifiedTrackingDistance:R}";

            if (!LoggedConfigurations.Add(configurationKey))
                return;

            logger.LogInfo($"Target-acquisition range modified: " + $"body={bodyName}, " + $"stacks={itemCount}, " + $"original={originalTrackingDistance:0.###}, " + $"multiplier={rangeMultiplier:0.###}, " + $"modified={modifiedTrackingDistance:0.###}");
        }
    }
}