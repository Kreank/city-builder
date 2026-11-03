using System;
using System.Collections.Generic;
using System.Text;
using CityBuilder.Simulation.Citizens.Content;
using CityBuilder.Simulation.Core;
using CityBuilder.Simulation.Economy;
using CityBuilder.Simulation.Services.Content;
using CityBuilder.Simulation.Utilities;

namespace CityBuilder.Simulation.Services
{
    /// <summary>Eine veröffentlichte Schlagzeile (aus einem echten Ereignis der Stadt).</summary>
    public sealed class Headline
    {
        public int Id { get; internal set; }
        public long Tick { get; internal set; }
        public int Day { get; internal set; }
        public HeadlineTopic Topic { get; internal set; }
        public string Title { get; internal set; }
        public string Text { get; internal set; }
        /// <summary>Stimmung −1..+1.</summary>
        public float Tone { get; internal set; }
        /// <summary>Medienhaus (z. B. „Stadt-Kurier“) und Diensttyp-Id.</summary>
        public string Source { get; internal set; }
        public string SourceTypeId { get; internal set; }
        /// <summary>Anteil der Einwohner, die das Medium erreicht (0..1) beim Erscheinen.</summary>
        public float Reach { get; internal set; }
        /// <summary>Bezug: Straßensegment (0 = keins).</summary>
        public int Street { get; internal set; }

        public override string ToString() => $"[{Source}] {Title}";
    }

    /// <summary>
    /// Medien (G4 erweitert, <c>world.Media</c>, alle 30 min nach den Diensten): erkennt Ereignisse in echten Stadtdaten
    /// (Stau, Strom-/Wasser-/Abwasserausfall, Steuersätze, Eröffnungen, Atomkraftwerk/Störfall, Trinkwasser, Zufriedenheit,
    /// Einwohnermarken, Arbeitslosigkeit) und macht daraus Schlagzeilen aus <see cref="Headlines"/>.
    /// <para>
    /// <b>Ohne Medien keine Schlagzeilen</b> (Spielanreiz): Es muss ein Zeitungsverlag/Radio-/TV-Sender stehen und mindestens
    /// <see cref="MinReach"/> der Einwohner erreichen. Je Thema und Bezug gilt eine Sperrfrist (<see cref="CooldownTicks"/>),
    /// höchstens <see cref="MaxPerRun"/> Meldungen je Durchlauf. Vorlage und Medienhaus per stabilem Hash (kein Zufall).
    /// </para>
    /// <para>
    /// <see cref="CurrentTone"/> = gewichtetes Mittel der Stimmung der Meldungen der letzten <see cref="ToneDays"/> Tage →
    /// Bürgersystem (Gemüt der Erreichten, vorsichtig dosiert). Ereignis <see cref="Published"/> für die HUD.
    /// </para>
    /// </summary>
    public sealed class MediaSystem : ISimulationSystem
    {
        public int TickInterval => 5;

        public const float MinReach = 0.01f;
        public const int CooldownTicks = 2 * SimulationClock.TicksPerDay;
        public const int MaxPerRun = 2;
        public const int MaxRecent = 40;
        public const float ToneDays = 2f;
        /// <summary>Stau: so viele (fast) stehende Fahrzeuge auf einem Segment.</summary>
        public const int JamVehicles = 6;
        /// <summary>Ausfall: ab so vielen unversorgten Gebäuden.</summary>
        public const int OutageBuildings = 3;
        public const float SatisfactionStep = 0.04f;
        public const float UnemploymentAlarm = 0.12f;
        static readonly int[] Milestones = { 100, 250, 500, 1000, 2500, 5000, 10000, 25000, 50000, 100000, 250000 };

        readonly SimulationWorld world;
        readonly List<Headline> recent = new List<Headline>();
        readonly Dictionary<string, long> lastPublished = new Dictionary<string, long>();
        readonly List<Candidate> pending = new List<Candidate>();
        readonly float[] lastTax = new float[4];
        bool taxKnown;
        int satDay = -1;
        float satYesterday = -1f, satToday = -1f;
        int lastMilestone = -1;
        int nextId = 1;

        struct Candidate
        {
            public HeadlineTopic Topic;
            public string Key;
            public Dictionary<string, string> Values;
            public int Street;
        }

        /// <summary>Neueste zuerst (höchstens <see cref="MaxRecent"/>).</summary>
        public IReadOnlyList<Headline> Recent => recent;
        public int Version { get; private set; }
        public int PublishedCount { get; private set; }
        /// <summary>Anteil der Einwohner, den die Medien erreichen (letzter Durchlauf).</summary>
        public float Reach { get; private set; }
        public event Action<Headline> Published;

        public MediaSystem(SimulationWorld world)
        {
            this.world = world ?? throw new ArgumentNullException(nameof(world));
            if (world.Services != null) world.Services.FacilityAdded += OnServiceAdded;
            if (world.Utilities != null)
            {
                world.Utilities.FacilityAdded += OnUtilityAdded;
                world.Utilities.NuclearIncident += f => Queue(HeadlineTopic.NuclearIncident, "incident" + f.Id + "-" + world.Clock.Day,
                    Values(("Name", f.Type.DisplayName)));
            }
        }

        /// <summary>Gibt es mindestens ein Medienhaus?</summary>
        public bool HasMedia
        {
            get
            {
                foreach (var f in world.Services.Facilities) if (f.Kind == ServiceKinds.Media) return true;
                return false;
            }
        }

        /// <summary>
        /// Stimmung der Nachrichtenlage −1..+1: Mittel der Meldungen der letzten <see cref="ToneDays"/> Tage, neuere zählen mehr.
        /// 0 ohne Meldungen.
        /// </summary>
        public float CurrentTone
        {
            get
            {
                long now = world.Clock.Tick;
                float span = ToneDays * SimulationClock.TicksPerDay;
                float sum = 0f, wsum = 0f;
                foreach (var h in recent)
                {
                    float age = now - h.Tick;
                    if (age > span) break; // neueste zuerst
                    float w = 1f - age / span;
                    sum += h.Tone * w;
                    wsum += w;
                }
                return wsum > 0f ? Math.Clamp(sum / wsum, -1f, 1f) : 0f;
            }
        }

        // ------------------------------------------------------------ Ereignisse

        void OnServiceAdded(ServiceFacility f)
        {
            HeadlineTopic topic;
            switch (f.Kind)
            {
                case ServiceKinds.Education: topic = HeadlineTopic.NewSchool; break;
                case ServiceKinds.Health: topic = HeadlineTopic.NewHealth; break;
                case ServiceKinds.Park: topic = HeadlineTopic.NewPark; break;
                case ServiceKinds.Police: topic = HeadlineTopic.NewPolice; break;
                case ServiceKinds.Fire: topic = HeadlineTopic.NewFireStation; break;
                case ServiceKinds.Media: topic = HeadlineTopic.NewMedia; break;
                default: return;
            }
            var v = Values(("Name", f.Type.DisplayName));
            if (f.AccessSegmentId >= 0) v["Straße"] = PlaceNames.Street(f.AccessSegmentId);
            if (f.Kind == ServiceKinds.Media) v["Medium"] = Headlines.OutletName(f.Type.Id);
            Queue(topic, "open" + f.Id, v, f.AccessSegmentId > 0 ? f.AccessSegmentId : 0);
        }

        void OnUtilityAdded(UtilityFacility f)
        {
            if (f.Type.ConcernRadius > 0f) Queue(HeadlineTopic.NuclearPlantBuilt, "plant" + f.Id, Values(("Name", f.Type.DisplayName)));
        }

        void Queue(HeadlineTopic topic, string key, Dictionary<string, string> values, int street = 0) =>
            pending.Add(new Candidate { Topic = topic, Key = key, Values = values, Street = street });

        static Dictionary<string, string> Values(params (string key, string value)[] pairs)
        {
            var d = new Dictionary<string, string>();
            foreach (var (k, v) in pairs) d[k] = v;
            return d;
        }

        // ------------------------------------------------------------ Tick

        public void Tick(SimulationWorld w)
        {
            var candidates = new List<Candidate>(pending);
            pending.Clear();
            Detect(candidates); // Schnappschüsse (Steuer, Zufriedenheit) laufen auch ohne Medien weiter
            var outlets = OutletTypes();
            Reach = outlets.Count == 0 ? 0f : ComputeReach();
            if (outlets.Count == 0 || Reach < MinReach) return; // ohne Medien keine Schlagzeilen

            int published = 0;
            foreach (var c in candidates)
            {
                if (published >= MaxPerRun) break;
                string key = c.Topic + ":" + c.Key;
                if (lastPublished.TryGetValue(key, out long last) && world.Clock.Tick - last < CooldownTicks) continue;
                var h = Compose(c, outlets);
                if (h == null) continue;
                lastPublished[key] = world.Clock.Tick;
                recent.Insert(0, h);
                if (recent.Count > MaxRecent) recent.RemoveAt(recent.Count - 1);
                published++;
                PublishedCount++;
                Version++;
                Published?.Invoke(h);
            }
        }

        /// <summary>Medien-Diensttypen in der Stadt (nach Id sortiert, ohne Doppelte).</summary>
        List<ServiceType> OutletTypes()
        {
            var list = new List<ServiceType>();
            var facilities = new List<ServiceFacility>();
            foreach (var f in world.Services.Facilities) if (f.Kind == ServiceKinds.Media) facilities.Add(f);
            facilities.Sort((a, b) => a.Id.CompareTo(b.Id));
            foreach (var f in facilities) if (!list.Contains(f.Type)) list.Add(f.Type);
            return list;
        }

        float ComputeReach()
        {
            float sum = 0f, n = 0f;
            foreach (var b in world.City.Buildings)
            {
                if (!b.IsConstructed || b.Residents <= 0) continue;
                sum += b.Residents * world.Services.Coverage(ServiceKinds.Media, b);
                n += b.Residents;
            }
            return n > 0f ? sum / n : 0f;
        }

        Headline Compose(in Candidate c, List<ServiceType> outlets)
        {
            int id = nextId;
            uint hash = SimRandomHash(id, (int)c.Topic);
            var outlet = outlets[(int)(hash % (uint)outlets.Count)];
            var values = c.Values ?? new Dictionary<string, string>();
            if (!values.ContainsKey("Medium")) values["Medium"] = Headlines.OutletName(outlet.Id);
            int count = Headlines.Count(c.Topic);
            for (int attempt = 0; attempt < count; attempt++)
            {
                var (title, text) = Headlines.Template(c.Topic, (int)(hash >> 8) + attempt);
                string t = Fill(title, values), x = Fill(text, values);
                if (t == null || x == null) continue;
                nextId++;
                return new Headline
                {
                    Id = id,
                    Tick = world.Clock.Tick,
                    Day = world.Clock.Day,
                    Topic = c.Topic,
                    Title = t,
                    Text = x,
                    Tone = Headlines.Tone(c.Topic),
                    Source = Headlines.OutletName(outlet.Id),
                    SourceTypeId = outlet.Id,
                    Reach = Reach,
                    Street = c.Street,
                };
            }
            return null;
        }

        static string Fill(string template, Dictionary<string, string> values)
        {
            if (template.IndexOf('{') < 0) return template;
            var sb = new StringBuilder(template.Length + 24);
            int i = 0;
            while (i < template.Length)
            {
                char ch = template[i];
                if (ch != '{') { sb.Append(ch); i++; continue; }
                int end = template.IndexOf('}', i);
                if (end < 0) { sb.Append(template, i, template.Length - i); break; }
                if (!values.TryGetValue(template.Substring(i + 1, end - i - 1), out var v) || string.IsNullOrEmpty(v)) return null;
                sb.Append(v);
                i = end + 1;
            }
            return sb.ToString();
        }

        static uint SimRandomHash(int a, int b) => Citizens.SimRandom.Hash(a, b);

        // ------------------------------------------------------------ Erkennung aus echten Daten

        void Detect(List<Candidate> into)
        {
            DetectTaxes(into);
            DetectSatisfaction(into);
            DetectOutage(into, UtilityKind.Power, HeadlineTopic.PowerOutage);
            DetectOutage(into, UtilityKind.Water, HeadlineTopic.WaterShortage);
            DetectOutage(into, UtilityKind.Sewage, HeadlineTopic.SewageShortage);
            DetectWaterPollution(into);
            DetectTraffic(into);
            DetectPopulation(into);
            DetectUnemployment(into);
            DetectTransit(into); // ÖPNV
        }

        // --- ÖPNV: neue Linie, volle Busse, Verspätungen (liest world.Transit; Transit entsteht nach den Medien)
        /// <summary>Ab so vielen stehen gelassenen Fahrgästen am Tag ist die Linie eine Meldung wert.</summary>
        public int BusOverloadNews = 20;
        readonly HashSet<int> knownLines = new HashSet<int>();
        bool linesKnown;

        void DetectTransit(List<Candidate> into)
        {
            var t = world.Transit;
            if (t == null) return;
            bool media = HasMedia;
            foreach (var l in t.Lines)
            {
                if (knownLines.Add(l.Id) && linesKnown && media)
                {
                    var first = l.Stops.Count > 0 ? t.GetStop(l.Stops[0]) : null;
                    into.Add(new Candidate
                    {
                        Topic = HeadlineTopic.NewBusLine, Key = "line" + l.Id,
                        Values = Values(("Name", l.Name), ("Anzahl", l.Stops.Count.ToString()), ("Straße", first?.Name ?? "")),
                        Street = first?.SegmentId ?? 0,
                    });
                }
                if (l.LeftBehindToday >= BusOverloadNews)
                    into.Add(new Candidate { Topic = HeadlineTopic.BusOverloaded, Key = "full" + l.Id, Values = Values(("Name", l.Name), ("Anzahl", l.LeftBehindToday.ToString())) });
                if (l.ActiveVehicles > 0 && l.Punctuality < 0.6f)
                    into.Add(new Candidate { Topic = HeadlineTopic.BusLate, Key = "late" + l.Id, Values = Values(("Name", l.Name), ("Prozent", ((int)(l.Punctuality * 100f)).ToString())) });
            }
            // Ohne Medien gebaute Linien sind später keine Neuigkeit mehr
            if (media) linesKnown = true;
        }
        // --- Ende ÖPNV

        void DetectTaxes(List<Candidate> into)
        {
            var eco = world.Economy;
            if (eco == null) return;
            for (int k = 0; k < lastTax.Length; k++)
            {
                float rate = eco.TaxRate((TaxKind)k);
                if (taxKnown && rate != lastTax[k])
                {
                    var topic = rate > lastTax[k] ? HeadlineTopic.TaxRaise : HeadlineTopic.TaxCut;
                    into.Add(new Candidate
                    {
                        Topic = topic,
                        Key = $"{k}-{rate:0.00}",
                        Values = Values(("Steuer", Budget.DisplayName((TaxKind)k)), ("Satz", $"{rate * 100f:0}")),
                    });
                }
                lastTax[k] = rate;
            }
            taxKnown = true;
        }

        void DetectSatisfaction(List<Candidate> into)
        {
            var s = world.Citizens?.Stats;
            if (s == null || s.Population < 20) return;
            int day = world.Clock.Day;
            if (day == satDay) return;
            satDay = day;
            satYesterday = satToday;
            satToday = s.AverageSatisfaction;
            if (satYesterday < 0f) return;
            float d = satToday - satYesterday;
            if (MathF.Abs(d) < SatisfactionStep) return;
            into.Add(new Candidate
            {
                Topic = d < 0f ? HeadlineTopic.SatisfactionDrop : HeadlineTopic.SatisfactionRise,
                Key = "sat",
                Values = Values(("Prozent", $"{satToday * 100f:0}")),
            });
        }

        void DetectOutage(List<Candidate> into, UtilityKind kind, HeadlineTopic topic)
        {
            var u = world.Utilities;
            if (u == null || !u.IsRequired(kind)) return;
            int count = 0, street = 0;
            int firstId = int.MaxValue;
            foreach (var b in world.City.Buildings)
            {
                if (!b.IsConstructed || u.IsSupplied(b, kind)) continue;
                count++;
                if (b.Id < firstId) { firstId = b.Id; street = b.FrontSegmentId; }
            }
            if (count < OutageBuildings) return;
            into.Add(new Candidate
            {
                Topic = topic,
                Key = "net",
                Values = Values(("Anzahl", count.ToString()), ("Straße", street > 0 ? PlaceNames.Street(street) : null)),
                Street = street,
            });
        }

        void DetectWaterPollution(List<Candidate> into)
        {
            var wq = world.WaterQuality;
            if (wq == null) return;
            UtilityFacility worst = null;
            float q = 1f;
            foreach (var f in world.Utilities.Facilities)
            {
                if (f.Kind != UtilityKind.Water || !f.Type.NeedsWater) continue;
                float pq = wq.PumpQuality(f);
                if (pq < q - 1e-4f || (worst != null && MathF.Abs(pq - q) <= 1e-4f && f.Id < worst.Id)) { q = pq; worst = f; }
            }
            if (worst == null || q >= WaterQualitySystem.BadDrinkingWater) return;
            into.Add(new Candidate
            {
                Topic = HeadlineTopic.WaterPolluted,
                Key = "pump" + worst.Id,
                Values = Values(("Name", worst.Type.DisplayName), ("Prozent", $"{q * 100f:0}")),
            });
        }

        void DetectTraffic(List<Candidate> into)
        {
            var t = world.Traffic;
            if (t == null || t.Vehicles.Count < JamVehicles) return;
            var counts = new Dictionary<int, int>();
            foreach (var v in t.ActiveVehicles)
            {
                if (v.InJunction || v.Lane == null || v.Speed >= 2f) continue;
                int id = v.Lane.Segment.Id;
                counts.TryGetValue(id, out int n);
                counts[id] = n + 1;
            }
            int best = -1, bestN = 0;
            foreach (var kv in counts)
                if (kv.Value > bestN || (kv.Value == bestN && kv.Key < best)) { best = kv.Key; bestN = kv.Value; }
            if (best < 0 || bestN < JamVehicles) return;
            into.Add(new Candidate
            {
                Topic = HeadlineTopic.TrafficJam,
                Key = "seg" + best,
                Values = Values(("Straße", PlaceNames.Street(best)), ("Anzahl", bestN.ToString())),
                Street = best,
            });
        }

        void DetectPopulation(List<Candidate> into)
        {
            var s = world.Citizens?.Stats;
            if (s == null) return;
            int reached = -1;
            foreach (int m in Milestones) if (s.Population >= m) reached = m;
            // Erst mit Medien zählen: frühere Marken sind keine Nachricht mehr
            if (!HasMedia || lastMilestone < 0) { lastMilestone = Math.Max(0, reached); return; }
            if (reached <= lastMilestone) return;
            lastMilestone = reached;
            into.Add(new Candidate { Topic = HeadlineTopic.PopulationMilestone, Key = "pop" + reached, Values = Values(("Einwohner", reached.ToString())) });
        }

        void DetectUnemployment(List<Candidate> into)
        {
            var s = world.Citizens?.Stats;
            if (s == null || s.WorkingAge < 50 || s.UnemploymentRate < UnemploymentAlarm) return;
            into.Add(new Candidate { Topic = HeadlineTopic.Unemployment, Key = "jobs", Values = Values(("Prozent", $"{s.UnemploymentRate * 100f:0}")) });
        }
    }
}
