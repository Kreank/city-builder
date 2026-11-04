using System;
using System.Collections.Generic;
using CityBuilder.Simulation.Goods;
using CityBuilder.Simulation.Persistence;
using CityBuilder.Simulation.Realm;
using CityBuilder.Simulation.Trade;

namespace CityBuilder.Simulation.World
{
    /// <summary>
    /// Spielstand „realm“: v1 (Spielstart) = Stadtname, Landesname, Flagge, Städte-Regionen, Besitz aller Regionen.
    /// v2 (Etappe 7 Reich) hängt an: aktive Stadt, Hauptstadt, Städte mit aggregierter Volkswirtschaft und Detail-Schnappschuss
    /// (lokale Abschnitte als Rohbytes – unkomprimiert, damit Prüfsummen nicht vom GZip abhängen), Lieferungen + Ware unterwegs,
    /// Landeskasse, Anerkennung, NPC-Expansion (Zufall, Abkühlzeiten), Meldungen, Command-Warteschlange und -Log.
    /// v1-Stände: eine Stadt (die Startstadt), sonst Standardwerte.
    /// </summary>
    public sealed partial class PlayerRealm
    {
        public string SaveId => "realm";
        public int SaveVersion => 2;

        public void Write(SaveWriter w)
        {
            // --- v1
            w.String(CityName);
            w.String(NationName);
            w.UInt(FlagColor);
            w.IntList(CityRegions);
            int n = planet?.Regions.Count ?? 0;
            w.Int(n);
            for (int i = 0; i < n; i++) w.Int(planet.Regions[i].CountryId);

            // --- v2 Etappe 7
            w.Int(ActiveCityId); w.Int(NextCityId); w.Int(CapitalCityId);
            w.Int(NationFoundedDay); w.Double(NationTreasury); w.Double(NationIncomeTotal);
            w.Bool(NpcExpansion); w.Int(InitialFreeRegions); w.Int(RegionsClaimedByNpc);
            Rng.WriteState(w);
            w.Int(cities.Count);
            foreach (var c in cities)
            {
                w.Int(c.Id); w.String(c.Name); w.Int(c.RegionId); w.Int(c.Cell); w.Int(c.TerrainSeed); w.Int(c.Seed);
                w.Int(c.FoundedDay); w.Int(c.LastDetailedDay);
                c.Economy.Write(w);
                var snap = c.Id == ActiveCityId ? null : c.Snapshot; // die aktive Stadt IST die Welt
                w.Bool(snap != null);
                if (snap != null)
                {
                    w.Int(snap.Count);
                    foreach (var s in snap) { w.String(s.Id); w.Int(s.Version); w.Bytes(s.Data); }
                }
            }
            w.Int(NextDeliveryId);
            w.Int(deliveries.Count);
            foreach (var d in deliveries)
            {
                w.Int(d.Id); w.Int(d.From); w.Int(d.To); WriteTerms(w, d.Terms);
                w.Int(d.StartDay); w.Int(d.NextDispatchDay); w.Int(d.WeeksDispatched); w.Name(d.Status);
                w.Double(d.Shipped); w.Double(d.Shortfall); w.Double(d.FreightPaid); w.Double(d.ValueSettled); w.Double(d.LastShipped);
            }
            w.Int(shipments.Count);
            foreach (var s in shipments)
            {
                w.Int(s.DeliveryId); w.Int(s.From); w.Int(s.To); w.String(s.Good.Id); w.Double(s.Units); w.Double(s.UnitPrice);
                w.Int(s.DispatchDay); w.Int(s.ArriveDay); w.Name(s.Mode);
            }
            w.Int(Recognized.Length);
            for (int i = 0; i < Recognized.Length; i++) { w.Bool(Recognized[i]); w.Int(RecognitionReviewDay[i]); w.Int(LastClaimDay[i]); }
            w.Int(news.Count);
            foreach (var m in news) { w.Int(m.Day); w.String(m.Title); w.String(m.Text); w.Bool(m.Important); }
            w.Int(queue.Count);
            foreach (var cmd in queue) WriteCommand(w, cmd);
            w.Int(log.Count);
            foreach (var l in log) { w.Int(l.Day); WriteCommand(w, l.Command); w.Bool(l.Result.Ok); w.String(l.Result.Message); w.Int(l.Result.Id); }
        }

        public void Read(SaveReader r, int version)
        {
            string cityName = Clean(r.String()) ?? DefaultCityName;
            NationName = Clean(r.String());
            FlagColor = r.UInt() & 0xFFFFFFu;
            var regions = r.IntList();
            int n = r.Count(4);
            var owners = new int[n];
            for (int i = 0; i < n; i++) owners[i] = r.Int();

            int rc = planet?.Regions.Count ?? 0;
            if (planet != null)
            {
                if (n != rc) throw new SaveFormatException($"{n} Regionen gespeichert, die Welt hat {rc} (anderer Seed?).");
                for (int i = 0; i < n; i++)
                {
                    int o = owners[i];
                    if (o < -1 || o >= planet.Countries.Count) throw new SaveFormatException($"Region {i}: unbekanntes Land {o}.");
                    var region = planet.Regions[i];
                    if (region.CountryId != o) planet.AssignRegion(region, o >= 0 ? planet.Countries[o] : null);
                }
            }

            if (version < 2)
            {
                // Spielstart-Stand: genau eine Stadt (Startstadt), Seeds aus der frisch erzeugten Welt (Attach)
                var first = cities.Count > 0 ? cities[0] : new RealmCity { Id = 0 };
                cities.Clear();
                first.Id = 0;
                first.Name = cityName;
                int region = regions.Count > 0 && regions[0] >= 0 && regions[0] < rc ? regions[0] : planet?.StartRegionId ?? -1;
                first.RegionId = region;
                first.Snapshot = null;
                cities.Add(first);
                ActiveCityId = CapitalCityId = 0;
                NextCityId = 1;
                RefreshRegions();
                Apply();
                Version++;
                return;
            }

            ActiveCityId = r.Int(); NextCityId = r.Int(); CapitalCityId = r.Int();
            NationFoundedDay = r.Int(); NationTreasury = r.Double(); NationIncomeTotal = r.Double();
            NpcExpansion = r.Bool(); InitialFreeRegions = r.Int(); RegionsClaimedByNpc = r.Int();
            Rng.ReadState(r);
            cities.Clear();
            int cc = r.Count(30);
            if (cc < 1) throw new SaveFormatException("Reich ohne Stadt.");
            for (int i = 0; i < cc; i++)
            {
                var c = new RealmCity
                {
                    Id = r.Int(), Name = Clean(r.String()) ?? DefaultCityName, RegionId = r.Int(), Cell = r.Int(), TerrainSeed = r.Int(),
                    Seed = r.Int(), FoundedDay = r.Int(), LastDetailedDay = r.Int(),
                };
                if (planet != null && (c.RegionId < 0 || c.RegionId >= rc)) throw new SaveFormatException($"Stadt {c.Name}: unbekannte Region {c.RegionId}.");
                c.Economy.Read(r);
                if (r.Bool())
                {
                    int sc = r.Count(9);
                    var list = new List<SaveSection>(sc);
                    for (int k = 0; k < sc; k++)
                    {
                        string id = r.String();
                        int v = r.Int();
                        var data = r.Bytes() ?? throw new SaveFormatException($"Stadt {c.Name}: Abschnitt {id} ohne Daten.");
                        list.Add(new SaveSection { Id = id, Version = v, Data = data });
                    }
                    c.Snapshot = list;
                }
                cities.Add(c);
            }
            if (City(ActiveCityId) == null) throw new SaveFormatException($"Aktive Stadt {ActiveCityId} fehlt.");
            if (City(CapitalCityId) == null) CapitalCityId = cities[0].Id;
            City(ActiveCityId).Snapshot = null;

            NextDeliveryId = r.Int();
            deliveries.Clear();
            int dc = r.Count(40);
            for (int i = 0; i < dc; i++)
            {
                var d = new RealmDelivery { Id = r.Int(), From = r.Int(), To = r.Int() };
                d.Terms = ReadTerms(r) ?? throw new SaveFormatException("Lieferung ohne Bedingungen.");
                d.StartDay = r.Int(); d.NextDispatchDay = r.Int(); d.WeeksDispatched = r.Int(); d.Status = r.Name<RealmDeliveryStatus>();
                d.Shipped = r.Double(); d.Shortfall = r.Double(); d.FreightPaid = r.Double(); d.ValueSettled = r.Double(); d.LastShipped = r.Double();
                deliveries.Add(d);
            }
            shipments.Clear();
            int shc = r.Count(30);
            for (int i = 0; i < shc; i++)
            {
                var s = new RealmShipment { DeliveryId = r.Int(), From = r.Int(), To = r.Int() };
                string good = r.String();
                s.Good = GoodsCatalog.Get(good) ?? throw new SaveFormatException($"Unbekanntes Gut „{good}“.");
                s.Units = r.Double(); s.UnitPrice = r.Double(); s.DispatchDay = r.Int(); s.ArriveDay = r.Int(); s.Mode = r.Name<RealmRouteMode>();
                shipments.Add(s);
            }
            int nc = r.Count(9);
            if (nc != Recognized.Length) throw new SaveFormatException($"{nc} Länder im Reich gespeichert, die Welt hat {Recognized.Length}.");
            for (int i = 0; i < nc; i++) { Recognized[i] = r.Bool(); RecognitionReviewDay[i] = r.Int(); LastClaimDay[i] = r.Int(); }
            news.Clear();
            int mc = r.Count(10);
            for (int i = 0; i < mc; i++) news.Add(new RealmNews(r.Int(), r.String(), r.String(), r.Bool()));
            queue.Clear();
            int qc = r.Count(4);
            for (int i = 0; i < qc; i++) { var cmd = ReadCommand(r); if (cmd != null) queue.Enqueue(cmd); }
            log.Clear();
            int lc = r.Count(10);
            for (int i = 0; i < lc; i++)
            {
                int day = r.Int();
                var cmd = ReadCommand(r);
                bool ok = r.Bool(); string msg = r.String(); int id = r.Int();
                if (cmd != null) log.Add(new RealmLogEntry(day, cmd, new TradeResult(ok, msg, id)));
            }
            RefreshRegions();
            Apply();
            Version++;
        }

        // ------------------------------------------------------------ Bausteine

        static void WriteTerms(SaveWriter w, ContractTerms t)
        {
            w.Bool(t != null);
            if (t == null) return;
            w.Name(t.Kind); w.String(t.Good?.Id); w.Double(t.UnitsPerWeek); w.Int(t.Weeks); w.Name(t.PriceMode); w.Double(t.Price); w.Double(t.PenaltyShare);
        }

        static ContractTerms ReadTerms(SaveReader r)
        {
            if (!r.Bool()) return null;
            var t = new ContractTerms { Kind = r.Name<ContractKind>() };
            string good = r.String();
            t.Good = good == null ? null : GoodsCatalog.Get(good) ?? throw new SaveFormatException($"Unbekanntes Gut „{good}“.");
            t.UnitsPerWeek = r.Double(); t.Weeks = r.Int(); t.PriceMode = r.Name<PriceMode>(); t.Price = r.Double(); t.PenaltyShare = r.Double();
            return t;
        }

        static void WriteCommand(SaveWriter w, RealmCommand cmd)
        {
            w.String(cmd.Kind);
            switch (cmd)
            {
                case ProposeDeliveryCommand p: w.Int(p.FromCity); w.Int(p.ToCity); WriteTerms(w, p.Terms); break;
                case CancelDeliveryCommand c: w.Int(c.DeliveryId); break;
                case FoundCityCommand f: w.Int(f.RegionId); w.Int(f.Cell); w.String(f.Name); break;
                case FoundNationCommand f: w.String(f.Name); w.UInt(f.Color); w.Int(f.CapitalCity); break;
                case TransferFundsCommand t: w.Int(t.City); w.Double(t.Amount); break;
                case RenameCityCommand n: w.Int(n.City); w.String(n.Name); break;
                default: throw new InvalidOperationException("Unbekanntes Reichs-Command " + cmd.GetType().Name);
            }
        }

        static RealmCommand ReadCommand(SaveReader r)
        {
            string kind = r.String();
            switch (kind)
            {
                case "delivery": { int a = r.Int(), b = r.Int(); return new ProposeDeliveryCommand(a, b, ReadTerms(r)); }
                case "cancel": return new CancelDeliveryCommand(r.Int());
                case "found-city": { int region = r.Int(), cell = r.Int(); return new FoundCityCommand(region, r.String(), cell); }
                case "found-nation": { string name = r.String(); uint color = r.UInt(); return new FoundNationCommand(name, color, r.Int()); }
                case "transfer": { int c = r.Int(); return new TransferFundsCommand(c, r.Double()); }
                case "rename": { int c = r.Int(); return new RenameCityCommand(c, r.String()); }
                default: throw new SaveFormatException($"Unbekanntes Reichs-Command „{kind}“.");
            }
        }
    }
}
