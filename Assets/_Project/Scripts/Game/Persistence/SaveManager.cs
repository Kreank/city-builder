using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using CityBuilder.Game.Roads;
using CityBuilder.Game.UI.Hud;
using CityBuilder.Simulation.Core;
using CityBuilder.Simulation.Persistence;
using UnityEngine;

namespace CityBuilder.Game.Persistence
{
    /// <summary>Eintrag der Spielstand-Liste (Kopf gelesen, ohne zu laden).</summary>
    public sealed class SaveSlot
    {
        public string Path;
        public string FileName;
        public SaveInfo Info;
        /// <summary>Fehler beim Lesen des Kopfs (beschädigt/fremd) – dann ist <see cref="Info"/> null.</summary>
        public string Error;
        public long Bytes;
        public DateTime Modified;
        public bool IsAutosave => FileName.StartsWith(SaveManager.AutosavePrefix, StringComparison.OrdinalIgnoreCase);
        public bool IsQuicksave => string.Equals(FileName, SaveManager.QuicksaveFile, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Speichern &amp; Laden im Spiel (Begleiter am <see cref="RoadTool"/>): Spielstände unter
    /// <c>Application.persistentDataPath/Saves</c> (<c>*.cbsave</c>), Speichern/Speichern unter/Laden/Löschen, Neues Spiel,
    /// Schnellspeichern <b>Strg+S</b> / Schnellladen <b>Strg+L</b>, Autosave alle N Spielminuten in einen Ringpuffer aus
    /// 3 Ständen. Das Spielmenü (<see cref="SaveMenuWindow"/>) öffnet <b>Esc</b>, wenn Esc in diesem Frame sonst niemand
    /// gebraucht hat (Fenster schließen, Bau abbrechen, zurück zum Auswählen).
    /// <para>Speichern: Zustand im Hauptthread erfassen (<see cref="SaveGame.Capture"/>, schnell), Komprimieren und
    /// Schreiben im Hintergrund (atomar über eine Temp-Datei). Laden: synchron, danach
    /// <see cref="SimulationRunner.ReplaceWorld"/> → alle Views bauen neu auf.</para>
    /// </summary>
    public sealed class SaveManager : MonoBehaviour
    {
        public const string Extension = ".cbsave";
        public const string AutosavePrefix = "autosave_";
        public const string QuicksaveFile = "schnellspeicher" + Extension;
        public const int AutosaveSlots = 3;
        const string PrefAutosave = "cb.autosave.minutes";
        /// <summary>Auswahl im Menü (Spielminuten; 0 = aus). 1 Spieltag = 1440 Spielminuten = 24 s bei Tempo 1.</summary>
        public static readonly int[] AutosaveChoices = { 0, 720, 1440, 4320, 10080 };

        RoadTool tool;
        SimulationRunner Runner => tool != null ? tool.Runner : null;
        Task writeTask;
        string writeLabel;
        long nextAutosaveTick = -1;
        int autosaveMinutes = -1;

        public SaveMenuWindow Window { get; private set; }
        /// <summary>Spielstart: Neues-Spiel-Fenster (Welt, Vorlieben, Startregion, Stadtname).</summary>
        public NewGameWindow NewGameDialog { get; private set; }

        /// <summary>Name des laufenden Spiels (letztes Speichern/Laden); null = noch nie gespeichert.</summary>
        public string CurrentName { get; private set; }
        public string LastError { get; private set; }
        public bool Busy => writeTask != null && !writeTask.IsCompleted;

        public static string Folder => System.IO.Path.Combine(Application.persistentDataPath, "Saves");

        /// <summary>Autosave-Abstand in Spielminuten (0 = aus), gespeichert in den PlayerPrefs.</summary>
        public int AutosaveMinutes
        {
            get
            {
                if (autosaveMinutes < 0) autosaveMinutes = Mathf.Max(0, PlayerPrefs.GetInt(PrefAutosave, 4320));
                return autosaveMinutes;
            }
            set
            {
                autosaveMinutes = Mathf.Max(0, value);
                PlayerPrefs.SetInt(PrefAutosave, autosaveMinutes);
                ScheduleAutosave();
            }
        }

        public void Init(RoadTool roadTool) => tool = roadTool;

        void Start()
        {
            if (tool == null || Runner == null) { enabled = false; return; }
            var hud = GetComponent<GameHud>();
            if (hud != null && hud.Windows != null)
            {
                Window = hud.Windows.Add(new SaveMenuWindow(this));
                NewGameDialog = hud.Windows.Add(new NewGameWindow(this)); // Spielstart
            }
            Runner.WorldReplaced += ScheduleAutosave;
            ScheduleAutosave();
            // Spielstart: erster Programmstart ohne Spielstand → gleich das Neues-Spiel-Fenster (nicht schließbar)
            if (Runner.AwaitingNewGame)
            {
                if (NewGameDialog != null) NewGameDialog.Open(firstStart: true);
                else NewGame(null, null); // ohne neue UI: Standardwelt wie früher
            }
        }

        void OnDestroy()
        {
            if (Runner != null) Runner.WorldReplaced -= ScheduleAutosave;
            try { writeTask?.Wait(5000); } catch { /* Fehler meldet Update; beim Beenden egal */ }
        }

        void ScheduleAutosave()
        {
            var w = Runner?.World;
            if (w == null) return;
            int min = AutosaveMinutes;
            nextAutosaveTick = min <= 0 ? -1 : w.Clock.Tick + Math.Max(1, min / 6); // 1 Tick = 6 Spielminuten
        }

        // ------------------------------------------------------------ Tasten, Autosave

        void Update()
        {
            if (Runner == null || Runner.World == null) return;
            var kb = GameKeyboard.Current;
            if (kb != null && kb.ctrlKey.isPressed)
            {
                if (kb.sKey.wasPressedThisFrame) QuickSave();
                if (kb.lKey.wasPressedThisFrame) QuickLoad();
            }
            if (nextAutosaveTick >= 0 && Runner.World.Clock.Tick >= nextAutosaveTick && !Busy)
            {
                Autosave();
                ScheduleAutosave();
            }
            PollWrite();
        }

        void LateUpdate()
        {
            // Esc öffnet das Spielmenü – erst nach allen Updates, wenn niemand Esc verbraucht hat
            var kb = GameKeyboard.Current;
            if (kb != null && kb.escapeKey.wasPressedThisFrame && Window != null && !Window.IsOpen &&
                !GameKeyboard.EscapeConsumedThisFrame && UI.Panels.WindowHost.EscapeConsumedFrame != Time.frameCount &&
                tool.IsSelectMode && !tool.IsBusy)
                Window.Open();
            // Sicherheitsnetz: Menü zu → keine Texteingabe mehr aktiv (Fokus kann am versteckten Feld hängen bleiben)
            if ((Window == null || !Window.IsOpen) && (NewGameDialog == null || !NewGameDialog.IsOpen)) GameKeyboard.TextInputActive = false;
        }

        void PollWrite()
        {
            if (writeTask == null || !writeTask.IsCompleted) return;
            if (writeTask.IsFaulted)
            {
                LastError = writeTask.Exception?.GetBaseException().Message;
                HudMessages.Warning("Speichern fehlgeschlagen", LastError, HudIconKind.Warning, "save");
            }
            else HudMessages.Info("Gespeichert", writeLabel, HudIconKind.Info, "save");
            writeTask = null;
        }

        // ------------------------------------------------------------ Speichern

        /// <summary>Speichert unter dem aktuellen Namen (ohne Namen: Stadtname bzw. „Spielstand“).</summary>
        public bool Save() => SaveAs(CurrentName ?? DefaultName());

        public bool SaveAs(string name)
        {
            name = string.IsNullOrWhiteSpace(name) ? DefaultName() : name.Trim();
            if (!Write(PathFor(name), name, $"„{name}“")) return false;
            CurrentName = name;
            return true;
        }

        public bool QuickSave() => Write(System.IO.Path.Combine(Folder, QuicksaveFile), (CurrentName ?? DefaultName()) + " (Schnellspeicher)", "Schnellspeicher (Strg+L lädt)");

        public bool Autosave()
        {
            // Ringpuffer: fehlender Platz zuerst, sonst der älteste
            string best = null;
            DateTime oldest = DateTime.MaxValue;
            for (int i = 1; i <= AutosaveSlots; i++)
            {
                string p = System.IO.Path.Combine(Folder, $"{AutosavePrefix}{i}{Extension}");
                if (!File.Exists(p)) { best = p; break; }
                var t = File.GetLastWriteTimeUtc(p);
                if (t < oldest) { oldest = t; best = p; }
            }
            return Write(best, (CurrentName ?? DefaultName()) + " (Autosave)", "Autosave");
        }

        bool Write(string path, string name, string label)
        {
            if (Busy) { HudMessages.Info("Speichern läuft noch", null, HudIconKind.Info, "save"); return false; }
            SaveContainer container;
            try { container = SaveGame.Capture(Runner.World, name, SaveGame.GameVersion + " (" + Application.version + ")"); }
            catch (Exception e)
            {
                LastError = e.Message;
                Debug.LogException(e);
                HudMessages.Warning("Speichern fehlgeschlagen", e.Message, HudIconKind.Warning, "save");
                return false;
            }
            writeLabel = label;
            writeTask = Task.Run(() =>
            {
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path));
                var bytes = container.ToBytes(); // Komprimieren im Hintergrund – der Container gehört nicht mehr zur Welt
                string tmp = path + ".tmp";
                File.WriteAllBytes(tmp, bytes);
                if (File.Exists(path)) File.Delete(path);
                File.Move(tmp, path);
            });
            return true;
        }

        // ------------------------------------------------------------ Laden

        public bool QuickLoad()
        {
            string p = System.IO.Path.Combine(Folder, QuicksaveFile);
            if (!File.Exists(p))
            {
                var list = ListSaves();
                if (list.Count == 0) { HudMessages.Info("Kein Spielstand vorhanden", "Strg+S speichert schnell.", HudIconKind.Info, "save"); return false; }
                p = list[0].Path;
            }
            return Load(p);
        }

        public bool Load(string path)
        {
            try { writeTask?.Wait(); } catch { /* Fehler wurde gemeldet */ }
            PollWrite();
            try
            {
                var info = SaveGame.ReadInfo(path);
                var world = SaveGame.LoadFromFile(path);
                Runner.ReplaceWorld(world);
                string name = info.Name ?? "";
                foreach (var suffix in new[] { " (Schnellspeicher)", " (Autosave)" })
                    if (name.EndsWith(suffix)) name = name.Substring(0, name.Length - suffix.Length);
                CurrentName = string.IsNullOrEmpty(name) ? null : name;
                LastError = null;
                HudMessages.Info("Spielstand geladen", $"{info.Name} · {SaveMenuWindow.DateText(info.Tick)}", HudIconKind.Info, "save");
                return true;
            }
            catch (SaveLoadException e)
            {
                LastError = e.Message;
                HudMessages.Warning("Laden fehlgeschlagen", e.Message, HudIconKind.Warning, "save");
                return false;
            }
        }

        public bool Delete(SaveSlot slot)
        {
            try { File.Delete(slot.Path); return true; }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                HudMessages.Warning("Löschen fehlgeschlagen", e.Message, HudIconKind.Warning, "save");
                return false;
            }
        }

        /// <summary>Neues Spiel (optional anderer Welt-Seed/Startregion); der alte Stand wird nicht gespeichert.</summary>
        public void NewGame(int? worldSeed, int? startRegion)
        {
            Runner.NewGame(worldSeed, startRegion);
            CurrentName = null;
            HudMessages.Info("Neues Spiel", worldSeed.HasValue ? $"Welt-Seed {worldSeed}" : null, HudIconKind.Info, "save");
        }

        // --- Spielstart & Startregion
        /// <summary>Neues-Spiel-Fenster öffnen (aus dem Spielmenü); ohne neue UI wie früher sofort neu starten.</summary>
        public void OpenNewGame()
        {
            if (NewGameDialog != null) NewGameDialog.Open(firstStart: false);
            else NewGame(null, null);
        }

        /// <summary>Neues Spiel aus dem Neues-Spiel-Fenster (Seed, Startregion, Stadtname stehen in den Einstellungen).</summary>
        public void StartNewGame(WorldSettings settings)
        {
            Runner.NewGame(settings);
            CurrentName = null;
            var w = Runner.World;
            HudMessages.Info($"{w.Realm.DisplayName} gegründet",
                w.StartRegion != null ? $"Region {w.StartRegion.Name} · Welt-Seed {settings.WorldSeed}" : null, HudIconKind.Info, "save");
        }

        /// <summary>Gibt es schon einen Spielstand (dann startet das Spiel ohne Neues-Spiel-Fenster)?</summary>
        public static bool HasAnySave()
        {
            try { return Directory.Exists(Folder) && Directory.GetFiles(Folder, "*" + Extension).Length > 0; }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException) { return false; }
        }
        // --- Ende Spielstart

        /// <summary>Alle Spielstände, neueste zuerst (nur Köpfe gelesen).</summary>
        public List<SaveSlot> ListSaves()
        {
            var list = new List<SaveSlot>();
            if (!Directory.Exists(Folder)) return list;
            foreach (var path in Directory.GetFiles(Folder, "*" + Extension))
            {
                var fi = new FileInfo(path);
                var slot = new SaveSlot { Path = path, FileName = fi.Name, Bytes = fi.Length, Modified = fi.LastWriteTime };
                try { slot.Info = SaveGame.ReadInfo(path); }
                catch (SaveLoadException e) { slot.Error = e.Message; }
                list.Add(slot);
            }
            list.Sort((a, b) => b.Modified.CompareTo(a.Modified));
            return list;
        }

        string DefaultName()
        {
            var w = Runner?.World;
            return w?.Realm != null ? w.Realm.CityName : w?.StartRegion != null ? w.StartRegion.Name : "Spielstand"; // Spielstart: Stadtname
        }

        static string PathFor(string name)
        {
            var chars = name.ToCharArray();
            var invalid = System.IO.Path.GetInvalidFileNameChars();
            for (int i = 0; i < chars.Length; i++) if (Array.IndexOf(invalid, chars[i]) >= 0) chars[i] = '_';
            string file = new string(chars).Trim().TrimEnd('.');
            if (file.Length == 0) file = "Spielstand";
            if (file.StartsWith(AutosavePrefix, StringComparison.OrdinalIgnoreCase)) file = "_" + file;
            return System.IO.Path.Combine(Folder, file + Extension);
        }
    }
}
