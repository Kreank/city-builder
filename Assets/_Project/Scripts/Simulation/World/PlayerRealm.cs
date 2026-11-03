using System;
using System.Collections.Generic;
using CityBuilder.Simulation.Persistence;

namespace CityBuilder.Simulation.World
{
    /// <summary>
    /// Das Gebiet des Spielers (Spielstart &amp; Startregion): Stadtname, später Landesname und Flagge. Der Spieler beginnt als
    /// <b>Stadtstaat</b> ohne eigenen Staatsnamen – angezeigt als „Freie Stadt {Stadtname}“ (<see cref="DisplayName"/>). Mit
    /// mehreren Städten (Etappe 7) kann er ein <b>Land gründen</b> (eigener Name/Flagge, <see cref="NationName"/>,
    /// <see cref="FlagColor"/>) – hier ist nur das Datenmodell vorbereitet, Gründen kommt später.
    /// <para>
    /// Immer vorhanden (<c>SimulationWorld.Realm</c>), auch ohne Weltkarte (dann nur der Stadtname). Gespeichert im Abschnitt
    /// „realm“: Namen, Städte und der <b>Besitz aller Regionen</b> (Land je Region, −1 = frei) – der Planet entsteht beim Laden
    /// neu aus dem Seed, spätere Gebietsänderungen (Städte gründen, Land gründen) werden so wiederhergestellt.
    /// </para>
    /// </summary>
    public sealed class PlayerRealm : ISaveable
    {
        public const string DefaultCityName = "Neustadt";
        public const int MaxNameLength = 40;
        /// <summary>Etappe 7 (vorbereitet): so viele eigene Städte braucht es, um ein Land zu gründen.</summary>
        public const int MinCitiesForNation = 3;

        readonly Planet planet;

        public PlayerRealm(Planet planet, string cityName = null)
        {
            this.planet = planet;
            CityName = Clean(cityName) ?? DefaultCityName;
            if (planet?.StartRegion != null) CityRegions.Add(planet.StartRegionId);
            Apply();
        }

        /// <summary>Name der Spielerstadt (obere Leiste, Spielstand, Handel als „Freie Stadt …“).</summary>
        public string CityName { get; private set; }
        /// <summary>Etappe 7: Name des gegründeten Landes; null = noch Stadtstaat.</summary>
        public string NationName { get; private set; }
        /// <summary>Etappe 7: Flaggen-/Kartenfarbe RGB (Standard Gold wie der Stadtstaat).</summary>
        public uint FlagColor { get; private set; } = PlanetGenerator.PlayerColor;
        /// <summary>Regionen mit eigenen Städten (zuerst die Startregion).</summary>
        public List<int> CityRegions { get; } = new List<int>();

        /// <summary>Hat der Spieler schon ein eigenes Land gegründet?</summary>
        public bool IsNation => NationName != null;
        /// <summary>Etappe 7 (vorbereitet): genug Städte, um ein Land zu gründen.</summary>
        public bool CanFoundNation => !IsNation && CityRegions.Count >= MinCitiesForNation;
        /// <summary>Anzeigename des Spieler-Gebiets: Landesname oder „Freie Stadt {Stadtname}“.</summary>
        public string DisplayName => NationName ?? CityStateName(CityName);

        /// <summary>Platzhalter-Name eines Stadtstaats.</summary>
        public static string CityStateName(string cityName) => "Freie Stadt " + (Clean(cityName) ?? DefaultCityName);

        /// <summary>Bereinigt einen Namen (Leerraum, Länge); null bei leerer Eingabe.</summary>
        public static string Clean(string name)
        {
            if (name == null) return null;
            var s = name.Trim();
            if (s.Length == 0) return null;
            if (s.Length > MaxNameLength) s = s.Substring(0, MaxNameLength).TrimEnd();
            return s;
        }

        /// <summary>Stadt umbenennen (leer = unverändert). Der Stadtstaat auf der Weltkarte/im Handel heißt mit.</summary>
        public bool RenameCity(string name)
        {
            var s = Clean(name);
            if (s == null) return false;
            CityName = s;
            Apply();
            return true;
        }

        /// <summary>Namen auf das Spieler-Land des Planeten übertragen (nur Stadtstaat bzw. gegründetes Land, nie ein NPC-Land).</summary>
        void Apply()
        {
            var c = planet?.PlayerCityState;
            if (c == null) return;
            c.Name = DisplayName;
            c.CapitalName = CityName;
            c.Color = FlagColor;
        }

        // ------------------------------------------------------------ Spielstand

        public string SaveId => "realm";
        public int SaveVersion => 1;

        public void Write(SaveWriter w)
        {
            w.String(CityName);
            w.String(NationName);
            w.UInt(FlagColor);
            w.IntList(CityRegions);
            // Besitz je Region (−1 = frei); ohne Weltkarte leer
            int n = planet?.Regions.Count ?? 0;
            w.Int(n);
            for (int i = 0; i < n; i++) w.Int(planet.Regions[i].CountryId);
        }

        public void Read(SaveReader r, int version)
        {
            CityName = Clean(r.String()) ?? DefaultCityName;
            NationName = Clean(r.String());
            FlagColor = r.UInt() & 0xFFFFFFu;
            var regions = r.IntList();
            int n = r.Count(4);
            var owners = new int[n];
            for (int i = 0; i < n; i++) owners[i] = r.Int();

            CityRegions.Clear();
            int rc = planet?.Regions.Count ?? 0;
            foreach (int id in regions) if (id >= 0 && id < rc && !CityRegions.Contains(id)) CityRegions.Add(id);
            if (planet != null)
            {
                if (n != rc) throw new SaveFormatException($"{n} Regionen gespeichert, die Welt hat {rc} (anderer Seed?).");
                for (int i = 0; i < n; i++)
                {
                    int o = owners[i];
                    if (o < -1 || o >= planet.Countries.Count) throw new SaveFormatException($"Region {i}: unbekanntes Land {o}.");
                    var region = planet.Regions[i];
                    if (region.CountryId != o) planet.AssignRegion(region, o >= 0 ? planet.Countries[o] : null);
                }
            }
            Apply();
        }
    }
}
