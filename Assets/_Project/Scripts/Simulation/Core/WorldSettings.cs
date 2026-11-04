namespace CityBuilder.Simulation.Core
{
    public sealed class WorldSettings
    {
        /// <summary>Kantenlänge der (quadratischen) Spielwelt in Metern. Die Welt ist um 0/0 zentriert.</summary>
        public float SizeMeters = 2048f;

        /// <summary>
        /// Kacheln: Kantenlänge einer kaufbaren Kachel (Standard 2 048 m). Die Karte wird in round(<see cref="SizeMeters"/> /
        /// TileSize) Kacheln je Seite geteilt (mindestens 1); bei einer Kachel ist alles gekauft (Tests, alte Stände).
        /// Größere Karten (z. B. 6 144 m = 3 × 3) starten mit der mittleren Kachel, die übrigen kauft der Spieler
        /// (<see cref="SimulationWorld.Tiles"/>).
        /// </summary>
        public float TileSize = 2048f;

        /// <summary>Kacheln: Kartengröße neuer Spiele im Spiel (3 × 3 Kacheln). Tests/Standard bleiben bei <see cref="SizeMeters"/> 2 048 m.</summary>
        public const float GameSizeMeters = 6144f;

        /// <summary>Zellgröße der Umwelt-Raster (Lärm, Abgas ...) in Metern.</summary>
        public float EnvironmentCellSize = 16f;

        /// <summary>Hügel/Fluss erzeugen (sonst flacher Boden auf 0 – z. B. für Tests).</summary>
        public bool GenerateTerrain;
        public int TerrainSeed = 4711;
        /// <summary>Abstand der Gitterpunkte der Höhenkarte in Metern.</summary>
        public float TerrainCellSize = 4f;

        // --- Stadtkasse
        /// <summary>
        /// Sandbox-Kasse: Kosten werden gebucht, aber nie blockiert (keine Zinsen, kein Baustopp).
        /// Standard an, damit Tests und Werkzeuge ohne Geld-Denken bauen können; das Spiel
        /// (<c>SimulationRunner</c>) schaltet es aus.
        /// </summary>
        public bool UnlimitedFunds = true;
        /// <summary>Startkapital der Stadtkasse in €.</summary>
        public double StartingFunds = 100000.0;
        // --- Ende Stadtkasse

        // --- Welt (Etappe 5)
        /// <summary>
        /// Fiktive Erde erzeugen (<see cref="SimulationWorld.Planet"/>); die Stadtkarte wird dann ein Ausschnitt der
        /// Startregion (Gelände, Klima, Ressourcen). Standard aus → alles wie bisher (Tests).
        /// </summary>
        public bool GenerateWorld;
        public int WorldSeed = 2024;
        /// <summary>Region der Spielerstadt; −1 = automatisch (gemäßigt, fruchtbar, Fluss/Küste).</summary>
        public int StartRegion = -1;
        /// <summary>Auflösung/Anzahl Regionen und Länder; null = Standard.</summary>
        public World.PlanetSettings Planet;
        // --- Ende Welt

        // --- Spielstart & Startregion
        /// <summary>Name der Spielerstadt (null = „Neustadt“); der Stadtstaat heißt „Freie Stadt {Name}“. Laufend: <c>SimulationWorld.Realm</c>.</summary>
        public string CityName;
        // --- Ende Spielstart
        // --- Außenanbindungen (Gateways)
        /// <summary>
        /// Beim Kartenstart (neue Welt) eine Autobahn quer durch die Karte mit Anschlussstelle + Zubringer bauen (gehört der
        /// Welt, nicht abreißbar). Standard aus → Tests/Standardwelt wie bisher; das Spiel (<c>SimulationRunner</c>) schaltet ein.
        /// </summary>
        public bool PrebuiltConnections;
        /// <summary>
        /// Neue Haushalte ziehen nur zu, wenn ihre Wohnung übers Straßennetz mit einem befahrbaren Gateway (bzw. ohne
        /// Gateways: einem Außenanschluss) verbunden ist; ersetzt die alte Regel „kein Außenanschluss nötig“. Standard aus (Tests).
        /// </summary>
        public bool RequireGatewayForMoveIn;
        /// <summary>
        /// Import/Export ohne Außenanschluss („Umland“) entfällt – Handel läuft nur über einen Anschluss. Standard aus (Tests).
        /// </summary>
        public bool RequireGatewayForTrade;
        // --- Ende Gateways
    }
}
