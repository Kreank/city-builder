using System.Numerics;

namespace CityBuilder.Simulation.Gateways
{
    /// <summary>Verkehrsträger eines Anschlusses an die Außenwelt.</summary>
    public enum GatewayKind
    {
        /// <summary>Fernstraße/Autobahn (befahrbar ab Kartenstart).</summary>
        Highway,
        /// <summary>Landstraße (vom Spieler an den Kartenrand gebaut – noch nicht als eigenes Gateway geführt).</summary>
        Road,
        /// <summary>Bahntrasse (Korridor freigehalten, Züge folgen – Stufe 2).</summary>
        Rail,
        /// <summary>Wasserstraße: Fluss oder Küste (Schiffe/Hafen folgen – Stufe 3).</summary>
        Water,
        /// <summary>Luftweg ohne Lage auf der Karte (Flughafen folgt – Stufe 4).</summary>
        Air,
    }

    /// <summary>Betriebszustand eines Anschlusses.</summary>
    public enum GatewayStatus
    {
        /// <summary>Befahrbar: Zuzug, Handel und Durchgangsverkehr laufen darüber.</summary>
        Operational,
        /// <summary>Lage steht fest, Verkehrsträger kommt später („noch nicht befahrbar (folgt)“).</summary>
        Planned,
    }

    /// <summary>Wohin ein Anschluss führt.</summary>
    public enum GatewayTargetKind
    {
        /// <summary>Ohne Weltkarte: generisches Umland in einer Himmelsrichtung.</summary>
        Hinterland,
        /// <summary>Nachbarregion im eigenen Land.</summary>
        Region,
        /// <summary>Anderes Land (Nachbar oder Hauptstadt in dieser Richtung).</summary>
        Country,
        /// <summary>Meer/Seeweg (Küste).</summary>
        Sea,
    }

    /// <summary>Verkehr und Handel eines Anschlusses an einem Spieltag.</summary>
    public sealed class GatewayDayStats
    {
        /// <summary>Fahrzeuge, die über den Anschluss in die Karte gefahren sind bzw. sie verlassen haben.</summary>
        public int VehiclesIn, VehiclesOut;
        /// <summary>Zugezogene Haushalte, die über diesen Anschluss gekommen sind.</summary>
        public int MoveIns;
        /// <summary>Import-/Export-Lieferungen (Lkw und Platzhalter) über diesen Anschluss.</summary>
        public int Imports, Exports;
        public double ImportValue, ExportValue;

        public void Clear()
        {
            VehiclesIn = VehiclesOut = MoveIns = Imports = Exports = 0;
            ImportValue = ExportValue = 0;
        }

        public void CopyFrom(GatewayDayStats o)
        {
            VehiclesIn = o.VehiclesIn; VehiclesOut = o.VehiclesOut; MoveIns = o.MoveIns;
            Imports = o.Imports; Exports = o.Exports; ImportValue = o.ImportValue; ExportValue = o.ExportValue;
        }
    }

    /// <summary>
    /// Ein Anschluss der Stadtkarte an die Außenwelt (wie in Cities: Skylines): Autobahn-Ende am Kartenrand, Bahntrasse,
    /// Wasserstraße oder Luftweg. Führt in Richtung eines <b>Ziels</b> (Nachbarregion/Land aus der Weltkarte, ohne Welt
    /// das „Umland“). Zuzug und Handel laufen über befahrbare Anschlüsse; Bahn/Wasser/Luft sind vorbereitet (Lage +
    /// Status „noch nicht befahrbar“) und werden in späteren Stufen befahrbar.
    /// </summary>
    public sealed class Gateway
    {
        public int Id { get; internal set; }
        public GatewayKind Kind { get; internal set; }
        public GatewayStatus Status { get; internal set; }
        /// <summary>Beschriftung, z. B. „A 7 Richtung Valmora“.</summary>
        public string Name { get; internal set; } = "";
        /// <summary>Kurzname der Strecke („A 7“, „Bahn“, „Fluss“ …).</summary>
        public string Route { get; internal set; } = "";

        /// <summary>Hat eine Lage auf der Karte (Luftweg: nein).</summary>
        public bool HasLocation { get; internal set; } = true;
        /// <summary>Punkt am Kartenrand (Mitte des Anschlusses).</summary>
        public Vector2 Position { get; internal set; }
        /// <summary>Richtung nach außen (Einheitsvektor, x = Ost, y = Nord).</summary>
        public Vector2 Direction { get; internal set; }

        // --- Ziel
        public GatewayTargetKind TargetKind { get; internal set; }
        /// <summary>Name des Ziels (Land, Region, „Umland (Ost)“, „Meer“).</summary>
        public string TargetName { get; internal set; } = "";
        /// <summary>Land des Ziels (−1 = keins/ohne Welt).</summary>
        public int TargetCountryId { get; internal set; } = -1;
        /// <summary>Region des Ziels (−1 = keine/ohne Welt).</summary>
        public int TargetRegionId { get; internal set; } = -1;
        /// <summary>Wichtigkeit des Ziels (≥ 0; steuert Durchgangsverkehr und Zuzugs-Anteil).</summary>
        public float Importance { get; internal set; } = 1f;

        /// <summary>Kapazität: Autobahn Fahrzeuge/h je Richtung, Wasser Schiffe/Tag, Bahn Züge/Tag (geplant).</summary>
        public float Capacity { get; internal set; }

        // --- Straße
        /// <summary>Einfahrt in die Karte (Straßenknoten mit abgehenden Spuren), 0 = keine.</summary>
        public int EntryNodeId { get; internal set; }
        /// <summary>Ausfahrt aus der Karte (Straßenknoten mit ankommenden Spuren), 0 = keine.</summary>
        public int ExitNodeId { get; internal set; }
        /// <summary>Zweites Ende derselben Strecke (Autobahn: Gateway am anderen Kartenrand), 0 = keins.</summary>
        public int PartnerId { get; internal set; }

        // --- Wasser
        /// <summary>Breite der Wasserstraße am Kartenrand (m).</summary>
        public float WaterWidth { get; internal set; }
        /// <summary>Wassertiefe (m).</summary>
        public float WaterDepth { get; internal set; }
        /// <summary>Schiffbar nach Breite/Tiefe (<see cref="GatewayRules.NavigableWidth"/>, <see cref="GatewayRules.NavigableDepth"/>).</summary>
        public bool Navigable { get; internal set; }

        // --- Bahn (Stufe 2): Hauptstrecke liegt (vom Bahnsystem gesetzt, abgeleitet, nicht gespeichert)
        public bool RailBuilt { get; internal set; }
        // --- Ende Bahn
        // --- Wasserstraße (Stufe 3): Grund, warum der Wasserweg nicht befahrbar ist (vom ShippingSystem gesetzt, abgeleitet)
        public string ShippingNote { get; internal set; }
        // --- Ende Wasserstraße

        /// <summary>Alle Zuzügler-Haushalte über diesen Anschluss seit Spielbeginn (verteilt den Zuzug nach Wichtigkeit).</summary>
        public int TotalMoveIns { get; internal set; }

        public GatewayDayStats Today { get; } = new GatewayDayStats();
        public GatewayDayStats Yesterday { get; } = new GatewayDayStats();

        /// <summary>Befahrbar (Zuzug/Handel/Durchgangsverkehr laufen darüber).</summary>
        public bool IsOperational => Status == GatewayStatus.Operational;
        public bool IsRoad => Kind == GatewayKind.Highway || Kind == GatewayKind.Road;

        public static string KindText(GatewayKind k)
        {
            switch (k)
            {
                case GatewayKind.Highway: return "Autobahn";
                case GatewayKind.Road: return "Landstraße";
                case GatewayKind.Rail: return "Bahn";
                case GatewayKind.Water: return "Wasserstraße";
                default: return "Luftweg";
            }
        }

        public string KindName => KindText(Kind);

        /// <summary>Zustand als Text („befahrbar“, „noch nicht befahrbar (folgt)“ …).</summary>
        public string StatusText
        {
            get
            {
                if (IsOperational) return "befahrbar";
                switch (Kind)
                {
                    case GatewayKind.Rail: return RailBuilt ? "noch nicht befahrbar (Bahnhof an der Hauptstrecke fehlt)" : "noch nicht befahrbar (Züge folgen)"; // Bahn
                    case GatewayKind.Water: return !Navigable ? "nicht schiffbar (zu schmal/flach)" : ShippingNote ?? "noch nicht befahrbar (Schiffe/Hafen folgen)"; // Wasserstraße
                    case GatewayKind.Air: return "noch nicht befahrbar (Flughafen folgt)";
                    default: return "noch nicht befahrbar (folgt)";
                }
            }
        }

        /// <summary>Himmelsrichtung nach außen („Ost“, „Nordwest“ …).</summary>
        public string CompassText => GatewayRules.Compass(Direction);

        public override string ToString() => $"{Name} [{KindName}, {StatusText}]";
    }
}
