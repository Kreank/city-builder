using System;
using System.Collections.Generic;
using CityBuilder.Game.Assets;
using CityBuilder.Simulation.Citizens;
using CityBuilder.Simulation.Core;
using CityBuilder.Simulation.People;
using UnityEngine;
using UnityEngine.Rendering;
using SimVector2 = System.Numerics.Vector2;

namespace CityBuilder.Game.People
{
    /// <summary>
    /// Zeichnet sichtbare Bürger (zu Fuß unterwegs, im Park, Spaziergang, vor dem Laden, an der Haustür) per GPU-Instancing
    /// ohne GameObjects. Zustand und Position kommen aus dem <see cref="PedestrianPlanner"/> (Simulation, rein lesend):
    /// Fußwege werden zwischen <see cref="Citizen.TripStartTick"/> und <see cref="Citizen.PlannedArrivalTick"/> entlang der
    /// Gehwegroute interpoliert (Zeit = Tick + <see cref="SimulationRunner.TickAlpha"/>), die Geschwindigkeit ergibt sich aus
    /// Routenlänge / Fahrzeit. Leistung: Vorprüfung je Bürger über Start/Ziel, nur in Kameranähe (≤ <see cref="FarDistance"/>,
    /// Sichtkegel), höchstens <see cref="MaxVisible"/> (nächste zuerst), volle Figur mit schwingenden Gliedern bis
    /// <see cref="NearDistance"/>, darüber zwei Quader ohne Schatten; neue Routen höchstens <see cref="RouteBudgetPerFrame"/>
    /// je Frame (Cache je Start/Ziel im Planer).
    /// Eigene Modelle aus <see cref="OwnModelCatalog.People"/> ersetzen die Platzhalter (s. <see cref="PersonModel"/>).
    /// </summary>
    public sealed class PeopleView : MonoBehaviour
    {
        const int Batch = 1023;

        /// <summary>Aktive Darstellung (Auswahl im Inspektor, Verfolgen).</summary>
        public static PeopleView Active { get; private set; }

        /// <summary>
        /// ÖPNV-Erweiterungsstelle (beim Zusammenführen mit „transit“ verbinden): Vorgabe je Bürger, z. B.
        /// <see cref="TransitPose.WaitingAt"/> an der Haltestelle oder <see cref="TransitPose.InVehicle"/> im Bus.
        /// </summary>
        public static Func<Citizen, double, TransitPose> TransitHook;

        [SerializeField] SimulationRunner runner;
        [SerializeField] Material baseMaterial;

        public bool Visible = true;
        [Tooltip("Höchstzahl gleichzeitig gezeichneter Personen (die nächsten zur Kamera).")]
        public int MaxVisible = 3000;
        [Tooltip("Bis zu dieser Entfernung (m) volle Figur mit Gliedern und Schatten.")]
        public float NearDistance = 170f;
        [Tooltip("Darüber werden keine Personen gezeichnet (m).")]
        public float FarDistance = 520f;
        [Tooltip("Neue Gehwegrouten je Frame (Rest im nächsten Frame).")]
        public int RouteBudgetPerFrame = 40;

        /// <summary>Statistik des letzten Frames.</summary>
        public int LastVisible { get; private set; }
        public int LastCandidates { get; private set; }
        public PedestrianPlanner Planner => planner;

        PedestrianPlanner planner;
        SimulationWorld plannerWorld;
        Dictionary<int, long> leisureSince = new Dictionary<int, long>();
        Dictionary<int, long> leisureNext = new Dictionary<int, long>();
        long lastTick = -1;
        float animClock;

        struct Person
        {
            public Citizen Citizen;
            public PedestrianPose Pose;
            public Vector3 Position;
            public float Dist2;
        }

        static readonly Comparison<Person> ByDistance = (a, b) => a.Dist2.CompareTo(b.Dist2);
        readonly List<Person> people = new List<Person>();
        readonly Plane[] frustum = new Plane[6];

        // Zeichnen
        Mesh boxBottom, boxTop;
        Material[] skinMats, hairMats, greyMats, shirtMats, kidMats, seniorMats, trouserMats;
        sealed class Bucket
        {
            public Mesh Mesh;
            public Material Material;
            public bool Shadows;
            public readonly List<Matrix4x4> Matrices = new List<Matrix4x4>();
        }
        readonly Dictionary<(Mesh, Material, bool), Bucket> buckets = new Dictionary<(Mesh, Material, bool), Bucket>();
        readonly Matrix4x4[] buffer = new Matrix4x4[Batch];
        readonly InstancedModels ownNear = new InstancedModels();
        readonly InstancedModels ownFar = new InstancedModels { Shadows = ShadowCastingMode.Off };
        readonly List<PersonModel>[] ownByGroup = new List<PersonModel>[5];
        readonly List<Material> createdMaterials = new List<Material>();

        // Auswahl: gezeichnete Personen des letzten Frames
        readonly List<Vector3> pickCenters = new List<Vector3>();
        readonly List<float> pickRadius = new List<float>();
        readonly List<int> pickIds = new List<int>();

        public void Init(SimulationRunner r, Material mat)
        {
            runner = r;
            baseMaterial = mat;
        }

        void OnEnable() => Active = this;

        void OnDisable()
        {
            if (Active == this) Active = null;
        }

        void Start()
        {
            boxBottom = PeopleFigures.Box(false, "PersonBoxBottom");
            boxTop = PeopleFigures.Box(true, "PersonBoxTop");
            skinMats = Mats(PeopleFigures.Skin, "Skin");
            hairMats = Mats(PeopleFigures.Hair, "Hair");
            greyMats = Mats(PeopleFigures.GreyHair, "GreyHair");
            shirtMats = Mats(PeopleFigures.Shirt, "Shirt");
            kidMats = Mats(PeopleFigures.KidShirt, "KidShirt");
            seniorMats = Mats(PeopleFigures.SeniorShirt, "SeniorShirt");
            trouserMats = Mats(PeopleFigures.Trousers, "Trousers");
            LoadOwnModels();
        }

        void OnDestroy()
        {
            foreach (var m in createdMaterials) if (m != null) Destroy(m);
            if (boxBottom != null) Destroy(boxBottom);
            if (boxTop != null) Destroy(boxTop);
        }

        Material[] Mats(Color[] colors, string name)
        {
            var result = new Material[colors.Length];
            for (int i = 0; i < colors.Length; i++)
            {
                Material m;
                if (baseMaterial != null) m = new Material(baseMaterial);
                else
                {
                    var shader = Shader.Find("Universal Render Pipeline/Lit");
                    m = new Material(shader != null ? shader : Shader.Find("Standard"));
                }
                m.name = $"Person_{name}_{i}";
                m.enableInstancing = true;
                if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", colors[i]); else m.color = colors[i];
                if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0.15f);
                createdMaterials.Add(m);
                result[i] = m;
            }
            return result;
        }

        void LoadOwnModels()
        {
            for (int g = 0; g < ownByGroup.Length; g++) ownByGroup[g] = new List<PersonModel>();
            var catalog = OwnModelCatalog.Instance;
            if (catalog == null || catalog.People == null) return;
            foreach (var e in catalog.People)
            {
                var m = PersonModel.From(e, catalog.Palette);
                if (m != null) ownByGroup[(int)m.Group].Add(m);
            }
        }

        /// <summary>Eigenes Modell für eine Altersgruppe (sonst Erwachsene, skaliert) oder null → Platzhalter.</summary>
        PersonModel OwnModel(FigureAge group, int id, out float extraScale)
        {
            extraScale = 1f;
            var list = ownByGroup[(int)group];
            if (list == null) return null;
            if (list.Count == 0)
            {
                list = ownByGroup[(int)FigureAge.Adult];
                if (list == null || list.Count == 0) return null;
                extraScale = PeopleFigures.ScaleOf(group);
            }
            return list[(int)(PedestrianPlanner.Unit(id, 51) * list.Count) % list.Count];
        }

        // ------------------------------------------------------------ Frame

        void LateUpdate()
        {
            pickCenters.Clear();
            pickRadius.Clear();
            pickIds.Clear();
            LastVisible = 0;
            if (!Visible || runner == null || runner.World == null || boxBottom == null) return;
            var world = runner.World;
            if (planner == null || plannerWorld != world)
            {
                planner = new PedestrianPlanner(world);
                plannerWorld = world;
                leisureSince.Clear();
                lastTick = -1;
            }
            planner.Transit = TransitHook;
            planner.RouteBudget = RouteBudgetPerFrame;
            if (!runner.IsPaused) animClock += Time.deltaTime;

            var cam = Camera.main;
            long tick = world.Clock.Tick;
            double time = tick + runner.TickAlpha;
            TrackLeisure(world, tick);
            if (cam == null) return;

            var camPos = cam.transform.position;
            GeometryUtility.CalculateFrustumPlanes(cam, frustum);
            float far2 = FarDistance * FarDistance;
            float camHeight = camPos.y - (world.Terrain != null ? world.Terrain.HeightAt(SimConvert.ToSim(camPos)) : 0f);
            if (camHeight > FarDistance) return; // zu weit weg – Menschen wären ohnehin nur Pixel
            float reach = Mathf.Sqrt(Mathf.Max(0f, far2 - camHeight * camHeight)) + 40f;
            var cam2 = SimConvert.ToSim(camPos);

            people.Clear();
            foreach (var c in world.Citizens.Citizens)
            {
                if (!MaybeVisible(world, c, cam2, reach)) continue;
                long since = long.MinValue;
                if (c.Activity == Activity.Leisure) leisureSince.TryGetValue(c.Id, out since);
                var pose = planner.Pose(c, time, since);
                if (!pose.IsVisible) continue;
                var pos = SimConvert.ToWorld(pose.Position, pose.Height);
                float d2 = (pos - camPos).sqrMagnitude;
                if (d2 > far2 || !InFrustum(pos)) continue;
                people.Add(new Person { Citizen = c, Pose = pose, Position = pos, Dist2 = d2 });
            }
            LastCandidates = people.Count;
            if (people.Count > MaxVisible)
            {
                people.Sort(ByDistance);
                people.RemoveRange(MaxVisible, people.Count - MaxVisible);
            }

            foreach (var b in buckets.Values) b.Matrices.Clear();
            ownNear.Clear();
            ownFar.Clear();
            float near2 = NearDistance * NearDistance;
            foreach (var p in people) AddFigure(p, p.Dist2 <= near2);
            LastVisible = people.Count;
            Render();
        }

        /// <summary>Freizeitbeginn beobachten (die Simulation speichert ihn nicht): Wechsel in „Freizeit“ seit dem letzten Frame.</summary>
        void TrackLeisure(SimulationWorld world, long tick)
        {
            bool known = lastTick >= 0;
            leisureNext.Clear();
            foreach (var c in world.Citizens.Citizens)
            {
                if (c.Activity != Activity.Leisure) continue;
                if (!leisureSince.TryGetValue(c.Id, out long since)) since = known ? Math.Min(tick, lastTick + 1) : long.MinValue;
                leisureNext[c.Id] = since;
            }
            var swap = leisureSince;
            leisureSince = leisureNext;
            leisureNext = swap;
            lastTick = tick;
        }

        /// <summary>Billige Vorprüfung ohne Route: kann der Bürger überhaupt sichtbar und in Reichweite sein?</summary>
        static bool MaybeVisible(SimulationWorld world, Citizen c, SimVector2 cam, float reach)
        {
            switch (c.Activity)
            {
                case Activity.Travelling:
                    if (c.HasCarTrip || c.Mode == TravelMode.Car) return TransitHook != null && Near(world, c.Location, cam, reach + 200f);
                    var a = PositionOf(world, c.Location);
                    var b = PositionOf(world, c.Destination);
                    if (!a.HasValue || !b.HasValue) return false;
                    return SegmentDistance(cam, a.Value, b.Value) <= reach + 0.3f * SimVector2.Distance(a.Value, b.Value);
                case Activity.Shopping:
                    return c.BusyUntilTick - world.Clock.Tick <= PedestrianPlanner.ShopLingerTicks + 1 && Near(world, c.Location, cam, reach);
                case Activity.Home:
                    return c.BusyUntilTick - world.Clock.Tick <= PedestrianPlanner.DoorTicks + 1 && c.BusyUntilTick >= world.Clock.Tick
                           && Near(world, c.Location, cam, reach);
                case Activity.Leisure:
                {
                    if (c.Household == null) return false;
                    var home = world.City.GetBuilding(c.Household.HomeId);
                    if (home == null) return false;
                    if (c.LeisureParkId <= 0) return SimVector2.Distance(home.Entrance, cam) <= reach + PedestrianPlanner.StrollLength;
                    var park = world.Services?.GetFacility(c.LeisureParkId);
                    return park != null && SegmentDistance(cam, home.Entrance, park.Position) <= reach + 0.3f * SimVector2.Distance(home.Entrance, park.Position);
                }
                default:
                    return TransitHook != null && Near(world, c.Location, cam, reach + 200f);
            }
        }

        static bool Near(SimulationWorld world, Place p, SimVector2 cam, float reach)
        {
            var pos = PositionOf(world, p);
            return pos.HasValue && SimVector2.Distance(pos.Value, cam) <= reach;
        }

        static SimVector2? PositionOf(SimulationWorld world, Place p)
        {
            if (p.IsBuilding) return world.City.GetBuilding(p.BuildingId)?.Entrance;
            if (p.IsOutside) return world.Roads.GetNode(p.ConnectionNodeId)?.Position;
            return null;
        }

        static float SegmentDistance(SimVector2 p, SimVector2 a, SimVector2 b)
        {
            var ab = b - a;
            float l2 = ab.LengthSquared();
            float u = l2 > 1e-6f ? Mathf.Clamp01(SimVector2.Dot(p - a, ab) / l2) : 0f;
            return SimVector2.Distance(a + ab * u, p);
        }

        bool InFrustum(Vector3 p)
        {
            for (int i = 0; i < 6; i++)
                if (frustum[i].GetDistanceToPoint(p) < -2f) return false;
            return true;
        }

        // ------------------------------------------------------------ Figuren

        void AddFigure(in Person p, bool near)
        {
            var c = p.Citizen;
            int id = c.Id;
            var group = PeopleFigures.AgeOf(c.Age);
            float scale = PeopleFigures.ScaleOf(group) * (group == FigureAge.Adult || group == FigureAge.Senior ? 0.95f + 0.1f * PedestrianPlanner.Unit(id, 42) : 1f);
            var pose = p.Pose;

            // Seitliche Streuung (Gehwegbreite 3 m) bzw. kleiner Versatz beim Stehen
            var dir = new Vector3(pose.Direction.X, 0f, pose.Direction.Y);
            if (dir.sqrMagnitude < 1e-6f) dir = Vector3.forward;
            var side = new Vector3(dir.z, 0f, -dir.x);
            var pos = p.Position;
            if (pose.Kind == PedestrianActivity.Walking)
                pos += side * ((PedestrianPlanner.Unit(id, 41) * 2f - 1f) * (pose.OnCrossing ? 0.5f : 0.85f));
            else
                pos += new Vector3(PedestrianPlanner.Unit(id, 43) - 0.5f, 0f, PedestrianPlanner.Unit(id, 44) - 0.5f) * 0.8f;
            var rot = Quaternion.LookRotation(dir);

            // Gang: Schrittfrequenz in Echtzeit (nicht im Zeitraffer der Simulation), Wippen
            bool walking = pose.Kind == PedestrianActivity.Walking;
            PeopleFigures.GaitOf(group, out float stepHz, out float legSwing);
            float phase = animClock * stepHz * Mathf.PI * 2f + PedestrianPlanner.Unit(id, 45) * 6.2832f;
            float s = Mathf.Sin(phase);
            float leg = walking ? s * legSwing : 0f;
            float arm = walking ? -s * legSwing * 0.8f : 0f;
            float bob = walking ? Mathf.Abs(Mathf.Cos(phase)) * 0.035f * scale : 0f;
            if (!walking) rot *= Quaternion.Euler(0f, Mathf.Sin(animClock * 0.7f + id) * 6f, 0f); // Umschauen
            float stoop = group == FigureAge.Senior ? 7f : 0f;

            var own = OwnModel(group, id, out float ownScale);
            if (own != null)
            {
                float sc = own.Scale * ownScale * (group == FigureAge.Adult || group == FigureAge.Senior ? scale / PeopleFigures.ScaleOf(group) : 1f);
                var root = Matrix4x4.TRS(pos + Vector3.up * bob, rot, Vector3.one * sc);
                var target = near ? ownNear : ownFar;
                foreach (var part in own.Parts)
                {
                    float angle = part.Role == PersonPartRole.LegL ? leg : part.Role == PersonPartRole.LegR ? -leg
                        : part.Role == PersonPartRole.ArmL ? arm : part.Role == PersonPartRole.ArmR ? -arm : 0f;
                    var m = angle != 0f && near
                        ? root * Matrix4x4.Translate(part.Pivot) * Matrix4x4.Rotate(Quaternion.Euler(angle, 0f, 0f)) * Matrix4x4.Translate(-part.Pivot) * part.Local
                        : root * part.Local;
                    target.Add(part.Entry, m);
                }
                AddPick(pos, scale, id);
                return;
            }

            // Platzhalter: Farben nach Id
            var skin = skinMats[Index(id, 61, skinMats.Length)];
            var hair = group == FigureAge.Senior ? greyMats[Index(id, 62, greyMats.Length)] : hairMats[Index(id, 62, hairMats.Length)];
            var shirtSet = group == FigureAge.Toddler || group == FigureAge.Child ? kidMats : group == FigureAge.Senior ? seniorMats : shirtMats;
            var shirt = shirtSet[Index(id, 63, shirtSet.Length)];
            var trousers = trouserMats[Index(id, 64, trouserMats.Length)];
            // Kinder: Kopf relativ größer
            float headScale = Mathf.Pow(1f / scale, 0.35f);
            var body = Matrix4x4.TRS(pos + Vector3.up * bob, rot, Vector3.one * scale);
            const float hip = PeopleFigures.HipHeight, torso = PeopleFigures.TorsoHeight, head = PeopleFigures.HeadHeight;

            if (!near)
            {
                Add(boxBottom, shirt, false, body * Matrix4x4.Scale(new Vector3(0.36f, hip + torso, 0.22f)));
                Add(boxBottom, skin, false, body * Matrix4x4.TRS(new Vector3(0f, hip + torso + 0.02f, 0f), Quaternion.identity,
                    new Vector3(0.21f, head + 0.05f, 0.21f) * headScale));
                AddPick(pos, scale, id);
                return;
            }

            for (int k = -1; k <= 1; k += 2)
            {
                float a = k < 0 ? leg : -leg;
                Add(boxTop, trousers, true, body * Matrix4x4.TRS(new Vector3(0.095f * k, hip, 0f), Quaternion.Euler(a, 0f, 0f), new Vector3(0.135f, hip, 0.16f)));
            }
            var chest = body * Matrix4x4.TRS(new Vector3(0f, hip, 0f), Quaternion.Euler(stoop, 0f, 0f), Vector3.one);
            Add(boxBottom, shirt, true, chest * Matrix4x4.Scale(new Vector3(0.36f, torso, 0.21f)));
            for (int k = -1; k <= 1; k += 2)
            {
                float a = k < 0 ? arm : -arm;
                Add(boxTop, shirt, true, chest * Matrix4x4.TRS(new Vector3(0.235f * k, torso - 0.03f, 0f), Quaternion.Euler(a, 0f, 4f * k),
                    new Vector3(0.095f, torso - 0.02f, 0.11f)));
                // Hände
                Add(boxTop, skin, true, chest * Matrix4x4.TRS(new Vector3(0.235f * k, torso - 0.03f, 0f), Quaternion.Euler(a, 0f, 4f * k), Vector3.one)
                                       * Matrix4x4.TRS(new Vector3(0f, -(torso - 0.02f), 0f), Quaternion.identity, new Vector3(0.08f, 0.08f, 0.09f)));
            }
            var neck = chest * Matrix4x4.TRS(new Vector3(0f, torso + 0.02f, 0f), Quaternion.identity, Vector3.one * headScale);
            Add(boxBottom, skin, true, neck * Matrix4x4.Scale(new Vector3(0.2f, head, 0.21f)));
            Add(boxBottom, hair, true, neck * Matrix4x4.TRS(new Vector3(0f, head - 0.04f, -0.012f), Quaternion.identity, new Vector3(0.215f, 0.09f, 0.225f)));
            AddPick(pos, scale, id);
        }

        static int Index(int id, uint salt, int count) => Mathf.Min(count - 1, (int)(PedestrianPlanner.Unit(id, salt) * count));

        void Add(Mesh mesh, Material mat, bool shadows, Matrix4x4 m)
        {
            var key = (mesh, mat, shadows);
            if (!buckets.TryGetValue(key, out var b)) buckets[key] = b = new Bucket { Mesh = mesh, Material = mat, Shadows = shadows };
            b.Matrices.Add(m);
        }

        void AddPick(Vector3 feet, float scale, int id)
        {
            pickCenters.Add(feet + Vector3.up * (0.9f * scale));
            pickRadius.Add(Mathf.Max(1.1f, 1.2f * scale));
            pickIds.Add(id);
        }

        void Render()
        {
            foreach (var b in buckets.Values)
            {
                var list = b.Matrices;
                if (list.Count == 0) continue;
                var rp = new RenderParams(b.Material)
                {
                    shadowCastingMode = b.Shadows ? ShadowCastingMode.On : ShadowCastingMode.Off,
                    receiveShadows = true,
                };
                for (int start = 0; start < list.Count; start += Batch)
                {
                    int n = Mathf.Min(Batch, list.Count - start);
                    list.CopyTo(start, buffer, 0, n);
                    Graphics.RenderMeshInstanced(rp, b.Mesh, 0, buffer, n);
                }
            }
            ownNear.Render();
            ownFar.Render();
        }

        // ------------------------------------------------------------ Auswahl / Verfolgen

        /// <summary>Strahl gegen die im letzten Frame gezeichneten Personen (Kugel um die Körpermitte).</summary>
        public bool Pick(Ray ray, float maxDistance, out int citizenId, out float distance)
        {
            citizenId = 0;
            distance = maxDistance;
            bool hit = false;
            for (int i = 0; i < pickCenters.Count; i++)
            {
                var oc = pickCenters[i] - ray.origin;
                float t = Vector3.Dot(oc, ray.direction);
                if (t < 0f || t >= distance) continue;
                float r = pickRadius[i];
                if ((oc - ray.direction * t).sqrMagnitude > r * r) continue;
                distance = t;
                citizenId = pickIds[i];
                hit = true;
            }
            return hit;
        }

        /// <summary>Aktuelle Position (Körpermitte) eines sichtbaren Bürgers – auch außerhalb der Kamera (Verfolgen).</summary>
        public bool TryGetPosition(Citizen c, out Vector3 position)
        {
            position = default;
            if (c == null || planner == null || runner == null || runner.World == null) return false;
            for (int i = 0; i < pickIds.Count; i++)
                if (pickIds[i] == c.Id) { position = pickCenters[i]; return true; }
            long since = long.MinValue;
            if (c.Activity == Activity.Leisure) leisureSince.TryGetValue(c.Id, out since);
            int budget = planner.RouteBudget;
            planner.RouteBudget = Math.Max(budget, 2);
            var pose = planner.Pose(c, runner.World.Clock.Tick + runner.TickAlpha, since);
            planner.RouteBudget = budget;
            if (!pose.IsVisible) return false;
            position = SimConvert.ToWorld(pose.Position, pose.Height + 0.9f);
            return true;
        }
    }
}
