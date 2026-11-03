using System;
using System.Collections.Generic;
using System.Numerics;

namespace CityBuilder.Simulation.Safety
{
    /// <summary>Wie ein Brand endete.</summary>
    public enum FireOutcome : byte
    {
        /// <summary>Brennt noch.</summary>
        Burning = 0,
        /// <summary>Entstehungsbrand, von Bewohnern/Beschäftigten selbst gelöscht (Feuerlöscher, Decke).</summary>
        SelfExtinguished = 1,
        /// <summary>Von der Feuerwehr gelöscht.</summary>
        Extinguished = 2,
        /// <summary>Total ausgebrannt (Zonengebäude abgerissen, gesetztes Gebäude = Ruine).</summary>
        TotalLoss = 3,
        /// <summary>Gebäude verschwand während des Brands (Abriss durch Spieler/Straße).</summary>
        Removed = 4,
    }

    /// <summary>Ein laufender Brand an einem Gebäude.</summary>
    public sealed class Fire
    {
        public int Id { get; internal set; }
        public int BuildingId { get; internal set; }
        public Vector2 Position { get; internal set; }
        public long StartTick { get; internal set; }
        /// <summary>Notruf eingegangen (Tick), −1 = noch unbemerkt.</summary>
        public long ReportTick { get; internal set; } = -1;
        /// <summary>Stärke 0..1 (0,1 Entstehungsbrand, 1 Vollbrand).</summary>
        public float Intensity { get; internal set; }
        /// <summary>Höchste bisher erreichte Stärke.</summary>
        public float PeakIntensity { get; internal set; }
        /// <summary>Schaden am Gebäude 0..1 (1 = Totalschaden).</summary>
        public float Damage { get; internal set; }
        /// <summary>Erstes Löschfahrzeug vor Ort (Tick), −1 = noch keins.</summary>
        public long FirstArrivalTick { get; internal set; } = -1;
        /// <summary>Notruf bzw. erste Ankunft in Verkehrszeit (s, genauer als Ticks), −1 = noch nicht.</summary>
        public double ReportTime { get; internal set; } = -1;
        public double FirstArrivalTime { get; internal set; } = -1;
        /// <summary>Minuten (Spielzeit) vom Notruf bis zum ersten Löschfahrzeug, −1 = noch keins da.</summary>
        public float ResponseMinutes => ReportTime < 0 || FirstArrivalTime < 0 ? -1f : (float)Core.TimeModel.GameMinutes(FirstArrivalTime - ReportTime);
        /// <summary>Ohne Dienstfahrzeuge (Tests, Rückfall): geschätzte Ankunft der Wache (Tick), −1 = keine Wache erreichbar.</summary>
        public long FallbackArrivalTick { get; internal set; } = -1;
        /// <summary>Löschfahrzeuge vor Ort / unterwegs (letzter Takt, nur Anzeige).</summary>
        public int TrucksOnSite { get; internal set; }
        public int TrucksEnRoute { get; internal set; }
        /// <summary>Ausgerückte Fahrzeuge insgesamt.</summary>
        public int TrucksSent { get; internal set; }
        /// <summary>Kein freies Löschfahrzeug (Anzahl Versuche).</summary>
        public int MissedDispatches { get; internal set; }
        /// <summary>Löschwasser-Faktor (1 = Hydrant/Wassernetz am Gebäude, sonst nur Tankwasser).</summary>
        public float Water { get; internal set; } = 1f;
        /// <summary>Übergegriffen von diesem Brand (0 = selbst entstanden).</summary>
        public int SourceFireId { get; internal set; }
        public int Injured { get; internal set; }
        /// <summary>Schlagzeile schon gemeldet; Verletzte bei Stärke 0,5 schon ausgewürfelt.</summary>
        internal bool Headlined, SecondInjuryRoll;

        public bool IsReported => ReportTick >= 0;
        public override string ToString() => $"Brand #{Id} an Gebäude {BuildingId} ({Intensity:0.00}, Schaden {Damage:0.00})";
    }

    /// <summary>Abgeschlossener Brand (Verlauf, Inspektor, Bodenwert-Abzug).</summary>
    public sealed class FireRecord
    {
        public int FireId, BuildingId, Street;
        public Vector2 Position;
        public long StartTick, EndTick;
        public float Damage, PeakIntensity;
        public FireOutcome Outcome;
        /// <summary>Minuten vom Notruf bis zum ersten Löschfahrzeug, −1 = keins kam.</summary>
        public float ResponseMinutes = -1f;
        public int Injured;
        public bool Spread;
        public string BuildingName;
    }

    /// <summary>Gebäude, das nach einem Brand außer Betrieb ist (Sanierung bzw. Ruine).</summary>
    public sealed class FireRepair
    {
        public int BuildingId;
        /// <summary>Ende der Sanierung (Tick), −1 = Ruine, wartet auf den Spieler (Wiederaufbau oder Abriss).</summary>
        public long UntilTick;
        public bool Ruin;
        /// <summary>Haushalte, die vorübergehend umgezogen sind (kehren nach der Sanierung zurück).</summary>
        public readonly List<int> Households = new List<int>();
        public bool IsRuin => Ruin && UntilTick < 0;
    }

    /// <summary>Art einer Straftat.</summary>
    public enum CrimeKind : byte
    {
        /// <summary>Einbruch (Wohnung oder Firma).</summary>
        Burglary = 0,
        /// <summary>Ladendiebstahl.</summary>
        Shoplifting = 1,
        /// <summary>Vandalismus (Schaden am Gebäude).</summary>
        Vandalism = 2,
    }

    /// <summary>Eine Straftat an einem Gebäude.</summary>
    public sealed class CrimeIncident
    {
        public int Id, BuildingId, Street;
        public CrimeKind Kind;
        public long Tick;
        public Vector2 Position;
        /// <summary>Schaden in € (Haushalt bzw. Firmenkasse/Reparatur).</summary>
        public float Loss;
        public bool Reported;
        /// <summary>Streifenwagen im Einsatz (Id), 0 = keiner.</summary>
        public int UnitId;
        /// <summary>Ohne Dienstfahrzeuge: geschätzte Ankunft der Polizei (Tick), −1 = keine.</summary>
        public long FallbackArrivalTick = -1;
        /// <summary>Polizei am Tatort (Tick), −1 = nicht/noch nicht.</summary>
        public long ArrivalTick = -1;
        public bool Closed, Solved;
        /// <summary>Tat bzw. Ankunft der Polizei in Verkehrszeit (s), −1 = nicht.</summary>
        public double Time = -1, ArrivalTime = -1;

        /// <summary>Spielminuten von der Tat bis zur Ankunft der Polizei, −1 = kam nicht.</summary>
        public float ResponseMinutes => ArrivalTime < 0 || Time < 0 ? -1f : (float)Core.TimeModel.GameMinutes(ArrivalTime - Time);
    }

    /// <summary>Kriminalität eines Gebäudes: Rate 0..1 und ihre Faktoren (jeweils 0..1).</summary>
    public struct CrimeFactors
    {
        public float Unemployment, Dissatisfaction, LowEducation, Density, LowLandValue, Shops;
        /// <summary>Polizeiabdeckung und Streifenpräsenz (senken die Rate).</summary>
        public float Police, Patrol;
        /// <summary>Druck vor Polizei (Summe der Faktoren) und Ergebnis.</summary>
        public float Pressure, Rate;
    }

    /// <summary>Brandgefahr eines Gebäudes: Faktoren und Ergebnis (Entstehungen je Tag).</summary>
    public struct FireRiskInfo
    {
        public float Zone, Recipe, Condition, Garbage, Power, Size, Prevention;
        /// <summary>Gesamtfaktor (1 = durchschnittliches Einfamilienhaus) und erwartete Brände je Jahr.</summary>
        public float Factor, PerYear;
        public string RecipeName;
    }

    /// <summary>Ereignis für HUD-Meldungen (fortlaufende Nummer, Simulation schreibt, Darstellung liest).</summary>
    public sealed class SafetyEvent
    {
        public int Serial;
        public long Tick;
        public bool Warning;
        public string Title, Text;
        public int BuildingId;
        /// <summary>Schlüssel (gleicher Schlüssel ersetzt die Meldung im HUD).</summary>
        public string Key;
    }

    /// <summary>Tageszähler (heute, gestern, gesamt).</summary>
    public sealed class SafetyStats
    {
        public int Fires, SelfExtinguished, Extinguished, SevereDamage, TotalLosses, Spread, Injured, MissedFireDispatches;
        public int Crimes, Burglaries, Shopliftings, Vandalism, Reported, Solved, Arrests, Released, PoliceCalls, MissedPoliceDispatches;
        public double CrimeLoss;
        public double ResponseMinutesSum;
        public int ResponseCount;

        public float AverageResponseMinutes => ResponseCount > 0 ? (float)(ResponseMinutesSum / ResponseCount) : -1f;
        public float ClearanceRate => Crimes > 0 ? Solved / (float)Crimes : 0f;

        public void CopyFrom(SafetyStats o)
        {
            Fires = o.Fires; SelfExtinguished = o.SelfExtinguished; Extinguished = o.Extinguished; SevereDamage = o.SevereDamage;
            TotalLosses = o.TotalLosses; Spread = o.Spread; Injured = o.Injured; MissedFireDispatches = o.MissedFireDispatches;
            Crimes = o.Crimes; Burglaries = o.Burglaries; Shopliftings = o.Shopliftings; Vandalism = o.Vandalism; Reported = o.Reported;
            Solved = o.Solved; Arrests = o.Arrests; Released = o.Released; PoliceCalls = o.PoliceCalls; MissedPoliceDispatches = o.MissedPoliceDispatches;
            CrimeLoss = o.CrimeLoss; ResponseMinutesSum = o.ResponseMinutesSum; ResponseCount = o.ResponseCount;
        }

        public void Clear() => CopyFrom(new SafetyStats());

        internal void Write(Persistence.SaveWriter w)
        {
            foreach (int v in new[] { Fires, SelfExtinguished, Extinguished, SevereDamage, TotalLosses, Spread, Injured, MissedFireDispatches,
                                      Crimes, Burglaries, Shopliftings, Vandalism, Reported, Solved, Arrests, Released, PoliceCalls, MissedPoliceDispatches })
                w.Int(v);
            w.Double(CrimeLoss); w.Double(ResponseMinutesSum); w.Int(ResponseCount);
        }

        internal void Read(Persistence.SaveReader r)
        {
            Fires = r.Int(); SelfExtinguished = r.Int(); Extinguished = r.Int(); SevereDamage = r.Int(); TotalLosses = r.Int(); Spread = r.Int();
            Injured = r.Int(); MissedFireDispatches = r.Int(); Crimes = r.Int(); Burglaries = r.Int(); Shopliftings = r.Int(); Vandalism = r.Int();
            Reported = r.Int(); Solved = r.Int(); Arrests = r.Int(); Released = r.Int(); PoliceCalls = r.Int(); MissedPoliceDispatches = r.Int();
            CrimeLoss = r.Double(); ResponseMinutesSum = r.Double(); ResponseCount = r.Int();
        }

        /// <summary>Addiert in die Gesamtzählung.</summary>
        public void AddTo(SafetyStats total)
        {
            total.Fires += Fires; total.SelfExtinguished += SelfExtinguished; total.Extinguished += Extinguished; total.SevereDamage += SevereDamage;
            total.TotalLosses += TotalLosses; total.Spread += Spread; total.Injured += Injured; total.MissedFireDispatches += MissedFireDispatches;
            total.Crimes += Crimes; total.Burglaries += Burglaries; total.Shopliftings += Shopliftings; total.Vandalism += Vandalism;
            total.Reported += Reported; total.Solved += Solved; total.Arrests += Arrests; total.Released += Released; total.PoliceCalls += PoliceCalls;
            total.MissedPoliceDispatches += MissedPoliceDispatches; total.CrimeLoss += CrimeLoss;
            total.ResponseMinutesSum += ResponseMinutesSum; total.ResponseCount += ResponseCount;
        }
    }

    /// <summary>Ansichts-Kennungen für die Dienste-Ansicht (keine echten Dienstarten).</summary>
    public static class SafetyOverlays
    {
        public const string FireRisk = "safety_fire", Crime = "safety_crime";
        public static bool IsSafety(string kind) => kind == FireRisk || kind == Crime;
        public static string DisplayName(string kind) => kind == FireRisk ? "Brandgefahr" : kind == Crime ? "Kriminalität" : kind;
    }

    public static class CrimeKinds
    {
        public static string DisplayName(CrimeKind k)
        {
            switch (k)
            {
                case CrimeKind.Burglary: return "Einbruch";
                case CrimeKind.Shoplifting: return "Ladendiebstahl";
                default: return "Vandalismus";
            }
        }
    }

    public static class FireOutcomes
    {
        public static string DisplayName(FireOutcome o)
        {
            switch (o)
            {
                case FireOutcome.Burning: return "brennt";
                case FireOutcome.SelfExtinguished: return "selbst gelöscht";
                case FireOutcome.Extinguished: return "von der Feuerwehr gelöscht";
                case FireOutcome.TotalLoss: return "ausgebrannt";
                default: return "Gebäude entfernt";
            }
        }
    }
}
