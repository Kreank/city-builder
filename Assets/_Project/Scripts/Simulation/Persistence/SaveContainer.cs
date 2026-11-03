using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace CityBuilder.Simulation.Persistence
{
    /// <summary>Kopfdaten eines Spielstands (unkomprimiert lesbar – für die Liste im Lade-Menü).</summary>
    public sealed class SaveInfo
    {
        public int FormatVersion;
        /// <summary>Spielversion, mit der gespeichert wurde.</summary>
        public string GameVersion;
        /// <summary>Name des Spielstands (frei, z. B. Stadtname oder „Autosave 2“).</summary>
        public string Name;
        /// <summary>Zeitstempel = Tick der Simulationsuhr (keine Systemzeit).</summary>
        public long Tick;
        public int Population;
        public double Balance;
        /// <summary>Größe der Nutzdaten (komprimiert) in Bytes.</summary>
        public int PayloadBytes;
        /// <summary>Prüfsumme (FNV-1a 64) der unkomprimierten Nutzdaten.</summary>
        public ulong PayloadHash;

        public int Day => (int)(Tick / Core.SimulationClock.TicksPerDay);
    }

    /// <summary>Ein Abschnitt: Id, Version und die Bytes, die das System geschrieben hat.</summary>
    public sealed class SaveSection
    {
        public string Id;
        public int Version;
        public byte[] Data;

        public override string ToString() => $"{Id} v{Version} ({Data?.Length ?? 0} B)";
    }

    /// <summary>
    /// Dateiformat des Spielstands:
    /// <code>
    /// Kopf (unkomprimiert): Magic „CBSV“, Formatversion (u16), Spielversion, Name, Tick, Einwohner, Kontostand,
    ///                       Länge der Nutzdaten, FNV-1a-64-Prüfsumme der unkomprimierten Nutzdaten
    /// Nutzdaten (GZip):     Anzahl Abschnitte, je Abschnitt: Id (Text), Version (i32), Länge (i32), Bytes
    /// </code>
    /// Unbekannte Abschnitte werden beim Laden übersprungen, fehlende bleiben im Standardzustand – so können künftige
    /// Systeme Abschnitte ergänzen, ohne alte Stände unlesbar zu machen. Little Endian, Texte UTF-8.
    /// </summary>
    public sealed class SaveContainer
    {
        public static readonly byte[] Magic = { (byte)'C', (byte)'B', (byte)'S', (byte)'V' };
        /// <summary>Version des Containers (Kopf + Abschnittsliste). Abschnitte haben eigene Versionen.</summary>
        public const ushort FormatVersion = 1;
        /// <summary>Obergrenze gegen unsinnige Längen in beschädigten Dateien.</summary>
        const int MaxPayloadBytes = 512 * 1024 * 1024;

        public SaveInfo Info = new SaveInfo();
        public readonly List<SaveSection> Sections = new List<SaveSection>();

        public SaveSection Find(string id)
        {
            foreach (var s in Sections) if (s.Id == id) return s;
            return null;
        }

        /// <summary>Ersetzt bzw. ergänzt einen Abschnitt (für Tests und Werkzeuge).</summary>
        public void Set(string id, int version, byte[] data)
        {
            var s = Find(id);
            if (s == null) Sections.Add(s = new SaveSection { Id = id });
            s.Version = version;
            s.Data = data;
        }

        public bool Remove(string id) => Sections.RemoveAll(s => s.Id == id) > 0;

        // ------------------------------------------------------------ Schreiben

        public void WriteTo(Stream output)
        {
            byte[] payload;
            using (var raw = new MemoryStream())
            {
                using (var w = new BinaryWriter(raw, new UTF8Encoding(false), leaveOpen: true))
                {
                    w.Write(Sections.Count);
                    foreach (var s in Sections)
                    {
                        w.Write(s.Id ?? "");
                        w.Write(s.Version);
                        w.Write(s.Data.Length);
                        w.Write(s.Data);
                    }
                }
                payload = raw.ToArray();
            }
            Info.FormatVersion = FormatVersion;
            Info.PayloadHash = Fnv.Hash(payload);

            byte[] compressed;
            using (var buffer = new MemoryStream())
            {
                using (var gz = new GZipStream(buffer, CompressionLevel.Fastest, leaveOpen: true)) gz.Write(payload, 0, payload.Length);
                compressed = buffer.ToArray();
            }
            Info.PayloadBytes = compressed.Length;

            using (var w = new BinaryWriter(output, new UTF8Encoding(false), leaveOpen: true))
            {
                w.Write(Magic);
                w.Write(FormatVersion);
                w.Write(Info.GameVersion ?? "");
                w.Write(Info.Name ?? "");
                w.Write(Info.Tick);
                w.Write(Info.Population);
                w.Write(Info.Balance);
                w.Write(Info.PayloadBytes);
                w.Write(Info.PayloadHash);
                w.Write(compressed);
            }
        }

        public byte[] ToBytes()
        {
            using (var ms = new MemoryStream())
            {
                WriteTo(ms);
                return ms.ToArray();
            }
        }

        // ------------------------------------------------------------ Lesen

        /// <summary>Nur den Kopf lesen (schnell, ohne zu entpacken).</summary>
        public static SaveInfo ReadInfo(Stream input)
        {
            try
            {
                using (var r = new BinaryReader(input, new UTF8Encoding(false), leaveOpen: true)) return ReadHeader(r);
            }
            catch (SaveLoadException) { throw; }
            catch (Exception e) when (e is EndOfStreamException || e is IOException || e is FormatException || e is ArgumentException)
            {
                throw new SaveLoadException("Der Spielstand ist beschädigt (Kopf unlesbar).", e);
            }
        }

        static SaveInfo ReadHeader(BinaryReader r)
        {
            var magic = r.ReadBytes(Magic.Length);
            if (magic.Length != Magic.Length) throw new SaveLoadException("Datei ist leer oder kein Spielstand.");
            for (int i = 0; i < Magic.Length; i++)
                if (magic[i] != Magic[i]) throw new SaveLoadException("Die Datei ist kein City-Builder-Spielstand.");
            var info = new SaveInfo { FormatVersion = r.ReadUInt16() };
            if (info.FormatVersion == 0 || info.FormatVersion > FormatVersion)
                throw new SaveLoadException($"Spielstand-Format {info.FormatVersion} ist neuer als dieses Spiel (kennt bis {FormatVersion}).");
            info.GameVersion = r.ReadString();
            info.Name = r.ReadString();
            info.Tick = r.ReadInt64();
            info.Population = r.ReadInt32();
            info.Balance = r.ReadDouble();
            info.PayloadBytes = r.ReadInt32();
            info.PayloadHash = r.ReadUInt64();
            if (info.Tick < 0 || info.PayloadBytes < 0 || info.PayloadBytes > MaxPayloadBytes) throw new SaveLoadException("Der Spielstand ist beschädigt (Kopf ungültig).");
            return info;
        }

        public static SaveContainer ReadFrom(Stream input)
        {
            try
            {
                using (var r = new BinaryReader(input, new UTF8Encoding(false), leaveOpen: true))
                {
                    var c = new SaveContainer { Info = ReadHeader(r) };
                    var compressed = r.ReadBytes(c.Info.PayloadBytes);
                    if (compressed.Length != c.Info.PayloadBytes) throw new SaveLoadException("Der Spielstand ist unvollständig (Datei abgeschnitten).");

                    byte[] payload;
                    using (var gz = new GZipStream(new MemoryStream(compressed), CompressionMode.Decompress))
                    using (var ms = new MemoryStream())
                    {
                        var buf = new byte[81920];
                        int n;
                        while ((n = gz.Read(buf, 0, buf.Length)) > 0)
                        {
                            ms.Write(buf, 0, n);
                            if (ms.Length > MaxPayloadBytes) throw new SaveLoadException("Der Spielstand ist beschädigt (Nutzdaten zu groß).");
                        }
                        payload = ms.ToArray();
                    }
                    if (Fnv.Hash(payload) != c.Info.PayloadHash) throw new SaveLoadException("Der Spielstand ist beschädigt (Prüfsumme stimmt nicht).");

                    using (var pr = new BinaryReader(new MemoryStream(payload), new UTF8Encoding(false)))
                    {
                        int count = pr.ReadInt32();
                        if (count < 0 || count > 10000) throw new SaveLoadException("Der Spielstand ist beschädigt (Abschnittsliste).");
                        for (int i = 0; i < count; i++)
                        {
                            var s = new SaveSection { Id = pr.ReadString(), Version = pr.ReadInt32() };
                            int len = pr.ReadInt32();
                            if (len < 0 || len > payload.Length) throw new SaveLoadException($"Der Spielstand ist beschädigt (Abschnitt „{s.Id}“).");
                            s.Data = pr.ReadBytes(len);
                            if (s.Data.Length != len) throw new SaveLoadException($"Der Spielstand ist beschädigt (Abschnitt „{s.Id}“ abgeschnitten).");
                            c.Sections.Add(s);
                        }
                    }
                    return c;
                }
            }
            catch (SaveLoadException) { throw; }
            catch (Exception e) when (e is EndOfStreamException || e is IOException || e is InvalidDataException || e is FormatException ||
                                      e is ArgumentException || e is OverflowException)
            {
                throw new SaveLoadException("Der Spielstand ist beschädigt und kann nicht geladen werden.", e);
            }
        }

        public static SaveContainer FromBytes(byte[] data)
        {
            if (data == null) throw new SaveLoadException("Keine Daten.");
            using (var ms = new MemoryStream(data, writable: false)) return ReadFrom(ms);
        }
    }

    /// <summary>FNV-1a 64 – einfache, schnelle, plattformunabhängige Prüfsumme (Determinismus-Vergleich, Dateiprüfung).</summary>
    public static class Fnv
    {
        public const ulong Offset = 14695981039346656037UL;
        public const ulong Prime = 1099511628211UL;

        public static ulong Hash(byte[] data, ulong h = Offset)
        {
            for (int i = 0; i < data.Length; i++) { h ^= data[i]; h *= Prime; }
            return h;
        }

        public static ulong Hash(string s, ulong h = Offset)
        {
            foreach (char c in s) { h ^= c; h *= Prime; }
            return h;
        }

        public static ulong Mix(ulong h, ulong value)
        {
            for (int i = 0; i < 8; i++) { h ^= (byte)(value >> (8 * i)); h *= Prime; }
            return h;
        }
    }
}
