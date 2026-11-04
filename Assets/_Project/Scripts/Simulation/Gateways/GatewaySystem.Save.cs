using System.Collections.Generic;
using CityBuilder.Simulation.Persistence;

namespace CityBuilder.Simulation.Gateways
{
    /// <summary>
    /// Spielstand „gateways“: alle Anschlüsse (Art, Lage, Ziel, Knoten, Zähler), Autobahn/Anschlussstelle/Zubringer,
    /// Bahnkorridor, Zuzugs-/Verkehrszählung. Die Straßen selbst stehen im Abschnitt „roads“ (Flag <c>Locked</c>).
    /// Abgeleitet und nicht gespeichert: Handelspartner-Zwischenspeicher je Tag, Wohngebäude-Zählung für den Hinweis.
    /// </summary>
    public sealed partial class GatewaySystem : ISaveable
    {
        public string SaveId => "gateways";
        /// <summary>v2: Mittellinie der (geschwungenen) Bahntrasse; v1 → gerade Linie zwischen den Enden.</summary>
        public int SaveVersion => 2;

        public void Write(SaveWriter w)
        {
            w.Int(nextId);
            w.Int(HighwayNumber);
            w.Float(HighwayAngle);
            w.Vector2(Interchange);
            w.List(feederEnds, w.Vector2);
            w.Bool(HasRailCorridor);
            w.Vector2(RailStart);
            w.Vector2(RailEnd);
            w.Int(BlockedMoveInsToday);
            w.Int(BlockedMoveInsYesterday);
            w.Int(lastShipmentId);
            w.List(gateways, g =>
            {
                w.Int(g.Id);
                w.Name(g.Kind);
                w.Name(g.Status);
                w.String(g.Name);
                w.String(g.Route);
                w.Bool(g.HasLocation);
                w.Vector2(g.Position);
                w.Vector2(g.Direction);
                w.Name(g.TargetKind);
                w.String(g.TargetName);
                w.Int(g.TargetCountryId);
                w.Int(g.TargetRegionId);
                w.Float(g.Importance);
                w.Float(g.Capacity);
                w.Int(g.EntryNodeId);
                w.Int(g.ExitNodeId);
                w.Int(g.PartnerId);
                w.Float(g.WaterWidth);
                w.Float(g.WaterDepth);
                w.Bool(g.Navigable);
                w.Int(g.TotalMoveIns);
                WriteStats(w, g.Today);
                WriteStats(w, g.Yesterday);
            });
            WriteSeen(w, seenIn);
            WriteSeen(w, seenOut);
            w.List(railPath, w.Vector2); // v2
        }

        static void WriteStats(SaveWriter w, GatewayDayStats s)
        {
            w.Int(s.VehiclesIn); w.Int(s.VehiclesOut); w.Int(s.MoveIns);
            w.Int(s.Imports); w.Int(s.Exports); w.Double(s.ImportValue); w.Double(s.ExportValue);
        }

        static void ReadStats(SaveReader r, GatewayDayStats s)
        {
            s.VehiclesIn = r.Int(); s.VehiclesOut = r.Int(); s.MoveIns = r.Int();
            s.Imports = r.Int(); s.Exports = r.Int(); s.ImportValue = r.Double(); s.ExportValue = r.Double();
        }

        static void WriteSeen(SaveWriter w, Dictionary<int, HashSet<int>> seen)
        {
            var keys = new List<int>(seen.Keys);
            keys.Sort();
            w.Int(keys.Count);
            foreach (int k in keys)
            {
                w.Int(k);
                var ids = new List<int>(seen[k]);
                ids.Sort();
                w.IntList(ids);
            }
        }

        static void ReadSeen(SaveReader r, Dictionary<int, HashSet<int>> seen)
        {
            seen.Clear();
            int n = r.Count(8);
            for (int i = 0; i < n; i++)
            {
                int k = r.Int();
                seen[k] = new HashSet<int>(r.IntList());
            }
        }

        public void Read(SaveReader r, int version)
        {
            nextId = r.Int();
            HighwayNumber = r.Int();
            HighwayAngle = r.Float();
            Interchange = r.Vector2();
            feederEnds.Clear();
            feederEnds.AddRange(r.List(r.Vector2, 8));
            HasRailCorridor = r.Bool();
            RailStart = r.Vector2();
            RailEnd = r.Vector2();
            BlockedMoveInsToday = r.Int();
            BlockedMoveInsYesterday = r.Int();
            lastShipmentId = r.Int();
            gateways.Clear();
            gateways.AddRange(r.List(() =>
            {
                var g = new Gateway
                {
                    Id = r.Int(),
                    Kind = r.Name<GatewayKind>(),
                    Status = r.Name<GatewayStatus>(),
                    Name = r.String() ?? "",
                    Route = r.String() ?? "",
                    HasLocation = r.Bool(),
                    Position = r.Vector2(),
                    Direction = r.Vector2(),
                    TargetKind = r.Name<GatewayTargetKind>(),
                    TargetName = r.String() ?? "",
                    TargetCountryId = r.Int(),
                    TargetRegionId = r.Int(),
                    Importance = r.Float(),
                    Capacity = r.Float(),
                    EntryNodeId = r.Int(),
                    ExitNodeId = r.Int(),
                    PartnerId = r.Int(),
                    WaterWidth = r.Float(),
                    WaterDepth = r.Float(),
                    Navigable = r.Bool(),
                    TotalMoveIns = r.Int(),
                };
                ReadStats(r, g.Today);
                ReadStats(r, g.Yesterday);
                return g;
            }, 16));
            ReadSeen(r, seenIn);
            ReadSeen(r, seenOut);
            if (version >= 2) SetRailPath(r.List(r.Vector2, 8));
            else if (HasRailCorridor) SetRailPath(new[] { RailStart, RailEnd }); // alte Stände: gerader Korridor
            else SetRailPath(System.Array.Empty<System.Numerics.Vector2>());
            RailPieces = null;
            HighwayCenterline = null;
            partnerDay = int.MinValue;
        }
    }
}
