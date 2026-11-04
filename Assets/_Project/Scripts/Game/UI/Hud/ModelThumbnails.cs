using System;
using System.Collections.Generic;
using CityBuilder.Game.Assets;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UIElements;

namespace CityBuilder.Game.UI.Hud
{
    /// <summary>
    /// Vorschaubilder der Bauleiste: rendert ein Modell (eigenes Blender-Prefab aus dem <see cref="OwnModelCatalog"/> oder
    /// Katalog-Mesh aus dem <see cref="AssetCatalog"/>) zur Laufzeit in eine kleine <see cref="RenderTexture"/> –
    /// isometrische 3/4-Ansicht von vorn rechts oben, orthografisch auf die Renderer-Bounds gerahmt, transparenter Hintergrund.
    /// <para>Ablauf: <see cref="Bind"/> meldet ein Bild an (Schlüssel + Bau-Funktion), <see cref="Tick"/> rendert je Frame
    /// höchstens <see cref="PerFrame"/> Bilder (die UI stockt nicht), fertige Bilder bleiben je Schlüssel im Cache. Gibt es kein
    /// Modell, bleibt das <see cref="HudIcon"/> stehen. Das Modell existiert nur während des Renderns (gleich danach deaktiviert
    /// und zerstört), steht weit außerhalb der Karte auf einer eigenen Ebene – die Spielkamera sieht es nie.</para>
    /// <para>URP: Rendern per <see cref="RenderPipeline.SubmitRenderRequest{RequestData}"/> mit
    /// <see cref="RenderPipeline.StandardRequest"/> (URP unterstützt das, s. <c>UniversalRenderPipeline.IsRenderRequestSupported</c>),
    /// ohne Render-Pipeline (Built-in) über <see cref="Camera.Render"/>.</para>
    /// </summary>
    public sealed class ModelThumbnails : IDisposable
    {
        /// <summary>Kantenlänge der Textur (Pixel). Angezeigt wird kleiner – Mipmaps glätten die Verkleinerung.</summary>
        public const int Size = 192;
        /// <summary>Bilder je Frame.</summary>
        public const int PerFrame = 2;
        /// <summary>Eigene, sonst ungenutzte Ebene (Projekteinstellungen: Ebenen 8–31 sind frei).</summary>
        const int Layer = 31;
        static readonly Vector3 StageOrigin = new Vector3(-20000f, 0f, -20000f);
        const float Yaw = 35f, Pitch = 30f;

        sealed class Job
        {
            public string Key;
            public Func<GameObject> Build;
        }

        readonly Dictionary<string, RenderTexture> cache = new Dictionary<string, RenderTexture>();
        readonly HashSet<string> failed = new HashSet<string>();
        readonly Dictionary<string, Job> queued = new Dictionary<string, Job>();
        readonly Queue<Job> pending = new Queue<Job>();
        readonly Dictionary<string, List<Action<Texture>>> waiting = new Dictionary<string, List<Action<Texture>>>();
        GameObject rig;
        Camera cam;
        Light fill;
        bool disposed;

        /// <summary>Fertiges Bild oder null (noch nicht gerendert bzw. kein Modell).</summary>
        public Texture Get(string key) => key != null && cache.TryGetValue(key, out var rt) ? rt : null;

        /// <summary>
        /// Zeigt im Element <paramref name="host"/> das Vorschaubild statt des Icons <paramref name="fallback"/>, sobald es fertig
        /// ist. <paramref name="build"/> erzeugt eine frische Instanz des Modells (null = kein Modell → Icon bleibt).
        /// </summary>
        public void Bind(VisualElement host, HudIcon fallback, string key, Func<GameObject> build)
        {
            if (disposed || host == null || key == null || build == null || failed.Contains(key)) return;
            var image = new Image { scaleMode = ScaleMode.ScaleToFit, pickingMode = PickingMode.Ignore };
            image.AddToClassList("cb-tile__thumb-image");
            image.AddToClassList("cb-hidden");
            host.Add(image);
            Action<Texture> show = tex =>
            {
                if (tex == null) { image.RemoveFromHierarchy(); return; }
                image.image = tex;
                image.RemoveFromClassList("cb-hidden");
                fallback?.AddToClassList("cb-hidden");
            };
            var ready = Get(key);
            if (ready != null) { show(ready); return; }
            if (!waiting.TryGetValue(key, out var list)) waiting[key] = list = new List<Action<Texture>>();
            list.Add(show);
            if (!queued.ContainsKey(key))
            {
                var job = new Job { Key = key, Build = build };
                queued[key] = job;
                pending.Enqueue(job);
            }
        }

        /// <summary>Einmal je Frame (aus <c>GameHud.Update</c>): rendert die nächsten Bilder der Warteschlange.</summary>
        public void Tick()
        {
            if (disposed) return;
            for (int n = 0; n < PerFrame && pending.Count > 0; n++)
            {
                var job = pending.Dequeue();
                queued.Remove(job.Key);
                Texture tex = null;
                try { tex = Render(job); }
                catch (Exception e)
                {
                    Debug.LogWarning($"[CityBuilder] Vorschaubild „{job.Key}“: {e.Message}");
                    tex = null;
                }
                if (tex == null) failed.Add(job.Key);
                if (waiting.TryGetValue(job.Key, out var list))
                {
                    waiting.Remove(job.Key);
                    foreach (var a in list) a(tex);
                }
            }
        }

        RenderTexture Render(Job job)
        {
            var subject = job.Build();
            if (subject == null) return null;
            EnsureRig();
            try
            {
                subject.transform.SetParent(rig.transform, false);
                subject.transform.localPosition = Vector3.zero;
                SetLayer(subject.transform);
                subject.SetActive(true);

                var renderers = subject.GetComponentsInChildren<Renderer>(false);
                bool any = false;
                var bounds = new Bounds(StageOrigin, Vector3.zero);
                foreach (var r in renderers)
                {
                    if (r == null || !r.enabled) continue;
                    if (!any) { bounds = r.bounds; any = true; }
                    else bounds.Encapsulate(r.bounds);
                }
                if (!any || bounds.size.sqrMagnitude < 1e-6f) return null;

                Frame(bounds);
                var rt = new RenderTexture(Size, Size, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB)
                {
                    name = "Thumb " + job.Key,
                    useMipMap = true,
                    autoGenerateMips = true,
                    filterMode = FilterMode.Trilinear,
                    antiAliasing = 1,
                };
                rt.Create();

                // Nebel (Smog-Dunst, Szenennebel) würde das Bild tönen – nur für dieses Bild aus
                bool fog = RenderSettings.fog;
                RenderSettings.fog = false;
                fill.enabled = true;
                try
                {
                    var request = new RenderPipeline.StandardRequest();
                    if (GraphicsSettings.currentRenderPipeline != null && RenderPipeline.SupportsRenderRequest(cam, request))
                    {
                        request.destination = rt;
                        RenderPipeline.SubmitRenderRequest(cam, request);
                    }
                    else
                    {
                        cam.targetTexture = rt;
                        cam.Render();
                        cam.targetTexture = null;
                    }
                }
                finally
                {
                    fill.enabled = false;
                    RenderSettings.fog = fog;
                }
                cache[job.Key] = rt;
                return rt;
            }
            finally
            {
                // sofort unsichtbar (Destroy wirkt erst am Ende des Frames – die Spielkamera soll es nicht mehr sehen)
                subject.SetActive(false);
                UnityEngine.Object.Destroy(subject);
            }
        }

        /// <summary>Orthografische Kamera aus der 3/4-Richtung, Ausschnitt = projizierte Ecken der Bounds + Rand.</summary>
        void Frame(Bounds b)
        {
            var rot = Quaternion.Euler(Pitch, 180f + Yaw, 0f); // Blick auf die Vorderseite (+Z) von vorn rechts oben
            var forward = rot * Vector3.forward;
            var right = rot * Vector3.right;
            var up = rot * Vector3.up;
            float minX = float.MaxValue, maxX = float.MinValue, minY = float.MaxValue, maxY = float.MinValue;
            var c = b.center;
            var e = b.extents;
            for (int i = 0; i < 8; i++)
            {
                var p = new Vector3((i & 1) == 0 ? -e.x : e.x, (i & 2) == 0 ? -e.y : e.y, (i & 4) == 0 ? -e.z : e.z); // Ecke relativ zur Mitte
                float x = Vector3.Dot(p, right), y = Vector3.Dot(p, up);
                minX = Mathf.Min(minX, x); maxX = Mathf.Max(maxX, x);
                minY = Mathf.Min(minY, y); maxY = Mathf.Max(maxY, y);
            }
            float half = Mathf.Max(maxX - minX, maxY - minY) * 0.5f * 1.1f;
            var center = c + right * ((minX + maxX) * 0.5f) + up * ((minY + maxY) * 0.5f);
            float dist = e.magnitude * 2f + 10f;
            cam.transform.SetPositionAndRotation(center - forward * dist, rot);
            cam.orthographicSize = Mathf.Max(0.1f, half);
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = dist * 2f + e.magnitude * 2f;
            fill.transform.rotation = Quaternion.Euler(40f, 180f + Yaw - 60f, 0f);
        }

        void EnsureRig()
        {
            if (rig != null) return;
            rig = new GameObject("Model Thumbnails (UI)") { hideFlags = HideFlags.DontSave };
            rig.transform.position = StageOrigin;
            var camGo = new GameObject("Thumbnail Camera") { hideFlags = HideFlags.DontSave };
            camGo.transform.SetParent(rig.transform, false);
            cam = camGo.AddComponent<Camera>();
            cam.enabled = false; // nur manuell
            cam.orthographic = true;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0f, 0f, 0f, 0f);
            cam.cullingMask = 1 << Layer;
            cam.allowHDR = false;  // HDR-Zwischenpuffer (B10G11R11) hätte keinen Alphakanal
            cam.allowMSAA = false;
            cam.useOcclusionCulling = false;
            cam.aspect = 1f;

            var lightGo = new GameObject("Thumbnail Fill Light") { hideFlags = HideFlags.DontSave };
            lightGo.transform.SetParent(rig.transform, false);
            fill = lightGo.AddComponent<Light>();
            fill.type = LightType.Directional;
            fill.intensity = 0.55f;
            fill.color = new Color(1f, 0.97f, 0.92f);
            fill.shadows = LightShadows.None;
            fill.cullingMask = 1 << Layer;
            fill.enabled = false; // nur während des Renderns an – die Szene sieht dieses Licht nie
        }

        static void SetLayer(Transform t)
        {
            t.gameObject.layer = Layer;
            for (int i = 0; i < t.childCount; i++) SetLayer(t.GetChild(i));
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            foreach (var rt in cache.Values)
                if (rt != null) { rt.Release(); UnityEngine.Object.Destroy(rt); }
            cache.Clear();
            pending.Clear();
            queued.Clear();
            waiting.Clear();
            if (rig != null) UnityEngine.Object.Destroy(rig);
            rig = null;
        }

        // ------------------------------------------------------------ Quellen (Bau-Funktionen je Katalog)

        /// <summary>Eigenes Blender-Prefab (alle Materialien = Paletten-Material wie in den Views) oder null.</summary>
        public static Func<GameObject> Prefab(GameObject prefab)
        {
            if (prefab == null) return null;
            return () =>
            {
                var go = UnityEngine.Object.Instantiate(prefab);
                go.SetActive(false);
                go.hideFlags = HideFlags.DontSave;
                var palette = OwnModelCatalog.Instance != null ? OwnModelCatalog.Instance.Palette : null;
                if (palette != null)
                    foreach (var r in go.GetComponentsInChildren<Renderer>(true))
                    {
                        var mats = new Material[r.sharedMaterials.Length];
                        for (int i = 0; i < mats.Length; i++) mats[i] = palette;
                        r.sharedMaterials = mats;
                    }
                return go;
            };
        }

        /// <summary>Katalog-Mesh (Store- bzw. eigene Gebäude, Bäume) oder null.</summary>
        public static Func<GameObject> Mesh(ModelEntry entry)
        {
            if (entry == null || entry.Mesh == null) return null;
            return () =>
            {
                var go = new GameObject("Thumb " + entry.Name) { hideFlags = HideFlags.DontSave };
                go.SetActive(false);
                go.AddComponent<MeshFilter>().sharedMesh = entry.Mesh;
                go.AddComponent<MeshRenderer>().sharedMaterials = entry.Materials;
                return go;
            };
        }

        public static Func<GameObject> Facility(string id)
        {
            var c = OwnModelCatalog.Instance;
            return c != null ? Prefab(c.Facility(id)) : null;
        }

        public static Func<GameObject> Service(string id)
        {
            var c = OwnModelCatalog.Instance;
            return c != null ? Prefab(c.Service(id)) : null;
        }

        public static Func<GameObject> Transport(string id)
        {
            var c = OwnModelCatalog.Instance;
            return c != null ? Prefab(c.TransportModel(id)) : null;
        }

        /// <summary>Weiteres eigenes Modell nach Rolle + Verwendung (z. B. Infrastruktur „power_pylon“).</summary>
        public static Func<GameObject> Extra(string role, string use)
        {
            var c = OwnModelCatalog.Instance;
            var e = c != null ? c.Find(role, use) : null;
            return e != null ? Prefab(e.Model) : null;
        }
    }
}
