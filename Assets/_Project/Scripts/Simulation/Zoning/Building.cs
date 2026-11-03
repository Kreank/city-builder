using System;
using System.Numerics;

namespace CityBuilder.Simulation.Zoning
{
    /// <summary>Ein (gewachsenes) Gebäude auf einem Grundstück aus Zonenzellen.</summary>
    public sealed class Building
    {
        public int Id { get; internal set; }
        public ZoneType Zone { get; internal set; }
        public int WidthCells { get; internal set; }
        public int DepthCells { get; internal set; }
        /// <summary>Mittelpunkt des Grundstücks.</summary>
        public Vector2 Position { get; internal set; }
        /// <summary>Richtung vom Gebäude zur Straße (Vorderseite).</summary>
        public Vector2 Front { get; internal set; }
        /// <summary>Geländehöhe (Bodenplatte).</summary>
        public float Height { get; internal set; }
        public int FrontSegmentId { get; internal set; }
        public int Level { get; internal set; } = 1;
        /// <summary>Zufallswert für die Modellauswahl.</summary>
        public int Variant { get; internal set; }

        /// <summary>Kapazität: Wohnungen bzw. Arbeitsplätze.</summary>
        public int Households { get; internal set; }
        public int Jobs { get; internal set; }

        // G2: Belegung – wird vom Bürgersystem (Citizens.CitizenSystem) aus den einzelnen Bürgern gesetzt
        public int OccupiedHouseholds { get; internal set; }
        public int Residents { get; internal set; }
        /// <summary>Besetzte Arbeitsplätze (Einheimische + Einpendler).</summary>
        public int Workers { get; internal set; }
        /// <summary>Anschlusspunkt an die Straße: Segment-Parameter t der Zufahrt auf <see cref="FrontSegmentId"/>.</summary>
        public float FrontT { get; internal set; }

        /// <summary>Baufortschritt 0..1.</summary>
        public float Construction { get; internal set; }
        public bool IsConstructed => Construction >= 1f;

        public float Width => WidthCells * ZoneGrid.CellSize;
        public float Depth => DepthCells * ZoneGrid.CellSize;
        /// <summary>Richtung entlang der Straße.</summary>
        public Vector2 Side => new Vector2(-Front.Y, Front.X);

        /// <summary>Liegt der Punkt auf dem Grundstück?</summary>
        public bool Contains(Vector2 p, float margin = 0f)
        {
            var d = p - Position;
            return MathF.Abs(Vector2.Dot(d, Side)) <= Width * 0.5f + margin && MathF.Abs(Vector2.Dot(d, Front)) <= Depth * 0.5f + margin;
        }

        /// <summary>Punkt mitten an der Straßenseite des Grundstücks (Zufahrt).</summary>
        public Vector2 Entrance => Position + Front * (Depth * 0.5f);

        public override string ToString() => $"{Zones.DisplayName(Zone)} #{Id} ({WidthCells}×{DepthCells})";
    }
}
