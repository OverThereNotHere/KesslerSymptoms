using System;
using UnityEngine;

namespace KesslerSymptoms
{
    /// <summary>
    /// Flight-scene loop for the active vessel: works out which band it's in, posts a text
    /// warning when the debris tier rises, and rolls for random encounters while off rails.
    /// Each encounter is either a one-off impact or, more likely in denser bands, a debris field.
    /// </summary>
    [KSPAddon(KSPAddon.Startup.Flight, false)]
    public class EncounterScheduler : MonoBehaviour
    {
        private const float CheckInterval = 0.25f;
        /// <summary>Cap on game time rolled in one check, so a hitch can't produce a burst of hits.</summary>
        private const double MaxStepSeconds = 5.0;

        private static readonly Color WarningColor = new Color(1f, 0.85f, 0.3f);

        public static EncounterScheduler Instance { get; private set; }

        // Live readout for the Effects tab.
        public static int CurrentBand { get; private set; }
        public static double CurrentDensity { get; private set; }
        public static int CurrentTier { get; private set; }
        public static double CurrentHitsPerHour { get; private set; }
        public static bool Rolling { get; private set; }

        /// <summary>The field currently hitting the active vessel, or null.</summary>
        public DebrisField Field { get; private set; }
        private Vessel fieldVessel;

        private Vessel lastVessel;
        private int lastTier;
        private double lastUT = -1;
        private float nextCheck;

        public void Start()
        {
            Instance = this;
            CurrentBand = -1;
        }

        public void OnDestroy()
        {
            if (Instance == this) Instance = null;
            CurrentBand = -1;
            CurrentDensity = 0;
            CurrentTier = 0;
            CurrentHitsPerHour = 0;
            Rolling = false;
        }

        /// <summary>
        /// Start a debris field on the active vessel, replacing any current one. Forced fields
        /// (debug buttons) ignore the tier toggles. Returns false if the vessel can't be hit now.
        /// </summary>
        public bool StartField(int tier, bool forced)
        {
            Vessel v = FlightGlobals.ActiveVessel;
            if (Encounters.Blocker(v) != null) return false;
            if (!forced && !Settings.TierEnabled(tier)) return false;

            Field = new DebrisField(tier, forced, Planetarium.GetUniversalTime());
            fieldVessel = v;
            Encounters.Alert(string.Format("Debris field! Tier {0}: {1}", tier, Encounters.TierNames[tier]), true);
            Log.Info(string.Format("Tier {0} debris field on {1}{2}, {3:F0} s",
                tier, v.vesselName, forced ? " (forced)" : "", Field.EndUT - Planetarium.GetUniversalTime()));
            return true;
        }

        /// <summary>End the current field early without the "passed" message (debug button).</summary>
        public void StopField()
        {
            EndField(false);
        }

        private void EndField(bool passed)
        {
            if (passed)
                ScreenMessages.PostScreenMessage("Debris field passed", 3f, ScreenMessageStyle.UPPER_CENTER, WarningColor);
            Field = null;
            fieldVessel = null;
        }

        public void Update()
        {
            // Fields tick every frame so their pelts can come faster than the band check.
            if (Field != null)
            {
                Vessel fv = FlightGlobals.ActiveVessel;
                if (fv != fieldVessel || Encounters.Blocker(fv) != null)
                    EndField(false);
                else if (!Field.Tick(fv, Planetarium.GetUniversalTime()))
                    EndField(true);
            }

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
            if (Field != null) return; // one encounter at a time

            double perSecond = CurrentHitsPerHour / 3600.0;
            if (UnityEngine.Random.value >= 1.0 - Math.Exp(-perSecond * dt)) return;

            if (UnityEngine.Random.value < Settings.FieldChance(density))
                StartField(tier, false);
            else
                Encounters.Trigger(v, tier, false);
        }
    }
}
