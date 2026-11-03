namespace CityBuilder.Simulation.Citizens.Content
{
    /// <summary>
    /// Namenslisten (mitgelieferte Daten, in der Entwicklung erzeugt – keine KI zur Laufzeit).
    /// Deutsch gemischt: klassische und moderne deutsche Namen sowie Namen aus Familien mit
    /// türkischen, polnischen, russischen, italienischen, griechischen, arabischen, vietnamesischen u. a. Wurzeln.
    /// Auswahl deterministisch über <see cref="SimRandom"/>.
    /// </summary>
    public static class Names
    {
        public static readonly string[] Female =
        {
            "Anna", "Marie", "Sophie", "Emma", "Mia", "Hannah", "Lena", "Lea", "Laura", "Julia",
            "Lisa", "Sarah", "Katharina", "Johanna", "Clara", "Charlotte", "Emilia", "Greta", "Ida", "Frieda",
            "Mathilda", "Luise", "Paula", "Nele", "Pia", "Jana", "Svenja", "Melanie", "Sabine", "Petra",
            "Monika", "Ursula", "Renate", "Brigitte", "Gisela", "Ingrid", "Helga", "Elke", "Karin", "Birgit",
            "Andrea", "Claudia", "Nicole", "Stefanie", "Daniela", "Tanja", "Anja", "Silke", "Kerstin", "Heike",
            "Ayşe", "Elif", "Zeynep", "Fatma", "Merve", "Leyla", "Aleksandra", "Katarzyna", "Agnieszka", "Magdalena",
            "Natalia", "Olga", "Swetlana", "Irina", "Giulia", "Francesca", "Chiara", "Eleni", "Maria", "Sofia",
            "Fatima", "Amira", "Yasmin", "Nour", "Linh", "Thao", "Mai", "Ana", "Ivana", "Milena",
            "Ronja", "Thea", "Mila", "Ella", "Lotta", "Wilma", "Edith", "Hildegard", "Marlene", "Rosa",
        };

        public static readonly string[] Male =
        {
            "Paul", "Leon", "Lukas", "Felix", "Jonas", "Elias", "Noah", "Ben", "Finn", "Max",
            "Moritz", "Jakob", "Emil", "Anton", "Theo", "Karl", "Oskar", "Henry", "Mats", "Ole",
            "Thomas", "Michael", "Andreas", "Stefan", "Frank", "Jürgen", "Uwe", "Klaus", "Peter", "Wolfgang",
            "Dieter", "Günter", "Horst", "Manfred", "Werner", "Helmut", "Rainer", "Bernd", "Ralf", "Holger",
            "Markus", "Christian", "Sebastian", "Tobias", "Daniel", "Florian", "Matthias", "Jan", "Sven", "Dirk",
            "Mehmet", "Mustafa", "Emre", "Can", "Deniz", "Murat", "Piotr", "Krzysztof", "Tomasz", "Paweł",
            "Dmitri", "Sergej", "Alexander", "Viktor", "Marco", "Luca", "Giuseppe", "Alessandro", "Nikos", "Giorgos",
            "Omar", "Ahmad", "Karim", "Yusuf", "Minh", "Duc", "Tuan", "Luka", "Marko", "Milan",
            "Hannes", "Fritz", "Willi", "Erwin", "Kurt", "Heinz", "Otto", "Johann", "Konrad", "Ludwig",
        };

        public static readonly string[] Last =
        {
            "Müller", "Schmidt", "Schneider", "Fischer", "Weber", "Meyer", "Wagner", "Becker", "Schulz", "Hoffmann",
            "Schäfer", "Koch", "Bauer", "Richter", "Klein", "Wolf", "Schröder", "Neumann", "Schwarz", "Zimmermann",
            "Braun", "Krüger", "Hofmann", "Hartmann", "Lange", "Schmitt", "Werner", "Schmitz", "Krause", "Meier",
            "Lehmann", "Schmid", "Schulze", "Maier", "Köhler", "Herrmann", "König", "Walter", "Mayer", "Huber",
            "Kaiser", "Fuchs", "Peters", "Lang", "Scholz", "Möller", "Weiß", "Jung", "Hahn", "Schubert",
            "Vogel", "Friedrich", "Keller", "Günther", "Frank", "Berger", "Winkler", "Roth", "Beck", "Lorenz",
            "Baumann", "Franke", "Albrecht", "Schuster", "Simon", "Ludwig", "Böhm", "Winter", "Kraus", "Martin",
            "Krämer", "Vogt", "Stein", "Jäger", "Otto", "Sommer", "Groß", "Seidel", "Heinrich", "Brandt",
            "Haas", "Schreiber", "Graf", "Dietrich", "Ziegler", "Kuhn", "Engel", "Pohl", "Horn", "Busch",
            "Yılmaz", "Kaya", "Demir", "Şahin", "Çelik", "Öztürk", "Aydın", "Arslan", "Kowalski", "Nowak",
            "Wiśniewski", "Lewandowski", "Kamiński", "Iwanow", "Petrow", "Smirnow", "Rossi", "Russo", "Esposito", "Romano",
            "Papadopoulos", "Georgiou", "Nikolaidis", "Haddad", "Khalil", "Nasser", "Nguyen", "Tran", "Pham", "Horvat",
            "Novak", "Kovačević", "Jovanović", "Hansen", "Jensen", "Lindqvist", "Dubois", "Moreau", "Silva", "Santos",
        };

        public static string First(Gender g, int index)
        {
            var list = g == Gender.Female ? Female : Male;
            return list[(index % list.Length + list.Length) % list.Length];
        }

        public static string LastName(int index) => Last[(index % Last.Length + Last.Length) % Last.Length];
    }
}
