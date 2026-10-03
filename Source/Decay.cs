using System;

namespace KesslerSymptoms
{
    /// <summary>
    /// Orbital lifetime of debris, standing in for drag decay. Lifetime grows exponentially
    /// with periapsis height above the atmosphere, like real low orbits. With the defaults on
    /// Kerbin (Earth-calendar time): 75 km ~2 weeks, 80 km ~2 months, 90 km ~1.6 years,
    /// 100 km ~17 years, 120 km+ millennia. Airless bodies have no drag, so their debris never decays.
    /// KSP's own Max Persistent Debris setting stays the hard cap on top of this.
    /// </summary>
    public static class Decay
    {
        /// <summary>Lifetime in game seconds of an orbit with this periapsis radius (infinite if none).</summary>
        public static double LifetimeSeconds(CelestialBody body, double peR)
        {
            if (!body.atmosphere || body.atmosphereDepth <= 0) return double.PositiveInfinity;

            double height = peR - body.Radius - body.atmosphereDepth;
            double exponent = height / (Settings.DecayScaleHeightFraction * body.atmosphereDepth);
            if (exponent > 300) return double.PositiveInfinity; // far beyond any save's lifetime
            return Settings.DecayBaseHours * 3600.0 * Math.Exp(exponent);
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
            if (seconds >= 1000 * year) return ">1000 y";
            if (seconds >= year) return (seconds / year).ToString("F1") + " y";
            if (seconds >= day) return (seconds / day).ToString("F1") + " d";
            return (seconds / 3600.0).ToString("F1") + " h";
        }
    }
}
