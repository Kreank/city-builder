using UnityEngine;
using SimVector2 = System.Numerics.Vector2;

namespace CityBuilder.Game
{
    /// <summary>Umrechnung Simulation (x, y auf der Ebene) ↔ Unity (x, 0, z).</summary>
    public static class SimConvert
    {
        public static Vector3 ToWorld(SimVector2 p, float height = 0f) => new Vector3(p.X, height, p.Y);
        public static SimVector2 ToSim(Vector3 p) => new SimVector2(p.x, p.z);
    }
}
