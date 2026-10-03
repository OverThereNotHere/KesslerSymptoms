using System;

namespace KesslerSymptoms
{
    /// <summary>
    /// How long debris lasts before it's removed. Not realistic drag: every orbit decays
    /// eventually (space game), so warping through long Kessler periods clears things out.
    ///
    /// Lifetime runs from DecayMinMonths with periapsis just above the atmosphere (or terrain on
    /// airless bodies) up to DecayMaxYears at the SOI edge, climbing with log(1 + height / (DecayCurveRadii × radius)) so
    /// orbits in between spread out instead of all sitting near one end. Kerbin with defaults
    /// (2 months to 200 years, curve 0.01 radii): 80 km ~5 months, 100 km ~9 months, 250 km
    /// ~2 years, synchronous orbit ~16 years, Mun's orbit ~45 years, Minmus's ~130.
    /// Each piece also gets a fixed random factor, and the result stays within the min and max.
    /// KSP's own Max Persistent Debris setting stays the hard cap on top of this.
    /// </summary>
    public static class Decay
    {
        private static double Month { get { return KSPUtil.dateTimeFormatter.Year / 12.0; } }
        private static double MinSeconds { get { return Settings.DecayMinMonths * Month; } }
        private static double MaxSeconds { get { return Math.Max(MinSeconds, Settings.DecayMaxYears * KSPUtil.dateTimeFormatter.Year); } }

        /// <summary>Lifetime in game seconds of an orbit with this periapsis radius, before randomness.</summary>
        public static double LifetimeSeconds(CelestialBody body, double peR)
        {
            // Floor clears atmosphere and terrain (same as the bands). Ceiling is the real SOI
            // edge, which can be beyond the band shell; bodies with an infinite SOI (the sun)
            // fall back to the band shell's top.
            double floor = body.minOrbitalDistance;
            double ceiling = body.sphereOfInfluence > 0 && !double.IsInfinity(body.sphereOfInfluence)
                ? body.sphereOfInfluence
                : body.Radius * Settings.CeilingRadii;
            if (ceiling <= floor) return MaxSeconds;

            double scale = Math.Max(1.0, Settings.DecayCurveRadii * body.Radius);
            double h = Math.Max(0.0, Math.Min(peR, ceiling) - floor);
            double f = Math.Log(1.0 + h / scale) / Math.Log(1.0 + (ceiling - floor) / scale);
            return MinSeconds * Math.Pow(MaxSeconds / MinSeconds, f);
        }

        /// <summary>This piece's lifetime: the orbit's, times its own random factor, kept within min and max.</summary>
        public static double LifetimeFor(CelestialBody body, double peR, uint persistentId)
        {
            double t = LifetimeSeconds(body, peR) * RandomFactor(persistentId);
            return Math.Max(MinSeconds, Math.Min(MaxSeconds, t));
        }

        /// <summary>
        /// This piece's personal lifetime multiplier, e^(±DecayRandomness). Seeded from the
        /// vessel's persistent id so it stays the same across scans and reloads.
        /// </summary>
        public static double RandomFactor(uint persistentId)
        {
            Random rng = new Random(unchecked((int)(persistentId * 2654435761u)));
            return Math.Exp((rng.NextDouble() * 2.0 - 1.0) * Settings.DecayRandomness);
        }

        /// <summary>Short human-readable duration in the game's calendar, for the Debug tab.</summary>
        public static string Format(double seconds)
        {
            if (double.IsInfinity(seconds)) return "never";
            double year = KSPUtil.dateTimeFormatter.Year;
            double day = KSPUtil.dateTimeFormatter.Day;
            if (seconds >= year) return (seconds / year).ToString("F1") + " y";
            if (seconds >= day) return (seconds / day).ToString("F1") + " d";
            return (seconds / 3600.0).ToString("F1") + " h";
        }
    }
}
