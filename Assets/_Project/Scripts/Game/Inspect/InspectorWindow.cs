using System.Collections.Generic;
using CityBuilder.Game.Services;
using CityBuilder.Game.UI.Hud;
using CityBuilder.Game.UI.Panels;
using CityBuilder.Game.Utilities;
using CityBuilder.Simulation.Citizens;
using CityBuilder.Simulation.Core;
using CityBuilder.Simulation.Services;
using CityBuilder.Simulation.Traffic;
using CityBuilder.Simulation.Utilities;
using CityBuilder.Simulation.Zoning;
using UnityEngine;
using static CityBuilder.Game.Inspect.InspectorTexts;

namespace CityBuilder.Game.Inspect
{
    /// <summary>
    /// Rechtes Panel des Inspektors (UI Toolkit, Phase 2; Entwurf <c>Main.dc.html</c>/<c>Panels.dc.html</c>). Zeigt die
    /// Auswahl des <see cref="InspectorPanel"/> (Logik: Picking, Verlauf, Hervorhebung, Verfolgen) mit allen Inhalten
    /// des früheren IMGUI-Fensters: Gebäude (Übersicht/Bewohner/Arbeit/Umfeld), Bürger (Person/Alltag/Befinden),
    /// Fahrzeug, Versorgungsbauwerk, Dienstgebäude und ohne Auswahl die Stadtübersicht. Bewohner, Partner, Fahrer,
    /// Fahrzeug, Wohnung/Arbeitsplatz/Ziel sind anklickbar (Verlauf, ← Zurück). Liest nur.
    /// </summary>
    public sealed partial class InspectorWindow : GameWindow
    {
        static readonly Color AgeColor = BuildLabels.Hex(0x4A90D9);
        static readonly Color EduColor = BuildLabels.Hex(0x5DBB63);

        readonly InspectorPanel panel;
        readonly List<string> memoryTexts = new List<string>();
        UtilityOverlayView utilityOverlay;
        ServiceOverlayView serviceOverlay;

        public InspectorWindow(InspectorPanel panel) : base(WindowSlot.Right) { this.panel = panel; }

        public override float OpenScore => panel != null && panel.Active ? 100f : 0f;

        /// <summary>× verlässt den Info-Modus.</summary>
        public override void Close() => panel.Close();

        /// <summary>Esc: erst die Auswahl aufheben (Stadtübersicht), dann den Info-Modus verlassen.</summary>
        public override bool HandleEscape()
        {
            if (!panel.Current.IsNone) panel.ClearSelection();
            else panel.Close();
            return true;
        }

        public override void Tick()
        {
            ContentVersion = panel.SelectionVersion;
            if (utilityOverlay == null) utilityOverlay = Tool.GetComponent<UtilityOverlayView>();
            if (serviceOverlay == null) serviceOverlay = Tool.GetComponent<ServiceOverlayView>();
        }

        Selection Current => panel.Current;

        void Go(SelectionKind kind, int id) => panel.Select(new Selection(kind, id));

        // ------------------------------------------------------------ Kopf

        public override void DrawHead(Ui ui)
        {
            var world = World;
            var s = Current;
            switch (s.Kind)
            {
                case SelectionKind.Building:
                {
                    var b = world.City.GetBuilding(s.Id);
                    if (b == null) break;
                    string state = b.IsConstructed ? $"Stufe {b.Level}" : $"im Bau {b.Construction * 100f:0} %";
                    StandardHead(ui, BuildLabels.ZoneName(b.Zone), Tone.Muted, BuildingName(b),
                        $"{UseName(b.Zone)} · {state} · Grundstück {b.WidthCells}×{b.DepthCells}", false, BuildLabels.ZoneColor(b.Zone));
                    var tabs = BuildingTabs(b);
                    ui.Tabs(tabs, Mathf.Clamp(panel.Tab, 0, tabs.Count - 1), panel.SelectTab);
                    return;
                }
                case SelectionKind.Citizen:
                {
                    var c = world.Citizens.Get(s.Id);
                    if (c == null) break;
                    StandardHead(ui, "Bürger", Tone.Info, c.FullName,
                        $"{c.Age} Jahre · {world.Citizens.FamilyStatusText(c)} · {ActivityName(c)}", true, null,
                        Ui.Initials(c.FullName), Ui.AvatarColor(c.Id));
                    using (ui.Chips())
                    {
                        ui.Chip(CitizenSystem.SatisfactionText(c.Satisfaction), SatisfactionTone(c.Satisfaction));
                        ui.Chip(CitizenSystem.MoodText(c.Mood), SatisfactionTone(c.Mood));
                        ui.Chip(c.IsIll ? "krank" : CitizenSystem.HealthText(c), c.IsIll ? Tone.Warn : Tone.Info);
                        ui.Chip($"#{c.Id}", Tone.Muted);
                    }
                    ui.Tabs(new[] { "Person", "Alltag", "Befinden" }, Mathf.Clamp(panel.Tab, 0, 2), panel.SelectTab);
                    return;
                }
                case SelectionKind.Vehicle:
                {
                    var v = InspectorPanel.FindVehicle(world, s.Id);
                    if (v == null) break;
                    var c = world.Citizens.CitizenForTrip(v.TripId);
                    string sub = c != null ? $"Fahrer: {c.FullName} · {PurposeName(c.Purpose)}"
                        : v.TripId != 0 ? "Bürgerfahrt (beendet)" : "Durchgangs-/Hintergrundverkehr";
                    StandardHead(ui, "Fahrzeug", Tone.Info, $"{VehicleClasses.DisplayName(v.Class)} #{v.Id}", sub, true);
                    return;
                }
                case SelectionKind.Facility:
                {
                    var f = world.Utilities.GetFacility(s.Id);
                    if (f == null) break;
                    bool power = f.Kind == UtilityKind.Power;
                    StandardHead(ui, $"Versorgung {UtilityCatalog.KindName(f.Kind)}", power ? Tone.Warn : Tone.Info,
                        $"{f.Type.DisplayName} #{f.Id}", $"Netz {f.NetworkId + 1} · {f.Type.Width:0}×{f.Type.Depth:0} m", false);
                    return;
                }
                case SelectionKind.Service:
                {
                    var f = world.Services.GetFacility(s.Id);
                    if (f == null) break;
                    StandardHead(ui, $"Dienst {ServiceKinds.DisplayName(f.Type.Channel)}", Tone.Info, $"{f.Type.DisplayName} #{f.Id}",
                        $"{f.ServedBuildings} Gebäude versorgt · {f.Type.Width:0}×{f.Type.Depth:0} m", false);
                    return;
                }
            }
            // Keine (gültige) Auswahl: Stadtübersicht
            Head(ui, "Info-Modus (C)", Tone.Accent, GameHud.CityName, "Stadtübersicht · Klick auf Gebäude, Fahrzeug oder Bauwerk");
        }

        void StandardHead(Ui ui, string chip, Tone tone, string title, string subtitle, bool canFollow, Color? dot = null,
            string avatar = null, Color? avatarColor = null)
        {
            Head(ui, chip, tone, title, subtitle,
                back: panel.Back, backEnabled: panel.CanGoBack,
                following: panel.Following, toggleFollow: canFollow ? () => panel.Following = !panel.Following : (System.Action)null,
                chipDot: dot, avatar: avatar, avatarColor: avatarColor,
                extraButtons: u => u.IconButton(HudIconKind.People, panel.ClearSelection, false, true, "Übersicht: Auswahl aufheben (Rechtsklick)"));
        }

        static List<string> BuildingTabs(Building b)
        {
            var tabs = new List<string> { "Übersicht" };
            if (b.Households > 0) tabs.Add("Bewohner");
            if (b.Jobs > 0) tabs.Add("Arbeit");
            tabs.Add("Umfeld");
            return tabs;
        }

        // ------------------------------------------------------------ Inhalt

        public override void DrawBody(Ui ui)
        {
            var world = World;
            var s = Current;
            switch (s.Kind)
            {
                case SelectionKind.Building:
                {
                    var b = world.City.GetBuilding(s.Id);
                    if (b != null) { BuildingBody(ui, world, b); return; }
                    break;
                }
                case SelectionKind.Citizen:
                {
                    var c = world.Citizens.Get(s.Id);
                    if (c != null) { CitizenBody(ui, world, c); return; }
                    break;
                }
                case SelectionKind.Vehicle:
                {
                    var v = InspectorPanel.FindVehicle(world, s.Id);
                    if (v != null) { VehicleBody(ui, world, v); return; }
                    break;
                }
                case SelectionKind.Facility:
                {
                    var f = world.Utilities.GetFacility(s.Id);
                    if (f != null) { FacilityBody(ui, world, f); return; }
                    break;
                }
                case SelectionKind.Service:
                {
                    var f = world.Services.GetFacility(s.Id);
                    if (f != null) { ServiceBody(ui, world, f); return; }
                    break;
                }
            }
            CityBody(ui, world);
        }

        // ------------------------------------------------------------ gemeinsame Zeilen

        /// <summary>Ort als Zeile: Gebäude anklickbar, sonst Text (außerhalb/–).</summary>
        void PlaceField(Ui ui, SimulationWorld world, string label, Place p)
        {
            if (p.IsBuilding)
            {
                var b = world.City.GetBuilding(p.BuildingId);
                if (b != null) ui.FieldLink(label, BuildingName(b), () => Go(SelectionKind.Building, b.Id));
                else ui.Field(label, $"Gebäude #{p.BuildingId} (abgerissen)", Tone.Muted);
            }
            else ui.Field(label, p.IsOutside ? $"außerhalb (Außenanschluss {p.ConnectionNodeId})" : "–");
        }

        /// <summary>Bürger als Listenzeile mit Avatar und Stimmung; Klick wählt ihn aus.</summary>
        void CitizenItem(Ui ui, Citizen m, string sub = null)
        {
            ui.ListItem(Ui.Initials(m.FullName), Ui.AvatarColor(m.Id), m.FullName, sub ?? Describe(m),
                CitizenSystem.MoodText(m.Mood), SatisfactionTone(m.Mood), () => Go(SelectionKind.Citizen, m.Id));
        }

        /// <summary>Seitenweise Liste: liefert Startindex, zeichnet den Blätterer.</summary>
        int Pager(Ui ui, int count)
        {
            int pages = Mathf.Max(1, (count + InspectorPanel.PageSize - 1) / InspectorPanel.PageSize);
            panel.Page = Mathf.Clamp(panel.Page, 0, pages - 1);
            ui.Pager(panel.Page, pages, p => panel.Page = Mathf.Clamp(p, 0, pages - 1));
            return panel.Page * InspectorPanel.PageSize;
        }

        static Tone SatisfactionTone(float s) => s < 0f ? Tone.Muted : s >= 0.6f ? Tone.Good : s >= 0.45f ? Tone.Accent : Tone.Warn;

        static Tone CoverageTone(float c) => c >= 0.6f ? Tone.Good : c >= 0.3f ? Tone.Accent : Tone.Warn;

        /// <summary>Kennwert 0..1 mit Balken; negativ = „niemand da“.</summary>
        static void SatisfactionMeter(Ui ui, string label, float s, string note = null)
        {
            string text = s < 0f ? "– (niemand da)" : $"{CitizenSystem.SatisfactionText(s)} · {s * 100f:0} %";
            ui.Meter(label, text, Mathf.Max(0f, s), SatisfactionTone(s));
            if (!string.IsNullOrEmpty(note)) ui.Note(note);
        }
    }
}
