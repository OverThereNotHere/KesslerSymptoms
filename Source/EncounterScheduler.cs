using System;
using UnityEngine;

namespace KesslerSymptoms
{
    /// <summary>
    /// Flight-scene loop for the active vessel: works out which band it's in, posts a text
    /// warning when the debris tier rises, and rolls for random impacts while off rails.
    /// </summary>
    [KSPAddon(KSPAddon.Startup.Flight, false)]
    public class EncounterScheduler : MonoBehaviour
    {
        private const float CheckInterval = 0.25f;
        /// <summary>Cap on game time rolled in one check, so a hitch can't produce a burst of hits.</summary>
        private const double MaxStepSeconds = 5.0;

        private static readonly Color WarningColor = new Color(1f, 0.85f, 0.3f);

        // Live readout for the Effects tab.
        public static int CurrentBand { get; private set; }
        public static double CurrentDensity { get; private set; }
        public static int CurrentTier { get; private set; }
        public static double CurrentHitsPerHour { get; private set; }
        public static bool Rolling { get; private set; }

        private Vessel lastVessel;
        private int lastTier;
        private double lastUT = -1;
        private float nextCheck;

        public void Start()
        {
            CurrentBand = -1;
        }

        public void OnDestroy()
        {
            CurrentBand = -1;
            CurrentDensity = 0;
            CurrentTier = 0;
            CurrentHitsPerHour = 0;
            Rolling = false;
        }

        public void Update()
        {
            if (Time.realtimeSinceStartup < nextCheck) return;
            nextCheck = Time.realtimeSinceStartup + CheckInterval;

            KesslerScenario scn = KesslerScenario.Instance;
            Vessel v = FlightGlobals.ActiveVessel;
            if (scn == null || v == null)
            {
                lastUT = -1;
                Rolling = false;
                return;
            }
            if (v != lastVessel)
            {
                lastVessel = v;
                lastTier = 0; // so switching into a cluttered band warns too
                lastUT = -1;
            }

            BandSet set = scn.GetBands(v.mainBody);
            int band = set.IndexOf(v.altitude + v.mainBody.Radius);
            double density = band >= 0 ? set.Density(band) : 0;
            int tier = band >= 0 ? Settings.TierFor(density) : 0;

            CurrentBand = band;
            CurrentDensity = density;
            CurrentTier = tier;
            CurrentHitsPerHour = tier >= 1 ? Settings.HitsPerHourPerDensity * density : 0;

            // Entering more cluttered space: text only. Crossing between bands of the same
            // tier stays quiet.
            if (tier > lastTier && Settings.TierEnabled(tier))
            {
                ScreenMessages.PostScreenMessage(
                    string.Format("Entering debris: Tier {0} ({1}) at {2:N0}-{3:N0} km",
                        tier, Encounters.TierNames[tier],
                        set.InnerAltitude(band) / 1000, set.OuterAltitude(band) / 1000),
                    5f, ScreenMessageStyle.UPPER_CENTER, WarningColor);
            }
            lastTier = tier;

            // Impact rolls: Poisson arrivals over the game time since the last check.
            Rolling = tier >= 1 && Settings.TierEnabled(tier) && Encounters.Blocker(v) == null;
            double ut = Planetarium.GetUniversalTime();
            if (!Rolling || lastUT < 0)
            {
                lastUT = Rolling ? ut : -1;
                return;
            }
            double dt = Math.Min(ut - lastUT, MaxStepSeconds);
            lastUT = ut;

            double perSecond = CurrentHitsPerHour / 3600.0;
            if (UnityEngine.Random.value < 1.0 - Math.Exp(-perSecond * dt))
                Encounters.Trigger(v, tier, false);
        }
    }
}
