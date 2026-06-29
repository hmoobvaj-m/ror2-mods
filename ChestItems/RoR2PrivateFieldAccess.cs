using System;
using System.Reflection;
using BepInEx.Logging;
using RoR2;
using UnityEngine;

namespace ChestItems 
{
    internal sealed class RoR2PrivateFieldAccess 
    {
        private readonly ManualLogSource logger;
        private readonly FieldInfo chestBehaviorDropPickupField;
        private readonly FieldInfo shopTerminalBehaviorPickupIndexField;
        private readonly FieldInfo multiShopControllerTerminalGameObjectsField;

        internal RoR2PrivateFieldAccess(ManualLogSource logger) 
        {
            this.logger = logger ?? throw new ArgumentNullException(nameof(logger));

            chestBehaviorDropPickupField = ResolveField(typeof(ChestBehavior), "dropPickup");
            shopTerminalBehaviorPickupIndexField = ResolveField(typeof(ShopTerminalBehavior), "pickupIndex");
            multiShopControllerTerminalGameObjectsField = ResolveField(typeof(MultiShopController), "terminalGameObjects");
        }

        internal bool IsAvailable 
        {
            get 
            {
                return chestBehaviorDropPickupField != null
                    && shopTerminalBehaviorPickupIndexField != null
                    && multiShopControllerTerminalGameObjectsField != null;
            }
        }

        internal bool TryGetChestDropPickup(ChestBehavior chestBehavior, out PickupIndex pickupIndex) 
        {
            return TryReadField(chestBehaviorDropPickupField, chestBehavior, "ChestBehavior.dropPickup", out pickupIndex);
        }

        internal bool TrySetChestDropPickup(ChestBehavior chestBehavior, PickupIndex pickupIndex) 
        {
            return TryWriteField(chestBehaviorDropPickupField, chestBehavior, pickupIndex, "ChestBehavior.dropPickup");
        }

        internal bool TryGetShopTerminalPickupIndex(ShopTerminalBehavior shopTerminalBehavior, out PickupIndex pickupIndex) 
        {
            return TryReadField(shopTerminalBehaviorPickupIndexField, shopTerminalBehavior, "ShopTerminalBehavior.pickupIndex", out pickupIndex);
        }

        internal bool TrySetShopTerminalPickupIndex(ShopTerminalBehavior shopTerminalBehavior, PickupIndex pickupIndex) 
        {
            return TryWriteField(shopTerminalBehaviorPickupIndexField, shopTerminalBehavior, pickupIndex, "ShopTerminalBehavior.pickupIndex");
        }

        internal bool TryGetMultiShopTerminalGameObjects(MultiShopController multiShopController, out GameObject[] terminalGameObjects) 
        {
            return TryReadField(multiShopControllerTerminalGameObjectsField, multiShopController, "MultiShopController.terminalGameObjects", out terminalGameObjects);
        }

        private FieldInfo ResolveField(Type declaringType, string fieldName) 
        {
            FieldInfo field = declaringType.GetField(fieldName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (field == null) 
            {
                logger.LogError($"Required RoR2 private field was not found: {declaringType.FullName}.{fieldName}");
                LogAvailableFields(declaringType);
            }
            return field;
        }

        private bool TryReadField<TValue>(FieldInfo field, object target, string fieldLabel, out TValue value) 
        {
            value = default;

            if (field == null) 
            {
                logger.LogError($"Cannot read {fieldLabel} because the field was not resolved.");
                return false;
            }

            if (target == null) 
            {
                logger.LogError($"Cannot read {fieldLabel} because the target object is null.");
                return false;
            }

            object rawValue;
            try 
            {
                rawValue = field.GetValue(target);
            } 

            catch (Exception ex) 
            {
                logger.LogError($"Failed to read {fieldLabel}: {ex}");
                return false;
            }

            if (rawValue is TValue typedValue) 
            {
                value = typedValue;
                return true;
            }

            logger.LogError($"Unexpected value type for {fieldLabel}. Expected {typeof(TValue).FullName}, got {rawValue?.GetType().FullName ?? "null"}.");
            return false;
        }

        private bool TryWriteField<TValue>(FieldInfo field, object target, TValue value, string fieldLabel) 
        {
            if (field == null) 
            {
                logger.LogError($"Cannot write {fieldLabel} because the field was not resolved.");
                return false;
            }

            if (target == null) 
            {
                logger.LogError($"Cannot write {fieldLabel} because the target object is null.");
                return false;
            }

            try 
            {
                field.SetValue(target, value);
                return true;
            } 
            
            catch (Exception ex) 
            {
                logger.LogError($"Failed to write {fieldLabel}: {ex}");
                return false;
            }
        }

        private void LogAvailableFields(Type declaringType) 
        {
            FieldInfo[] fields = declaringType.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            foreach (FieldInfo field in fields) 
            {
                logger.LogInfo($"Available field on {declaringType.FullName}: {field.FieldType.FullName} {field.Name}");
            }
        }
    }
}