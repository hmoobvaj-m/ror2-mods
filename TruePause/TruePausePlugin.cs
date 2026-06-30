using BepInEx;
using R2API.Networking;
using R2API.Networking.Interfaces;
using RoR2;
using System.Reflection;
using UnityEngine;
using UnityEngine.Networking;

namespace TruePause
{
    [BepInPlugin(ModGuid, "True Pause V2", "1.0.0")]
    [BepInDependency(NetworkingAPI.PluginGUID)]
    public class TruePausePlugin : BaseUnityPlugin
    {
        internal const string OriginalModGuid = "com.github.mcmrarm.truepause";
        internal const string ModGuid = "com.github.hmoobvaj-m.truepausev2";

        private static readonly FieldInfo appPauseScreenInstanceField = typeof(RoR2Application).GetField("pauseScreenInstance", BindingFlags.NonPublic | BindingFlags.Instance);

        internal static TruePausePlugin Instance { get; private set; }

        private float oldTimeScale = 1f;
        private bool netPaused;

        public void Awake()
        {
            Instance = this;

            NetworkingAPI.RegisterMessageType<RequestPauseMessage>();
            NetworkingAPI.RegisterMessageType<SetPausedMessage>();

            On.RoR2.UI.PauseScreenController.OnEnable += PauseScreenController_OnEnable;
            On.RoR2.UI.PauseScreenController.OnDisable += PauseScreenController_OnDisable;
        }

        public void OnDestroy()
        {
            On.RoR2.UI.PauseScreenController.OnEnable -= PauseScreenController_OnEnable;
            On.RoR2.UI.PauseScreenController.OnDisable -= PauseScreenController_OnDisable;

            if (netPaused)
                SetPaused(false);

            if (Instance == this)
                Instance = null;
        }

        private void PauseScreenController_OnEnable(On.RoR2.UI.PauseScreenController.orig_OnEnable orig, RoR2.UI.PauseScreenController self)
        {
            orig(self);

            if (!netPaused)
                RequestPause(true);
        }

        private void PauseScreenController_OnDisable(On.RoR2.UI.PauseScreenController.orig_OnDisable orig, RoR2.UI.PauseScreenController self)
        {
            orig(self);

            if (netPaused)
                RequestPause(false);
        }

        private bool IsPauseScreenVisible()
        {
            var currentPauseScreen = (GameObject)appPauseScreenInstanceField.GetValue(RoR2Application.instance);

            return currentPauseScreen != null;
        }

        private void SetPauseScreenVisible(bool paused)
        {
            bool wasPaused = IsPauseScreenVisible();

            if (paused && !wasPaused)
            {
                GameObject pauseScreen = Instantiate(Resources.Load<GameObject>("Prefabs/UI/PauseScreen"), RoR2Application.instance.transform);
                appPauseScreenInstanceField.SetValue(RoR2Application.instance, pauseScreen);
            }

            else if (!paused && wasPaused)
            {
                var currentPauseScreen = (GameObject)appPauseScreenInstanceField.GetValue(RoR2Application.instance);

                Destroy(currentPauseScreen);
                appPauseScreenInstanceField.SetValue(RoR2Application.instance, null);
            }
        }

        private void RequestPause(bool paused)
        {
            if (NetworkServer.active)
            {
                BroadcastPaused(paused);
                return;
            }

            if (NetworkClient.active)
                new RequestPauseMessage(paused).Send(NetworkDestination.Server);
        }

        private void BroadcastPaused(bool paused)
        {
            SetPaused(paused);
            new SetPausedMessage(paused).Send(NetworkDestination.Clients);
        }

        internal static void ReceivePauseRequest(bool paused)
        {
            if (!NetworkServer.active)
                return;

            Instance?.BroadcastPaused(paused);
        }

        internal static void ReceivePaused(bool paused)
        {
            Instance?.SetPaused(paused);
        }

        private void SetPaused(bool paused)
        {
            if (netPaused == paused)
                return;

            netPaused = paused;
            SetPauseScreenVisible(paused);

            if (netPaused)
            {
                oldTimeScale = Time.timeScale;
                Time.timeScale = 0f;
            }

            else
                Time.timeScale = oldTimeScale;
            
        }
    }
}