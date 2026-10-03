using System;
using System.Reflection;
using KSP.Localization;
using UnityEngine;

namespace KesslerSymptoms
{
    /// <summary>
    /// Debris damage state for one part, added by an MM patch to every part with deployables or
    /// resources. Holds a self-sealing tank puncture (persisted in the save) and replaces stock's
    /// deployable Repair with a free one (same skill rule, no kits). Stock's own button can't be
    /// made free: it counts carried kits as -1 when you have none, so even "0 kits" fails.
    ///
    /// Leak model: rate(t) = rate0 × e^-(t - start)/tau, as a fraction of each leakable resource's
    /// capacity per second. Drain over any interval has a closed form, so time warp and time
    /// spent unloaded are caught up exactly the next time the part runs. While loaded, a LeakFx
    /// child shows a vapour jet and hiss scaled by the current rate.
    /// </summary>
    public class ModuleKesslerDamage : PartModule
    {
        /// <summary>Resources that never leak even though they have mass and can flow.</summary>
        private static readonly string[] NeverLeaks = { "Ore", "Ablator", "SolidFuel" };

        private static readonly FieldInfo RepairKitsField = typeof(ModuleDeployablePart)
            .GetField("repairKitsNecessary", BindingFlags.Instance | BindingFlags.NonPublic);
        /// <summary>Stock's repair itself (protected virtual, so subclasses' versions still run).</summary>
        private static readonly MethodInfo DoRepairMethod = typeof(ModuleDeployablePart)
            .GetMethod("DoRepair", BindingFlags.Instance | BindingFlags.NonPublic);
        private const string StockRepairEvent = "EventRepairExternal";

        [KSPField(isPersistant = true)] public bool leaking;
        [KSPField(isPersistant = true)] public double leakStartUT;
        /// <summary>Initial leak rate, fraction of capacity per second.</summary>
        [KSPField(isPersistant = true)] public double leakRate0;
        /// <summary>Seal time constant, seconds (half-life / ln 2).</summary>
        [KSPField(isPersistant = true)] public double leakTau;
        /// <summary>UT up to which the leak has already been drained.</summary>
        [KSPField(isPersistant = true)] public double leakAppliedUT;
        [KSPField(isPersistant = true)] public Vector3 leakLocalPos;
        [KSPField(isPersistant = true)] public Vector3 leakLocalNormal;

        [KSPField(guiActive = false, guiName = "Debris damage")]
        public string leakStatus = "";

        private LeakFx fx;

        public bool Leaking { get { return leaking; } }

        /// <summary>True if this part holds anything that could leak.</summary>
        public bool CanPuncture
        {
            get
            {
                foreach (PartResource r in part.Resources)
                    if (IsLeakable(r)) return true;
                return false;
            }
        }

        private static bool IsLeakable(PartResource r)
        {
            if (r.maxAmount <= 0 || r.info == null) return false;
            if (r.info.density <= 0 || r.info.resourceFlowMode == ResourceFlowMode.NO_FLOW) return false;
            return Array.IndexOf(NeverLeaks, r.resourceName) < 0;
        }

        public override void OnStartFinished(StartState state)
        {
            // Stock sets the kit count in its own OnStart (from part mass, capped by
            // PART_REPAIR_MAX_KIT_AMOUNT); this runs after every OnStart, so ours sticks.
            if (RepairKitsField != null)
            {
                foreach (ModuleDeployablePart dp in part.FindModulesImplementing<ModuleDeployablePart>())
                    RepairKitsField.SetValue(dp, 0);
            }
            else
            {
                Log.Warn("ModuleDeployablePart.repairKitsNecessary not found; repairs keep needing kits");
            }
            UpdateUI();
        }

        /// <summary>Start a leak at a world-space point on the part's surface.</summary>
        public void Puncture(Vector3 worldPoint, Vector3 worldNormal)
        {
            double now = Planetarium.GetUniversalTime();
            double minRate = Math.Min(Settings.LeakRateMinPctPerMin, Settings.LeakRateMaxPctPerMin);
            double maxRate = Math.Max(Settings.LeakRateMinPctPerMin, Settings.LeakRateMaxPctPerMin);
            double pctPerMin = minRate + UnityEngine.Random.value * (maxRate - minRate);

            leaking = true;
            leakStartUT = now;
            leakAppliedUT = now;
            leakRate0 = pctPerMin / 100.0 / 60.0;
            leakTau = Settings.LeakSealHalfLifeMinutes * 60.0 / Math.Log(2.0);
            leakLocalPos = part.transform.InverseTransformPoint(worldPoint);
            leakLocalNormal = part.transform.InverseTransformDirection(worldNormal).normalized;
            UpdateUI();
        }

        /// <summary>Fraction of capacity per second leaking at this UT.</summary>
        private double RateAt(double ut)
        {
            return leakRate0 * Math.Exp(-(ut - leakStartUT) / leakTau);
        }

        /// <summary>Fraction of capacity lost between two UTs (integral of RateAt).</summary>
        private double LossBetween(double a, double b)
        {
            return leakRate0 * leakTau * (Math.Exp(-(a - leakStartUT) / leakTau) - Math.Exp(-(b - leakStartUT) / leakTau));
        }

        public void FixedUpdate()
        {
            if (!leaking || !HighLogic.LoadedSceneIsFlight || part.vessel == null) return;

            double now = Planetarium.GetUniversalTime();
            if (now <= leakAppliedUT) return;

            double lossFraction = LossBetween(leakAppliedUT, now);
            double massFlow = 0; // tonnes per second right now, for thrust
            bool anyLeft = false;
            foreach (PartResource r in part.Resources)
            {
                if (!IsLeakable(r)) continue;
                r.amount = Math.Max(0, r.amount - r.maxAmount * lossFraction);
                if (r.amount > 0)
                {
                    anyLeft = true;
                    massFlow += r.maxAmount * r.info.density * RateAt(now);
                }
            }
            leakAppliedUT = now;

            // Reaction push from venting gas: the jet leaves along the surface normal.
            if (anyLeft && !part.packed && part.rb != null && massFlow > 0)
            {
                Vector3 pos = part.transform.TransformPoint(leakLocalPos);
                Vector3 dir = -part.transform.TransformDirection(leakLocalNormal);
                part.rb.AddForceAtPosition(dir * (float)(massFlow * Settings.LeakVentSpeed), pos, ForceMode.Force);
            }

            // Sealed once the remaining total loss is negligible, or there's nothing left to lose.
            double remaining = leakRate0 * leakTau * Math.Exp(-(now - leakStartUT) / leakTau);
            if (!anyLeft || remaining < 0.001)
            {
                StopLeak();
                ScreenMessages.PostScreenMessage(
                    anyLeft ? part.partInfo.title + ": leak has sealed itself" : part.partInfo.title + ": leaked dry",
                    4f, ScreenMessageStyle.UPPER_CENTER);
            }
            else if (Time.frameCount % 25 == 0)
            {
                UpdateUI();
            }
        }

        private void StopLeak()
        {
            leaking = false;
            leakRate0 = 0;
            DestroyFx();
            UpdateUI();
        }

        /// <summary>
        /// Show our Repair instead of stock's on broken deployables, and keep the leak's visuals
        /// and sound in step with its current rate. Runs after stock's module (we're added later),
        /// so stock re-enabling its button each frame is overridden.
        /// </summary>
        public void Update()
        {
            if (HighLogic.LoadedSceneIsFlight) UpdateRepairButtons();

            if (!leaking || !HighLogic.LoadedSceneIsFlight)
            {
                if (fx != null) DestroyFx();
                return;
            }
            if (fx == null)
            {
                bool big = leakRate0 * 6000.0 >= LeakFx.BigLeakPctPerMin;
                fx = LeakFx.Create(part, leakLocalPos, leakLocalNormal, big);
            }
            fx.SetRate(RateAt(Planetarium.GetUniversalTime()) * 6000.0);
        }

        public void OnDestroy()
        {
            DestroyFx();
        }

        private void DestroyFx()
        {
            if (fx != null) Destroy(fx.gameObject);
            fx = null;
        }

        /// <summary>
        /// Stock's repair rule, minus the kits: the active vessel must be a kerbal on EVA with
        /// repair skill (an Engineer). Posts stock's own refusal message if not.
        /// </summary>
        private static bool EvaCanRepair()
        {
            Vessel eva = FlightGlobals.ActiveVessel;
            if (eva == null || !eva.isEVA) return false;
            if (eva.VesselValues.RepairSkill.value >= 1) return true;

            Game game = HighLogic.CurrentGame;
            bool experience = game != null &&
                game.Parameters.CustomParams<GameParameters.AdvancedParams>().KerbalExperienceEnabled(game.Mode);
            ScreenMessages.PostScreenMessage(experience
                ? Localizer.Format("#autoLOC_246904", 1.ToString())
                : Localizer.Format("#autoLOC_6006098"));
            return false;
        }

        private void UpdateRepairButtons()
        {
            bool anyBroken = false;
            foreach (ModuleDeployablePart dp in part.FindModulesImplementing<ModuleDeployablePart>())
            {
                if (dp.deployState != ModuleDeployablePart.DeployState.BROKEN) continue;
                anyBroken = true;
                BaseEvent stock = dp.Events[StockRepairEvent];
                if (stock != null && DoRepairMethod != null)
                {
                    stock.active = false;
                    stock.guiActiveUnfocused = false;
                }
            }
            // If reflection failed, leave stock's button alone rather than offer a dead one.
            Events["RepairDeployable"].active = anyBroken && DoRepairMethod != null;
        }

        [KSPEvent(guiName = "Repair", guiActive = false, guiActiveUnfocused = true,
            externalToEVAOnly = true, unfocusedRange = 4f, active = false)]
        public void RepairDeployable()
        {
            if (!EvaCanRepair()) return;
            foreach (ModuleDeployablePart dp in part.FindModulesImplementing<ModuleDeployablePart>())
            {
                if (dp.deployState != ModuleDeployablePart.DeployState.BROKEN) continue;
                DoRepairMethod.Invoke(dp, null);
                Log.Info("Repaired " + part.partInfo.title);
            }
            ScreenMessages.PostScreenMessage(part.partInfo.title + " repaired", 4f, ScreenMessageStyle.UPPER_CENTER);
        }

        [KSPEvent(guiName = "Patch leak", guiActive = false, guiActiveUnfocused = true,
            externalToEVAOnly = true, unfocusedRange = 4f, active = false)]
        public void PatchLeak()
        {
            if (!EvaCanRepair()) return;

            FixedUpdate(); // settle the drain up to now first
            if (!leaking) return;
            StopLeak();
            ScreenMessages.PostScreenMessage(part.partInfo.title + ": leak patched", 4f, ScreenMessageStyle.UPPER_CENTER);
            Log.Info("Leak patched on " + part.partInfo.title);
        }

        private void UpdateUI()
        {
            Events["PatchLeak"].active = leaking;
            Fields["leakStatus"].guiActive = leaking;
            if (leaking)
                leakStatus = string.Format("Leaking {0:F2}%/min", RateAt(Planetarium.GetUniversalTime()) * 6000.0);
        }

        public override string GetInfo()
        {
            return ""; // nothing to show in the editor
        }
    }
}
