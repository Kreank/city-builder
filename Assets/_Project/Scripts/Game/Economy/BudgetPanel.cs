using CityBuilder.Game.Roads;
using CityBuilder.Game.UI;
using CityBuilder.Simulation.Economy;
using UnityEngine;
using UnityEngine.InputSystem;

namespace CityBuilder.Game.Economy
{
    /// <summary>
    /// Fenster „Stadtkasse“ (IMGUI, Taste M, verschiebbar): Kontostand, Kreditrahmen/Baustopp, Bilanz der
    /// letzten und der laufenden Woche je Kategorie, Tagesprognose, Steuersätze mit +/- und Saldo-Verlauf.
    /// Liest nur; Steuersätze ändert es über <see cref="EconomySystem.SetTaxRate"/> (Spieleraktion der Simulation).
    /// Begleiter des <see cref="RoadTool"/>.
    /// </summary>
    public sealed class BudgetPanel : MonoBehaviour
    {
        const int WindowId = 0x5ADE;
        const int ChartWeeks = 12;

        RoadTool tool;
        Rect window = new Rect(0f, 0f, 430f, 600f);
        bool visible;
        bool placed;
        Vector2 scroll;
        GUIStyle header, small, right;
        BudgetPeriod estimate;
        float estimateAge = 999f;

        public bool Visible { get => visible; set => visible = value; }

        public void Init(RoadTool roadTool) => tool = roadTool;

        void Update()
        {
            var kb = Keyboard.current;
            if (kb != null && kb.mKey.wasPressedThisFrame) visible = !visible;
            estimateAge += Time.unscaledDeltaTime;
        }

        EconomySystem Economy => tool != null && tool.Runner != null && tool.Runner.World != null ? tool.Runner.World.Economy : null;

        void OnGUI()
        {
            var e = Economy;
            if (e == null) return;
            if (header == null)
            {
                header = new GUIStyle(GUI.skin.label) { fontSize = 15, fontStyle = FontStyle.Bold, richText = true };
                small = new GUIStyle(GUI.skin.label) { fontSize = 12, richText = true, wordWrap = true };
                right = new GUIStyle(small) { alignment = TextAnchor.UpperRight, wordWrap = false };
            }

            // Kontostand dauerhaft im HUD zeigt das neue UI-Toolkit-HUD (Agent „ui“) – hier nur das Fenster.
            if (!visible) return;
            if (CityBuilder.Game.UI.Panels.UiPanels.ToolkitActive) return; // neue UI: s. UI/Windows (IMGUI nur Rückfall)

            if (!placed)
            {
                window.x = Screen.width * 0.5f - window.width * 0.5f;
                window.y = 40f;
                window.height = Mathf.Min(window.height, Screen.height - 80f);
                placed = true;
            }
            UiBlocker.Register(window);
            window = GUI.Window(WindowId, window, Draw, "Stadtkasse (M)");
        }

        void Draw(int id)
        {
            var e = Economy;
            if (e == null) return;
            if (estimate == null || estimateAge > 1f) { estimate = e.EstimateDay(); estimateAge = 0f; }

            GUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("×", GUILayout.Width(24f))) visible = false;
            GUILayout.EndHorizontal();

            scroll = GUILayout.BeginScrollView(scroll);

            // --- Kontostand
            GUILayout.Label($"<color=#{ColorUtility.ToHtmlStringRGB(BalanceColor(e))}>Kontostand {Money(e.Balance)}</color>", header);
            if (e.UnlimitedFunds)
                GUILayout.Label("<color=#9fd0ff>Sandbox: unbegrenztes Geld – Kosten werden nur gezählt.</color>", small);
            else
            {
                GUILayout.Label($"Verfügbar (inkl. Kreditrahmen {Money(e.CreditLimit)}): {Money(e.AvailableFunds)}", small);
                if (e.ConstructionFrozen)
                    GUILayout.Label("<color=#ff7060><b>Baustopp:</b> zu lange im Minus. Keine Neubauten, bis der Kontostand wieder ≥ 0 ist.</color>", small);
                else if (e.IsOverdrawn)
                    GUILayout.Label($"<color=#ffb040>Überzogen seit {e.DaysInDebt} Tagen · {EconomyRules.DailyOverdraftInterest * 100:0.0#} % Zinsen je Tag · " +
                                    $"Baustopp in {e.DaysUntilFreeze} Tagen</color>", small);
            }

            // --- Prognose und Wochen
            GUILayout.Space(4f);
            GUILayout.Label($"Prognose je Tag: Einnahmen {Money(estimate.Income)} · Ausgaben {Money(estimate.Expense)} · " +
                            $"Saldo <b>{Signed(estimate.Net)}</b>", small);
            var last = e.LastWeek;
            GUILayout.Label(last != null
                ? $"Letzte Woche (Nr. {last.Index + 1}): Saldo <b>{Signed(last.Net)}</b>"
                : "Letzte Woche: noch keine abgeschlossen", small);

            GUILayout.Space(4f);
            GUILayout.BeginHorizontal();
            GUILayout.Label("<b>Kategorie</b>", small, GUILayout.Width(180f));
            GUILayout.Label("<b>letzte Woche</b>", right, GUILayout.Width(100f));
            GUILayout.Label($"<b>diese ({e.CurrentWeek.Days}/7 T.)</b>", right, GUILayout.Width(110f));
            GUILayout.EndHorizontal();
            for (int i = 0; i < Budget.Count; i++)
            {
                var c = (BudgetCategory)i;
                double a = last?.Amount(c) ?? 0, b = e.CurrentWeek.Amount(c);
                if (a == 0 && b == 0 && estimate.Amount(c) == 0) continue;
                bool income = Budget.IsIncome(c);
                GUILayout.BeginHorizontal();
                GUILayout.Label(Budget.DisplayName(c), small, GUILayout.Width(180f));
                GUILayout.Label(Colored(a, income), right, GUILayout.Width(100f));
                GUILayout.Label(Colored(b, income), right, GUILayout.Width(110f));
                GUILayout.EndHorizontal();
            }
            if (last != null)
                foreach (var kv in last.ServiceUpkeep)
                    GUILayout.Label($"    {kv.Key}: {Money(kv.Value)}", small);

            // --- Steuern
            GUILayout.Space(6f);
            GUILayout.Label("Steuersätze", header);
            for (int i = 0; i < Budget.TaxKinds; i++)
            {
                var k = (TaxKind)i;
                float rate = e.TaxRate(k);
                float f = EconomySystem.DemandFactorForRate(rate);
                GUILayout.BeginHorizontal();
                GUILayout.Label(Budget.DisplayName(k), small, GUILayout.Width(80f));
                if (GUILayout.Button("−", GUILayout.Width(26f))) { e.ChangeTaxRate(k, -EconomyRules.TaxStep); estimateAge = 999f; }
                GUILayout.Label($"<b>{rate * 100f:0} %</b>", right, GUILayout.Width(44f));
                if (GUILayout.Button("+", GUILayout.Width(26f))) { e.ChangeTaxRate(k, EconomyRules.TaxStep); estimateAge = 999f; }
                string color = f < 0.999f ? "#ffb040" : f > 1.001f ? "#7dff8a" : "#cccccc";
                GUILayout.Label($"  Nachfrage <color={color}>×{f:0.00}</color>", small);
                GUILayout.EndHorizontal();
            }
            GUILayout.Label($"Standard {EconomyRules.DefaultTaxRate * 100f:0} % (Nachfrage ×1). Darüber −{EconomyRules.DemandPenaltyPerPoint * 100f:0} % " +
                            $"Nachfrage je Prozentpunkt, darunter +{EconomyRules.DemandBonusPerPoint * 100f:0} %.", small);

            // --- Verlauf
            GUILayout.Space(6f);
            GUILayout.Label("Saldo je Woche", header);
            DrawChart(e);

            GUILayout.EndScrollView();
            GUI.DragWindow(new Rect(0f, 0f, 10000f, 22f));
        }

        void DrawChart(EconomySystem e)
        {
            var rect = GUILayoutUtility.GetRect(380f, 90f, GUILayout.ExpandWidth(true));
            GUI.Box(rect, GUIContent.none);
            var h = e.History;
            if (h.Count == 0) { GUI.Label(rect, "  noch keine abgeschlossene Woche", small); return; }

            int first = Mathf.Max(0, h.Count - ChartWeeks);
            double max = 1;
            for (int i = first; i < h.Count; i++) max = System.Math.Max(max, System.Math.Abs(h[i].Net));
            float mid = rect.y + rect.height * 0.5f;
            float barW = (rect.width - 8f) / ChartWeeks;
            var old = GUI.color;
            GUI.color = new Color(1f, 1f, 1f, 0.3f);
            GUI.DrawTexture(new Rect(rect.x + 2f, mid, rect.width - 4f, 1f), Texture2D.whiteTexture);
            for (int i = first; i < h.Count; i++)
            {
                double net = h[i].Net;
                float len = (float)(System.Math.Abs(net) / max) * (rect.height * 0.5f - 4f);
                float x = rect.x + 4f + (i - first) * barW;
                GUI.color = net >= 0 ? new Color(0.45f, 0.9f, 0.5f) : new Color(1f, 0.45f, 0.4f);
                GUI.DrawTexture(net >= 0 ? new Rect(x, mid - len, barW - 3f, len) : new Rect(x, mid + 1f, barW - 3f, len), Texture2D.whiteTexture);
            }
            GUI.color = old;
            GUI.Label(new Rect(rect.x + 4f, rect.y + 2f, rect.width, 16f), $"±{Money(max)}", small);
        }

        static Color BalanceColor(EconomySystem e) =>
            e.ConstructionFrozen ? new Color(1f, 0.44f, 0.38f) : e.IsOverdrawn ? new Color(1f, 0.7f, 0.25f) : new Color(0.5f, 1f, 0.55f);

        static string Money(double v) => $"{v:N0} €";
        static string Signed(double v) => (v >= 0 ? "+" : "−") + Money(System.Math.Abs(v));

        static string Colored(double amount, bool income)
        {
            if (amount == 0) return "–";
            return income ? $"<color=#7dff8a>+{Money(amount)}</color>" : $"<color=#ff9080>−{Money(amount)}</color>";
        }
    }
}
