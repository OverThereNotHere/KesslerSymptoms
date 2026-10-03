using System;
using UnityEngine;

namespace KesslerSymptoms
{
    /// <summary>
    /// Flight-scene loop for the active vessel: works out which band it's in, posts a text
    /// warning when the debris tier rises, and rolls for random encounters. Each encounter is
    /// either a one-off impact or, more likely in denser bands, a debris field.
    ///
    /// Time warp: with RailsWarpEncounters on, encounters keep rolling during rails warp.
    /// Tier 1 plays out without stopping the warp; tier 2/3 drop you to 1x first and land once
    /// the vessel is back in physics. Rails warp is locked out while a field is running.
    /// </summary>
    [KSPAddon(KSPAddon.Startup.Flight, false)]
    public class EncounterScheduler : MonoBehaviour
    {
        private const float CheckInterval = 0.25f;
        /// <summary>Cap on game time rolled in one check outside warp, so a hitch can't produce a burst.</summary>
        private const double MaxStepSeconds = 5.0;
        /// <summary>Cap during rails warp (one game day), just to bound absurd frame gaps.</summary>
        private const double MaxWarpStepSeconds = 86400.0;
        /// <summary>Real seconds to wait for the vessel to unpack after dropping out of warp.</summary>
        private const float PendingTimeout = 10f;

        private static readonly Color WarningColor = new Color(1f, 0.85f, 0.3f);

        public static EncounterScheduler Instance { get; private set; }

        // Live readout for the Effects tab.
        public static int CurrentBand { get; private set; }
        public static double CurrentDensity { get; private set; }
        public static int CurrentTier { get; private set; }
        /// <summary>Average encounters per game hour right now (either mode).</summary>
        public static double CurrentHitsPerHour { get; private set; }
        /// <summary>Chance mode: chance per check right now.</summary>
        public static double CurrentCheckChance { get; private set; }
        public static bool Rolling { get; private set; }

        /// <summary>The field currently hitting the active vessel, or null.</summary>
        public DebrisField Field { get; private set; }
        private Vessel fieldVessel;
        private FieldFx fieldFx;

        /// <summary>A tier 2/3 encounter waiting for the vessel to leave warp and unpack.</summary>
        private class Pending
        {
            public int Tier;
            public bool IsField;
            public bool Forced;
            public Vessel Vessel;
            public float Expires;
        }
        private Pending pending;

        private Vessel lastVessel;
        private int lastTier;
        private double lastUT = -1;
        private float nextCheck;
        private float nextWarpLockMessage;
        /// <summary>Chance mode: game seconds accumulated toward the next check.</summary>
        private double checkTimer;

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
        /// Start an encounter on the active vessel: the one way in for both the random rolls and
        /// the debug buttons. Forced encounters ignore the tier toggles. During rails warp a
        /// tier 2/3 encounter drops out of warp and lands once physics resumes.
        /// Returns false if the vessel can't be hit now.
        /// </summary>
        public bool Request(int tier, bool isField, bool forced)
        {
            Vessel v = FlightGlobals.ActiveVessel;
            if (Encounters.Blocker(v) != null) return false;
            if (!forced && !Settings.TierEnabled(tier)) return false;

            if (v.packed && tier >= 2 && Settings.WarpDropOut)
            {
                pending = new Pending
                {
                    Tier = tier, IsField = isField, Forced = forced, Vessel = v,
                    Expires = Time.realtimeSinceStartup + PendingTimeout,
                };
                TimeWarp.SetRate(0, true);
                ScreenMessages.PostScreenMessage(
                    string.Format("Tier {0} debris ahead: leaving time warp", tier),
                    3f, ScreenMessageStyle.UPPER_CENTER, WarningColor);
                Log.Info(string.Format("Tier {0} {1} during warp: dropping out", tier, isField ? "field" : "encounter"));
                return true;
            }

            if (isField) StartField(v, tier, forced);
            else Encounters.Trigger(v, tier, forced);
            return true;
        }

        /// <summary>End the current field early without the "passed" message (debug button).</summary>
        public void StopField()
        {
            EndField(false);
        }

        private void StartField(Vessel v, int tier, bool forced)
        {
            // Only a tier 1 field arriving mid-warp leaves the warp alone.
            bool blocksWarp = Settings.WarpDropOut && !(v.packed && tier == 1);
            if (fieldFx != null) fieldFx.End(); // a forced field can replace a running one
            Field = new DebrisField(tier, forced, Planetarium.GetUniversalTime(), blocksWarp);
            fieldVessel = v;
            fieldFx = FieldFx.Create(v, tier, Field.FlowWorld(v));
            Encounters.Alert(string.Format("Debris field! Tier {0}: {1}", tier, Encounters.TierNames[tier]), true);
            Log.Info(string.Format("Tier {0} debris field on {1}{2}, {3:F0} s",
                tier, v.vesselName, forced ? " (forced)" : "", Field.EndUT - Planetarium.GetUniversalTime()));
        }

        private void EndField(bool passed)
        {
            if (passed)
                ScreenMessages.PostScreenMessage("Debris field passed", 3f, ScreenMessageStyle.UPPER_CENTER, WarningColor);
            Field = null;
            fieldVessel = null;
            if (fieldFx != null) fieldFx.End(); // fades out, then removes itself
            fieldFx = null;
        }

        public void Update()
        {
            UpdatePending();
            UpdateField();

            if (Time.realtimeSinceStartup < nextCheck) return;
            nextCheck = Time.realtimeSinceStartup + CheckInterval;
            UpdateRolls();
        }

        /// <summary>Land a tier 2/3 encounter once its vessel is out of warp and in physics.</summary>
        private void UpdatePending()
        {
            if (pending == null) return;
            Vessel v = FlightGlobals.ActiveVessel;
            if (v != pending.Vessel || Time.realtimeSinceStartup > pending.Expires)
            {
                pending = null;
                return;
            }
            if (v.packed || Encounters.Blocker(v) != null) return;

            Pending p = pending;
            pending = null;
            if (p.IsField) StartField(v, p.Tier, p.Forced);
            else Encounters.Trigger(v, p.Tier, p.Forced);
        }

        /// <summary>Tick the field every frame (pelts outpace the band check) and hold off rails warp.</summary>
        private void UpdateField()
        {
            if (Field == null) return;

            if (Field.BlocksWarp && Encounters.InRailsWarp)
            {
                TimeWarp.SetRate(0, true);
                if (Time.realtimeSinceStartup >= nextWarpLockMessage)
                {
                    ScreenMessages.PostScreenMessage("Can't time warp inside a debris field",
                        2f, ScreenMessageStyle.UPPER_CENTER, WarningColor);
                    nextWarpLockMessage = Time.realtimeSinceStartup + 2f;
                }
            }

            // Only a vessel switch or unload ends a field early. Brief packing (e.g. while warp
            // spins up or down) just pauses its hits; see DebrisField.Tick.
            Vessel fv = FlightGlobals.ActiveVessel;
            if (fv == null || fv != fieldVessel || !fv.loaded)
                EndField(false);
            else if (!Field.Tick(fv, Planetarium.GetUniversalTime()))
                EndField(true);
        }

        private void UpdateRolls()
        {
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
            if (Settings.ChanceEncounters)
            {
                CurrentCheckChance = Settings.CheckChance(density);
                CurrentHitsPerHour = CurrentCheckChance <= 0 ? 0
                    : CurrentCheckChance >= 1 ? 3600.0 / Settings.ChanceCheckSeconds
                    : -Math.Log(1.0 - CurrentCheckChance) * 3600.0 / Settings.ChanceCheckSeconds;
            }
            else
            {
                CurrentCheckChance = 0;
                CurrentHitsPerHour = tier >= 1 ? Settings.HitsPerHourPerDensity * density : 0;
            }

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

            // Encounter rolls. Any tier 1+ band rolls (the picked tier is checked against its
            // toggle in Request, since lower tiers can come up in higher bands).
            Rolling = tier >= 1 && Settings.EffectsEnabled && Encounters.Blocker(v) == null;
            double ut = Planetarium.GetUniversalTime();
            if (!Rolling || lastUT < 0)
            {
                lastUT = Rolling ? ut : -1;
                return;
            }
            double dt = Math.Min(ut - lastUT, v.packed ? MaxWarpStepSeconds : MaxStepSeconds);
            lastUT = ut;

            double chanceAny;
            if (Settings.ChanceEncounters)
            {
                // Every check that came due since last time (several per frame in warp) is
                // folded into one roll: chance at least one of them hits.
                checkTimer += dt;
                int checks = (int)Math.Floor(checkTimer / Settings.ChanceCheckSeconds);
                checkTimer -= checks * Settings.ChanceCheckSeconds;
                chanceAny = checks > 0 ? 1.0 - Math.Pow(1.0 - CurrentCheckChance, checks) : 0;
            }
            else
            {
                // Poisson arrivals over the game time since the last check.
                chanceAny = 1.0 - Math.Exp(-CurrentHitsPerHour / 3600.0 * dt);
            }

            if (Field != null || pending != null) return; // one encounter at a time
            if (UnityEngine.Random.value >= chanceAny) return;

            Request(Settings.PickEncounterTier(tier), UnityEngine.Random.value < Settings.FieldChance(density), false);
        }
    }
}
