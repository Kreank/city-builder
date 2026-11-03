using System;
using System.Numerics;

namespace CityBuilder.Simulation.Zoning
{
    /// <summary>Herkunft eines Gebäudes: aus einer Zone gewachsen oder vom Spieler einzeln gesetzt (Anno-Stil).</summary>
    public enum BuildingOrigin : byte
    {
        /// <summary>Private Bauherren auf gemalter Zone (G1) – Stufen nach Bodenwert (G6).</summary>
        Zone = 0,
        /// <summary>Vom Spieler gesetztes Wohnhaus – Stufen nach Bedürfnissen in Reichweite (<see cref="HouseRules"/>).</summary>
        Player,
    }

    /// <summary>Ein Gebäude: aus Zonenzellen gewachsen oder vom Spieler gesetzt (<see cref="Origin"/>).</summary>
    public sealed class Building
    {
        public int Id { get; internal set; }
        public ZoneType Zone { get; internal set; }
        /// <summary>Herkunft (Zone oder Spieler). Spieler-Häuser reißt keine Zonenlogik ab, sie sperren Straßen wie Dienste.</summary>
        public BuildingOrigin Origin { get; internal set; }
        public bool IsPlayerPlaced => Origin == BuildingOrigin.Player;
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

        // --- G6 Bodenwert & Ausbaustufen: physischer Zustand 0..1 (1 = neu). Gesetzt vom LandValue.LandValueSystem
        // (Alterung, Verfall ohne Versorgung/bei Müll, Instandhaltung); 0 → Gebäude verfällt und wird abgerissen.
        public float Condition { get; internal set; } = 1f;
        // --- Ende G6

        // --- Sicherheit (Brände): nach schwerem Brand unbewohnbar (Sanierung) bzw. Brandruine – Baufortschritt ruht,
        // niemand zieht ein. Gesetzt/gespeichert vom Safety.SafetySystem (Abschnitt „safety“).
        public bool OutOfService { get; internal set; }
        // --- Ende Sicherheit

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

        /// <summary>Grundfläche (gemeinsamer Baustein mit Straßensperre und Ausrichtung).</summary>
        public Roads.Footprint Footprint => new Roads.Footprint(Position, Front, Width, Depth);

        Roads.StructureBlock block;
        /// <summary>Eintrag für die Bauwerks-Sperre (nur Spieler-Häuser, Art „house“).</summary>
        internal Roads.StructureBlock Block => block ??= new Roads.StructureBlock("house", Id, HouseCatalog.NameOf(this), Footprint, Height);
        internal void InvalidateBlock() => block = null;

        /// <summary>Punkt mitten an der Straßenseite des Grundstücks (Zufahrt).</summary>
        public Vector2 Entrance => Position + Front * (Depth * 0.5f);

        public override string ToString() => $"{Zones.DisplayName(Zone)} #{Id} ({WidthCells}×{DepthCells})";
    }
}
