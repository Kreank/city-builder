using System;

namespace CityBuilder.Simulation.Traffic
{
    /// <summary>Bahn (Stufe 2): Bahnübergänge – eigener Block, damit <c>TrafficSystem.cs</c> nur eine markierte Einhängestelle bekommt.</summary>
    public sealed partial class TrafficSystem
    {
        /// <summary>
        /// (Spur, Strecke des Fahrzeugs) → Strecke der Haltelinie vor dem nächsten geschlossenen Bahnübergang auf dieser Spur,
        /// den das Fahrzeug noch nicht passiert hat; −1 = keiner. null = keine Bahn (alles wie bisher).
        /// </summary>
        public Func<Lane, float, float> BarrierAhead;

        /// <summary>Wartet vor einer Schranke: Wartezeit nicht bis zum Abbruch (90 s) hochzählen – der Zug kommt ja.</summary>
        const float BarrierWaitCap = 20f;
        /// <summary>Fahrzeuge in der Schlange bis so weit hinter der Haltelinie warten ebenfalls ohne Abbruch (Klappbrücke öffnet länger).</summary>
        const float BarrierQueueReach = 160f;

        /// <summary>Begrenzt Abstand/Vorausgeschwindigkeit durch eine geschlossene Schranke; true = sie ist das Hindernis.</summary>
        bool ApplyBarrier(Vehicle v, Lane lane, ref float gap, ref float leaderSpeed)
        {
            if (BarrierAhead == null) return false;
            float stop = BarrierAhead(lane, v.S);
            if (stop < 0f) return false;
            float g = stop - v.S;
            if (g >= gap) return g - gap < BarrierQueueReach; // --- Wasserstraße: auch die Schlange vor der Schranke/Klappbrücke wartet geduldig
            gap = MathF.Max(0.1f, g);
            leaderSpeed = 0f;
            return true;
        }
    }
}
