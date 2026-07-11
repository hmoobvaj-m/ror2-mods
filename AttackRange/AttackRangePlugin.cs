using AttackRange.Content;
using AttackRange.Range;
using BepInEx;
using R2API;

namespace AttackRange
{
    [BepInDependency(ItemAPI.PluginGUID)]
    [BepInDependency(LanguageAPI.PluginGUID)]
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    public sealed class AttackRangePlugin : BaseUnityPlugin
    {
        public const string PluginGuid = "com.github.hmoobvaj-m.attackrange";
        public const string PluginName = "Attack Range";
        public const string PluginVersion = "0.1.0";

        private void Awake()
        {
            RangeExtenderItem.Initialize();
            BulletRangeHooks.Initialize();

            Logger.LogInfo($"{PluginName} {PluginVersion} loaded.");
        }

        private void OnDestroy()
        {
            BulletRangeHooks.Dispose();
        }
    }
}