using System;

namespace CityBuilder.Simulation.Core
{
    /// <summary>
    /// Speicherbarer Ersatz für <see cref="System.Random"/> mit Seed: derselbe Algorithmus (Knuths subtraktiver
    /// Generator, wie <c>new Random(seed)</c> in .NET Framework/Mono und .NET ab 6 im Kompatibilitätsmodus) und damit
    /// <b>exakt dieselbe Zahlenfolge</b> – bestehende Abläufe ändern sich nicht. Anders als <see cref="System.Random"/>
    /// lässt sich der Zustand speichern und wiederherstellen (Spielstand, <see cref="Write"/>/<see cref="Read"/>).
    /// </summary>
    public sealed class CompatRandom
    {
        const int MBig = int.MaxValue;
        const int MSeed = 161803398;

        readonly int[] seedArray = new int[56];
        int inext, inextp;

        public CompatRandom(int seed)
        {
            int subtraction = seed == int.MinValue ? int.MaxValue : Math.Abs(seed);
            int mj = MSeed - subtraction;
            seedArray[55] = mj;
            int mk = 1;
            for (int i = 1; i < 55; i++)
            {
                int ii = 21 * i % 55;
                seedArray[ii] = mk;
                mk = mj - mk;
                if (mk < 0) mk += MBig;
                mj = seedArray[ii];
            }
            for (int k = 1; k < 5; k++)
            for (int i = 1; i < 56; i++)
            {
                seedArray[i] -= seedArray[1 + (i + 30) % 55];
                if (seedArray[i] < 0) seedArray[i] += MBig;
            }
            inext = 0;
            inextp = 21;
        }

        int InternalSample()
        {
            int locINext = inext, locINextp = inextp;
            if (++locINext >= 56) locINext = 1;
            if (++locINextp >= 56) locINextp = 1;
            int retVal = seedArray[locINext] - seedArray[locINextp];
            if (retVal == MBig) retVal--;
            if (retVal < 0) retVal += MBig;
            seedArray[locINext] = retVal;
            inext = locINext;
            inextp = locINextp;
            return retVal;
        }

        double Sample() => InternalSample() * (1.0 / MBig);

        double SampleForLargeRange()
        {
            int result = InternalSample();
            if (InternalSample() % 2 == 0) result = -result;
            double d = result;
            d += int.MaxValue - 1;
            d /= 2.0 * int.MaxValue - 1;
            return d;
        }

        /// <summary>0 ≤ x &lt; int.MaxValue.</summary>
        public int Next() => InternalSample();

        /// <summary>0 ≤ x &lt; max (wie <see cref="System.Random.Next(int)"/>).</summary>
        public int Next(int maxValue)
        {
            if (maxValue < 0) throw new ArgumentOutOfRangeException(nameof(maxValue));
            return (int)(Sample() * maxValue);
        }

        /// <summary>min ≤ x &lt; max (wie <see cref="System.Random.Next(int, int)"/>).</summary>
        public int Next(int minValue, int maxValue)
        {
            if (minValue > maxValue) throw new ArgumentOutOfRangeException(nameof(minValue));
            long range = (long)maxValue - minValue;
            return range <= int.MaxValue
                ? (int)(Sample() * range) + minValue
                : (int)((long)(SampleForLargeRange() * range) + minValue);
        }

        /// <summary>0 ≤ x &lt; 1.</summary>
        public double NextDouble() => Sample();

        // ------------------------------------------------------------ Spielstand

        /// <summary>Zustand schreiben (58 Ganzzahlen).</summary>
        public void Write(Persistence.SaveWriter w)
        {
            for (int i = 0; i < seedArray.Length; i++) w.Int(seedArray[i]);
            w.Int(inext);
            w.Int(inextp);
        }

        /// <summary>Zustand lesen (Gegenstück zu <see cref="Write"/>).</summary>
        public void Read(Persistence.SaveReader r)
        {
            for (int i = 0; i < seedArray.Length; i++) seedArray[i] = r.Int();
            inext = r.Int();
            inextp = r.Int();
            if (inext < 0 || inext > 55 || inextp < 0 || inextp > 55) throw new Persistence.SaveFormatException("Zufallszustand ungültig.");
        }
    }
}
