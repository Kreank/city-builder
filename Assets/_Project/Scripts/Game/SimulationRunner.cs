using CityBuilder.Simulation.Core;
using UnityEngine;
using UnityEngine.InputSystem;

namespace CityBuilder.Game
{
    /// <summary>
    /// Brücke zwischen Unity und Simulation: erzeugt die Welt und führt pro Frame
    /// so viele Ticks aus, wie es die Spielgeschwindigkeit verlangt.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public sealed class SimulationRunner : MonoBehaviour
    {
        [SerializeField] float worldSize = 2048f;
        [Tooltip("Ticks pro Echtzeit-Sekunde bei Geschwindigkeit 1.")]
        [SerializeField] float ticksPerSecond = 10f;
        [SerializeField] int maxTicksPerFrame = 20;
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

        static readonly int[] SpeedMultipliers = { 0, 1, 3, 10 };

        float accumulator;

        public SimulationWorld World { get; private set; }
        public int SpeedIndex { get; private set; } = 1;
        public int SpeedMultiplier => SpeedMultipliers[SpeedIndex];
        public bool IsPaused => SpeedIndex == 0;

        /// <summary>Anteil (0..1) zwischen letztem und nächstem Tick – für flüssige Darstellung (Interpolation).</summary>
        public float TickAlpha => Mathf.Clamp01(accumulator);

        void Awake()
        {
            World = SimulationWorld.CreateDefault(CreateSettings());
            if (World.Planet != null)
                Debug.Log($"[CityBuilder] Welt {worldSeed}: {World.Planet.Regions.Count} Regionen, {World.Planet.Countries.Count} Länder, " +
                          $"{World.Planet.GenerationMilliseconds:0} ms. Start: {World.StartRegion.Name} ({World.Planet.PlayerCountry.Name})");
        }

        void Update()
        {
            HandleSpeedInput();

            if (IsPaused) return;
            accumulator += Time.deltaTime * ticksPerSecond * SpeedMultiplier;

            int ticks = 0;
            while (accumulator >= 1f && ticks < maxTicksPerFrame)
            {
                World.Step();
                accumulator -= 1f;
                ticks++;
            }
            // Wenn der Rechner nicht hinterherkommt, lieber langsamer laufen als einfrieren.
            if (ticks == maxTicksPerFrame) accumulator = 0f;
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
        };

        /// <summary>Ersetzt die laufende Welt (geladener Spielstand oder neues Spiel) und meldet <see cref="WorldReplaced"/>.</summary>
        public void ReplaceWorld(SimulationWorld world)
        {
            World = world ?? throw new System.ArgumentNullException(nameof(world));
            accumulator = 0f;
            WorldGeneration++;
            WorldReplaced?.Invoke();
        }

        /// <summary>Neues Spiel mit den Einstellungen des Inspectors (optional anderer Welt-Seed/andere Startregion).</summary>
        public SimulationWorld NewGame(int? worldSeed = null, int? startRegion = null, int? seed = null)
        {
            var world = SimulationWorld.CreateDefault(CreateSettings(worldSeed, startRegion), seed ?? 12345);
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
