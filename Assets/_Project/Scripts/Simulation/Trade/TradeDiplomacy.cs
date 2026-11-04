using System;
using System.Collections.Generic;
using CityBuilder.Simulation.Citizens;
using CityBuilder.Simulation.World;

namespace CityBuilder.Simulation.Trade
{
    // =====================================================================================================================
    // Handel 2 (Etappe 8 Stufe 2): Regierungen, Bündnisse, Abstimmungen, Diplomatie, Tourismus – Typen und Zahlen.
    // =====================================================================================================================

    /// <summary>Regierungsform eines Landes (fiktiv, prägt Persönlichkeit, Zölle und Grundbeziehungen).</summary>
    public enum GovernmentForm : byte
    {
        /// <summary>Republik: offen, verlässlich.</summary>
        Republic,
        /// <summary>Monarchie: vorsichtig, verlässlich, traditionell.</summary>
        Monarchy,
        /// <summary>Technokratie: geschäftstüchtig, freihändlerisch.</summary>
        Technocracy,
        /// <summary>Räterat: Schutz der eigenen Produktion, wenig Geschäftssinn.</summary>
        Council,
        /// <summary>Autokratie: protektionistisch, risikofreudig, weniger verlässlich.</summary>
        Autocracy,
    }

    /// <summary>Wirkungen der Regierungsformen (Platzhalter fürs Balancing, alles an einem Ort).</summary>
    public static class Governments
    {
        public static string DisplayName(GovernmentForm f)
        {
            switch (f)
            {
                case GovernmentForm.Republic: return "Republik";
                case GovernmentForm.Monarchy: return "Monarchie";
                case GovernmentForm.Technocracy: return "Technokratie";
                case GovernmentForm.Council: return "Räterat";
                default: return "Autokratie";
            }
        }

        /// <summary>Verschiebung der Persönlichkeit (Risiko, Protektionismus, Verlässlichkeit, Geschäftssinn).</summary>
        public static (float risk, float protection, float reliability, float thrift) Nudge(GovernmentForm f)
        {
            switch (f)
            {
                case GovernmentForm.Republic: return (0f, -0.05f, 0.05f, 0f);
                case GovernmentForm.Monarchy: return (-0.1f, 0f, 0.1f, 0f);
                case GovernmentForm.Technocracy: return (0f, -0.1f, 0f, 0.1f);
                case GovernmentForm.Council: return (0f, 0.1f, 0f, -0.1f);
                default: return (0.1f, 0.15f, -0.1f, 0f);
            }
        }

        /// <summary>Faktor auf den Grundzoll (Autokratie und Räterat schützen mehr).</summary>
        public static double TariffFactor(GovernmentForm f) =>
            f == GovernmentForm.Autocracy ? 1.3 : f == GovernmentForm.Council ? 1.15 : f == GovernmentForm.Technocracy ? 0.8 : 1.0;

        /// <summary>Grundbeziehung zwischen zwei Regierungsformen: gleiche +5, Autokratie ↔ Republik −6, Räterat ↔ Monarchie −4.</summary>
        public static float Affinity(GovernmentForm a, GovernmentForm b)
        {
            if (a == b) return DiplomacyRules.SameGovernmentRelation;
            if ((a == GovernmentForm.Autocracy && b == GovernmentForm.Republic) || (b == GovernmentForm.Autocracy && a == GovernmentForm.Republic)) return -6f;
            if ((a == GovernmentForm.Council && b == GovernmentForm.Monarchy) || (b == GovernmentForm.Council && a == GovernmentForm.Monarchy)) return -4f;
            return 0f;
        }

        /// <summary>Regierungsform aus Seed + Land (Profil prägt: Rohstoffländer eher Autokratie, Technik eher Technokratie). Spieler: Republik.</summary>
        internal static GovernmentForm For(int seed, Country c)
        {
            if (c.IsPlayer) return GovernmentForm.Republic;
            uint h = SimRandom.Hash(unchecked(seed * 4409 + 77), c.Id * 31 + 5) % 100u;
            int[] w = { 34, 20, 14, 14, 18 };
            switch (c.Profile.Primary)
            {
                case Strength.RawMaterials: w[(int)GovernmentForm.Autocracy] += 12; w[(int)GovernmentForm.Republic] -= 6; break;
                case Strength.Technology: w[(int)GovernmentForm.Technocracy] += 14; w[(int)GovernmentForm.Autocracy] -= 6; break;
                case Strength.Agriculture: w[(int)GovernmentForm.Monarchy] += 8; w[(int)GovernmentForm.Council] += 4; break;
                case Strength.Industry: w[(int)GovernmentForm.Council] += 10; break;
                default: w[(int)GovernmentForm.Republic] += 8; break;
            }
            int total = 0;
            foreach (int x in w) total += Math.Max(0, x);
            int pick = (int)(h * (uint)total / 100u), acc = 0;
            for (int i = 0; i < w.Length; i++)
            {
                acc += Math.Max(0, w[i]);
                if (pick < acc) return (GovernmentForm)i;
            }
            return GovernmentForm.Republic;
        }
    }

    /// <summary>Art eines Bündnisses.</summary>
    public enum BlocKind : byte
    {
        /// <summary>Handelsbündnis: zollfrei untereinander, jedes Mitglied behält seine Außenzölle.</summary>
        TradeAlliance,
        /// <summary>Zollunion: zollfrei untereinander und gemeinsamer Außenzoll gegen Nichtmitglieder.</summary>
        CustomsUnion,
    }

    /// <summary>
    /// Bündnis mehrerer Länder (Handel 2). Wirksam ab zwei Mitgliedern (<see cref="Active"/>): Binnenzoll 0, in der Zollunion
    /// gemeinsamer Außenzoll, Sanktionen (beschlossen per Abstimmung) gelten für alle Mitglieder (Zoll 50 %, kein neuer Handel).
    /// Ein Land gehört höchstens einem Bündnis an.
    /// </summary>
    public sealed class TradeBloc
    {
        public int Id { get; internal set; }
        public string Name { get; internal set; }
        public BlocKind Kind { get; internal set; }
        /// <summary>Gründer = Vorsitz (setzt den gemeinsamen Außenzoll).</summary>
        public int Founder { get; internal set; }
        public int FoundedDay { get; internal set; }
        public int DissolvedDay { get; internal set; } = -1;
        /// <summary>Gemeinsamer Außenzoll der Zollunion (0..0,5).</summary>
        public double ExternalTariff { get; internal set; }
        internal readonly List<int> members = new List<int>();
        internal readonly List<int> sanctioned = new List<int>();
        public IReadOnlyList<int> Members => members;
        /// <summary>Länder, gegen die das Bündnis Sanktionen beschlossen hat.</summary>
        public IReadOnlyList<int> Sanctioned => sanctioned;

        public bool Dissolved => DissolvedDay >= 0;
        public bool Active => !Dissolved && members.Count >= 2;
        public bool IsMember(int n) => !Dissolved && members.Contains(n);
        public bool Sanctions(int n) => Active && sanctioned.Contains(n);
        public string KindText => Kind == BlocKind.CustomsUnion ? "Zollunion" : "Handelsbündnis";
        public override string ToString() => $"{Name} ({KindText}, {members.Count} Mitglieder)";
    }

    public enum VoteKind : byte
    {
        /// <summary>Aufnahme eines Landes (Zweidrittelmehrheit der abgegebenen Stimmen).</summary>
        Admit,
        /// <summary>Sanktionen gegen ein Land (einfache Mehrheit).</summary>
        Sanction,
        /// <summary>Sanktionen aufheben (einfache Mehrheit).</summary>
        LiftSanction,
    }

    public enum VoteChoice : byte { Abstain, Yes, No }

    public enum VoteStatus : byte { Open, Passed, Failed }

    /// <summary>Abstimmung in einem Bündnis. Mitglieder stimmen (NPC per Utility AI, der Spieler per Command); Ende nach 7 Tagen oder wenn alle gestimmt haben.</summary>
    public sealed class BlocVote
    {
        public int Id { get; internal set; }
        public int BlocId { get; internal set; }
        public VoteKind Kind { get; internal set; }
        /// <summary>Betroffenes Land (Bewerber bzw. Ziel der Sanktion).</summary>
        public int Subject { get; internal set; }
        public int Proposer { get; internal set; }
        public int OpenedDay { get; internal set; }
        /// <summary>Ab diesem Handelstag stimmen die NPC-Mitglieder (eine Nacht Bedenkzeit).</summary>
        public int FirstVoteDay { get; internal set; }
        public int CloseDay { get; internal set; }
        public VoteStatus Status { get; internal set; }
        public string Result { get; internal set; }
        /// <summary>Stimmen je Mitglied (nach Land sortiert).</summary>
        internal readonly SortedDictionary<int, VoteChoice> ballots = new SortedDictionary<int, VoteChoice>();
        public IReadOnlyDictionary<int, VoteChoice> Ballots => ballots;
        public bool IsOpen => Status == VoteStatus.Open;

        public int Count(VoteChoice c)
        {
            int k = 0;
            foreach (var kv in ballots) if (kv.Value == c) k++;
            return k;
        }

        public string KindText => Kind == VoteKind.Admit ? "Aufnahme" : Kind == VoteKind.Sanction ? "Sanktionen" : "Sanktionen aufheben";
    }

    /// <summary>Einladung eines Mitglieds an ein Land, dem Bündnis beizutreten (Annahme = Beitritt).</summary>
    public sealed class BlocInvitation
    {
        public int Id { get; internal set; }
        public int BlocId { get; internal set; }
        public int From { get; internal set; }
        public int To { get; internal set; }
        public int CreatedDay { get; internal set; }
        public int ExpiresDay { get; internal set; }
        /// <summary>Erster Tag, an dem ein NPC antwortet (eine Nacht Bedenkzeit).</summary>
        public int AnswerDay { get; internal set; }
        public OfferStatus Status { get; internal set; }
        public string Reason { get; internal set; }
        public bool IsOpen => Status == OfferStatus.Open;
    }

    /// <summary>Diplomatische Aktion (Command <see cref="DiplomacyCommand"/>).</summary>
    public enum DiplomacyAction : byte
    {
        /// <summary>Geldgeschenk: Beziehung + (abnehmend bei mehreren Geschenken in 30 Tagen), Vertrauen + 0,01.</summary>
        Gift,
        /// <summary>Protest gegen Zölle: Beziehung −3, manchmal senkt das Land seinen Sonderzoll.</summary>
        Protest,
        /// <summary>Drohung: Beziehung −8, Vertrauen −0,05; gibt das Land nach (Macht), fällt der Sonderzoll, sonst Gegenzoll.</summary>
        Threat,
    }

    /// <summary>Zugang der Spielerstadt für Touristen (aus Gateways/Bahnhof/Hafen/Flughafen).</summary>
    public struct TourismAccess
    {
        public bool Road, Rail, Sea, Air;
        public static TourismAccess All => new TourismAccess { Road = true, Rail = true, Sea = true, Air = true };
    }

    /// <summary>Wie Touristen in die Stadt kommen.</summary>
    public enum TourismMode : byte { Road, Rail, Ship, Air, Lost }

    /// <summary>Zahlen für Gegenangebote, Bündnisse, Diplomatie und Tourismus (Platzhalter fürs Balancing).</summary>
    public static class DiplomacyRules
    {
        // ------------------------------------------------------------ Verhandlung
        /// <summary>Höchstens so viele Gegenangebote in einer Verhandlung (für alle Seiten).</summary>
        public const int MaxNegotiationRounds = 6;
        /// <summary>Gegenangebot zielt auf eigenen Nutzen ≥ diesem Wert (aus eigener Sicht).</summary>
        public const float CounterMargin = 0.12f;
        /// <summary>Weiter auseinander (Preis) lohnt kein Gegenangebot.</summary>
        public const double MaxCounterPriceShift = 0.6;
        /// <summary>Gegenangebot-Wahrscheinlichkeit = Grund + Anteil × Geschäftssinn.</summary>
        public const double CounterChanceBase = 0.55, CounterChanceThrift = 0.4;

        // ------------------------------------------------------------ Regierungen & Bündnisse
        public const float SameGovernmentRelation = 5f;
        /// <summary>Bündnisse beim Erzeugen der Welt (höchstens).</summary>
        public const int InitialBlocs = 3;
        public const int InitialBlocMaxMembers = 4;
        /// <summary>Gründung braucht diese Grundbeziehung zum Gründer bzw. zu allen Mitgliedern.</summary>
        public const float InitialBlocRelation = 18f, InitialBlocMinPair = 0f;
        /// <summary>Mitglieder verstehen sich wöchentlich besser.</summary>
        public const float RelationBlocWeekly = 0.3f;
        /// <summary>Beitritt: Beziehung + zu jedem Mitglied; Austritt: −.</summary>
        public const float RelationJoin = 3f, RelationLeave = 4f;
        /// <summary>Sanktion: Zoll der Mitglieder gegen das Ziel; Beziehung Ziel ↔ Mitglieder sinkt einmalig.</summary>
        public const double SanctionTariff = 0.5;
        public const float RelationSanction = 12f;
        /// <summary>Abstimmungen laufen so viele Tage.</summary>
        public const int VoteDays = 7;
        /// <summary>Aufnahme braucht diesen Anteil Ja an den abgegebenen Stimmen.</summary>
        public const double AdmitMajority = 2.0 / 3.0;
        public const int InvitationValidDays = 14;
        /// <summary>Bündnis im Aufbau (ein Mitglied) löst sich nach so vielen Tagen ohne Beitritt auf.</summary>
        public const int FormingDays = 21;
        /// <summary>Bündnispartner bewerten Angebote freundlicher.</summary>
        public const float BlocPartnerBonus = 0.15f;
        /// <summary>KI: Chance je Wochenentscheidung, überhaupt Bündnispolitik zu machen.</summary>
        public const double DiplomacyChance = 0.35;
        /// <summary>KI: neues Bündnis ab dieser Beziehung zum Partner; Einladung ab dieser Beziehung; Sanktionsantrag unter dieser.</summary>
        public const float FoundRelation = 40f, InviteRelation = 32f, SanctionRelation = -55f, LiftRelation = -15f;
        /// <summary>Sanktionsantrag nur mit Grund: Vertragsbruch/Drohung im Gedächtnis oder Beziehung unter diesem Wert.</summary>
        public const float SanctionWithoutCause = -75f;
        /// <summary>Stimme für Sanktionen: Grundablehnung (Sanktionen kosten Handel).</summary>
        public const float SanctionVoteBias = 0.45f;
        /// <summary>Höchstzahl aktiver Bündnisse in der Welt bzw. Mitglieder je Bündnis.</summary>
        public const int MaxBlocs = 6, MaxBlocMembers = 8;
        /// <summary>Nach abgelehnter Aufnahme: neuer Antrag beim selben Bündnis erst nach so vielen Tagen (KI).</summary>
        public const int ReapplyDays = 60;
        /// <summary>Ein Bündnis stimmt höchstens alle so viele Tage über neue Sanktionen ab (KI).</summary>
        public const int SanctionCooldownDays = 28;

        // ------------------------------------------------------------ Diplomatie
        /// <summary>Geschenk: Beziehung + Faktor × log10(1 + Betrag / Bezug), geteilt durch (1 + Geschenke der letzten 30 Tage).</summary>
        public const double GiftRelationFactor = 8.0, GiftReference = 10000.0, MinGift = 1000.0;
        public const int GiftMemoryDays = 30;
        /// <summary>Protest/Drohung: höchstens einmal je Woche gegen dasselbe Land.</summary>
        public const int DiplomacyCooldownDays = 7;
        public const float RelationProtest = 3f, RelationThreat = 8f, TrustThreat = 0.05f;
        /// <summary>Gegenzoll nach erfolgloser Drohung.</summary>
        public const double ThreatRetaliation = 0.05;

        // ------------------------------------------------------------ Tourismus (Dienstleistungshandel)
        /// <summary>Anteil des Tourismus-Haushaltsbedarfs, den ein Land im Ausland verbringt.</summary>
        public const double OutboundShare = 0.25;
        /// <summary>Entfernungsabfall exp(−km / Maß).</summary>
        public const double TourismDistanceKm = 3000.0;
        /// <summary>Preis-Elastizität (Preis Herkunft / Preis Ziel)^e, begrenzt.</summary>
        public const double TourismPriceElasticity = 0.6;
        /// <summary>Gleiches Bündnis: leichteres Reisen.</summary>
        public const double TourismBlocBonus = 1.25;
        /// <summary>Ein Ziel bedient höchstens diesen Anteil seines Lagers über 10 Tagen Eigenbedarf je Tag.</summary>
        public const double TourismCapacityShare = 0.25, TourismReserveDays = 10.0;
        /// <summary>Kategorie-Einheiten je Besucher (≈ 250 € Ausgaben eines Aufenthalts).</summary>
        public const double UnitsPerVisitor = 25.0;
        /// <summary>Bodengebundene Anreise (Straße/Bahn) bis zu dieser Entfernung über Land.</summary>
        public const double GroundTourismKm = 1500.0;
        /// <summary>Ohne passendes Verkehrsmittel kommt nur dieser Anteil (lange Autofahrt, Schiff).</summary>
        public const double DetourShare = 0.5;
        /// <summary>Marktfaktor der Spielerstadt (Beziehungen, Preise, Bündnis) begrenzt auf dieses Band.</summary>
        public const double MarketFactorMin = 0.25, MarketFactorMax = 1.6;
    }
}
