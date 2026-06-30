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
        private readonly FieldInfo chestBehaviorCurrentPickupBackingField;
        private readonly PropertyInfo chestBehaviorCurrentPickupProperty;
        private readonly PropertyInfo uniquePickupPickupIndexProperty;
        private readonly FieldInfo uniquePickupPickupIndexField;
        private readonly FieldInfo shopTerminalBehaviorPickupIndexField;
        private readonly FieldInfo multiShopControllerTerminalGameObjectsField;

        internal RoR2PrivateFieldAccess(ManualLogSource logger) 
        {
            this.logger = logger ?? throw new ArgumentNullException(nameof(logger));

            chestBehaviorDropPickupField = TryResolveField(typeof(ChestBehavior), "dropPickup");
            chestBehaviorCurrentPickupBackingField = TryResolveField(typeof(ChestBehavior), "<currentPickup>k__BackingField");
            chestBehaviorCurrentPickupProperty = TryResolveProperty(typeof(ChestBehavior), "currentPickup");

            Type uniquePickupType = ResolveUniquePickupType();
            if (uniquePickupType != null) 
            {
                uniquePickupPickupIndexProperty = TryResolveProperty(uniquePickupType, "pickupIndex");
                uniquePickupPickupIndexField = TryResolveField(uniquePickupType, "pickupIndex");

                if (uniquePickupPickupIndexProperty == null && uniquePickupPickupIndexField == null) 
                {
                    logger.LogError($"Could not resolve {uniquePickupType.FullName}.pickupIndex.");
                    LogAvailableMembers(uniquePickupType);
                }
            }
            else 
            {
                logger.LogError("Could not resolve ChestBehavior current pickup type.");
            }

            shopTerminalBehaviorPickupIndexField = ResolveField(typeof(ShopTerminalBehavior), "pickupIndex");
            multiShopControllerTerminalGameObjectsField = ResolveField(typeof(MultiShopController), "terminalGameObjects");
        }

        internal bool IsAvailable 
        {
            get 
            {
                return IsChestPickupAccessAvailable()
                    && shopTerminalBehaviorPickupIndexField != null
                    && multiShopControllerTerminalGameObjectsField != null;
            }
        }

        private bool IsChestPickupAccessAvailable() 
        {
            if (chestBehaviorDropPickupField != null) 
                return true;
            
            bool hasCurrentPickupAccessor = chestBehaviorCurrentPickupBackingField != null || chestBehaviorCurrentPickupProperty != null;
            bool hasUniquePickupIndexAccessor = uniquePickupPickupIndexProperty != null || uniquePickupPickupIndexField != null;

            if (!hasCurrentPickupAccessor) 
                logger.LogError("ChestBehavior pickup access is unavailable because currentPickup was not found.");
            
            if (!hasUniquePickupIndexAccessor) 
                logger.LogError("ChestBehavior pickup access is unavailable because UniquePickup.pickupIndex was not found.");
            
            return hasCurrentPickupAccessor && hasUniquePickupIndexAccessor;
        }

        internal bool TryGetChestDropPickup(ChestBehavior chestBehavior, out PickupIndex pickupIndex) 
        {
            if (chestBehaviorDropPickupField != null) 
                return TryReadField(chestBehaviorDropPickupField, chestBehavior, "ChestBehavior.dropPickup", out pickupIndex);
            

            if (!TryGetChestUniquePickup(chestBehavior, out object uniquePickup)) 
            {
                pickupIndex = default;
                return false;
            }

            return TryGetUniquePickupIndex(uniquePickup, out pickupIndex);
        }

        internal bool TrySetChestDropPickup(ChestBehavior chestBehavior, PickupIndex pickupIndex) 
        {
            if (chestBehaviorDropPickupField != null) 
                return TryWriteField(chestBehaviorDropPickupField, chestBehavior, pickupIndex, "ChestBehavior.dropPickup");
            
            if (!TryGetChestUniquePickup(chestBehavior, out object uniquePickup)) 
                return false;
            
            if (!TrySetUniquePickupIndex(uniquePickup, pickupIndex)) 
                return false;
            
            return TrySetChestUniquePickup(chestBehavior, uniquePickup);
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

        private bool TryGetChestUniquePickup(ChestBehavior chestBehavior, out object uniquePickup) 
        {
            uniquePickup = null;

            if (chestBehavior == null) 
            {
                logger.LogError("Cannot read ChestBehavior.currentPickup because the target object is null.");
                return false;
            }

            try 
            {
                if (chestBehaviorCurrentPickupProperty != null) 
                {
                    uniquePickup = chestBehaviorCurrentPickupProperty.GetValue(chestBehavior);
                    return uniquePickup != null;
                }

                if (chestBehaviorCurrentPickupBackingField != null) 
                {
                    uniquePickup = chestBehaviorCurrentPickupBackingField.GetValue(chestBehavior);
                    return uniquePickup != null;
                }
            } 
            
            catch (Exception ex) 
            {
                logger.LogError($"Failed to read ChestBehavior.currentPickup: {ex}");
                return false;
            }

            logger.LogError("Cannot read ChestBehavior.currentPickup because no supported currentPickup accessor was resolved.");
            return false;
        }

        private bool TrySetChestUniquePickup(ChestBehavior chestBehavior, object uniquePickup) 
        {
            if (chestBehavior == null) 
            {
                logger.LogError("Cannot write ChestBehavior.currentPickup because the target object is null.");
                return false;
            }

            try 
            {
                if (chestBehaviorCurrentPickupProperty != null && chestBehaviorCurrentPickupProperty.CanWrite) 
                {
                    chestBehaviorCurrentPickupProperty.SetValue(chestBehavior, uniquePickup);
                    return true;
                }

                if (chestBehaviorCurrentPickupBackingField != null) 
                {
                    chestBehaviorCurrentPickupBackingField.SetValue(chestBehavior, uniquePickup);
                    return true;
                }
            } 
            
            catch (Exception ex) 
            {
                logger.LogError($"Failed to write ChestBehavior.currentPickup: {ex}");
                return false;
            }

            logger.LogError("Cannot write ChestBehavior.currentPickup because no supported currentPickup writer was resolved.");
            return false;
        }

        private bool TryGetUniquePickupIndex(object uniquePickup, out PickupIndex pickupIndex) 
        {
            pickupIndex = default;

            if (uniquePickup == null) 
            {
                logger.LogError("Cannot read UniquePickup.pickupIndex because UniquePickup is null.");
                return false;
            }

            try 
            {
                if (uniquePickupPickupIndexProperty != null) 
                {
                    object rawValue = uniquePickupPickupIndexProperty.GetValue(uniquePickup);
                    if (rawValue is PickupIndex propertyPickupIndex) 
                    {
                        pickupIndex = propertyPickupIndex;
                        return true;
                    }
                }

                if (uniquePickupPickupIndexField != null) 
                {
                    object rawValue = uniquePickupPickupIndexField.GetValue(uniquePickup);
                    if (rawValue is PickupIndex fieldPickupIndex) 
                    {
                        pickupIndex = fieldPickupIndex;
                        return true;
                    }
                }
            } 
            
            catch (Exception ex) 
            {
                logger.LogError($"Failed to read UniquePickup.pickupIndex: {ex}");
                return false;
            }

            logger.LogError("Cannot read UniquePickup.pickupIndex because no supported pickupIndex accessor was resolved.");
            return false;
        }

        private bool TrySetUniquePickupIndex(object uniquePickup, PickupIndex pickupIndex) 
        {
            if (uniquePickup == null) 
            {
                logger.LogError("Cannot write UniquePickup.pickupIndex because UniquePickup is null.");
                return false;
            }

            try 
            {
                if (uniquePickupPickupIndexProperty != null && uniquePickupPickupIndexProperty.CanWrite) 
                {
                    uniquePickupPickupIndexProperty.SetValue(uniquePickup, pickupIndex);
                    return true;
                }

                if (uniquePickupPickupIndexField != null) 
                {
                    uniquePickupPickupIndexField.SetValue(uniquePickup, pickupIndex);
                    return true;
                }
            } 
            
            catch (Exception ex) 
            {
                logger.LogError($"Failed to write UniquePickup.pickupIndex: {ex}");
                return false;
            }

            logger.LogError("Cannot write UniquePickup.pickupIndex because no supported pickupIndex writer was resolved.");
            return false;
        }

        private FieldInfo ResolveField(Type declaringType, string fieldName) 
        {
            FieldInfo field = TryResolveField(declaringType, fieldName);
            if (field == null) 
            {
                logger.LogError($"Required RoR2 field was not found: {declaringType.FullName}.{fieldName}");
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

        private FieldInfo TryResolveField(Type declaringType, string fieldName) 
        {
            return declaringType.GetField(fieldName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        }

        private PropertyInfo TryResolveProperty(Type declaringType, string propertyName) 
        {
            return declaringType.GetProperty(propertyName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        }

        private Type ResolveUniquePickupType() 
        {
            if (chestBehaviorCurrentPickupProperty != null) 
                return chestBehaviorCurrentPickupProperty.PropertyType;
            
            if (chestBehaviorCurrentPickupBackingField != null) 
                return chestBehaviorCurrentPickupBackingField.FieldType;
            
            return null;
        }

        private void LogAvailableFields(Type declaringType) 
        {
            FieldInfo[] fields = declaringType.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            foreach (FieldInfo field in fields) 
            {
                logger.LogInfo($"Available field on {declaringType.FullName}: {field.FieldType.FullName} {field.Name}");
            }
        }

        private void LogAvailableMembers(Type declaringType) 
        {
            FieldInfo[] fields = declaringType.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            foreach (FieldInfo field in fields) 
            {
                logger.LogInfo($"Available field on {declaringType.FullName}: {field.FieldType.FullName} {field.Name}");
            }

            PropertyInfo[] properties = declaringType.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            foreach (PropertyInfo property in properties) 
            {
                logger.LogInfo($"Available property on {declaringType.FullName}: {property.PropertyType.FullName} {property.Name}");
            }
        }
    }
}