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
        private readonly HashSet<NetworkInstanceId> pickerPurchaseReplays = new HashSet<NetworkInstanceId>();

        private sealed class PendingPickerRequest 
        {
            internal PendingPickerRequest(NetworkInstanceId targetId, string requestToken, PickupIndex generatedPickup, Interactor interactor, PurchaseInteraction purchaseInteraction, float createdAt) 
            {
                TargetId = targetId;
                RequestToken = requestToken;
                GeneratedPickup = generatedPickup;
                Interactor = interactor;
                PurchaseInteraction = purchaseInteraction;
                CreatedAt = createdAt;
            }

            internal NetworkInstanceId TargetId { get; }
            internal string RequestToken { get; }
            internal PickupIndex GeneratedPickup { get; }
            internal Interactor Interactor { get; }
            internal PurchaseInteraction PurchaseInteraction { get; }
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

            On.RoR2.PurchaseInteraction.OnInteractionBegin += PurchaseInteraction_OnInteractionBegin;
            On.RoR2.MultiShopController.CreateTerminals += (orig, self) => {
                orig(self);
                HandlePostCreateMultiShopTerminals(self);
            };
        }

        private void PurchaseInteraction_OnInteractionBegin(On.RoR2.PurchaseInteraction.orig_OnInteractionBegin orig, PurchaseInteraction self, Interactor interactor) 
        {
            if (TryHandlePickerPurchase(self, interactor)) 
                return;
            
            orig(self, interactor);
        }

        private bool TryHandlePickerPurchase(PurchaseInteraction purchaseInteraction, Interactor interactor) 
        {
            if (!NetworkServer.active) 
            {
                Logger.LogInfo("Picker purchase skipped because NetworkServer.active is false.");
                return false;
            }

            if (purchaseInteraction == null) 
            {
                Logger.LogInfo("Picker purchase skipped because PurchaseInteraction was null.");
                return false;
            }

            if (interactor == null) 
            {
                Logger.LogInfo($"Picker purchase skipped for {DescribePurchaseInteraction(purchaseInteraction)} because Interactor was null.");
                return false;
            }

            Logger.LogInfo($"Picker checking purchase interaction: {DescribePurchaseInteraction(purchaseInteraction)}");

            if (!TryGetPickerTarget(purchaseInteraction.gameObject, out NetworkBehaviour target, out PickupIndex generatedPickup)) 
            {
                Logger.LogInfo($"Picker purchase skipped because no supported picker target was found. Object hierarchy: {DescribeObjectHierarchy(purchaseInteraction.gameObject)}");
                return false;
            }

            Logger.LogInfo($"Picker target resolved to {target.GetType().Name} '{target.gameObject.name}' netId={target.netId} generatedPickup={generatedPickup}");

            if (pickerPurchaseReplays.Remove(target.netId)) 
            {
                Logger.LogInfo($"Picker purchase replay allowed for {target.GetType().Name} '{target.gameObject.name}' netId={target.netId}.");
                return false;
            }

            RemoveExpiredPendingPickerRequests();

            if (pendingPickerRequests.ContainsKey(target.netId)) 
            {
                Logger.LogInfo($"Picker purchase blocked because a pending picker request already exists for netId={target.netId}.");
                return true;
            }

            bool handled = HandlePurchaseInteraction(interactor, target, generatedPickup, purchaseInteraction);
            Logger.LogInfo($"Picker purchase HandlePurchaseInteraction returned {handled} for {target.GetType().Name} '{target.gameObject.name}' generatedPickup={generatedPickup}.");
            return handled;
        }

        private bool TryGetPickerTarget(GameObject targetObject, out NetworkBehaviour target, out PickupIndex generatedPickup) 
        {
            target = null;
            generatedPickup = default;

            var chest = targetObject.GetComponent<ChestBehavior>();
            if (chest == null)
                chest = targetObject.GetComponentInParent<ChestBehavior>();

            if (chest == null)
                chest = targetObject.GetComponentInChildren<ChestBehavior>();

            if (chest != null && privateFieldAccess.TryGetChestDropPickup(chest, out generatedPickup)) 
            {
                target = chest;
                return true;
            }

            var terminal = targetObject.GetComponent<ShopTerminalBehavior>();
            if (terminal == null)
                terminal = targetObject.GetComponentInParent<ShopTerminalBehavior>();

            if (terminal == null)
                terminal = targetObject.GetComponentInChildren<ShopTerminalBehavior>();

            if (terminal != null) 
            {
                generatedPickup = terminal.CurrentPickupIndex();
                if (generatedPickup != PickupIndex.none) 
                {
                    target = terminal;
                    return true;
                }
            }

            return false;
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

        private static string DescribePurchaseInteraction(PurchaseInteraction purchaseInteraction) 
        {
            if (purchaseInteraction == null)
                return "null PurchaseInteraction";

            NetworkIdentity identity = purchaseInteraction.GetComponent<NetworkIdentity>();
            string netId = identity != null ? identity.netId.ToString() : "no NetworkIdentity";
            return $"'{purchaseInteraction.gameObject.name}' displayNameToken='{purchaseInteraction.displayNameToken}' contextToken='{purchaseInteraction.contextToken}' costType={purchaseInteraction.costType} cost={purchaseInteraction.cost} netId={netId} components={DescribeComponents(purchaseInteraction.gameObject)}";
        }

        private static string DescribeObjectHierarchy(GameObject gameObject) 
        {
            if (gameObject == null)
                return "null GameObject";

            var parts = new List<string>();
            Transform current = gameObject.transform;
            int depth = 0;

            while (current != null && depth < 8) 
            {
                parts.Add($"'{current.gameObject.name}'[{DescribeComponents(current.gameObject)}]");
                current = current.parent;
                depth++;
            }

            return string.Join(" <- parent ", parts.ToArray());
        }

        private static string DescribeComponents(GameObject gameObject) 
        {
            if (gameObject == null)
                return "null GameObject";

            var componentNames = new List<string>();
            Component[] components = gameObject.GetComponents<Component>();

            foreach (Component component in components) 
            {
                if (component == null)
                    componentNames.Add("null");
                else
                    componentNames.Add(component.GetType().Name);
            }

            return string.Join(", ", componentNames.ToArray());
        }

        private List<PickupIndex> GetAvailablePickups(PickupIndex generatedPickup) 
        {
            var availablePickups = new List<PickupIndex>();
            if (generatedPickup.itemIndex != ItemIndex.None) {
                ItemDef itemDef = ItemCatalog.GetItemDef(generatedPickup.itemIndex);
                if (itemDef == null)
                    return availablePickups;

                switch (itemDef.tier) 
                {
                    case ItemTier.Tier1:
                        availablePickups.AddRange(Run.instance.availableTier1DropList);
                        break;

                    case ItemTier.Tier2:
                        availablePickups.AddRange(Run.instance.availableTier2DropList);
                        break;

                    case ItemTier.Tier3:
                        availablePickups.AddRange(Run.instance.availableTier3DropList);
                        break;

                    case ItemTier.Lunar:
                        availablePickups.AddRange(Run.instance.availableLunarItemDropList);
                        break;

                    case ItemTier.Boss:
                        availablePickups.AddRange(Run.instance.availableBossDropList);
                        break;
                }
            } 

            else if (generatedPickup.equipmentIndex != EquipmentIndex.None) 
            {
                EquipmentDef equipmentDef = EquipmentCatalog.GetEquipmentDef(generatedPickup.equipmentIndex);
                if (equipmentDef == null)
                    return availablePickups;

                if (equipmentDef.isLunar) 
                    availablePickups.AddRange(Run.instance.availableLunarEquipmentDropList);
                else 
                    availablePickups.AddRange(Run.instance.availableEquipmentDropList);
            }
            return availablePickups;
        }

        private bool HandlePurchaseInteraction(Interactor interactor, NetworkBehaviour ctr, PickupIndex generatedPickup, PurchaseInteraction purchaseInteraction) 
        {
            var user = interactor.GetComponent<CharacterBody>()?.master?.GetComponent<PlayerCharacterMasterController>()?.networkUser;
            if (user == null)
            {
                Logger.LogInfo($"Picker purchase skipped for {ctr.GetType().Name} '{ctr.gameObject.name}' because no NetworkUser was found from the interactor.");
                return false;
            }

            List<PickupIndex> pickups = GetAvailablePickups(generatedPickup);
            if (pickups.Count == 0)
            {
                Logger.LogInfo($"Picker purchase skipped for {ctr.GetType().Name} '{ctr.gameObject.name}' because generatedPickup={generatedPickup} produced zero available picker options.");
                return false;
            }
            

            if (user.connectionToClient == null) 
            {
                Logger.LogWarning("Could not show item picker because the target user has no client connection.");
                return false;
            }

            Logger.LogInfo($"Picker purchase opening picker for {ctr.GetType().Name} '{ctr.gameObject.name}' netId={ctr.netId} with {pickups.Count} options.");

            PendingPickerRequest request = CreatePendingPickerRequest(ctr.netId, generatedPickup, interactor, purchaseInteraction);
            new ShowItemPickerMessage(ctr.netId, request.RequestToken, pickups).Send(user.connectionToClient);
            return true;
        }

        private void HandlePostCreateMultiShopTerminals(MultiShopController multiShop) 
        {
            if (!privateFieldAccess.TryGetMultiShopTerminalGameObjects(multiShop, out GameObject[] terminalObjects)) 
            {
                Logger.LogInfo($"Picker multishop setup skipped for '{multiShop.gameObject.name}' because terminal GameObjects could not be read.");
                return;
            }

            Logger.LogInfo($"Picker multishop setup found {terminalObjects.Length} terminals for '{multiShop.gameObject.name}'.");
            
            foreach (GameObject terminalObject in terminalObjects) 
            {
                if (terminalObject == null) 
                {
                    Logger.LogInfo("Picker multishop setup found a null terminal object.");
                    continue;
                }

                ShopTerminalBehavior terminal = terminalObject.GetComponent<ShopTerminalBehavior>();
                PurchaseInteraction purchaseInteraction = terminalObject.GetComponent<PurchaseInteraction>();

                if (terminal == null) 
                {
                    Logger.LogInfo($"Picker multishop setup skipped '{terminalObject.name}' because it has no ShopTerminalBehavior. Components: {DescribeComponents(terminalObject)}");
                    continue;
                }

                Logger.LogInfo($"Picker multishop terminal '{terminalObject.name}' purchaseInteraction={purchaseInteraction != null} currentPickupIndex={terminal.CurrentPickupIndex()} netId={terminal.netId} components={DescribeComponents(terminalObject)}");
            }
        }

        private void ShowItemPicker(List<PickupIndex> availablePickups, ItemCallback cb) 
        {
            var itemInventoryDisplay = GameObject.Find("ItemInventoryDisplay");
            bool selectionSubmitted = false;

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
            
            foreach (PickupIndex index in availablePickups) 
            {
                PickupDef pickupDef = PickupCatalog.GetPickupDef(index);
                if (pickupDef == null)
                    continue;

                AddPickupButton(itemCtr.transform, pickupDef.iconSprite, pickupDef.nameToken, pickupDef.baseColor, index, () => {
                    if (selectionSubmitted)
                        return;

                    selectionSubmitted = true;
                    Logger.LogInfo("Pickup picked: " + index);
                    UnityEngine.Object.Destroy(g);
                    cb(index);
                });
            }
            LayoutRebuilder.ForceRebuildLayoutImmediate(itemCtr.GetComponent<RectTransform>());
            ctr.GetComponent<RectTransform>().SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, itemCtr.GetComponent<RectTransform>().sizeDelta.y + 100f + 20f);
        }

        private void AddPickupButton(Transform parent, Sprite pickupIconSprite, string titleToken, Color titleColor, PickupIndex pickupIndex, UnityAction onClick) 
        {
            var item = new GameObject();
            item.name = "Pickup " + pickupIndex;
            item.transform.SetParent(parent, false);

            var image = item.AddComponent<Image>();
            image.sprite = pickupIconSprite;
            image.preserveAspect = true;
            image.raycastTarget = true;

            var tooltipProvider = item.AddComponent<TooltipProvider>();
            tooltipProvider.titleToken = titleToken;
            tooltipProvider.titleColor = titleColor;
            tooltipProvider.bodyToken = "";
            tooltipProvider.bodyColor = Color.gray;

            var button = item.AddComponent<Button>();
            button.targetGraphic = image;
            button.onClick.AddListener(onClick);
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

            PickupIndex validationPickup = request.GeneratedPickup;

            if (targetObject.GetComponent<ChestBehavior>() != null) 
            {
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
                validationPickup = currentGeneratedPickup;
            }

            List<PickupIndex> currentAllowedPickups = GetAvailablePickups(validationPickup);
            if (!PickupListContains(currentAllowedPickups, selectedPickup)) 
            {
                Logger.LogWarning($"Rejected invalid pickup selection {selectedPickup} for target {targetId}.");
                return;
            }

            pendingPickerRequests.Remove(targetId);
            ApplySelectedPickup(targetObject, request, selectedPickup);
        }

        private PendingPickerRequest CreatePendingPickerRequest(NetworkInstanceId targetId, PickupIndex generatedPickup, Interactor interactor, PurchaseInteraction purchaseInteraction) 
        {
            RemoveExpiredPendingPickerRequests();

            var request = new PendingPickerRequest(targetId, Guid.NewGuid().ToString("N"), generatedPickup, interactor, purchaseInteraction, Time.realtimeSinceStartup);
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
            {
                generatedPickup = terminal.CurrentPickupIndex();
                return generatedPickup != PickupIndex.none;
            }
            
            return false;
        }

        private void ApplySelectedPickup(GameObject targetObject, PendingPickerRequest request, PickupIndex selectedPickup) 
        {
            var chest = targetObject.GetComponent<ChestBehavior>();
            if (chest != null) 
            {
                if (!privateFieldAccess.TrySetChestDropPickup(chest, selectedPickup)) 
                    return;             

                ReplayPurchase(targetObject, request);
                return;
            }

            var terminal = targetObject.GetComponent<ShopTerminalBehavior>();
            if (terminal != null) 
            {
                terminal.SetPickupIndex(selectedPickup);
                ReplayPurchase(targetObject, request);
                return;
            }
            Logger.LogWarning("Selected pickup target was not a supported chest or shop terminal.");
        }

        private void ReplayPurchase(GameObject targetObject, PendingPickerRequest request) 
        {
            if (request.PurchaseInteraction == null) 
            {
                Logger.LogWarning($"Could not replay picker purchase for {request.TargetId} because the PurchaseInteraction was missing.");
                return;
            }

            if (request.Interactor == null) 
            {
                Logger.LogWarning($"Could not replay picker purchase for {request.TargetId} because the original interactor was missing.");
                return;
            }
            pickerPurchaseReplays.Add(request.TargetId);
            request.PurchaseInteraction.OnInteractionBegin(request.Interactor);
        }
    }
}