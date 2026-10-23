using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Events;

namespace NomadDriveWardrobe
{
    /// <summary>
    /// Adds a "Wardrobe" button to the pause menu that opens an outfit picker.
    /// The game assigns outfits round-robin by spawn order and has no menu to change them, but the
    /// networked API (PlayerCosmeticManager.SetCosmetic / AddAccessory / ...) is there. We call it.
    /// The server validates the ids and syncs them, so every player sees your look without the mod.
    /// Game types are reached through reflection, so no game DLLs are needed to build.
    /// </summary>
    [BepInPlugin(Guid, Name, Version)]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "com.nunod87.nomaddrive.wardrobe";
        public const string Name = "Nomad Drive Wardrobe";
        public const string Version = "0.3.0";

        internal static ManualLogSource Log;
        internal static Plugin Instance;

        private static ConfigEntry<bool> _enabled;
        private static ConfigEntry<bool> _autoApply;
        private static ConfigEntry<string> _buttonPlacement;
        internal const int SlotCount = 3;
        private static readonly ConfigEntry<string>[] _slots = new ConfigEntry<string>[SlotCount];
        private static ConfigEntry<bool> _saved;
        private static ConfigEntry<string> _feet, _lower, _upper, _head, _accessories;

        private Harmony _harmony;
        private readonly HashSet<string> _patched = new HashSet<string>();

        // Window state
        private bool _open;                 // simple IMGUI fallback window
        private WardrobePanel _panel;        // native panel
        private Component _pauseRoot;
        private GameObject _panelGo;
        private Rect _rect = new Rect(40, 60, 440, 640);
        private Vector2 _scroll;
        private Component _manager;
        private bool _loggedCatalog;

        // Auto-apply state
        private Component _lastManager;
        private float _applyAt = -1f;
        private float _nextFind;

        private void Awake()
        {
            Log = Logger;
            Instance = this;

            _enabled = Config.Bind("General", "Enabled", true, "Turn the mod on or off without uninstalling it.");
            _autoApply = Config.Bind("General", "AutoApply", true,
                "Re-apply your saved look automatically when you join a game.");
            _buttonPlacement = Config.Bind("General", "ButtonPlacement", "Inline",
                new ConfigDescription("Where the Wardrobe button goes in the pause menu: Inline (a new entry right below Settings; the list makes room) or Side (to the right of Settings).",
                    new AcceptableValueList<string>("Inline", "Side")));
            for (int i = 0; i < SlotCount; i++)
                _slots[i] = Config.Bind("My looks", "Slot" + (i + 1), "", "Saved look (written by the Wardrobe, no need to edit).");
            _saved = Config.Bind("Saved look", "HasSavedLook", false,
                "Set by the mod when you pick something in the Wardrobe. Set to false to go back to the game's default outfit.");
            _feet = Config.Bind("Saved look", "Feet", "", "Cosmetic id (written by the Wardrobe).");
            _lower = Config.Bind("Saved look", "LowerBody", "", "Cosmetic id (written by the Wardrobe).");
            _upper = Config.Bind("Saved look", "UpperBody", "", "Cosmetic id (written by the Wardrobe).");
            _head = Config.Bind("Saved look", "Head", "", "Cosmetic id, empty = no head item (written by the Wardrobe).");
            _accessories = Config.Bind("Saved look", "Accessories", "", "Comma separated cosmetic ids (written by the Wardrobe).");

            if (!_enabled.Value)
            {
                Log.LogInfo("Disabled in config.");
                return;
            }

            _harmony = new Harmony(Guid);
            AppDomain.CurrentDomain.AssemblyLoad += (_, args) => TryPatch(args.LoadedAssembly);
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies()) TryPatch(asm);
            foreach (var name in new[] { "EvilCore.UI" })
            {
                try { Assembly.Load(name); }
                catch (Exception e) { Log.LogInfo("Could not preload " + name + ": " + e.Message); }
            }

            Log.LogInfo(Name + " " + Version + " loaded.");
        }

        private void TryPatch(Assembly asm)
        {
            string asmName;
            try { asmName = asm.GetName().Name; } catch { return; }
            if (asmName != "EvilCore.UI" || !_patched.Add(asmName)) return;

            try
            {
                var panel = asm.GetType("EvilCore.UI.GameMenu.Panels.GameMenuRootPanel");
                var start = panel?.GetMethod("Start", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                if (start == null)
                {
                    Log.LogError("GameMenuRootPanel.Start not found. Game version changed?");
                    return;
                }

                _harmony.Patch(start, postfix: new HarmonyMethod(typeof(Plugin).GetMethod(
                    nameof(RootPanelStartPostfix), BindingFlags.Static | BindingFlags.NonPublic)));
                Log.LogInfo("Pause menu: Wardrobe button patch ready.");
            }
            catch (Exception e)
            {
                Log.LogError("Patching failed: " + e);
            }
        }

        // ---- Pause menu button ---------------------------------------------------------

        private static void RootPanelStartPostfix(object __instance)
        {
            try
            {
                var panel = __instance as Component;
                var settings = AccessTools.Field(__instance.GetType(), "settingsButton")?.GetValue(__instance) as Component;
                if (panel == null || settings == null) { Log.LogWarning("settingsButton not found on the pause menu."); return; }

                var btnType = AccessTools.TypeByName("UnityEngine.UI.Button");
                var layoutType = AccessTools.TypeByName("UnityEngine.UI.LayoutGroup");

                // Walk up from the Settings button to the entry that sits directly in the menu's list.
                Transform item = settings.transform;
                Transform list = null;
                var chain = new System.Text.StringBuilder("Pause menu structure:");
                for (int i = 0; i < 5 && item != null; i++)
                {
                    var par = item.parent;
                    bool isList = par != null && layoutType != null && par.GetComponent(layoutType) != null;
                    chain.Append(' ').Append(item.name).Append('[').Append(item.childCount).Append("] <");
                    if (isList) { list = par; chain.Append(' ').Append(par.name).Append('[').Append(par.childCount).Append(", layout]"); break; }
                    item = par;
                }
                Log.LogInfo(chain.ToString());

                int buttons = btnType != null && item != null ? item.GetComponentsInChildren(btnType, true).Length : 99;
                if (list != null && buttons <= 1)
                {
                    if (list.Find("WardrobeButton") != null) return;
                    var entry = UnityEngine.Object.Instantiate(item.gameObject, list);
                    entry.name = "WardrobeButton";
                    entry.transform.SetSiblingIndex(item.GetSiblingIndex() + 1);
                    PrepareClone(entry, panel);

                    if (string.Equals(_buttonPlacement.Value, "Side", StringComparison.OrdinalIgnoreCase))
                        Instance.StartCoroutine(PlaceSide(item, entry));
                    Log.LogInfo("Wardrobe entry added to the pause menu list (" + _buttonPlacement.Value + ").");
                    return;
                }

                Log.LogWarning("Could not find the pause menu list (button count in entry: " + buttons + "); using a plain copy next to Settings.");
                var parent = settings.transform.parent;
                if (parent.Find("WardrobeButton") != null) return;
                var clone = UnityEngine.Object.Instantiate(settings.gameObject, parent);
                clone.name = "WardrobeButton";
                PrepareClone(clone, panel);
                Instance.StartCoroutine(PlaceSide(settings.transform, clone));
            }
            catch (Exception e)
            {
                Log.LogError("RootPanelStartPostfix: " + e);
            }
        }

        private static void PrepareClone(GameObject clone, Component panel)
        {
            // Localization components would overwrite our label; strip them on the copy only.
            foreach (var c in clone.GetComponentsInChildren<Component>(true))
            {
                if (c == null) continue;
                if (c.GetType().Name.IndexOf("Locali", StringComparison.OrdinalIgnoreCase) >= 0) UnityEngine.Object.Destroy(c);
            }
            Instance.StartCoroutine(ConfigureButton(clone, "Wardrobe", () => Instance.Toggle(panel.gameObject, panel)));
        }

        // Side placement: leave the list's layout and sit to the right of the Settings entry.
        private static IEnumerator PlaceSide(Transform reference, GameObject clone)
        {
            yield return new WaitForSecondsRealtime(0.8f);
            if (clone == null || reference == null) yield break;
            try
            {
                var le = AccessTools.TypeByName("UnityEngine.UI.LayoutElement");
                if (le != null)
                {
                    var comp = clone.GetComponent(le) ?? clone.AddComponent(le);
                    AccessTools.Property(le, "ignoreLayout")?.SetValue(comp, true, null);
                }
                var rRect = (RectTransform)reference;
                var cRect = (RectTransform)clone.transform;
                cRect.anchorMin = rRect.anchorMin;
                cRect.anchorMax = rRect.anchorMax;
                cRect.pivot = rRect.pivot;
                cRect.sizeDelta = rRect.sizeDelta;
                float w = rRect.rect.width > 20f ? rRect.rect.width : 360f;
                cRect.anchoredPosition = rRect.anchoredPosition + new Vector2(w + 24f, 0f);
            }
            catch (Exception e)
            {
                Log.LogError("PlaceSide: " + e);
            }
        }

        internal static IEnumerator ConfigureButton(GameObject clone, string label, Action onClick)
        {
            yield return null; // let the destroyed localization components go first
            if (clone == null) yield break;

            try
            {
                var tmp = AccessTools.TypeByName("TMPro.TMP_Text");
                var text = tmp != null ? clone.GetComponentInChildren(tmp, true) : null;
                if (text != null) AccessTools.Property(tmp, "text")?.SetValue(text, label, null);

                var btnType = AccessTools.TypeByName("UnityEngine.UI.Button");
                var btn = btnType != null ? clone.GetComponentInChildren(btnType, true) : null;
                var ev = btn != null ? AccessTools.Property(btnType, "onClick")?.GetValue(btn, null) as UnityEvent : null;
                if (ev == null) { Log.LogWarning("Could not wire the button '" + label + "' (Button.onClick not found)."); yield break; }

                ev.RemoveAllListeners();
                ev.AddListener(() => onClick());
                Log.LogInfo("Button '" + label + "' ready.");
            }
            catch (Exception e)
            {
                Log.LogError("ConfigureButton: " + e);
            }
        }

        private void Toggle(GameObject panelGo, Component pauseRoot)
        {
            if (_open || (_panel != null && _panel.IsOpen)) { CloseWardrobe(); return; }

            _panelGo = panelGo;
            _pauseRoot = pauseRoot;
            _manager = Cosmetics.FindLocal();
            if (_manager == null) { Log.LogWarning("Wardrobe: no local player found."); return; }

            _panel = WardrobePanel.TryOpen(pauseRoot, _manager);
            if (_panel == null) _open = true; // fall back to the simple window
        }

        internal void CloseWardrobe()
        {
            if (_panel != null) { _panel.Close(); _panel = null; }
            _open = false;
        }

        // ---- Auto-apply the saved look ---------------------------------------------------

        private void Update()
        {
            if (_panel != null)
            {
                if (_panelGo == null || !_panelGo.activeInHierarchy) CloseWardrobe();
                else _panel.Tick(Time.unscaledDeltaTime);
            }

            if (!_enabled.Value || !_autoApply.Value || !_saved.Value) return;
            if (Time.realtimeSinceStartup < _nextFind) return;
            _nextFind = Time.realtimeSinceStartup + 1f;

            var mgr = Cosmetics.FindLocal();
            if (mgr == null) { _lastManager = null; _applyAt = -1f; return; }

            if (mgr != _lastManager)
            {
                _lastManager = mgr;
                _applyAt = Time.realtimeSinceStartup + 3f; // give the connection time to become ready
                LogCatalog(mgr);
            }

            if (_applyAt > 0f && Time.realtimeSinceStartup >= _applyAt)
            {
                _applyAt = -1f;
                try
                {
                    Cosmetics.ApplyLook(mgr, LoadLook());
                    Log.LogInfo("Applied your saved look.");
                }
                catch (Exception e)
                {
                    Log.LogError("Applying the saved look failed: " + e);
                }
            }
        }

        private void LogCatalog(Component mgr)
        {
            if (_loggedCatalog) return;
            _loggedCatalog = true;
            try
            {
                foreach (var s in Cosmetics.Sets(mgr))
                    Log.LogInfo("Outfit \"" + s.Name + "\": feet=" + s.Feet + " lower=" + s.LowerBody +
                                " upper=" + s.UpperBody + " head=" + s.Head + " acc=[" + string.Join(", ", s.Accessories.ToArray()) + "]");
                foreach (var part in Cosmetics.Parts.Concat(new[] { Cosmetics.AccessoryCategory }))
                    Log.LogInfo(part + " options: " + string.Join(", ", Cosmetics.Ids(mgr, part).ToArray()));
            }
            catch (Exception e)
            {
                Log.LogError("LogCatalog: " + e);
            }
        }

        private static Cosmetics.Look LoadLook()
        {
            return new Cosmetics.Look
            {
                Feet = _feet.Value,
                LowerBody = _lower.Value,
                UpperBody = _upper.Value,
                Head = _head.Value,
                Accessories = _accessories.Value.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(s => s.Trim()).Where(s => s.Length > 0).ToList(),
            };
        }

        internal static Cosmetics.Look GetSlot(int i)
        {
            var v = _slots[i].Value;
            if (string.IsNullOrEmpty(v)) return null;
            var parts = v.Split('|');
            if (parts.Length != 5) return null;
            return new Cosmetics.Look
            {
                Feet = parts[0], LowerBody = parts[1], UpperBody = parts[2], Head = parts[3],
                Accessories = parts[4].Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).ToList(),
            };
        }

        internal static void SetSlot(int i, Cosmetics.Look l)
        {
            _slots[i].Value = string.Join("|", new[] { l.Feet, l.LowerBody, l.UpperBody, l.Head, string.Join(",", l.Accessories.ToArray()) });
        }

        internal static void SaveLook(Component mgr)
        {
            var look = Cosmetics.Capture(mgr);
            _feet.Value = look.Feet;
            _lower.Value = look.LowerBody;
            _upper.Value = look.UpperBody;
            _head.Value = look.Head;
            _accessories.Value = string.Join(",", look.Accessories.ToArray());
            _saved.Value = true;
        }

        // ---- Wardrobe window (IMGUI) -----------------------------------------------------

        private void OnGUI()
        {
            if (!_open) return;
            if (_panelGo == null || !_panelGo.activeInHierarchy) { _open = false; return; }

            GUI.depth = -1000;
            float scale = Mathf.Max(1f, Screen.height / 1080f);
            var old = GUI.matrix;
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));
            _rect.height = Mathf.Min(640f, Screen.height / scale - 120f);
            _rect.x = Mathf.Clamp(_rect.x, 0f, Screen.width / scale - 100f);
            _rect.y = Mathf.Clamp(_rect.y, 0f, Screen.height / scale - 60f);
            _rect = GUI.Window(0x57A8, _rect, DrawWindow, "Wardrobe");
            GUI.matrix = old;
        }

        private void DrawWindow(int id)
        {
            if (_manager == null) _manager = Cosmetics.FindLocal();
            if (_manager == null)
            {
                GUILayout.Label("No local player found. Join a game first.");
                GUI.DragWindow();
                return;
            }

            _scroll = GUILayout.BeginScrollView(_scroll);

            var sets = Cosmetics.Sets(_manager);
            if (sets.Count > 0)
            {
                GUILayout.Label("<b>Outfits</b>", RichLabel());
                foreach (var s in sets)
                {
                    if (GUILayout.Button(Names.Outfit(s.Name))) ApplySet(s);
                }
                GUILayout.Space(8);
            }

            GUILayout.Label("<b>Mix and match</b>", RichLabel());
            foreach (var part in Cosmetics.Parts) DrawPart(part);

            var accessories = Cosmetics.Ids(_manager, Cosmetics.AccessoryCategory);
            if (accessories.Count > 0)
            {
                GUILayout.Space(8);
                GUILayout.Label("<b>Accessories</b>", RichLabel());
                var active = Cosmetics.ActiveAccessories(_manager);
                foreach (var a in accessories)
                {
                    bool on = active.Contains(a);
                    bool now = GUILayout.Toggle(on, " " + Pretty(a));
                    if (now == on) continue;
                    if (now) Cosmetics.AddAccessory(_manager, a); else Cosmetics.RemoveAccessory(_manager, a);
                    SaveLook(_manager);
                }
            }

            GUILayout.Space(10);
            if (_saved.Value && GUILayout.Button("Forget my saved look (use the game's default next time)"))
                _saved.Value = false;
            GUILayout.Label(_saved.Value ? "Your look is saved and applied when you join." : "Pick something to save your look.");

            GUILayout.EndScrollView();
            GUI.DragWindow(new Rect(0, 0, 10000, 22));
        }

        private void DrawPart(string part)
        {
            var ids = Cosmetics.Ids(_manager, part);
            if (part == "Head") ids.Insert(0, ""); // no head item
            if (ids.Count == 0) return;

            var current = Cosmetics.Active(_manager, part);
            int index = ids.IndexOf(current);
            if (index < 0) index = 0;

            GUILayout.BeginHorizontal();
            GUILayout.Label(PartTitle(part), GUILayout.Width(90));
            if (GUILayout.Button("<", GUILayout.Width(30))) Change(part, ids[(index - 1 + ids.Count) % ids.Count]);
            GUILayout.Label(current.Length == 0 ? "(none)" : Pretty(current), GUILayout.ExpandWidth(true));
            if (GUILayout.Button(">", GUILayout.Width(30))) Change(part, ids[(index + 1) % ids.Count]);
            GUILayout.EndHorizontal();
        }

        private void Change(string part, string id)
        {
            Cosmetics.Set(_manager, part, id);
            SaveLook(_manager);
        }

        private void ApplySet(Cosmetics.OutfitSet s)
        {
            Cosmetics.ApplyLook(_manager, new Cosmetics.Look
            {
                Feet = s.Feet, LowerBody = s.LowerBody, UpperBody = s.UpperBody, Head = s.Head, Accessories = s.Accessories,
            });
            SaveLook(_manager);
        }

        private static string PartTitle(string part)
        {
            return part == "LowerBody" ? "Legs" : part == "UpperBody" ? "Torso" : part;
        }

        private static string Pretty(string id)
        {
            return Names.Single(id);
        }

        private static GUIStyle _rich;
        private static GUIStyle RichLabel()
        {
            return _rich ?? (_rich = new GUIStyle(GUI.skin.label) { richText = true });
        }
    }
}
