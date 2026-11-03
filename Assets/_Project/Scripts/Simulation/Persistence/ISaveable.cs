using System;

namespace CityBuilder.Simulation.Persistence
{
    /// <summary>
    /// Ein System, das seinen Zustand im Spielstand ablegt – <b>jedes neue System implementiert diese Schnittstelle,
    /// wird in <see cref="SaveRegistry"/> angemeldet und erscheint damit automatisch im Determinismus-Test</b>.
    /// <para>
    /// Regeln: <see cref="Write"/> darf nichts verändern (auch keine Caches auffrischen). <see cref="Read"/> wird auf
    /// einer frisch mit <c>SimulationWorld.CreateDefault</c> erzeugten Welt aufgerufen (Hooks/Events sind verdrahtet,
    /// frühere Abschnitte der <see cref="SaveRegistry"/>-Reihenfolge sind schon gelesen) und ersetzt den Zustand
    /// vollständig; abgeleitete Daten (Spurgraph, Netze, Raster-Indizes …) baut das System danach selbst neu auf.
    /// Fehlt der Abschnitt (älterer Spielstand), bleibt der Standardzustand der frischen Welt.
    /// </para>
    /// <para>
    /// Migrationen: <see cref="SaveVersion"/> erhöhen, wenn sich das Format ändert, und in <see cref="Read"/> alte
    /// Versionen weiter lesen (fehlende Werte mit Standard füllen). Neuere Versionen als bekannt → Fehler.
    /// </para>
    /// </summary>
    public interface ISaveable
    {
        /// <summary>Eindeutige, nie mehr geänderte Abschnitts-Id (kurz, ASCII), z. B. „roads“.</summary>
        string SaveId { get; }

        /// <summary>Aktuelle Formatversion des Abschnitts (ab 1).</summary>
        int SaveVersion { get; }

        void Write(SaveWriter w);

        /// <param name="version">Version, mit der der Abschnitt geschrieben wurde (1 … <see cref="SaveVersion"/>).</param>
        void Read(SaveReader r, int version);
    }

    /// <summary>Optional: wird nach dem Lesen <b>aller</b> Abschnitte aufgerufen (Registrierungsreihenfolge), auch ohne eigenen Abschnitt.</summary>
    public interface IAfterLoad
    {
        void AfterLoad(Core.SimulationWorld world);
    }

    /// <summary>
    /// Optional: vor dem Schreiben <b>ohnehin fällige</b>, aufgeschobene Aktualisierungen abschließen (z. B. Spurgraph
    /// nach Straßenbau ohne Tick dazwischen). Nur für Dinge, die der nächste Tick sowieso täte – sonst verändert
    /// Speichern den Spielverlauf.
    /// </summary>
    public interface IBeforeSave
    {
        void BeforeSave();
    }

    /// <summary>Spielstand unlesbar (beschädigt, abgeschnitten, fremdes Format, zu neue Version) – mit verständlicher Meldung.</summary>
    public class SaveLoadException : Exception
    {
        public SaveLoadException(string message, Exception inner = null) : base(message, inner) { }
    }

    /// <summary>Formatfehler innerhalb eines Abschnitts (wird beim Laden in eine <see cref="SaveLoadException"/> mit Abschnittsnamen übersetzt).</summary>
    public sealed class SaveFormatException : SaveLoadException
    {
        public SaveFormatException(string message) : base(message) { }
    }
}
