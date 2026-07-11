using System;
using System.Reflection;
using RoR2;

namespace AttackRange.Utilities
{
    internal static class RoR2PrivateFieldAccess
    {
        private static readonly FieldInfo ItemTierDefField =
            typeof(ItemDef).GetField("_itemTierDef", BindingFlags.Instance | BindingFlags.NonPublic) ?? throw new MissingFieldException(typeof(ItemDef).FullName, "_itemTierDef");

        public static void SetItemTierDef(ItemDef itemDef, ItemTierDef itemTierDef)
        {
            if (itemDef == null) { throw new ArgumentNullException(nameof(itemDef)); }
            if (itemTierDef == null) { throw new ArgumentNullException(nameof(itemTierDef)); }

            ItemTierDefField.SetValue(itemDef, itemTierDef);
        }
    }
}