using System;
using System.Collections.Generic;
using System.Numerics;

namespace CityBuilder.Simulation.World
{
    /// <summary>
    /// Die fiktive Erde: grobes Zellraster (Länge × Breite, Standard 360 × 180 → ~111 km je Zelle am Äquator)
    /// mit Relief, Klima, Biomen, Böden und Bodenschätzen, aufgeteilt in Regionen und Länder.
    /// Zelle (0,0) liegt im Nordwesten (180° W, 90° N); x läuft nach Osten (Länge wiederholt sich), y nach Süden.
    /// Reines C#, erzeugt von <see cref="PlanetGenerator"/>.
    /// </summary>
    public sealed class Planet
    {
        public const float EarthCircumferenceKm = 40075f;

        public int Seed { get; }
        public int Width { get; }
        public int Height { get; }
        public int CellCount => Width * Height;

        // --- Felder je Zelle (Index = y * Width + x)
        /// <summary>Höhe über dem Meeresspiegel in m (negativ = Meerestiefe).</summary>
        public readonly float[] Elevation;
        /// <summary>Jahresmitteltemperatur °C.</summary>
        public readonly float[] Temperature;
        /// <summary>Halbe Jahresschwankung der Monatsmittel in K (Kontinentalität).</summary>
        public readonly float[] Amplitude;
        /// <summary>Jahresniederschlag mm.</summary>
        public readonly float[] Precipitation;
        public readonly Biome[] BiomeOf;
        /// <summary>0..1: Bodenfruchtbarkeit, Waldbedeckung und Bodenschätze (relative Ergiebigkeit).</summary>
        public readonly float[] Fertility, Forest, Ore, Coal, Oil, Sand, Groundwater;
        /// <summary>Abfluss (Summe des Niederschlags flussaufwärts, in Zell-Metern); Flüsse ab <see cref="RiverThreshold"/>.</summary>
        public readonly float[] Flow;
        /// <summary>0..1: Gebirgsbildung an konvergenten Plattengrenzen (heute und alt).</summary>
        public readonly float[] Orogeny;
        /// <summary>0..1: Sedimentbecken (Senken, Vorlandbecken, Schelf).</summary>
        public readonly float[] Basin;
        /// <summary>Lithosphärenplatte je Zelle.</summary>
        public readonly int[] Plate;
        /// <summary>Region je Landzelle (−1 = Meer).</summary>
        public readonly int[] RegionOf;
        /// <summary>Meer: Region der nächsten Küste (für Schelf-Öl), −1 wenn weiter als <see cref="MaritimeReach"/> Zellen.</summary>
        public readonly int[] MaritimeRegion;
        /// <summary>Abstand zur Küste in Zellen (Land: zum Meer, Meer: zum Land), max. 255.</summary>
        public readonly byte[] CoastDistance;

        public const int MaritimeReach = 3;

        public float RiverThreshold { get; internal set; }

        public List<Region> Regions { get; } = new List<Region>();
        public List<Country> Countries { get; } = new List<Country>();

        /// <summary>Aggregierte Volkswirtschaften aller Länder (LOD-Simulation).</summary>
        public WorldEconomy Economy { get; internal set; }

        /// <summary>Region der Spielerstadt bzw. −1.</summary>
        public int StartRegionId { get; private set; } = -1;
        public int PlayerCountryId => StartRegionId >= 0 ? Regions[StartRegionId].CountryId : -1;
        public Country PlayerCountry => PlayerCountryId >= 0 ? Countries[PlayerCountryId] : null;
        public Region StartRegion => StartRegionId >= 0 ? Regions[StartRegionId] : null;

        /// <summary>Gemessene Erzeugungsdauer in ms (Info).</summary>
        public double GenerationMilliseconds { get; internal set; }

        internal Planet(int seed, int width, int height)
        {
            Seed = seed;
            Width = width;
            Height = height;
            int n = width * height;
            Elevation = new float[n]; Temperature = new float[n]; Amplitude = new float[n]; Precipitation = new float[n];
            BiomeOf = new Biome[n];
            Fertility = new float[n]; Forest = new float[n]; Ore = new float[n]; Coal = new float[n]; Oil = new float[n];
            Sand = new float[n]; Groundwater = new float[n]; Flow = new float[n]; Orogeny = new float[n]; Basin = new float[n];
            Plate = new int[n]; RegionOf = new int[n]; MaritimeRegion = new int[n]; CoastDistance = new byte[n];
        }

        // ------------------------------------------------------------ Geometrie

        public int Index(int x, int y) => y * Width + x;
        public int XOf(int i) => i % Width;
        public int YOf(int i) => i / Width;

        /// <summary>Geografische Breite der Zellmitte in Grad (+ = Nord).</summary>
        public float Latitude(int y) => 90f - (y + 0.5f) * 180f / Height;
        public float LatitudeOf(int i) => Latitude(YOf(i));
        /// <summary>Geografische Länge der Zellmitte in Grad (+ = Ost).</summary>
        public float Longitude(int x) => (x + 0.5f) * 360f / Width - 180f;

        /// <summary>Zelle zu Länge/Breite (Grad).</summary>
        public int CellAt(float latitude, float longitude)
        {
            int y = Math.Clamp((int)MathF.Floor((90f - latitude) / 180f * Height), 0, Height - 1);
            float lon = ((longitude + 180f) % 360f + 360f) % 360f;
            int x = Math.Clamp((int)MathF.Floor(lon / 360f * Width), 0, Width - 1);
            return Index(x, y);
        }

        /// <summary>Kantenlänge einer Zelle (Nord-Süd) in km.</summary>
        public float CellKm => EarthCircumferenceKm * 0.5f / Height;

        public float CellAreaKm2(int y)
        {
            float dx = EarthCircumferenceKm / Width * MathF.Cos(Latitude(y) * MathF.PI / 180f);
            return dx * CellKm;
        }

        /// <summary>Nachbarzelle (Länge wiederholt sich, über die Pole hinaus −1).</summary>
        public int Neighbor(int i, int dx, int dy)
        {
            int y = YOf(i) + dy;
            if (y < 0 || y >= Height) return -1;
            int x = (XOf(i) + dx + Width) % Width;
            return y * Width + x;
        }

        internal static readonly int[] Dx8 = { 1, -1, 0, 0, 1, 1, -1, -1 };
        internal static readonly int[] Dy8 = { 0, 0, 1, -1, 1, -1, 1, -1 };

        /// <summary>Einheitsvektor der Zellmitte auf der Kugel.</summary>
        public Vector3 UnitVector(int i)
        {
            float lat = LatitudeOf(i) * MathF.PI / 180f, lon = Longitude(XOf(i)) * MathF.PI / 180f;
            float c = MathF.Cos(lat);
            return new Vector3(c * MathF.Cos(lon), MathF.Sin(lat), c * MathF.Sin(lon));
        }

        public bool IsLand(int i) => Elevation[i] >= 0f;
        public bool IsRiver(int i) => IsLand(i) && Flow[i] >= RiverThreshold;

        /// <summary>Größter Höhenunterschied zu den 8 Nachbarn (m) – Maß für Gebirge/Zerklüftung.</summary>
        public float Ruggedness(int i)
        {
            float h = MathF.Max(0f, Elevation[i]), r = 0f;
            for (int k = 0; k < 8; k++)
            {
                int j = Neighbor(i, Dx8[k], Dy8[k]);
                if (j < 0) continue;
                r = MathF.Max(r, MathF.Abs(MathF.Max(0f, Elevation[j]) - h));
            }
            return r;
        }

        /// <summary>Land einer Zelle; null für Meer und für unbesiedeltes (freies) Land.</summary>
        public Country CountryOfCell(int i)
        {
            int r = RegionOf[i];
            return r >= 0 && Regions[r].CountryId >= 0 ? Countries[Regions[r].CountryId] : null;
        }

        // ------------------------------------------------------------ Startregion

        // --- Spielstart & Startregion: freies Land, Spieler als Stadtstaat
        /// <summary>Anzahl der Regionen ohne Land (unbesiedelt, „frei“).</summary>
        public int FreeRegionCount
        {
            get { int n = 0; foreach (var r in Regions) if (r.IsFree) n++; return n; }
        }

        /// <summary>Kann der Spieler hier (neu) beginnen? Freie Region oder die Region seines Stadtstaats.</summary>
        public static bool IsStartable(Region r) => r.IsFree || (r.Country != null && r.Country.IsCityState);

        /// <summary>Stadtstaat des Spielers (null, wenn der Spieler in einem NPC-Land begonnen hat – alte Spielstände).</summary>
        public Country PlayerCityState
        {
            get { foreach (var c in Countries) if (c.IsCityState) return c; return null; }
        }

        /// <summary>
        /// Setzt die Region der Spielerstadt. <b>Freie Region</b> (Normalfall): Der Spieler wird ein eigener
        /// <b>Stadtstaat</b> (<see cref="Country.IsCityState"/>, hinten an <see cref="Countries"/> angehängt, eigene kleine
        /// Volkswirtschaft) – erneuter Aufruf mit einer anderen freien Region verlegt ihn. <b>Region eines NPC-Landes</b>
        /// (alte Spielstände ohne freies Land): dieses Land wird Spieler-Land wie bisher. Vor dem Anlegen des Handels aufrufen.
        /// </summary>
        public void SetStartRegion(int regionId)
        {
            if (regionId < 0 || regionId >= Regions.Count) throw new ArgumentOutOfRangeException(nameof(regionId));
            var region = Regions[regionId];
            var cityState = PlayerCityState;
            if (cityState != null)
            {
                if (!region.IsFree && region.CountryId != cityState.Id)
                    throw new InvalidOperationException("Der Stadtstaat kann nur in eine freie Region verlegt werden.");
                StartRegionId = regionId;
                PlanetGenerator.PlaceCityState(this, cityState, region);
                return;
            }
            if (StartRegionId >= 0) Countries[Regions[StartRegionId].CountryId].IsPlayer = false;
            StartRegionId = regionId;
            if (region.IsFree)
                PlanetGenerator.PlaceCityState(this, null, region);
            else
                Countries[region.CountryId].IsPlayer = true;
        }

        /// <summary>Region einem Land zuordnen (null = frei); hält <see cref="Region.CountryId"/>/<see cref="Region.Country"/> und <see cref="Country.Regions"/> gleich.</summary>
        internal void AssignRegion(Region r, Country c)
        {
            if (r.Country == c && r.CountryId == (c?.Id ?? -1)) return;
            r.Country?.Regions.Remove(r.Id);
            r.Country = c;
            r.CountryId = c?.Id ?? -1;
            if (c != null && !c.Regions.Contains(r.Id))
            {
                c.Regions.Add(r.Id);
                c.Regions.Sort();
            }
        }
        // --- Ende Spielstart

        /// <summary>
        /// Gute Startregion: gemäßigtes Klima, fruchtbar, Fluss oder Küste (Wasser für Pumpe/Hafen), nicht zu
        /// zerklüftet. Gibt es freies Land, nur unter den freien Regionen (Spieler startet als Stadtstaat).
        /// Deterministisch (bei Gleichstand kleinste Id).
        /// </summary>
        public int SuggestStartRegion()
        {
            int best = 0;
            float bestScore = float.MinValue;
            bool anyFree = false;
            foreach (var r in Regions) if (IsStartable(r)) { anyFree = true; break; }
            foreach (var r in Regions)
            {
                if (r.Cells.Count < 4) continue;
                if (anyFree && !IsStartable(r)) continue;
                float climate = 1f - MathF.Min(1f, MathF.Abs(r.MeanTemperature - 11f) / 12f);
                float water = (r.HasRiver ? 0.6f : 0f) + (r.IsCoastal ? 0.4f : 0f);
                float relief = 1f - MathF.Min(1f, MathF.Abs(r.Ruggedness - 300f) / 900f);
                float s = climate * 1.4f + r.Fertility + water + relief * 0.5f + r.Forest * 0.3f;
                if (s > bestScore) { bestScore = s; best = r.Id; }
            }
            return best;
        }
    }

    /// <summary>Region = zusammenhängender Landstrich (einige Weltzellen) mit gemittelten Natureigenschaften. Jede Stadtkarte ist ein Ausschnitt einer Region.</summary>
    public sealed class Region
    {
        public int Id { get; internal set; }
        public string Name { get; internal set; }
        /// <summary>Land der Region; −1 = unbesiedeltes, freies Land (dort kann der Spieler eine Stadt gründen).</summary>
        public int CountryId { get; internal set; } = -1;
        /// <summary>Land der Region (null = frei). Gleich zu <see cref="CountryId"/>.</summary>
        public Country Country { get; internal set; }
        /// <summary>Unbesiedeltes Land ohne Staat (Status „frei“).</summary>
        public bool IsFree => CountryId < 0;
        public List<int> Cells { get; } = new List<int>();
        /// <summary>Benachbarte Regionen über Land.</summary>
        public List<int> Neighbors { get; } = new List<int>();
        public int CenterCell { get; internal set; }
        public float Latitude { get; internal set; }
        public float Longitude { get; internal set; }
        public float AreaKm2 { get; internal set; }

        public float MeanElevation { get; internal set; }
        public float MaxElevation { get; internal set; }
        /// <summary>Mittlere Zerklüftung (m Höhenunterschied zwischen Nachbarzellen).</summary>
        public float Ruggedness { get; internal set; }
        public float MeanTemperature { get; internal set; }
        public float SeasonalAmplitude { get; internal set; }
        public float Precipitation { get; internal set; }
        public Biome DominantBiome { get; internal set; }

        public float Fertility { get; internal set; }
        public float Forest { get; internal set; }
        public float Ore { get; internal set; }
        public float Coal { get; internal set; }
        /// <summary>Öl inkl. Schelf vor der Küste.</summary>
        public float Oil { get; internal set; }
        public float Sand { get; internal set; }
        public float Groundwater { get; internal set; }

        public float MaxFlow { get; internal set; }
        public bool HasRiver { get; internal set; }
        public int CoastCells { get; internal set; }
        public bool IsCoastal => CoastCells > 0;
        /// <summary>Richtung zum Meer (x = Ost, y = Nord), Länge 1; null ohne Küste.</summary>
        public Vector2? CoastDirection { get; internal set; }

        public override string ToString() => Name;
    }

    /// <summary>Ein Land: Regionen, Hauptstadt, Stärken (komparative Vorteile) und aggregierte Volkswirtschaft.</summary>
    public sealed class Country
    {
        public int Id { get; internal set; }
        public string Name { get; internal set; }
        public string CapitalName { get; internal set; }
        public int CapitalRegionId { get; internal set; }
        public int CapitalCell { get; internal set; }
        public List<int> Regions { get; } = new List<int>();
        public int LandCells { get; internal set; }
        public int CoastCells { get; internal set; }
        public float AreaKm2 { get; internal set; }
        public float MeanTemperature { get; internal set; }
        public float Precipitation { get; internal set; }
        public CountryProfile Profile { get; } = new CountryProfile();
        public NationEconomy Economy { get; internal set; }
        public bool IsPlayer { get; internal set; }
        /// <summary>
        /// Spieler-Gebiet ohne eigenen Staatsnamen: die „Freie Stadt {Stadtname}“ in einer vorher freien Region.
        /// Etappe 7: mehrere Städte → Land gründen (eigener Name/Flagge, s. <see cref="PlayerRealm"/>).
        /// </summary>
        public bool IsCityState { get; internal set; }
        /// <summary>Kartenfarbe RGB (0xRRGGBB).</summary>
        public uint Color { get; internal set; }

        public override string ToString() => Name;
    }

    /// <summary>Stärken eines Landes – Grundlage der komparativen Vorteile (Spezialisierung, Handel).</summary>
    public enum Strength { Tourism, Agriculture, RawMaterials, Industry, Technology }

    /// <summary>
    /// Stärken-Profil: <see cref="Raw"/> = absolute Eignung aus Natur/Lage (0..1), <see cref="this[Strength]"/> =
    /// Rang unter allen Ländern (0 = schwächstes, 1 = stärkstes Land). <see cref="Comparative"/> = Stärke relativ zu
    /// den eigenen anderen Stärken (&gt; 1 = Vorteil) – darauf spezialisiert sich ein Land, auch wenn es absolut schwach ist.
    /// </summary>
    public sealed class CountryProfile
    {
        public const int Count = 5;
        public readonly float[] Raw = new float[Count];
        public readonly float[] Rank = new float[Count];
        public readonly float[] Comparative = new float[Count];

        public float this[Strength s] => Rank[(int)s];
        public Strength Primary { get; internal set; }
        public Strength Secondary { get; internal set; }

        public static string DisplayName(Strength s)
        {
            switch (s)
            {
                case Strength.Tourism: return "Tourismus";
                case Strength.Agriculture: return "Landwirtschaft";
                case Strength.RawMaterials: return "Rohstoffe";
                case Strength.Industry: return "Industrie";
                case Strength.Technology: return "Technik";
                default: return s.ToString();
            }
        }
    }
}
