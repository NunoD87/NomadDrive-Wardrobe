using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace NomadDriveWardrobe
{
    /// <summary>
    /// Live preview of your character: a hidden camera renders the character into a RenderTexture.
    /// While it is open the character's renderers are moved to an unused layer (and unhidden if the
    /// first-person view hides them) so the camera sees only the character. Everything is restored on Close.
    /// </summary>
    internal class Preview
    {
        private const int PreviewLayer = 31;

        private readonly Component _manager;
        private Camera _cam;
        private RenderTexture _rt;
        private Transform _modelRoot;
        private float _yaw = 200f;

        private class Saved { public Renderer R; public int Layer; public bool Enabled; public UnityEngine.Rendering.ShadowCastingMode Shadow; }
        private readonly List<Saved> _saved = new List<Saved>();

        internal Texture Texture { get { return _rt; } }
        internal bool Ok { get; private set; }

        internal Preview(Component manager, int width, int height)
        {
            _manager = manager;
            try
            {
                _modelRoot = FindModelRoot(manager);
                if (_modelRoot == null) { Plugin.Log.LogWarning("Preview: character model root not found."); return; }

                _rt = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32) { name = "WardrobePreview" };
                _rt.Create();

                var go = new GameObject("WardrobePreviewCamera");
                UnityEngine.Object.DontDestroyOnLoad(go);
                _cam = go.AddComponent<Camera>();
                _cam.targetTexture = _rt;
                _cam.cullingMask = 1 << PreviewLayer;
                _cam.clearFlags = CameraClearFlags.SolidColor;
                _cam.backgroundColor = new Color(0.10f, 0.11f, 0.12f, 1f);
                _cam.fieldOfView = 28f;
                _cam.nearClipPlane = 0.05f;
                _cam.farClipPlane = 30f;
                _cam.allowHDR = false;
                _cam.depth = -100;
                ConfigureHdrp(go);

                IsolateCharacter();
                Ok = _saved.Count > 0;
                if (!Ok) Plugin.Log.LogWarning("Preview: no character renderers found.");
                else Plugin.Log.LogInfo("Preview: " + _saved.Count + " renderers, camera ready.");
            }
            catch (Exception e)
            {
                Plugin.Log.LogError("Preview setup failed: " + e);
            }
        }

        private static Transform FindModelRoot(Component manager)
        {
            // The cosmetics sit below the animated character model; its Animator is the root.
            var animator = manager.GetComponentInParent<Animator>();
            if (animator != null) return animator.transform;
            var cosmeticRoot = AccessTools.Field(manager.GetType(), "cosmeticRoot")?.GetValue(manager) as Transform;
            return cosmeticRoot != null ? cosmeticRoot.parent : manager.transform;
        }

        private void IsolateCharacter()
        {
            foreach (var r in _modelRoot.GetComponentsInChildren<Renderer>(false))
            {
                if (r == null || !r.gameObject.activeInHierarchy) continue;
                _saved.Add(new Saved { R = r, Layer = r.gameObject.layer, Enabled = r.enabled, Shadow = r.shadowCastingMode });
            }
            Apply();
        }

        // Re-run after the look changes: newly activated parts need the layer and visibility too.
        internal void Refresh()
        {
            if (_modelRoot == null) return;
            var known = new HashSet<Renderer>(_saved.Select(s => s.R));
            foreach (var r in _modelRoot.GetComponentsInChildren<Renderer>(false))
            {
                if (r == null || !r.gameObject.activeInHierarchy || known.Contains(r)) continue;
                _saved.Add(new Saved { R = r, Layer = r.gameObject.layer, Enabled = r.enabled, Shadow = r.shadowCastingMode });
            }
            Apply();
        }

        private void Apply()
        {
            foreach (var s in _saved)
            {
                if (s.R == null) continue;
                s.R.gameObject.layer = PreviewLayer;
                s.R.enabled = true;
                s.R.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            }
        }

        /// <summary>Turntable: call every frame while the panel is open.</summary>
        internal void Tick(float unscaledDt)
        {
            if (!Ok || _cam == null || _modelRoot == null) return;
            _yaw += 24f * unscaledDt;

            // Frame the visible renderers.
            Bounds b = default(Bounds);
            bool any = false;
            foreach (var s in _saved)
            {
                if (s.R == null || !s.R.gameObject.activeInHierarchy) continue;
                if (!any) { b = s.R.bounds; any = true; } else b.Encapsulate(s.R.bounds);
            }
            if (!any) return;

            float height = Mathf.Max(0.5f, b.size.y);
            float dist = (height * 0.5f) / Mathf.Tan(_cam.fieldOfView * 0.5f * Mathf.Deg2Rad) * 1.25f;
            var center = b.center;
            var dir = Quaternion.Euler(0f, _modelRoot.eulerAngles.y + _yaw, 0f) * Vector3.forward;
            _cam.transform.position = center + dir * dist + Vector3.up * (height * 0.05f);
            _cam.transform.LookAt(center);
        }

        internal void Close()
        {
            foreach (var s in _saved)
            {
                if (s.R == null) continue;
                s.R.gameObject.layer = s.Layer;
                s.R.enabled = s.Enabled;
                s.R.shadowCastingMode = s.Shadow;
            }
            _saved.Clear();

            if (_cam != null) UnityEngine.Object.Destroy(_cam.gameObject);
            if (_rt != null) { _rt.Release(); UnityEngine.Object.Destroy(_rt); }
            _cam = null;
            _rt = null;
            Ok = false;
        }

        // HDRP cameras need extra data to draw on a plain colour instead of the sky.
        private static void ConfigureHdrp(GameObject camGo)
        {
            try
            {
                var t = AccessTools.TypeByName("UnityEngine.Rendering.HighDefinition.HDAdditionalCameraData");
                if (t == null) return;
                var data = camGo.GetComponent(t) ?? camGo.AddComponent(t);

                var modeProp = AccessTools.Property(t, "clearColorMode");
                if (modeProp != null)
                    modeProp.SetValue(data, Enum.Parse(modeProp.PropertyType, "Color"), null);
                var bg = AccessTools.Property(t, "backgroundColorHDR");
                bg?.SetValue(data, new Color(0.10f, 0.11f, 0.12f, 1f), null);
            }
            catch (Exception e)
            {
                Plugin.Log.LogInfo("Preview: could not set the HDRP background (" + e.Message + "); it may show the sky.");
            }
        }
    }
}
