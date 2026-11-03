using System.Linq;
using CityBuilder.Game.Services;
using CityBuilder.Game.UI.Hud;
using CityBuilder.Game.UI.Panels;
using CityBuilder.Game.Utilities;
using CityBuilder.Simulation.Citizens;
using CityBuilder.Simulation.Core;
using CityBuilder.Simulation.LandValue;
using CityBuilder.Simulation.Services;
using CityBuilder.Simulation.Traffic;
using CityBuilder.Simulation.Utilities;
using CityBuilder.Simulation.Zoning;
using UnityEngine;
using static CityBuilder.Game.Inspect.InspectorTexts;

namespace CityBuilder.Game.Inspect
{
    /// <summary>Inhalte des Inspektors je Auswahlart (aus dem früheren IMGUI-Inspektor übernommen).</summary>
    public sealed partial class InspectorWindow
    {
        // ------------------------------------------------------------ Gebäude

        void BuildingBody(Ui ui, SimulationWorld world, Building b)
        {
            var tabs = BuildingTabs(b);
            string current = tabs[Mathf.Clamp(panel.Tab, 0, tabs.Count - 1)];
            switch (current)
            {
                case "Übersicht": BuildingOverview(ui, world, b); break;
                case "Bewohner": ResidentsTab(ui, world, b); break;
                case "Arbeit": WorkersTab(ui, world, b); break;
                case "Firma": FirmTab(ui, world, b); break; // G5 Warenketten (InspectorWindow.Goods.cs)
                default: SurroundingsTab(ui, world, b); break;
            }
        }

        void BuildingOverview(Ui ui, SimulationWorld world, Building b)
        {
            var citizens = world.Citizens;
            using (ui.Grid(2))
            {
                if (b.Households > 0)
                {
                    ui.Stat("Bewohner", BuildLabels.Number(b.Residents));
                    ui.Stat("Haushalte", BuildLabels.Number(b.OccupiedHouseholds), $"/ {b.Households}");
                }
                if (b.Jobs > 0) ui.Stat("Arbeitsplätze", BuildLabels.Number(b.Workers), $"/ {b.Jobs}");
                ui.Stat("Gerade anwesend", BuildLabels.Number(citizens.PresentIn(b.Id)));
            }

            using (ui.Card())
            {
                if (b.Households > 0) SatisfactionMeter(ui, "Zufriedenheit Bewohner", citizens.ResidentSatisfaction(b.Id));
                if (b.Jobs > 0) SatisfactionMeter(ui, "Zufriedenheit Mitarbeiter", citizens.WorkerSatisfaction(b.Id), "Arbeit + Arbeitsweg");
                float surroundings = citizens.SurroundingsScore(b);
                ui.Meter("Zufriedenheit Umfeld", Percent(surroundings), surroundings, SatisfactionTone(surroundings));
                ui.Note("Ruhe, Sicherheit, Dienste, Park");
                float condition = citizens.BuildingCondition(b);
                ui.Meter("Wohnqualität", Percent(condition), condition, SatisfactionTone(condition));
                ui.Note("Stufe × Zustand, ohne Versorgung schlechter");
            }

            LandValueAndLevel(ui, world, b);

            ui.Section("Versorgung");
            using (ui.Fields())
            {
                UtilityField(ui, world, b, UtilityKind.Power);
                UtilityField(ui, world, b, UtilityKind.Water);
                // --- G3 erweitert: Abwasser, Trinkwasser, Atomkraftwerk nebenan
                UtilityField(ui, world, b, UtilityKind.Sewage);
                if (world.WaterQuality != null && world.Utilities.IsSupplied(b, UtilityKind.Water))
                {
                    float q = world.WaterQuality.DrinkingWaterQuality(b);
                    ui.Field("Trinkwasser", $"{q * 100f:0} % Qualität" + (q < WaterQualitySystem.BadDrinkingWater ? " · verschmutzt (Abwasser im Fluss an der Pumpe) → Gesundheit sinkt" : ""),
                        q < WaterQualitySystem.BadDrinkingWater ? Tone.Warn : Tone.Good);
                }
                float concern = world.Utilities.ConcernAt(b.Position);
                if (concern > 0f) ui.Field("Atomkraftwerk", $"in der Nähe · Sorge {concern * 100f:0} % (wirkt nach Ängstlichkeit)", Tone.Accent);
                // --- Ende
            }

            ui.Section("Gebäude");
            using (ui.Fields())
            {
                ui.Field("Grundstück", $"{b.WidthCells}×{b.DepthCells} Zellen ({b.Width:0}×{b.Depth:0} m)");
                var view = panel.BuildingView;
                ui.Field("Höhe (sichtbar)", view != null ? $"≈ {view.VisualHeight(b):0} m" : "–");
                ui.Field("Stufe", b.Level.ToString());
                ui.Field("Baufortschritt", $"{b.Construction * 100f:0} %", b.IsConstructed ? Tone.None : Tone.Accent);
                if (b.Households > 0) ui.Field("Wohnungen", $"{b.OccupiedHouseholds}/{b.Households} belegt · {b.Residents} Bewohner");
                if (b.Jobs > 0)
                {
                    var ws = citizens.WorkersAt(b.Id);
                    int inbound = ws.Count(w => w.IsCommuterIn);
                    ui.Field("Arbeitsplätze", $"{b.Workers}/{b.Jobs} besetzt · {ws.Count - inbound} Einheimische, {inbound} Einpendler");
                }
                ui.Field("Gerade anwesend", citizens.PresentIn(b.Id).ToString());
                var seg = world.Roads.GetSegment(b.FrontSegmentId);
                ui.Field("Zufahrt", seg != null ? $"{seg.Type.DisplayName} (Segment {seg.Id})" : "keine", seg != null ? Tone.None : Tone.Warn);
            }
        }

        static void UtilityField(Ui ui, SimulationWorld world, Building b, UtilityKind kind)
        {
            var u = world.Utilities;
            var s = u.GetStatus(b);
            string name = UtilityCatalog.KindName(kind);
            float demand = u.DemandOf(b, kind); // G5: Industrie nach Rezept
            string need = $"Bedarf {demand:0.#} {UtilityCatalog.Unit(kind)}";
            if (!b.IsConstructed) { ui.Field(name, "– (im Bau, noch kein Bedarf)", Tone.Muted); return; }
            if (s.Has(kind))
            {
                var net = u.NetworkOf(b, kind);
                ui.Field(name, $"versorgt · {need}{(net != null ? $" · Netz {net.Id + 1}" : "")}", Tone.Good);
                return;
            }
            if (!u.IsRequired(kind))
            {
                ui.Field(name, $"nicht versorgt – noch kein Versorger in der Stadt (folgenlos) · {need}", Tone.Muted);
                return;
            }
            int without = s.TicksWithout(kind);
            string left = u.AbandonmentEnabled
                ? $" · verlassen in ≈ {Duration(Mathf.Max(0, (kind == UtilityKind.Sewage ? UtilitySystem.SewageAbandonAfterTicks : UtilitySystem.AbandonAfterTicks) - without))}"
                : "";
            ui.Field(name, $"fehlt seit {Duration(without)}{left} · {need}", Tone.Warn);
        }

        void ResidentsTab(Ui ui, SimulationWorld world, Building b)
        {
            var hs = world.Citizens.HouseholdsIn(b.Id);
            ui.Section($"Haushalte {hs.Count}/{b.Households}", $"{b.Residents} Bewohner");
            if (hs.Count == 0) { ui.Note("Noch niemand eingezogen."); return; }
            using (ui.Chips())
                foreach (var g in hs.GroupBy(h => h.Kind).OrderBy(g => g.Key))
                    ui.Chip($"{HouseholdKinds.DisplayName(g.Key)} {g.Count()}", Tone.Muted);

            int start = Pager(ui, hs.Count);
            for (int i = start; i < Mathf.Min(hs.Count, start + InspectorPanel.PageSize); i++)
            {
                var h = hs[i];
                ui.Section($"Haushalt {h.Id}", $"{HouseholdKinds.DisplayName(h.Kind)} · seit Tag {h.MovedInDay}");
                foreach (var m in h.Members) CitizenItem(ui, m);
            }
        }

        void WorkersTab(Ui ui, SimulationWorld world, Building b)
        {
            var ws = world.Citizens.WorkersAt(b.Id);
            int inbound = ws.Count(w => w.IsCommuterIn);
            int present = ws.Count(w => w.Activity == Activity.Work && w.Location.BuildingId == b.Id);
            using (ui.Grid(2))
            {
                ui.Stat("Beschäftigte", BuildLabels.Number(ws.Count), $"/ {b.Jobs}");
                ui.Stat("Offene Stellen", BuildLabels.Number(Mathf.Max(0, b.Jobs - ws.Count)));
                ui.Stat("Einheimische", BuildLabels.Number(ws.Count - inbound));
                ui.Stat("Einpendler", BuildLabels.Number(inbound));
            }
            var edu = new int[4];
            foreach (var w in ws) edu[(int)w.Education]++;
            using (ui.Fields())
            {
                ui.Field("Bildung", $"ohne {edu[0]} · Grund {edu[1]} · mittel {edu[2]} · hoch {edu[3]}");
                ui.Field("Gerade bei der Arbeit", present.ToString());
            }
            if (ws.Count == 0) return;

            ui.Section("Beschäftigte");
            int start = Pager(ui, ws.Count);
            for (int i = start; i < Mathf.Min(ws.Count, start + InspectorPanel.PageSize); i++)
            {
                var w = ws[i];
                string from = w.IsCommuterIn ? "Einpendler" : $"wohnt in #{w.Home.BuildingId}";
                CitizenItem(ui, w, $"{w.Age} J., {EducationName(w.Education)} · {from} · {ActivityName(w)}");
            }
        }

        void SurroundingsTab(Ui ui, SimulationWorld world, Building b)
        {
            var env = world.Environment;
            float noise = env.Noise.Sample(b.Position);
            ui.Section("Umwelt am Gebäude");
            using (ui.Card())
            {
                ui.Meter("Lärm", $"{noise * 100f:0} % (Straßenseite {env.Noise.Sample(b.Entrance) * 100f:0} %)", noise,
                    noise >= 0.6f ? Tone.Warn : noise >= 0.3f ? Tone.Accent : Tone.Good);
                float air = env.AirPollution.Sample(b.Position);
                ui.Meter("Abgas", $"{air:0.00} (Rasterwert, ohne Einheit)", Mathf.Clamp01(air), air >= 0.5f ? Tone.Warn : air >= 0.2f ? Tone.Accent : Tone.Good);
            }

            // G4: Abdeckung je Dienstart (0..1, sinkt mit Fahrzeit und Überlastung)
            ui.Section("Dienste in Reichweite");
            var services = world.Services;
            using (ui.Card())
            {
                foreach (var k in ServiceKinds.All)
                {
                    float c = services.Coverage(k, b);
                    ui.Meter(ServiceKinds.DisplayName(k), $"{c * 100f:0} %", c, CoverageTone(c));
                    if (k == ServiceKinds.Education)
                        ui.Note($"Grundschule {services.Coverage(ServiceKinds.EducationPrimary, b) * 100f:0} %, " +
                                $"Oberschule {services.Coverage(ServiceKinds.EducationSecondary, b) * 100f:0} %");
                }
            }
            using (ui.Fields())
            {
                float level = services.GarbageLevel(b);
                ui.Field("Müll am Gebäude", $"{services.GarbageAt(b):0.##} t ({level * 100f:0} % voll)", level >= 0.5f ? Tone.Warn : Tone.None);
                ui.Field("Brandrisiko", $"{services.FireRisk(b) * 100f:0} % (Kennwert, noch ohne Brände)");
                ui.Field("Zufriedenheit Umfeld", Percent(world.Citizens.SurroundingsScore(b)));
            }

            ui.Section("Gelände");
            var terrain = world.Terrain;
            float natural = terrain.NaturalHeightAt(b.Position);
            using (ui.Fields())
            {
                ui.Field("Bodenplatte", $"{b.Height:0.0} m (Wasserspiegel {terrain.WaterLevel:0} m)");
                ui.Field("Ursprüngliches Gelände", $"{natural:0.0} m ({(b.Height - natural >= 0 ? "+" : "")}{b.Height - natural:0.0} m eingeebnet)");
            }

            LandValueFactorsSection(ui, world, b);
            DepositsSection(ui, world, b.Position, "auf dem Grundstück");
        }

        /// <summary>Welt (Etappe 5): Vorkommen am Punkt (<c>world.Resources.DepositAt</c>, 0..1) mit Menge je Hektar.</summary>
        static void DepositsSection(Ui ui, SimulationWorld world, System.Numerics.Vector2 p, string where)
        {
            var r = world.Resources;
            if (r == null) return;
            ui.Section("Vorkommen", where + " · Ansicht F3");
            using (ui.Card())
                foreach (var id in CityBuilder.Simulation.World.ResourceIds.All)
                {
                    var kind = r.Kind(id);
                    if (kind == null) continue;
                    float v = r.DepositAt(id, p);
                    ui.Meter(kind.DisplayName, $"{v * 100f:0} % · ≈ {BuildLabels.Number(v * kind.AmountPerHectare)} {kind.Unit}/ha", v,
                        v >= 0.5f ? Tone.Good : v >= 0.2f ? Tone.Accent : Tone.Muted);
                }
        }

        // ------------------------------------------------------------ G6 Bodenwert, Ausbaustufe, Zustand

        static Tone ValueTone(float v) => v < 0f ? Tone.Muted : v >= 0.55f ? Tone.Good : v >= 0.3f ? Tone.Accent : Tone.Warn;

        /// <summary>Übersicht: Bodenwert, Stufe mit Fortschritt und „was fehlt“, Zustand der Bausubstanz.</summary>
        static void LandValueAndLevel(Ui ui, SimulationWorld world, Building b)
        {
            var lv = world.LandValue;
            if (lv == null) return;
            float value = lv.ValueOf(b);
            var info = lv.LevelInfo(b);
            ui.Section("Bodenwert & Ausbaustufe", "Faktoren im Reiter Umfeld");
            using (ui.Card())
            {
                ui.Meter("Bodenwert", value < 0f ? "noch unbekannt" : $"{LandValueRules.ValueText(value)} · {value * 100f:0} %",
                    Mathf.Max(0f, value), ValueTone(value));
                bool top = info.Level >= info.MaxLevel;
                ui.Meter($"Stufe {info.Level} von {info.MaxLevel}",
                    top ? "höchste Stufe" : $"Aufstieg {info.ProgressDays}/{info.DaysNeeded} Tage", info.Progress,
                    top ? Tone.Good : info.Missing == LevelGap.None ? Tone.Good : Tone.Accent);
                if (!string.IsNullOrEmpty(info.MissingText) && info.Missing != LevelGap.None && info.Missing != LevelGap.MaxLevel)
                    ui.Note("Es fehlt: " + info.MissingText);
                ui.Meter("Zustand", $"{info.ConditionText} · {info.Condition * 100f:0} %", info.Condition, ValueTone(info.Condition));
            }
            if (info.Declining)
                ui.Message("Abstieg droht", $"Seit {info.DeclineDays} von {info.DeclineDaysLimit} Tagen unter den Anforderungen der Stufe {info.Level}.", Tone.Warn);
            if (info.EmptyDays > 0)
                ui.Note($"Seit {info.EmptyDays} Tagen leer.");
        }

        /// <summary>Umfeld: Bodenwert mit allen Beiträgen und den Anforderungen der nächsten Stufe.</summary>
        static void LandValueFactorsSection(Ui ui, SimulationWorld world, Building b)
        {
            var lv = world.LandValue;
            ui.Section("Bodenwert", "G6 · Ansicht F2");
            if (lv == null) { ui.Note("Bodenwert nicht verfügbar."); return; }
            var f = lv.Explain(b);
            if (f.IsWater) { ui.Note("Grundstück im Wasser – Bodenwert 0."); return; }
            using (ui.Fields())
            {
                ui.Field("Aktuell", f.Current < 0f ? "noch nicht berechnet" : $"{LandValueRules.ValueText(f.Current)} · {f.Current * 100f:0} %", ValueTone(f.Current));
                ui.Field("Zielwert", $"{f.Target * 100f:0} % (Bodenwert läuft träge dorthin)");
                foreach (var (label, contribution) in f.Contributions())
                    ui.Field(label, $"{(contribution >= 0f ? "+" : "−")}{System.Math.Abs(contribution) * 100f:0.#} %",
                        contribution >= 0f ? Tone.Good : Tone.Warn);
            }

            var info = lv.LevelInfo(b);
            if (info.Level >= info.MaxLevel) { ui.Note("Höchste Ausbaustufe erreicht."); return; }
            var next = info.Next;
            var facts = info.Facts;
            ui.Section($"Für Stufe {info.Level + 1}", $"{next.Days} Tage erfüllt");
            using (ui.Fields())
            {
                Requirement(ui, "Bodenwert", facts.LandValue, next.LandValue);
                Requirement(ui, "Zufriedenheit", facts.Satisfaction, next.Satisfaction);
                if (next.Skill > 0f) Requirement(ui, "Bildung/Fachkräfte", facts.Skill, next.Skill);
                if (next.Occupancy > 0f) Requirement(ui, "Belegung", facts.Occupancy, next.Occupancy);
                if (next.Condition > 0f) Requirement(ui, "Zustand", info.Condition, next.Condition);
                ui.Field("Versorgung", facts.Supplied ? "vorhanden" : "fehlt", facts.Supplied ? Tone.Good : Tone.Warn);
            }
        }

        static void Requirement(Ui ui, string label, float have, float need)
        {
            bool ok = have >= need;
            ui.Field(label, have < 0f ? $"– / mind. {need * 100f:0} %" : $"{have * 100f:0} % / mind. {need * 100f:0} %", ok ? Tone.Good : Tone.Warn);
        }

        // ------------------------------------------------------------ Bürger

        void CitizenBody(Ui ui, SimulationWorld world, Citizen c)
        {
            switch (Mathf.Clamp(panel.Tab, 0, 2))
            {
                case 0: PersonTab(ui, world, c); break;
                case 1: DailyTab(ui, world, c); break;
                default: WellbeingTab(ui, world, c); break;
            }
        }

        void PersonTab(Ui ui, SimulationWorld world, Citizen c)
        {
            ui.Section("Person");
            using (ui.Fields())
            {
                ui.Field("Alter", $"{c.Age} J. (Geburtstag: Tag {c.BirthDayOfYear + 1} des Jahres)");
                ui.Field("Lebensphase", StageName(c));
                ui.Field("Bildung", EducationName(c.Education) + (c.Age < Citizen.AdultAge ? " (Abschluss mit 18)" : ""));
                ui.Field("Familienstand", world.Citizens.FamilyStatusText(c));
                var partner = world.Citizens.PartnerOf(c);
                if (partner != null)
                    ui.FieldLink("Partner", $"{partner.FullName} ({partner.Age} J.)", () => Go(SelectionKind.Citizen, partner.Id));
            }

            ui.Section("Persönlichkeit");
            ui.Text(c.Personality.Describe(c.Gender));
            var traits = Traits(c.Personality);
            using (ui.Chips())
            {
                if (traits.Count == 0) ui.Chip("ausgeglichen", Tone.Muted);
                foreach (var t in traits) ui.Chip(t, Tone.None);
            }

            ui.Section("Haushalt & Wohnung");
            if (c.Household == null)
            {
                using (ui.Fields())
                {
                    ui.Field("Haushalt", "keiner – wohnt außerhalb (Einpendler)");
                    PlaceField(ui, world, "Zuhause", c.Home);
                }
                return;
            }
            var h = c.Household;
            using (ui.Fields())
            {
                ui.Field("Haushaltsart", $"{HouseholdKinds.DisplayName(h.Kind)} ({h.Members.Count} Personen, Haushalt {h.Id})");
                PlaceField(ui, world, "Wohnung", c.Home);
                ui.Field("Eingezogen", $"Tag {h.MovedInDay}");
            }
            foreach (var m in h.Members)
                if (m != c) CitizenItem(ui, m);

            if (c.Home.IsBuilding && world.City.GetBuilding(c.Home.BuildingId) != null)
                using (ui.Actions())
                {
                    int home = c.Home.BuildingId;
                    ui.ActionButton("Wohnung zeigen", () => Go(SelectionKind.Building, home), ActionStyle.Primary);
                    ui.ActionButton(panel.Following ? "Folgen aus" : "Folgen", () => panel.Following = !panel.Following,
                        ActionStyle.Secondary, panel.Following, true, "Kamera folgt dem Bürger (F)");
                }
        }

        void DailyTab(Ui ui, SimulationWorld world, Citizen c)
        {
            ui.Section("Arbeit");
            using (ui.Fields())
            {
                if (c.WorkplaceId != 0) PlaceField(ui, world, "Arbeitsplatz", c.Workplace);
                else if (c.WorksOutside) ui.Field("Arbeitsplatz", $"außerhalb – Auspendler über Anschluss {c.OutsideWorkConnectionId}");
                else if (c.IsWorkingAge) ui.Field("Arbeitsplatz", $"arbeitslos seit {c.UnemployedDays} Tagen", Tone.Warn);
                else ui.Field("Arbeitsplatz", c.Stage == LifeStage.Senior ? "Rente" : c.Stage == LifeStage.Student ? "Schüler (bleibt vorerst zu Hause)" : "Kind");
                if (c.IsEmployed) ui.Field("Arbeitszeit", $"ab {Clock(c.WorkStart)} Uhr, {CitizenSystem.WorkTicks * 6 / 60} h");
                if (c.IsCommuterIn) ui.Field("Pendeln", "Einpendler (wohnt außerhalb)");
                else if (c.WorksOutside) ui.Field("Pendeln", "Auspendler");
            }

            ui.Section("Gerade");
            using (ui.Fields())
            {
                ui.Field("Tätigkeit", ActivityName(c));
                if (c.Activity != Activity.Travelling) PlaceField(ui, world, "Ort", c.Location);
                else
                {
                    ui.Field("Zweck", PurposeName(c.Purpose));
                    PlaceField(ui, world, "Ziel", c.Destination);
                    ui.Field("Unterwegs seit", Duration(world.Clock.Tick - c.TripStartTick) + $" (ab {Clock(c.TripStartTick)} Uhr)");
                    if (c.Mode == TravelMode.Car)
                    {
                        var v = world.Citizens.VehicleOf(c);
                        if (v != null)
                            ui.FieldLink("Verkehrsmittel", $"Auto – {VehicleClasses.DisplayName(v.Class)} #{v.Id}", () => Go(SelectionKind.Vehicle, v.Id));
                        else ui.Field("Verkehrsmittel", "Auto (gleich am Ziel)");
                    }
                    else if (c.Mode == TravelMode.Transit) CitizenTransitFields(ui, world, c); // ÖPNV
                    else
                    {
                        ui.Field("Verkehrsmittel", "zu Fuß/Rad (Platzhalter, Luftlinie 15 km/h)");
                        long eta = c.PlannedArrivalTick - world.Clock.Tick;
                        ui.Field("Ankunft", eta > 0 ? $"≈ {Clock(c.PlannedArrivalTick)} Uhr (in {Duration(eta)})" : "gleich");
                    }
                }
                if (c.Activity == Activity.Leisure) ui.Field("Freizeit", CitizenSystem.LeisurePlaceText(c));
            }

            ui.Section("Einkaufen");
            using (ui.Card())
            {
                float need = c.ShoppingNeed;
                ui.Meter("Einkaufsdrang", need >= 1f ? $"{need:0.00} · dringend" : $"{need:0.00}", Mathf.Clamp01(need),
                    need >= 1f ? Tone.Warn : Tone.Accent);
            }
        }

        void WellbeingTab(Ui ui, SimulationWorld world, Citizen c)
        {
            using (ui.Card())
            {
                SatisfactionMeter(ui, "Zufriedenheit", c.Satisfaction);
                ui.Meter("Gemüt", $"{CitizenSystem.MoodText(c.Mood)} · {Percent(c.Mood)}", c.Mood, SatisfactionTone(c.Mood));
                ui.Meter("Gesundheit", $"{CitizenSystem.HealthText(c)} · {Percent(c.Health)}" + (c.IsIll ? " · krank" : ""), c.Health,
                    c.IsIll ? Tone.Warn : SatisfactionTone(c.Health));
            }
            using (ui.Fields()) ui.Field("Sorgen", CitizenSystem.Concerns(c));

            ui.Section("Bedürfnisse", "Erfüllung");
            using (ui.Grid(2))
                foreach (var k in Needs.All)
                {
                    float v = c.Need(k);
                    ui.Need(Needs.DisplayName(k), $"{Needs.Describe(v)} · {v * 100f:0} %", v);
                }

            ui.Section("Erinnerungen");
            world.Citizens.MemoryTexts(c, memoryTexts);
            if (memoryTexts.Count == 0) ui.Note("Noch nichts Besonderes erlebt.");
            for (int i = 0; i < memoryTexts.Count; i++) ui.Quote(i == 0 ? "Erinnert sich" : null, $"„{memoryTexts[i]}“");

            ui.Section("Einflüsse", "Rohwerte");
            using (ui.Fields())
            {
                var hb = c.Home.IsBuilding ? world.City.GetBuilding(c.Home.BuildingId) : null;
                if (hb != null)
                {
                    var env = world.Environment;
                    ui.Field("Lärm an der Wohnung", $"{env.Noise.Sample(hb.Position) * 100f:0} %");
                    ui.Field("Abgas an der Wohnung", $"{env.AirPollution.Sample(hb.Position):0.00}");
                    var s = world.Utilities.GetStatus(hb);
                    ui.Field("Wohnung Strom/Wasser", $"{(s.HasPower ? "ja" : "nein")} / {(s.HasWater ? "ja" : "nein")}",
                        s.HasPower && s.HasWater ? Tone.None : Tone.Warn);
                }
                else ui.Field("Wohnung", "außerhalb – keine Werte");
                if (c.IsWorkingAge) ui.Field("Arbeit", c.IsEmployed ? "beschäftigt" : $"arbeitslos seit {c.UnemployedDays} Tagen");
                ui.Field("Einkaufsbedürfnis", $"{c.ShoppingNeed:0.00}");
                if (c.Household != null) ui.Field("Haushaltseinkommen", $"{c.Household.Income:0} € je Tag (brutto)");
            }
        }

        // ------------------------------------------------------------ Fahrzeug

        void VehicleBody(Ui ui, SimulationWorld world, Vehicle v)
        {
            var traffic = world.Traffic;
            var c = world.Citizens.CitizenForTrip(v.TripId);
            using (ui.Grid(2))
            {
                ui.Stat("Geschwindigkeit", $"{v.Speed * 3.6f:0}", "km/h");
                ui.Stat("Fahrtdauer", FormatSeconds(traffic.Time - v.StartTime), "Verkehrszeit");
            }
            if (BusFields(ui, world, v)) return; // ÖPNV: Linienbus
            string target = c != null && c.Destination.IsBuilding && world.City.GetBuilding(c.Destination.BuildingId) is Building tb
                ? BuildingName(tb) : "Ziel";
            using (ui.Card())
                ui.Progress(v.RouteProgress, Tone.Info, "Strecke", $"{v.RemainingEdges} Abschnitte bis {target}");

            ui.Section("Fahrt");
            using (ui.Fields())
            {
                ui.Field("Geschwindigkeit", $"{v.Speed * 3.6f:0} km/h (max. {v.MaxSpeed * 3.6f:0} km/h)");
                if (v.WaitTime > 0.5f) ui.Field("Steht seit", $"{v.WaitTime:0} s", Tone.Accent);
                ui.Field("Fahrtdauer", $"{FormatSeconds(traffic.Time - v.StartTime)} Verkehrszeit");
                if (v.InJunction) ui.Field("Ort", "in einer Kreuzung");
                else if (v.Lane != null) ui.Field("Ort", $"{v.Lane.Segment.Type.DisplayName} (Segment {v.Lane.Segment.Id})");
                ui.Field("Restliche Abschnitte", v.RemainingEdges.ToString());
            }

            ui.Section("Ziel & Fahrer");
            using (ui.Fields())
            {
                if (c != null)
                {
                    PlaceField(ui, world, "Ziel", c.Destination);
                    ui.Field("Zweck", PurposeName(c.Purpose));
                    ui.FieldLink("Fahrer", $"{c.FullName} · {c.Age} J. · {(c.IsCommuterIn ? "Einpendler" : "Einwohner")}",
                        () => Go(SelectionKind.Citizen, c.Id));
                    ui.Field("Unterwegs seit", $"{Duration(world.Clock.Tick - c.TripStartTick)} Spielzeit");
                }
                else if (v.TripId < 0 && world.Goods?.ShipmentForTrip(v.TripId) != null)
                    ui.Field("Fahrer", "Lieferfahrt (Lkw des Warensystems, s. Ladung)"); // G5
                else
                {
                    ui.Field("Ziel", v.HasSegmentDestination ? $"Zufahrt an Segment {v.DestinationSegmentId}"
                        : v.Destination != null ? $"Knoten {v.Destination.Id} (Außenanschluss/Kartenrand)" : "–");
                    ui.Field("Fahrer", v.TripId != 0 ? "Bürgerfahrt (beendet)" : "Durchgangs-/Hintergrundverkehr (kein Bürger)");
                }
            }
            ShipmentSection(ui, world, v); // G5: Ladung des Lkw
            ui.Note("Verkehr läuft mit 0,25 s je Tick, die Uhr mit 6 min je Tick – Autofahrten dauern daher in Spielzeit oft Stunden.");
            using (ui.Actions())
            {
                if (c != null) ui.ActionButton("Fahrer zeigen", () => Go(SelectionKind.Citizen, c.Id), ActionStyle.Primary);
                ui.ActionButton(panel.Following ? "Folgen aus" : "Folgen", () => panel.Following = !panel.Following,
                    ActionStyle.Secondary, panel.Following, true, "Kamera folgt dem Fahrzeug (F)");
            }
        }

        // ------------------------------------------------------------ Versorgungsbauwerk

        void FacilityBody(Ui ui, SimulationWorld world, UtilityFacility f)
        {
            var t = f.Type;
            string unit = UtilityCatalog.Unit(f.Kind);
            using (ui.Grid(3))
            {
                ui.Stat("Leistung", BuildLabels.Number(f.Output), unit);
                ui.Stat("Auslastung", t.Capacity > 0f ? $"{f.Output / t.Capacity * 100f:0} %" : "–");
                ui.Stat("Unterhalt", BuildLabels.Number(t.UpkeepPerDay), "€/Tag");
            }

            ui.Section("Leistung");
            using (ui.Fields())
            {
                ui.Field("Aktuell", $"{f.Output:0.#} {unit} von {t.Capacity:0.#} {unit}");
                var nets = world.Utilities.Networks(f.Kind);
                var net = f.NetworkId >= 0 && f.NetworkId < nets.Count ? nets[f.NetworkId] : null;
                if (net != null)
                {
                    ui.Field("Netz", $"{net.Id + 1}: Erzeugung {net.Production:0.#} / Bedarf {net.Demand:0.#} {unit}" +
                                     (net.HasShortage ? " · Unterdeckung" : ""), net.HasShortage ? Tone.Warn : Tone.Good);
                    ui.Field("Im Netz", $"{net.Facilities} Bauwerke · {net.SuppliedBuildings}/{net.Buildings} Gebäude versorgt · {net.Lines} Leitungen");
                }
                else ui.Field("Netz", "nicht angeschlossen", Tone.Warn);
            }

            ui.Section("Bauwerk");
            using (ui.Fields())
            {
                ui.Field("Größe", $"{t.Width:0}×{t.Depth:0} m · Boden {f.Height:0.0} m");
                ui.Field("Kosten / Unterhalt", $"{BuildLabels.Money(t.Cost)} / {BuildLabels.Money(t.UpkeepPerDay)} je Tag (Stadtkasse)");
                if (t.NoiseLevel > 0f) ui.Field("Lärm", $"{t.NoiseLevel * 100f:0} % im Umkreis {t.NoiseRadius:0} m");
                if (t.Pollution > 0f) ui.Field("Abgas", $"{t.Pollution:0.##} im Umkreis {t.PollutionRadius:0} m");
                if (t.NeedsWater) ui.Field("Standort", "braucht Wasser in Reichweite");
                ui.Field("Vorderseite", t.NeedsRoadFront ? "braucht Zufahrt zur Straße (Pfeil in der Vorschau)" : "beliebig (keine Zufahrt nötig)", Tone.Muted); // UX
                // --- G3 erweitert: Abwasser, Pumpe, Atom
                var wq = world.WaterQuality;
                if (wq != null && t.Kind == UtilityKind.Sewage)
                    ui.Field(t.Untreated ? "Eingeleitet" : "Geklärt", t.Untreated
                        ? $"{wq.DischargeOf(f):0.#} m³/h ungeklärt ins Gewässer · verschmutzt flussabwärts"
                        : $"{wq.TreatedBy(f):0.#} m³/h", t.Untreated && wq.DischargeOf(f) > 0f ? Tone.Warn : Tone.Good);
                if (wq != null && t.Kind == UtilityKind.Water && t.NeedsWater)
                {
                    float pq = wq.PumpQuality(f);
                    ui.Field("Wasserqualität", $"{pq * 100f:0} % am Ansaugpunkt", pq < WaterQualitySystem.BadDrinkingWater ? Tone.Warn : Tone.Good);
                }
                if (t.ConcernRadius > 0f)
                {
                    ui.Field("Akzeptanz", $"Anwohner im Umkreis {t.ConcernRadius:0} m sorgen sich (je nach Ängstlichkeit) · kein Abgas");
                    int left = world.Utilities.OfflineDaysLeft(f);
                    ui.Field("Betrieb", left > 0 ? $"nach Störfall abgeschaltet, noch {left} Tage" : $"läuft · Störfälle bisher {world.Utilities.Incidents}",
                        left > 0 ? Tone.Warn : Tone.None);
                }
                // --- Ende
                ui.Field("Zustand", "— (Verschleiß/Wartung später)", Tone.Muted);
            }
            DepositsSection(ui, world, f.Position, "am Standort");
            MoveAction(ui, SelectionKind.Facility, f.Id, t.Cost); // UX: Verschieben
            if (utilityOverlay != null)
            {
                var overlay = f.Kind == UtilityKind.Power ? UtilityOverlayView.Overlay.Power : f.Kind == UtilityKind.Sewage ? UtilityOverlayView.Overlay.Sewage : UtilityOverlayView.Overlay.Water;
                using (ui.Actions())
                    ui.ActionButton("Netz anzeigen", () => utilityOverlay.Toggle(overlay), ActionStyle.Secondary,
                        utilityOverlay.Current == overlay, true, "Info-Ansicht Strom/Wasser (O)");
            }
        }

        // ------------------------------------------------------------ Dienstgebäude (G4)

        void ServiceBody(Ui ui, SimulationWorld world, ServiceFacility f)
        {
            var t = f.Type;
            using (ui.Grid(3))
            {
                ui.Stat("Kapazität", BuildLabels.Number(t.Capacity), $"{t.CapacityUnit}/Tag");
                ui.Stat("Auslastung", f.EffectiveCapacity > 0f ? $"{f.Utilization * 100f:0} %" : "—", null,
                    f.Utilization > 1f ? Tone.Warn : Tone.None);
                ui.Stat("Versorgt", BuildLabels.Number(f.ServedBuildings), "Gebäude");
            }
            using (ui.Card())
                ui.Meter("Auslastung", f.EffectiveCapacity > 0f ? $"{f.Utilization * 100f:0} %" : "—", Mathf.Clamp01(f.Utilization),
                    f.Utilization > 1f ? Tone.Warn : f.Utilization > 0.8f ? Tone.Accent : Tone.Good);

            ui.Section("Leistung");
            using (ui.Fields())
            {
                ui.Field("Kapazität", $"{t.Capacity:0} {t.CapacityUnit} je Tag" + (f.IsFull ? " (voll – nimmt nichts mehr an)" : ""),
                    f.IsFull ? Tone.Warn : Tone.None);
                string load = f.EffectiveCapacity > 0f ? $"{f.Utilization * 100f:0} %" : "—";
                string over = f.Utilization > 1f ? $" · überlastet → Wirkung × {f.LoadFactor:0.00}" : "";
                ui.Field("Auslastung", $"{load} · Last {f.Load:0.#} {t.CapacityUnit}{over}", f.Utilization > 1f ? Tone.Accent : Tone.None);
                ui.Field("Versorgt", $"{f.ServedBuildings} Gebäude");
                ui.Field("Reichweite", t.Broadcast ? $"Funkradius {t.Range:0} m (Luftlinie, keine Zufahrt nötig)" : t.Walking
                    ? $"Fußweg {t.Range:0} m (Luftlinie)"
                    : $"{t.Range:0.#} min Fahrzeit übers Straßennetz (voll bis {ServiceSystem.FullEffectShare * 100f:0} %)" +
                      (f.HasAccess ? "" : " · keine Zufahrt"), !t.Walking && !f.HasAccess ? Tone.Warn : Tone.None);
                if (t.Storage > 0f) ui.Field("Lager", $"{f.Stored:0} / {t.Storage:0} t");
                if (f.Kind == ServiceKinds.Garbage) ui.Field("Abgeholt", $"{f.CollectedPerDay:0.#} t/Tag");
            }

            ui.Section("Gebäude");
            using (ui.Fields())
            {
                ui.Field("Größe", $"{t.Width:0}×{t.Depth:0} m · Boden {f.Height:0.0} m");
                ui.Field("Kosten / Unterhalt", $"{BuildLabels.Money(t.Cost)} / {BuildLabels.Money(t.UpkeepPerWeek)} je Woche (Stadtkasse, Kategorie Dienste)");
                if (t.NoiseLevel > 0f) ui.Field("Lärm", $"{t.NoiseLevel * 100f:0} % im Umkreis {t.NoiseRadius:0} m");
                if (t.Pollution > 0f) ui.Field("Abgas", $"{t.Pollution:0.##} im Umkreis {t.PollutionRadius:0} m");
            }
            MoveAction(ui, SelectionKind.Service, f.Id, t.Cost); // UX: Verschieben
            if (serviceOverlay != null)
            {
                string kind = f.Kind;
                using (ui.Actions())
                    ui.ActionButton("Abdeckung anzeigen", () => serviceOverlay.Toggle(kind), ActionStyle.Secondary,
                        serviceOverlay.Selected == kind, true, "Abdeckungs-Ansicht dieser Dienstart (⇧4 schaltet durch)");
            }
        }

        // ------------------------------------------------------------ Stadtübersicht

        void CityBody(Ui ui, SimulationWorld world)
        {
            var s = world.Citizens.Stats;
            var pop = world.City.Population;
            var traffic = world.Traffic.Stats;

            using (ui.Grid(2))
            {
                ui.Stat("Einwohner", BuildLabels.Number(s.Population));
                ui.Stat("Haushalte", BuildLabels.Number(s.Households));
                ui.Stat("Arbeitslos", BuildLabels.Number(s.Unemployed), $"{s.UnemploymentRate * 100f:0} %", s.Unemployed > 0 ? Tone.Warn : Tone.None);
                ui.Stat("Freie Wohnungen", BuildLabels.Number(pop.FreeHomes));
            }
            using (ui.Fields()) ui.Field("Zuzug", $"{world.City.MoveInPull:0} %");

            ui.Section("Arbeit");
            using (ui.Fields())
            {
                ui.Field("Erwerbsfähig", $"{s.WorkingAge} · beschäftigt {s.Employed} (hier {s.WorkingLocal})");
                ui.Field("Arbeitslos", $"{s.Unemployed} ({s.UnemploymentRate * 100f:0} %) · freie Jobs {pop.VacantJobs}", s.Unemployed > 0 ? Tone.Warn : Tone.None);
                ui.Field("Pendler", $"Auspendler {s.CommutersOut} · Einpendler {s.CommutersIn}");
                ui.Field("Außenanschlüsse", s.ExternalConnections.ToString());
            }
            if (s.ExternalConnections == 0)
                ui.Message("Keine Außenanschlüsse", "Straße bis an den Kartenrand bauen, dann kommen Pendler und Durchgangsverkehr.", Tone.Warn);

            ui.Section("Alter");
            int maxAge = Mathf.Max(1, s.AgeGroups.Max());
            for (int i = 0; i < s.AgeGroups.Length; i++)
                ui.ValueBar(CitizenStats.AgeGroupNames[i], s.AgeGroups[i], maxAge, AgeColor, s.AgeGroups[i].ToString());
            ui.Note($"Kinder {s.Children} · Schüler {s.Students} · Erwachsene {s.Adults} · Senioren {s.Seniors}");

            ui.Section("Bildung", "Erwachsene");
            int maxEdu = Mathf.Max(1, s.ByEducation.Max());
            for (int i = 0; i < s.ByEducation.Length; i++)
                ui.ValueBar(EducationName((Education)i), s.ByEducation[i], maxEdu, EduColor, s.ByEducation[i].ToString());

            ui.Section("Gerade");
            using (ui.Fields())
            {
                ui.Field("Zu Hause / Arbeit / Einkauf", $"{s.AtHome} / {s.AtWork} / {s.Shopping}");
                ui.Field("Unterwegs", $"{s.Travelling} (davon Auto {s.TravellingByCar})");
                ui.Field("Bürgerautos im Verkehr", traffic.CitizenVehicles.ToString());
            }

            ui.Section("Bisher");
            using (ui.Fields())
            {
                ui.Field("Autofahrten", $"{s.CarTrips} (angekommen {s.CarArrivals}, abgebrochen {s.CarTripsFailed})");
                ui.Field("Zu Fuß/ÖPNV", s.OtherTrips.ToString());
                ui.Field("Zugezogen / weggezogen", $"{s.MovedIn} / {s.MovedOut}");
                ui.Field("Gestorben", s.Died.ToString());
            }

            if (Tool.HasCursor) DepositsSection(ui, world, Tool.Cursor, "unter dem Mauszeiger");

            ui.Note("Klick auf Gebäude, Fahrzeug, Kraftwerk/Pumpe oder Dienstgebäude zeigt Details; Bewohner und Beschäftigte sind " +
                    "dort anklickbar. F: Fahrzeug/Bürger verfolgen. Rechtsklick: Auswahl aufheben. C oder Esc: Info schließen.");
        }
    }
}
