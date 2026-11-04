using System;
using System.Collections.Generic;
using CityBuilder.Simulation.Goods;
using CityBuilder.Simulation.Trade;
using CityBuilder.Simulation.World;

namespace CityBuilder.Simulation.Realm
{
    public enum RealmRouteMode : byte
    {
        /// <summary>Lkw über die Autobahn (Gateway Straße).</summary>
        Road,
        /// <summary>Güterzug (beide Städte mit Güterbahnhof in Betrieb).</summary>
        Rail,
        /// <summary>Schiff (beide Städte an der Küste; eigener Hafen spart den Vorlauf).</summary>
        Sea,
    }

    /// <summary>Weg zwischen zwei eigenen Städten über die realen Anschlüsse (Etappe 8: Kosten je Frachttonne, Lieferzeit; ohne Zoll).</summary>
    public sealed class RealmRoute
    {
        public int From { get; internal set; }
        public int To { get; internal set; }
        public RealmRouteMode Mode { get; internal set; }
        public double DirectKm { get; internal set; }
        public double LandKm { get; internal set; }
        public double SeaKm { get; internal set; }
        public double CostPerTon { get; internal set; }
        public int LeadDays { get; internal set; }

        public double UnitCost(GoodType g) => CostPerTon * TradeGoods.FreightTons(g);

        public string ModeText => Mode == RealmRouteMode.Sea ? "Schiff" : Mode == RealmRouteMode.Rail ? "Bahn" : "Lkw (Autobahn)";
        public override string ToString() => $"{ModeText} · {LandKm + SeaKm:0} km · {LeadDays} T. · {CostPerTon:0.0} €/t";
    }

    public enum RealmDeliveryStatus : byte { Active, Completed, Cancelled }

    /// <summary>
    /// Liefervereinbarung zwischen zwei eigenen Städten – dieselben <see cref="ContractTerms"/> wie ein Handelsvertrag (Gut, Menge je
    /// Woche, Wochen, Festpreis bzw. Weltpreis × Faktor), Partner = eigene Stadt, ohne Zoll und ohne Strafe. Wöchentlicher Versand
    /// über den <see cref="RealmRoute"/>, Bezahlung (Verrechnungspreis) bei Ankunft von der Empfänger- an die Senderkasse,
    /// Fracht trägt der Sender.
    /// </summary>
    public sealed class RealmDelivery
    {
        public int Id { get; internal set; }
        public int From { get; internal set; }
        public int To { get; internal set; }
        public ContractTerms Terms { get; internal set; }
        public int StartDay { get; internal set; }
        public int NextDispatchDay { get; internal set; }
        public int WeeksDispatched { get; internal set; }
        public RealmDeliveryStatus Status { get; internal set; }
        /// <summary>Summen: verschickt, gefehlt (Einheiten), Fracht und Verrechnung (€).</summary>
        public double Shipped { get; internal set; }
        public double Shortfall { get; internal set; }
        public double FreightPaid { get; internal set; }
        public double ValueSettled { get; internal set; }
        /// <summary>Letzte Woche: verschickte Menge (Info).</summary>
        public double LastShipped { get; internal set; }

        public bool IsActive => Status == RealmDeliveryStatus.Active;
        public bool AllDispatched => WeeksDispatched >= Terms.Weeks;
        public GoodType Good => Terms.Good;
    }

    /// <summary>Ware unterwegs zwischen zwei eigenen Städten.</summary>
    public sealed class RealmShipment
    {
        public int DeliveryId { get; internal set; }
        public int From { get; internal set; }
        public int To { get; internal set; }
        public GoodType Good { get; internal set; }
        public double Units { get; internal set; }
        public double UnitPrice { get; internal set; }
        public int DispatchDay { get; internal set; }
        public int ArriveDay { get; internal set; }
        public RealmRouteMode Mode { get; internal set; }
    }

    /// <summary>Meldung des Reichs (UI/Medien): Städte, Lieferungen, Landesgründung, Anerkennung, NPC-Expansion.</summary>
    public readonly struct RealmNews
    {
        public readonly int Day;
        public readonly string Title;
        public readonly string Text;
        public readonly bool Important;
        public RealmNews(int day, string title, string text, bool important = false) { Day = day; Title = title; Text = text; Important = important; }
    }

    // ------------------------------------------------------------ Commands (wie Handels-Commands: Submit/Execute, Log)

    /// <summary>
    /// Spieleraktion im Reich – gleiche Schnittstelle wie <see cref="TradeCommand"/> (Ergebnis <see cref="TradeResult"/>,
    /// <see cref="RealmSystem.Submit"/> reiht ein, <see cref="RealmSystem.Execute"/> führt sofort aus, Log für Replays).
    /// Der Wechsel der detaillierten Stadt ist bewusst <b>kein</b> Command (er ersetzt die Welt wie ein Laden, s. <see cref="RealmSwitch"/>).
    /// </summary>
    public abstract class RealmCommand
    {
        internal abstract TradeResult Apply(RealmSystem realm);
        public abstract string Describe();
        /// <summary>Art für den Spielstand (Name statt Typ).</summary>
        internal abstract string Kind { get; }
    }

    /// <summary>Liefervereinbarung zwischen zwei eigenen Städten (Partner = eigene Stadt; <see cref="ContractTerms"/> wie im Handel).</summary>
    public sealed class ProposeDeliveryCommand : RealmCommand
    {
        public int FromCity { get; }
        public int ToCity { get; }
        public ContractTerms Terms { get; }
        public ProposeDeliveryCommand(int fromCity, int toCity, ContractTerms terms) { FromCity = fromCity; ToCity = toCity; Terms = terms?.Clone(); }
        internal override TradeResult Apply(RealmSystem r) => r.DoProposeDelivery(FromCity, ToCity, Terms);
        public override string Describe() => $"Lieferung Stadt {FromCity} → {ToCity}: {Terms?.Describe()}";
        internal override string Kind => "delivery";
    }

    public sealed class CancelDeliveryCommand : RealmCommand
    {
        public int DeliveryId { get; }
        public CancelDeliveryCommand(int id) { DeliveryId = id; }
        internal override TradeResult Apply(RealmSystem r) => r.DoCancelDelivery(DeliveryId);
        public override string Describe() => $"Lieferung #{DeliveryId} beenden";
        internal override string Kind => "cancel";
    }

    /// <summary>Neue Stadt gründen (Region, Weltzelle −1 = beste Zelle, Name). Kosten trägt die detailliert laufende Mutterstadt.</summary>
    public sealed class FoundCityCommand : RealmCommand
    {
        public int RegionId { get; }
        public int Cell { get; }
        public string Name { get; }
        public FoundCityCommand(int regionId, string name, int cell = -1) { RegionId = regionId; Name = name; Cell = cell; }
        internal override TradeResult Apply(RealmSystem r) => r.DoFoundCity(RegionId, Cell, Name);
        public override string Describe() => $"Stadt „{Name}“ in Region {RegionId} gründen";
        internal override string Kind => "found-city";
    }

    /// <summary>Land gründen: Name, Flaggenfarbe (0xRRGGBB), Hauptstadt.</summary>
    public sealed class FoundNationCommand : RealmCommand
    {
        public string Name { get; }
        public uint Color { get; }
        public int CapitalCity { get; }
        public FoundNationCommand(string name, uint color, int capitalCity) { Name = name; Color = color & 0xFFFFFFu; CapitalCity = capitalCity; }
        internal override TradeResult Apply(RealmSystem r) => r.DoFoundNation(Name, Color, CapitalCity);
        public override string Describe() => $"Land „{Name}“ gründen (Hauptstadt {CapitalCity})";
        internal override string Kind => "found-nation";
    }

    /// <summary>Landeskasse → Stadtkasse (Zuschuss; negativ = Stadt zahlt an das Land).</summary>
    public sealed class TransferFundsCommand : RealmCommand
    {
        public int City { get; }
        public double Amount { get; }
        public TransferFundsCommand(int city, double amount) { City = city; Amount = amount; }
        internal override TradeResult Apply(RealmSystem r) => r.DoTransfer(City, Amount);
        public override string Describe() => $"Landeskasse → Stadt {City}: {Amount:N0} €";
        internal override string Kind => "transfer";
    }

    /// <summary>Eigene Stadt umbenennen.</summary>
    public sealed class RenameCityCommand : RealmCommand
    {
        public int City { get; }
        public string Name { get; }
        public RenameCityCommand(int city, string name) { City = city; Name = name; }
        internal override TradeResult Apply(RealmSystem r) => r.DoRename(City, Name);
        public override string Describe() => $"Stadt {City} heißt „{Name}“";
        internal override string Kind => "rename";
    }
}
