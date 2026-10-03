using System;

namespace KesslerSymptoms
{
    /// <summary>
    /// The altitude bands around one body plus their current debris tallies.
    /// All radii are measured from the body's centre (like Orbit.PeR/ApR), not from the surface.
    /// </summary>
    public class BandSet
    {
        public readonly CelestialBody Body;
        public readonly double[] Inner;
        public readonly double[] Outer;

        /// <summary>Sum of fractional debris weights overlapping each band.</summary>
        public readonly double[] DebrisWeight;
        /// <summary>Number of debris vessels whose orbit touches each band at all.</summary>
        public readonly int[] DebrisCount;
        /// <summary>Current decayed explosion-spike density in each band.</summary>
        public readonly double[] Spike;

        /// <summary>Volume of each band relative to the lowest one; used to normalise density.</summary>
        private readonly double[] relVolume;

        public int Count { get { return Inner.Length; } }

        public BandSet(CelestialBody body, int bands, double ceilingRadii, double growth)
        {
            Body = body;
            double floor = body.minOrbitalDistance;
            double ceiling = body.Radius * ceilingRadii;
            if (body.sphereOfInfluence > 0 && !double.IsInfinity(body.sphereOfInfluence))
                ceiling = Math.Min(ceiling, body.sphereOfInfluence);
            if (ceiling <= floor)
                ceiling = floor * 1.5; // tiny SOI around a huge atmosphere; still give it a shell

            Inner = new double[bands];
            Outer = new double[bands];
            DebrisWeight = new double[bands];
            DebrisCount = new int[bands];
            Spike = new double[bands];
            relVolume = new double[bands];

            // Geometric thicknesses t0, t0*g, t0*g^2 ... summing exactly to the shell height.
            double height = ceiling - floor;
            double t0 = growth <= 1.0 + 1e-9
                ? height / bands
                : height * (growth - 1.0) / (Math.Pow(growth, bands) - 1.0);

            double r = floor;
            for (int i = 0; i < bands; i++)
            {
                Inner[i] = r;
                r += t0 * Math.Pow(growth, i);
                Outer[i] = r;
            }
            Outer[bands - 1] = ceiling; // absorb rounding

            double v0 = ShellVolume(0);
            for (int i = 0; i < bands; i++)
                relVolume[i] = ShellVolume(i) / v0;
        }

        private double ShellVolume(int i)
        {
            return Math.Pow(Outer[i], 3) - Math.Pow(Inner[i], 3);
        }

        public void ClearDebris()
        {
            Array.Clear(DebrisWeight, 0, DebrisWeight.Length);
            Array.Clear(DebrisCount, 0, DebrisCount.Length);
        }

        /// <summary>Index of the band containing radius r, or -1 if outside the shell.</summary>
        public int IndexOf(double r)
        {
            for (int i = 0; i < Inner.Length; i++)
                if (r >= Inner[i] && r < Outer[i])
                    return i;
            return -1;
        }

        /// <summary>
        /// Spread one unit of debris across the bands overlapped by [pe, ap], in proportion
        /// to radial overlap. A near-circular orbit goes entirely into one band.
        /// </summary>
        public void AddOrbit(double pe, double ap, double weight)
        {
            double span = ap - pe;
            if (span < 1.0)
            {
                int i = IndexOf(pe);
                if (i >= 0)
                {
                    DebrisWeight[i] += weight;
                    DebrisCount[i]++;
                }
                return;
            }

            for (int i = 0; i < Inner.Length; i++)
            {
                double overlap = Math.Min(ap, Outer[i]) - Math.Max(pe, Inner[i]);
                if (overlap <= 0) continue;
                DebrisWeight[i] += weight * overlap / span;
                DebrisCount[i]++;
            }
        }

        /// <summary>
        /// Volume-normalised density: what the tier thresholds are compared against.
        /// Debris weight is scaled by DebrisWeightMultiplier; spikes are already in density units.
        /// </summary>
        public double Density(int i)
        {
            return (DebrisWeight[i] * Settings.DebrisWeightMultiplier + Spike[i]) / relVolume[i];
        }

        public double InnerAltitude(int i) { return Inner[i] - Body.Radius; }
        public double OuterAltitude(int i) { return Outer[i] - Body.Radius; }
    }
}
