using CityBuilder.Simulation.Zoning;

namespace CityBuilder.Simulation.Citizens.Content
{
    /// <summary>
    /// Namen für Orte in Gedanken-/Ereignistexten: Straßen (aus der Segment-Id), Firmen (aus Gebäude-Id und Zone),
    /// Parks (aus der Wohngebäude-Id). Rein aus stabilen Hashes – verbraucht keinen Zufall und bleibt gleich,
    /// solange das Objekt existiert. (Straßen haben noch keine echten Namen; sobald es die gibt, hier ersetzen.)
    /// </summary>
    public static class PlaceNames
    {
        static readonly string[] StreetStems =
        {
            "Linden", "Eichen", "Birken", "Kastanien", "Ahorn", "Buchen", "Tannen", "Ulmen", "Rosen", "Tulpen",
            "Goethe", "Schiller", "Lessing", "Heine", "Mozart", "Beethoven", "Bach", "Kant", "Humboldt", "Einstein",
            "Bahnhof", "Markt", "Kirch", "Schul", "Post", "Mühlen", "Brunnen", "Garten", "Wiesen", "Feld",
            "Berg", "Tal", "Hafen", "Fluss", "Brücken", "Burg", "Schloss", "Kloster", "Rathaus", "Wald",
            "Sonnen", "Mond", "Stern", "Lerchen", "Amsel", "Finken", "Falken", "Kranich", "Hirsch", "Fuchs",
            "Industrie", "Handwerker", "Kaufmanns", "Weber", "Schmiede", "Gießerei", "Ziegelei", "Werft", "Kraftwerk", "Gleis",
        };

        static readonly string[] StreetSuffixes =
        {
            "straße", "straße", "straße", "weg", "weg", "allee", "gasse", "ring", "damm", "chaussee", "steig", "pfad",
        };

        static readonly string[] ParkNames =
        {
            "Stadtpark", "Bürgerpark", "Rosengarten", "Volkspark", "Lindenhain", "Uferwiese", "Schlossgarten",
            "Kurpark", "Spielwiese am Bach", "Ententeich", "Hügelpark", "Grüne Lunge", "Obstwiese", "Kirschgarten",
        };

        static readonly string[] ShopPatterns =
        {
            "Bäckerei {0}", "Metzgerei {0}", "Kiosk am Eck", "Blumen {0}", "Supermarkt Frischekorb", "Drogerie {0}",
            "Buchhandlung {0}", "Café {0}", "Eisdiele Venezia", "Imbiss Anatolia", "Modehaus {0}", "Elektro {0}",
            "Getränke {0}", "Obst & Gemüse {0}", "Schuhhaus {0}", "Asia-Markt Saigon",
        };

        static readonly string[] OfficePatterns =
        {
            "{0} Consulting", "{0} & Partner", "Nordlicht Software", "Kanzlei {0}", "Versicherung {0}", "Agentur Pixelwerk",
            "Stadtsparkasse", "{0} Immobilien", "Ingenieurbüro {0}", "Datenwerk GmbH", "Steuerbüro {0}", "Zentrale Logistik AG",
        };

        static readonly string[] IndustryPatterns =
        {
            "Stahlbau {0}", "Maschinenfabrik {0}", "Spedition {0}", "Druckerei {0}", "Brauerei {0}", "Möbelwerk {0}",
            "Kunststoffwerk Süd", "Gießerei {0}", "Holzverarbeitung {0}", "Zementwerk Nord", "Textilwerk {0}", "Recyclinghof {0}",
        };

        public static string Street(int segmentId)
        {
            if (segmentId <= 0) return "Hauptstraße";
            uint h = SimRandom.Hash(segmentId, 77);
            return StreetStems[h % (uint)StreetStems.Length] + StreetSuffixes[(h >> 12) % (uint)StreetSuffixes.Length];
        }

        public static string Park(int homeBuildingId)
        {
            uint h = SimRandom.Hash(homeBuildingId, 313);
            return ParkNames[h % (uint)ParkNames.Length];
        }

        public static string Company(int buildingId, ZoneType zone)
        {
            if (buildingId <= 0) return "einer Firma außerhalb";
            uint h = SimRandom.Hash(buildingId, 991);
            string[] patterns = zone == ZoneType.Office ? OfficePatterns : zone == ZoneType.Industry ? IndustryPatterns : ShopPatterns;
            string owner = Names.LastName((int)(h >> 8));
            return string.Format(patterns[h % (uint)patterns.Length], owner);
        }
    }
}
