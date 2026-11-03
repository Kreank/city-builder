using System;
using System.Collections.Generic;
using CityBuilder.Simulation.Goods;
using CityBuilder.Simulation.World;

namespace CityBuilder.Simulation.Trade
{
    public enum TransportMode : byte
    {
        /// <summary>Über Land (Lkw/Bahn) – nur auf derselben Landmasse.</summary>
        Land,
        /// <summary>Zum Hafen, per Schiff, vom Hafen (Vor- und Nachlauf über Land).</summary>
        Sea,
    }

    /// <summary>Handelsweg zwischen zwei Ländern: Strecke, Verkehrsmittel, Kosten je Frachttonne, Lieferzeit.</summary>
    public sealed class TradeRoute
    {
        public int From { get; internal set; }
        public int To { get; internal set; }
        public TransportMode Mode { get; internal set; }
        /// <summary>Großkreis zwischen den Handelsorten (km).</summary>
        public double DirectKm { get; internal set; }
        /// <summary>Strecke über Land (bei See: Vor- + Nachlauf zum/vom Hafen).</summary>
        public double LandKm { get; internal set; }
        public double SeaKm { get; internal set; }
        /// <summary>€ je Frachttonne.</summary>
        public double CostPerTon { get; internal set; }
        /// <summary>Lieferzeit in Tagen (Abfertigung + Fahrt + Hafen).</summary>
        public int LeadDays { get; internal set; }

        /// <summary>Transportkosten je Wareneinheit.</summary>
        public double UnitCost(GoodType g) => CostPerTon * TradeGoods.FreightTons(g);

        /// <summary>Transportkosten je Kategorie-Einheit (Länder untereinander).</summary>
        public double CategoryUnitCost(GoodsCategory c) => CostPerTon * TradeGoods.CategoryFreightTons(c);

        public string ModeText => Mode == TransportMode.Sea ? "Seeweg" : "Landweg";
        public override string ToString() => $"{ModeText} {LandKm + SeaKm:0} km · {LeadDays} Tage · {CostPerTon:0.0} €/t";
    }

    /// <summary>
    /// Handelswege zwischen allen Ländern aus der Weltkarte (<see cref="Planet"/>): Handelsort = Hauptstadt (Spieler: Mitte
    /// der Startregion). Landweg, wenn beide Orte auf derselben Landmasse liegen: Großkreis × 1,35 × 0,03 €/t·km. Seeweg über
    /// den nächsten Küstenpunkt jedes Landes (Breitensuche über Land): Vorlauf + Großkreis Hafen–Hafen × 1,25 × 0,004 €/t·km +
    /// Nachlauf + 2 × 4 €/t Umschlag. Gewählt wird der billigere Weg. Lieferzeit = ⌈1 + Land/600 + See/500 (+2 Hafentage)⌉.
    /// Einmal beim Start berechnet (36 Länder: 1 296 Wege).
    /// </summary>
    public sealed class TradeGeography
    {
        const double EarthRadiusKm = Planet.EarthCircumferenceKm / (2.0 * Math.PI);

        readonly Planet planet;
        readonly TradeRoute[,] routes;
        readonly int[] location, port, landmass;
        readonly double[] portKm;

        public int Count { get; }

        public TradeGeography(Planet planet)
        {
            this.planet = planet ?? throw new ArgumentNullException(nameof(planet));
            Count = planet.Countries.Count;
            location = new int[Count];
            port = new int[Count];
            portKm = new double[Count];
            landmass = Landmasses(planet);
            for (int c = 0; c < Count; c++)
            {
                var country = planet.Countries[c];
                location[c] = country.IsPlayer && planet.StartRegion != null ? planet.StartRegion.CenterCell : country.CapitalCell;
                (port[c], portKm[c]) = NearestCoast(planet, location[c]);
            }
            routes = new TradeRoute[Count, Count];
            for (int a = 0; a < Count; a++)
                for (int b = 0; b < Count; b++)
                    if (a != b) routes[a, b] = Build(a, b);
        }

        /// <summary>Handelsort eines Landes (Weltzelle).</summary>
        public int LocationOf(int country) => location[country];
        /// <summary>Hafen (nächste Küstenzelle) und Entfernung dorthin (km).</summary>
        public int PortOf(int country) => port[country];
        public double PortDistanceKm(int country) => portKm[country];

        /// <summary>Handelsweg von → nach (null für dasselbe Land).</summary>
        public TradeRoute Route(int from, int to) => from == to ? null : routes[from, to];

        public double DistanceKm(int cellA, int cellB)
        {
            var u = planet.UnitVector(cellA);
            var v = planet.UnitVector(cellB);
            double dot = Math.Clamp(System.Numerics.Vector3.Dot(u, v), -1f, 1f);
            return Math.Acos(dot) * EarthRadiusKm;
        }

        TradeRoute Build(int a, int b)
        {
            double direct = DistanceKm(location[a], location[b]);
            var r = new TradeRoute { From = a, To = b, DirectKm = direct };
            double landCost = double.MaxValue;
            bool sameLand = landmass[location[a]] >= 0 && landmass[location[a]] == landmass[location[b]];
            if (sameLand) landCost = direct * TradeRules.LandDetour * TradeRules.LandCostPerTonKm;

            double seaKm = DistanceKm(port[a], port[b]) * TradeRules.SeaDetour;
            double feeder = portKm[a] + portKm[b];
            double seaCost = feeder * TradeRules.LandCostPerTonKm + seaKm * TradeRules.SeaCostPerTonKm + 2.0 * TradeRules.PortHandlingPerTon;

            if (landCost <= seaCost)
            {
                r.Mode = TransportMode.Land;
                r.LandKm = direct * TradeRules.LandDetour;
                r.CostPerTon = landCost;
                r.LeadDays = (int)Math.Ceiling(1.0 + r.LandKm / TradeRules.LandKmPerDay);
            }
            else
            {
                r.Mode = TransportMode.Sea;
                r.LandKm = feeder;
                r.SeaKm = seaKm;
                r.CostPerTon = seaCost;
                r.LeadDays = (int)Math.Ceiling(3.0 + feeder / TradeRules.LandKmPerDay + seaKm / TradeRules.SeaKmPerDay);
            }
            r.LeadDays = Math.Max(1, r.LeadDays);
            return r;
        }

        /// <summary>Zusammenhängende Landmassen (8er-Nachbarschaft); Meer = −1.</summary>
        static int[] Landmasses(Planet p)
        {
            var id = new int[p.CellCount];
            for (int i = 0; i < id.Length; i++) id[i] = -1;
            var stack = new Stack<int>();
            int next = 0;
            for (int i = 0; i < id.Length; i++)
            {
                if (id[i] >= 0 || !p.IsLand(i)) continue;
                id[i] = next;
                stack.Push(i);
                while (stack.Count > 0)
                {
                    int c = stack.Pop();
                    for (int k = 0; k < 8; k++)
                    {
                        int j = p.Neighbor(c, Planet.Dx8[k], Planet.Dy8[k]);
                        if (j < 0 || id[j] >= 0 || !p.IsLand(j)) continue;
                        id[j] = next;
                        stack.Push(j);
                    }
                }
                next++;
            }
            return id;
        }

        /// <summary>Nächste Küstenzelle (Land am Meer) per Breitensuche über Land; Entfernung als Großkreis × Umweg.</summary>
        (int cell, double km) NearestCoast(Planet p, int start)
        {
            if (!p.IsLand(start)) return (start, 0);
            if (p.CoastDistance[start] <= 1) return (start, 0);
            var seen = new HashSet<int> { start };
            var queue = new Queue<int>();
            queue.Enqueue(start);
            while (queue.Count > 0)
            {
                int c = queue.Dequeue();
                for (int k = 0; k < 8; k++)
                {
                    int j = p.Neighbor(c, Planet.Dx8[k], Planet.Dy8[k]);
                    if (j < 0 || !seen.Add(j)) continue;
                    if (!p.IsLand(j)) continue;
                    if (p.CoastDistance[j] <= 1) return (j, DistanceKm(start, j) * TradeRules.LandDetour);
                    queue.Enqueue(j);
                }
            }
            return (start, 0);
        }
    }
}
