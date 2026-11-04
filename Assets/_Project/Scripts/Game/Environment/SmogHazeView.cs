using UnityEngine;
using SimVector2 = System.Numerics.Vector2;

namespace CityBuilder.Game.Environment
{
    /// <summary>
    /// Umwelt (Etappe 6, nur Darstellung, dezent): bei hoher Luftbelastung um den Blickpunkt der Kamera ein bräunlich-grauer
    /// Dunst über die Nebel-Einstellungen der Szene (exponentiell², Dichte höchstens <see cref="MaxDensity"/>). Unter der Schwelle
    /// werden die ursprünglichen Nebel-Einstellungen wiederhergestellt. Blendet weich ein/aus (Echtzeit).
    /// </summary>
    public sealed class SmogHazeView : MonoBehaviour
    {
        const float Threshold = 0.15f, Full = 0.5f, MaxDensity = 0.0022f, SampleRadius = 240f;
        static readonly Color HazeColor = new Color(0.62f, 0.58f, 0.5f);

        SimulationRunner runner;
        Camera cam;
        bool savedFog, active;
        FogMode savedMode;
        Color savedColor;
        float savedDensity, current, nextSample, target;

        public void Init(SimulationRunner r, Camera camera)
        {
            runner = r;
            cam = camera;
        }

        void LateUpdate()
        {
            var e = runner != null ? runner.World?.Ecology : null;
            if (cam == null || e == null || !e.Enabled) { target = 0f; Blend(); return; }
            if (Time.unscaledTime >= nextSample)
            {
                nextSample = Time.unscaledTime + 0.5f;
                target = Mathf.InverseLerp(Threshold, Full, SampleAround(runner.World.Environment.AirPollution));
            }
            Blend();
        }

        float SampleAround(CityBuilder.Simulation.Environment.GridLayer air)
        {
            // Blickpunkt: Schnitt des Kamerastrahls mit der Ebene y = 0
            var ray = new Ray(cam.transform.position, cam.transform.forward);
            float t = Mathf.Abs(ray.direction.y) > 1e-3f ? -ray.origin.y / ray.direction.y : 200f;
            var p = ray.GetPoint(Mathf.Clamp(t, 0f, 3000f));
            var c = new SimVector2(p.x, p.z);
            float sum = 0f;
            int n = 0;
            for (int y = -2; y <= 2; y++)
            for (int x = -2; x <= 2; x++)
            {
                sum += air.Sample(c + new SimVector2(x, y) * (SampleRadius * 0.5f));
                n++;
            }
            return sum / n;
        }

        void Blend()
        {
            current = Mathf.MoveTowards(current, target, Time.unscaledDeltaTime * 0.3f);
            if (current <= 0.001f)
            {
                if (active) Restore();
                return;
            }
            if (!active)
            {
                savedFog = RenderSettings.fog; savedMode = RenderSettings.fogMode;
                savedColor = RenderSettings.fogColor; savedDensity = RenderSettings.fogDensity;
                active = true;
            }
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogColor = savedFog ? Color.Lerp(savedColor, HazeColor, current) : HazeColor;
            RenderSettings.fogDensity = Mathf.Lerp(savedFog ? savedDensity : 0f, MaxDensity, current);
        }

        void Restore()
        {
            RenderSettings.fog = savedFog;
            RenderSettings.fogMode = savedMode;
            RenderSettings.fogColor = savedColor;
            RenderSettings.fogDensity = savedDensity;
            active = false;
        }

        void OnDisable()
        {
            if (active) Restore();
        }
    }
}
