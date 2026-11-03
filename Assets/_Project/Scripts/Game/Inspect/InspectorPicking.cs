using CityBuilder.Game.Roads;
using CityBuilder.Simulation.Core;
using CityBuilder.Simulation.Services;
using CityBuilder.Simulation.Traffic;
using CityBuilder.Simulation.Utilities;
using CityBuilder.Simulation.Zoning;
using UnityEngine;

namespace CityBuilder.Game.Inspect
{
    public enum SelectionKind : byte { None, Building, Vehicle, Facility, Citizen, Service }

    /// <summary>Was im Inspektor ausgewählt ist (nur Art + Id – das Objekt wird jedes Frame neu nachgeschlagen).</summary>
    public readonly struct Selection
    {
        public readonly SelectionKind Kind;
        public readonly int Id;

        public Selection(SelectionKind kind, int id)
        {
            Kind = kind;
            Id = id;
        }

        public static readonly Selection None = default;
        public bool IsNone => Kind == SelectionKind.None;
        public bool Equals(Selection o) => Kind == o.Kind && Id == o.Id;
        public override string ToString() => $"{Kind} #{Id}";
    }

    /// <summary>
    /// Auswahl per Mausstrahl: Gebäude und Versorgungsbauwerke als gedrehte Quader (Grundstück × sichtbare Höhe),
    /// Fahrzeuge als Kugel um die interpolierte Position. Gewinnt der vorderste Treffer; kleine Fahrzeuge haben
    /// etwas Vorrang, damit man sie vor Gebäuden anklicken kann. Liest nur.
    /// </summary>
    public static class InspectorPicking
    {
        /// <summary>Fahrzeuge gelten bis zu so viel Meter hinter einem Gebäudetreffer noch als getroffen.</summary>
        const float VehicleBias = 6f;
        const float MaxDistance = 6000f;

        public static Selection Pick(Ray ray, SimulationWorld world, float tickAlpha, System.Func<Building, float> buildingHeight)
        {
            float best = MaxDistance;
            var result = Selection.None;

            foreach (var b in world.City.Buildings)
            {
                float h = Mathf.Max(2f, buildingHeight(b));
                if (RayBox(ray, b.Position, b.Front, b.Width, b.Depth, b.Height, h, out float t) && t < best)
                {
                    best = t;
                    result = new Selection(SelectionKind.Building, b.Id);
                }
            }
            foreach (var f in world.Utilities.Facilities)
            {
                if (RayBox(ray, f.Position, f.Front, f.Type.Width, f.Type.Depth, f.Height, FacilityHeight(f), out float t) && t < best)
                {
                    best = t;
                    result = new Selection(SelectionKind.Facility, f.Id);
                }
            }
            if (world.Services != null) // G4 Dienstgebäude
                foreach (var f in world.Services.Facilities)
                {
                    if (RayBox(ray, f.Position, f.Front, f.Type.Width, f.Type.Depth, f.Height, ServiceHeight(f), out float t) && t < best)
                    {
                        best = t;
                        result = new Selection(SelectionKind.Service, f.Id);
                    }
                }

            float bestVehicle = best + VehicleBias;
            foreach (var v in world.Traffic.Vehicles)
            {
                if (v.Lane == null && v.Connection == null) continue;
                var p = VehiclePosition(v, tickAlpha);
                float radius = Mathf.Clamp(v.Length * 0.5f, 2.5f, 8f);
                if (RaySphere(ray, p, radius, out float t) && t < bestVehicle)
                {
                    bestVehicle = t;
                    result = new Selection(SelectionKind.Vehicle, v.Id);
                }
            }
            return result;
        }

        /// <summary>Interpolierte Fahrzeugmitte in Unity-Koordinaten (wie <see cref="Traffic.VehicleView"/>).</summary>
        public static Vector3 VehiclePosition(Vehicle v, float alpha)
        {
            var p = System.Numerics.Vector2.Lerp(v.PrevPosition, v.Position, alpha);
            float h = Mathf.Lerp(v.PrevHeight, v.Height, alpha);
            return SimConvert.ToWorld(p, h + RoadMeshBuilder.RoadHeight + 1f);
        }

        /// <summary>Grobe sichtbare Höhe der Platzhalter aus <c>UtilityView</c>.</summary>
        public static float FacilityHeight(UtilityFacility f)
        {
            switch (f.Type.Id)
            {
                case "power_coal": return 50f;
                case "power_gas": return 30f;
                case "power_wind": return 50f;
                case "power_solar": return 3f;
                case "water_tower": return 24f;
                case "water_pump": return 7f;
                default: return 10f;
            }
        }

        /// <summary>Grobe sichtbare Höhe der Platzhalter aus <c>ServiceView</c> (G4).</summary>
        public static float ServiceHeight(ServiceFacility f)
        {
            switch (f.Type.Id)
            {
                case "hospital": return 22f;
                case "fire_station": return 18f;
                case "incinerator": return 40f;
                case "school_high": return 12f;
                case "landfill": return 6f;
                case "park_small":
                case "park_large": return 6f;
                default: return 9f;
            }
        }

        static bool RaySphere(Ray ray, Vector3 center, float radius, out float t)
        {
            var oc = center - ray.origin;
            t = Vector3.Dot(oc, ray.direction);
            if (t < 0f) return false;
            return (oc - ray.direction * t).sqrMagnitude <= radius * radius;
        }

        /// <summary>Strahl gegen gedrehten Quader (Grundriss width × depth um center, Höhe base..base+height).</summary>
        static bool RayBox(Ray ray, System.Numerics.Vector2 center, System.Numerics.Vector2 front, float width, float depth,
            float baseHeight, float height, out float t)
        {
            var c = SimConvert.ToWorld(center, baseHeight + height * 0.5f);
            var axisZ = new Vector3(front.X, 0f, front.Y).normalized;
            var axisX = new Vector3(-front.Y, 0f, front.X).normalized;
            var o = ray.origin - c;
            var ro = new Vector3(Vector3.Dot(o, axisX), o.y, Vector3.Dot(o, axisZ));
            var rd = new Vector3(Vector3.Dot(ray.direction, axisX), ray.direction.y, Vector3.Dot(ray.direction, axisZ));
            var half = new Vector3(width * 0.5f, height * 0.5f, depth * 0.5f);

            float tMin = 0f, tMax = MaxDistance;
            for (int i = 0; i < 3; i++)
            {
                if (Mathf.Abs(rd[i]) < 1e-6f)
                {
                    if (Mathf.Abs(ro[i]) > half[i]) { t = 0f; return false; }
                    continue;
                }
                float inv = 1f / rd[i];
                float t1 = (-half[i] - ro[i]) * inv, t2 = (half[i] - ro[i]) * inv;
                if (t1 > t2) { float x = t1; t1 = t2; t2 = x; }
                tMin = Mathf.Max(tMin, t1);
                tMax = Mathf.Min(tMax, t2);
                if (tMin > tMax) { t = 0f; return false; }
            }
            t = tMin;
            return true;
        }
    }
}
