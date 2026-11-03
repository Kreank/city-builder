using CityBuilder.Simulation.Core;
using UnityEngine;
using UnityEngine.InputSystem;

namespace CityBuilder.Game
{
    /// <summary>
    /// Brücke zwischen Unity und Simulation: erzeugt die Welt und führt pro Frame so viele <b>Verkehrsschritte</b>
    /// (<see cref="SimulationWorld.Substep"/>) aus, wie es die Spielgeschwindigkeit verlangt – je 120 Schritte folgt ein
    /// Kalender-Tick (Zeitmodell, <see cref="TimeModel"/>). Tempo 1/2/3 = 1 Spieltag in 5 / 2,5 / 1,25 Minuten.
    /// Überlast: höchstens <see cref="maxSimMillisecondsPerFrame"/> Rechenzeit je Frame; was nicht passt, entfällt
    /// (die Simulation läuft dann langsamer, der Frame friert nicht ein).
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public sealed class SimulationRunner : MonoBehaviour
    {
        [SerializeField] float worldSize = 2048f;
        [Tooltip("Kalender-Ticks (6 Spielminuten) pro Echtzeit-Sekunde bei Geschwindigkeit 1 (Standard aus TimeModel: 0,8 → 5 min je Spieltag).")]
        [SerializeField] float ticksPerSecond = TimeModel.TicksPerRealSecond;
        [Tooltip("Rechenzeit-Budget der Simulation je Frame (ms). Darüber wird die Simulation langsamer statt den Frame einzufrieren.")]
        [SerializeField] float maxSimMillisecondsPerFrame = 8f;
        [Header("Gelände")]
        [SerializeField] bool generateTerrain = true;
        [SerializeField] int terrainSeed = 4711;
        [Header("Stadtkasse")]
        [Tooltip("Sandbox: Kosten werden gebucht, aber Bauen wird nie wegen Geld blockiert.")]
        [SerializeField] bool unlimitedFunds = false;
        [SerializeField] double startingFunds = 100000.0;
        // --- Welt (Etappe 5)
        [Header("Welt")]
        [Tooltip("Fiktive Erde erzeugen; die Stadtkarte wird ein Ausschnitt der Startregion (Gelände, Klima, Ressourcen).")]
        [SerializeField] bool generateWorld = true;
        [SerializeField] int worldSeed = 2024;
        [Tooltip("Region der Stadt (−1 = automatisch: gemäßigt, fruchtbar, Fluss/Küste).")]
        [SerializeField] int startRegion = -1;
        // --- Ende Welt
        // --- Spielstart & Startregion
        [Tooltip("Erster Start ohne Spielstand: Neues-Spiel-Fenster (Welt, Vorlieben, Startregion, Stadtname) statt Standardwelt.")]
        [SerializeField] bool newGameDialogOnFirstStart = true;
        /// <summary>Erster Start ohne Spielstand: läuft eine Platzhalterwelt, bis der Spieler im Neues-Spiel-Fenster startet.</summary>
        public bool AwaitingNewGame { get; private set; }
        // --- Ende Spielstart
        // --- Außenanbindungen (Gateways)
        [Header("Außenanbindung")]
        [Tooltip("Beim Kartenstart eine Autobahn mit Anschlussstelle + Zubringer bauen (wie Cities: Skylines); dazu Bahn-/Wasserwege vorbereitet.")]
        [SerializeField] bool prebuiltConnections = true;
        [Tooltip("Zuzug und Handel nur über Anschlüsse (die Stadt muss an die Autobahn angeschlossen werden).")]
        [SerializeField] bool requireGateways = true;
        // --- Ende Gateways

        static int[] SpeedMultipliers => TimeModel.SpeedMultipliers;

        /// <summary>Offene Verkehrsschritte (Bruchteil = Interpolation zwischen zwei Schritten).</summary>
        double accumulator;
        readonly System.Diagnostics.Stopwatch frameWatch = new System.Diagnostics.Stopwatch();

        public SimulationWorld World { get; private set; }
        public int SpeedIndex { get; private set; } = 1;
        public int SpeedMultiplier => SpeedMultipliers[SpeedIndex];
        public bool IsPaused => SpeedIndex == 0;

        /// <summary>
        /// Anteil (0..1) zwischen letztem und nächstem <b>Verkehrsschritt</b> – Interpolation der Fahrzeuge
        /// (<c>Vehicle.PrevPosition</c> → <c>Position</c>).
        /// </summary>
        public float TickAlpha => Mathf.Clamp01((float)accumulator);

        /// <summary>Kalenderzeit in Ticks inkl. Bruchteil (Verkehrsschritte + Interpolation) – für Fußgänger und Uhr-Anzeigen.</summary>
        public double GameTickTime => World.TickTime + TickAlpha / (double)TimeModel.TrafficStepsPerTick;

        /// <summary>Verkehrssekunden je Echtzeitsekunde beim aktuellen Tempo (0 bei Pause) – für Animationsraten der Views.</summary>
        public float TrafficSecondsPerRealSecond => ticksPerSecond * SpeedMultiplier * TimeModel.TrafficSecondsPerTick;

        /// <summary>Höchste Beschleunigung von Darstellungs-Animationen (Schritte, Rotoren) gegenüber Verkehrszeit = Echtzeit.</summary>
        public const float MaxAnimationSpeedup = 3f;
        /// <summary>
        /// Zeitfaktor für Animationen der Views (Schrittfrequenz, Rotoren): folgt der Verkehrszeit, ist aber auf
        /// <see cref="MaxAnimationSpeedup"/> begrenzt (sonst flimmern Beine/Rotorblätter bei 24-facher Verkehrszeit). 0 bei Pause.
        /// </summary>
        public float AnimationTimeScale => IsPaused ? 0f : Mathf.Min(TrafficSecondsPerRealSecond, MaxAnimationSpeedup);

        /// <summary>Verkehrsschritte, die im letzten Frame wegen des Rechenzeit-Budgets entfallen sind (Überlast-Anzeige).</summary>
        public int DroppedStepsLastFrame { get; private set; }
        /// <summary>Rechenzeit der Simulation im letzten Frame (ms).</summary>
        public float LastFrameSimMilliseconds { get; private set; }

        void Awake()
        {
            // --- Spielstart: erster Programmstart ohne Spielstand → Neues-Spiel-Fenster statt Standardwelt
            // (bis dahin eine schnelle Platzhalterwelt ohne Weltkarte, angehalten; SaveManager öffnet das Fenster)
            AwaitingNewGame = newGameDialogOnFirstStart && generateWorld && !Persistence.SaveManager.HasAnySave();
            if (AwaitingNewGame)
            {
                var placeholder = CreateSettings();
                placeholder.GenerateWorld = false;
                placeholder.PrebuiltConnections = placeholder.RequireGatewayForMoveIn = placeholder.RequireGatewayForTrade = false; // Gateways: Platzhalter braucht keine Autobahn
                World = SimulationWorld.CreateDefault(placeholder);
                SetSpeed(0);
                return;
            }
            // --- Ende Spielstart
            World = SimulationWorld.CreateDefault(CreateSettings());
            if (World.Planet != null)
                Debug.Log($"[CityBuilder] Welt {worldSeed}: {World.Planet.Regions.Count} Regionen, {World.Planet.Countries.Count} Länder, " +
                          $"{World.Planet.GenerationMilliseconds:0} ms. Start: {World.StartRegion.Name} ({World.Planet.PlayerCountry.Name})");
        }

        void Update()
        {
            HandleSpeedInput();

            DroppedStepsLastFrame = 0;
            if (IsPaused) { LastFrameSimMilliseconds = 0f; return; }
            // Höchstens 0,25 s Echtzeit nachholen (z. B. nach einem Ruckler/Laden)
            float dt = Mathf.Min(Time.deltaTime, 0.25f);
            accumulator += dt * ticksPerSecond * SpeedMultiplier * TimeModel.TrafficStepsPerTick;

            frameWatch.Restart();
            while (accumulator >= 1.0)
            {
                World.Substep();
                accumulator -= 1.0;
                if (frameWatch.Elapsed.TotalMilliseconds >= maxSimMillisecondsPerFrame) break;
            }
            LastFrameSimMilliseconds = (float)frameWatch.Elapsed.TotalMilliseconds;
            // Überlast: Rückstand verwerfen – lieber langsamer laufen als einfrieren (Ergebnis bleibt deterministisch,
            // nur die Geschwindigkeit sinkt)
            if (accumulator >= 1.0)
            {
                DroppedStepsLastFrame = (int)accumulator;
                accumulator -= System.Math.Floor(accumulator);
            }
        }

        // --- Speichern & Laden: Welt ersetzen (Laden, Neues Spiel)
        /// <summary>
        /// Die Welt wurde ersetzt (<see cref="ReplaceWorld"/>). Views, die Objekte der alten Welt zwischenspeichern oder
        /// deren Ereignisse abonniert haben, bauen sich hier neu auf (alte Abos lösen, neue setzen, Caches verwerfen).
        /// </summary>
        public event System.Action WorldReplaced;
        /// <summary>Zählt Weltwechsel (0 = Startwelt) – für Views, die lieber vergleichen als abonnieren.</summary>
        public int WorldGeneration { get; private set; }

        /// <summary>Einstellungen aus dem Inspector; <paramref name="worldSeed"/>/<paramref name="startRegion"/> überschreiben (Neues Spiel).</summary>
        public WorldSettings CreateSettings(int? worldSeed = null, int? startRegion = null) => new WorldSettings
        {
            SizeMeters = worldSize,
            GenerateTerrain = generateTerrain,
            TerrainSeed = terrainSeed,
            UnlimitedFunds = unlimitedFunds,
            StartingFunds = startingFunds,
            GenerateWorld = generateWorld,   // Welt (Etappe 5)
            WorldSeed = worldSeed ?? this.worldSeed,
            StartRegion = startRegion ?? this.startRegion,
            // --- Außenanbindungen (Gateways)
            PrebuiltConnections = prebuiltConnections,
            RequireGatewayForMoveIn = requireGateways,
            RequireGatewayForTrade = requireGateways,
        };

        /// <summary>Ersetzt die laufende Welt (geladener Spielstand oder neues Spiel) und meldet <see cref="WorldReplaced"/>.</summary>
        public void ReplaceWorld(SimulationWorld world)
        {
            World = world ?? throw new System.ArgumentNullException(nameof(world));
            accumulator = 0.0;
            WorldGeneration++;
            WorldReplaced?.Invoke();
        }

        /// <summary>Neues Spiel mit den Einstellungen des Inspectors (optional anderer Welt-Seed/andere Startregion).</summary>
        public SimulationWorld NewGame(int? worldSeed = null, int? startRegion = null, int? seed = null) =>
            NewGame(CreateSettings(worldSeed, startRegion), seed ?? 12345);

        /// <summary>Spielstart: neues Spiel mit fertigen Einstellungen (Neues-Spiel-Fenster: Seed, Startregion, Stadtname).</summary>
        public SimulationWorld NewGame(WorldSettings settings, int seed = 12345)
        {
            var world = SimulationWorld.CreateDefault(settings ?? CreateSettings(), seed);
            AwaitingNewGame = false;
            ReplaceWorld(world);
            return world;
        }
        // --- Ende Speichern & Laden

        public void SetSpeed(int index) => SpeedIndex = Mathf.Clamp(index, 0, SpeedMultipliers.Length - 1);

        /// <summary>Anzahl der Tempostufen inkl. Pause (Index 0).</summary>
        public static int SpeedCount => SpeedMultipliers.Length;
        public static int MultiplierOf(int index) => SpeedMultipliers[Mathf.Clamp(index, 0, SpeedMultipliers.Length - 1)];

        int lastSpeedBeforePause = 1;

        /// <summary>Pause an/aus (Leertaste oder Pause-Knopf der UI); kehrt zum Tempo vor der Pause zurück.</summary>
        public void TogglePause()
        {
            if (IsPaused) SetSpeed(lastSpeedBeforePause);
            else { lastSpeedBeforePause = SpeedIndex; SetSpeed(0); }
        }

        void HandleSpeedInput()
        {
            var kb = GameKeyboard.Current; // null während einer Texteingabe
            if (kb == null) return;

            if (kb.spaceKey.wasPressedThisFrame) TogglePause();
            if (kb.digit1Key.wasPressedThisFrame) SetSpeed(1);
            if (kb.digit2Key.wasPressedThisFrame) SetSpeed(2);
            if (kb.digit3Key.wasPressedThisFrame) SetSpeed(3);
        }
    }
}
