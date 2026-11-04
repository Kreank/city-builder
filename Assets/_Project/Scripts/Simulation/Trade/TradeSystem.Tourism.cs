using System;
using CityBuilder.Simulation.World;

namespace CityBuilder.Simulation.Trade
{
    /// <summary>
    /// Handel 2 – Tourismus als Dienstleistungshandel (Kategorie <see cref="GoodsCategory.Tourism"/>, ohne Gut): Jedes Land
    /// verbringt <see cref="DiplomacyRules.OutboundShare"/> seines Tourismus-Haushaltsbedarfs im Ausland. Die Ziele bekommen
    /// Gewichte aus Anziehung (Tourismus-Stärke^1,5), Entfernung (e^(−km/3000)), Beziehung, Preisniveau ((Preis Herkunft /
    /// Preis Ziel)^0,6) und Bündnis (×1,25); Sanktion/Embargo = 0. Ein Ziel bedient je Tag höchstens 25 % seines Lagers über
    /// 10 Tagen Eigenbedarf. Wirkung: Tourismus-Lager wandert vom Ziel zur Herkunft (Dienstleistung „exportiert“), Wert =
    /// Menge × Tourismuspreis des Ziels (Ausfuhr/Einfuhr der Länder, Handelsvolumen).
    /// <para>
    /// <b>Spielerstadt</b> (mit <see cref="PlayerTourismPotential"/>): Besucher je Tag = Potenzial der Stadt (Flughafen-Formel:
    /// Tourismus-Stärke × Attraktivität × Stadtgröße × Umwelt) × Marktfaktor (Beziehungen/Preise/Bündnis der Herkunftsländer gegen
    /// neutral, 0,25–1,6), aufgeteilt auf die Herkunftsländer. Anreise: bis 1 500 km über Land per Bahn (Personenbahnhof) bzw. Straße,
    /// weiter per Flugzeug (Flughafen); ohne passendes Verkehrsmittel kommt die Hälfte per Schiff/langer Autofahrt oder niemand.
    /// Flugtouristen gehen als Nachfrage an den Flughafen (<c>AviationSystem.TouristDemandSource</c>), bodengebundene Besucher
    /// direkt in die Besucherliste (<see cref="CityGroundVisitors"/>) – Ausgaben, Kurtaxe und Ladenumsatz wie bisher.
    /// </para>
    /// </summary>
    public sealed partial class TradeSystem
    {
        double[,] tourismDistance, tourismFlow;
        double[] tourismBuffer, tourismInbound, playerVisitorsFrom;
        readonly double[] visitorsByMode = new double[5];
        double groundRemainder;

        /// <summary>Besucher-Potenzial der Spielerstadt je Tag (null = der Spieler zählt wie ein NPC-Land, z. B. in reinen Handelstests).</summary>
        public Func<double> PlayerTourismPotential { get; set; }
        /// <summary>Wie Touristen die Stadt erreichen (null = alles möglich).</summary>
        public Func<TourismAccess> CityTourismAccess { get; set; }
        /// <summary>Bodengebundene Besucher, die heute ankommen (ganze Personen) – die Stadt nimmt sie in die Besucherliste auf.</summary>
        public Action<int> CityGroundVisitors { get; set; }

        /// <summary>Touristen je Tag, die per Flugzeug kommen wollen (Nachfrage des Flughafens).</summary>
        public double CityAirTourists { get; private set; }
        /// <summary>Marktfaktor der Stadt gestern (Beziehungen, Preise, Bündnis der Herkunftsländer; 1 = neutral).</summary>
        public double PlayerTourismMarket { get; private set; } = 1.0;
        /// <summary>Besucher-Potenzial der Stadt gestern (vor Marktfaktor und Anreise).</summary>
        public double PlayerTourismPotentialYesterday { get; private set; }

        /// <summary>Besucher der Stadt gestern je Anreise (Lost = wollten kommen, aber kein Verkehrsmittel).</summary>
        public double VisitorsByMode(TourismMode m) => visitorsByMode[(int)m];
        /// <summary>Besucher der Stadt gestern aus einem Land.</summary>
        public double PlayerVisitorsFrom(int country) => country >= 0 && country < playerVisitorsFrom.Length ? playerVisitorsFrom[country] : 0.0;
        /// <summary>Tourismusfluss gestern (Kategorie-Einheiten) von Herkunft nach Ziel.</summary>
        public double TourismFlow(int origin, int destination) => tourismFlow[origin, destination];

        void AllocateTourism(int n)
        {
            tourismDistance = new double[n, n];
            tourismFlow = new double[n, n];
            tourismBuffer = new double[n];
            tourismInbound = new double[n];
            playerVisitorsFrom = new double[n];
            for (int a = 0; a < n; a++)
                for (int b = 0; b < n; b++)
                {
                    var r = a == b ? null : Geography.Route(a, b);
                    tourismDistance[a, b] = r != null ? Math.Exp(-r.DirectKm / DiplomacyRules.TourismDistanceKm) : 0.0;
                }
        }

        static double TourismAppeal(Country c) => 0.05 + Math.Pow(Math.Clamp(c.Profile.Raw[(int)Strength.Tourism], 0f, 1f), 1.5);

        TradeBloc[] tourismBloc;
        double[] tourismAppeal;

        /// <summary>Reiselust von o nach d ohne Anziehung (Entfernung, Beziehung, Preis, Bündnis, Sanktion). Bündnisse aus dem Tages-Cache.</summary>
        double TravelFactor(int o, int d)
        {
            double dist = tourismDistance[o, d];
            if (dist <= 0) return 0.0;
            var bo = tourismBloc[o];
            var bd = tourismBloc[d];
            if ((bo != null && bo.sanctioned.Contains(d)) || (bd != null && bd.sanctioned.Contains(o))) return 0.0;
            float rel = relation[o, d];
            if (rel < TradeRules.RelationEmbargo) return 0.0;
            int T = (int)GoodsCategory.Tourism;
            double relF = Math.Clamp(1.0 + rel / 150.0, 0.2, 1.7);
            double po = nations[o].Economy.Price[T], pd = nations[d].Economy.Price[T];
            double priceF = pd > 0 ? Math.Clamp(Math.Pow(po / pd, DiplomacyRules.TourismPriceElasticity), 0.5, 2.0) : 1.0;
            double blocF = bo != null && bo == bd ? DiplomacyRules.TourismBlocBonus : 1.0;
            return dist * relF * priceF * blocF;
        }

        void TourismDay()
        {
            int n = nations.Count, T = (int)GoodsCategory.Tourism;
            bool cityHook = PlayerTourismPotential != null && PlayerId >= 0;
            if (tourismBloc == null) { tourismBloc = new TradeBloc[n]; tourismAppeal = new double[n]; }
            for (int i = 0; i < n; i++)
            {
                var b = BlocOf(i);
                tourismBloc[i] = b != null && b.Active ? b : null;
                tourismAppeal[i] = TourismAppeal(nations[i].Country);
            }
            Array.Clear(tourismInbound, 0, n);
            for (int o = 0; o < n; o++)
            {
                var eo = nations[o].Economy;
                double outbound = eo.Population * GoodsCategories.PerCapitaDemand[T] * DiplomacyRules.OutboundShare;
                double sum = 0;
                for (int d = 0; d < n; d++)
                {
                    tourismFlow[o, d] = 0;
                    double w = 0;
                    if (d != o && !(cityHook && IsPlayer(d))) w = tourismAppeal[d] * TravelFactor(o, d);
                    tourismBuffer[d] = w;
                    sum += w;
                }
                if (!(outbound > 0) || sum <= 0) continue;
                for (int d = 0; d < n; d++)
                {
                    if (tourismBuffer[d] <= 0) continue;
                    double f = outbound * tourismBuffer[d] / sum;
                    tourismFlow[o, d] = f;
                    tourismInbound[d] += f;
                }
            }
            // Kapazität der Ziele
            for (int d = 0; d < n; d++)
            {
                var ed = nations[d].Economy;
                double avail = Math.Max(0.0, ed.Stock[T] - DiplomacyRules.TourismReserveDays * ed.Demand[T]) * DiplomacyRules.TourismCapacityShare;
                tourismBuffer[d] = tourismInbound[d] > avail ? (tourismInbound[d] > 0 ? avail / tourismInbound[d] : 0) : 1.0;
            }
            foreach (var nt in nations) { nt.TourismInYesterday = 0; nt.TourismOutYesterday = 0; }
            for (int o = 0; o < n; o++)
                for (int d = 0; d < n; d++)
                {
                    double f = tourismFlow[o, d] * tourismBuffer[d];
                    tourismFlow[o, d] = f;
                    if (f <= 0) continue;
                    var ed = nations[d].Economy;
                    var eo = nations[o].Economy;
                    ed.Stock[T] = Math.Max(0.0, ed.Stock[T] - f);
                    eo.Stock[T] += f;
                    BookTourism(o, d, f, ed.Price[T]);
                }
            if (cityHook) PlayerTourism();
            else
            {
                Array.Clear(playerVisitorsFrom, 0, n);
                Array.Clear(visitorsByMode, 0, visitorsByMode.Length);
                CityAirTourists = 0;
            }
        }

        void BookTourism(int o, int d, double units, double price)
        {
            int T = (int)GoodsCategory.Tourism;
            double value = units * price;
            var no = nations[o];
            var nd = nations[d];
            nd.TradedToday[T] += units;
            no.TradedToday[T] += units;
            nd.ExportValue += value; nd.TourismExportValue += value; nd.TourismInYesterday += units;
            no.ImportValue += value; no.TourismImportValue += value; no.TourismOutYesterday += units;
        }

        void PlayerTourism()
        {
            int n = nations.Count, p = PlayerId, T = (int)GoodsCategory.Tourism;
            double potential = Math.Max(0.0, PlayerTourismPotential());
            PlayerTourismPotentialYesterday = potential;
            var access = CityTourismAccess?.Invoke() ?? TourismAccess.All;
            double sum = 0, neutral = 0;
            for (int o = 0; o < n; o++)
            {
                tourismBuffer[o] = 0;
                if (o == p) continue;
                double size = Math.Sqrt(Math.Max(1.0, nations[o].Economy.Population));
                double w = size * TravelFactor(o, p);
                tourismBuffer[o] = w;
                sum += w;
                neutral += size * tourismDistance[o, p];
            }
            PlayerTourismMarket = neutral > 0 ? Math.Clamp(sum / neutral, DiplomacyRules.MarketFactorMin, DiplomacyRules.MarketFactorMax) : 1.0;
            double visitors = potential * PlayerTourismMarket;
            Array.Clear(visitorsByMode, 0, visitorsByMode.Length);
            double price = nations[p].Economy.Price[T];
            for (int o = 0; o < n; o++)
            {
                playerVisitorsFrom[o] = 0;
                if (o == p || tourismBuffer[o] <= 0 || sum <= 0) continue;
                double v = visitors * tourismBuffer[o] / sum;
                var route = Geography.Route(o, p);
                bool land = route != null && route.Mode == TransportMode.Land;
                double km = route?.DirectKm ?? double.MaxValue;
                TourismMode mode;
                double share = 1.0;
                if (land && km <= DiplomacyRules.GroundTourismKm)
                    mode = access.Rail ? TourismMode.Rail : access.Road ? TourismMode.Road : access.Air ? TourismMode.Air : TourismMode.Lost;
                else if (access.Air) mode = TourismMode.Air;
                else if (!land && access.Sea) { mode = TourismMode.Ship; share = DiplomacyRules.DetourShare; }
                else if (land && access.Road) { mode = TourismMode.Road; share = DiplomacyRules.DetourShare; }
                else mode = TourismMode.Lost;
                if (mode == TourismMode.Lost) { visitorsByMode[(int)TourismMode.Lost] += v; continue; }
                double come = v * share;
                visitorsByMode[(int)mode] += come;
                visitorsByMode[(int)TourismMode.Lost] += v - come;
                playerVisitorsFrom[o] = come;
                double units = come * DiplomacyRules.UnitsPerVisitor;
                nations[o].Economy.Stock[T] += units; // Urlaub im Ausland deckt den Tourismusbedarf der Herkunft
                BookTourism(o, p, units, price);
            }
            CityAirTourists = visitorsByMode[(int)TourismMode.Air];
            double ground = visitorsByMode[(int)TourismMode.Road] + visitorsByMode[(int)TourismMode.Rail] + visitorsByMode[(int)TourismMode.Ship];
            groundRemainder += ground;
            int arrive = (int)Math.Floor(groundRemainder);
            groundRemainder -= arrive;
            if (arrive > 0 && CityGroundVisitors != null)
            {
                CityGroundVisitors(arrive);
                GroundVisitorsTotal += arrive;
            }
        }

        /// <summary>Bodengebundene Besucher seit Beginn (in die Besucherliste der Stadt übergeben).</summary>
        public long GroundVisitorsTotal { get; private set; }

        /// <summary>Besucher der Stadt gestern insgesamt (ohne „verloren“).</summary>
        public double PlayerVisitorsYesterday =>
            visitorsByMode[(int)TourismMode.Road] + visitorsByMode[(int)TourismMode.Rail] + visitorsByMode[(int)TourismMode.Ship] + visitorsByMode[(int)TourismMode.Air];
    }
}
