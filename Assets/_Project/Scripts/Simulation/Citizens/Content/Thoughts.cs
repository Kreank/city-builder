namespace CityBuilder.Simulation.Citizens.Content
{
    /// <summary>
    /// Kurze Gedankensätze je Erinnerungsart (Ich-Form, mitgelieferte Daten). Platzhalter:
    /// {Name} eigener Vorname, {Partner} Vorname des Partners/der beteiligten Person, {Kind} Vorname des Kindes,
    /// {Straße} Straße (Bezug bzw. Wohnstraße), {Firma} Arbeitgeber, {Park} Park im Viertel.
    /// Die Vorlage wird beim Entstehen der Erinnerung über den RNG gewählt und gespeichert.
    /// </summary>
    public static class Thoughts
    {
        static readonly string[][] ByKind =
        {
            /* TrafficJam */ new[]
            {
                "Auf der {Straße} stand ich schon wieder ewig im Stau.",
                "Eine Stunde für drei Kilometer – vielleicht nehme ich morgen den Bus.",
                "Die {Straße} ist jeden Morgen dicht. Das macht mich fertig.",
                "Stoßstange an Stoßstange auf der {Straße}. Mit dem Rad wäre ich schneller gewesen.",
                "Wieder zu spät bei {Firma}, weil auf der {Straße} nichts ging.",
            },
            /* LongCommute */ new[]
            {
                "Der Weg zu {Firma} ist einfach zu weit.",
                "Jeden Tag so lange unterwegs – da bleibt kaum Zeit für etwas anderes.",
                "Ich sollte mir einen Job näher an der {Straße} suchen.",
                "Pendeln frisst meinen halben Abend.",
            },
            /* PowerOutage */ new[]
            {
                "Schon wieder kein Strom. Der Kühlschrank taut ab.",
                "Gestern saßen wir bei Kerzenlicht – romantisch war das nicht.",
                "Ohne Strom kann ich nicht arbeiten. Wer plant diese Stadt eigentlich?",
                "Der Stromausfall in der {Straße} hat mir echt Angst gemacht.",
            },
            /* WaterOutage */ new[]
            {
                "Kein Wasser aus dem Hahn – wie soll man so leben?",
                "Ich musste Wasser in Flaschen kaufen, um mir die Zähne zu putzen.",
                "Die Leitung in der {Straße} ist wieder trocken.",
                "Ohne Wasser keine Dusche. Ich überlege ernsthaft umzuziehen.",
            },
            /* Noise */ new[]
            {
                "Seit die {Straße} so voll ist, schlafe ich schlecht.",
                "Der Verkehrslärm vor meinem Fenster hört nie auf.",
                "Ich höre jeden Lkw auf der {Straße}. Ohrstöpsel helfen kaum.",
                "Hier ist es einfach zu laut. Ich träume von einem ruhigen Hinterhof.",
                "Fenster zu, Musik an – anders ist der Lärm in der {Straße} nicht auszuhalten.",
            },
            /* Smog */ new[]
            {
                "Die Luft riecht nach Abgas. Ich huste morgens.",
                "Der Schornstein am Stadtrand qualmt Tag und Nacht.",
                "Wäsche draußen aufhängen? Lieber nicht, sie riecht danach nach Rauch.",
                "Ich mache mir Sorgen um die Lungen der Kinder.",
            },
            /* NicePark */ new[]
            {
                "Im {Park} war es heute wunderschön.",
                "Ein Spaziergang durch den {Park} – genau das brauchte ich.",
                "Die Bänke im {Park} sind mein Lieblingsplatz.",
                "Im {Park} habe ich Nachbarn getroffen, die ich noch gar nicht kannte.",
                "Frische Luft, Vogelgezwitscher – der {Park} ist ein Glück für das Viertel.",
            },
            /* NiceShopping */ new[]
            {
                "Ein netter Bummel durch die Läden – und sogar ein Schnäppchen.",
                "Beim Einkaufen habe ich {Partner} getroffen und wir haben Kaffee getrunken.",
                "Die Geschäfte hier haben alles, was man braucht.",
                "Ich gönne mir ab und zu etwas – heute war so ein Tag.",
            },
            /* NoShop */ new[]
            {
                "Kein Laden in der Nähe – wo soll ich denn einkaufen?",
                "Für ein Brot muss ich durch die halbe Stadt.",
                "Der Kühlschrank ist leer und kein Geschäft erreichbar.",
                "Hier fehlt ein Supermarkt. Dringend.",
            },
            /* NewJob */ new[]
            {
                "Ich habe eine Stelle bei {Firma}! Endlich wieder Arbeit.",
                "Der erste Tag bei {Firma} war aufregend.",
                "Neuer Job, neues Glück – {Firma} hat mich genommen.",
                "Die Kollegen bei {Firma} scheinen nett zu sein.",
            },
            /* JobLost */ new[]
            {
                "Ich habe meinen Job verloren. Wie soll es jetzt weitergehen?",
                "{Firma} hat Stellen gestrichen – meine war dabei.",
                "Wieder auf Jobsuche. Ich hasse Bewerbungen.",
                "Ohne Arbeit fällt mir die Decke auf den Kopf.",
            },
            /* Retired */ new[]
            {
                "Endlich Rente! Jetzt habe ich Zeit für den Garten.",
                "Nach all den Jahren bei {Firma} ist es seltsam, morgens nicht loszumüssen.",
                "Ruhestand – ich will endlich mehr reisen.",
                "Die Rente ist knapp, aber die Freiheit ist schön.",
            },
            /* Birth */ new[]
            {
                "{Kind} ist auf der Welt! Wir sind überglücklich.",
                "Schlaflose Nächte, aber {Kind} ist jede Minute wert.",
                "Unser kleines {Kind} hat heute zum ersten Mal gelächelt.",
                "Die Wohnung wird langsam eng – aber {Kind} macht alles schöner.",
            },
            /* NewPartner */ new[]
            {
                "Ich habe {Partner} kennengelernt. Ich bin verliebt!",
                "{Partner} und ich ziehen zusammen.",
                "Mit {Partner} fühlt sich die Stadt ganz anders an.",
                "Ein erstes Date im Café – und jetzt sind {Partner} und ich ein Paar.",
            },
            /* Wedding */ new[]
            {
                "{Partner} und ich haben geheiratet!",
                "Die Hochzeit war wunderschön – die halbe Straße hat mitgefeiert.",
                "Ja gesagt! Mit {Partner} an meiner Seite.",
                "Was für ein Fest. Ich werde diesen Tag nie vergessen.",
            },
            /* Separation */ new[]
            {
                "{Partner} und ich haben uns getrennt.",
                "Es ging einfach nicht mehr mit {Partner}.",
                "Plötzlich wieder allein. Das muss ich erst verdauen.",
                "Die Trennung von {Partner} tut weh, aber es war richtig.",
            },
            /* PartnerDied */ new[]
            {
                "{Partner} ist gestorben. Die Wohnung ist so still.",
                "Ich vermisse {Partner} jeden Tag.",
                "Ohne {Partner} weiß ich nicht, wie es weitergehen soll.",
                "Wir hatten so viele gute Jahre, {Partner} und ich.",
            },
            /* Illness */ new[]
            {
                "Ich bin krank und liege flach.",
                "Fieber und Husten – hoffentlich gibt es bald einen Arzttermin.",
                "Krank zu sein macht mir Angst.",
                "Die Luft hier tut mir nicht gut, ich bin schon wieder krank.",
            },
            /* Recovered */ new[]
            {
                "Endlich wieder gesund!",
                "Ich fühle mich wie neugeboren.",
                "Die Ärzte haben mir gut geholfen.",
                "Wieder fit – heute geht's in den {Park}.",
            },
            /* MovedHome */ new[]
            {
                "Die neue Wohnung an der {Straße} gefällt mir.",
                "Umzug geschafft! Die Kisten packe ich irgendwann aus.",
                "Endlich mehr Platz und mehr Ruhe.",
                "Neue Nachbarn, neues Viertel – ich bin gespannt.",
            },
            /* OwnFlat */ new[]
            {
                "Meine erste eigene Wohnung an der {Straße}!",
                "Ausgezogen! Endlich räumt niemand mehr mein Zimmer auf.",
                "Eigene vier Wände – und eigene Rechnungen.",
                "Mama ruft jeden Tag an, aber ich genieße die Freiheit.",
            },
            /* ChildMovedOut */ new[]
            {
                "{Kind} ist ausgezogen. Das Kinderzimmer ist so leer.",
                "Unser {Kind} wird erwachsen – schön und traurig zugleich.",
                "Jetzt haben wir die Wohnung wieder für uns.",
                "{Kind} kommt sonntags zum Essen. Immerhin.",
            },
            /* FeelsUnsafe */ new[]
            {
                "Abends traue ich mich nicht mehr allein raus.",
                "Hier gibt es viel zu wenig Polizei.",
                "Wenn es brennt – wie lange braucht die Feuerwehr bis zur {Straße}?",
                "In der {Straße} fühle ich mich nicht sicher.",
            },
            /* Demolished */ new[]
            {
                "Sie haben mein Haus abgerissen. Einfach so.",
                "Für eine neue Straße mussten wir raus.",
                "Jahrelang dort gewohnt – und jetzt steht da nichts mehr.",
                "Ich musste über Nacht umziehen. Unfassbar.",
            },
            /* QuietEvening */ new[]
            {
                "Ein ruhiger Abend zu Hause – herrlich.",
                "Tee, ein gutes Buch und Stille.",
                "Heute einfach mal nichts gemacht. Das tat gut.",
                "Ein gemütlicher Abend mit {Partner} auf dem Sofa.",
            },
            // --- Abwasser/Wasserqualität/Atom (G3 erweitert)
            /* SewageBackup */ new[]
            {
                "Aus dem Abfluss gluckert es – und es riecht furchtbar.",
                "Der Kanal in der {Straße} läuft über. Wer kümmert sich darum?",
                "Seit Tagen staut sich das Abwasser im Keller.",
                "Ohne Kanalanschluss kann man hier nicht wohnen.",
            },
            /* BadTapWater */ new[]
            {
                "Das Leitungswasser schmeckt komisch. Ich koche es lieber ab.",
                "Im Fluss treibt das Abwasser – und daraus machen sie unser Trinkwasser?",
                "Seit einer Woche Bauchweh. Ob das am Wasser liegt?",
                "Ich kaufe nur noch Wasser in Flaschen.",
            },
            /* NuclearFear */ new[]
            {
                "Das Atomkraftwerk ist viel zu nah an der {Straße}.",
                "Nachts frage ich mich, was passiert, wenn dort etwas schiefgeht.",
                "Billiger Strom ist schön – aber muss das Kraftwerk vor unserer Tür stehen?",
                "{Partner} sagt, ich mache mir zu viele Sorgen wegen des Kraftwerks.",
            },
            // --- Ende
            /* BusFull */ new[]
            {
                "Der Bus war so voll, dass er einfach weitergefahren ist.",
                "Zwei volle Busse an der {Straße} – dann bin ich eben gelaufen.",
                "Eingequetscht wie eine Sardine. Morgen nehme ich wieder das Auto.",
                "Die Linie braucht dringend mehr Busse.",
            },
            /* BusLate */ new[]
            {
                "Ewig an der Haltestelle gestanden. Wo bleibt der Bus?",
                "Der Bus steckte im Stau – wie alle anderen auch.",
                "Laut Fahrplan hätte er längst da sein müssen.",
                "Wegen des Busses wieder zu spät bei {Firma}.",
            },
            /* BusRide */ new[]
            {
                "Mit dem Bus war ich entspannt und pünktlich da.",
                "Im Bus konnte ich in Ruhe lesen.",
                "Kein Parkplatz suchen – der Bus hält direkt an der {Straße}.",
                "Die Buslinie ist wirklich praktisch.",
            },
            // --- Bahn (Stufe 2)
            /* TrainRide */ new[]
            {
                "Mit dem Fernzug ganz entspannt aus der Stadt – kein Stau auf der Autobahn.",
                "Im Zug habe ich die ganze Fahrt gelesen.",
                "Der neue Bahnhof ist ein Segen für Pendler wie mich.",
                "Pünktlich auf die Minute – so macht Bahnfahren Spaß.",
            },
            /* TrainLate */ new[]
            {
                "Ewig auf dem Bahnsteig gestanden – wo bleibt der Zug?",
                "Der Zug war so voll, dass ich nicht mehr reingekommen bin.",
                "Signalstörung, hieß es. Wieder zu spät bei {Firma}.",
                "Nachts fährt hier ja gar nichts – dann eben doch das Auto.",
            },
            // --- Ende Bahn
            // --- Sicherheit (Brände, Kriminalität)
            /* Fire */ new[]
            {
                "Ich sehe die Flammen an der {Straße} immer noch vor mir.",
                "Der Rauchmelder hat uns gerettet – aber die Wohnung riecht noch nach Brand.",
                "Wie lange hat die Feuerwehr gebraucht? Gefühlt eine Ewigkeit.",
                "An der {Straße} hat es gebrannt. Ich prüfe jetzt jeden Abend den Herd.",
                "{Partner} und ich standen nachts auf der Straße und haben zugesehen, wie es brennt.",
            },
            /* Burglary */ new[]
            {
                "Bei uns wurde eingebrochen. Ich schlafe seitdem schlecht.",
                "Die Tür aufgebrochen, alles durchwühlt – an der {Straße} fühle ich mich nicht mehr sicher.",
                "Bei {Firma} wurde eingebrochen. Wer macht so etwas?",
                "Ich habe ein neues Schloss gekauft. Die Polizei war erst spät da.",
            },
            // --- Ende Sicherheit
        };

        public static int Count(MemoryKind k) => ByKind[(int)k].Length;

        public static string Template(MemoryKind k, int index)
        {
            var list = ByKind[(int)k];
            return list[(index % list.Length + list.Length) % list.Length];
        }

        /// <summary>Anzahl der Arten mit Texten (Prüfung in Tests: jede Art hat Vorlagen).</summary>
        public static int KindCount => ByKind.Length;
    }
}
