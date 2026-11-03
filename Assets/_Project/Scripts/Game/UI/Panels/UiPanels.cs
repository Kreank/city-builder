namespace CityBuilder.Game.UI.Panels
{
    /// <summary>
    /// Schalter zwischen neuer (UI Toolkit) und alter (IMGUI) Fensterdarstellung.
    /// <para>
    /// Die IMGUI-Fenster mit <b>Aktionen</b> (Stadtkasse: Steuersätze, Verkehrsregeln, Versorgungs-/Dienste-Übersicht,
    /// Hilfe F1) bleiben als Rückfall erhalten – genau wie die IMGUI-<c>RoadToolbar</c>: Fehlen Theme/USS, startet die
    /// neue UI nicht, und das Spiel bleibt trotzdem vollständig bedienbar. Sie zeichnen nur, wenn
    /// <see cref="ToolkitActive"/> falsch ist. Die IMGUI-Darstellung des (rein lesenden, ~600 Zeilen großen) Inspektors
    /// wurde dagegen entfernt, um doppelte Pflege zu vermeiden.
    /// </para>
    /// </summary>
    public static class UiPanels
    {
        /// <summary>Zum Vergleichen/Debuggen: true erzwingt die alten IMGUI-Fenster (vor dem Start setzen).</summary>
        public static bool ForceImgui;

        /// <summary>Wahr, solange die neuen Fenster laufen – dann zeichnen die alten IMGUI-Fenster nicht.</summary>
        public static bool ToolkitActive { get; internal set; }
    }
}
