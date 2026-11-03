using System;
using System.Collections.Generic;
using CityBuilder.Simulation.Persistence;

namespace CityBuilder.Simulation.Goods
{
    /// <summary>
    /// Spielstand der Warenketten: Firmen (Rezept, Lager inkl. Einkaufswert und Bestellungen unterwegs, Kasse,
    /// Gewinnrechnung heute/gestern, geglättete Werte), Märkte (Preise, Bestände, Tageswerte, Summen), Lieferungen
    /// (Lkw und Platzhalter, Warteschlange der Abfahrten, Zuordnung Fahrt → Lieferung), Haushaltseinkäufe des Tages,
    /// Statistik, Handelsbilanz, Zufall und Zähler. Lieferantenlisten/Außenanschlüsse entstehen jede Stunde neu.
    /// </summary>
    public sealed partial class GoodsSystem : ISaveable
    {
        public string SaveId => "goods";
        public int SaveVersion => 1;

        public void Write(SaveWriter w)
        {
            rng.WriteState(w);
            w.Int(lastCityVersion);
            w.Int(harvestDay);
            w.Float(harvestToday);
            w.Int(Version);
            w.Bool(AbandonBankruptBuildings);
            w.Float(DepositSampleSpacing);
            w.Floats(dayDemand);

            // Märkte
            w.Int(markets.Length);
            foreach (var m in markets)
            {
                w.String(m.Good.Id);
                w.Float(m.LocalPrice); w.Float(m.WorldPrice); w.Float(m.Stock); w.Float(m.TargetStock);
                w.Float(m.DemandPerDay); w.Float(m.SupplyPerDay); w.Float(m.CapacityPerDay); w.Float(m.PlannedDemandPerDay);
                WriteDay(w, m.Today);
                WriteDay(w, m.Yesterday);
                w.Double(m.TotalImportValue); w.Double(m.TotalExportValue); w.Float(m.TotalImported); w.Float(m.TotalExported);
            }

            // Firmen (Reihenfolge = Gebäude-Id)
            w.Int(firms.Count);
            foreach (var f in firms)
            {
                w.Int(f.BuildingId);
                w.String(f.Recipe.Id);
                w.Int(f.FoundedDay);
                w.Name(f.Status);
                w.Int(f.AgeDays);
                w.Floats(f.InputStock); w.Floats(f.InputCapacity); w.Floats(f.Incoming); w.Floats(f.OutputStock); w.Floats(f.OutputCapacity);
                w.Doubles(f.InputValue);
                w.Float(f.Productivity); w.Float(f.SkillCoverage); w.Float(f.DepositQuality);
                w.Double(f.Cash);
                WriteLedger(w, f.Today);
                WriteLedger(w, f.Yesterday);
                w.Double(f.ProfitEma); w.Double(f.RevenueEma); w.Int(f.LossDays);
            }
            w.IntDictionary(sizedJobs, w.Int);

            // Lieferungen
            w.Int(nextShipmentId); w.Int(nextTripId);
            w.Int(shipmentsToday); w.Int(trucksToday); w.Int(deliveredToday); w.Int(truckFailedToday); w.Int(truckSkippedToday);
            w.Int(activeTrucks); w.Int(pendingSpread);
            w.Int(shipments.Count);
            foreach (var s in shipments) WriteShipment(w, s);
            w.Int(pendingTrucks.Count);
            foreach (var s in pendingTrucks) w.Int(s.Id);
            w.IntDictionary(byTrip, s => w.Int(s.Id));

            // Haushalte heute, Statistik, Handel
            w.Int(purchasesToday); w.Int(successfulToday);
            w.Floats(tierWantedToday); w.Floats(tierBoughtToday);
            var st = Stats;
            w.Int(st.Firms); w.Int(st.Extraction); w.Int(st.Processing); w.Int(st.Manufacturing); w.Int(st.Retail); w.Int(st.Office);
            w.Int(st.ActiveShipments); w.Int(st.ActiveTrucks); w.Int(st.ShipmentsYesterday); w.Int(st.TrucksYesterday);
            w.Int(st.DeliveredYesterday); w.Int(st.TruckFailedYesterday); w.Int(st.PlaceholderYesterday); w.Int(st.LostShipments);
            w.Int(st.Bankruptcies); w.Int(st.PurchasesYesterday); w.Int(st.SuccessfulPurchasesYesterday);
            w.Floats(st.TierWantedYesterday); w.Floats(st.TierBoughtYesterday); w.Double(st.TotalProfitYesterday);
            WriteTrade(w, TradeYesterday);
            WriteTrade(w, TradeTotal);
        }

        public void Read(SaveReader r, int version)
        {
            rng.ReadState(r);
            lastCityVersion = r.Int();
            harvestDay = r.Int();
            harvestToday = r.Float();
            Version = r.Int();
            AbandonBankruptBuildings = r.Bool();
            DepositSampleSpacing = r.Float();
            var demand = r.Floats() ?? new float[0];
            Array.Clear(dayDemand, 0, dayDemand.Length);

            // Märkte über die Güter-Id (neue Güter behalten ihren Startzustand)
            int mc = r.Count(8);
            var savedIndex = new int[mc];
            for (int i = 0; i < mc; i++)
            {
                string id = r.String();
                var g = GoodsCatalog.Get(id);
                savedIndex[i] = g?.Index ?? -1;
                var m = g != null ? markets[g.Index] : new GoodMarket(GoodsCatalog.All[0]); // unbekanntes Gut → verwerfen
                m.LocalPrice = r.Float(); m.WorldPrice = r.Float(); m.Stock = r.Float(); m.TargetStock = r.Float();
                m.DemandPerDay = r.Float(); m.SupplyPerDay = r.Float(); m.CapacityPerDay = r.Float(); m.PlannedDemandPerDay = r.Float();
                ReadDay(r, m.Today);
                ReadDay(r, m.Yesterday);
                m.TotalImportValue = r.Double(); m.TotalExportValue = r.Double(); m.TotalImported = r.Float(); m.TotalExported = r.Float();
            }
            for (int i = 0; i < demand.Length && i < mc; i++)
                if (savedIndex[i] >= 0) dayDemand[savedIndex[i]] = demand[i];

            // Firmen
            firms.Clear();
            byBuilding.Clear();
            int fc = r.Count(40);
            for (int i = 0; i < fc; i++)
            {
                int bid = r.Int();
                var b = city.GetBuilding(bid) ?? throw new SaveFormatException($"Firma: Gebäude {bid} fehlt.");
                string rid = r.String();
                var recipe = RecipeCatalog.Get(rid) ?? throw new SaveFormatException($"Unbekanntes Rezept „{rid}“.");
                var f = new Firm(b, recipe, r.Int())
                {
                    Status = r.Name<FirmStatus>(),
                    AgeDays = r.Int(),
                };
                ReadGoods(r, f.InputStock, savedIndex); ReadGoods(r, f.InputCapacity, savedIndex); ReadGoods(r, f.Incoming, savedIndex);
                ReadGoods(r, f.OutputStock, savedIndex); ReadGoods(r, f.OutputCapacity, savedIndex);
                var values = r.Doubles() ?? new double[0];
                for (int k = 0; k < values.Length && k < mc; k++) if (savedIndex[k] >= 0) f.InputValue[savedIndex[k]] = values[k];
                f.Productivity = r.Float(); f.SkillCoverage = r.Float(); f.DepositQuality = r.Float();
                f.Cash = r.Double();
                ReadLedger(r, f.Today);
                ReadLedger(r, f.Yesterday);
                f.ProfitEma = r.Double(); f.RevenueEma = r.Double(); f.LossDays = r.Int();
                if (byBuilding.ContainsKey(bid)) throw new SaveFormatException($"Firma {bid} doppelt.");
                firms.Add(f);
                byBuilding[bid] = f;
            }
            r.IntDictionaryInto(sizedJobs, r.Int, 4);

            // Lieferungen
            nextShipmentId = r.Int(); nextTripId = r.Int();
            shipmentsToday = r.Int(); trucksToday = r.Int(); deliveredToday = r.Int(); truckFailedToday = r.Int(); truckSkippedToday = r.Int();
            activeTrucks = r.Int(); pendingSpread = r.Int();
            shipments.Clear();
            var byId = new Dictionary<int, Shipment>();
            int sc = r.Count(60);
            for (int i = 0; i < sc; i++)
            {
                var s = ReadShipment(r, savedIndex);
                shipments.Add(s);
                byId[s.Id] = s; // Id 0 kommt nicht vor (vergeben beim Abschicken)
            }
            Shipment Find(int id) => byId.TryGetValue(id, out var s) ? s : throw new SaveFormatException($"Lieferung {id} fehlt.");
            pendingTrucks.Clear();
            int pc = r.Count(4);
            for (int i = 0; i < pc; i++) pendingTrucks.Add(Find(r.Int()));
            byTrip.Clear();
            int tc = r.Count(8);
            for (int i = 0; i < tc; i++) { int trip = r.Int(); byTrip[trip] = Find(r.Int()); }

            purchasesToday = r.Int(); successfulToday = r.Int();
            r.FloatsInto(tierWantedToday); r.FloatsInto(tierBoughtToday);
            var st = Stats;
            st.Firms = r.Int(); st.Extraction = r.Int(); st.Processing = r.Int(); st.Manufacturing = r.Int(); st.Retail = r.Int(); st.Office = r.Int();
            st.ActiveShipments = r.Int(); st.ActiveTrucks = r.Int(); st.ShipmentsYesterday = r.Int(); st.TrucksYesterday = r.Int();
            st.DeliveredYesterday = r.Int(); st.TruckFailedYesterday = r.Int(); st.PlaceholderYesterday = r.Int(); st.LostShipments = r.Int();
            st.Bankruptcies = r.Int(); st.PurchasesYesterday = r.Int(); st.SuccessfulPurchasesYesterday = r.Int();
            r.FloatsInto(st.TierWantedYesterday); r.FloatsInto(st.TierBoughtYesterday); st.TotalProfitYesterday = r.Double();
            ReadTrade(r, TradeYesterday);
            ReadTrade(r, TradeTotal);
        }

        /// <summary>Lager je Gut über den gespeicherten Index des Guts (Katalog darf wachsen).</summary>
        static void ReadGoods(SaveReader r, float[] target, int[] savedIndex)
        {
            var a = r.Floats() ?? new float[0];
            Array.Clear(target, 0, target.Length);
            for (int k = 0; k < a.Length && k < savedIndex.Length; k++)
                if (savedIndex[k] >= 0) target[savedIndex[k]] = a[k];
        }

        static void WriteDay(SaveWriter w, MarketDay d)
        {
            w.Float(d.Produced); w.Float(d.Consumed); w.Float(d.HouseholdBought); w.Float(d.HouseholdWanted); w.Float(d.LocalTraded);
            w.Float(d.Imported); w.Float(d.Exported); w.Double(d.ImportValue); w.Double(d.ExportValue); w.Float(d.Spoiled);
        }

        static void ReadDay(SaveReader r, MarketDay d)
        {
            d.Produced = r.Float(); d.Consumed = r.Float(); d.HouseholdBought = r.Float(); d.HouseholdWanted = r.Float(); d.LocalTraded = r.Float();
            d.Imported = r.Float(); d.Exported = r.Float(); d.ImportValue = r.Double(); d.ExportValue = r.Double(); d.Spoiled = r.Float();
        }

        static void WriteLedger(SaveWriter w, FirmLedger l)
        {
            w.Double(l.Revenue); w.Double(l.InputCost); w.Double(l.Wages); w.Double(l.Transport); w.Double(l.Purchases);
            w.Float(l.Produced); w.Float(l.Sold); w.Float(l.Exported); w.Float(l.Imported);
        }

        static void ReadLedger(SaveReader r, FirmLedger l)
        {
            l.Revenue = r.Double(); l.InputCost = r.Double(); l.Wages = r.Double(); l.Transport = r.Double(); l.Purchases = r.Double();
            l.Produced = r.Float(); l.Sold = r.Float(); l.Exported = r.Float(); l.Imported = r.Float();
        }

        static void WriteTrade(SaveWriter w, TradeBalance t)
        {
            w.Double(t.ImportValue); w.Double(t.ExportValue); w.Float(t.ImportedUnits); w.Float(t.ExportedUnits);
        }

        static void ReadTrade(SaveReader r, TradeBalance t)
        {
            t.ImportValue = r.Double(); t.ExportValue = r.Double(); t.ImportedUnits = r.Float(); t.ExportedUnits = r.Float();
        }

        static void WriteShipment(SaveWriter w, Shipment s)
        {
            w.Int(s.Id);
            w.Name(s.Kind);
            w.Bool(s.IsContract); // Etappe 8: Vertragsware aus dem Grenzlager
            w.String(s.Good.Id);
            w.Float(s.Amount);
            w.Int(s.FromBuildingId); w.Int(s.ToBuildingId); w.Int(s.ConnectionNodeId);
            w.Vector2(s.From); w.Vector2(s.To);
            w.Double(s.Value); w.Double(s.TransportCost);
            w.Int(s.PayerBuildingId);
            w.Long(s.DispatchTick); w.Long(s.ArriveTick);
            w.Bool(s.IsTruck); w.Int(s.TripId); w.Float(s.StartTrafficTime);
            w.Long(s.LaunchTick); w.Int(s.FallbackTicks);
            WriteNode(w, s.FromNode); WriteNode(w, s.ToNode);
            w.Double(s.TruckEstimate);
            w.Bool(s.Done);
        }

        static void WriteNode(SaveWriter w, Roads.RoadNode n)
        {
            w.Int(n?.Id ?? 0);
            if (n != null) { w.Vector2(n.Position); w.Float(n.Height); }
        }

        /// <summary>Knoten des Außenanschlusses; inzwischen abgerissen → losgelöster Platzhalter mit derselben Id (wie im laufenden Spiel).</summary>
        Roads.RoadNode ReadNode(SaveReader r)
        {
            int id = r.Int();
            if (id == 0) return null;
            var p = r.Vector2();
            float h = r.Float();
            return network.GetNode(id) ?? new Roads.RoadNode(id, p, h);
        }

        Shipment ReadShipment(SaveReader r, int[] savedIndex)
        {
            var s = new Shipment { Id = r.Int(), Kind = r.Name<ShipmentKind>(), IsContract = r.Bool() };
            string gid = r.String();
            s.Good = GoodsCatalog.Get(gid) ?? throw new SaveFormatException($"Unbekanntes Gut „{gid}“.");
            s.Amount = r.Float();
            s.FromBuildingId = r.Int(); s.ToBuildingId = r.Int(); s.ConnectionNodeId = r.Int();
            s.From = r.Vector2(); s.To = r.Vector2();
            s.Value = r.Double(); s.TransportCost = r.Double();
            s.PayerBuildingId = r.Int();
            s.DispatchTick = r.Long(); s.ArriveTick = r.Long();
            s.IsTruck = r.Bool(); s.TripId = r.Int(); s.StartTrafficTime = r.Float();
            s.LaunchTick = r.Long(); s.FallbackTicks = r.Int();
            s.FromNode = ReadNode(r);
            s.ToNode = ReadNode(r);
            s.TruckEstimate = r.Double();
            s.Done = r.Bool();
            return s;
        }
    }
}
