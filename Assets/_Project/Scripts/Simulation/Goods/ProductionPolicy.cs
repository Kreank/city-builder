using System;
using System.Collections.Generic;
using System.Numerics;
using CityBuilder.Simulation.Citizens;
using CityBuilder.Simulation.Core;
using CityBuilder.Simulation.Zoning;

namespace CityBuilder.Simulation.Goods
{
    /// <summary>Stellschrauben der Lenkung („Produktion lenken“) an einem Ort – Platzhalter fürs Balancing.</summary>
    public static class PolicyRules
    {
        /// <summary>Standortwahl: Kernbetrieb des Schwerpunkts bekommt diesen Zuschlag auf den Score (typisch 0,3 … 2,5).</summary>
        public const float FocusPrimaryBonus = 1.2f;
        /// <summary>Fremder Betrieb im Schwerpunkt-Gebiet: Abschlag (nicht verboten – sehr dringender Bedarf kann ihn schlagen).</summary>
        public const float FocusForeignPenalty = 0.6f;
        /// <summary>Förderstufen 0–3.</summary>
        public const int MaxLevel = 3;
        /// <summary>Standortwahl: Zuschlag je Förderstufe für Betriebe der geförderten Branche (überall in der Stadt).</summary>
        public static readonly float[] SubsidySiteBonus = { 0f, 0.15f, 0.3f, 0.5f };
        /// <summary>Förderung je Beschäftigtem und Tag (€) – wird der Firma gutgeschrieben und von der Stadtkasse bezahlt.</summary>
        public static readonly double[] SubsidyPerWorkerDay = { 0, 5, 12, 25 };
        /// <summary>Betriebswechsel frühestens so viele Tage nach Gründung bzw. dem letzten Wechsel durch die Lenkung.</summary>
        public const int SwitchMinDays = 14;
        /// <summary>Tageschance, dass ein fremder Betrieb im Schwerpunkt-Gebiet umstellt (neuer Schwerpunkt, Betrieb rentabel) …</summary>
        public const double SwitchChance = 0.06;
        /// <summary>… bzw. wenn er Verlust macht (geglätteter Gewinn &lt; 0).</summary>
        public const double SwitchChanceUnprofitable = 0.25;
        /// <summary>Höchstens so viele Wechsel je Tag in der ganzen Stadt (träge, nicht alle sofort).</summary>
        public const int MaxSwitchesPerDay = 3;
        /// <summary>Hinweiszeile: ab dieser Güte heißt ein Vorkommen „gut“ (darunter bis zur Mindestgüte „schwach“).</summary>
        public const float GoodDeposit = 0.35f;
        /// <summary>Empfehlungen: so viele ergiebige Zellen (16 m) ohne Betrieb in der Nähe gelten als „ungenutzt“.</summary>
        public const int MinUnusedCells = 8;
        /// <summary>Empfehlungen: Zelle gilt als genutzt, wenn ein passender Betrieb so nah ist (m).</summary>
        public const float UsedRadius = 200f;
        /// <summary>Empfehlungen: Importanteil, ab dem ein Gut als „wird importiert“ gemeldet wird.</summary>
        public const float ImportShareWarn = 0.6f;

        /// <summary>Ab dieser Güte gilt eine Zelle als ergiebig (Boden ist fast überall mittel – daher höher).</summary>
        public static float RichDeposit(string resource) =>
            resource == ResourceIds.Fertility ? 0.75f : resource == ResourceIds.Wood ? 0.6f : 0.5f;
    }

    /// <summary>Wie gut das Vorkommen eines Rohstoff-Schwerpunkts an einer Stelle ist.</summary>
    public enum DepositRating : byte
    {
        /// <summary>Schwerpunkt ohne Vorkommen (Verarbeitung/Fertigung).</summary>
        NotNeeded,
        /// <summary>Unter der Mindestgüte – dort kann kein Gewinnungsbetrieb entstehen.</summary>
        None,
        Weak,
        Good,
    }

    /// <summary>Kennzahlen einer Branche für Fenster/Inspektor.</summary>
    public struct BranchReport
    {
        public IndustryFocus Branch;
        public int Firms, Workers, Jobs;
        /// <summary>Förderung je Tag bei heutiger Belegschaft (0 ohne Förderung).</summary>
        public double SubsidyPerDay;
        /// <summary>Gestern: Wert der Produktion, Import und Export der Branchen-Güter (lokaler Preis bzw. gebuchte Werte).</summary>
        public double ProducedValue, ImportValue, ExportValue;
        /// <summary>Fläche mit diesem Schwerpunkt (Hektar).</summary>
        public float FocusHectares;
    }

    /// <summary>
    /// „Produktion lenken statt selbst bauen“ (Entscheidung Bauart): Die Simulation wählt die Industrie weiter selbst, der
    /// Spieler lenkt mit zwei Hebeln.
    /// <list type="number">
    /// <item><b>Schwerpunkt-Gebiete</b> (wie Spezialisierung in Cities: Skylines): eigenes Raster (16 m, unabhängig von
    /// Straßen/Zonen → überlebt Umbau), gemalt im Zonen-Werkzeug. Standortwahl (<see cref="SiteAdjust"/>, Hook
    /// <see cref="GoodsSystem.SiteModifier"/>): Kernbetriebe +<see cref="PolicyRules.FocusPrimaryBonus"/>, fremde
    /// −<see cref="PolicyRules.FocusForeignPenalty"/>; die Mindestgüte des Vorkommens bleibt hart. Bestehende fremde Betriebe
    /// stellen täglich mit kleiner Chance um (<see cref="Steer"/>, über <see cref="GoodsSystem.Restructure"/>), Verlustbetriebe
    /// eher; höchstens <see cref="PolicyRules.MaxSwitchesPerDay"/> je Tag.</item>
    /// <item><b>Förderung je Branche</b> (Stufe 0–3): Geld je Beschäftigtem und Tag an die Firma (<see cref="SubsidyFor"/>, Hook
    /// <see cref="GoodsSystem.SubsidyFor"/> → <c>FirmLedger.Subsidy</c>), bezahlt aus der Stadtkasse (Kategorie
    /// „Wirtschaftsförderung“), plus Zuschlag bei der Standortwahl → mehr Betriebe dieser Art. Bei Baustopp (Kasse leer,
    /// <see cref="CanPay"/>) ruht die Förderung.</item>
    /// </list>
    /// Läuft täglich nach dem Warensystem (Tageswechsel ist gebucht), vor der Stadtkasse. Rein deterministisch: Firmen in
    /// Gebäude-Id-Reihenfolge, Zufall nur über <see cref="SimRandom"/>. Ohne Schwerpunkt und Förderung ändert sich nichts.
    /// </summary>
    public sealed partial class ProductionPolicy : ISimulationSystem
    {
        public int TickInterval => SimulationClock.TicksPerDay;

        readonly GoodsSystem goods;
        readonly SimulationClock clock;
        readonly SimRandom rng;
        readonly byte[] map;
        readonly int resolution;
        readonly Vector2 origin;
        readonly float cell;
        readonly int[] cellCounts = new int[IndustryBranches.Count];
        readonly byte[] levels = new byte[IndustryBranches.Count];
        /// <summary>Gebäude-Id → Tag des letzten Betriebswechsels durch die Lenkung (Trägheit).</summary>
        readonly Dictionary<int, int> lastSwitch = new Dictionary<int, int>();

        public ProductionPolicy(GoodsSystem goods, SimulationClock clock, Vector2 origin, float sizeMeters, float cellSize = 16f, int seed = 1)
        {
            this.goods = goods ?? throw new ArgumentNullException(nameof(goods));
            this.clock = clock ?? throw new ArgumentNullException(nameof(clock));
            this.origin = origin;
            cell = cellSize > 0f ? cellSize : 16f;
            resolution = Math.Max(1, (int)MathF.Ceiling(sizeMeters / cell));
            map = new byte[resolution * resolution];
            cellCounts[0] = map.Length;
            rng = new SimRandom(unchecked(seed * 7919 + 4243));
        }

        /// <summary>Kasse zahlungsfähig? false (Baustopp) → Förderung ruht. null = immer.</summary>
        public Func<bool> CanPay { get; set; }
        bool Paying => CanPay == null || CanPay();

        /// <summary>Wird bei jeder Änderung (Malen, Förderstufe, Betriebswechsel) erhöht – für die Darstellung.</summary>
        public int Version { get; private set; }
        /// <summary>Betriebswechsel durch die Lenkung: gestern bzw. seit Spielbeginn.</summary>
        public int SwitchesYesterday { get; private set; }
        public int SwitchesTotal { get; private set; }
        /// <summary>Gestern wollten so viele fremde Betriebe umstellen, fanden aber keinen passenden (meist: kein Vorkommen).</summary>
        public int BlockedYesterday { get; private set; }

        // ------------------------------------------------------------ Raster

        public float CellSize => cell;
        public int Resolution => resolution;
        public Vector2 Origin => origin;
        public Vector2 CellCenter(int x, int y) => origin + new Vector2((x + 0.5f) * cell, (y + 0.5f) * cell);
        public IndustryFocus FocusAtCell(int x, int y) =>
            x < 0 || y < 0 || x >= resolution || y >= resolution ? IndustryFocus.Auto : (IndustryFocus)map[y * resolution + x];

        /// <summary>Schwerpunkt an einer Stelle (außerhalb der Karte: automatisch).</summary>
        public IndustryFocus FocusAt(Vector2 p)
        {
            int x = (int)MathF.Floor((p.X - origin.X) / cell), y = (int)MathF.Floor((p.Y - origin.Y) / cell);
            return FocusAtCell(x, y);
        }

        /// <summary>Fläche mit Schwerpunkt (Hektar).</summary>
        public float Hectares(IndustryFocus f) => cellCounts[(int)f] * cell * cell / 10000f;

        /// <summary>
        /// Malt einen Schwerpunkt (Kreis; mindestens die Zelle unter dem Mittelpunkt). <see cref="IndustryFocus.Auto"/> = entfernen.
        /// Liefert die Zahl geänderter Zellen.
        /// </summary>
        public int Paint(Vector2 center, float radius, IndustryFocus focus)
        {
            if ((int)focus >= IndustryBranches.Count) return 0;
            int cx = (int)MathF.Floor((center.X - origin.X) / cell), cy = (int)MathF.Floor((center.Y - origin.Y) / cell);
            int r = (int)MathF.Ceiling(MathF.Max(0f, radius) / cell);
            int changed = 0;
            for (int y = cy - r; y <= cy + r; y++)
            for (int x = cx - r; x <= cx + r; x++)
            {
                if (x < 0 || y < 0 || x >= resolution || y >= resolution) continue;
                bool inside = (x == cx && y == cy) || Vector2.Distance(CellCenter(x, y), center) <= radius;
                if (!inside) continue;
                int i = y * resolution + x;
                if (map[i] == (byte)focus) continue;
                cellCounts[map[i]]--;
                map[i] = (byte)focus;
                cellCounts[(int)focus]++;
                changed++;
            }
            if (changed > 0) Version++;
            return changed;
        }

        // ------------------------------------------------------------ Förderung

        public int SubsidyLevel(IndustryFocus branch) => (int)branch < levels.Length ? levels[(int)branch] : 0;

        /// <summary>Förderstufe einer Branche setzen (0–3; Auto ist keine Branche).</summary>
        public void SetSubsidy(IndustryFocus branch, int level)
        {
            if (branch == IndustryFocus.Auto || (int)branch >= levels.Length) return;
            byte v = (byte)Math.Clamp(level, 0, PolicyRules.MaxLevel);
            if (levels[(int)branch] == v) return;
            levels[(int)branch] = v;
            Version++;
        }

        public void ChangeSubsidy(IndustryFocus branch, int delta) => SetSubsidy(branch, SubsidyLevel(branch) + delta);

        /// <summary>Ruht die Förderung gerade (Baustopp)?</summary>
        public bool SubsidiesSuspended => !Paying;

        /// <summary>Förderung einer Firma je Tag (Hook <see cref="GoodsSystem.SubsidyFor"/>): Beschäftigte × Satz der Stufe ihrer Branche.</summary>
        public double SubsidyFor(Firm f)
        {
            if (f == null || !f.Recipe.IsIndustry || f.Workers <= 0) return 0;
            int lvl = SubsidyLevel(IndustryBranches.HomeOf(f.Recipe));
            if (lvl <= 0 || !Paying) return 0;
            return f.Workers * PolicyRules.SubsidyPerWorkerDay[lvl];
        }

        /// <summary>Förderung aller Firmen je Tag bei heutiger Belegschaft (Prognose).</summary>
        public double SubsidyPerDay()
        {
            double s = 0;
            foreach (var f in goods.Firms) s += SubsidyFor(f);
            return s;
        }

        // ------------------------------------------------------------ Standortwahl (Hook GoodsSystem.SiteModifier)

        /// <summary>Score eines Rezepts für ein Grundstück nach Schwerpunkt und Förderung anpassen.</summary>
        public float SiteAdjust(Building b, Recipe r, float score)
        {
            if (b == null || r == null || float.IsNegativeInfinity(score)) return score;
            switch (IndustryBranches.Fit(FocusAt(b.Position), r))
            {
                case FocusFit.Primary: score += PolicyRules.FocusPrimaryBonus; break;
                case FocusFit.Foreign: score -= PolicyRules.FocusForeignPenalty; break;
            }
            int lvl = SubsidyLevel(IndustryBranches.HomeOf(r));
            if (lvl > 0 && Paying) score += PolicyRules.SubsidySiteBonus[lvl];
            return score;
        }

        // ------------------------------------------------------------ Täglich: fremde Betriebe stellen träge um

        public void Tick(SimulationWorld world) => Steer();

        readonly List<int> staleKeys = new List<int>();

        /// <summary>
        /// Ein Lenkungstag: fremde Betriebe in Schwerpunkt-Gebieten stellen mit kleiner Chance auf den besten passenden
        /// Betrieb um (Kern oder verwandt); findet die Standortwahl keinen (z. B. kein Vorkommen), bleibt der Betrieb.
        /// Liefert die Zahl der Wechsel. Öffentlich für Tests.
        /// </summary>
        public int Steer()
        {
            int day = clock.Day;
            int switched = 0, blocked = 0;
            var firms = goods.Firms;
            for (int i = 0; i < firms.Count && switched < PolicyRules.MaxSwitchesPerDay; i++)
            {
                var f = firms[i];
                if (!f.Recipe.IsIndustry || !f.Building.IsConstructed) continue;
                var focus = FocusAt(f.Building.Position);
                if (focus == IndustryFocus.Auto || IndustryBranches.Fit(focus, f.Recipe) != FocusFit.Foreign) continue;
                if (f.AgeDays < PolicyRules.SwitchMinDays) continue;
                if (lastSwitch.TryGetValue(f.BuildingId, out int last) && day - last < PolicyRules.SwitchMinDays) continue;
                double p = f.ProfitEma < 0 ? PolicyRules.SwitchChanceUnprofitable : PolicyRules.SwitchChance;
                if (!rng.Chance(p)) continue;
                var r = goods.SuggestIndustry(f.Building, f.Recipe);
                var fit = IndustryBranches.Fit(focus, r);
                if (fit != FocusFit.Primary && fit != FocusFit.Related) { blocked++; continue; }
                if (!goods.Restructure(f, r)) continue;
                lastSwitch[f.BuildingId] = day;
                switched++;
            }
            // abgerissene Gebäude vergessen (sortiert → deterministisch)
            staleKeys.Clear();
            foreach (var k in lastSwitch.Keys) if (goods.FirmAt(k) == null) staleKeys.Add(k);
            staleKeys.Sort();
            foreach (var k in staleKeys) lastSwitch.Remove(k);

            SwitchesYesterday = switched;
            SwitchesTotal += switched;
            BlockedYesterday = blocked;
            if (switched > 0) Version++;
            return switched;
        }

        // ------------------------------------------------------------ Rückmeldung (Hinweiszeile, Fenster, Inspektor)

        /// <summary>Güte des Vorkommens, das ein Schwerpunkt braucht, an einer Stelle (Mittel über das Entnahmefenster).</summary>
        public DepositRating RateDeposit(IndustryFocus f, Vector2 p, out float quality)
        {
            quality = 0f;
            string res = IndustryBranches.Deposit(f);
            if (res == null) return DepositRating.NotNeeded;
            quality = goods.DepositQualityAt(res, p);
            var extraction = IndustryBranches.ExtractionOf(f);
            float min = extraction?.MinDeposit ?? 0f;
            if (quality < min) return DepositRating.None;
            return quality < PolicyRules.GoodDeposit ? DepositRating.Weak : DepositRating.Good;
        }

        /// <summary>Hinweiszeile beim Malen: „Erdöl-Vorkommen hier: gut (62 %)“ bzw. „keins – hier entsteht keine Ölförderung“.</summary>
        public string DepositHint(IndustryFocus f, Vector2 p)
        {
            if (f == IndustryFocus.Auto) return "Automatisch: die Simulation wählt den Betrieb selbst";
            var rating = RateDeposit(f, p, out float q);
            string res = IndustryBranches.Deposit(f);
            switch (rating)
            {
                case DepositRating.NotNeeded:
                    return $"{IndustryBranches.DisplayName(f)}: braucht kein Vorkommen – Rohstoffe kommen aus der Stadt oder per Import";
                case DepositRating.None:
                    return $"{DepositLabel(res)} hier: keins ({q * 100f:0} %, mind. {IndustryBranches.ExtractionOf(f).MinDeposit * 100f:0} %) – " +
                           $"hier kann kein Betrieb „{IndustryBranches.ExtractionOf(f).Name}“ entstehen";
                case DepositRating.Weak:
                    return $"{DepositLabel(res)} hier: schwach ({q * 100f:0} %)";
                default:
                    return $"{DepositLabel(res)} hier: gut ({q * 100f:0} %)";
            }
        }

        /// <summary>Kennzahlen einer Branche (Firmen, Beschäftigte, Förderung, Produktion/Import/Export gestern, Fläche).</summary>
        public BranchReport Report(IndustryFocus branch)
        {
            var rep = new BranchReport { Branch = branch, FocusHectares = branch == IndustryFocus.Auto ? 0f : Hectares(branch) };
            foreach (var f in goods.Firms)
            {
                if (!f.Recipe.IsIndustry || IndustryBranches.HomeOf(f.Recipe) != branch) continue;
                rep.Firms++;
                rep.Workers += f.Workers;
                rep.Jobs += f.Jobs;
                rep.SubsidyPerDay += SubsidyFor(f);
            }
            if (branch != IndustryFocus.Auto)
                foreach (var r in IndustryBranches.Primary(branch))
                    foreach (var o in r.Outputs)
                    {
                        var m = goods.Market(o.Good);
                        rep.ProducedValue += m.Yesterday.Produced * m.LocalPrice;
                        rep.ImportValue += m.Yesterday.ImportValue;
                        rep.ExportValue += m.Yesterday.ExportValue;
                    }
            return rep;
        }

        // --- Empfehlungen aus Daten (berechnet bei Bedarf, zwischengespeichert je Tag/Stand – kein Spielzustand)
        readonly List<string> recommendations = new List<string>();
        int recDay = -1, recVersion = -1, recGoods = -1;

        /// <summary>
        /// Empfehlungen wie „Hohes Ölvorkommen im Osten ungenutzt“, „Stahl wird zu 80 % importiert“, „Förderung Kohle läuft,
        /// aber kein Betrieb“, „Schwerpunkt Öl ohne Vorkommen“. Höchstens 6, wichtigste zuerst.
        /// </summary>
        public IReadOnlyList<string> Recommendations()
        {
            if (recDay == clock.Day && recVersion == Version && recGoods == goods.Version) return recommendations;
            recDay = clock.Day; recVersion = Version; recGoods = goods.Version;
            recommendations.Clear();
            UnusedDeposits();
            ImportShares();
            IdleSubsidies();
            FocusWithoutDeposit();
            if (recommendations.Count > 6) recommendations.RemoveRange(6, recommendations.Count - 6);
            return recommendations;
        }

        void UnusedDeposits()
        {
            if (goods.DepositAt == null) return;
            var found = new List<(int cells, string text)>();
            var mines = new List<Vector2>();
            foreach (var branch in IndustryBranches.Branches)
            {
                string res = IndustryBranches.Deposit(branch);
                var extraction = IndustryBranches.ExtractionOf(branch);
                if (res == null || extraction == null) continue;
                mines.Clear();
                foreach (var f in goods.Firms) if (f.Recipe == extraction) mines.Add(f.Building.Position);
                float rich = PolicyRules.RichDeposit(res);
                float r2 = PolicyRules.UsedRadius * PolicyRules.UsedRadius;
                int count = 0;
                var sum = Vector2.Zero;
                for (int y = 0; y < resolution; y++)
                for (int x = 0; x < resolution; x++)
                {
                    var c = CellCenter(x, y);
                    if (goods.DepositAt(res, c) < rich) continue;
                    bool used = false;
                    foreach (var m in mines) if (Vector2.DistanceSquared(m, c) <= r2) { used = true; break; }
                    if (used) continue;
                    count++;
                    sum += c;
                }
                if (count < PolicyRules.MinUnusedCells) continue;
                var centre = sum / count;
                float ha = count * cell * cell / 10000f;
                found.Add((count, $"{RichPhrase(res)} {Where(centre)} ungenutzt (~{ha:0} ha) – Schwerpunkt „{IndustryBranches.ShortName(branch)}“ dort malen"));
            }
            found.Sort((a, b) => b.cells.CompareTo(a.cells));
            for (int i = 0; i < found.Count && i < 2; i++) recommendations.Add(found[i].text);
        }

        void ImportShares()
        {
            var list = new List<(double value, string text)>();
            foreach (var m in goods.Markets)
            {
                if (m.Good.IsConsumerGood) continue; // Endprodukte: Läden zeigen das schon; hier Rohstoffe/Zwischenprodukte
                var y = m.Yesterday;
                float total = y.Imported + y.Produced;
                if (y.Imported < 5f || total <= 0f) continue;
                float share = y.Imported / total;
                if (share < PolicyRules.ImportShareWarn) continue;
                var branch = BranchOf(m.Good);
                string hint = branch == IndustryFocus.Auto ? "" : $" – Schwerpunkt oder Förderung „{IndustryBranches.ShortName(branch)}“";
                list.Add((y.ImportValue, $"{m.Good.Name} wird zu {share * 100f:0} % importiert{hint}"));
            }
            list.Sort((a, b) => b.value.CompareTo(a.value));
            for (int i = 0; i < list.Count && i < 3; i++) recommendations.Add(list[i].text);
        }

        void IdleSubsidies()
        {
            foreach (var branch in IndustryBranches.Branches)
            {
                if (SubsidyLevel(branch) <= 0) continue;
                bool any = false;
                foreach (var f in goods.Firms) if (f.Recipe.IsIndustry && IndustryBranches.HomeOf(f.Recipe) == branch) { any = true; break; }
                if (any) continue;
                string res = IndustryBranches.Deposit(branch);
                recommendations.Add($"Förderung „{IndustryBranches.ShortName(branch)}“ läuft, aber es gibt noch keinen Betrieb – " +
                                    (res != null ? $"Schwerpunkt über {ResourceIds.DisplayName(res)}-Vorkommen malen" : "Schwerpunkt-Gebiet auf Industrie malen"));
            }
        }

        void FocusWithoutDeposit()
        {
            if (goods.DepositAt == null) return;
            foreach (var branch in IndustryBranches.Branches)
            {
                string res = IndustryBranches.Deposit(branch);
                if (res == null || cellCounts[(int)branch] == 0) continue;
                float min = IndustryBranches.ExtractionOf(branch).MinDeposit;
                bool any = false;
                for (int i = 0; i < map.Length && !any; i++)
                    if (map[i] == (byte)branch && goods.DepositAt(res, CellCenter(i % resolution, i / resolution)) >= min) any = true;
                if (!any)
                    recommendations.Add($"Schwerpunkt „{IndustryBranches.ShortName(branch)}“: im Gebiet kein {ResourceIds.DisplayName(res)}-Vorkommen " +
                                        $"über {min * 100f:0} % – dort kann kein Betrieb „{IndustryBranches.ExtractionOf(branch).Name}“ entstehen (Rohstoff-Ansicht F3)");
            }
        }

        /// <summary>Branche, deren Kernbetrieb ein Gut herstellt (Auto, wenn keiner).</summary>
        public static IndustryFocus BranchOf(GoodType g)
        {
            foreach (var r in RecipeCatalog.Industry) if (r.Produces(g)) return IndustryBranches.HomeOf(r);
            return IndustryFocus.Auto;
        }

        /// <summary>„Erdöl-Vorkommen“, „Fruchtbarer Boden“, „Wald“ – für die Hinweiszeile.</summary>
        public static string DepositLabel(string res) =>
            res == ResourceIds.Fertility ? "Fruchtbarer Boden" : res == ResourceIds.Wood ? "Wald" : ResourceIds.DisplayName(res) + "-Vorkommen";

        static string RichPhrase(string res)
        {
            switch (res)
            {
                case ResourceIds.Fertility: return "Sehr fruchtbarer Boden";
                case ResourceIds.Wood: return "Dichter Wald";
                case ResourceIds.Ore: return "Reiches Erzvorkommen";
                case ResourceIds.Coal: return "Reiches Kohlevorkommen";
                case ResourceIds.Oil: return "Hohes Ölvorkommen";
                case ResourceIds.Sand: return "Großes Sand-/Kiesvorkommen";
                default: return "Vorkommen";
            }
        }

        /// <summary>„im Osten“ / „in der Kartenmitte“ relativ zur Kartenmitte (x = Ost, y = Nord).</summary>
        string Where(Vector2 p)
        {
            var mid = origin + new Vector2(resolution * cell * 0.5f);
            var d = p - mid;
            if (d.Length() < resolution * cell * 0.12f) return "in der Kartenmitte";
            float deg = MathF.Atan2(d.Y, d.X) * 180f / MathF.PI;
            string[] names = { "im Osten", "im Nordosten", "im Norden", "im Nordwesten", "im Westen", "im Südwesten", "im Süden", "im Südosten" };
            return names[(int)MathF.Round(((deg % 360f) + 360f) % 360f / 45f) % 8];
        }
    }
}
