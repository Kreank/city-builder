using System;
using System.Collections.Generic;
using CityBuilder.Simulation.Core;
using CityBuilder.Simulation.Persistence;

namespace CityBuilder.Simulation.Shipping
{
    /// <summary>
    /// Spielstand „shipping“: Häfen mit Statistik, Schiffe vollständig (Rinne über Art/Hafen/Gateways, Richtung, Bug-Lage,
    /// Tempo, Zustand, Liegeplatz, Liegezeit, Ladung), Zustand der Klappbrücken (über die Lage), Zähler, offene Gebühren.
    /// Abgeleitet (nach dem Laden neu): Navigationsraster und Durchgangsrinnen (natürliches Gelände beim Erzeugen), Hafenrinnen,
    /// Brücken-Querungen, Klappbrücken, Zufahrten, Gateway-Status.
    /// </summary>
    public sealed partial class ShippingSystem : ISaveable, IAfterLoad
    {
        public string SaveId => "shipping";
        public int SaveVersion => 1;

        public void Write(SaveWriter w)
        {
            w.Int(nextHarborId); w.Int(nextShipId);
            w.Double(pendingFees); w.Double(FeesTotal);
            w.Int(ShipsSpawned); w.Int(ShipsCompleted); w.Int(ShipsCancelled); w.Int(ThroughToday); w.Int(ThroughYesterday);
            w.Int(lastShipmentId);
            WriteCounts(w, blockedToday);
            WriteCounts(w, blockedYesterday);
            w.List(harbors, h =>
            {
                w.Int(h.Id); w.String(h.Type.Id); w.String(h.Name); w.Vector2(h.Position); w.Vector2(h.Front); w.Float(h.Height);
                w.Int(h.CallsToday); w.Int(h.CallsYesterday); w.Int(h.LoadsToday); w.Int(h.LoadsYesterday); w.Int(h.TotalLoads);
                w.Int(h.PendingLoads); w.Long(h.LastCallTick); w.Int(h.CallCounter);
            });
            w.List(ships, s =>
            {
                w.Int(s.Id); w.String(s.Class.Id); w.Name(s.Purpose); w.Name(s.State);
                // Rinne: Art + Hafen + Gateways (Ids der Rinnen sind abgeleitet)
                w.Name(s.Waterway?.Kind ?? WaterwayKind.Through); w.Int(s.Waterway?.HarborId ?? 0);
                w.Int(s.Waterway?.GatewayA ?? 0); w.Int(s.Waterway?.GatewayB ?? 0);
                w.Bool(s.Forward); w.Float(s.S); w.Float(s.PrevS); w.Float(s.Speed);
                w.Int(s.HarborId); w.Int(s.Berth); w.Int(s.FromGatewayId); w.Int(s.ToGatewayId);
                w.Int(s.Loads); w.Int(s.Passengers);
                w.Double(s.SpawnTime); w.Double(s.WaitSince); w.Double(s.DwellUntil); w.Double(s.WaitedSeconds); w.Int(s.WaitingForBridge);
            });
            w.List(drawbridges, d =>
            {
                w.Vector2(d.Position); w.Name(d.State); w.Double(d.StateSince); w.Double(d.RoadOpenSince);
                w.Int(d.OpeningsToday); w.Int(d.OpeningsYesterday); w.Int(d.OpeningsTotal); w.Double(d.ClosedSecondsToday);
            });
        }

        static void WriteCounts(SaveWriter w, Dictionary<string, int> map)
        {
            var keys = new List<string>(map.Keys);
            keys.Sort(StringComparer.Ordinal);
            w.Int(keys.Count);
            foreach (var k in keys) { w.String(k); w.Int(map[k]); }
        }

        static void ReadCounts(SaveReader r, Dictionary<string, int> map)
        {
            map.Clear();
            int n = r.Count(5);
            for (int i = 0; i < n; i++) { string k = r.String() ?? ""; map[k] = r.Int(); }
        }

        // Schiffe warten bis AfterLoad auf ihre Rinne (Hafenrinnen entstehen erst, wenn Gateways und Straßen geladen sind)
        readonly List<(Ship ship, WaterwayKind kind, int harbor, int a, int b)> pendingShips = new List<(Ship, WaterwayKind, int, int, int)>();

        public void Read(SaveReader r, int version)
        {
            nextHarborId = r.Int(); nextShipId = r.Int();
            pendingFees = r.Double(); FeesTotal = r.Double();
            ShipsSpawned = r.Int(); ShipsCompleted = r.Int(); ShipsCancelled = r.Int(); ThroughToday = r.Int(); ThroughYesterday = r.Int();
            lastShipmentId = r.Int();
            ReadCounts(r, blockedToday);
            ReadCounts(r, blockedYesterday);
            harbors.Clear();
            harbors.AddRange(r.List(() =>
            {
                var h = new Harbor { Id = r.Int() };
                string type = r.String();
                h.Type = HarborCatalog.Get(type ?? "") ?? throw new SaveFormatException($"Unbekannter Hafentyp „{type}“.");
                h.Name = r.String() ?? ""; h.Position = r.Vector2(); h.Front = r.Vector2(); h.Height = r.Float();
                h.CallsToday = r.Int(); h.CallsYesterday = r.Int(); h.LoadsToday = r.Int(); h.LoadsYesterday = r.Int(); h.TotalLoads = r.Int();
                h.PendingLoads = r.Int(); h.LastCallTick = r.Long(); h.CallCounter = r.Int();
                return h;
            }, 16));
            ships.Clear();
            pendingShips.Clear();
            foreach (var x in r.List(() =>
            {
                var s = new Ship { Id = r.Int() };
                string cls = r.String();
                s.Class = ShipCatalog.Get(cls ?? "") ?? throw new SaveFormatException($"Unbekannte Schiffsklasse „{cls}“.");
                s.Purpose = r.Name<ShipPurpose>(); s.State = r.Name<ShipState>();
                var kind = r.Name<WaterwayKind>(); int harbor = r.Int(); int ga = r.Int(); int gb = r.Int();
                s.Forward = r.Bool(); s.S = r.Float(); s.PrevS = r.Float(); s.Speed = r.Float();
                s.HarborId = r.Int(); s.Berth = r.Int(); s.FromGatewayId = r.Int(); s.ToGatewayId = r.Int();
                s.Loads = r.Int(); s.Passengers = r.Int();
                s.SpawnTime = r.Double(); s.WaitSince = r.Double(); s.DwellUntil = r.Double(); s.WaitedSeconds = r.Double(); s.WaitingForBridge = r.Int();
                return (s, kind, harbor, ga, gb);
            }, 16))
                pendingShips.Add(x);
            loadedBridgeStates.Clear();
            drawbridges.Clear();
            foreach (var d in r.List(() => new Drawbridge
            {
                Position = r.Vector2(), State = r.Name<DrawbridgeState>(), StateSince = r.Double(), RoadOpenSince = r.Double(),
                OpeningsToday = r.Int(), OpeningsYesterday = r.Int(), OpeningsTotal = r.Int(), ClosedSecondsToday = r.Double(),
            }, 20))
                loadedBridgeStates.Add(d);
            removedShips.Clear();
        }

        public void AfterLoad(SimulationWorld w)
        {
            if (!Active) { ships.Clear(); pendingShips.Clear(); return; }
            // Hafenrinnen neu (alte entfernen), Gateways zuordnen
            foreach (var id in new List<int>(harborWaterways.Keys)) RemoveHarborWaterways(id, false);
            linkedGateways = -1;
            EnsureLinks();
            foreach (var h in harbors) BuildHarborWaterways(h);
            RebuildFloor();
            foreach (var (s, kind, harbor, a, b) in pendingShips)
            {
                Waterway way = null;
                foreach (var wy in waterways)
                    if (wy.Kind == kind && wy.HarborId == harbor && wy.GatewayA == a && wy.GatewayB == b) { way = wy; break; }
                if (way == null) continue; // Rinne gibt es nicht mehr (Hafen abgerissen) – Schiff entfällt
                s.Waterway = way;
                ships.Add(s);
            }
            pendingShips.Clear();
            crossingsDirty = true;
            accessDirty = true;
            EnsureCrossings();
            EnsureAccess();
            UpdateGatewayStatus();
            Version++;
        }
    }
}
