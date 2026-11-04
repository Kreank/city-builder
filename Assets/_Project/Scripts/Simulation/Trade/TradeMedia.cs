using System.Globalization;
using CityBuilder.Simulation.Services;
using CityBuilder.Simulation.Services.Content;

namespace CityBuilder.Simulation.Trade
{
    /// <summary>
    /// Handel → Medien: Vertrag mit dem Spieler („Handelsvertrag mit {Land}“), Embargo (Beziehung unter −50) und Zollerhöhung
    /// eines Landes gegen uns werden als Schlagzeilen gemeldet (erscheinen nur mit Medienhaus in der Stadt, s. <see cref="MediaSystem"/>).
    /// </summary>
    public static class TradeMedia
    {
        public static void Connect(TradeSystem trade, MediaSystem media)
        {
            if (trade == null || media == null) return;
            trade.News += n =>
            {
                switch (n.Kind)
                {
                    case TradeNewsKind.ContractSigned:
                        media.Report(HeadlineTopic.TradeDeal, $"deal{n.Country}-{n.Day}",
                            ("Land", n.CountryName), ("Name", n.Detail), ("Anzahl", ((int)n.Value).ToString(CultureInfo.InvariantCulture)));
                        break;
                    case TradeNewsKind.Embargo:
                        media.Report(HeadlineTopic.Embargo, $"embargo{n.Country}", ("Land", n.CountryName));
                        break;
                    // --- Handel 2
                    case TradeNewsKind.CounterOffer:
                        media.Report(HeadlineTopic.CounterOffer, $"counter{n.Country}-{n.Day}",
                            ("Land", n.CountryName), ("Name", n.Detail ?? "Waren"), ("Anzahl", ((int)n.Value).ToString(CultureInfo.InvariantCulture)));
                        break;
                    case TradeNewsKind.BlocJoined:
                        media.Report(HeadlineTopic.Alliance, $"bloc{n.Detail}-{n.Day}", ("Land", n.CountryName), ("Name", $"Gemeinsam in {n.Detail}"));
                        break;
                    case TradeNewsKind.BlocInvitation:
                        media.Report(HeadlineTopic.Alliance, $"blocinv{n.Detail}-{n.Day / 7}", ("Land", n.CountryName), ("Name", $"Einladung in {n.Detail}"));
                        break;
                    case TradeNewsKind.BlocLeft:
                        media.Report(HeadlineTopic.Alliance, $"blocleft{n.Detail}-{n.Day}", ("Land", n.CountryName), ("Name", $"Abschied aus {n.Detail}"));
                        break;
                    case TradeNewsKind.Sanction:
                        media.Report(HeadlineTopic.Sanction, $"sanction{n.Country}-{n.Day}", ("Land", n.CountryName), ("Name", n.Detail ?? "Sanktionen"));
                        break;
                    // --- Ende Handel 2
                    default:
                        media.Report(HeadlineTopic.TariffRaise, $"tariff{n.Country}-{n.Day / 7}",
                            ("Land", n.CountryName), ("Satz", (n.Value * 100).ToString("0", CultureInfo.InvariantCulture)));
                        break;
                }
            };
        }
    }
}
