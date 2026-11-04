using System;
using System.Collections.Generic;
using System.Numerics;
using CityBuilder.Simulation.Core;
using CityBuilder.Simulation.World;

namespace CityBuilder.Simulation.Utilities
{
    /// <summary>Wasserkraft-Standort: Querschnitt des Flusses, Abfluss, Fallhöhe und daraus die Nennleistung.</summary>
    public struct HydroSite
    {
        public bool Valid;
        /// <summary>Wasserbreite quer zum Wehr (m) und mittlere Wassertiefe (m).</summary>
        public float Width, MeanDepth;
        /// <summary>Mittlerer Abfluss Q (m³/s) = Breite × Tiefe × Fließgeschwindigkeit × Klimafaktor der Region.</summary>
        public float Flow;
        /// <summary>Nutzbare Fallhöhe H (m): niedrigeres Ufer − Wasserspiegel − Freibord (Aufstau darf nicht übers Ufer).</summary>
        public float Head;
        /// <summary>Nennleistung (kW) = ρ·g·min(Q, Ausbaudurchfluss)·H·η.</summary>
        public float Rated;
        /// <summary>Höhe des niedrigeren Ufers über dem Wasserspiegel (m) – Erklärung im Werkzeug.</summary>
        public float Bank;
    }

    /// <summary>
    /// <b>Energie erweitert</b> (Eigene Assets 2026-11-03, Modelle <c>Models/Energy</c>): Standortregeln für Wasserkraft (Fluss) und
    /// Offshore-Wind (Meer), schwankende Erzeugung (Abfluss nach Niederschlag, Wind stündlich, Biogas nach Rohstoff) und
    /// <b>Batteriespeicher</b> im Netzmodell.
    /// <para>
    /// <b>Wasserkraft – Entscheidung:</b> Laufwasserkraftwerk mit Wehr statt Talsperre. Der erzeugte Fluss hat kein Gefälle
    /// (Bett überall −4 m), eine Talsperre müsste das Tal fluten (Terraforming, Gebäude, Straßen) und den Fluss unterbrechen.
    /// Das Wehr staut nur so hoch, wie das niedrigere Ufer erlaubt (Fallhöhe aus dem Gelände am Standort), das Gelände bleibt
    /// unverändert → <b>der Fluss bleibt durchgängig</b>: Wasserzellen bleiben Wasser, Verschmutzung wandert weiter flussabwärts
    /// (<see cref="WaterQualitySystem"/>), Wasser-Gateways bleiben schiffbar (Schleuse/Fischtreppe gehören zum Bauwerk). Leistung
    /// P = ρ·g·Q·H·η (Q aus dem Querschnitt, gedeckelt auf den Ausbaudurchfluss), Abfluss schwankt mit der Niederschlagslage
    /// (gleitendes Mittel des Klimas) – bei großen Flüssen liegt er meist über dem Ausbaudurchfluss (fast immer volle Leistung).
    /// </para>
    /// <para>
    /// <b>Speicher im Netz:</b> Hochspannungs-Speicher bieten vor der Umspannung an (zählen für die Lieferquote), Ortsnetz-Speicher
    /// nach dem Bezug aus der Hochspannung – Speicher liefern also erst, wenn die Kraftwerke nicht reichen. Geladen wird aus dem
    /// Überschuss: zuerst im eigenen Netz, Ortsnetz-Speicher zusätzlich über freie Umspannkapazität aus dem Überschuss der
    /// Hochspannung. Wirkungsgrad je Richtung <see cref="StorageEfficiency"/>; ein Durchlauf = <see cref="StepHours"/> Stunden.
    /// </para>
    /// Alles in Id-Reihenfolge, Zufall nur über stabile Hashes → deterministisch.
    /// </summary>
    public sealed partial class UtilitySystem
    {
        // ------------------------------------------------------------ Regeln (alle Zahlen an einem Ort)

        /// <summary>Biogas: Einzugsgebiet für Rohstoff (Mais, Gülle) von Bauernhöfen (m Luftlinie).</summary>
        public const float BiogasFeedRadius = 3000f;
        /// <summary>Biogas: so viele Beschäftigte in Bauernhöfen liefern genug Rohstoff für Volllast (≈ 400 ha Mais + Gülle für 2 MW).</summary>
        public const float BiogasFullFeedWorkers = 40f;
        /// <summary>Biogas: Grundlast aus Bioabfall/Reststoffen der Stadt ohne Bauernhof (Anteil der Nennleistung).</summary>
        public const float BiogasBaseShare = 0.25f;
        /// <summary>Wasserkraft: mittlere Fließgeschwindigkeit im Querschnitt (m/s).</summary>
        public const float RiverFlowVelocity = 0.9f;
        /// <summary>Wasserkraft: Ausbaudurchfluss der Turbinen (m³/s) – mehr Abfluss läuft übers Wehr.</summary>
        public const float HydroDesignFlow = 150f;
        public const float HydroEfficiency = 0.88f;
        /// <summary>Fallhöhe: mindestens / höchstens (m); Freibord zwischen Stauziel und Uferkante (m).</summary>
        public const float HydroMinHead = 2f, HydroMaxHead = 15f, HydroFreeboard = 1f;
        /// <summary>Flussbreite am Wehr (m).</summary>
        public const float HydroMinRiverWidth = 12f, HydroMaxRiverWidth = 450f;
        /// <summary>So weit hinter der Uferlinie wird die Uferhöhe gesucht (m).</summary>
        public const float HydroBankProbe = 60f;
        /// <summary>Meer: natürliche Geländehöhe höchstens (Flussbett liegt bei −4 m, Meeresgrund bei −7 m).</summary>
        public const float SeaMaxNaturalHeight = -5f;
        /// <summary>Offshore: Mindest-Wassertiefe an jeder Ecke (Gründung) in m.</summary>
        public const float SeaMinDepth = 3f;
        /// <summary>Offenes Meer: Anteil Wasser im Umkreis (Flussmündung/Teich zählt nicht).</summary>
        public const float SeaOpenWaterRadius = 150f, SeaOpenWaterShare = 0.75f;
        /// <summary>Batteriespeicher: Wirkungsgrad je Richtung (Laden und Entladen, zusammen ≈ 81 %).</summary>
        public const float StorageEfficiency = 0.9f;

        /// <summary>Spielstunden je Durchlauf (Speicher-Energie, 5 Ticks = 30 Spielminuten).</summary>
        public float StepHours => TickInterval / (float)TimeModel.TicksPerHour;

        /// <summary>Klima der Stadt (Niederschlag → Abfluss). Gesetzt in <c>CreateDefault</c>; null = mittlere Lage.</summary>
        public ClimateModel Climate { get; set; }

        /// <summary>
        /// Biogas (Hook, gesetzt in <c>CreateDefault</c> aus dem Warensystem): Bauernhöfe mit Lage und Beschäftigten.
        /// null = Rohstoff unbegrenzt (Tests ohne Warenketten).
        /// </summary>
        public Func<IEnumerable<(Vector2 Position, float Workers)>> FarmSources { get; set; }

        float riverFlowIndex = 1f;
        int flowIndexDay = -1;
        long energyTick;

        /// <summary>Gleitendes Mittel des Niederschlags (1 = normal) – Abflusslage der Flüsse.</summary>
        public float RiverFlowIndex => riverFlowIndex;
        /// <summary>Abfluss heute relativ zum Mittel (0,45 … 1,5).</summary>
        public float RiverFlowFactor => Math.Clamp(0.55f + 0.45f * riverFlowIndex, 0.45f, 1.5f);
        /// <summary>Regionale Wasserführung aus dem Jahresniederschlag (750 mm = 1).</summary>
        public float ClimateFlowFactor => Climate == null ? 1f : Math.Clamp(MathF.Sqrt(Climate.AnnualPrecipitation / 750f), 0.5f, 1.5f);

        // ------------------------------------------------------------ Gewässer

        /// <summary>Anteil Wasser im Umkreis (Raster 25 m).</summary>
        public float OpenWaterShare(Vector2 p, float radius)
        {
            int water = 0, all = 0;
            const float step = 25f;
            for (float y = -radius; y <= radius; y += step)
            for (float x = -radius; x <= radius; x += step)
            {
                if (x * x + y * y > radius * radius) continue;
                all++;
                if (terrain.IsWater(p + new Vector2(x, y))) water++;
            }
            return all > 0 ? water / (float)all : 0f;
        }

        /// <summary>Offenes Meer: Wasser, natürlicher Grund tiefer als ein Flussbett, ringsum fast nur Wasser.</summary>
        public bool IsSea(Vector2 p) =>
            terrain.IsWater(p) && terrain.NaturalHeightAt(p) <= SeaMaxNaturalHeight && OpenWaterShare(p, SeaOpenWaterRadius) >= SeaOpenWaterShare;

        /// <summary>Fließgewässer (Wasser, aber kein offenes Meer).</summary>
        public bool IsRiver(Vector2 p) => terrain.IsWater(p) && !IsSea(p);

        /// <summary>Wasserstrecke durch p entlang dir: Abstände bis zum Ufer in beide Richtungen (2-m-Schritte, max. 400 m).</summary>
        void WaterRun(Vector2 p, Vector2 dir, out float forward, out float backward)
        {
            const float step = 2f, max = 400f;
            forward = 0f;
            while (forward < max && terrain.IsWater(p + dir * (forward + step))) forward += step;
            backward = 0f;
            while (backward < max && terrain.IsWater(p - dir * (backward + step))) backward += step;
        }

        /// <summary>
        /// Wasserkraft am Punkt mit Wehr quer zu <paramref name="front"/> (Wehrachse = Seitenrichtung): Querschnitt, Abfluss,
        /// Fallhöhe, Nennleistung. Ungültig bei zu schmalem/zu breitem Fluss oder zu niedrigem Ufer.
        /// </summary>
        public HydroSite EvaluateHydroSite(Vector2 p, Vector2 front)
        {
            var site = new HydroSite();
            if (!terrain.IsWater(p)) return site;
            front = front.LengthSquared() > 1e-8f ? Vector2.Normalize(front) : Vector2.UnitY;
            var side = new Vector2(-front.Y, front.X);
            WaterRun(p, side, out float a, out float b);
            site.Width = a + b + 2f;
            // mittlere Tiefe über den Querschnitt
            float depthSum = 0f;
            int n = 0;
            for (float s = -b; s <= a; s += 2f)
            {
                float d = terrain.WaterLevel - terrain.HeightAt(p + side * s);
                if (d > 0f) { depthSum += d; n++; }
            }
            site.MeanDepth = n > 0 ? depthSum / n : 0f;
            // Uferhöhen: höchster Punkt bis HydroBankProbe hinter der Uferlinie, das niedrigere Ufer begrenzt den Aufstau
            float BankHeight(Vector2 dir, float start)
            {
                float best = terrain.WaterLevel;
                for (float s = start; s <= start + HydroBankProbe; s += 4f) best = MathF.Max(best, terrain.HeightAt(p + dir * s));
                return best - terrain.WaterLevel;
            }
            site.Bank = MathF.Min(BankHeight(side, a + 2f), BankHeight(-side, b + 2f));
            site.Head = MathF.Min(HydroMaxHead, site.Bank - HydroFreeboard);
            site.Flow = site.Width * site.MeanDepth * RiverFlowVelocity * ClimateFlowFactor;
            site.Valid = site.Width >= HydroMinRiverWidth && site.Width <= HydroMaxRiverWidth && site.Head >= HydroMinHead;
            site.Rated = site.Valid ? HydroPower(site.Flow, site.Head) : 0f;
            return site;
        }

        /// <summary>P = ρ·g·Q·H·η in kW (ρ = 1000 kg/m³, g = 9,81 m/s²), Q höchstens <see cref="HydroDesignFlow"/>.</summary>
        public static float HydroPower(float flow, float head) => 9.81f * MathF.Min(flow, HydroDesignFlow) * MathF.Max(0f, head) * HydroEfficiency;

        /// <summary>
        /// Wehr ausrichten: schmalster Wasserquerschnitt durch den Cursor (15°-Schritte) = Wehrachse, Mitte des Querschnitts,
        /// Vorderseite flussabwärts (<see cref="TerrainMap.FlowDirection"/>); <paramref name="rotationDegrees"/> dreht zusätzlich.
        /// </summary>
        public bool TryAlignHydro(Vector2 cursor, float rotationDegrees, out Vector2 center, out Vector2 front)
        {
            center = cursor;
            front = Vector2.UnitY;
            if (!terrain.IsWater(cursor)) return false;
            float best = float.MaxValue;
            Vector2 bestDir = Vector2.UnitX;
            float bestA = 0f, bestB = 0f;
            for (int k = 0; k < 12; k++)
            {
                float ang = k * MathF.PI / 12f;
                var dir = new Vector2(MathF.Cos(ang), MathF.Sin(ang));
                WaterRun(cursor, dir, out float a, out float b);
                if (a + b < best - 0.01f) { best = a + b; bestDir = dir; bestA = a; bestB = b; }
            }
            center = cursor + bestDir * ((bestA - bestB) * 0.5f);
            front = new Vector2(bestDir.Y, -bestDir.X);
            if (Vector2.Dot(front, terrain.FlowDirection) < 0f) front = -front;
            if (MathF.Abs(rotationDegrees) > 0.01f)
            {
                float r = rotationDegrees * MathF.PI / 180f, c = MathF.Cos(r), s = MathF.Sin(r);
                front = new Vector2(front.X * c - front.Y * s, front.X * s + front.Y * c);
            }
            return true;
        }

        /// <summary>Standortregeln für Fluss/Meer (Teil von CheckFacility). Setzt bei Wasserkraft die Nennleistung des Plans.</summary>
        UtilityBuildError CheckWaterSite(FacilityPlan plan, Vector2[] corners)
        {
            var type = plan.Type;
            if (type.Site == FacilitySite.River)
            {
                if (!IsRiver(plan.Position)) return UtilityBuildError.NeedsRiver;
                var site = EvaluateHydroSite(plan.Position, plan.Front);
                plan.Hydro = site;
                if (!site.Valid) return UtilityBuildError.NoHydroSite;
                plan.Rated = site.Rated;
                return UtilityBuildError.None;
            }
            // Meer: Mitte offenes Meer, alle Ecken im Wasser mit Gründungstiefe
            if (!IsSea(plan.Position)) return UtilityBuildError.NeedsSea;
            foreach (var c in corners)
                if (!terrain.IsWater(c) || terrain.WaterLevel - terrain.HeightAt(c) < SeaMinDepth) return UtilityBuildError.NeedsSea;
            return UtilityBuildError.None;
        }

        /// <summary>Standortwerte vom Plan ins Bauwerk (Neubau, Verschieben).</summary>
        static void ApplySite(UtilityFacility f, FacilityPlan plan)
        {
            f.Rated = plan.Type.IsStorage ? plan.Type.Capacity : plan.Rated;
            f.SiteFlow = plan.Hydro.Flow;
            f.SiteHead = plan.Hydro.Head;
        }

        // ------------------------------------------------------------ schwankende Erzeugung

        /// <summary>Verfügbarkeit je Bauwerk (0..1 der Nennleistung): Abfluss, Wind, Rohstoff. Konstante Kraftwerke = 1.</summary>
        void UpdateAvailability(SimulationWorld world)
        {
            if (world != null)
            {
                energyTick = world.Clock.Tick;
                int day = world.Clock.Day;
                if (day != flowIndexDay)
                {
                    flowIndexDay = day;
                    if (Climate != null)
                    {
                        float mean = MathF.Max(0.01f, Climate.MeanDailyPrecipitation);
                        float ratio = Math.Clamp(Climate.PrecipitationOn(day) / mean, 0f, 4f);
                        riverFlowIndex += 0.08f * (ratio - riverFlowIndex);
                    }
                }
            }
            List<UtilityFacility> biogas = null;
            foreach (var f in facilities.Values)
            {
                switch (f.Type.Output)
                {
                    case OutputModel.Hydro:
                    {
                        float design = MathF.Min(f.SiteFlow, HydroDesignFlow);
                        float now = MathF.Min(f.SiteFlow * RiverFlowFactor, HydroDesignFlow);
                        f.Availability = design > 1e-3f ? MathF.Min(1f, now / design) : 0f;
                        break;
                    }
                    case OutputModel.OffshoreWind: f.Availability = WindFactor(energyTick); break;
                    case OutputModel.Biogas: (biogas ?? (biogas = new List<UtilityFacility>())).Add(f); break;
                    default: f.Availability = 1f; break;
                }
            }
            if (biogas != null) AllocateFeedstock(biogas);
        }

        /// <summary>
        /// Offshore-Windangebot 0,05 … 1 (Anteil der Nennleistung) zum Tick: zwei Lagen geglätteten Hash-Rauschens (6 h und 1,5 h),
        /// im Mittel ≈ 0,5 (echte Offshore-Parks: Volllast-Anteil 40–50 %). Gleich für alle Parks der Karte (ein Wetter).
        /// </summary>
        public static float WindFactor(long tick)
        {
            double hours = tick / (double)TimeModel.TicksPerHour;
            float w = 0.7f * SmoothNoise(hours / 6.0, 11u) + 0.3f * SmoothNoise(hours / 1.5, 23u);
            return Math.Clamp((w - 0.12f) / 0.76f, 0.05f, 1f);
        }

        static float SmoothNoise(double x, uint salt)
        {
            double fl = Math.Floor(x);
            long i = (long)fl;
            float t = (float)(x - fl);
            t = t * t * (3f - 2f * t);
            return Hash01(i, salt) + (Hash01(i + 1, salt) - Hash01(i, salt)) * t;
        }

        static float Hash01(long i, uint salt)
        {
            unchecked
            {
                uint h = (uint)i * 0x9E3779B1u ^ (uint)(i >> 32) * 0x85EBCA77u ^ salt * 0xC2B2AE3Du;
                h ^= h >> 15; h *= 0x2C1B3C6Du;
                h ^= h >> 12; h *= 0x297A2D39u;
                h ^= h >> 15;
                return (h & 0xFFFFFF) / (float)0xFFFFFF;
            }
        }

        /// <summary>
        /// Biogas: Bauernhöfe im Umkreis teilen ihren Rohstoff auf (Anlagen nach Id, nächste Höfe zuerst – ein Hof beliefert nicht
        /// zwei Anlagen doppelt). Verfügbarkeit = Grundlast + Rest × gelieferter Anteil.
        /// </summary>
        void AllocateFeedstock(List<UtilityFacility> plants)
        {
            plants.Sort((a, b) => a.Id.CompareTo(b.Id));
            if (FarmSources == null)
            {
                foreach (var f in plants) { f.Availability = 1f; f.FeedWorkers = BiogasFullFeedWorkers; }
                return;
            }
            var farms = new List<(Vector2 Position, float Workers)>(FarmSources());
            var left = new float[farms.Count];
            for (int i = 0; i < farms.Count; i++) left[i] = MathF.Max(0f, farms[i].Workers);
            var near = new List<(float d, int i)>();
            foreach (var f in plants)
            {
                near.Clear();
                for (int i = 0; i < farms.Count; i++)
                {
                    float d = Vector2.Distance(farms[i].Position, f.Position);
                    if (d <= BiogasFeedRadius && left[i] > 0f) near.Add((d, i));
                }
                near.Sort((x, y) => x.d != y.d ? x.d.CompareTo(y.d) : x.i.CompareTo(y.i));
                float need = BiogasFullFeedWorkers, got = 0f;
                foreach (var (_, i) in near)
                {
                    if (need <= 0f) break;
                    float take = MathF.Min(need, left[i]);
                    left[i] -= take;
                    need -= take;
                    got += take;
                }
                f.FeedWorkers = got;
                f.Availability = BiogasBaseShare + (1f - BiogasBaseShare) * Math.Clamp(got / BiogasFullFeedWorkers, 0f, 1f);
            }
        }

        // ------------------------------------------------------------ Batteriespeicher

        readonly Dictionary<int, float> storageOffer = new Dictionary<int, float>();
        readonly List<UtilityFacility> storageScratch = new List<UtilityFacility>();

        /// <summary>
        /// Speicher bieten ihre Entladeleistung an (begrenzt durch Ladestand): <paramref name="beforeTransform"/> = Hochspannungs-
        /// Speicher (in die Erzeugung, vor der Umspannung), sonst Ortsnetz-Speicher (in die verfügbare Leistung, nach dem Bezug).
        /// Liefert true, wenn es Speicher gibt.
        /// </summary>
        bool OfferStorage(List<UtilityNetwork> list, bool beforeTransform)
        {
            if (beforeTransform)
            {
                storageOffer.Clear();
                storageScratch.Clear();
                foreach (var f in facilities.Values) if (f.IsStorage && !f.MissingRoad) storageScratch.Add(f); // Frei setzen: ohne Zufahrt außer Betrieb
                if (storageScratch.Count == 0) return false;
                storageScratch.Sort((a, b) => a.Id.CompareTo(b.Id));
            }
            float hours = StepHours;
            foreach (var f in storageScratch)
            {
                if (f.NetworkId < 0 || f.NetworkId >= list.Count) continue;
                var n = list[f.NetworkId];
                bool hv = n.Level == GridLevel.HighVoltage;
                if (hv != beforeTransform) continue;
                float offer = MathF.Max(0f, MathF.Min(f.Rated, f.StoredKwh * StorageEfficiency / hours));
                storageOffer[f.Id] = offer;
                if (hv) n.Generation += offer;
                else n.Production += offer;
            }
            return true;
        }

        /// <summary>Nach der Verteilung: tatsächliche Entladung buchen, Überschuss in die Speicher laden (s. Klassenbeschreibung).</summary>
        void SettleStorage(List<UtilityNetwork> list)
        {
            float hours = StepHours;
            int count = list.Count;
            var offer = new float[count];
            var wish = new float[count];
            var leftover = new float[count];
            var charged = new float[count];
            foreach (var f in storageScratch)
            {
                f.Output = 0f;
                f.Charging = 0f;
                if (f.NetworkId < 0 || f.NetworkId >= count) continue;
                storageOffer.TryGetValue(f.Id, out float o);
                offer[f.NetworkId] += o;
            }

            // 1) Entladen: Speicher liefern nur, was die übrige Erzeugung nicht gedeckt hat
            var used = new float[count];
            for (int id = 0; id < count; id++)
            {
                var n = list[id];
                if (n.Level == GridLevel.HighVoltage)
                {
                    float nb = n.Generation - offer[id];
                    used[id] = Math.Clamp(n.Delivered - nb, 0f, offer[id]);
                    leftover[id] = MathF.Max(0f, nb - n.Delivered);
                    n.Generation = nb + used[id];
                }
                else
                {
                    float nb = n.Generation + n.Imported;
                    used[id] = Math.Clamp(n.Delivered - nb, 0f, offer[id]);
                    leftover[id] = MathF.Max(0f, nb - n.Delivered);
                    n.Generation += used[id];
                    n.Production = n.Generation + n.Imported;
                }
                n.StorageDischarge = used[id];
            }
            foreach (var f in storageScratch)
            {
                if (f.NetworkId < 0 || f.NetworkId >= count) continue;
                storageOffer.TryGetValue(f.Id, out float o);
                float total = offer[f.NetworkId];
                float d = total > 1e-6f ? used[f.NetworkId] * o / total : 0f;
                if (d <= 0f) continue;
                f.Output = d;
                f.StoredKwh = MathF.Max(0f, f.StoredKwh - d * hours / StorageEfficiency);
            }

            // 2) Ladewunsch je Netz (Speicher, die gerade entladen, laden nicht)
            foreach (var f in storageScratch)
            {
                if (f.NetworkId < 0 || f.NetworkId >= count || f.Output > 0f) continue;
                wish[f.NetworkId] += ChargeWish(f, hours);
            }

            // 3) Hochspannungs-Speicher aus dem eigenen Überschuss
            for (int id = 0; id < count; id++)
            {
                if (list[id].Level != GridLevel.HighVoltage || wish[id] <= 0f) continue;
                charged[id] = MathF.Min(wish[id], leftover[id]);
                leftover[id] -= charged[id];
            }
            // 4) Ortsnetz-Speicher: eigener Überschuss, dann über freie Umspannkapazität aus dem Überschuss der Hochspannung
            for (int id = 0; id < count; id++)
            {
                if (list[id].Level != GridLevel.Distribution || wish[id] <= 0f) continue;
                charged[id] = MathF.Min(wish[id], leftover[id]);
            }
            foreach (var s in Substations)
            {
                int lo = s.NetworkId, up = s.UpstreamNetworkId;
                if (lo < 0 || lo >= count || up < 0 || up >= count) continue;
                float missing = wish[lo] - charged[lo];
                if (missing <= 1e-4f || leftover[up] <= 1e-4f) continue;
                float amount = MathF.Min(missing, MathF.Min(MathF.Max(0f, s.Type.Capacity - s.Load), leftover[up]));
                if (amount <= 0f) continue;
                s.Load += amount;
                leftover[up] -= amount;
                charged[lo] += amount;
                list[up].Delivered += amount;
                list[up].Imported += amount;
            }
            // 5) Ladung auf die Speicher verteilen (nach Wunsch)
            foreach (var f in storageScratch)
            {
                if (f.NetworkId < 0 || f.NetworkId >= count || f.Output > 0f) continue;
                int id = f.NetworkId;
                if (wish[id] <= 1e-6f || charged[id] <= 0f) continue;
                float c = charged[id] * ChargeWish(f, hours) / wish[id];
                f.Charging = c;
                f.StoredKwh = MathF.Min(f.Type.StorageKwh, f.StoredKwh + c * hours * StorageEfficiency);
            }
            for (int id = 0; id < count; id++) list[id].StorageCharge = charged[id];
        }

        static float ChargeWish(UtilityFacility f, float hours) =>
            MathF.Max(0f, MathF.Min(f.Rated, (f.Type.StorageKwh - f.StoredKwh) / (StorageEfficiency * hours)));

        /// <summary>Für Tests/Szenarien: Abflusslage setzen (1 = normal).</summary>
        internal void SetRiverFlowIndex(float index) => riverFlowIndex = Math.Max(0f, index);

        /// <summary>Für Tests/Szenarien: Ladestand eines Speichers setzen (kWh, begrenzt).</summary>
        public void SetStoredEnergy(int facilityId, float kwh)
        {
            if (facilities.TryGetValue(facilityId, out var f) && f.IsStorage) f.StoredKwh = Math.Clamp(kwh, 0f, f.Type.StorageKwh);
        }

        /// <summary>Gespeicherte Energie aller Speicher (kWh).</summary>
        public float TotalStoredKwh { get { float s = 0f; foreach (var f in facilities.Values) s += f.StoredKwh; return s; } }
    }
}
