using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace NomadDriveWardrobe
{
    /// <summary>
    /// Thin reflection wrapper around the game's PlayerCosmeticManager (Assembly-CSharp).
    /// The game already has a networked cosmetic API; it just has no menu that calls it.
    /// Every call here goes through the game's own methods, so the server validates it and
    /// everyone sees the result through the synced cosmetic ids.
    /// </summary>
    internal static class Cosmetics
    {
        internal static readonly string[] Parts = { "Feet", "LowerBody", "UpperBody", "Head" };
        internal const string AccessoryCategory = "Accessory";

        private static Type _mgr, _cat, _cosmeticItem;

        private static bool Init()
        {
            if (_mgr != null) return true;
            _mgr = AccessTools.TypeByName("NomadDrive.Features.Player.Cosmetics.PlayerCosmeticManager");
            _cat = AccessTools.TypeByName("NomadDrive.Features.Player.Cosmetics.CosmeticCategory");
            _cosmeticItem = AccessTools.TypeByName("NomadDrive.Features.Player.Cosmetics.PlayerCosmetic");
            return _mgr != null && _cat != null && _cosmeticItem != null;
        }

        private static object Cat(string name)
        {
            return Enum.Parse(_cat, name);
        }

        /// <summary>The cosmetic manager of the local player, or null before the player has spawned.</summary>
        internal static Component FindLocal()
        {
            if (!Init()) return null;
            var isLocal = AccessTools.Field(_mgr, "_isLocalPlayer");
            foreach (var o in UnityEngine.Object.FindObjectsOfType(_mgr))
            {
                var c = o as Component;
                if (c == null) continue;
                if (isLocal != null && (bool)isLocal.GetValue(c)) return c;
            }
            return null;
        }

        internal static List<string> Ids(Component mgr, string category)
        {
            var result = new List<string>();
            var dict = AccessTools.Property(_mgr, "CosmeticsByCategory")?.GetValue(mgr, null) as IDictionary;
            if (dict == null) return result;

            var idProp = AccessTools.Property(_cosmeticItem, "CosmeticId");
            var wanted = Convert.ToInt32(Cat(category));
            foreach (DictionaryEntry e in dict)
            {
                if (Convert.ToInt32(e.Key) != wanted) continue;
                foreach (var item in (IEnumerable)e.Value)
                {
                    var id = idProp?.GetValue(item, null) as string;
                    if (!string.IsNullOrEmpty(id) && !result.Contains(id)) result.Add(id);
                }
            }
            return result;
        }

        internal static string Active(Component mgr, string category)
        {
            var m = AccessTools.Method(_mgr, "GetActiveCosmetic");
            return m?.Invoke(mgr, new[] { Cat(category) }) as string ?? "";
        }

        internal static List<string> ActiveAccessories(Component mgr)
        {
            var m = AccessTools.Method(_mgr, "GetActiveAccessories");
            var list = new List<string>();
            if (m?.Invoke(mgr, null) is IEnumerable e)
                foreach (var x in e) if (x is string s && s.Length > 0) list.Add(s);
            return list;
        }

        internal static void Set(Component mgr, string category, string id)
        {
            var m = AccessTools.Method(_mgr, "SetCosmetic");
            if (m == null) { Plugin.Log.LogError("PlayerCosmeticManager.SetCosmetic not found."); return; }
            m.Invoke(mgr, new[] { Cat(category), id ?? "" });
        }

        internal static void AddAccessory(Component mgr, string id)
        {
            AccessTools.Method(_mgr, "AddAccessory")?.Invoke(mgr, new object[] { id });
        }

        internal static void RemoveAccessory(Component mgr, string id)
        {
            AccessTools.Method(_mgr, "RemoveAccessory")?.Invoke(mgr, new object[] { id });
        }

        /// <summary>Whole outfits ("sets") the game ships with: name plus the ids it applies.</summary>
        internal static List<OutfitSet> Sets(Component mgr)
        {
            var result = new List<OutfitSet>();
            var m = AccessTools.Method(_mgr, "GetAvailableSets");
            if (!(m?.Invoke(mgr, null) is IEnumerable e)) return result;

            foreach (var set in e)
            {
                if (set == null) continue;
                var t = set.GetType();
                string Str(string prop) => AccessTools.Property(t, prop)?.GetValue(set, null) as string ?? "";
                var acc = new List<string>();
                if (AccessTools.Property(t, "AccessoryCosmeticIds")?.GetValue(set, null) is IEnumerable accs)
                    foreach (var a in accs) if (a is string s && s.Length > 0) acc.Add(s);

                result.Add(new OutfitSet
                {
                    Name = Str("SetName"),
                    Feet = Str("FeetCosmeticId"),
                    LowerBody = Str("LowerBodyCosmeticId"),
                    UpperBody = Str("UpperBodyCosmeticId"),
                    Head = Str("HeadCosmeticId"),
                    Accessories = acc,
                });
            }
            return result;
        }

        internal class OutfitSet
        {
            public string Name, Feet, LowerBody, UpperBody, Head;
            public List<string> Accessories = new List<string>();

            public string For(string part)
            {
                switch (part)
                {
                    case "Feet": return Feet;
                    case "LowerBody": return LowerBody;
                    case "UpperBody": return UpperBody;
                    default: return Head;
                }
            }
        }

        /// <summary>Applies a full look: the four body parts and the accessory list.</summary>
        internal static void ApplyLook(Component mgr, Look look)
        {
            foreach (var part in Parts)
            {
                var id = look.For(part);
                // Head may legitimately be empty (no hat); the others must not be.
                if (id == null) continue;
                if (id.Length == 0 && part != "Head") continue;
                if (Active(mgr, part) != id) Set(mgr, part, id);
            }

            var wanted = look.Accessories ?? new List<string>();
            var current = ActiveAccessories(mgr);
            foreach (var a in current.Where(a => !wanted.Contains(a)).ToList()) RemoveAccessory(mgr, a);
            foreach (var a in wanted.Where(a => !current.Contains(a))) AddAccessory(mgr, a);
        }

        internal static Look Capture(Component mgr)
        {
            return new Look
            {
                Feet = Active(mgr, "Feet"),
                LowerBody = Active(mgr, "LowerBody"),
                UpperBody = Active(mgr, "UpperBody"),
                Head = Active(mgr, "Head"),
                Accessories = ActiveAccessories(mgr),
            };
        }

        internal class Look
        {
            public string Feet, LowerBody, UpperBody, Head;
            public List<string> Accessories = new List<string>();

            public string For(string part)
            {
                switch (part)
                {
                    case "Feet": return Feet;
                    case "LowerBody": return LowerBody;
                    case "UpperBody": return UpperBody;
                    default: return Head;
                }
            }
        }
    }
}
