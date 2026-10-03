using System;

namespace KesslerSymptoms
{
    /// <summary>
    /// A debris field in progress on the active vessel: real tier hits at a quick pace plus many
    /// sound-only micro pelts, for a random stretch of game time. Owned by EncounterScheduler.
    /// </summary>
    public class DebrisField
    {
        /// <summary>Cap per frame, so physics warp or a hitch can't fire a pile at once.</summary>
        private const int MaxHitsPerTick = 2;
        private const int MaxPeltsPerTick = 4;

        public readonly int Tier;
        public readonly bool Forced;
        public readonly double EndUT;
        /// <summary>
        /// Whether rails warp is locked out while this field runs. A tier 1 field that arrives
        /// mid-warp doesn't stop the warp, so it doesn't lock it either.
        /// </summary>
        public readonly bool BlocksWarp;

        private double nextHitUT;
        private double nextPeltUT;

        public DebrisField(int tier, bool forced, double now, bool blocksWarp)
        {
            Tier = tier;
            Forced = forced;
            BlocksWarp = blocksWarp;
            double min = Math.Min(Settings.FieldDurationMin, Settings.FieldDurationMax);
            double max = Math.Max(Settings.FieldDurationMin, Settings.FieldDurationMax);
            EndUT = now + min + UnityEngine.Random.value * (max - min);
            nextHitUT = now + NextGap(Settings.FieldSecondsPerHit);
            nextPeltUT = now + NextGap(PeltGap);
        }

        private static double PeltGap
        {
            get { return Settings.FieldPeltsPerSecond > 0 ? 1.0 / Settings.FieldPeltsPerSecond : double.PositiveInfinity; }
        }

        /// <summary>Random gap with the given mean (exponential), so arrivals feel irregular.</summary>
        private static double NextGap(double mean)
        {
            if (double.IsInfinity(mean)) return mean;
            return -mean * Math.Log(Math.Max(1e-6, 1.0 - UnityEngine.Random.value));
        }

        /// <summary>
        /// Advance to <paramref name="now"/>. Returns false once the field has passed. While the
        /// vessel can't be hit (e.g. packed as warp spins up or down) the clock runs but nothing lands.
        /// </summary>
        public bool Tick(Vessel vessel, double now)
        {
            if (now >= EndUT) return false;
            if (Encounters.Blocker(vessel) != null)
            {
                nextHitUT = Math.Max(nextHitUT, now);
                nextPeltUT = Math.Max(nextPeltUT, now);
                return true;
            }

            for (int n = 0; now >= nextHitUT && n < MaxHitsPerTick; n++)
            {
                Encounters.Trigger(vessel, Tier, Forced, false);
                nextHitUT += NextGap(Settings.FieldSecondsPerHit);
            }
            if (now >= nextHitUT) nextHitUT = now + NextGap(Settings.FieldSecondsPerHit);

            for (int n = 0; now >= nextPeltUT && n < MaxPeltsPerTick; n++)
            {
                Encounters.Pelt(vessel);
                nextPeltUT += NextGap(PeltGap);
            }
            if (now >= nextPeltUT) nextPeltUT = now + NextGap(PeltGap);

            return true;
        }
    }
}
