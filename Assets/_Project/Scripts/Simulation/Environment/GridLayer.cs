using System;
using System.Numerics;

namespace CityBuilder.Simulation.Environment
{
    /// <summary>
    /// Ein Raster aus float-Werten über der Welt (z. B. Lärm, Abgas, Fruchtbarkeit,
    /// Rohstoffvorkommen). Die Bebauung ist frei – die Umwelt wird trotzdem in
    /// Zellen gerechnet, weil das schnell und gut skalierbar ist.
    /// </summary>
    public sealed class GridLayer
    {
        readonly float[] cells;
        float[] scratch;

        public string Name { get; }
        public int Width { get; }
        public int Height { get; }
        public float CellSize { get; }
        /// <summary>Weltposition der unteren linken Ecke von Zelle (0,0).</summary>
        public Vector2 Origin { get; }

        /// <summary>Wird bei jeder Änderung erhöht – Views aktualisieren sich nur, wenn sich das ändert.</summary>
        public int Version { get; private set; }

        public GridLayer(string name, Vector2 origin, int width, int height, float cellSize)
        {
            if (width <= 0 || height <= 0) throw new ArgumentOutOfRangeException(nameof(width));
            if (cellSize <= 0) throw new ArgumentOutOfRangeException(nameof(cellSize));
            Name = name;
            Origin = origin;
            Width = width;
            Height = height;
            CellSize = cellSize;
            cells = new float[width * height];
        }

        public float this[int x, int y]
        {
            get => cells[y * Width + x];
            set { cells[y * Width + x] = value; Version++; }
        }

        /// <summary>Direkter Lesezugriff (Index = y * Width + x), z. B. zum Füllen einer Textur.</summary>
        public float[] RawValues => cells;

        public bool TryWorldToCell(Vector2 world, out int x, out int y)
        {
            x = (int)MathF.Floor((world.X - Origin.X) / CellSize);
            y = (int)MathF.Floor((world.Y - Origin.Y) / CellSize);
            return x >= 0 && y >= 0 && x < Width && y < Height;
        }

        public Vector2 CellCenter(int x, int y) =>
            Origin + new Vector2((x + 0.5f) * CellSize, (y + 0.5f) * CellSize);

        public float Sample(Vector2 world) =>
            TryWorldToCell(world, out int x, out int y) ? cells[y * Width + x] : 0f;

        public void Clear()
        {
            Array.Clear(cells, 0, cells.Length);
            Version++;
        }

        /// <summary>
        /// Setzt im Radius den Wert auf max(alt, Stärke mit linearem Abfall).
        /// Max statt Summe: dichte Abtastung entlang einer Straße verstärkt nicht künstlich.
        /// </summary>
        public void StampRadialMax(Vector2 center, float radius, float strength)
        {
            if (radius <= 0f || strength <= 0f) return;
            ForEachCellInRadius(center, radius, (index, falloff) =>
            {
                float v = strength * falloff;
                if (v > cells[index]) cells[index] = v;
            });
            Version++;
        }

        /// <summary>Addiert im Radius (mit linearem Abfall) – für Quellen, die sich aufsummieren sollen, z. B. Abgas.</summary>
        public void AddRadial(Vector2 center, float radius, float amount)
        {
            if (radius <= 0f || amount == 0f) return;
            ForEachCellInRadius(center, radius, (index, falloff) => cells[index] += amount * falloff);
            Version++;
        }

        /// <summary>Verteilt einen Anteil jeder Zelle gleichmäßig auf die 4 Nachbarn (Ausbreitung).</summary>
        public void Diffuse(float rate)
        {
            if (rate <= 0f) return;
            rate = MathF.Min(rate, 1f);
            if (scratch == null) scratch = new float[cells.Length];
            Array.Copy(cells, scratch, cells.Length);

            for (int y = 0; y < Height; y++)
            for (int x = 0; x < Width; x++)
            {
                int i = y * Width + x;
                float share = scratch[i] * rate * 0.25f;
                if (share == 0f) continue;
                // Was über den Rand fließen würde, verlässt die Welt.
                cells[i] -= share * 4f;
                if (x > 0) cells[i - 1] += share;
                if (x < Width - 1) cells[i + 1] += share;
                if (y > 0) cells[i - Width] += share;
                if (y < Height - 1) cells[i + Width] += share;
            }
            Version++;
        }

        /// <summary>Multipliziert alle Werte mit (1 - rate), z. B. Abbau von Abgas über die Zeit.</summary>
        public void Decay(float rate)
        {
            float factor = 1f - Math.Clamp(rate, 0f, 1f);
            for (int i = 0; i < cells.Length; i++) cells[i] *= factor;
            Version++;
        }

        public float MaxValue()
        {
            float max = 0f;
            for (int i = 0; i < cells.Length; i++) if (cells[i] > max) max = cells[i];
            return max;
        }

        void ForEachCellInRadius(Vector2 center, float radius, Action<int, float> action)
        {
            int minX = Math.Max(0, (int)MathF.Floor((center.X - radius - Origin.X) / CellSize));
            int maxX = Math.Min(Width - 1, (int)MathF.Floor((center.X + radius - Origin.X) / CellSize));
            int minY = Math.Max(0, (int)MathF.Floor((center.Y - radius - Origin.Y) / CellSize));
            int maxY = Math.Min(Height - 1, (int)MathF.Floor((center.Y + radius - Origin.Y) / CellSize));

            for (int y = minY; y <= maxY; y++)
            for (int x = minX; x <= maxX; x++)
            {
                float dist = Vector2.Distance(CellCenter(x, y), center);
                if (dist > radius) continue;
                action(y * Width + x, 1f - dist / radius);
            }
        }
    }
}
