using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Events;

namespace NomadDriveWardrobe
{
    /// <summary>
    /// The Wardrobe panel, built from the game's own UI: OptionSelector rows cloned from the Settings
    /// screen (same arrows, fonts, sounds and look), the game's themed surface component for the
    /// background, and a clone of its menu button for "Close". Returns null if a template is missing,
    /// and the plugin falls back to the simple window.
    /// </summary>
    internal class WardrobePanel
    {
        private GameObject _root;
        private Component _mgr;
        private Preview _preview;
        private UnityEngine.Object _rawImage;
        private Type _selType;

        private readonly List<Row> _rows = new List<Row>();
        private Row _outfitRow;
        private readonly Dictionary<string, Row> _partRows = new Dictionary<string, Row>();
        private readonly Dictionary<string, Row> _accRows = new Dictionary<string, Row>();
        private Dictionary<string, List<string>> _partIds = new Dictionary<string, List<string>>();
        private List<Cosmetics.OutfitSet> _sets;
        private bool _updating;

        private class Row { public Component Selector; public GameObject Go; }

        internal bool IsOpen { get { return _root != null; } }

        // ---------------------------------------------------------------------------------

        internal static WardrobePanel TryOpen(Component pauseRootPanel, Component mgr)
        {
            var p = new WardrobePanel();
            try
            {
                if (p.Build(pauseRootPanel, mgr)) return p;
            }
            catch (Exception e)
            {
                Plugin.Log.LogError("Wardrobe panel failed: " + e);
            }
            p.Close();
            return null;
        }

        private bool Build(Component pauseRoot, Component mgr)
        {
            _mgr = mgr;
            _selType = AccessTools.TypeByName("EvilCore.UI.Settings.OptionSelector");
            var imageType = AccessTools.TypeByName("UnityEngine.UI.Image");
            var rawType = AccessTools.TypeByName("UnityEngine.UI.RawImage");
            var tmpType = AccessTools.TypeByName("TMPro.TextMeshProUGUI");
            if (_selType == null || imageType == null || rawType == null || tmpType == null)
            {
                Plugin.Log.LogWarning("Wardrobe panel: missing UI types, using the simple window.");
                return false;
            }

            // A selector from the Settings screen is the template for every row.
            var template = FindTemplateSelector();
            if (template == null)
            {
                Plugin.Log.LogWarning("Wardrobe panel: no OptionSelector found to copy.");
                return false;
            }
            var templateRect = template.GetComponent<RectTransform>();
            float rowW = Mathf.Max(360f, templateRect.rect.width > 10 ? templateRect.rect.width : templateRect.sizeDelta.x);
            float rowH = Mathf.Max(44f, templateRect.rect.height > 10 ? templateRect.rect.height : templateRect.sizeDelta.y);
            var labelTemplate = AccessTools.Property(_selType, "LabelText")?.GetValue(template, null) as Component;

            // Data
            _sets = Cosmetics.Sets(mgr);
            bool female = false;
            foreach (var part in Cosmetics.Parts.Concat(new[] { Cosmetics.AccessoryCategory }))
            {
                var ids = Cosmetics.Ids(mgr, part);
                if (part == "Head") ids.Insert(0, "");
                _partIds[part] = ids;
                if (ids.Any(i => i.IndexOf("Female", StringComparison.OrdinalIgnoreCase) >= 0)) female = true;
            }

            // Root: dim background + window
            var canvas = pauseRoot.GetComponentInParent<Canvas>();
            if (canvas == null) { Plugin.Log.LogWarning("Wardrobe panel: no canvas."); return false; }
            var canvasTf = canvas.rootCanvas.transform;

            _root = new GameObject("WardrobePanel", typeof(RectTransform));
            _root.transform.SetParent(canvasTf, false);
            var rootRect = (RectTransform)_root.transform;
            Stretch(rootRect);
            _root.transform.SetAsLastSibling();

            var dim = NewImage("Dim", _root.transform, imageType, new Color(0f, 0f, 0f, 0.7f));
            Stretch((RectTransform)dim.transform);

            // Layout numbers
            const float margin = 28f, gap = 8f, previewW = 440f, titleH = 90f, footerH = 96f;
            int leftRows = 1 + Cosmetics.Parts.Length;
            int accCount = _partIds[Cosmetics.AccessoryCategory].Count;
            int maxRows = Math.Max(leftRows, accCount);
            float cols = accCount > 0 ? 2f : 1f;
            float winW = margin + cols * (rowW + margin) + previewW + margin;
            float winH = titleH + maxRows * (rowH + gap) + footerH;
            winH = Math.Max(winH, titleH + 620f + footerH);

            var window = NewImage("Window", _root.transform, imageType, new Color(0.075f, 0.08f, 0.09f, 0.97f));
            var winRect = (RectTransform)window.transform;
            winRect.anchorMin = winRect.anchorMax = winRect.pivot = new Vector2(0.5f, 0.5f);
            winRect.sizeDelta = new Vector2(winW, winH);
            float fit = Mathf.Min(1f, 1760f / winW, 980f / winH);
            winRect.localScale = new Vector3(fit, fit, 1f);
            ApplyThemeSurface(window, "Panel");

            // Title
            var title = NewText("Title", window.transform, tmpType, labelTemplate, "Wardrobe", 40f, "Left");
            Place((RectTransform)title.transform, margin, -22f, winW - 2 * margin, 56f);

            // Left column: outfit + parts
            float x = margin, y = -titleH;
            if (_sets.Count > 0)
            {
                var names = new List<string> { "Custom" };
                names.AddRange(_sets.Select(s => Names.Outfit(s.Name)));
                _outfitRow = AddRow(template, window.transform, "Outfit", names, 0, x, y, rowW, rowH, OnOutfit);
                y -= rowH + gap;
            }

            foreach (var part in Cosmetics.Parts)
            {
                var ids = _partIds[part];
                if (ids.Count == 0) continue;
                var pretty = Names.ForGroup(ids.Where(i => i.Length > 0), female);
                var opts = ids.Select(i => i.Length == 0 ? "None" : pretty[i]).ToList();
                string captured = part;
                _partRows[part] = AddRow(template, window.transform, PartTitle(part), opts, 0, x, y, rowW, rowH,
                    idx => OnPart(captured, idx));
                y -= rowH + gap;
            }

            // Middle column: accessories as On/Off rows
            float ax = margin + rowW + margin, ay = -titleH;
            if (accCount > 0)
            {
                var accIds = _partIds[Cosmetics.AccessoryCategory];
                var pretty = Names.ForGroup(accIds, female);
                foreach (var id in accIds)
                {
                    string captured = id;
                    _accRows[id] = AddRow(template, window.transform, pretty[id], new List<string> { "Off", "On" }, 0,
                        ax, ay, rowW, rowH, idx => OnAccessory(captured, idx == 1));
                    ay -= rowH + gap;
                }
            }

            // Right: preview
            float px = winW - margin - previewW;
            var frame = NewImage("PreviewFrame", window.transform, imageType, new Color(0.10f, 0.11f, 0.12f, 1f));
            Place((RectTransform)frame.transform, px, -titleH, previewW, winH - titleH - footerH);
            _rawImage = NewComponentOn("Preview", frame.transform, rawType);
            var rawRect = ((Component)_rawImage).GetComponent<RectTransform>();
            Stretch(rawRect);
            rawRect.offsetMin = new Vector2(4f, 4f);
            rawRect.offsetMax = new Vector2(-4f, -4f);

            _preview = new Preview(mgr, 512, 768);
            if (_preview.Ok)
            {
                AccessTools.Property(rawType, "texture")?.SetValue(_rawImage, _preview.Texture, null);
            }
            else
            {
                var msg = NewText("PreviewNote", frame.transform, tmpType, labelTemplate, "Preview not available", 24f, "Center");
                Stretch((RectTransform)msg.transform);
            }

            // Close button: clone of the pause menu's own button
            var settingsBtn = AccessTools.Field(pauseRoot.GetType(), "settingsButton")?.GetValue(pauseRoot) as Component;
            if (settingsBtn != null)
            {
                var btn = UnityEngine.Object.Instantiate(settingsBtn.gameObject, window.transform);
                btn.name = "WardrobeClose";
                var br = (RectTransform)btn.transform;
                br.anchorMin = br.anchorMax = br.pivot = new Vector2(0.5f, 0f);
                br.anchoredPosition = new Vector2(0f, 24f);
                Plugin.Instance.StartCoroutine(Plugin.ConfigureButton(btn, "Close", () => Plugin.Instance.CloseWardrobe()));
            }

            RefreshFromCharacter();
            _root.SetActive(true);
            Plugin.Log.LogInfo("Wardrobe panel opened (" + _rows.Count + " rows, preview " + (_preview.Ok ? "on" : "off") + ").");
            return true;
        }

        // ---- state sync ------------------------------------------------------------------

        private void RefreshFromCharacter()
        {
            _updating = true;
            try
            {
                foreach (var part in Cosmetics.Parts)
                {
                    if (!_partRows.TryGetValue(part, out var row)) continue;
                    var ids = _partIds[part];
                    int idx = ids.IndexOf(Cosmetics.Active(_mgr, part));
                    SetIndex(row, idx < 0 ? 0 : idx);
                }

                var active = Cosmetics.ActiveAccessories(_mgr);
                foreach (var kv in _accRows) SetIndex(kv.Value, active.Contains(kv.Key) ? 1 : 0);

                if (_outfitRow != null) SetIndex(_outfitRow, MatchingOutfit() + 1);
            }
            finally { _updating = false; }
        }

        private int MatchingOutfit()
        {
            var look = Cosmetics.Capture(_mgr);
            for (int i = 0; i < _sets.Count; i++)
            {
                var s = _sets[i];
                if (s.Feet == look.Feet && s.LowerBody == look.LowerBody && s.UpperBody == look.UpperBody &&
                    s.Head == look.Head && SameSet(s.Accessories, look.Accessories)) return i;
            }
            return -1;
        }

        private static bool SameSet(List<string> a, List<string> b)
        {
            return a.Count == b.Count && a.All(b.Contains);
        }

        private void OnOutfit(int index)
        {
            if (_updating || index <= 0) return;
            var s = _sets[index - 1];
            Cosmetics.ApplyLook(_mgr, new Cosmetics.Look
            {
                Feet = s.Feet, LowerBody = s.LowerBody, UpperBody = s.UpperBody, Head = s.Head, Accessories = s.Accessories,
            });
            Plugin.SaveLook(_mgr);
            Plugin.Instance.StartCoroutine(AfterChange());
        }

        private void OnPart(string part, int index)
        {
            if (_updating) return;
            var ids = _partIds[part];
            if (index < 0 || index >= ids.Count) return;
            Cosmetics.Set(_mgr, part, ids[index]);
            Plugin.SaveLook(_mgr);
            Plugin.Instance.StartCoroutine(AfterChange());
        }

        private void OnAccessory(string id, bool on)
        {
            if (_updating) return;
            if (on) Cosmetics.AddAccessory(_mgr, id); else Cosmetics.RemoveAccessory(_mgr, id);
            Plugin.SaveLook(_mgr);
            Plugin.Instance.StartCoroutine(AfterChange());
        }

        // The server confirms the change a moment later; then refresh the rows and the preview.
        private IEnumerator AfterChange()
        {
            yield return null; yield return null; yield return null;
            if (_root == null) yield break;
            RefreshFromCharacter();
            _preview?.Refresh();
        }

        internal void Tick(float unscaledDt)
        {
            _preview?.Tick(unscaledDt);
        }

        internal void Close()
        {
            _preview?.Close();
            _preview = null;
            if (_root != null) UnityEngine.Object.Destroy(_root);
            _root = null;
        }

        // ---- building blocks --------------------------------------------------------------

        private Component FindTemplateSelector()
        {
            var all = UnityEngine.Object.FindObjectsOfType(_selType, true);
            var isRange = AccessTools.Property(_selType, "IsRangeMode");
            var isText = AccessTools.Property(_selType, "IsTextMode");
            foreach (var o in all)
            {
                var c = o as Component;
                if (c == null) continue;
                if (c.GetComponentInParent<WardrobeMarker>() != null) continue;
                if (isRange != null && (bool)isRange.GetValue(c, null)) continue;
                if (isText != null && (bool)isText.GetValue(c, null)) continue;
                return c;
            }
            return null;
        }

        private Row AddRow(Component template, Transform parent, string label, List<string> options, int index,
            float x, float y, float w, float h, Action<int> onChange)
        {
            var go = UnityEngine.Object.Instantiate(template.gameObject, parent);
            go.name = "Row_" + label;
            var rect = (RectTransform)go.transform;
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(x, y);
            rect.sizeDelta = new Vector2(w, h);
            go.SetActive(true);
            go.AddComponent<WardrobeMarker>();

            var sel = go.GetComponent(_selType);
            var labelText = AccessTools.Property(_selType, "LabelText")?.GetValue(sel, null);
            if (labelText != null) AccessTools.Property(labelText.GetType(), "text")?.SetValue(labelText, label, null);

            var setOptions = AccessTools.Method(_selType, "SetOptions");
            if (setOptions != null)
            {
                var ps = setOptions.GetParameters();
                object arg = options;
                if (ps.Length > 0 && ps[0].ParameterType.IsArray) arg = options.ToArray();
                var args = new object[ps.Length];
                args[0] = arg;
                for (int i = 1; i < ps.Length; i++)
                    args[i] = ps[i].HasDefaultValue ? ps[i].DefaultValue : (ps[i].ParameterType.IsValueType ? Activator.CreateInstance(ps[i].ParameterType) : null);
                setOptions.Invoke(sel, args);
            }
            AccessTools.Method(_selType, "SetValueWithoutNotify")?.Invoke(sel, new object[] { index });

            var ev = _selType.GetEvent("OnValueChanged");
            if (ev != null && ev.EventHandlerType == typeof(Action<int>)) ev.AddEventHandler(sel, onChange);
            else Plugin.Log.LogWarning("OptionSelector.OnValueChanged has an unexpected type: " + ev?.EventHandlerType);

            var row = new Row { Selector = (Component)sel, Go = go };
            _rows.Add(row);
            return row;
        }

        private void SetIndex(Row row, int index)
        {
            AccessTools.Method(_selType, "SetValueWithoutNotify")?.Invoke(row.Selector, new object[] { index });
        }

        private static void Stretch(RectTransform r)
        {
            r.anchorMin = Vector2.zero;
            r.anchorMax = Vector2.one;
            r.offsetMin = Vector2.zero;
            r.offsetMax = Vector2.zero;
        }

        private static void Place(RectTransform r, float x, float y, float w, float h)
        {
            r.anchorMin = r.anchorMax = r.pivot = new Vector2(0f, 1f);
            r.anchoredPosition = new Vector2(x, y);
            r.sizeDelta = new Vector2(w, h);
        }

        private static Component NewComponentOn(string name, Transform parent, Type type)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return go.AddComponent(type);
        }

        private static Component NewImage(string name, Transform parent, Type imageType, Color color)
        {
            var c = NewComponentOn(name, parent, imageType);
            AccessTools.Property(imageType, "color")?.SetValue(c, color, null);
            AccessTools.Property(imageType, "raycastTarget")?.SetValue(c, true, null);
            return c;
        }

        private static Component NewText(string name, Transform parent, Type tmpType, Component fontSource, string text, float size, string align)
        {
            var c = NewComponentOn(name, parent, tmpType);
            AccessTools.Property(tmpType, "text")?.SetValue(c, text, null);
            AccessTools.Property(tmpType, "fontSize")?.SetValue(c, size, null);
            AccessTools.Property(tmpType, "color")?.SetValue(c, new Color(0.93f, 0.93f, 0.93f, 1f), null);
            AccessTools.Property(tmpType, "raycastTarget")?.SetValue(c, false, null);
            var alignProp = AccessTools.Property(tmpType, "alignment");
            if (alignProp != null)
            {
                try { alignProp.SetValue(c, Enum.Parse(alignProp.PropertyType, align), null); } catch { }
            }
            if (fontSource != null)
            {
                var ft = fontSource.GetType();
                var font = AccessTools.Property(ft, "font")?.GetValue(fontSource, null);
                if (font != null) AccessTools.Property(tmpType, "font")?.SetValue(c, font, null);
            }
            return c;
        }

        // Lets the game's theme paint the surface (colour, border, depth) if its component takes over.
        private static void ApplyThemeSurface(Component target, string kind)
        {
            try
            {
                var t = AccessTools.TypeByName("EvilCore.UI.Theme.UIStyledSurface");
                var kt = AccessTools.TypeByName("EvilCore.UI.Theme.UISurfaceKind");
                if (t == null || kt == null) return;
                var comp = target.gameObject.AddComponent(t);
                var field = AccessTools.Field(t, "kind");
                field?.SetValue(comp, Enum.Parse(kt, kind));
            }
            catch (Exception e)
            {
                Plugin.Log.LogInfo("Theme surface not applied: " + e.Message);
            }
        }

        private static string PartTitle(string part)
        {
            return part == "LowerBody" ? "Legs" : part == "UpperBody" ? "Torso" : part;
        }
    }

    /// <summary>Marks our own rows so they are never picked as a template.</summary>
    internal class WardrobeMarker : MonoBehaviour { }
}
