using System.Collections.Generic;
using CityBuilder.Simulation.Persistence;
using CityBuilder.Simulation.Services.Content;

namespace CityBuilder.Simulation.Services
{
    /// <summary>
    /// Spielstand der Medien: letzte Schlagzeilen (Text fest beim Entstehen), Sperrfristen je Thema/Bezug, noch nicht
    /// veröffentlichte Kandidaten, Vergleichswerte (Steuern, Zufriedenheit, Meilensteine, bekannte Linien), Zähler.
    /// </summary>
    public sealed partial class MediaSystem : ISaveable
    {
        public string SaveId => "media";
        public int SaveVersion => 1;

        public void Write(SaveWriter w)
        {
            w.Int(nextId); w.Int(Version); w.Int(PublishedCount); w.Float(Reach);
            w.Int(BusOverloadNews);
            w.Bool(taxKnown); w.Floats(lastTax);
            w.Int(satDay); w.Float(satYesterday); w.Float(satToday); w.Int(lastMilestone);
            w.Bool(linesKnown);
            var known = new List<int>(knownLines);
            known.Sort();
            w.IntList(known);

            w.Int(recent.Count);
            foreach (var h in recent)
            {
                w.Int(h.Id); w.Long(h.Tick); w.Int(h.Day); w.Name(h.Topic); w.String(h.Title); w.String(h.Text);
                w.Float(h.Tone); w.String(h.Source); w.String(h.SourceTypeId); w.Float(h.Reach); w.Int(h.Street);
            }
            var keys = new List<string>(lastPublished.Keys);
            keys.Sort(System.StringComparer.Ordinal);
            w.Int(keys.Count);
            foreach (var k in keys) { w.String(k); w.Long(lastPublished[k]); }

            w.Int(pending.Count);
            foreach (var c in pending)
            {
                w.Name(c.Topic); w.String(c.Key); w.Int(c.Street);
                w.Int(c.Values?.Count ?? -1);
                if (c.Values != null) foreach (var kv in c.Values) { w.String(kv.Key); w.String(kv.Value); }
            }
        }

        public void Read(SaveReader r, int version)
        {
            nextId = r.Int(); Version = r.Int(); PublishedCount = r.Int(); Reach = r.Float();
            BusOverloadNews = r.Int();
            taxKnown = r.Bool();
            var tax = r.Floats() ?? new float[0];
            for (int i = 0; i < tax.Length && i < lastTax.Length; i++) lastTax[i] = tax[i];
            satDay = r.Int(); satYesterday = r.Float(); satToday = r.Float(); lastMilestone = r.Int();
            linesKnown = r.Bool();
            knownLines.Clear();
            foreach (int id in r.IntList() ?? new List<int>()) knownLines.Add(id);

            recent.Clear();
            int n = r.Count(30);
            for (int i = 0; i < n; i++)
            {
                var h = new Headline { Id = r.Int(), Tick = r.Long(), Day = r.Int() };
                bool known = r.TryName<HeadlineTopic>(out var topic);
                h.Topic = topic;
                h.Title = r.String(); h.Text = r.String(); h.Tone = r.Float(); h.Source = r.String(); h.SourceTypeId = r.String();
                h.Reach = r.Float(); h.Street = r.Int();
                if (known) recent.Add(h);
            }
            lastPublished.Clear();
            int k = r.Count(9);
            for (int i = 0; i < k; i++) { string key = r.String() ?? ""; lastPublished[key] = r.Long(); }

            pending.Clear();
            int p = r.Count(10);
            for (int i = 0; i < p; i++)
            {
                bool known = r.TryName<HeadlineTopic>(out var topic);
                var c = new Candidate { Topic = topic, Key = r.String(), Street = r.Int() };
                int vc = r.CountOrNull(2);
                if (vc >= 0)
                {
                    c.Values = new Dictionary<string, string>();
                    for (int v = 0; v < vc; v++) { string key = r.String() ?? ""; c.Values[key] = r.String(); }
                }
                if (known) pending.Add(c);
            }
        }
    }
}
