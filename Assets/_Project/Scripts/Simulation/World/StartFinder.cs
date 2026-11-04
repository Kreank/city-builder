using System;
using System.Collections.Generic;
using System.Text;

namespace CityBuilder.Simulation.World
{
    /// <summary>
    /// Gemessene Eigenschaften einer Region für die Startsuche (aus den echten Planet-Daten, einmal je Region berechnet).
    /// Alle Kennwerte 0..1, sofern nicht anders angegeben.
    /// </summary>
    public sealed class RegionFacts
    {
        public Region Region { get; internal set; }
        public ClimateZone Climate { get; internal set; }
        /// <summary>Relief-Index: max(Zerklüftung/150 m, mittlere Höhe/600 m) – &lt; 1 flach, 1–3 hügelig, ≥ 3 gebirgig.</summary>
        public float Relief { get; internal set; }
        /// <summary>Anteil der Küstenzellen.</summary>
        public float CoastShare { get; internal set; }
        public float CoastScore { get; internal set; }
        /// <summary>Größter Abfluss / Flussschwelle (≥ 1 = Fluss).</summary>
        public float RiverSize { get; internal set; }
        public float RiverScore { get; internal set; }
        public float Agriculture { get; internal set; }
        public float Tourism { get; internal set; }
        /// <summary>Lebensfreundlichkeit (mild, fruchtbar, Wasser) – Grundlage der Schwierigkeit.</summary>
        public float Habitability { get; internal set; }
        /// <summary>Entfernung von der Regionsmitte zum nächsten NPC-Land (km, über Land und See).</summary>
        public float NeighborKm { get; internal set; }
        /// <summary>Nächstes NPC-Land (−1 = keins).</summary>
        public int NearestCountryId { get; internal set; } = -1;

        public string TerrainWord => Relief >= 3f ? "gebirgig" : Relief >= 1f ? "hügelig" : "flach";
    }

    /// <summary>Zustand einer Option bei der aktuellen Auswahl.</summary>
    public enum OptionAvailability { Available, Rare, Unavailable }

    public sealed class StartOptionState
    {
        public StartOption Option { get; internal set; }
        public bool Selected { get; internal set; }
        /// <summary>Freie Regionen, die mit der Auswahl + dieser Option (bzw. bei gewählter Option: mit der Auswahl) passen.</summary>
        public int Matches { get; internal set; }
        public OptionAvailability Availability { get; internal set; }
        /// <summary>Tooltip: Beschreibung + bei gesperrt/selten der Grund und die Geologie.</summary>
        public string Tip { get; internal set; }
    }

    /// <summary>Ein Wert für die Anzeige eines Vorschlags (Balken).</summary>
    public readonly struct StartValue
    {
        public readonly string Label;
        public readonly float Value;
        public readonly string Text;
        public StartValue(string label, float value, string text) { Label = label; Value = value; Text = text; }
    }

    /// <summary>Vorgeschlagene Startregion mit Bewertung und Begründung.</summary>
    public sealed class StartSuggestion
    {
        public RegionFacts Facts { get; internal set; }
        public Region Region => Facts.Region;
        public float Score { get; internal set; }
        /// <summary>„gemäßigt, Fluss, sehr fruchtbar, Erz in den Hügeln im Norden, nächstes Land 600 km“.</summary>
        public string Reason { get; internal set; }
        public List<StartValue> Values { get; } = new List<StartValue>();
    }

    /// <summary>
    /// Startregion nach Vorlieben (Spielstart &amp; Startregion, rein und testbar): bewertet jede <b>freie</b> Region nach den
    /// Kriterien aus <see cref="StartCriteria"/> (Score 0..1 je Kriterium, Mindestwert). <b>Gegenseitiger Ausschluss ergibt
    /// sich aus der Welt:</b> <see cref="Evaluate"/> zählt für jede noch nicht gewählte Option, wie viele freie Regionen mit
    /// der aktuellen Auswahl + dieser Option passen – 0 → nicht wählbar (mit Erklärung), ≤ <see cref="RareThreshold"/> →
    /// „selten“. <see cref="Suggest"/> liefert die besten Regionen mit Begründung. Deterministisch (Gleichstand: kleinere Id).
    /// </summary>
    public sealed class StartFinder
    {
        /// <summary>Höchstens so viele passende Regionen → „selten“.</summary>
        public const int RareThreshold = 3;
        /// <summary>Regionen mit weniger Zellen sind zu klein für eine Stadtkarte mit Umland.</summary>
        public const int MinCells = 4;

        readonly Planet planet;
        readonly List<RegionFacts> candidates = new List<RegionFacts>();
        readonly Dictionary<int, RegionFacts> byRegion = new Dictionary<int, RegionFacts>();
        readonly float[,] scores;
        readonly IReadOnlyList<StartOption> options;

        public StartFinder(Planet planet)
        {
            this.planet = planet ?? throw new ArgumentNullException(nameof(planet));
            options = StartCriteria.Options;
            var (km, nearest) = NeighborDistances(planet);
            foreach (var r in planet.Regions)
            {
                // Startregion des Spielers zählt als frei (sie war frei, bevor er dort gegründet hat)
                bool free = Planet.IsStartable(r);
                if (!free || r.Cells.Count < MinCells) continue;
                var f = Measure(planet, r, km, nearest);
                candidates.Add(f);
                byRegion[r.Id] = f;
            }
            scores = new float[candidates.Count, options.Count];
            for (int c = 0; c < candidates.Count; c++)
                for (int o = 0; o < options.Count; o++)
                    scores[c, o] = options[o].ScoreOf(candidates[c]);
        }

        public Planet Planet => planet;
        /// <summary>Alle wählbaren Regionen (frei und groß genug).</summary>
        public IReadOnlyList<RegionFacts> Candidates => candidates;
        public IReadOnlyList<StartOption> Options => options;

        /// <summary>Kennwerte einer Region (null, wenn sie nicht frei oder zu klein ist).</summary>
        public RegionFacts FactsFor(int regionId) => byRegion.TryGetValue(regionId, out var f) ? f : null;
        public bool IsSelectable(int regionId) => byRegion.ContainsKey(regionId);

        // ------------------------------------------------------------ Auswahl

        /// <summary>Auswahl umschalten: an → aus; aus → an (in exklusiven Gruppen ersetzt sie die bisherige).</summary>
        public static void Toggle(ICollection<string> selection, StartOption option)
        {
            if (selection.Contains(option.Id)) { selection.Remove(option.Id); return; }
            if (StartCriteria.GroupInfo(option.Group).Exclusive)
            {
                var drop = new List<string>();
                foreach (var id in selection) { var o = StartCriteria.Find(id); if (o != null && o.Group == option.Group) drop.Add(id); }
                foreach (var id in drop) selection.Remove(id);
            }
            selection.Add(option.Id);
        }

        int[] Indices(IEnumerable<string> selection)
        {
            var list = new List<int>();
            if (selection != null)
                foreach (var id in selection)
                    for (int o = 0; o < options.Count; o++)
                        if (options[o].Id == id && !list.Contains(o)) list.Add(o);
            list.Sort();
            return list.ToArray();
        }

        bool Fits(int c, int[] sel)
        {
            foreach (int o in sel) if (scores[c, o] < options[o].Min) return false;
            return true;
        }

        /// <summary>Passt die Region zu allen gewählten Optionen?</summary>
        public bool Matches(RegionFacts f, IEnumerable<string> selection)
        {
            foreach (int o in Indices(selection)) if (options[o].ScoreOf(f) < options[o].Min) return false;
            return true;
        }

        /// <summary>Anzahl der freien Regionen, die zur Auswahl passen.</summary>
        public int Count(IEnumerable<string> selection)
        {
            var sel = Indices(selection);
            int n = 0;
            for (int c = 0; c < candidates.Count; c++) if (Fits(c, sel)) n++;
            return n;
        }

        /// <summary>
        /// Zustand aller Optionen bei dieser Auswahl: Treffer mit „Auswahl + Option“ (exklusive Gruppen: statt der
        /// bisherigen), 0 → <see cref="OptionAvailability.Unavailable"/>, wenige → <see cref="OptionAvailability.Rare"/>.
        /// </summary>
        public List<StartOptionState> Evaluate(ICollection<string> selection)
        {
            var result = new List<StartOptionState>(options.Count);
            var current = new HashSet<string>(selection ?? (ICollection<string>)Array.Empty<string>());
            foreach (var opt in options)
            {
                bool selected = current.Contains(opt.Id);
                var trial = new HashSet<string>(current);
                if (!selected) Toggle(trial, opt);
                int n = Count(trial);
                var st = new StartOptionState
                {
                    Option = opt, Selected = selected, Matches = n,
                    Availability = n == 0 ? OptionAvailability.Unavailable : n <= RareThreshold ? OptionAvailability.Rare : OptionAvailability.Available,
                };
                st.Tip = TipFor(st, current);
                result.Add(st);
            }
            return result;
        }

        string TipFor(StartOptionState st, HashSet<string> current)
        {
            var o = st.Option;
            if (st.Availability == OptionAvailability.Available)
                return $"{o.Description}\n{st.Matches} freie Regionen passen.";
            bool alone = Count(new[] { o.Id }) == 0;
            string why = alone ? "keine freie Region auf dieser Welt" : current.Count > 0 ? "keine passende Region zu deiner übrigen Auswahl" : "keine passende Region";
            if (st.Availability == OptionAvailability.Unavailable)
                return $"{o.Name}: {why} – {o.Geology}";
            return $"{o.Name}: selten – nur {st.Matches} freie Region{(st.Matches == 1 ? "" : "en")}. {o.Geology}";
        }

        // ------------------------------------------------------------ Vorschläge

        /// <summary>
        /// Beste passende Regionen (höchstens <paramref name="max"/>): Summe der Scores der gewählten Optionen + Grundqualität
        /// (Lebensfreundlichkeit, bei „Schwer“ umgekehrt) + kleiner Bonus für Vielfalt. Passt nichts, leer.
        /// </summary>
        public List<StartSuggestion> Suggest(ICollection<string> selection, int max = 5)
        {
            var sel = Indices(selection);
            bool hard = selection != null && selection.Contains("hard");
            var ranked = new List<(float score, int c)>();
            for (int c = 0; c < candidates.Count; c++)
            {
                if (!Fits(c, sel)) continue;
                var f = candidates[c];
                float s = 0f;
                foreach (int o in sel) s += scores[c, o];
                s += 0.6f * (hard ? 1f - f.Habitability : f.Habitability);
                s += 0.1f * WorldNoise.Clamp01(f.Region.Fertility + f.Region.Forest + f.Region.Ore + f.Region.Coal + f.Region.Oil);
                ranked.Add((s, c));
            }
            ranked.Sort((a, b) => a.score != b.score ? b.score.CompareTo(a.score) : candidates[a.c].Region.Id.CompareTo(candidates[b.c].Region.Id));
            var result = new List<StartSuggestion>();
            for (int k = 0; k < Math.Min(max, ranked.Count); k++)
                result.Add(Describe(candidates[ranked[k].c], selection, ranked[k].score));
            return result;
        }

        /// <summary>Vorschlag für eine bestimmte freie Region (freie Wahl auf der Karte) – Begründung wie bei <see cref="Suggest"/>.</summary>
        public StartSuggestion Describe(int regionId, ICollection<string> selection)
        {
            var f = FactsFor(regionId);
            return f == null ? null : Describe(f, selection, float.NaN);
        }

        StartSuggestion Describe(RegionFacts f, ICollection<string> selection, float score)
        {
            var r = f.Region;
            var s = new StartSuggestion { Facts = f, Score = score };
            var parts = new List<string> { StartCriteria.ClimateWord(f.Climate), f.TerrainWord };
            if (f.CoastScore >= StartCriteria.Fit) parts.Add("Küste");
            if (f.RiverScore >= StartCriteria.Fit) parts.Add("großer Fluss");
            else if (r.HasRiver) parts.Add("Fluss");
            if (!r.IsCoastal && !r.HasRiver) parts.Add("Binnenland ohne Fluss");
            if (f.Agriculture >= 0.75f) parts.Add("sehr fruchtbar");
            else if (f.Agriculture >= 0.5f) parts.Add("fruchtbar");
            else if (f.Agriculture < 0.15f) parts.Add("karger Boden");
            if (r.Forest >= 0.45f) parts.Add("dichter Wald");

            // Bodenschätze: gewählte zuerst, sonst die ergiebigsten (über der Schwelle) mit Lage in der Region
            var deposits = new List<(string id, string word, float[] field, float value)>
            {
                ("ore", "Erz", planet.Ore, r.Ore / 0.5f), ("coal", "Kohle", planet.Coal, r.Coal / 0.6f),
                ("oil", "Öl", planet.Oil, r.Oil / 0.3f), ("building", "Sand/Kies", planet.Sand, (r.Sand - 0.2f) / 0.6f),
            };
            deposits.Sort((a, b) =>
            {
                bool sa = selection != null && selection.Contains(a.id), sb = selection != null && selection.Contains(b.id);
                if (sa != sb) return sa ? -1 : 1;
                return b.value.CompareTo(a.value);
            });
            int shown = 0;
            foreach (var d in deposits)
            {
                if (shown >= 2 || d.value < StartCriteria.Fit) continue;
                parts.Add($"{d.word} {Where(r, d.field)}");
                shown++;
            }
            if (f.Tourism >= StartCriteria.Fit) parts.Add("reizvoll für Urlauber");
            if (f.NearestCountryId >= 0)
                parts.Add($"nächstes Land {planet.Countries[f.NearestCountryId].Name} {Round(f.NeighborKm)} km");
            else parts.Add("kein Land in Reichweite");
            s.Reason = string.Join(", ", parts);

            s.Values.Add(new StartValue("Landwirtschaft", f.Agriculture, $"Boden {r.Fertility:0.00}"));
            s.Values.Add(new StartValue("Wald", WorldNoise.Clamp01(r.Forest / 0.6f), $"{r.Forest * 100f:0} %"));
            s.Values.Add(new StartValue("Erz", WorldNoise.Clamp01(r.Ore / 0.5f), $"{r.Ore:0.00}"));
            s.Values.Add(new StartValue("Kohle", WorldNoise.Clamp01(r.Coal / 0.6f), $"{r.Coal:0.00}"));
            s.Values.Add(new StartValue("Öl/Gas", WorldNoise.Clamp01(r.Oil / 0.3f), $"{r.Oil:0.00}"));
            s.Values.Add(new StartValue("Sand/Kies", WorldNoise.Clamp01((r.Sand - 0.2f) / 0.6f), $"{r.Sand:0.00}"));
            s.Values.Add(new StartValue("Tourismus", f.Tourism, f.Tourism >= 0.5f ? "gut" : "wenig"));
            s.Values.Add(new StartValue("Lebensfreundlich", f.Habitability, $"{r.MeanTemperature:0} °C · {r.Precipitation:0} mm"));
            return s;
        }

        static int Round(float km) => km >= 1000f ? (int)MathF.Round(km / 100f) * 100 : (int)MathF.Round(km / 50f) * 50;

        /// <summary>„in den Hügeln im Norden“: Schwerpunkt der Vorkommen relativ zur Regionsmitte + Relief dort.</summary>
        string Where(Region r, float[] field)
        {
            var p = planet;
            float lat0 = p.LatitudeOf(r.CenterCell), lon0 = p.Longitude(p.XOf(r.CenterCell));
            float w = 0f, dn = 0f, de = 0f, rug = 0f;
            foreach (int i in r.Cells)
            {
                float v = field[i];
                if (v <= 0f) continue;
                v *= v; // Schwerpunkt der ergiebigen Stellen
                float dLon = p.Longitude(p.XOf(i)) - lon0;
                if (dLon > 180f) dLon -= 360f; else if (dLon < -180f) dLon += 360f;
                dn += (p.LatitudeOf(i) - lat0) * v;
                de += dLon * MathF.Cos(lat0 * MathF.PI / 180f) * v;
                rug += p.Ruggedness(i) * v;
                w += v;
            }
            if (w <= 0f) return "verstreut";
            dn /= w; de /= w; rug /= w;
            float cellDeg = 180f / p.Height;
            string terrain = rug >= 450f ? "in den Bergen" : rug >= 150f ? "in den Hügeln" : "im Flachland";
            if (MathF.Sqrt(dn * dn + de * de) < 0.6f * cellDeg) return terrain + " der Mitte";
            float ang = MathF.Atan2(dn, de) * 180f / MathF.PI; // 0 = Ost, 90 = Nord
            string[] dirs = { "Osten", "Nordosten", "Norden", "Nordwesten", "Westen", "Südwesten", "Süden", "Südosten" };
            int k = ((int)MathF.Round(ang / 45f) % 8 + 8) % 8;
            return $"{terrain} im {dirs[k]}";
        }

        // ------------------------------------------------------------ Messung

        static RegionFacts Measure(Planet p, Region r, float[] km, int[] nearest)
        {
            var f = new RegionFacts { Region = r, Climate = StartCriteria.ClimateOf(r) };
            f.Relief = MathF.Max(r.Ruggedness / 150f, MathF.Max(0f, r.MeanElevation) / 600f);
            if (r.DominantBiome == Biome.Mountain) f.Relief = MathF.Max(f.Relief, 3f);
            f.CoastShare = r.Cells.Count > 0 ? r.CoastCells / (float)r.Cells.Count : 0f;
            f.CoastScore = r.IsCoastal ? WorldNoise.Clamp01(f.CoastShare / 0.12f) : 0f;
            f.RiverSize = p.RiverThreshold > 0f && p.RiverThreshold < float.MaxValue ? r.MaxFlow / p.RiverThreshold : 0f;
            f.RiverScore = WorldNoise.SmoothStep(1.5f, 4f, f.RiverSize);

            float t = r.MeanTemperature, pr = r.Precipitation;
            float water = MathF.Max(WorldNoise.SmoothStep(250f, 800f, pr), MathF.Max(r.HasRiver ? 0.5f + 0.4f * f.RiverScore : 0f, r.Groundwater));
            float growing = WorldNoise.Clamp01(1f - MathF.Abs(t - 17f) / 18f);
            f.Agriculture = WorldNoise.Clamp01(r.Fertility / 0.5f) * water * WorldNoise.Clamp01(growing * 1.5f);
            float warmSunny = WorldNoise.Clamp01(1f - MathF.Abs(t - 24f) / 9f) * (1f - WorldNoise.SmoothStep(1200f, 2600f, pr));
            float mountains = WorldNoise.Clamp01((f.Relief - 2f) / 2f) * (t > -8f ? 1f : 0.4f);
            f.Tourism = MathF.Max(f.CoastScore * warmSunny, mountains);
            float mild = WorldNoise.Clamp01(1f - MathF.Abs(t - 14f) / 16f);
            f.Habitability = WorldNoise.Clamp01(0.35f * mild + 0.35f * WorldNoise.Clamp01(r.Fertility / 0.55f) + 0.3f * water);

            f.NeighborKm = km[r.CenterCell];
            f.NearestCountryId = nearest[r.CenterCell];
            return f;
        }

        /// <summary>Entfernung jeder Zelle zum nächsten NPC-Land (Dijkstra über alle Zellen, Land und Meer, km).</summary>
        static (float[] km, int[] nearest) NeighborDistances(Planet p)
        {
            int n = p.CellCount;
            var km = new float[n];
            var nearest = new int[n];
            var heap = new MinHeap();
            for (int i = 0; i < n; i++)
            {
                km[i] = float.MaxValue;
                nearest[i] = -1;
                var c = p.CountryOfCell(i);
                if (c == null || c.IsCityState) continue;
                km[i] = 0f;
                nearest[i] = c.Id;
                heap.Push(0f, i);
            }
            float cellKm = p.CellKm;
            while (heap.Count > 0)
            {
                var (d, i) = heap.Pop();
                if (d > km[i]) continue;
                float cosLat = MathF.Max(0.05f, MathF.Cos(p.LatitudeOf(i) * MathF.PI / 180f));
                for (int k = 0; k < 8; k++)
                {
                    int j = p.Neighbor(i, Planet.Dx8[k], Planet.Dy8[k]);
                    if (j < 0) continue;
                    float dx = Planet.Dx8[k] * cosLat, dy = Planet.Dy8[k];
                    float nd = d + MathF.Sqrt(dx * dx + dy * dy) * cellKm;
                    if (nd >= km[j]) continue;
                    km[j] = nd;
                    nearest[j] = nearest[i];
                    heap.Push(nd, j);
                }
            }
            return (km, nearest);
        }

        /// <summary>Kurzbeschreibung der Auswahl („Gemäßigt · Großer Fluss · Landwirtschaft“).</summary>
        public static string DescribeSelection(IEnumerable<string> selection)
        {
            var sb = new StringBuilder();
            foreach (var o in StartCriteria.Options)
            {
                bool on = false;
                foreach (var id in selection) if (id == o.Id) { on = true; break; }
                if (!on) continue;
                if (sb.Length > 0) sb.Append(" · ");
                sb.Append(o.Name);
            }
            return sb.Length > 0 ? sb.ToString() : "keine Wünsche";
        }
    }
}
