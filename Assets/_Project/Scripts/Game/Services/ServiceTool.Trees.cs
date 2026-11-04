using CityBuilder.Simulation.Environment;
using UnityEngine;
using UnityEngine.InputSystem;
using SimVector2 = System.Numerics.Vector2;

namespace CityBuilder.Game.Services
{
    /// <summary>
    /// Umwelt (Etappe 6): Bäume pflanzen als Pinsel im Dienste-Werkzeug (Kachel „Bäume pflanzen“ bzw. Umweltfenster).
    /// Ring zeigt den Pinsel, Hinweis Bäume/Felder/Kosten; LMB halten pflanzt (4× je Sekunde Echtzeit), <c>[</c>/<c>]</c> Radius.
    /// Die Simulation (<see cref="EcologySystem.PlanTrees"/>) prüft freie Fläche und Geld; Bäume wachsen über das Waldraster
    /// (sichtbar über die vorhandenen Bäume der Landschaft).
    /// </summary>
    public sealed partial class ServiceTool
    {
        float treeRadius = 24f;
        float nextPlantTime;
        TreePlan treePlan;
        SimVector2 treePlanPos = new SimVector2(float.NaN);
        int treePlanVersion = -1;

        public float TreeRadius => treeRadius;

        void UpdateTrees(SimVector2 p)
        {
            foreach (var l in reachPieces) l.Clear();
            cachedPlan = null;
            ghost.SetActive(false);
            frontMarker?.Hide();
            var world = tool.Runner.World;
            var eco = world.Ecology;
            if (eco == null) { ring.enabled = false; return; }
            var kb = GameKeyboard.Current;
            if (kb != null)
            {
                if (kb.leftBracketKey.wasPressedThisFrame) treeRadius = Mathf.Max(8f, treeRadius - 8f);
                if (kb.rightBracketKey.wasPressedThisFrame) treeRadius = Mathf.Min(EcologyRules.MaxPlantRadius, treeRadius + 8f);
            }
            if (treePlan == null || SimVector2.DistanceSquared(p, treePlanPos) > 4f || treePlanVersion != eco.Version || !Mathf.Approximately(treePlan.Radius, treeRadius))
            {
                treePlan = eco.PlanTrees(p, treeRadius);
                treePlanPos = p;
                treePlanVersion = eco.Version;
            }

            ring.enabled = true;
            ring.sharedMaterial = treePlan.IsValid ? okMaterial : badMaterial;
            const int n = 48;
            ring.positionCount = n;
            for (int i = 0; i < n; i++)
            {
                float a = i * Mathf.PI * 2f / n;
                var q = p + new SimVector2(Mathf.Cos(a), Mathf.Sin(a)) * treeRadius;
                ring.SetPosition(i, SimConvert.ToWorld(q, world.Terrain.HeightAt(q) + 1f));
            }

            string text = treePlan.IsValid
                ? $"Bäume pflanzen: {treePlan.Trees:0} Bäume auf {treePlan.Cells.Count} Feldern · {treePlan.Cost:N0} € · wachsen in ~2 Monaten · LMB halten · [ ] Radius {treeRadius:0} m"
                : TreeErrorText(treePlan.Error) + $" · [ ] Radius {treeRadius:0} m";
            SetLabel(text, !treePlan.IsValid, SimConvert.ToWorld(p, world.Terrain.HeightAt(p) + 8f));

            var mouse = Mouse.current;
            if (mouse != null && mouse.leftButton.isPressed && treePlan.IsValid && Time.unscaledTime >= nextPlantTime)
            {
                if (eco.PlantTrees(treePlan)) treePlan = null;
                nextPlantTime = Time.unscaledTime + 0.25f;
            }
        }

        public static string TreeErrorText(TreePlantError e)
        {
            switch (e)
            {
                case TreePlantError.OutOfWorld: return "Außerhalb der Karte";
                case TreePlantError.NothingFree: return "Kein freier Platz (Straßen, Gebäude, Bauwerke, Wasser oder schon Wald)";
                case TreePlantError.InsufficientFunds: return "Zu wenig Geld in der Stadtkasse (M)";
                default: return null;
            }
        }
    }
}
