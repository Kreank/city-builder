using CityBuilder.Simulation.Persistence;

namespace CityBuilder.Simulation.Tiles
{
    /// <summary>
    /// Kacheln – Spielstand „tiles“ v1: Kacheln je Seite, Besitz je Kachel, Zahl der Zukäufe, Summe der Kaufpreise.
    /// Fehlt der Abschnitt (alte Stände: 2-km-Karte = eine Kachel), bleibt der Startzustand (alles bzw. die Mitte gekauft).
    /// </summary>
    public sealed partial class TileSystem : ISaveable
    {
        public string SaveId => "tiles";
        public int SaveVersion => 1;

        public void Write(SaveWriter w)
        {
            w.Int(TilesPerSide);
            var bytes = new byte[owned.Length];
            for (int i = 0; i < owned.Length; i++) bytes[i] = owned[i] ? (byte)1 : (byte)0;
            w.Bytes(bytes);
            w.Int(Purchases);
            w.Double(TotalSpent);
        }

        public void Read(SaveReader r, int version)
        {
            int n = r.Int();
            var bytes = r.Bytes();
            int purchases = r.Int();
            double spent = r.Double();
            if (n != TilesPerSide || bytes == null || bytes.Length != owned.Length)
                throw new SaveFormatException("Kacheln passen nicht zur Kartengröße.");
            for (int i = 0; i < owned.Length; i++) owned[i] = bytes[i] != 0;
            if (!Restricted) owned[0] = true;
            Purchases = purchases < 0 ? 0 : purchases;
            TotalSpent = spent;
            Version++;
        }
    }
}
