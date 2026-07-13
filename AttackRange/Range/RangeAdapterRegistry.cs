using System;

using AttackRange.Range.Adapters;

using BepInEx.Logging;

namespace AttackRange.Range
{
    public static class RangeAdapterRegistry
    {
        private static bool initialized;

        public static void Initialize(ManualLogSource logger)
        {
            if (initialized)
                return;

            if (logger == null) { throw new ArgumentNullException(nameof(logger)); }

            HuntressTrackerRangeAdapter.Initialize(logger);
            initialized = true;
            logger.LogInfo("Range adapters initialized.");
        }

        public static void Dispose()
        {
            if (!initialized)
                return;

            HuntressTrackerRangeAdapter.Dispose();
            initialized = false;
        }
    }
}