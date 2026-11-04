using System;
using System.Collections.Generic;
using System.Numerics;
using CityBuilder.Simulation.Roads;

namespace CityBuilder.Simulation.Shipping
{
    /// <summary>
    /// Alle Zahlen der Wasserstraße &amp; des Hafens (Stufe 3) an einem Ort – Platzhalter fürs Balancing. Zeiten in Sekunden
    /// Verkehrszeit (<c>TrafficSystem.Time</c>, Schiffe fahren wie Autos und Züge in physikalischen m/s), Geld in €.
    /// </summary>
    public static class ShippingRules
    {
        // --- Navigationsraster / Fahrrinne
        /// <summary>Zellgröße des Navigationsrasters (m).</summary>
        public const float GridCell = 8f;
        /// <summary>Kielfreiheit: Wassertiefe ≥ Tiefgang + so viel (m).</summary>
        public const float KeelClearance = 0.5f;
        /// <summary>Fahrrinne bleibt mindestens so weit vom Ufer (m, Rand der Fahrrinnenzelle).</summary>
        public const float MinShoreDistance = 10f;
        /// <summary>Die Fahrrinne liegt bevorzugt so weit vom Ufer (Kosten steigen näher am Ufer).</summary>
        public const float PreferredShoreDistance = 40f;
        /// <summary>Halbe Breite der Fahrrinne (für Brücken, Sperre fürs Terraforming): Anteil der Gewässerbreite, begrenzt.</summary>
        public const float ChannelShare = 0.35f, MinChannelHalfWidth = 10f, MaxChannelHalfWidth = 45f;
        /// <summary>Abstand der Punkte des geglätteten Fahrrinnen-Linienzugs (m).</summary>
        public const float PointSpacing = 24f;
        /// <summary>Fahrrinne beginnt/endet so weit außerhalb des Kartenrands (Schiffe kommen von draußen).</summary>
        public const float OutsideRun = 80f;

        // --- Brücken
        /// <summary>Konstruktionshöhe (Tragwerk unter der Fahrbahn) von Straßen- bzw. Gleisbrücken.</summary>
        public const float RoadDeck = 1.5f, RailDeck = 1.2f;
        /// <summary>Sicherheitsabstand zwischen Masthöhe und lichter Höhe (m).</summary>
        public const float AirSafety = 0.5f;
        /// <summary>Lichte Durchfahrtshöhe, für die eine Fahrrinne ausgelegt ist (Binnenwasserstraße / Seeweg).</summary>
        public const float RiverDesignClearance = 7f, SeaDesignClearance = 25f;

        // --- Klappbrücke
        /// <summary>Fahrbahn der Klappbrücke über dem Wasserspiegel (niedrig – sie öffnet ja).</summary>
        public const float DrawbridgeDeck = 4f;
        /// <summary>Länge der Klappe (Stück über der Fahrrinne): Fahrrinne + 2 × so viel, begrenzt.</summary>
        public const float DrawbridgeMargin = 8f, DrawbridgeMinSpan = 36f, DrawbridgeMaxSpan = 90f;
        /// <summary>Die Klappe deckt höchstens so viel Rinne je Seite ab (Schiffe fahren mittig ± Ausweichen nach Steuerbord).</summary>
        public const float DrawbridgeMaxHalfChannel = 28f;
        /// <summary>Klappbrücke quert die Fahrrinne mindestens so steil (Grad).</summary>
        public const float DrawbridgeMinAngle = 50f;
        /// <summary>Mindestlänge der Zufahrten links/rechts der Klappe (Haltelinie davor, m).</summary>
        public const float DrawbridgeMinApproach = 20f;
        /// <summary>Mechanik (Klappe, Antrieb, Brückenhaus) – zusätzlich zur Fahrbahn, Unterhalt je Woche.</summary>
        public const double DrawbridgeCost = 180000, DrawbridgeUpkeep = 900;
        /// <summary>Ablauf in Sekunden: Warnen (Ampel rot, Schranke senkt), Klappe hebt, Klappe senkt, Schranke öffnet.</summary>
        public const float DrawbridgeWarn = 10f, DrawbridgeRaise = 15f, DrawbridgeLower = 15f, DrawbridgeClear = 4f;
        /// <summary>Zwischen zwei Öffnungen bleibt die Straße mindestens so lange frei; offen höchstens so lange.</summary>
        public const float DrawbridgeMinRoadOpen = 75f, DrawbridgeMaxOpen = 150f;
        /// <summary>Schiffe melden sich so weit vor der Brücke an (m) und halten so weit davor (Bug, m).</summary>
        public const float DrawbridgeRequestDistance = 450f, DrawbridgeStopGap = 30f;
        /// <summary>Haltelinie der Autos vor der Klappe (m vor dem Ende der Zufahrt).</summary>
        public const float DrawbridgeStopLine = 3f;
        /// <summary>Warten auf eine geräumte Klappe höchstens so lange, dann öffnet sie trotzdem (Rückstau auf der Brücke).</summary>
        public const float DrawbridgeClearPatience = 45f;

        // --- Fahren
        /// <summary>Bremsverzögerung und Beschleunigung der Schiffe (m/s²) – Schiffe sind träge.</summary>
        public const float Deceleration = 0.08f;
        /// <summary>Abstand zum Vordermann (Heck) zusätzlich zur eigenen Länge (m).</summary>
        public const float FollowGap = 60f;
        /// <summary>Schiff wartet so lange vor der Karte (erster Abschnitt belegt), sonst fällt es aus.</summary>
        public const float SpawnPatience = 900f;
        /// <summary>Wartet ein Schiff vor der Klappbrücke länger, gilt es als „lange gewartet“ (Statistik/Hinweis).</summary>
        public const float LongWait = 240f;
        /// <summary>Vor einer (nachträglich gebauten) zu niedrigen festen Brücke kehrt ein Schiff nach so vielen Sekunden um.</summary>
        public const float TurnBackAfter = 120f;

        // --- Fahrplan (Spielstunden)
        /// <summary>Binnenschiffe im Durchgang: je Richtung alle so viele Stunden, rund um die Uhr.</summary>
        public const int BargeEveryHours = 2;
        /// <summary>Fahrgastschiffe 10–18 Uhr alle 2 h je Richtung (April–Oktober), Segelyachten 9–18 Uhr alle 3 h (Mai–September).</summary>
        public const int ExcursionFirstHour = 10, ExcursionLastHour = 18, YachtFirstHour = 9, YachtLastHour = 18;
        /// <summary>Hafenanlauf: alle so viele Stunden je Hafen (zusätzlich, wenn viel Fracht wartet).</summary>
        public const int HarborCallEveryHours = 3;

        // --- Hafen
        /// <summary>Hafen rastet am Ufer ein: sucht Wasser im Umkreis (m).</summary>
        public const float HarborSnapRadius = 90f;
        /// <summary>Kaimauer ragt mindestens so weit ins Wasser (m) – höchstens <see cref="MaxQuayReach"/> (aufgeschüttet).</summary>
        public const float QuayOverhang = 4f, MaxQuayReach = 80f;
        /// <summary>Liegeplatz: Schiffsmitte so weit vor der Kaikante (+ halbe Breite des Schiffs).</summary>
        public const float BerthGap = 3f;
        /// <summary>Wendebecken/Anfahrpunkt vor der Kaimitte (m).</summary>
        public const float HarborApproach = 70f;
        /// <summary>Höhenunterschied im Hafengelände (wird auf Kaihöhe abgetragen/aufgeschüttet).</summary>
        public const float HarborMaxSlope = 20f;
        /// <summary>Kaihöhe über dem Wasserspiegel.</summary>
        public const float QuayHeight = 2.5f;
        /// <summary>Zufahrt an der Vorderseite (Rand ≤, wie Bahnhöfe).</summary>
        public const float HarborRoadReach = 8f;
        /// <summary>Liegezeit eines Frachters: Grundzeit + je umgeschlagener Ladung (s Verkehrszeit; 450 s = 1 Spielstunde).</summary>
        public const float DwellBase = 900f, DwellPerLoad = 4f;
        /// <summary>Ausflugsschiffe/Yachten legen nicht an; Fahrgastschiff im Durchgang hält kurz an keinem Kai (Stufe 3).</summary>
        public const float ExcursionDwell = 0f;

        // --- Kasse
        /// <summary>Hafengeld je Anlauf (Binnenschiff / Küstenfrachter) und Umschlag je Ladung.</summary>
        public const double BargeCallFee = 220.0, CoasterCallFee = 600.0, LoadFee = 4.0;

        // --- Güter
        /// <summary>
        /// Fernfracht per Schiff (Anteil am Warenwert) statt <see cref="Goods.GoodsRules.WorldFreightShare"/> per Lkw: Seeweg
        /// ≈ 0,004 €/t·km gegen 0,03 €/t·km über Land (Etappe 8), dafür langsamer und nur in großen Mengen. Binnenhafen etwas teurer.
        /// </summary>
        public const double SeaFreightShare = 0.025, InlandFreightShare = 0.035;
    }

    /// <summary>Wofür ein Schiff gebaut ist.</summary>
    public enum ShipRole : byte { Cargo, Passenger, Leisure }

    /// <summary>Schiffsklasse: Maße, Tiefgang, Masthöhe (Höhe über Wasser), Tempo, Ladung, Modell.</summary>
    public sealed class ShipClass
    {
        public string Id { get; }
        public string DisplayName { get; }
        public ShipRole Role { get; }
        public float Length { get; }
        public float Beam { get; }
        /// <summary>Tiefgang (m unter der Wasserlinie).</summary>
        public float Draft { get; }
        /// <summary>Masthöhe: höchster Punkt über der Wasserlinie (m) – muss unter jeder festen Brücke durchpassen.</summary>
        public float AirDraft { get; }
        /// <summary>Reisegeschwindigkeit (m/s) und Beschleunigung (m/s²).</summary>
        public float Speed { get; }
        public float Acceleration { get; }
        /// <summary>Ladungen (Lkw-Ladungen) je Anlauf.</summary>
        public int Capacity { get; }
        /// <summary>Modell-Id in <c>OwnModelCatalog.Transport</c> (null = Platzhalter-Rumpf).</summary>
        public string ModelId { get; }

        public ShipClass(string id, string name, ShipRole role, float length, float beam, float draft, float airDraft, float kmh, float acc, int capacity, string model)
        {
            Id = id; DisplayName = name; Role = role; Length = length; Beam = beam; Draft = draft; AirDraft = airDraft;
            Speed = kmh / 3.6f; Acceleration = acc; Capacity = capacity; ModelId = model;
        }

        /// <summary>Braucht so viel Wassertiefe.</summary>
        public float RequiredDepth => Draft + ShippingRules.KeelClearance;
        /// <summary>Braucht so viel lichte Höhe unter festen Brücken.</summary>
        public float RequiredClearance => AirDraft + ShippingRules.AirSafety;
        public override string ToString() => DisplayName;
    }

    public static class ShipCatalog
    {
        /// <summary>Gütermotorschiff (Europaschiff-Klasse, Steuerhaus absenkbar) – Binnenhafen und Durchgang auf dem Fluss.</summary>
        public static readonly ShipClass Barge = new ShipClass("barge", "Binnenschiff", ShipRole.Cargo, 85f, 9.5f, 2.8f, 6.5f, 14f, 0.04f, 60, null);
        /// <summary>Küstenmotorschiff/Feeder (Modell „CargoShip“, 80 × 13 m, Brückenhaus ≈ 24 m) – Seehafen.</summary>
        public static readonly ShipClass Coaster = new ShipClass("coaster", "Küstenfrachter", ShipRole.Cargo, 80f, 13f, 5.5f, 24f, 22f, 0.03f, 160, "ship_cargo");
        /// <summary>Fahrgastschiff (Modell „Ferry“, 42 × 12 m, Schornsteine ≈ 11 m) – Ausflugsfahrten im Sommer.</summary>
        public static readonly ShipClass Excursion = new ShipClass("excursion", "Fahrgastschiff", ShipRole.Passenger, 42f, 12f, 1.6f, 11f, 13f, 0.05f, 0, "ship_ferry");
        /// <summary>Segelyacht (Modell „Sailboat“, Mast ≈ 13 m) – öffnet Klappbrücken, passt unter kaum eine feste Brücke.</summary>
        public static readonly ShipClass Yacht = new ShipClass("yacht", "Segelyacht", ShipRole.Leisure, 9f, 3f, 1.6f, 13f, 9f, 0.08f, 0, "ship_sailboat");

        public static readonly IReadOnlyList<ShipClass> All = new[] { Barge, Coaster, Excursion, Yacht };

        public static ShipClass Get(string id)
        {
            foreach (var c in All) if (c.Id == id) return c;
            return null;
        }
    }

    /// <summary>Bauwerkstyp eines Hafens (Kai an der Rückseite = Wasser, Vorderseite zur Straße).</summary>
    public sealed class HarborType
    {
        public string Id { get; }
        public string DisplayName { get; }
        /// <summary>Breite = Kailänge (entlang des Ufers) und Tiefe (Land, Kran, Lager).</summary>
        public float Width { get; }
        public float Depth { get; }
        public int Berths { get; }
        public double Cost { get; }
        public double UpkeepPerWeek { get; }

        public HarborType(string id, string name, float width, float depth, int berths, double cost, double upkeep)
        {
            Id = id; DisplayName = name; Width = width; Depth = depth; Berths = berths; Cost = cost; UpkeepPerWeek = upkeep;
        }

        public override string ToString() => DisplayName;
    }

    public static class HarborCatalog
    {
        public static readonly HarborType Cargo = new HarborType("cargo_harbor", "Güterhafen", 112f, 56f, 2, 260000, 4200);
        public static readonly IReadOnlyList<HarborType> All = new[] { Cargo };

        public static HarborType Get(string id)
        {
            foreach (var t in All) if (t.Id == id) return t;
            return null;
        }
    }

    public enum HarborBuildError
    {
        None,
        OutOfWorld,
        /// <summary>Kein Wasser in der Nähe (Hafen rastet am Ufer ein).</summary>
        NoShore,
        /// <summary>Zu viel Wasser unter dem Gelände bzw. Kai liegt nicht am Ufer.</summary>
        OnWater,
        /// <summary>Vor dem Kai ist das Wasser zu flach für das kleinste Frachtschiff.</summary>
        TooShallow,
        /// <summary>Liegeplätze nicht über die Fahrrinne mit einem Wasser-Gateway verbunden.</summary>
        NoFairway,
        TooSteep,
        OverlapsRoad,
        OverlapsTrack,
        OverlapsStructure,
        InsufficientFunds,
        /// <summary>Kacheln (hinten angefügt): Hafengelände auf einer nicht gekauften Kachel.</summary>
        TileNotOwned,
    }

    /// <summary>Gebauter Hafen: Kai an tiefem Wasser (Rückseite), Kräne, Lager, Straßenfront.</summary>
    public sealed class Harbor
    {
        public int Id { get; internal set; }
        public HarborType Type { get; internal set; }
        public string Name { get; internal set; } = "";
        public Vector2 Position { get; internal set; }
        /// <summary>Vorderseite (zur Straße, vom Wasser weg) – der Kai liegt an der Rückseite (−Front).</summary>
        public Vector2 Front { get; internal set; }
        /// <summary>Kaihöhe (Gelände des Hafens).</summary>
        public float Height { get; internal set; }

        // --- abgeleitet
        public int AccessSegmentId { get; internal set; } = -1;
        /// <summary>Straßenknoten an der Zufahrt: hier beginnen/enden die Lkw des Imports/Exports (0 = keiner).</summary>
        public int AccessNodeId { get; internal set; }
        /// <summary>Schiffsklasse, die der Hafen bedient (Küstenfrachter am tiefen Seeweg, sonst Binnenschiff).</summary>
        public ShipClass ShipClass { get; internal set; } = ShipCatalog.Barge;
        /// <summary>Mindestens eine Fahrrinne zu einem Wasser-Gateway ist für <see cref="ShipClass"/> frei.</summary>
        public bool Reachable { get; internal set; }
        /// <summary>Warum kein Schiff herkommt (zu niedrige Brücke …), null = alles gut.</summary>
        public string BlockedReason { get; internal set; }

        public bool HasAccess => AccessSegmentId >= 0;
        public bool InService => HasAccess && Reachable;

        // --- Statistik / Betrieb
        public int CallsToday { get; internal set; }
        public int CallsYesterday { get; internal set; }
        public int LoadsToday { get; internal set; }
        public int LoadsYesterday { get; internal set; }
        public int TotalLoads { get; internal set; }
        /// <summary>Ladungen seit dem letzten Anlauf (werden beim nächsten Schiff umgeschlagen).</summary>
        public int PendingLoads { get; internal set; }
        internal long LastCallTick = long.MinValue;
        internal int CallCounter;

        public Footprint Footprint => new Footprint(Position, Front, Type.Width, Type.Depth);
        public Vector2 Side => new Vector2(-Front.Y, Front.X);
        /// <summary>Mitte der Kaikante (Rückseite).</summary>
        public Vector2 QuayMid => Position - Front * (Type.Depth * 0.5f);
        public Vector2 FrontMid => Position + Front * (Type.Depth * 0.5f);
        public bool Contains(Vector2 p, float margin = 0f) => Footprint.Contains(p, margin);

        /// <summary>Liegeplatz i (Schiffsmitte) für eine Schiffsklasse; Schiffe liegen parallel zur Kaikante.</summary>
        public Vector2 Berth(int i, ShipClass c)
        {
            float along = (i + 0.5f) / Type.Berths - 0.5f;
            return QuayMid - Front * (c.Beam * 0.5f + ShippingRules.BerthGap) + Side * (along * Type.Width);
        }

        /// <summary>Anfahrpunkt vor dem Kai (Ende der Fahrrinne des Hafens).</summary>
        public Vector2 Approach => QuayMid - Front * ShippingRules.HarborApproach;
        public override string ToString() => $"{Type.DisplayName} {Name} #{Id}";
    }

    public sealed class HarborPlan
    {
        public HarborType Type;
        public Vector2 Position, Front;
        public float Height;
        public double Cost;
        public HarborBuildError Error;
        public StructureBlock BlockedBy;
        /// <summary>Wassertiefe vor dem Kai (m) und die Schiffsklasse, die anlegen könnte.</summary>
        public float BerthDepth;
        public ShipClass ShipClass;
        public bool HasRoadAccess;
        public bool IsValid => Error == HarborBuildError.None;
    }

    /// <summary>Wohin eine Fahrrinne führt.</summary>
    public enum WaterwayKind : byte
    {
        /// <summary>Durchgang zwischen zwei Wasser-Gateways (Fluss quer über die Karte, Fluss ↔ Meer).</summary>
        Through,
        /// <summary>Vom Wasser-Gateway zum Anfahrpunkt eines Hafens.</summary>
        Harbor,
    }

    /// <summary>Stelle, an der eine Straße oder ein Gleis die Fahrrinne quert (Brücke, Klappbrücke; Tunnel zählen nicht).</summary>
    public sealed class WaterwayCrossing
    {
        /// <summary>Strecke auf der Fahrrinne (in Punktreihenfolge).</summary>
        public float S { get; internal set; }
        public bool Rail { get; internal set; }
        public int SegmentId { get; internal set; }
        public Vector2 Position { get; internal set; }
        /// <summary>Lichte Höhe über dem Wasserspiegel (Unterkante Tragwerk, tiefster Punkt über der Fahrrinne).</summary>
        public float Clearance { get; internal set; }
        /// <summary>Klappbrücke (öffnet) – sonst feste Brücke.</summary>
        public Drawbridge Bridge { get; internal set; }
        public bool IsDrawbridge => Bridge != null;
        /// <summary>Kann ein Schiff dieser Klasse hier durch (fest: hoch genug; Klappbrücke: immer)?</summary>
        public bool Passable(ShipClass c) => IsDrawbridge || Clearance >= c.RequiredClearance;
        /// <summary>Muss die Klappbrücke für diese Klasse öffnen?</summary>
        public bool NeedsOpening(ShipClass c) => IsDrawbridge && Clearance < c.RequiredClearance;
    }

    /// <summary>
    /// Fahrrinne: schiffbarer Weg als Linienzug durch tiefes Wasser (aus dem Navigationsraster, Mitte des Gewässers bevorzugt)
    /// mit Tiefe und Breite je Punkt. Abgeleitet (Gelände beim Kartenstart + Lage der Häfen), nicht gespeichert.
    /// </summary>
    public sealed class Waterway
    {
        public int Id { get; internal set; }
        public WaterwayKind Kind { get; internal set; }
        /// <summary>Wasser-Gateway am Anfang (Punkt 0) und am Ende (Durchgang) bzw. 0.</summary>
        public int GatewayA { get; internal set; }
        public int GatewayB { get; internal set; }
        /// <summary>Hafen am Ende (Kind Harbor), sonst 0.</summary>
        public int HarborId { get; internal set; }
        /// <summary>Lage der Enden (Gateway-Zuordnung über die Lage).</summary>
        public Vector2 EndA { get; internal set; }
        public Vector2 EndB { get; internal set; }
        /// <summary>Seeweg (am Meer) – sonst Binnenwasserstraße.</summary>
        public bool Sea { get; internal set; }

        internal Vector2[] Points = Array.Empty<Vector2>();
        internal float[] Cum = Array.Empty<float>();
        internal float[] Depth = Array.Empty<float>();
        internal float[] Width = Array.Empty<float>();
        public IReadOnlyList<Vector2> Path => Points;
        public float Length { get; internal set; }
        public float MinDepth { get; internal set; }
        public float MinWidth { get; internal set; }
        /// <summary>Lichte Höhe, für die die Rinne ausgelegt ist (Hinweis „zu niedrig“ darunter).</summary>
        public float DesignClearance => Sea ? ShippingRules.SeaDesignClearance : ShippingRules.RiverDesignClearance;

        internal readonly List<WaterwayCrossing> CrossingList = new List<WaterwayCrossing>();
        public IReadOnlyList<WaterwayCrossing> Crossings => CrossingList;

        /// <summary>Tief genug für die Klasse?</summary>
        public bool DeepEnough(ShipClass c) => MinDepth >= c.RequiredDepth;

        /// <summary>Kann die Klasse die ganze Rinne befahren (Tiefe, alle festen Brücken hoch genug)?</summary>
        public bool Passable(ShipClass c) => DeepEnough(c) && FirstBlocking(c) == null;

        /// <summary>Erste feste Brücke, die für die Klasse zu niedrig ist (null = frei).</summary>
        public WaterwayCrossing FirstBlocking(ShipClass c)
        {
            foreach (var x in CrossingList) if (!x.Passable(c)) return x;
            return null;
        }

        /// <summary>Punkt, Richtung und Breite an Strecke s (in Punktreihenfolge, außerhalb begrenzt).</summary>
        public void Sample(float s, out Vector2 position, out Vector2 direction)
        {
            int n = Points.Length;
            if (n == 0) { position = default; direction = Vector2.UnitX; return; }
            if (n == 1) { position = Points[0]; direction = Vector2.UnitX; return; }
            if (s <= 0f) { position = Points[0]; direction = Vector2.Normalize(Points[1] - Points[0]); position += direction * s; return; }
            if (s >= Length) { direction = Vector2.Normalize(Points[n - 1] - Points[n - 2]); position = Points[n - 1] + direction * (s - Length); return; }
            int i = Index(s);
            float seg = Cum[i + 1] - Cum[i];
            float u = seg > 1e-4f ? (s - Cum[i]) / seg : 0f;
            position = Vector2.Lerp(Points[i], Points[i + 1], u);
            var d = Points[i + 1] - Points[i];
            direction = d.LengthSquared() > 1e-8f ? Vector2.Normalize(d) : Vector2.UnitX;
        }

        public float WidthAt(float s)
        {
            if (Width.Length == 0) return 0f;
            int i = Index(Math.Clamp(s, 0f, Length));
            return Width[Math.Min(i, Width.Length - 1)];
        }

        /// <summary>Halbe Breite der Fahrrinne (Kanal) an Strecke s.</summary>
        public float ChannelHalfWidth(float s) =>
            Math.Clamp(WidthAt(s) * ShippingRules.ChannelShare, ShippingRules.MinChannelHalfWidth, ShippingRules.MaxChannelHalfWidth);

        int Index(float s)
        {
            int lo = 0, hi = Cum.Length - 2;
            while (lo < hi)
            {
                int mid = (lo + hi + 1) / 2;
                if (Cum[mid] <= s) lo = mid; else hi = mid - 1;
            }
            return lo;
        }

        /// <summary>Nächste Strecke s und Abstand eines Punkts zum Linienzug.</summary>
        public float Project(Vector2 p, out float distance)
        {
            float best = float.MaxValue, bestS = 0f;
            for (int i = 0; i + 1 < Points.Length; i++)
            {
                var a = Points[i]; var b = Points[i + 1];
                var ab = b - a;
                float len2 = ab.LengthSquared();
                float u = len2 > 1e-6f ? Math.Clamp(Vector2.Dot(p - a, ab) / len2, 0f, 1f) : 0f;
                float d = Vector2.Distance(p, a + ab * u);
                if (d < best) { best = d; bestS = Cum[i] + (Cum[i + 1] - Cum[i]) * u; }
            }
            distance = best;
            return bestS;
        }

        public override string ToString() => Kind == WaterwayKind.Through ? $"Fahrrinne #{Id} (Durchgang, {Length:0} m)" : $"Fahrrinne #{Id} (Hafen {HarborId}, {Length:0} m)";
    }

    /// <summary>Zustand einer Klappbrücke (Straße frei ↔ Klappe offen).</summary>
    public enum DrawbridgeState : byte
    {
        /// <summary>Klappe unten, Schranken offen – Straßenverkehr fährt.</summary>
        RoadOpen,
        /// <summary>Ampel rot, Schranken senken sich, Fahrzeuge räumen die Klappe.</summary>
        Warning,
        /// <summary>Klappe hebt sich.</summary>
        Raising,
        /// <summary>Klappe oben – Schiffe fahren durch.</summary>
        Open,
        /// <summary>Klappe senkt sich, danach öffnen die Schranken.</summary>
        Lowering,
    }

    /// <summary>Klappbrücke: Straßenstück mit <see cref="RoadSegmentFlags.Drawbridge"/> über der Fahrrinne (abgeleitet, Zustand gespeichert).</summary>
    public sealed class Drawbridge
    {
        public int SegmentId { get; internal set; }
        public Vector2 Position { get; internal set; }
        /// <summary>Parameter des Fahrrinnen-Schnittpunkts auf dem Straßenstück.</summary>
        public float RoadT { get; internal set; }
        /// <summary>Fahrbahnhöhe der Klappe.</summary>
        public float Height { get; internal set; }
        /// <summary>Lichte Höhe geschlossen (kleine Boote passen auch so durch).</summary>
        public float ClosedClearance { get; internal set; }
        public DrawbridgeState State { get; internal set; }
        internal double StateSince, RoadOpenSince;
        /// <summary>Öffnungen heute/gestern und gesamt.</summary>
        public int OpeningsToday { get; internal set; }
        public int OpeningsYesterday { get; internal set; }
        public int OpeningsTotal { get; internal set; }
        /// <summary>Summe der Sperrzeit für den Straßenverkehr heute (s Verkehrszeit).</summary>
        public double ClosedSecondsToday { get; internal set; }
        /// <summary>Schiffe, die gerade vor der Brücke warten bzw. angemeldet sind (abgeleitet je Schritt).</summary>
        public int ShipsWaiting { get; internal set; }
        public int ShipsRequesting { get; internal set; }
        /// <summary>Straße gesperrt (Schranke unten)?</summary>
        public bool RoadClosed => State != DrawbridgeState.RoadOpen;
        /// <summary>0 = Klappe unten, 1 = ganz oben (Darstellung, aus Zustand und Zeit).</summary>
        public float Lift { get; internal set; }
        public override string ToString() => $"Klappbrücke Straße {SegmentId} ({State})";
    }

    /// <summary>Was ein Schiff gerade tut.</summary>
    public enum ShipState : byte
    {
        /// <summary>Vor der Karte, wartet auf freie Einfahrt.</summary>
        Waiting,
        Underway,
        /// <summary>Hält vor einer geschlossenen Klappbrücke.</summary>
        WaitingBridge,
        /// <summary>Wartet auf einen freien Liegeplatz (Reede vor dem Hafen).</summary>
        WaitingBerth,
        /// <summary>Liegt am Kai (Umschlag).</summary>
        Moored,
    }

    public enum ShipPurpose : byte
    {
        /// <summary>Durchgangsschiff zwischen zwei Wasser-Gateways (öffnet Klappbrücken auch ohne Hafen).</summary>
        Through,
        /// <summary>Frachter auf dem Weg zum Hafen.</summary>
        Inbound,
        /// <summary>Frachter verlässt den Hafen zum Wasser-Gateway.</summary>
        Outbound,
    }

    /// <summary>
    /// Ein Schiff auf einer Fahrrinne. <see cref="S"/> = Lage des Bugs auf der gerichteten Fahrt (0 = Beginn der Fahrt,
    /// negativ = noch draußen); <see cref="Forward"/> = Fahrt in Punktreihenfolge der Fahrrinne.
    /// </summary>
    public sealed class Ship
    {
        public int Id { get; internal set; }
        public ShipClass Class { get; internal set; }
        public ShipPurpose Purpose { get; internal set; }
        public ShipState State { get; internal set; }
        public Waterway Waterway { get; internal set; }
        public bool Forward { get; internal set; }
        public float S { get; internal set; }
        public float PrevS { get; internal set; }
        public float Speed { get; internal set; }
        public int HarborId { get; internal set; }
        public int Berth { get; internal set; } = -1;
        public int FromGatewayId { get; internal set; }
        public int ToGatewayId { get; internal set; }
        /// <summary>Ladungen an Bord (Anzeige; umgeschlagen am Kai).</summary>
        public int Loads { get; internal set; }
        /// <summary>Fahrgäste an Bord (Ausflugsschiff, Anzeige).</summary>
        public int Passengers { get; internal set; }
        internal double SpawnTime, WaitSince, DwellUntil;
        /// <summary>Gesamte Wartezeit vor Klappbrücken/Liegeplätzen (s Verkehrszeit).</summary>
        public double WaitedSeconds { get; internal set; }
        /// <summary>Wartet gerade auf diese Klappbrücke (Straßenstück, 0 = keine).</summary>
        public int WaitingForBridge { get; internal set; }

        public bool IsOnMap => State != ShipState.Waiting;
        public override string ToString() => $"{Class.DisplayName} #{Id}";
    }

    /// <summary>Prüfung einer geplanten Brücke gegen die Fahrrinnen (Hinweis im Werkzeug).</summary>
    public struct BridgeCheck
    {
        /// <summary>Quert die Planung eine Fahrrinne?</summary>
        public bool CrossesFairway;
        /// <summary>Kleinste lichte Höhe über einer Fahrrinne (m) und die verlangte Höhe der Rinne.</summary>
        public float Clearance, Design;
        /// <summary>Unter einer Fahrrinne hindurch (Tunnel) – kein Hindernis.</summary>
        public bool Tunnel;
        public bool Drawbridge;
        /// <summary>Hinweistext (null = keine Fahrrinne betroffen).</summary>
        public string Text;
        /// <summary>Zu niedrig für die Bemessungsschiffe (Fahrrinne gesperrt).</summary>
        public bool TooLow;
    }

    /// <summary>Geplante Klappbrücke: Zufahrt, Klappe über der Fahrrinne, Zufahrt (drei Straßenstücke).</summary>
    public sealed class DrawbridgePlan
    {
        public RoadType Type;
        public Vector2 Start, End;
        /// <summary>Anfang und Ende der Klappe auf der Linie.</summary>
        public Vector2 SpanStart, SpanEnd;
        public float Deck;
        public RoadPlan ApproachA, Span, ApproachB;
        public RoadBuildError Error;
        /// <summary>Kosten gesamt (Fahrbahn + Mechanik).</summary>
        public double Cost;
        public Waterway Waterway;
        public bool IsValid => Error == RoadBuildError.None;
    }
}
