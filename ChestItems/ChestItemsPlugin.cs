using BepInEx;
using BepInEx.Logging;
using LeTai.Asset.TranslucentImage;
using R2API.Networking;
using R2API.Networking.Interfaces;
using RoR2;
using RoR2.UI;
using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;
using UnityEngine.Events;

namespace ChestItems {

    [BepInPlugin(ModGuid, "Chest Item Picker V2", "1.0.0")]
    [BepInDependency(NetworkingAPI.PluginGUID)]
    [BepInIncompatibility(OriginalModGuid)]    
    public class ChestItemsPlugin : BaseUnityPlugin {

        private const string OriginalModGuid = "com.github.mcmrarm.chestitempicker";
        private const string ModGuid = "com.github.hmoobvaj-m.chestitempickerv2";
        private RoR2PrivateFieldAccess privateFieldAccess;

        private const float PendingPickerRequestTimeoutSeconds = 30f;

        private readonly Dictionary<NetworkInstanceId, PendingPickerRequest> pendingPickerRequests = new Dictionary<NetworkInstanceId, PendingPickerRequest>();

        private sealed class PendingPickerRequest 
        {
            internal PendingPickerRequest(NetworkInstanceId targetId, string requestToken, PickupIndex generatedPickup, float createdAt) 
            {
                TargetId = targetId;
                RequestToken = requestToken;
                GeneratedPickup = generatedPickup;
                CreatedAt = createdAt;
            }

            internal NetworkInstanceId TargetId { get; }
            internal string RequestToken { get; }
            internal PickupIndex GeneratedPickup { get; }
            internal float CreatedAt { get; }
        }

        private void Awake() 
        {
            Instance = this;
            NetworkingAPI.RegisterMessageType<ShowItemPickerMessage>();
            NetworkingAPI.RegisterMessageType<ItemPickedMessage>();
            Logger.LogInfo("Chest Item Picker V2 loaded.");
        }

        private void OnDestroy() 
        {
            if (Instance == this)
                Instance = null;
        }

        public void Start() {
            privateFieldAccess = new RoR2PrivateFieldAccess(Logger);
            if (!privateFieldAccess.IsAvailable) 
            {
                Logger.LogError("Chest Item Picker is disabled because required RoR2 private fields could not be resolved. This usually means the game updated and the mod needs a compatibility update.");
                enabled = false;
                return;
            }

            On.RoR2.ChestBehavior.Start += (orig, self) => {
                orig(self);

                var purchaseInteraction = self.GetComponent<PurchaseInteraction>();
                DisablePersistentListener(purchaseInteraction.onPurchase, self, "ItemDrop");
                DisablePersistentListener(purchaseInteraction.onPurchase, self, "Open");
                purchaseInteraction.onPurchase.AddListener((v) => {
                    if (!privateFieldAccess.TryGetChestDropPickup(self, out PickupIndex generatedPickup) || !HandlePurchaseInteraction(v, self, generatedPickup)) {
                        self.Open();
                    }
                });
            };
            On.RoR2.ShopTerminalBehavior.Start += (orig, self) => {
                orig(self);

                if (!self.Networkhidden) 
                    return;
                

                if (!privateFieldAccess.TryGetShopTerminalPickupIndex(self, out PickupIndex generatedPickup)) 
                    return;
                

                var purchaseInteraction = self.GetComponent<PurchaseInteraction>();
                DisablePersistentListener(purchaseInteraction.onPurchase, self, "DropPickup");
                DisablePersistentListener(purchaseInteraction.onPurchase, self, "SetNoPickup");
                purchaseInteraction.onPurchase.AddListener((v) => {
                    if (!HandlePurchaseInteraction(v, self, generatedPickup)) {
                        self.DropPickup();
                        self.SetNoPickup();
                    }
                });
            };
            On.RoR2.MultiShopController.CreateTerminals += (orig, self) => {
                orig(self);
                HandlePostCreateMultiShopTerminals(self);
            };
        }

        private static int FindPersistentListener(UnityEventBase ev, UnityEngine.Object target, string methodName) 
        {
            for (int i = 0; i < ev.GetPersistentEventCount(); i++) 
            {
                if (ev.GetPersistentTarget(i) == target && ev.GetPersistentMethodName(i) == methodName)
                    return i;
            }
            return -1;
        }

        private static void DisablePersistentListener(UnityEventBase ev, UnityEngine.Object target, string methodName) 
        {
            int index = FindPersistentListener(ev, target, methodName);
            if (index != -1)
                ev.SetPersistentListenerState(index, UnityEventCallState.Off);
        }

        private List<PickupIndex> GetAvailablePickups(PickupIndex generatedPickup) 
        {
            var availablePickups = new List<PickupIndex>();
            if (generatedPickup.itemIndex != ItemIndex.None) {
                var tier = ItemCatalog.GetItemDef(generatedPickup.itemIndex).tier;
                if (tier == ItemTier.Tier1 || tier == ItemTier.Tier2 || tier == ItemTier.Tier3)
                    availablePickups.AddRange(Run.instance.availableTier1DropList);

                if (tier == ItemTier.Tier2 || tier == ItemTier.Tier3)
                    availablePickups.AddRange(Run.instance.availableTier2DropList);

                if (tier == ItemTier.Tier3)
                    availablePickups.AddRange(Run.instance.availableTier3DropList);

                if (tier == ItemTier.Lunar)
                    availablePickups.AddRange(Run.instance.availableLunarItemDropList);


                //if (tier != ItemTier.Tier1 && tier != ItemTier.Tier2 && tier != ItemTier.Tier3 && tier != ItemTier.Lunar)
                //    return;
            } 
            
            else if (generatedPickup.equipmentIndex != EquipmentIndex.None) 
            {
                if (EquipmentCatalog.GetEquipmentDef(generatedPickup.equipmentIndex).isLunar) 
                    availablePickups.AddRange(Run.instance.availableLunarEquipmentDropList);
                else 
                    availablePickups.AddRange(Run.instance.availableEquipmentDropList);
            }
            return availablePickups;
        }

        private bool HandlePurchaseInteraction(Interactor interactor, NetworkBehaviour ctr, PickupIndex generatedPickup) 
        {
            var user = interactor.GetComponent<CharacterBody>()?.master?.GetComponent<PlayerCharacterMasterController>()?.networkUser;
            if (user == null)
                return false;

            List<PickupIndex> pickups = GetAvailablePickups(generatedPickup);
            if (pickups.Count == 0)
                return false;
            

            if (user.connectionToClient == null) 
            {
                Logger.LogWarning("Could not show item picker because the target user has no client connection.");
                return false;
            }

            PendingPickerRequest request = CreatePendingPickerRequest(ctr.netId, generatedPickup);
            new ShowItemPickerMessage(ctr.netId, request.RequestToken, pickups).Send(user.connectionToClient);
            return true;
        }

        private void HandlePostCreateMultiShopTerminals(MultiShopController multiShop) 
        {
            // Show items from all terminals except for one
            if (!privateFieldAccess.TryGetMultiShopTerminalGameObjects(multiShop, out GameObject[] objects)) {
                return;
}

            GameObject hidden = null;
            foreach (GameObject o in objects) 
            {
                if (o.GetComponent<ShopTerminalBehavior>().Networkhidden)
                    hidden = o;
            }

            if (hidden == null)
                hidden = Run.instance.treasureRng.NextElementUniform<GameObject>(objects);

            foreach (GameObject o in objects)
                o.GetComponent<ShopTerminalBehavior>().Networkhidden = (o == hidden);

            // Fix anim - Don't close the terminal we are picking the item from.
            foreach (GameObject gameObject in objects) 
            {
                // Remove the .DisableAllTerminals listener and reimplement it
                gameObject.GetComponent<PurchaseInteraction>().onPurchase.RemoveAllListeners();
                gameObject.GetComponent<PurchaseInteraction>().onPurchase.AddListener((v) => {
                    foreach (GameObject other in objects) 
                    {
                        if (other == gameObject) // CHANGE: exclude the terminal we are opening
                            continue;
                        other.GetComponent<PurchaseInteraction>().Networkavailable = false;
                        other.GetComponent<ShopTerminalBehavior>().SetNoPickup();
                    }
                    multiShop.Networkavailable = false;
                });
            }
        }

        private void ShowItemPicker(List<PickupIndex> availablePickups, ItemCallback cb) 
        {
            var itemInventoryDisplay = GameObject.Find("ItemInventoryDisplay");

            float uiWidth = 400f;
            if (availablePickups.Count > 8 * 5) // at least 5 rows of 8 items
                uiWidth = 500f;

            if (availablePickups.Count > 10 * 5) // at least 5 rows of 10 items
                uiWidth = 600f;

            Logger.Log(LogLevel.Info, "Run started");
            var g = new GameObject();
            g.name = "ChestItemsUI";
            g.layer = 5; // UI
            g.AddComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            g.GetComponent<Canvas>().sortingOrder = -1; // Required or the UI will render over pause and tooltips.
            // g.AddComponent<CanvasScaler>().scaleFactor = 10.0f;
            // g.GetComponent<CanvasScaler>().dynamicPixelsPerUnit = 10f;
            g.AddComponent<GraphicRaycaster>();
            g.AddComponent<MPEventSystemProvider>().fallBackToMainEventSystem = true;
            g.AddComponent<MPEventSystemLocator>();
            g.AddComponent<CursorOpener>();

            var ctr = new GameObject();
            ctr.name = "Container";
            ctr.transform.SetParent(g.transform, false);
            ctr.AddComponent<RectTransform>().SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, uiWidth);

            var bg2 = new GameObject();
            bg2.name = "Background";
            bg2.transform.SetParent(ctr.transform, false);
            bg2.AddComponent<TranslucentImage>().color = new Color(0f, 0f, 0f, 1f);
            bg2.GetComponent<TranslucentImage>().raycastTarget = true;
            bg2.GetComponent<TranslucentImage>().material = Resources.Load<GameObject>("Prefabs/UI/Tooltip").GetComponentInChildren<TranslucentImage>(true).material;
            bg2.GetComponent<RectTransform>().anchorMin = new Vector2(0f, 0f);
            bg2.GetComponent<RectTransform>().anchorMax = new Vector2(1f, 1f);
            bg2.GetComponent<RectTransform>().sizeDelta = new Vector2(0, 0);

            var bg = new GameObject();
            bg.name = "Background";
            bg.transform.SetParent(ctr.transform, false);
            bg.AddComponent<Image>().sprite = itemInventoryDisplay.GetComponent<Image>().sprite;
            bg.GetComponent<Image>().type = Image.Type.Sliced;
            bg.GetComponent<RectTransform>().anchorMin = new Vector2(0f, 0f);
            bg.GetComponent<RectTransform>().anchorMax = new Vector2(1f, 1f);
            bg.GetComponent<RectTransform>().sizeDelta = new Vector2(0, 0);

            var header = new GameObject();
            header.name = "Header";
            header.transform.SetParent(ctr.transform, false);
            header.transform.localPosition = new Vector2(0, 0);
            header.AddComponent<HGTextMeshProUGUI>().fontSize = 30;
            header.GetComponent<HGTextMeshProUGUI>().text = "Select the item";
            header.GetComponent<HGTextMeshProUGUI>().color = Color.white;
            header.GetComponent<HGTextMeshProUGUI>().alignment = TextAlignmentOptions.Center;
            header.GetComponent<RectTransform>().anchorMin = new Vector2(0f, 1f);
            header.GetComponent<RectTransform>().anchorMax = new Vector2(1f, 1f);
            header.GetComponent<RectTransform>().pivot = new Vector2(0.5f, 1f);
            header.GetComponent<RectTransform>().sizeDelta = new Vector2(0, 90);

            var itemCtr = new GameObject();
            itemCtr.name = "Item Container";
            itemCtr.transform.SetParent(ctr.transform, false);
            itemCtr.transform.localPosition = new Vector2(0, -100f);
            itemCtr.AddComponent<GridLayoutGroup>().childAlignment = TextAnchor.UpperCenter;
            itemCtr.GetComponent<GridLayoutGroup>().cellSize = new Vector2(50f, 50f);
            itemCtr.GetComponent<GridLayoutGroup>().spacing = new Vector2(8f, 8f);
            itemCtr.GetComponent<RectTransform>().anchorMin = new Vector2(0f, 1f);
            itemCtr.GetComponent<RectTransform>().anchorMax = new Vector2(1f, 1f);
            itemCtr.GetComponent<RectTransform>().pivot = new Vector2(0.5f, 1f);
            itemCtr.GetComponent<RectTransform>().sizeDelta = new Vector2(-16f, 0);
            itemCtr.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            
            var itemIconPrefab = itemInventoryDisplay.GetComponent<ItemInventoryDisplay>().itemIconPrefab;
            foreach (PickupIndex index in availablePickups) 
            {
                if (index.itemIndex == ItemIndex.None)
                    continue;

                var item = Instantiate<GameObject>(itemIconPrefab, itemCtr.transform).GetComponent<ItemIcon>();
                item.SetItemIndex(index.itemIndex, 1);
                item.gameObject.AddComponent<Button>().onClick.AddListener(() => {
                    Logger.LogInfo("Item picked: " + index);
                    UnityEngine.Object.Destroy(g);
                    cb(index);
                });
            }
            foreach (PickupIndex index in availablePickups) 
            {
                if (index.equipmentIndex == EquipmentIndex.None)
                    continue;

                var def = EquipmentCatalog.GetEquipmentDef(index.equipmentIndex);
                var item = Instantiate<GameObject>(itemIconPrefab, itemCtr.transform).GetComponent<ItemIcon>();
                item.GetComponent<RawImage>().texture = def.pickupIconTexture;
                item.stackText.enabled = false;
                item.tooltipProvider.titleToken = def.nameToken;
                item.tooltipProvider.titleColor = ColorCatalog.GetColor(def.colorIndex);
                item.tooltipProvider.bodyToken = def.pickupToken;
                item.tooltipProvider.bodyColor = Color.gray;
                item.gameObject.AddComponent<Button>().onClick.AddListener(() => {
                    Logger.LogInfo("Equipment picked: " + index);
                    UnityEngine.Object.Destroy(g);
                    cb(index);
                });
            }
            LayoutRebuilder.ForceRebuildLayoutImmediate(itemCtr.GetComponent<RectTransform>());
            ctr.GetComponent<RectTransform>().SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, itemCtr.GetComponent<RectTransform>().sizeDelta.y + 100f + 20f);
        }

        internal static ChestItemsPlugin Instance { get; private set; }

        public delegate void ItemCallback(PickupIndex index);

        internal void HandleShowItemPicker(NetworkInstanceId targetId, string requestToken, List<PickupIndex> pickups) 
        {
            ShowItemPicker(pickups, selectedPickup => SendItemPicked(targetId, requestToken, selectedPickup));
        }

        private void SendItemPicked(NetworkInstanceId targetId, string requestToken, PickupIndex selectedPickup) 
        {
            new ItemPickedMessage(targetId, requestToken, selectedPickup).Send(NetworkDestination.Server);
        }

        internal void HandleItemPicked(NetworkInstanceId targetId, string requestToken, PickupIndex selectedPickup) 
        {
            if (!NetworkServer.active) 
                return;
            

            RemoveExpiredPendingPickerRequests();

            if (!pendingPickerRequests.TryGetValue(targetId, out PendingPickerRequest request)) 
            {
                Logger.LogWarning($"Rejected item picker response for {targetId} because no pending picker request exists.");
                return;
            }

            if (!string.Equals(request.RequestToken, requestToken, StringComparison.Ordinal)) 
            {
                Logger.LogWarning($"Rejected item picker response for {targetId} because the request token did not match.");
                return;
            }

            GameObject targetObject = Util.FindNetworkObject(targetId);
            if (targetObject == null) 
            {
                pendingPickerRequests.Remove(targetId);
                Logger.LogWarning($"Rejected item picker response because network object {targetId} was not found.");
                return;
            }

            if (!TryGetCurrentGeneratedPickup(targetObject, out PickupIndex currentGeneratedPickup)) 
            {
                pendingPickerRequests.Remove(targetId);
                Logger.LogWarning($"Rejected item picker response because generated pickup could not be read for {targetId}.");
                return;
            }

            if (!request.GeneratedPickup.Equals(currentGeneratedPickup)) 
            {
                pendingPickerRequests.Remove(targetId);
                Logger.LogWarning($"Rejected item picker response for {targetId} because the generated pickup changed before selection was applied.");
                return;
            }

            List<PickupIndex> currentAllowedPickups = GetAvailablePickups(currentGeneratedPickup);
            if (!PickupListContains(currentAllowedPickups, selectedPickup)) 
            {
                Logger.LogWarning($"Rejected invalid pickup selection {selectedPickup} for target {targetId}.");
                return;
            }

            pendingPickerRequests.Remove(targetId);
            ApplySelectedPickup(targetObject, selectedPickup);
        }

        private PendingPickerRequest CreatePendingPickerRequest(NetworkInstanceId targetId, PickupIndex generatedPickup) 
        {
            RemoveExpiredPendingPickerRequests();

            var request = new PendingPickerRequest(targetId, Guid.NewGuid().ToString("N"), generatedPickup, Time.realtimeSinceStartup);
            pendingPickerRequests[targetId] = request;
            return request;
        }

        private void RemoveExpiredPendingPickerRequests() 
        {
            float now = Time.realtimeSinceStartup;
            var expiredTargetIds = new List<NetworkInstanceId>();

            foreach (KeyValuePair<NetworkInstanceId, PendingPickerRequest> pair in pendingPickerRequests) 
            {
                if (now - pair.Value.CreatedAt > PendingPickerRequestTimeoutSeconds) 
                    expiredTargetIds.Add(pair.Key);                
            }

            foreach (NetworkInstanceId targetId in expiredTargetIds) 
            {
                pendingPickerRequests.Remove(targetId);
            }
        }

        private static bool PickupListContains(List<PickupIndex> pickups, PickupIndex selectedPickup) 
        {
            foreach (PickupIndex pickup in pickups) 
            {
                if (pickup.Equals(selectedPickup)) 
                    return true;
            }
            return false;
        }

        private bool TryGetCurrentGeneratedPickup(GameObject targetObject, out PickupIndex generatedPickup) 
        {
            generatedPickup = default;

            var chest = targetObject.GetComponent<ChestBehavior>();
            if (chest != null) 
                return privateFieldAccess.TryGetChestDropPickup(chest, out generatedPickup);
            
            var terminal = targetObject.GetComponent<ShopTerminalBehavior>();
            if (terminal != null) 
                return privateFieldAccess.TryGetShopTerminalPickupIndex(terminal, out generatedPickup);
            
            return false;
        }

        private void ApplySelectedPickup(GameObject targetObject, PickupIndex selectedPickup) 
        {
            var chest = targetObject.GetComponent<ChestBehavior>();
            if (chest != null) 
            {
                if (!privateFieldAccess.TrySetChestDropPickup(chest, selectedPickup)) 
                    return;             

                if (FindPersistentListener(chest.GetComponent<PurchaseInteraction>().onPurchase, chest, "ItemDrop") != -1) 
                    chest.ItemDrop();
                
                chest.Open();
                return;
            }

            var terminal = targetObject.GetComponent<ShopTerminalBehavior>();
            if (terminal != null) 
            {
                if (!privateFieldAccess.TrySetShopTerminalPickupIndex(terminal, selectedPickup)) 
                    return;
                
                terminal.DropPickup();
                terminal.SetNoPickup();
                return;
            }

            Logger.LogWarning("Selected pickup target was not a supported chest or shop terminal.");
        }
    }
}
