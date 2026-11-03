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
            World = SimulationWorld.CreateDefault(new WorldSettings
            {
                SizeMeters = worldSize,
                GenerateTerrain = generateTerrain,
                TerrainSeed = terrainSeed,
            });
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
            var kb = Keyboard.current;
            if (kb == null) return;

            if (kb.spaceKey.wasPressedThisFrame) TogglePause();
            if (kb.digit1Key.wasPressedThisFrame) SetSpeed(1);
            if (kb.digit2Key.wasPressedThisFrame) SetSpeed(2);
            if (kb.digit3Key.wasPressedThisFrame) SetSpeed(3);
        }
    }
}
