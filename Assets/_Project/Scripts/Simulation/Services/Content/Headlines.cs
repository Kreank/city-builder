namespace CityBuilder.Simulation.Services.Content
{
    /// <summary>Thema einer Schlagzeile (aus echten Stadtdaten erkannt, s. <see cref="MediaSystem"/>).</summary>
    public enum HeadlineTopic : byte
    {
        TrafficJam,
        PowerOutage,
        WaterShortage,
        SewageShortage,
        TaxRaise,
        TaxCut,
        NewSchool,
        NewHealth,
        NewPark,
        NewPolice,
        NewFireStation,
        NewMedia,
        NuclearPlantBuilt,
        NuclearIncident,
        WaterPolluted,
        SatisfactionDrop,
        SatisfactionRise,
        PopulationMilestone,
        Unemployment,
        // --- ÖPNV
        NewBusLine,
        BusOverloaded,
        BusLate,
        // --- Etappe 8 Handel
        TradeDeal,
        Embargo,
        TariffRaise,
        // --- Sicherheit (Brände, Kriminalität)
        Fire,
        FireRuin,
        CrimeWave,
        Arrest,
        // --- Umwelt (Etappe 6)
        Smog,
        FishKill,
        EnvironmentRules,
        // --- Handel 2 (Bündnis, Sanktion, Gegenangebot)
        Alliance,
        Sanction,
        CounterOffer,
    }

    /// <summary>
    /// Schlagzeilen-Vorlagen als mitgelieferte Daten (wie <c>Citizens/Content</c>, keine KI zur Laufzeit).
    /// Platzhalter: {Straße}, {Anzahl}, {Steuer}, {Satz}, {Name}, {Einwohner}, {Prozent}, {Medium}, {Land} (Etappe 8).
    /// Nicht auflösbare Platzhalter → nächste Vorlage. Je Thema eine Grundstimmung (Ton −1..+1).
    /// </summary>
    public static class Headlines
    {
        static readonly (string title, string text)[][] ByTopic =
        {
            /* TrafficJam */ new[]
            {
                ("Stau auf der {Straße}", "{Anzahl} Fahrzeuge stehen auf der {Straße} – Pendler verlieren Zeit und Nerven."),
                ("Nichts geht mehr: {Straße} dicht", "Auf der {Straße} stockt der Verkehr. {Medium} rät: Umfahren oder Bus nehmen."),
                ("Verkehrschaos in der {Straße}", "Hupkonzert statt Feierabend: {Anzahl} Autos im Stau. Wann reagiert die Stadt?"),
            },
            /* PowerOutage */ new[]
            {
                ("Stromausfall! {Anzahl} Gebäude im Dunkeln", "Das Netz reicht nicht: {Anzahl} Gebäude, u. a. in der {Straße}, sind ohne Strom."),
                ("Licht aus in der {Straße}", "Stromausfall in {Anzahl} Gebäuden. Bürger fragen: Wo bleibt das neue Kraftwerk?"),
                ("Netz überlastet", "Zu wenig Kraftwerksleistung – {Anzahl} Häuser ohne Strom."),
            },
            /* WaterShortage */ new[]
            {
                ("Trockene Hähne in {Anzahl} Gebäuden", "Die Wasserversorgung reicht nicht – betroffen ist u. a. die {Straße}."),
                ("Wassernot in der {Straße}", "{Anzahl} Gebäude ohne Wasser. Die Pumpen schaffen es nicht."),
            },
            /* SewageShortage */ new[]
            {
                ("Kanalnetz am Limit", "{Anzahl} Gebäude, u. a. in der {Straße}, werden ihr Abwasser nicht los."),
                ("Es stinkt in der {Straße}", "Abwasser staut sich in {Anzahl} Gebäuden. Die Stadt braucht mehr Kläranlagen."),
            },
            /* TaxRaise */ new[]
            {
                ("Steuererhöhung: {Steuer} jetzt {Satz} %", "Die Stadt dreht an der Steuerschraube – {Steuer} steigt auf {Satz} %."),
                ("Teurer wohnen und arbeiten?", "Der Satz für {Steuer} steigt auf {Satz} %. Viele sind verärgert."),
            },
            /* TaxCut */ new[]
            {
                ("Steuern sinken: {Steuer} nur noch {Satz} %", "Gute Nachricht für die Stadt – {Steuer} sinkt auf {Satz} %."),
                ("Entlastung beschlossen", "{Steuer} fällt auf {Satz} %. Unternehmen und Familien atmen auf."),
            },
            /* NewSchool */ new[]
            {
                ("Neue Schule eröffnet", "{Name} an der {Straße} nimmt den Unterricht auf. Eltern freuen sich."),
                ("Mehr Platz für Schüler", "Die Stadt eröffnet {Name}. Kürzere Schulwege für viele Kinder."),
            },
            /* NewHealth */ new[]
            {
                ("Bessere Versorgung: {Name} eröffnet", "An der {Straße} gibt es jetzt {Name}. Kürzere Wege zum Arzt."),
                ("Neue Anlaufstelle für Kranke", "{Name} nimmt Patienten auf."),
            },
            /* NewPark */ new[]
            {
                ("Neuer Park für die Stadt", "Grün statt Grau: {Name} lädt zum Spazieren ein."),
                ("Aufatmen im Viertel", "{Name} ist eröffnet – Familien und Senioren freuen sich."),
            },
            /* NewPolice */ new[]
            {
                ("Mehr Sicherheit: neue Polizeiwache", "{Name} an der {Straße} ist besetzt."),
                ("Polizei zeigt Präsenz", "Mit {Name} sollen Einsatzzeiten sinken."),
            },
            /* NewFireStation */ new[]
            {
                ("Neue Feuerwache in Betrieb", "{Name} an der {Straße} ist einsatzbereit."),
                ("Schneller am Brandort", "Mit {Name} verkürzen sich die Wege der Feuerwehr."),
            },
            /* NewMedia */ new[]
            {
                ("{Medium} ist auf Sendung", "Ab heute berichtet {Medium} aus der Stadt."),
                ("Neues Medium in der Stadt", "{Name} informiert die Bürger – von Stau bis Stadtrat."),
            },
            /* NuclearPlantBuilt */ new[]
            {
                ("Atomkraftwerk am Fluss: Strom satt – und Sorgen", "{Name} liefert riesige Leistung. Anwohner sind gespalten."),
                ("Umstrittener Neubau", "Die Stadt setzt auf Atomstrom. Kritiker fürchten Störfälle."),
            },
            /* NuclearIncident */ new[]
            {
                ("Störfall im Atomkraftwerk!", "{Name} wurde nach einem Störfall abgeschaltet. Die Behörden beschwichtigen."),
                ("Schnellabschaltung", "Nach einer Störung steht {Name} still – das Stromnetz muss ohne auskommen."),
            },
            /* WaterPolluted */ new[]
            {
                ("Trinkwasser verschmutzt", "An {Name} misst man nur noch {Prozent} % Wasserqualität. Abwasser im Fluss?"),
                ("Fluss als Kloake", "Ungeklärtes Abwasser verdirbt das Flusswasser – Qualität an der Pumpe: {Prozent} %."),
            },
            /* SatisfactionDrop */ new[]
            {
                ("Stimmung kippt", "Nur noch {Prozent} % Zufriedenheit – die Bürger sind unzufrieden mit ihrer Stadt."),
                ("Frust in der Stadt", "Umfrage: Die Zufriedenheit sinkt auf {Prozent} %."),
            },
            /* SatisfactionRise */ new[]
            {
                ("Die Stadt blüht auf", "Umfrage: {Prozent} % Zufriedenheit – Tendenz steigend."),
                ("Gute Laune in der Stadt", "Die Bürger sind zufriedener: {Prozent} %."),
            },
            /* PopulationMilestone */ new[]
            {
                ("{Einwohner} Einwohner!", "Die Stadt wächst – heute wurde die Marke von {Einwohner} Einwohnern geknackt."),
                ("Wachstum hält an", "Schon {Einwohner} Menschen leben hier."),
            },
            /* Unemployment */ new[]
            {
                ("Arbeitslosigkeit bei {Prozent} %", "Viele suchen Arbeit. Die Stadt braucht mehr Gewerbe."),
                ("Kein Job in Sicht", "{Prozent} % der Erwerbsfähigen sind ohne Arbeit."),
            },
            // --- ÖPNV
            /* NewBusLine */ new[]
            {
                ("Neue Buslinie: {Name}", "{Name} fährt ab heute {Anzahl} Haltestellen an. Weniger Autos, hofft die Stadt."),
                ("Mit dem Bus durch die Stadt", "Die Stadt eröffnet {Name} – {Anzahl} Halte, erster Halt {Straße}."),
            },
            /* BusOverloaded */ new[]
            {
                ("Busse voll: {Name} am Limit", "{Anzahl} Fahrgäste blieben heute an den Haltestellen stehen. Die Linie braucht mehr Busse."),
                ("Gedränge im Bus", "Auf {Name} geht nichts mehr rein – {Anzahl} Fahrgäste mussten den nächsten Bus abwarten."),
            },
            /* BusLate */ new[]
            {
                ("{Name} chronisch verspätet", "Nur {Prozent} % der Busse kommen pünktlich. Der Stau trifft auch den Nahverkehr."),
                ("Warten auf den Bus", "Pünktlichkeit auf {Name}: {Prozent} %. {Medium} fragt: Wann kommen Busspuren?"),
            },
            // --- Ende ÖPNV
            // --- Etappe 8 Handel
            /* TradeDeal */ new[]
            {
                ("Handelsvertrag mit {Land}", "Die Stadt hat einen Vertrag über {Name} mit {Land} geschlossen – Laufzeit {Anzahl} Wochen."),
                ("Neuer Handelspartner: {Land}", "{Land} und die Stadt einigen sich: {Name} für {Anzahl} Wochen."),
            },
            /* Embargo */ new[]
            {
                ("Embargo! {Land} bricht den Handel ab", "Die Beziehungen zu {Land} sind am Tiefpunkt – Verträge werden gekündigt."),
                ("Eiszeit mit {Land}", "{Land} will keine Geschäfte mehr mit uns machen. {Medium} fragt: Was nun?"),
            },
            /* TariffRaise */ new[]
            {
                ("Zollerhöhung: {Land} verlangt {Satz} %", "{Land} hebt den Zoll auf unsere Waren auf {Satz} % – Exporteure sind besorgt."),
                ("Zollstreit mit {Land}", "Neue Zölle von {Satz} %: {Land} antwortet auf unsere Handelspolitik."),
            },
            // --- Ende Etappe 8
            // --- Sicherheit (Brände, Kriminalität)
            /* Fire */ new[]
            {
                ("Feuer an der {Straße}", "{Name} brennt – die Feuerwehr ist im Einsatz. Anwohner sollen Fenster geschlossen halten."),
                ("Brand in der {Straße}", "Dichter Rauch über dem Viertel: {Name} steht in Flammen. {Medium} berichtet live."),
                ("Großeinsatz der Feuerwehr", "An der {Straße} brennt {Name}. {Anzahl} Löschfahrzeuge sind unterwegs."),
            },
            /* FireRuin */ new[]
            {
                ("{Name} ausgebrannt", "Von {Name} an der {Straße} ist nur eine Ruine geblieben. Die Bewohner kommen bei Freunden unter."),
                ("Totalschaden nach Brand", "Das Feuer an der {Straße} war nicht mehr zu stoppen. {Medium} fragt: Kam die Feuerwehr zu spät?"),
            },
            /* CrimeWave */ new[]
            {
                ("Einbruchserie im Viertel", "{Anzahl} Einbrüche und Diebstähle seit gestern, besonders rund um die {Straße}. Anwohner fordern mehr Streifen."),
                ("Unsicheres Pflaster", "Rund um die {Straße} häufen sich Diebstähle und Vandalismus – {Anzahl} Fälle an einem Tag."),
            },
            /* Arrest */ new[]
            {
                ("Festnahme an der {Straße}", "Die Polizei hat nach einem Einbruch an der {Straße} einen Verdächtigen gefasst."),
                ("Polizei fasst Täter", "Schneller Einsatz: an der {Straße} klickten die Handschellen. Aufklärungsquote zuletzt {Prozent} %."),
            },
            // --- Ende Sicherheit
            // --- Umwelt (Etappe 6)
            /* Smog */ new[]
            {
                ("Smog-Alarm!", "Dicke Luft über der Stadt: {Anzahl} Wohngebäude ({Prozent} %) liegen im Smog. Ärzte raten Kindern und Älteren, drinnen zu bleiben."),
                ("Die Stadt hustet", "Abgase von Verkehr, Industrie und Heizungen hängen fest – {Prozent} % der Wohnungen betroffen. {Medium} fragt: Wo bleiben die Filter?"),
                ("Grauer Himmel, schwere Luft", "Smog in {Anzahl} Wohngebäuden. Ohne Wind und Bäume zieht die schlechte Luft nicht ab."),
            },
            /* FishKill */ new[]
            {
                ("Fischsterben im Fluss", "Tote Fische treiben flussabwärts – die Verschmutzung erreicht {Prozent} %. Ungeklärtes Abwasser gilt als Ursache."),
                ("Der Fluss kippt", "Anwohner melden Gestank und tote Fische. Messwert der Verschmutzung: {Prozent} %. {Medium} fordert eine Kläranlage."),
            },
            /* EnvironmentRules */ new[]
            {
                ("Strengere Umweltauflagen", "Die Stadt verschärft die Auflagen: {Name}. Fabriken müssen nachrüsten – die Wirtschaft klagt über Kosten."),
                ("Filterpflicht beschlossen", "Neue Regeln für Schornsteine: {Name}. Umweltverbände jubeln, Unternehmen rechnen nach."),
            },
            // --- Ende Umwelt
            // --- Handel 2
            /* Alliance */ new[]
            {
                ("{Name}", "Bündnispolitik: {Land} – {Name}. {Medium} fragt, was das für unsere Exporte bedeutet."),
                ("Neue Partner: {Name}", "{Name}: die Stadt rückt mit {Land} enger zusammen."),
            },
            /* Sanction */ new[]
            {
                ("Sanktionen! {Name}", "{Name}. Betroffene Verträge enden sofort, der Handel mit {Land} ruht."),
                ("Handelsstreit eskaliert", "{Name} – Exporteure und Importeure suchen neue Partner."),
            },
            /* CounterOffer */ new[]
            {
                ("{Land} verhandelt nach", "{Land} lehnt unser Angebot über {Name} ab und macht ein Gegenangebot (Runde {Anzahl})."),
                ("Feilschen um {Name}", "Gegenangebot aus {Land}: Die Stadt muss entscheiden, ob sie einschlägt."),
            },
            // --- Ende Handel 2
        };

        public static int TopicCount => ByTopic.Length;
        public static int Count(HeadlineTopic t) => ByTopic[(int)t].Length;

        public static (string title, string text) Template(HeadlineTopic t, int index)
        {
            var list = ByTopic[(int)t];
            return list[(index % list.Length + list.Length) % list.Length];
        }

        /// <summary>Grundstimmung je Thema −1..+1 (schlechte Nachrichten drücken das Gemüt, gute heben es).</summary>
        public static float Tone(HeadlineTopic t)
        {
            switch (t)
            {
                case HeadlineTopic.TrafficJam: return -0.4f;
                case HeadlineTopic.PowerOutage:
                case HeadlineTopic.WaterShortage: return -0.7f;
                case HeadlineTopic.SewageShortage: return -0.6f;
                case HeadlineTopic.TaxRaise: return -0.5f;
                case HeadlineTopic.TaxCut: return 0.4f;
                case HeadlineTopic.NuclearPlantBuilt: return -0.2f;
                case HeadlineTopic.NuclearIncident: return -1f;
                case HeadlineTopic.WaterPolluted: return -0.7f;
                case HeadlineTopic.SatisfactionDrop: return -0.5f;
                case HeadlineTopic.SatisfactionRise: return 0.5f;
                case HeadlineTopic.Unemployment: return -0.5f;
                case HeadlineTopic.PopulationMilestone: return 0.6f;
                case HeadlineTopic.BusOverloaded: return -0.4f; // ÖPNV
                case HeadlineTopic.BusLate: return -0.35f;      // ÖPNV
                case HeadlineTopic.TradeDeal: return 0.4f;      // Etappe 8
                case HeadlineTopic.Embargo: return -0.7f;       // Etappe 8
                case HeadlineTopic.TariffRaise: return -0.35f;  // Etappe 8
                case HeadlineTopic.Fire: return -0.6f;          // Sicherheit
                case HeadlineTopic.FireRuin: return -0.8f;
                case HeadlineTopic.CrimeWave: return -0.6f;
                case HeadlineTopic.Arrest: return 0.3f;
                case HeadlineTopic.Smog: return -0.7f;             // Umwelt
                case HeadlineTopic.FishKill: return -0.8f;
                case HeadlineTopic.EnvironmentRules: return 0.2f;
                case HeadlineTopic.Alliance: return 0.3f;          // Handel 2
                case HeadlineTopic.Sanction: return -0.6f;
                case HeadlineTopic.CounterOffer: return 0.0f;
                default: return 0.5f; // Eröffnungen
            }
        }

        /// <summary>Namen der Medienhäuser je Diensttyp-Id.</summary>
        public static string OutletName(string serviceTypeId)
        {
            switch (serviceTypeId)
            {
                case "media_newspaper": return "Stadt-Kurier";
                case "media_radio": return "Radio Welle 98";
                case "media_tv": return "Stadt-TV 7";
                default: return "Stadtnachrichten";
            }
        }
    }
}
