namespace CityBuilder.Game.UI
{
    /// <summary>
    /// Ränder, die die Spiel-UI (UI Toolkit) belegt – in IMGUI-/Bildschirmpixeln (Ursprung oben links).
    /// Die übrigen IMGUI-Fenster (Inspektor, Verkehrs-/Versorgungsfenster, Debug-Anzeige) richten sich danach,
    /// damit sie nicht unter der oberen Leiste oder der Bauleiste liegen. Ohne neue UI gelten die alten Werte.
    /// </summary>
    public static class HudLayout
    {
        /// <summary>Unterkante der oberen Leiste + Abstand.</summary>
        public static float TopInset = 10f;
        /// <summary>Platz, den die Bauleiste unten belegt (vom unteren Bildschirmrand gemessen).</summary>
        public static float BottomInset = 160f;
        /// <summary>Rechte Kante der linken Ansichtsleiste + Abstand.</summary>
        public static float LeftInset = 10f;
    }
}
