using System.Collections.Generic;
using CityBuilder.Game.Zoning;
using CityBuilder.Simulation.Safety;
using CityBuilder.Simulation.Services;
using UnityEngine;

namespace CityBuilder.Game.Services
{
    /// <summary>
    /// Sicherheit (nur Darstellung, liest <c>world.Safety</c>): Flammen und Rauch an brennenden Gebäuden (flackernde Quader,
    /// Größe nach Brandstärke), verkohlte Reste an Brandruinen, Gerüst an Gebäuden in Sanierung, Blaulicht auf Dienstfahrzeugen
    /// im Einsatz (Brand, Polizei, Rettung). Alles per GPU-Instancing aus Grundkörpern, keine Partikelsysteme/Assets nötig.
    /// Begleiter am <c>RoadTool</c>.
    /// </summary>
    public sealed class SafetyView : MonoBehaviour
    {
        SimulationRunner runner;
        BuildingView buildings;
        Mesh cube;
        Material flameHot, flameCore, smoke, charred, scaffold, blue, red;
        readonly List<Matrix4x4> hot = new List<Matrix4x4>(), core = new List<Matrix4x4>(), smokes = new List<Matrix4x4>(),
            ruins = new List<Matrix4x4>(), scaffolds = new List<Matrix4x4>(), blues = new List<Matrix4x4>(), reds = new List<Matrix4x4>();
        readonly Matrix4x4[] buffer = new Matrix4x4[1023];

        public void Init(SimulationRunner r, Material lit, Material transparent)
        {
            runner = r;
            flameHot = Make(lit, new Color(1f, 0.42f, 0.05f), "FireOuter", new Color(1f, 0.35f, 0f) * 2.2f);
            flameCore = Make(lit, new Color(1f, 0.85f, 0.25f), "FireCore", new Color(1f, 0.8f, 0.2f) * 3f);
            smoke = Make(transparent != null ? transparent : lit, new Color(0.22f, 0.22f, 0.24f, 0.55f), "FireSmoke", Color.black);
            charred = Make(lit, new Color(0.12f, 0.1f, 0.09f), "FireRuin", Color.black);
            scaffold = Make(lit, new Color(0.62f, 0.6f, 0.55f), "RepairScaffold", Color.black);
            blue = Make(lit, new Color(0.15f, 0.35f, 1f), "BlueLight", new Color(0.2f, 0.4f, 1f) * 4f);
            red = Make(lit, new Color(1f, 0.1f, 0.1f), "RedLight", new Color(1f, 0.1f, 0.1f) * 3f);
        }

        static Material Make(Material source, Color c, string name, Color emission)
        {
            var m = new Material(source) { name = name, enableInstancing = true };
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c); else m.color = c;
            if (emission.maxColorComponent > 0f && m.HasProperty("_EmissionColor"))
            {
                m.EnableKeyword("_EMISSION");
                m.SetColor("_EmissionColor", emission);
            }
            return m;
        }

        void Start()
        {
            var tmp = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube = tmp.GetComponent<MeshFilter>().sharedMesh;
            Destroy(tmp);
            buildings = GetComponent<BuildingView>();
        }

        void OnDestroy()
        {
            foreach (var m in new[] { flameHot, flameCore, smoke, charred, scaffold, blue, red }) if (m != null) Destroy(m);
        }

        void LateUpdate()
        {
            var w = runner != null ? runner.World : null;
            if (w?.Safety == null || cube == null || flameHot == null) return;
            hot.Clear(); core.Clear(); smokes.Clear(); ruins.Clear(); scaffolds.Clear(); blues.Clear(); reds.Clear();
            float t = Time.time;
            var safety = w.Safety;

            foreach (var f in safety.ActiveFires)
            {
                var b = w.City.GetBuilding(f.BuildingId);
                if (b == null) continue;
                float h = buildings != null ? buildings.VisualHeight(b) : 8f;
                float i = Mathf.Clamp01(f.Intensity);
                var front = new Vector3(b.Front.X, 0f, b.Front.Y);
                var side = new Vector3(-b.Front.Y, 0f, b.Front.X);
                var top = SimConvert.ToWorld(b.Position, b.Height + h);
                int tongues = 2 + Mathf.RoundToInt(i * 6f);
                for (int k = 0; k < tongues; k++)
                {
                    // feste Lage je Flamme (Hash aus Brand + Index), Flackern über die Zeit
                    float u = Frac(Mathf.Sin((f.Id * 13 + k * 7) * 12.9898f) * 43758.5f) - 0.5f;
                    float v = Frac(Mathf.Sin((f.Id * 29 + k * 3) * 78.233f) * 12345.6f) - 0.5f;
                    var p = top + side * (u * b.Width * 0.7f) + front * (v * b.Depth * 0.7f);
                    float flicker = 0.75f + 0.25f * Mathf.Sin(t * (7f + k) + f.Id + k * 1.7f);
                    float size = (1.2f + 3.5f * i) * flicker;
                    hot.Add(Matrix4x4.TRS(p + Vector3.up * size * 0.5f, Quaternion.Euler(0f, k * 37f + t * 20f, 0f), new Vector3(size * 0.7f, size * 1.6f, size * 0.7f)));
                    core.Add(Matrix4x4.TRS(p + Vector3.up * size * 0.35f, Quaternion.Euler(0f, k * 53f, 0f), new Vector3(size * 0.4f, size * 0.9f, size * 0.4f)));
                }
                // Rauchsäule: Wolken steigen auf und wachsen (Zyklus 4 s), dichter bei starkem Brand
                int puffs = 3 + Mathf.RoundToInt(i * 6f);
                for (int k = 0; k < puffs; k++)
                {
                    float phase = Frac(t * 0.25f + k / (float)puffs);
                    float size = (2f + 6f * i) * (0.5f + phase);
                    var drift = new Vector3(0.6f, 0f, 0.3f) * (phase * 14f);
                    smokes.Add(Matrix4x4.TRS(top + Vector3.up * (4f + phase * (18f + 30f * i)) + drift, Quaternion.Euler(phase * 90f, k * 40f, 0f), Vector3.one * size));
                }
            }

            foreach (var r in safety.Repairs)
            {
                var b = w.City.GetBuilding(r.BuildingId);
                if (b == null) continue;
                var look = Quaternion.LookRotation(new Vector3(b.Front.X, 0f, b.Front.Y));
                var basePos = SimConvert.ToWorld(b.Position, b.Height);
                var side = new Vector3(-b.Front.Y, 0f, b.Front.X);
                var front = new Vector3(b.Front.X, 0f, b.Front.Y);
                if (r.IsRuin)
                {
                    // verkohlte Mauerreste: Bodenplatte + drei unterschiedlich hohe Wandstücke
                    ruins.Add(Matrix4x4.TRS(basePos + Vector3.up * 0.3f, look, new Vector3(b.Width * 0.85f, 0.6f, b.Depth * 0.85f)));
                    ruins.Add(Matrix4x4.TRS(basePos - side * (b.Width * 0.4f) + Vector3.up * 2f, look, new Vector3(0.6f, 4f, b.Depth * 0.7f)));
                    ruins.Add(Matrix4x4.TRS(basePos + front * (b.Depth * 0.4f) + Vector3.up * 1.2f, look, new Vector3(b.Width * 0.5f, 2.4f, 0.6f)));
                    ruins.Add(Matrix4x4.TRS(basePos + side * (b.Width * 0.38f) - front * (b.Depth * 0.2f) + Vector3.up * 3f, look, new Vector3(0.6f, 6f, b.Depth * 0.35f)));
                    // Glutnester (rot glimmend)
                    float glow = 0.4f + 0.2f * Mathf.Sin(t * 3f + b.Id);
                    reds.Add(Matrix4x4.TRS(basePos + Vector3.up * 0.7f, look, new Vector3(glow, glow * 0.4f, glow)));
                }
                else
                {
                    // Sanierung: grauer Rohbau mit Gerüst
                    float h = 6f;
                    scaffolds.Add(Matrix4x4.TRS(basePos + Vector3.up * (h * 0.5f), look, new Vector3(b.Width * 0.8f, h, b.Depth * 0.8f)));
                    foreach (var s in new[] { -0.45f, 0.45f })
                    {
                        scaffolds.Add(Matrix4x4.TRS(basePos + side * (b.Width * s) + front * (b.Depth * 0.45f) + Vector3.up * (h * 0.6f), look, new Vector3(0.25f, h * 1.2f, 0.25f)));
                    }
                    scaffolds.Add(Matrix4x4.TRS(basePos + front * (b.Depth * 0.46f) + Vector3.up * (h * 1.15f), look, new Vector3(b.Width * 0.92f, 0.2f, 0.6f)));
                }
            }

            // Blaulicht: Dienstfahrzeuge im Einsatz (fahrend aus dem Verkehr, stehend am Halt)
            var fleet = w.ServiceVehicles;
            if (fleet != null && fleet.Enabled)
            {
                bool on = Mathf.Repeat(t * 3f, 1f) < 0.5f;
                foreach (var v in w.Traffic.Vehicles)
                {
                    var u = fleet.UnitForTrip(v.TripId);
                    if (u == null || !Emergency(u)) continue;
                    AddLight(SimConvert.ToWorld(v.Position, v.Height), v.Direction, u.Kind, on);
                }
                foreach (var u in fleet.ParkedUnits)
                    if (Emergency(u)) AddLight(SimConvert.ToWorld(u.ParkPosition, u.ParkHeight), u.ParkDirection, u.Kind, on);
            }

            Draw(hot, flameHot, false);
            Draw(core, flameCore, false);
            Draw(smokes, smoke, false);
            Draw(ruins, charred, true);
            Draw(scaffolds, scaffold, true);
            Draw(blues, blue, false);
            Draw(reds, red, false);
        }

        static bool Emergency(ServiceUnit u) =>
            u.Mission == ServiceMission.FireCall || u.Mission == ServiceMission.CrimeCall || u.Mission == ServiceMission.Rescue;

        void AddLight(Vector3 pos, System.Numerics.Vector2 dir, ServiceVehicleKind kind, bool on)
        {
            float roof = kind == ServiceVehicleKind.FireTruck ? 3.3f : kind == ServiceVehicleKind.Ambulance ? 2.9f : 1.7f;
            var d = dir.LengthSquared() > 1e-6f ? new Vector3(dir.X, 0f, dir.Y).normalized : Vector3.forward;
            var side = new Vector3(-d.z, 0f, d.x);
            var rot = Quaternion.LookRotation(d);
            // zwei Leuchten im Wechsel
            var a = pos + Vector3.up * roof + side * 0.45f;
            var b = pos + Vector3.up * roof - side * 0.45f;
            blues.Add(Matrix4x4.TRS(on ? a : b, rot, new Vector3(0.45f, 0.3f, 0.45f)));
        }

        static float Frac(float x) => x - Mathf.Floor(x);

        void Draw(List<Matrix4x4> list, Material material, bool shadows)
        {
            if (list.Count == 0 || material == null) return;
            var rp = new RenderParams(material)
            {
                shadowCastingMode = shadows ? UnityEngine.Rendering.ShadowCastingMode.On : UnityEngine.Rendering.ShadowCastingMode.Off,
                receiveShadows = shadows,
            };
            for (int start = 0; start < list.Count; start += buffer.Length)
            {
                int n = Mathf.Min(buffer.Length, list.Count - start);
                list.CopyTo(start, buffer, 0, n);
                Graphics.RenderMeshInstanced(rp, cube, 0, buffer, n);
            }
        }
    }
}
