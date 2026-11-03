using System;
using System.Collections.Generic;
using System.IO;
using CityBuilder.Simulation.Core;

namespace CityBuilder.Simulation.Persistence
{
    /// <summary>
    /// Speichern und Laden einer ganzen <see cref="SimulationWorld"/> (Format: <see cref="SaveContainer"/>).
    /// <para>
    /// Laden = neue Welt mit den gespeicherten <see cref="WorldSettings"/> und dem Seed über
    /// <see cref="SimulationWorld.CreateDefault"/> erzeugen (Planet/Gelände entstehen deterministisch neu, alle Hooks sind
    /// verdrahtet) und danach jeden Abschnitt der <see cref="SaveRegistry"/> einlesen. Die alte Welt bleibt unberührt –
    /// schlägt das Laden fehl, gibt es eine <see cref="SaveLoadException"/> und das Spiel läuft mit der alten Welt weiter.
    /// </para>
    /// <para>
    /// Determinismus: Welt A speichert, Welt B wird daraus geladen → A und B laufen danach bitgleich weiter
    /// (<see cref="StateHash"/>; Test <c>SaveLoadTests</c>).
    /// </para>
    /// </summary>
    public static class SaveGame
    {
        /// <summary>Spielversion im Kopf (nur Information; Kompatibilität regeln die Abschnittsversionen).</summary>
        public const string GameVersion = "0.6";
        /// <summary>Abschnitt mit Einstellungen, Seed und Uhr – wird vor allen anderen gelesen (baut die Welt).</summary>
        public const string WorldSectionId = "world";
        /// <summary>
        /// v2: Zeitmodell – Verkehrsschritt im laufenden Tick. v3 (Spielstart &amp; Startregion): Anteil freies Land + Stadtname;
        /// v1/v2-Stände laden mit freiem Land 0 (altes politische Modell).
        /// </summary>
        const int WorldSectionVersion = 3;

        // ------------------------------------------------------------ Speichern

        /// <summary>Zustand der Welt als Container (Abschnitte) – verändert die Welt nicht.</summary>
        public static SaveContainer Capture(SimulationWorld world, string name = null, string gameVersion = GameVersion)
        {
            if (world == null) throw new ArgumentNullException(nameof(world));
            var saveables = SaveRegistry.Collect(world);
            foreach (var s in saveables)
                if (s is IBeforeSave b) b.BeforeSave();
            var c = new SaveContainer();
            c.Info.GameVersion = gameVersion ?? GameVersion;
            c.Info.Name = name ?? "";
            c.Info.Tick = world.Clock.Tick;
            c.Info.Population = world.Citizens?.Stats.Population ?? 0;
            c.Info.Balance = world.Economy?.Balance ?? 0;
            c.Set(WorldSectionId, WorldSectionVersion, Bytes(w => WriteWorld(world, w)));
            foreach (var s in saveables)
            {
                if (s.SaveId == WorldSectionId || c.Find(s.SaveId) != null)
                    throw new InvalidOperationException($"Abschnitts-Id „{s.SaveId}“ ist doppelt vergeben.");
                c.Set(s.SaveId, s.SaveVersion, Bytes(s.Write));
            }
            return c;
        }

        public static void Save(SimulationWorld world, Stream output, string name = null) => Capture(world, name).WriteTo(output);

        public static byte[] SaveToBytes(SimulationWorld world, string name = null) => Capture(world, name).ToBytes();

        /// <summary>Speichert atomar: erst in eine Temp-Datei, dann umbenennen (ein Absturz beim Schreiben zerstört keinen alten Stand).</summary>
        public static void SaveToFile(SimulationWorld world, string path, string name = null)
        {
            var bytes = SaveToBytes(world, name);
            var dir = Path.GetDirectoryName(Path.GetFullPath(path));
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            string tmp = path + ".tmp";
            File.WriteAllBytes(tmp, bytes);
            if (File.Exists(path)) File.Delete(path);
            File.Move(tmp, path);
        }

        static byte[] Bytes(Action<SaveWriter> write)
        {
            using (var ms = new MemoryStream())
            {
                var w = new SaveWriter(ms);
                write(w);
                w.Flush();
                return ms.ToArray();
            }
        }

        // ------------------------------------------------------------ Laden

        public static SimulationWorld Load(Stream input) => Restore(SaveContainer.ReadFrom(input));

        public static SimulationWorld Load(byte[] data) => Restore(SaveContainer.FromBytes(data));

        public static SimulationWorld LoadFromFile(string path)
        {
            byte[] data;
            try { data = File.ReadAllBytes(path); }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                throw new SaveLoadException($"Spielstand konnte nicht gelesen werden: {e.Message}", e);
            }
            return Load(data);
        }

        /// <summary>Nur den Kopf einer Datei lesen (Name, Spielzeit, Einwohner, Kontostand).</summary>
        public static SaveInfo ReadInfo(string path)
        {
            try
            {
                using (var f = File.OpenRead(path)) return SaveContainer.ReadInfo(f);
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                throw new SaveLoadException($"Spielstand konnte nicht gelesen werden: {e.Message}", e);
            }
        }

        /// <summary>Baut aus einem Container eine neue Welt.</summary>
        public static SimulationWorld Restore(SaveContainer c)
        {
            var ws = c.Find(WorldSectionId) ?? throw new SaveLoadException("Der Spielstand enthält keine Weltdaten.");
            if (ws.Version > WorldSectionVersion) throw new SaveLoadException("Der Spielstand stammt aus einer neueren Spielversion (Weltdaten).");

            WorldSettings settings;
            int seed, substep;
            long tick;
            try
            {
                using (var ms = new MemoryStream(ws.Data, writable: false))
                {
                    var r = new SaveReader(ms);
                    ReadWorld(r, ws.Version, out settings, out seed, out tick, out substep);
                }
            }
            catch (SaveFormatException e) { throw new SaveLoadException($"Weltdaten beschädigt: {e.Message}", e); }

            SimulationWorld world;
            try { world = SimulationWorld.CreateDefault(settings, seed); }
            catch (Exception e) when (e is ArgumentException || e is OverflowException || e is OutOfMemoryException || e is IndexOutOfRangeException)
            {
                throw new SaveLoadException("Weltdaten ungültig (Welt kann nicht erzeugt werden).", e);
            }
            world.Clock.Restore(tick);
            world.RestoreSubstep(substep);

            var saveables = SaveRegistry.Collect(world);
            foreach (var s in saveables)
            {
                var sec = c.Find(s.SaveId);
                if (sec == null) continue; // fehlt (älterer Stand) → Standardzustand
                if (sec.Version > s.SaveVersion)
                    throw new SaveLoadException($"Abschnitt „{s.SaveId}“ (Version {sec.Version}) stammt aus einer neueren Spielversion (kennt bis {s.SaveVersion}).");
                if (sec.Version < 1) throw new SaveLoadException($"Abschnitt „{s.SaveId}“ hat eine ungültige Version.");
                try
                {
                    using (var ms = new MemoryStream(sec.Data, writable: false))
                    {
                        var r = new SaveReader(ms);
                        s.Read(r, sec.Version);
                        if (r.Remaining != 0) throw new SaveFormatException($"{r.Remaining} Bytes am Ende nicht gelesen.");
                    }
                }
                catch (SaveFormatException e) { throw new SaveLoadException($"Abschnitt „{s.SaveId}“ ist beschädigt: {e.Message}", e); }
                catch (SaveLoadException) { throw; }
                catch (Exception e) when (e is ArgumentException || e is InvalidOperationException || e is NullReferenceException ||
                                          e is IndexOutOfRangeException || e is KeyNotFoundException || e is InvalidCastException ||
                                          e is OverflowException || e is EndOfStreamException || e is OutOfMemoryException)
                {
                    throw new SaveLoadException($"Abschnitt „{s.SaveId}“ ist beschädigt und kann nicht geladen werden.", e);
                }
            }
            foreach (var s in saveables)
                if (s is IAfterLoad a) a.AfterLoad(world);
            return world;
        }

        // ------------------------------------------------------------ Weltabschnitt

        static void WriteWorld(SimulationWorld world, SaveWriter w)
        {
            var s = world.Settings;
            w.Float(s.SizeMeters);
            w.Float(s.EnvironmentCellSize);
            w.Bool(s.GenerateTerrain);
            w.Int(s.TerrainSeed);
            w.Float(s.TerrainCellSize);
            w.Bool(s.UnlimitedFunds);
            w.Double(s.StartingFunds);
            w.Bool(s.GenerateWorld);
            w.Int(s.WorldSeed);
            w.Int(s.StartRegion);
            w.Bool(s.Planet != null);
            if (s.Planet != null)
            {
                w.Int(s.Planet.Width);
                w.Int(s.Planet.Height);
                w.Int(s.Planet.Plates);
                w.Float(s.Planet.LandFraction);
                w.Int(s.Planet.TargetRegions);
                w.Int(s.Planet.TargetCountries);
            }
            w.Int(world.Seed);
            w.Long(world.Clock.Tick);
            w.Int(world.SubstepInTick); // v2
            // v3 Spielstart: politisches Modell (Anteil freies Land) und Stadtname der Einstellungen
            w.Float(s.Planet?.FreeLandShare ?? World.PlanetSettings.DefaultFreeLandShare);
            w.String(s.CityName);
        }

        static void ReadWorld(SaveReader r, int version, out WorldSettings s, out int seed, out long tick, out int substep)
        {
            s = new WorldSettings
            {
                SizeMeters = r.Float(),
                EnvironmentCellSize = r.Float(),
                GenerateTerrain = r.Bool(),
                TerrainSeed = r.Int(),
                TerrainCellSize = r.Float(),
                UnlimitedFunds = r.Bool(),
                StartingFunds = r.Double(),
                GenerateWorld = r.Bool(),
                WorldSeed = r.Int(),
                StartRegion = r.Int(),
            };
            if (r.Bool())
                s.Planet = new World.PlanetSettings
                {
                    Width = r.Int(), Height = r.Int(), Plates = r.Int(), LandFraction = r.Float(),
                    TargetRegions = r.Int(), TargetCountries = r.Int(),
                };
            seed = r.Int();
            tick = r.Long();
            substep = version >= 2 ? r.Int() : 0; // v1: immer auf der Tick-Grenze gespeichert
            // Spielstart: v1/v2 kannten kein freies Land → Welt im alten Modell neu erzeugen (alle Regionen gehören NPC-Ländern,
            // Spieler-Land = Land der Startregion). Nur so passen die gespeicherten Länder-/Handelsabschnitte (Länder-Ids).
            float free = 0f;
            if (version >= 3)
            {
                free = r.Float();
                if (!(free >= 0f && free <= 0.9f)) throw new SaveFormatException("Welteinstellungen ungültig (freies Land).");
                s.CityName = r.String();
            }
            // Standardwert ohne eigene Planet-Einstellungen bleibt null (gleiche Bytes beim erneuten Speichern)
            if (s.Planet != null) s.Planet.FreeLandShare = free;
            else if (free != World.PlanetSettings.DefaultFreeLandShare) s.Planet = new World.PlanetSettings { FreeLandShare = free };
            if (!(s.SizeMeters >= 64f && s.SizeMeters <= 65536f) || !(s.EnvironmentCellSize >= 1f) || !(s.TerrainCellSize >= 0.5f) || tick < 0)
                throw new SaveFormatException("Welteinstellungen ungültig.");
            if (s.SizeMeters / s.TerrainCellSize > 8192f || s.SizeMeters / s.EnvironmentCellSize > 8192f)
                throw new SaveFormatException("Welteinstellungen ungültig (Raster zu groß).");
        }

        // ------------------------------------------------------------ Prüfsummen (Determinismus)

        /// <summary>Prüfsumme je Abschnitt (FNV-1a 64 über die gespeicherten Bytes) – zeigt, welches System abweicht.</summary>
        public static List<KeyValuePair<string, ulong>> SectionHashes(SimulationWorld world)
        {
            var result = new List<KeyValuePair<string, ulong>>();
            foreach (var s in Capture(world).Sections) result.Add(new KeyValuePair<string, ulong>(s.Id, Fnv.Hash(s.Data)));
            return result;
        }

        /// <summary>Prüfsumme über den gesamten Zustand aller Systeme (gleich ⇔ alle Abschnitte bitgleich).</summary>
        public static ulong StateHash(SimulationWorld world)
        {
            ulong h = Fnv.Offset;
            foreach (var kv in SectionHashes(world))
            {
                h = Fnv.Hash(kv.Key, h);
                h = Fnv.Mix(h, kv.Value);
            }
            return h;
        }

        /// <summary>Prüfsumme eines einzelnen Systems.</summary>
        public static ulong StateHash(ISaveable system) => Fnv.Hash(Bytes(system.Write));
    }
}
