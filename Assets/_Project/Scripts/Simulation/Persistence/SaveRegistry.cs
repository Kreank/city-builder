using System.Collections.Generic;
using CityBuilder.Simulation.Core;

namespace CityBuilder.Simulation.Persistence
{
    /// <summary>
    /// <b>Die eine Stelle</b>, an der festgelegt ist, welche Systeme in welcher Reihenfolge gespeichert/geladen werden.
    /// Reihenfolge = Lesereihenfolge: ein Abschnitt darf sich beim Lesen auf alle vorherigen verlassen
    /// (z. B. Verkehr nach Straßen, Warenketten nach der Stadt).
    /// <para>
    /// Neues System: <see cref="ISaveable"/> implementieren (am besten als <c>partial class XyzSystem</c> in
    /// <c>XyzSystem.Save.cs</c>) und hier eine Zeile ergänzen – oder einfach mit <see cref="SimulationWorld.AddSystem"/>
    /// anmelden: registrierte Systeme, die <see cref="ISaveable"/> sind und unten nicht stehen, werden automatisch
    /// hinten angehängt. Damit erscheinen sie auch im Determinismus-Test (<c>SaveLoadTests</c>).
    /// </para>
    /// </summary>
    public static class SaveRegistry
    {
        public static List<ISaveable> Collect(SimulationWorld w)
        {
            var list = new List<ISaveable>();
            void Add(ISaveable s)
            {
                if (s != null && !list.Contains(s)) list.Add(s);
            }

            Add(w.Terrain);                 // Höhen (aktuell + natürlich), bearbeiteter Boden
            Add(w.Environment);             // Lärm, Abgas
            Add(w.Resources);               // Ressourcen-Raster + natürlicher Ausgangszustand
            Add(w.Realm);                   // Spielstart: Stadtname, Spieler-Gebiet, Besitz der Regionen
            Add(w.Planet?.Economy);         // Länder-Volkswirtschaften (Planet selbst entsteht aus dem Seed neu)
            Add(w.Roads);                   // Knoten, Segmente, Profile, Typen
            Add(w.Traffic?.Rules);          // Verkehrsregeln
            Add(w.City);                    // Zonen, Zonenraster, Gebäude, Nachfrage
            Add(w.Traffic);                 // Fahrzeuge, Stau-Index, Zufall
            Add(w.Citizens);                // Bürger, Haushalte, Fahrten, Erinnerungen
            Add(w.Utilities);               // Versorgung (Strom, Wasser, Abwasser, Atom-Störfälle)
            Add(w.WaterQuality);            // Wasserverschmutzung, Trinkwasser
            Add(w.Services);                // Dienste, Müll
            Add(w.Media);                   // Schlagzeilen, Sperrfristen
            Add(w.Transit);                 // ÖPNV: Haltestellen, Linien, Busse, Fahrgäste (nach Verkehr und Bürgern)
            Add(w.LandValue);               // Bodenwert, Stufen
            Add(w.Goods);                   // Firmen, Märkte, Lieferungen
            Add(w.Economy);                 // Stadtkasse
            Add(w.Trade);                   // Handel zwischen Ländern (nur mit Weltkarte), Grenzlager
            // --- neue Systeme hier anmelden (oder einfach per AddSystem – s. unten)

            // Weitere registrierte Systeme mit eigenem Zustand automatisch hinten anhängen
            foreach (var s in w.Systems)
                if (s is ISaveable saveable) Add(saveable);
            return list;
        }
    }
}
