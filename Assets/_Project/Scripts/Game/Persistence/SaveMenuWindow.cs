using System.Collections.Generic;
using CityBuilder.Game.UI.Panels;
using CityBuilder.Simulation.Core;
using UnityEngine;

namespace CityBuilder.Game.Persistence
{
    /// <summary>
    /// Spielmenü (mittiges Fenster, Esc): Speichern / Speichern unter, Laden (Liste mit Name, Spielzeit, Einwohner,
    /// Kontostand, Dateigröße), Neues Spiel (Welt-Seed/Startregion optional), Einstellungen (Autosave). Logik im
    /// <see cref="SaveManager"/>. Solange das Menü offen ist, ist das Spiel pausiert.
    /// </summary>
    public sealed class SaveMenuWindow : GameWindow
    {
        static readonly string[] TabNames = { "Speichern", "Laden", "Neues Spiel", "Einstellungen" };
        static readonly string[] Months =
        {
            "Januar", "Februar", "März", "April", "Mai", "Juni", "Juli", "August", "September", "Oktober", "November", "Dezember",
        };
        const int PageSize = 8;

        readonly SaveManager manager;
        bool open;
        int tab;
        string saveAsName = "";
        string seedText = "", regionText = "";
        SaveSlot selected;
        List<SaveSlot> slots;
        float slotsReadAt = -10f;
        int page;
        bool confirmNew;
        int speedBeforeOpen = -1;

        public SaveMenuWindow(SaveManager manager) : base(WindowSlot.Center) { this.manager = manager; }

        public bool IsOpen => open;

        public void Open(int startTab = 0)
        {
            open = true;
            tab = startTab;
            selected = null;
            confirmNew = false;
            slots = null;
            ContentVersion++;
            if (Runner != null && !Runner.IsPaused) { speedBeforeOpen = Runner.SpeedIndex; Runner.SetSpeed(0); }
        }

        public override float OpenScore => open ? 1f : 0f;

        public override void Close()
        {
            open = false;
            GameKeyboard.TextInputActive = false;
            if (speedBeforeOpen > 0 && Runner != null && Runner.IsPaused) Runner.SetSpeed(speedBeforeOpen);
            speedBeforeOpen = -1;
        }

        /// <summary>Datum der Spielzeit (Anzeige-Kalender 12 × 30 Tage, wie die obere Leiste).</summary>
        public static string DateText(long tick)
        {
            long day = tick / SimulationClock.TicksPerDay;
            int doy = (int)(day % SimulationClock.DaysPerYear);
            int year = (int)(day / SimulationClock.DaysPerYear) + 1;
            return $"{doy % 30 + 1}. {Months[Mathf.Clamp(doy / 30, 0, 11)]}, Jahr {year}";
        }

        static string Money(double v) => v.ToString("#,##0", System.Globalization.CultureInfo.GetCultureInfo("de-DE")) + " €";

        public override void DrawHead(Ui ui) =>
            Head(ui, "Spiel", Tone.Info, "Spielmenü", (manager.CurrentName ?? "ungespeichertes Spiel") + " · " + DateText(World.Clock.Tick));

        public override void DrawBody(Ui ui)
        {
            ui.Tabs(TabNames, tab, i => { tab = i; selected = null; confirmNew = false; slots = null; ContentVersion++; });
            switch (tab)
            {
                case 0: DrawSave(ui); break;
                case 1: DrawLoad(ui); break;
                case 2: DrawNewGame(ui); break;
                default: DrawSettings(ui); break;
            }
        }

        void DrawSave(Ui ui)
        {
            ui.Section("Speichern");
            using (ui.Fields())
            {
                ui.Field("Spielstand", manager.CurrentName ?? "– noch nicht gespeichert –");
                ui.Field("Spielzeit", DateText(World.Clock.Tick));
                ui.Field("Einwohner", World.Citizens.Stats.Population.ToString("#,##0"));
                ui.Field("Kontostand", Money(World.Economy.Balance), World.Economy.Balance < 0 ? Tone.Bad : Tone.None);
            }
            using (ui.Actions())
                ui.ActionButton(manager.CurrentName != null ? "Speichern" : "Speichern (neuer Name)", () => manager.Save(),
                    ActionStyle.Primary, enabled: !manager.Busy, tip: "Strg+S speichert schnell in den Schnellspeicher");
            ui.Section("Speichern unter");
            ui.TextInput("Name", saveAsName, v => saveAsName = v, () => { if (manager.SaveAs(saveAsName)) saveAsName = ""; });
            using (ui.Actions())
                ui.ActionButton("Speichern unter", () => { if (manager.SaveAs(saveAsName)) saveAsName = ""; }, ActionStyle.Secondary,
                    enabled: !manager.Busy && !string.IsNullOrWhiteSpace(saveAsName));
            if (manager.Busy) ui.Note("Speichern läuft …");
            if (!string.IsNullOrEmpty(manager.LastError)) ui.Message("Fehler", manager.LastError, Tone.Bad);
            ui.Note("Strg+S Schnellspeichern · Strg+L Schnellladen · Ablage: " + SaveManager.Folder);
        }

        void DrawLoad(Ui ui)
        {
            if (slots == null || Time.unscaledTime - slotsReadAt > 3f)
            {
                slots = manager.ListSaves();
                slotsReadAt = Time.unscaledTime;
                if (selected != null) selected = slots.Find(s => s.Path == selected.Path);
            }
            if (slots.Count == 0) { ui.Note("Noch keine Spielstände."); return; }
            int pages = (slots.Count + PageSize - 1) / PageSize;
            page = Mathf.Clamp(page, 0, pages - 1);
            ui.Section("Spielstände", $"{slots.Count}");
            for (int i = page * PageSize; i < Mathf.Min(slots.Count, (page + 1) * PageSize); i++)
            {
                var s = slots[i];
                string title = s.Info != null && !string.IsNullOrEmpty(s.Info.Name) ? s.Info.Name : s.FileName;
                string sub = s.Info != null
                    ? $"{DateText(s.Info.Tick)} · {s.Info.Population:#,##0} Einw. · {Money(s.Info.Balance)} · {s.Bytes / 1024.0:0} KB"
                    : $"unlesbar: {s.Error}";
                string badge = s.IsAutosave ? "Auto" : s.IsQuicksave ? "Schnell" : s.Modified.ToString("dd.MM. HH:mm");
                var slot = s;
                ui.ListItem(Ui.Initials(title), s == selected ? new Color(0.3f, 0.6f, 1f) : new Color(0.35f, 0.4f, 0.5f), title, sub, badge,
                    s.Info == null ? Tone.Bad : s.IsAutosave ? Tone.Muted : Tone.Info, () => { selected = slot; });
            }
            ui.Pager(page, pages, p => page = p);
            if (selected == null) { ui.Note("Spielstand anklicken, dann laden."); return; }
            using (ui.Actions())
            {
                ui.ActionButton("Laden", () => { if (manager.Load(selected.Path)) Close(); }, ActionStyle.Primary, enabled: selected.Info != null,
                    tip: "Ungespeicherter Fortschritt geht verloren");
                ui.ActionButton("Löschen", () => { if (manager.Delete(selected)) { selected = null; slots = null; } }, ActionStyle.Danger);
            }
        }

        void DrawNewGame(Ui ui)
        {
            ui.Section("Neues Spiel");
            ui.Note("Leer lassen = Einstellungen der Szene (Welt-Seed) bzw. automatische Startregion (gemäßigt, fruchtbar, Fluss/Küste).");
            ui.TextInput("Welt-Seed", seedText, v => seedText = v, null, 12);
            ui.TextInput("Startregion (Nr.)", regionText, v => regionText = v, null, 6);
            bool seedOk = string.IsNullOrWhiteSpace(seedText) || int.TryParse(seedText.Trim(), out _);
            bool regionOk = string.IsNullOrWhiteSpace(regionText) || int.TryParse(regionText.Trim(), out _);
            if (!seedOk || !regionOk) ui.Message("Eingabe", "Seed und Startregion müssen ganze Zahlen sein.", Tone.Warn);
            using (ui.Actions())
            {
                if (!confirmNew)
                    ui.ActionButton("Neues Spiel …", () => confirmNew = true, ActionStyle.Secondary, enabled: seedOk && regionOk);
                else
                {
                    ui.ActionButton("Wirklich neu starten", () =>
                    {
                        int? seed = int.TryParse(seedText.Trim(), out int s) ? s : (int?)null;
                        int? region = int.TryParse(regionText.Trim(), out int r) ? r : (int?)null;
                        manager.NewGame(seed, region);
                        confirmNew = false;
                        Close();
                    }, ActionStyle.Danger);
                    ui.ActionButton("Abbrechen", () => confirmNew = false);
                }
            }
            if (confirmNew) ui.Message("Achtung", "Nicht gespeicherter Fortschritt geht verloren (Autosave bleibt erhalten).", Tone.Warn);
        }

        void DrawSettings(Ui ui)
        {
            ui.Section("Autosave", $"Ringpuffer {SaveManager.AutosaveSlots} Stände");
            int current = manager.AutosaveMinutes;
            int idx = System.Array.IndexOf(SaveManager.AutosaveChoices, current);
            if (idx < 0) idx = 0;
            string Text(int min) => min <= 0 ? "aus" : min < 1440 ? $"alle {min / 60} Spielstunden" : $"alle {min / 1440} Spieltage";
            ui.Stepper("Intervall", Text(current),
                idx > 0 ? () => manager.AutosaveMinutes = SaveManager.AutosaveChoices[idx - 1] : (System.Action)null,
                idx < SaveManager.AutosaveChoices.Length - 1 ? () => manager.AutosaveMinutes = SaveManager.AutosaveChoices[idx + 1] : (System.Action)null,
                current > 0 ? $"{current} Spielminuten" : null, Tone.Muted);
            ui.Note("1 Spieltag = 1440 Spielminuten ≈ 24 s bei Tempo 1. Gespeichert wird im Hintergrund.");
        }
    }
}
