using System;
using System.Collections.Generic;
using CityBuilder.Simulation.Core;
using CityBuilder.Simulation.Persistence;
using CityBuilder.Simulation.Trade;
using CityBuilder.Simulation.World;

namespace CityBuilder.Simulation.Realm
{
    /// <summary>
    /// Wechsel der detailliert laufenden Stadt (Etappe 7, LOD). <b>Entscheidung:</b> Eine Stadt = eine <see cref="SimulationWorld"/>;
    /// ein Spielstand = die aktive Welt + im Abschnitt „realm“ je anderer Stadt ein <b>eingebetteter Unter-Spielstand</b> (ihre lokalen
    /// Abschnitte). Reichsweit (beim Wechsel mitgenommen) sind nur <see cref="GlobalSections"/>: „realm“ (Städte, Lieferungen,
    /// Landeskasse, Regionsbesitz), „planet“ (Länder-Volkswirtschaften), „trade“ (Verträge, Beziehungen, Zölle, Grenzlager) und die
    /// Uhr. Alles andere (Straßen, Bürger, Kasse, Firmen …) ist lokal. So nutzt der Wechsel genau den erprobten Weg von
    /// <see cref="SaveGame.Restore"/> (Welt aus Einstellungen + Seed neu, Abschnitte lesen) und bleibt deterministisch – kein
    /// System musste „mehrstädtefähig“ werden.
    /// <para>
    /// Ablauf: laufenden Tick zu Ende rechnen → Kennzahlen der aktiven Stadt ins Aggregat → lokale Abschnitte als Schnappschuss
    /// in ihre <see cref="RealmCity"/> → Ziel aktiv setzen → Container zusammensetzen (Weltabschnitt mit Region/Geländeseed/Name
    /// der Zielstadt und der aktuellen Uhr, lokale Abschnitte der Zielstadt – bzw. einer frisch erzeugten Stadtkarte –, reichsweite
    /// Abschnitte) → <see cref="SaveGame.Restore"/>. Danach: Kassenstand aus dem Aggregat übernehmen (Differenz als „Reich:
    /// Verrechnung“), Exportware auf dem Weg zum Grenzlager gilt als angekommen.
    /// </para>
    /// </summary>
    public static class RealmSwitch
    {
        /// <summary>Abschnitte, die zum Reich gehören und beim Wechsel mitwandern (alles andere gehört der Stadt).</summary>
        public static readonly string[] GlobalSections = { SaveGame.WorldSectionId, "realm", "planet", "trade" };

        public static bool IsGlobal(string id) => Array.IndexOf(GlobalSections, id) >= 0;

        /// <summary>Kann zu dieser Stadt gewechselt werden?</summary>
        public static RealmCheck Check(SimulationWorld world, int cityId)
        {
            var check = new RealmCheck();
            var realm = world?.Realm;
            var city = realm?.City(cityId);
            check.Add("Stadt", city != null, city == null ? "unbekannte Stadt" : null);
            if (city == null) return check;
            check.Add("Andere Stadt", city.Id != realm.ActiveCityId, "läuft schon detailliert");
            check.Add("Weltkarte", world.Planet != null, "ohne Weltkarte gibt es nur eine Stadt");
            return check;
        }

        /// <summary>
        /// Wechselt zur Stadt <paramref name="cityId"/> und gibt die neue Welt zurück (die alte wird verworfen – der Aufrufer ersetzt
        /// sie, z. B. <c>SimulationRunner.ReplaceWorld</c>). Schlägt der Aufbau fehl, bleibt die alte Welt unverändert gültig
        /// (<see cref="SaveLoadException"/>).
        /// </summary>
        public static SimulationWorld Switch(SimulationWorld world, int cityId)
        {
            var check = Check(world, cityId);
            if (!check.Ok) throw new InvalidOperationException(check.FirstProblem);
            while (!world.AtTickBoundary) world.Substep(); // laufenden Tick zu Ende (deterministisch)

            var realm = world.Realm;
            var from = realm.ActiveCity;
            var to = realm.City(cityId);
            from.Economy.CaptureFrom(world);
            from.LastDetailedDay = world.Clock.Day - 1;

            var full = SaveGame.Capture(world);
            var local = new List<SaveSection>();
            foreach (var s in full.Sections) if (!IsGlobal(s.Id)) local.Add(s);
            var targetLocal = to.Snapshot;
            int oldActive = realm.ActiveCityId;
            from.Snapshot = local;
            to.Snapshot = null;
            realm.ActiveCityId = to.Id;
            try
            {
                var settings = SaveGame.ReadWorldSettings(full.Find(SaveGame.WorldSectionId), out _, out _, out _);
                settings.StartRegion = to.RegionId;
                settings.TerrainSeed = to.TerrainSeed;
                settings.CityName = to.Name;
                long tick = world.Clock.Tick;

                if (targetLocal == null)
                {
                    // Nie detailliert gespielt: Stadtkarte frisch aus ihrer Region (Gelände, Klima, Vorkommen, vorgebaute Autobahn)
                    var freshSettings = SaveGame.ReadWorldSettings(SaveGame.WorldSection(settings, to.Seed, 0, 0), out _, out _, out _);
                    var fresh = SimulationWorld.CreateDefault(freshSettings, to.Seed, newGame: true);
                    targetLocal = new List<SaveSection>();
                    foreach (var s in SaveGame.Capture(fresh).Sections) if (!IsGlobal(s.Id)) targetLocal.Add(s);
                }

                var c = new SaveContainer();
                c.Info.Name = to.Name;
                c.Info.Tick = tick;
                c.Sections.Add(SaveGame.WorldSection(settings, to.Seed, tick, 0));
                foreach (var s in targetLocal) c.Sections.Add(s);
                c.Set("realm", realm.SaveVersion, SaveGame.SectionBytes(realm));
                foreach (var id in new[] { "planet", "trade" })
                {
                    var s = full.Find(id);
                    if (s != null) c.Sections.Add(s);
                }
                var next = SaveGame.Restore(c);
                AfterSwitch(next);
                return next;
            }
            catch
            {
                // alte Welt bleibt gültig: Reichszustand zurücksetzen
                realm.ActiveCityId = oldActive;
                from.Snapshot = null;
                to.Snapshot = targetLocal;
                throw;
            }
        }

        /// <summary>
        /// Nach dem Laden der Zielstadt: Kasse = aggregierte Kasse (während der Abwesenheit liefen Steuern, Unterhalt, Lieferungen
        /// und Landesabgabe dort weiter – die Differenz wird als „Reich: Verrechnung“ gebucht), Exportware auf dem Weg ins Grenzlager
        /// (Lkw der vorigen Stadt) gilt als angekommen, Kennzahlen neu erfassen.
        /// </summary>
        static void AfterSwitch(SimulationWorld w)
        {
            var realm = w.Realm;
            var city = realm.ActiveCity;
            var eco = w.Economy;
            if (eco != null)
            {
                double delta = city.Economy.Treasury - eco.Balance;
                if (delta > 0.005) eco.Post(Economy.BudgetCategory.RealmIncome, delta);
                else if (delta < -0.005) eco.Post(Economy.BudgetCategory.RealmExpenses, -delta);
            }
            var depot = w.Trade?.City;
            if (depot != null)
                for (int k = 0; k < depot.ExportEnRoute.Length; k++)
                {
                    depot.ExportStock[k] += depot.ExportEnRoute[k];
                    depot.ExportEnRoute[k] = 0;
                }
            city.Economy.CaptureFrom(w);
            city.LastDetailedDay = w.Clock.Day - 1;
            realm.Report(w.Clock.Day, $"Willkommen in {city.Name}", city.Economy.Measured ? "Die Stadt läuft wieder detailliert." : "Neue Stadt – die Karte ist noch unbebaut.");
        }
    }
}
