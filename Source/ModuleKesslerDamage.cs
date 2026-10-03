using System;
using System.Collections.Generic;
using System.Reflection;
using CommNet;
using KSP.Localization;
using UnityEngine;

namespace KesslerSymptoms
{
    /// <summary>
    /// Debris damage state for one part, added by an MM patch to every part that can be damaged.
    /// Persists everything a hit can leave behind and enforces it while the part is loaded:
    ///   - tank puncture: self-sealing leak (tier 2/3),
    ///   - smashed fixed solar cells and antenna dropouts (tier 2 small/short, tier 3 big/long),
    ///   - tier 3: shorted battery, blown fuel cell, dead RCS block, cracked RTG.
    /// (Tier 2's partial battery short just drains charge; it leaves nothing to persist.)
    /// Also replaces stock's deployable Repair with a free one (same skill rule, no kits). Stock's
    /// own button can't be made free: it counts carried kits as -1 when you have none.
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
        private const string EC = "ElectricCharge";

        private static readonly FieldInfo RepairKitsField = typeof(ModuleDeployablePart)
            .GetField("repairKitsNecessary", BindingFlags.Instance | BindingFlags.NonPublic);
        /// <summary>Stock's repair itself (protected virtual, so subclasses' versions still run).</summary>
        private static readonly MethodInfo DoRepairMethod = typeof(ModuleDeployablePart)
            .GetMethod("DoRepair", BindingFlags.Instance | BindingFlags.NonPublic);
        private const string StockRepairEvent = "EventRepairExternal";

        // --- Leak ---
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

        // --- Electrical / output damage ---
        [KSPField(isPersistant = true)] public bool batteryShorted;
        /// <summary>ElectricCharge capacity before the short, restored on repair.</summary>
        [KSPField(isPersistant = true)] public double batteryOriginalMax;
        [KSPField(isPersistant = true)] public bool fuelCellBlown;
        [KSPField(isPersistant = true)] public bool rcsDead;
        [KSPField(isPersistant = true)] public int generatorHits;
        /// <summary>Old saves: fixed panel damage as a hit count. Converted to solarLoss on load.</summary>
        [KSPField(isPersistant = true)] public int solarHits;
        /// <summary>Fraction of fixed panel output lost (tier 2 and 3 take different-sized bites).</summary>
        [KSPField(isPersistant = true)] public double solarLoss;
        /// <summary>UT the antenna dropout ends; 0 = none.</summary>
        [KSPField(isPersistant = true)] public double signalLossUntil;

        [KSPField(guiActive = false, guiName = "Debris damage")]
        public string damageStatus = "";

        private LeakFx fx;
        /// <summary>This part's deployables and whether each was broken last frame, to catch new breaks.</summary>
        private List<ModuleDeployablePart> deployables;
        private bool[] wasBroken;
        // Undamaged output rates, captured on start (they come from the part config each load).
        private readonly List<ModuleResource> generatorOutputs = new List<ModuleResource>();
        private readonly List<double> generatorBaseRates = new List<double>();
        private readonly List<ModuleDeployableSolarPanel> fixedPanels = new List<ModuleDeployableSolarPanel>();
        private readonly List<float> panelBaseRates = new List<float>();

        public bool Leaking { get { return leaking; } }

        // ------------------------------------------------------------ what this part can suffer

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

        public bool CanShortBattery
        {
            get
            {
                PartResource r = part.Resources.Get(EC);
                return !batteryShorted && r != null && r.maxAmount > 0;
            }
        }

        public bool CanBlowFuelCell { get { return !fuelCellBlown && FuelCells().Count > 0; } }
        public bool CanKillRcs { get { return !rcsDead && part.FindModulesImplementing<ModuleRCS>().Count > 0; } }
        public bool CanHitGenerator { get { return generatorOutputs.Count > 0 && Settings.OutputFactor(generatorHits + 1) < Settings.OutputFactor(generatorHits); } }
        public bool CanHitSolar { get { return fixedPanels.Count > 0 && SolarFactor > Settings.OutputFloor; } }

        private double SolarFactor { get { return Math.Max(Settings.OutputFloor, 1.0 - solarLoss); } }

        /// <summary>A battery with any charge left, for tier 2's partial short.</summary>
        public bool HasCharge
        {
            get
            {
                PartResource r = part.Resources.Get(EC);
                return r != null && r.amount > 0;
            }
        }

        public bool CanLoseSignal
        {
            get { return signalLossUntil <= 0 && FixedAntennas().Count > 0 && CommNetScenario.CommNetEnabled; }
        }

        private static bool IsLeakable(PartResource r)
        {
            if (r.maxAmount <= 0 || r.info == null) return false;
            if (r.info.density <= 0 || r.info.resourceFlowMode == ResourceFlowMode.NO_FLOW) return false;
            return Array.IndexOf(NeverLeaks, r.resourceName) < 0;
        }

        /// <summary>Converters that make ElectricCharge: stock fuel cells.</summary>
        private List<ModuleResourceConverter> FuelCells()
        {
            List<ModuleResourceConverter> cells = new List<ModuleResourceConverter>();
            foreach (ModuleResourceConverter c in part.FindModulesImplementing<ModuleResourceConverter>())
            {
                if (c.outputList == null) continue;
                foreach (ResourceRatio r in c.outputList)
                    if (r.ResourceName == EC) { cells.Add(c); break; }
            }
            return cells;
        }

        /// <summary>Antennas that don't deploy (deployable ones break instead), incl. probe cores'.</summary>
        private List<ModuleDataTransmitter> FixedAntennas()
        {
            if (part.FindModuleImplementing<ModuleDeployableAntenna>() != null) return new List<ModuleDataTransmitter>();
            return part.FindModulesImplementing<ModuleDataTransmitter>();
        }

        // ------------------------------------------------------------ lifecycle

        public override void OnStartFinished(StartState state)
        {
            if (solarHits > 0)
            {
                solarLoss = solarHits * Settings.OutputLossPerHit;
                solarHits = 0;
            }

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

            // Already-broken parts (e.g. from the save) don't play a break sound on load.
            deployables = part.FindModulesImplementing<ModuleDeployablePart>();
            wasBroken = new bool[deployables.Count];
            for (int i = 0; i < deployables.Count; i++)
                wasBroken[i] = deployables[i].deployState == ModuleDeployablePart.DeployState.BROKEN;

            // RTGs: generators with no inputs that make ElectricCharge.
            foreach (ModuleGenerator g in part.FindModulesImplementing<ModuleGenerator>())
            {
                if (g.resHandler == null || g.resHandler.inputResources.Count > 0) continue;
                foreach (ModuleResource r in g.resHandler.outputResources)
                {
                    if (r.name != EC) continue;
                    generatorOutputs.Add(r);
                    generatorBaseRates.Add(r.rate);
                }
            }
            // Fixed solar panels: the non-breakable kind (stock's OX-STATs), which can't break off.
            foreach (ModuleDeployableSolarPanel p in part.FindModulesImplementing<ModuleDeployableSolarPanel>())
            {
                if (p.isBreakable) continue;
                fixedPanels.Add(p);
                panelBaseRates.Add(p.chargeRate);
            }
            ApplyOutputDamage();
            UpdateUI();
        }

        public void OnDestroy()
        {
            DestroyFx();
        }

        /// <summary>
        /// Show our Repair instead of stock's, keep tier 3 failures in force (stock modules would
        /// otherwise switch themselves back on), run antenna dropouts, and keep the leak's visuals
        /// in step with its rate. Runs after stock's modules (we're added later in the part).
        /// </summary>
        public void Update()
        {
            if (!HighLogic.LoadedSceneIsFlight)
            {
                if (fx != null) DestroyFx();
                return;
            }

            UpdateRepairButtons();
            CheckForNewBreaks();
            EnforceFailures();
            UpdateSignalLoss();

            if (!leaking)
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

        // ------------------------------------------------------------ leak

        /// <summary>Start a leak at a world-space point, starting between the given rates (%/min).</summary>
        public void Puncture(Vector3 worldPoint, Vector3 worldNormal, double minPctPerMin, double maxPctPerMin)
        {
            double now = Planetarium.GetUniversalTime();
            double lo = Math.Min(minPctPerMin, maxPctPerMin);
            double hi = Math.Max(minPctPerMin, maxPctPerMin);
            double pctPerMin = lo + UnityEngine.Random.value * (hi - lo);

            leaking = true;
            leakStartUT = now;
            leakAppliedUT = now;
            leakRate0 = pctPerMin / 100.0 / 60.0;
            leakTau = Settings.LeakSealHalfLifeMinutes * 60.0 / Math.Log(2.0);
            leakLocalPos = part.transform.InverseTransformPoint(worldPoint);
            leakLocalNormal = part.transform.InverseTransformDirection(worldNormal).normalized;
            DestroyFx(); // rebuild at the new hole
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

        private void DestroyFx()
        {
            if (fx != null) Destroy(fx.gameObject);
            fx = null;
        }

        // ------------------------------------------------------------ tier 3 failures

        /// <summary>Charge to zero and capacity down until repaired.</summary>
        public void ShortBattery()
        {
            PartResource r = part.Resources.Get(EC);
            if (r == null) return;
            batteryShorted = true;
            batteryOriginalMax = r.maxAmount;
            r.maxAmount = r.maxAmount * (1.0 - Settings.BatteryShortCapacityLoss);
            r.amount = 0;
            UpdateUI();
        }

        public void BlowFuelCell()
        {
            fuelCellBlown = true;
            EnforceFailures();
            UpdateUI();
        }

        public void KillRcs()
        {
            rcsDead = true;
            EnforceFailures();
            UpdateUI();
        }

        /// <summary>Tier 2 partial short: drain a fraction of the current charge; nothing to repair.</summary>
        public void DrainBattery(double fraction)
        {
            PartResource r = part.Resources.Get(EC);
            if (r != null) r.amount *= 1.0 - fraction;
        }

        /// <summary>Crack the RTG: output down another step. Returns the new output fraction.</summary>
        public double HitGenerator()
        {
            generatorHits++;
            ApplyOutputDamage();
            UpdateUI();
            return Settings.OutputFactor(generatorHits);
        }

        /// <summary>Smash cells on the fixed panel, taking <paramref name="loss"/> more output. Returns the new output fraction.</summary>
        public double HitSolar(double loss)
        {
            solarLoss = Math.Min(1.0 - Settings.OutputFloor, solarLoss + loss);
            ApplyOutputDamage();
            UpdateUI();
            return SolarFactor;
        }

        /// <summary>Drop this part's antennas out for the given game seconds.</summary>
        public void LoseSignal(double seconds)
        {
            signalLossUntil = Planetarium.GetUniversalTime() + seconds;
            UpdateSignalLoss();
            UpdateUI();
        }

        private void ApplyOutputDamage()
        {
            double g = Settings.OutputFactor(generatorHits);
            for (int i = 0; i < generatorOutputs.Count; i++)
                generatorOutputs[i].rate = generatorBaseRates[i] * g;
            float s = (float)SolarFactor;
            for (int i = 0; i < fixedPanels.Count; i++)
                fixedPanels[i].chargeRate = panelBaseRates[i] * s;
        }

        /// <summary>Stock modules can be switched back on by the player; hold them off while broken.</summary>
        private void EnforceFailures()
        {
            if (fuelCellBlown)
            {
                foreach (ModuleResourceConverter c in FuelCells())
                {
                    if (c.IsActivated) c.StopResourceConverter();
                    BaseEvent start = c.Events["StartResourceConverter"];
                    if (start != null) start.active = false;
                }
            }
            if (rcsDead)
            {
                foreach (ModuleRCS rcs in part.FindModulesImplementing<ModuleRCS>())
                {
                    rcs.rcsEnabled = false;
                    BaseField toggle = rcs.Fields["rcsEnabled"];
                    if (toggle != null) toggle.guiActive = toggle.guiActiveEditor = false;
                }
            }
        }

        /// <summary>
        /// Antennas switched off (CommNet skips modules that aren't enabled) until the dropout
        /// ends. Always switched back on once it's over, also after a reload mid-dropout.
        /// </summary>
        private void UpdateSignalLoss()
        {
            if (signalLossUntil <= 0) return;
            bool lost = Planetarium.GetUniversalTime() < signalLossUntil;
            foreach (ModuleDataTransmitter t in FixedAntennas())
                t.moduleIsEnabled = !lost;
            if (!lost)
            {
                signalLossUntil = 0;
                ScreenMessages.PostScreenMessage(part.partInfo.title + ": signal restored", 3f, ScreenMessageStyle.UPPER_CENTER);
                UpdateUI();
            }
        }

        // ------------------------------------------------------------ repair

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

        private bool AnyBrokenDeployable
        {
            get
            {
                if (deployables == null) return false;
                foreach (ModuleDeployablePart dp in deployables)
                    if (dp.deployState == ModuleDeployablePart.DeployState.BROKEN) return true;
                return false;
            }
        }

        /// <summary>Anything the Engineer's Repair button would fix.</summary>
        private bool NeedsRepair
        {
            get
            {
                return (AnyBrokenDeployable && DoRepairMethod != null) || batteryShorted || fuelCellBlown || rcsDead
                    || generatorHits > 0 || solarLoss > 0;
            }
        }

        /// <summary>Play the break sound for any deployable that broke since last frame, whatever broke it.</summary>
        private void CheckForNewBreaks()
        {
            if (deployables == null) return;
            for (int i = 0; i < deployables.Count; i++)
            {
                bool broken = deployables[i].deployState == ModuleDeployablePart.DeployState.BROKEN;
                if (broken && !wasBroken[i])
                    Sfx.PlayBreak(part, deployables[i] is ModuleDeployableSolarPanel);
                wasBroken[i] = broken;
            }
        }

        private void UpdateRepairButtons()
        {
            if (deployables != null && DoRepairMethod != null)
            {
                foreach (ModuleDeployablePart dp in deployables)
                {
                    if (dp.deployState != ModuleDeployablePart.DeployState.BROKEN) continue;
                    BaseEvent stock = dp.Events[StockRepairEvent];
                    if (stock == null) continue;
                    stock.active = false;
                    stock.guiActiveUnfocused = false;
                }
            }
            // If reflection failed, stock's button stays for broken deployables rather than a dead one.
            Events["RepairDamage"].active = NeedsRepair;
        }

        [KSPEvent(guiName = "Repair", guiActive = false, guiActiveUnfocused = true,
            externalToEVAOnly = true, unfocusedRange = 4f, active = false)]
        public void RepairDamage()
        {
            if (!EvaCanRepair()) return;

            if (DoRepairMethod != null && deployables != null)
                foreach (ModuleDeployablePart dp in deployables)
                    if (dp.deployState == ModuleDeployablePart.DeployState.BROKEN)
                        DoRepairMethod.Invoke(dp, null);

            if (batteryShorted)
            {
                PartResource r = part.Resources.Get(EC);
                if (r != null && batteryOriginalMax > 0) r.maxAmount = batteryOriginalMax;
                batteryShorted = false;
            }
            if (fuelCellBlown)
            {
                fuelCellBlown = false;
                foreach (ModuleResourceConverter c in FuelCells())
                {
                    BaseEvent start = c.Events["StartResourceConverter"];
                    if (start != null) start.active = true;
                }
            }
            if (rcsDead)
            {
                rcsDead = false;
                foreach (ModuleRCS rcs in part.FindModulesImplementing<ModuleRCS>())
                {
                    rcs.rcsEnabled = true;
                    BaseField toggle = rcs.Fields["rcsEnabled"];
                    if (toggle != null) toggle.guiActive = toggle.guiActiveEditor = true;
                }
            }
            generatorHits = 0;
            solarLoss = 0;
            ApplyOutputDamage();

            ScreenMessages.PostScreenMessage(part.partInfo.title + " repaired", 4f, ScreenMessageStyle.UPPER_CENTER);
            Log.Info("Repaired " + part.partInfo.title);
            UpdateUI();
        }

        [KSPEvent(guiName = "Patch leak", guiActive = false, guiActiveUnfocused = true,
            externalToEVAOnly = true, unfocusedRange = 4f, active = false)]
        public void PatchLeak()
        {
            // Any kerbal on EVA can slap a patch on a leak; no Engineer needed (unlike Repair).
            Vessel eva = FlightGlobals.ActiveVessel;
            if (eva == null || !eva.isEVA) return;

            FixedUpdate(); // settle the drain up to now first
            if (!leaking) return;
            StopLeak();
            ScreenMessages.PostScreenMessage(part.partInfo.title + ": leak patched", 4f, ScreenMessageStyle.UPPER_CENTER);
            Log.Info("Leak patched on " + part.partInfo.title);
        }

        // ------------------------------------------------------------ UI

        private void UpdateUI()
        {
            Events["PatchLeak"].active = leaking;

            List<string> bits = new List<string>();
            if (leaking) bits.Add(string.Format("leaking {0:F2}%/min", RateAt(Planetarium.GetUniversalTime()) * 6000.0));
            if (batteryShorted) bits.Add("battery shorted");
            if (fuelCellBlown) bits.Add("fuel cell blown");
            if (rcsDead) bits.Add("RCS dead");
            if (generatorHits > 0) bits.Add(string.Format("RTG at {0:P0}", Settings.OutputFactor(generatorHits)));
            if (solarLoss > 0) bits.Add(string.Format("cells at {0:P0}", SolarFactor));
            if (signalLossUntil > 0) bits.Add("no signal");

            damageStatus = string.Join(", ", bits.ToArray());
            Fields["damageStatus"].guiActive = bits.Count > 0;
        }

        public override string GetInfo()
        {
            return ""; // nothing to show in the editor
        }
    }
}
