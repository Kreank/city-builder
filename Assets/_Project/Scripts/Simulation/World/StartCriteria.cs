using System;
using System.Collections.Generic;

namespace CityBuilder.Simulation.World
{
    /// <summary>Klimazone einer Region (aus Jahresmittel, Niederschlag und vorherrschendem Biom).</summary>
    public enum ClimateZone { Polar, Subpolar, Temperate, Subtropical, Tropical, Arid }

    /// <summary>Gruppen der Startvorlieben im Neues-Spiel-Fenster.</summary>
    public enum StartGroup { Climate, Location, Terrain, Focus, Difficulty, Neighbors }

    /// <summary>Eine Gruppe von Vorlieben. <see cref="Exclusive"/> = höchstens eine Option (eine neue ersetzt die alte).</summary>
    public sealed class StartGroupInfo
    {
        public StartGroup Group { get; }
        public string Name { get; }
        public string Description { get; }
        public bool Exclusive { get; }

        internal StartGroupInfo(StartGroup group, string name, bool exclusive, string description)
        {
            Group = group; Name = name; Exclusive = exclusive; Description = description;
        }
    }

    /// <summary>
    /// Eine wählbare Vorliebe (Chip): Bewertung 0..1 je Region (<see cref="Score"/>) und Mindestwert (<see cref="Min"/>) –
    /// eine Region passt, wenn Score ≥ Min. <see cref="Geology"/> erklärt, wo es so etwas auf der Erde gibt (Tooltip, wenn
    /// die Option gesperrt oder selten ist).
    /// </summary>
    public sealed class StartOption
    {
        public string Id { get; }
        public StartGroup Group { get; }
        public string Name { get; }
        /// <summary>Kurzbeschreibung (Tooltip).</summary>
        public string Description { get; }
        /// <summary>Erklärtext „warum/wo“ (Geologie, Klima) – Daten, keine Logik.</summary>
        public string Geology { get; }
        public float Min { get; }
        internal Func<RegionFacts, float> Score { get; }

        internal StartOption(string id, StartGroup group, string name, string description, string geology, float min, Func<RegionFacts, float> score)
        {
            Id = id; Group = group; Name = name; Description = description; Geology = geology; Min = min; Score = score;
        }

        public float ScoreOf(RegionFacts f) => WorldNoise.Clamp01(Score(f));
        public bool Fits(RegionFacts f) => ScoreOf(f) >= Min;
        public override string ToString() => Name;
    }

    /// <summary>
    /// Katalog der Startvorlieben (Spielstart &amp; Startregion) – Bewertungsregeln und Erklärtexte als Daten. Alle Werte
    /// kommen aus den echten Planet-Daten der Region (<see cref="RegionFacts"/>). Schwellen so gewählt, dass eine Vorliebe
    /// etwa das obere Fünftel bis Drittel der Regionen trifft (geprüft an den Seeds 2024 und 7).
    /// </summary>
    public static class StartCriteria
    {
        public static readonly IReadOnlyList<StartGroupInfo> Groups = new[]
        {
            new StartGroupInfo(StartGroup.Climate, "Klima", true, "Jahresmittel, Niederschlag und Pflanzenwelt der Region."),
            new StartGroupInfo(StartGroup.Location, "Lage", false, "Wasser in der Nähe: Meer, großer Fluss oder Binnenland."),
            new StartGroupInfo(StartGroup.Terrain, "Gelände", true, "Relief der Region (Zerklüftung und Höhe)."),
            new StartGroupInfo(StartGroup.Focus, "Schwerpunkt", false, "Wovon soll deine Stadt leben? Mehrfachauswahl."),
            new StartGroupInfo(StartGroup.Difficulty, "Schwierigkeit", true, "Leicht = mild und reich, schwer = karg oder extrem."),
            new StartGroupInfo(StartGroup.Neighbors, "Nachbarn", true, "Abstand zum nächsten Land (Handel, später Politik)."),
        };

        public static StartGroupInfo GroupInfo(StartGroup g)
        {
            foreach (var x in Groups) if (x.Group == g) return x;
            return null;
        }

        public const float Fit = 0.5f;

        public static readonly IReadOnlyList<StartOption> Options = new[]
        {
            // --- Klima (eine Zone)
            Climate("polar", ClimateZone.Polar, "Polar", "Eis und Frostschutt, Jahresmittel unter −6 °C.",
                "Polares Klima gibt es nur nahe den Polen und auf sehr hohen Gebirgen: lange Winter, kaum Landwirtschaft, viel Heizbedarf."),
            Climate("subpolar", ClimateZone.Subpolar, "Subpolar/kühl", "Tundra und Nadelwald (Taiga), kurzer Sommer.",
                "Kühles Klima liegt zwischen gemäßigter Zone und Polarkreis oder hoch im Gebirge – Holz ja, Ackerbau kaum."),
            Climate("temperate", ClimateZone.Temperate, "Gemäßigt", "4–14 °C im Jahresmittel, vier Jahreszeiten.",
                "Gemäßigtes Klima findet sich in mittleren Breiten (etwa 35–60°), oft mit Westwind und Regen von der See."),
            Climate("subtropical", ClimateZone.Subtropical, "Subtropisch/mediterran", "14–21 °C, milde Winter, trockene Sommer.",
                "Subtropen liegen am Rand der Wendekreise; viele davon sind trocken (Rossbreiten) – grüne Subtropen gibt es vor allem an Küsten."),
            Climate("tropical", ClimateZone.Tropical, "Tropisch", "Über 21 °C, Regenwald oder Savanne.",
                "Tropen liegen nahe am Äquator: ganzjährig warm, Regenzeiten, ausgewaschene Böden."),
            Climate("arid", ClimateZone.Arid, "Trocken/Wüste", "Wüste oder sehr wenig Regen.",
                "Wüsten entstehen in den Rossbreiten (absinkende Luft) und im Regenschatten hoher Gebirge – Wasser ist knapp, Grundwasser oft fossil."),

            // --- Lage (Mehrfachauswahl, alle müssen passen)
            new StartOption("coast", StartGroup.Location, "Meeresküste", "Die Region grenzt an das Meer (Hafen, Fischerei, Tourismus).",
                "Küsten gibt es an jedem Kontinent – im Hochgebirge oder tief im Binnenland natürlich nicht.", Fit, f => f.CoastScore),
            new StartOption("river", StartGroup.Location, "Großer Fluss", "Ein wasserreicher Fluss fließt durch die Region (Wasser, Auen, Schifffahrt).",
                "Große Flüsse sammeln Regen aus weiten, feuchten Einzugsgebieten; in Wüsten und auf kleinen Inseln sind sie selten.", Fit, f => f.RiverScore),
            new StartOption("inland", StartGroup.Location, "Binnenland", "Keine Meeresküste.",
                "Binnenland und Meeresküste schließen sich aus.", Fit, f => f.Region.IsCoastal ? 0f : 1f),

            // --- Gelände (eine Art)
            new StartOption("flat", StartGroup.Terrain, "Flach", "Ebenen und Tiefland – leicht zu bebauen.",
                "Ebenen liegen in Becken, Flusstälern und auf alten, abgetragenen Kontinentkernen.", Fit, f => 1.5f - f.Relief),
            new StartOption("hilly", StartGroup.Terrain, "Hügelig", "Hügel und Mittelgebirge.",
                "Hügelland findet sich am Rand von Gebirgen und auf alten, abgetragenen Gebirgen.", Fit, f => 1f - MathF.Abs(f.Relief - 2f) / 2f),
            new StartOption("mountains", StartGroup.Terrain, "Gebirgig", "Hochgebirge mit Tälern – schwer zu bebauen, reich an Erz und Aussicht.",
                "Gebirge entstehen, wo Kontinentalplatten zusammenstoßen oder Ozeanplatten abtauchen (Faltengebirge, Vulkane).", Fit, f => (f.Relief - 2f) / 2f),

            // --- Schwerpunkt (Mehrfachauswahl)
            new StartOption("agriculture", StartGroup.Focus, "Landwirtschaft", "Fruchtbarer Boden und genug Wasser (Regen, Fluss oder Grundwasser).",
                "Fruchtbar sind Steppen mit Schwarzerde, die Auen großer Flüsse (wie am Nil) und Vulkanböden – wenn es genug regnet oder ein Fluss Wasser bringt.", Fit, f => f.Agriculture),
            new StartOption("forestry", StartGroup.Focus, "Forst", "Viel Wald (Holz, Bretter, Möbel).",
                "Wald wächst, wo es feucht und nicht zu kalt ist: gemäßigte Wälder, Taiga, Regenwald. Wüste, Tundra und Hochgebirge sind kahl.", Fit, f => f.Region.Forest / 0.6f),
            new StartOption("ore", StartGroup.Focus, "Erze", "Erzlagerstätten (Erz → Stahl).",
                "Erze entstehen, wo Platten kollidieren: Magma und Druck reichern Metalle in Gebirgen an. Auch alte, abgetragene Gebirge im Hügel- oder Flachland halten noch Erz – am Meer findet man es selten.", Fit, f => f.Region.Ore / 0.5f),
            new StartOption("coal", StartGroup.Focus, "Kohle", "Kohleflöze (Energie, Stahl).",
                "Kohle stammt aus Sumpfwäldern der Urzeit, die in absinkenden Becken begraben wurden: Tiefländer, Senken und das Vorland von Gebirgen.", Fit, f => f.Region.Coal / 0.6f),
            new StartOption("oil", StartGroup.Focus, "Öl/Gas", "Erdöl- und Erdgasfelder, auch auf dem Schelf vor der Küste.",
                "Öl und Gas sammeln sich in tiefen Sedimentbecken und auf dem flachen Schelf vor Küsten; Gebirge und alte Schilde haben kaum welches.", Fit, f => f.Region.Oil / 0.3f),
            new StartOption("building", StartGroup.Focus, "Baustoffe", "Sand und Kies (Beton, Glas, Zement).",
                "Sand und Kies lagern Flüsse, Brandung, Wüstenwind und Gletscher ab – an Ufern, Küsten, in Wüsten und im Gletschervorland.", Fit, f => (f.Region.Sand - 0.2f) / 0.6f),
            new StartOption("tourism", StartGroup.Focus, "Tourismus", "Warme, eher trockene Küste oder Berge.",
                "Urlauber suchen Sonne am Meer (warme Küsten mit wenig Regen) oder Berge; kalte, flache Binnenländer locken kaum jemanden.", Fit, f => f.Tourism),
            new StartOption("port", StartGroup.Focus, "Hafen & Handel", "Meeresküste oder großer schiffbarer Fluss.",
                "Ein Hafen braucht Meeresküste oder einen großen, schiffbaren Fluss.", Fit, f => MathF.Max(f.CoastScore, f.RiverScore)),

            // --- Schwierigkeit (eine Stufe)
            new StartOption("easy", StartGroup.Difficulty, "Leicht", "Mildes Klima, fruchtbar, Wasser – verzeiht Fehler.",
                "Milde, fruchtbare Regionen mit Wasser sind begehrt – die meisten gehören schon einem Land.", Fit, f => (f.Habitability - 0.55f) / 0.3f),
            new StartOption("normal", StartGroup.Difficulty, "Normal", "Nicht ganz einfach, nicht extrem.",
                "Die meisten Regionen liegen hier.", Fit, f => 1f - MathF.Abs(f.Habitability - 0.525f) / 0.35f),
            new StartOption("hard", StartGroup.Difficulty, "Schwer", "Karg oder extrem: Kälte, Hitze, Trockenheit, magerer Boden.",
                "Karge Regionen sind Wüsten, Tundren, Eis und Hochgebirge.", Fit, f => (0.5f - f.Habitability) / 0.3f),

            // --- Nachbarn (eine Wahl)
            new StartOption("near", StartGroup.Neighbors, "Nahe an Ländern", "Das nächste Land ist höchstens ~320 km von der Stadt entfernt (kurze Handelswege).",
                "Freies Land liegt oft zwischen Ländern – abgelegene Inseln und Polargebiete sind weit weg von allen.", Fit, f => 1f - WorldNoise.SmoothStep(200f, 450f, f.NeighborKm)),
            new StartOption("remote", StartGroup.Neighbors, "Abgelegen", "Mehr als ~500 km zum nächsten Land – Ruhe, aber lange Wege.",
                "Abgelegen sind Inseln, Polargebiete und große unbesiedelte Weiten.", Fit, f => WorldNoise.SmoothStep(350f, 650f, f.NeighborKm)),
        };

        static StartOption Climate(string id, ClimateZone zone, string name, string description, string geology) =>
            new StartOption(id, StartGroup.Climate, name, description, geology, Fit, f => f.Climate == zone ? 1f : 0f);

        public static StartOption Find(string id)
        {
            foreach (var o in Options) if (o.Id == id) return o;
            return null;
        }

        /// <summary>Klimazone aus Jahresmittel (°C), Niederschlag (mm) und vorherrschendem Biom.</summary>
        public static ClimateZone ClimateOf(float temperature, float precipitation, Biome biome)
        {
            if (biome == Biome.Ice || temperature < -6f) return ClimateZone.Polar;
            if (biome == Biome.Desert || (precipitation < 250f && temperature >= 0f)) return ClimateZone.Arid;
            if (biome == Biome.Tundra || biome == Biome.Taiga || temperature < 4f) return ClimateZone.Subpolar;
            if (temperature < 14f) return ClimateZone.Temperate;
            if (temperature < 21f && biome != Biome.TropicalRainforest) return ClimateZone.Subtropical;
            return ClimateZone.Tropical;
        }

        public static ClimateZone ClimateOf(Region r) => ClimateOf(r.MeanTemperature, r.Precipitation, r.DominantBiome);

        /// <summary>Adjektiv für die Begründung („gemäßigt“, „subtropisch“ …).</summary>
        public static string ClimateWord(ClimateZone z)
        {
            switch (z)
            {
                case ClimateZone.Polar: return "polar";
                case ClimateZone.Subpolar: return "subpolar";
                case ClimateZone.Temperate: return "gemäßigt";
                case ClimateZone.Subtropical: return "subtropisch";
                case ClimateZone.Tropical: return "tropisch";
                default: return "trocken";
            }
        }
    }
}
