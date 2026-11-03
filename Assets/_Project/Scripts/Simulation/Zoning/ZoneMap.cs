using System;
using System.Numerics;

namespace CityBuilder.Simulation.Zoning
{
    /// <summary>
    /// Was der Spieler gemalt hat, als Raster (4 m) über der Welt – unabhängig von den Zellen,
    /// damit die Zonierung erhalten bleibt, wenn Straßen zerteilt/umgebaut werden.
    /// </summary>
    public sealed partial class ZoneMap
    {
        const float Cell = 4f;
        readonly byte[] data;
        readonly int resolution;
        readonly Vector2 origin;

        public int Version { get; private set; }

        public ZoneMap(Vector2 origin, float sizeMeters)
        {
            this.origin = origin;
            resolution = (int)MathF.Ceiling(sizeMeters / Cell);
            data = new byte[resolution * resolution];
        }

        public ZoneType Get(Vector2 p)
        {
            int x = (int)MathF.Floor((p.X - origin.X) / Cell), y = (int)MathF.Floor((p.Y - origin.Y) / Cell);
            if (x < 0 || y < 0 || x >= resolution || y >= resolution) return ZoneType.None;
            return (ZoneType)data[y * resolution + x];
        }

        /// <summary>Setzt die Zone für eine Zelle (3 × 3 Rasterpunkte um den Mittelpunkt).</summary>
        public void SetCell(Vector2 center, ZoneType zone)
        {
            int cx = (int)MathF.Floor((center.X - origin.X) / Cell), cy = (int)MathF.Floor((center.Y - origin.Y) / Cell);
            for (int y = cy - 1; y <= cy + 1; y++)
            for (int x = cx - 1; x <= cx + 1; x++)
            {
                if (x < 0 || y < 0 || x >= resolution || y >= resolution) continue;
                data[y * resolution + x] = (byte)zone;
            }
            Version++;
        }
    }
}
