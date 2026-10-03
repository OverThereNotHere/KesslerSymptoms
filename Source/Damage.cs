using System.Collections.Generic;
using UnityEngine;

namespace KesslerSymptoms
{
    /// <summary>Tier 3 failure kinds, also used by the Effects tab's force buttons.</summary>
    public enum Failure { Break, Puncture, BatteryShort, FuelCell, Rcs, Engine, Generator, FixedSolar, Sas, SignalLoss }

    /// <summary>
    /// Part damage from tier 2+ hits. Damage lands on the part that was hit: its possible
    /// failures are rolled in random order and the first that succeeds happens, so at most one
    /// failure per hit. Tier 2 has a gentler menu (partial shorts, small cell losses, signal
    /// blips, rare punctures); tier 3 the full one. Every failure is announced loudly.
    /// </summary>
    public static class Damage
    {
        private static readonly Color DamageColor = new Color(1f, 0.3f, 0.2f);

        /// <summary>
        /// Roll damage for a hit on <paramref name="part"/>. Returns a short description for the
        /// log, or null if nothing broke. Panels only break in physics (stock refuses on packed
        /// parts, e.g. mid-warp with warp drop-out off); punctures can happen either way.
        /// </summary>
        public static string TryDamage(Part part, Vector3 point, Vector3 normal, int tier)
        {
            if (tier < 2) return null;
            tier = Mathf.Min(tier, 3);
            List<Failure> options = Possible(part, tier);
            // Shuffle, then the first failure whose chance comes up is the one that happens.
            for (int i = options.Count - 1; i > 0; i--)
            {
                int j = Random.Range(0, i + 1);
                Failure t = options[i]; options[i] = options[j]; options[j] = t;
            }
            foreach (Failure f in options)
                if (Random.value < ChanceOf(f, tier))
                    return Apply(f, part, point, normal, tier);
            return null;
        }

        private static bool CanBreak(ModuleDeployablePart dp)
        {
            return dp != null && dp.isBreakable && !dp.part.packed && dp.deployState != ModuleDeployablePart.DeployState.BROKEN;
        }

        private static List<ModuleEngines> RunningEngines(Part part)
        {
            List<ModuleEngines> running = new List<ModuleEngines>();
            foreach (ModuleEngines e in part.FindModulesImplementing<ModuleEngines>())
                if (e.EngineIgnited && !e.flameout) running.Add(e);
            return running;
        }

        /// <summary>Failures this part can suffer right now at this tier.</summary>
        private static List<Failure> Possible(Part part, int tier)
        {
            List<Failure> list = new List<Failure>();
            ModuleKesslerDamage dmg = part.FindModuleImplementing<ModuleKesslerDamage>();
            if (CanBreak(part.FindModuleImplementing<ModuleDeployablePart>())) list.Add(Failure.Break);
            if (dmg != null)
            {
                if (!dmg.Leaking && dmg.CanPuncture) list.Add(Failure.Puncture);
                if (dmg.CanHitSolar) list.Add(Failure.FixedSolar);
                if (dmg.CanLoseSignal) list.Add(Failure.SignalLoss);
                if (tier >= 3 ? dmg.CanShortBattery : dmg.HasCharge) list.Add(Failure.BatteryShort);
            }
            if (tier < 3) return list;

            // Tier 3 only.
            if (RunningEngines(part).Count > 0) list.Add(Failure.Engine);
            if (part.FindModuleImplementing<ModuleCommand>() != null && part.vessel != null &&
                part.vessel.ActionGroups[KSPActionGroup.SAS]) list.Add(Failure.Sas);
            if (dmg != null)
            {
                if (dmg.CanBlowFuelCell) list.Add(Failure.FuelCell);
                if (dmg.CanKillRcs) list.Add(Failure.Rcs);
                if (dmg.CanHitGenerator) list.Add(Failure.Generator);
            }
            return list;
        }

        private static double ChanceOf(Failure f, int tier)
        {
            if (tier < 3)
            {
                switch (f)
                {
                    case Failure.Break: return Settings.Tier2BreakChance;
                    case Failure.Puncture: return Settings.Tier2PunctureChance;
                    case Failure.BatteryShort: return Settings.Tier2ShortChance;
                    case Failure.FixedSolar: return Settings.Tier2SolarChance;
                    case Failure.SignalLoss: return Settings.Tier2SignalLossChance;
                    default: return 0;
                }
            }
            switch (f)
            {
                case Failure.Break: return Settings.Tier3BreakChance;
                case Failure.Puncture: return Settings.Tier3PunctureChance;
                case Failure.BatteryShort: return Settings.BatteryShortChance;
                case Failure.FuelCell: return Settings.FuelCellChance;
                case Failure.Rcs: return Settings.RcsChance;
                case Failure.Engine: return Settings.EngineChance;
                case Failure.Generator: return Settings.GeneratorChance;
                case Failure.FixedSolar: return Settings.FixedSolarChance;
                case Failure.Sas: return Settings.SasChance;
                case Failure.SignalLoss: return Settings.SignalLossChance;
                default: return 0;
            }
        }

        /// <summary>Make the failure happen and announce it. Returns a description for the log.</summary>
        private static string Apply(Failure f, Part part, Vector3 point, Vector3 normal, int tier)
        {
            bool heavy = tier >= 3;
            string title = part.partInfo.title;
            ModuleKesslerDamage dmg = part.FindModuleImplementing<ModuleKesslerDamage>();
            switch (f)
            {
                case Failure.Break:
                    return Break(part.FindModuleImplementing<ModuleDeployablePart>()) ? "broke " + title : null;

                case Failure.Puncture:
                    if (heavy) Puncture(dmg, point, normal, Settings.Tier3LeakRateMinPctPerMin, Settings.Tier3LeakRateMaxPctPerMin);
                    else Puncture(dmg, point, normal, Settings.LeakRateMinPctPerMin, Settings.LeakRateMaxPctPerMin);
                    return (heavy ? "big puncture in " : "punctured ") + title;

                case Failure.BatteryShort:
                    // Lighter muffle than impacts: a zap is mostly high crackle, which the hull cutoff would erase.
                    Sfx.PlayAtPart(part, Sfx.Pick(Sfx.Zaps, Sfx.ZapFallback), heavy ? 1f : 0.85f, 8000f);
                    if (heavy)
                    {
                        dmg.ShortBattery();
                        Announce(part, string.Format("Debris shorted the {0}'s battery!", title));
                        return "shorted " + title;
                    }
                    double drained = Settings.Tier2ShortMinLoss + Random.value * (Settings.Tier2ShortMaxLoss - Settings.Tier2ShortMinLoss);
                    dmg.DrainBattery(drained);
                    Announce(part, string.Format("Debris partly shorted the {0}: lost {1:P0} of its charge", title, drained));
                    return "partial short on " + title;

                case Failure.FuelCell:
                    dmg.BlowFuelCell();
                    ImpactFx.Spawn(part, part.transform.position, (point - part.transform.position).normalized, 1.6f);
                    Sfx.PlayAtPart(part, Sfx.Pick(Sfx.Pops, Sfx.Snaps), 0.9f, Sfx.ImpactMuffleHz);
                    Announce(part, string.Format("The {0}'s fuel cell blew out!", title));
                    return "blew fuel cell on " + title;

                case Failure.Rcs:
                    dmg.KillRcs();
                    Announce(part, string.Format("Debris knocked out the {0}!", title));
                    return "killed RCS " + title;

                case Failure.Engine:
                    foreach (ModuleEngines e in RunningEngines(part)) e.Shutdown();
                    Announce(part, string.Format("Debris knocked the {0} out: engine shut down", title));
                    return "shut down " + title;

                case Failure.Generator:
                    double g = dmg.HitGenerator();
                    // Unmuffled: it's the cabin's dosimeter going off, not a sound through the hull.
                    Sfx.PlayAtPart(part, Sfx.Get(Sfx.Geiger, null), 0.8f, 0f);
                    Announce(part, string.Format("Debris cracked the {0}: output at {1:P0}", title, g));
                    return "cracked RTG " + title;

                case Failure.FixedSolar:
                    double s = dmg.HitSolar(heavy ? Settings.OutputLossPerHit : Settings.Tier2SolarLossPerHit);
                    Sfx.PlayBreak(part, true);
                    Announce(part, string.Format("Debris smashed cells on the {0}: output at {1:P0}", title, s));
                    return "smashed cells on " + title;

                case Failure.Sas:
                    part.vessel.ActionGroups.SetGroup(KSPActionGroup.SAS, false);
                    Announce(part, string.Format("Impact on the {0} knocked SAS offline", title));
                    return "SAS off from " + title;

                case Failure.SignalLoss:
                    double lo = heavy ? Settings.SignalLossMinSeconds : Settings.Tier2SignalLossMinSeconds;
                    double hi = heavy ? Settings.SignalLossMaxSeconds : Settings.Tier2SignalLossMaxSeconds;
                    double secs = lo + Random.value * (hi - lo);
                    dmg.LoseSignal(secs);
                    Sfx.PlayFor(part, Sfx.Pick(Sfx.Statics, null), (float)secs, 0.6f, 0f);
                    Announce(part, string.Format("Impact on the {0}: signal lost ({1:F1} s)", title, secs));
                    return "signal loss on " + title;
            }
            return null;
        }

        private static bool Break(ModuleDeployablePart dp)
        {
            dp.breakPanels();
            if (dp.deployState != ModuleDeployablePart.DeployState.BROKEN)
            {
                Log.Info("breakPanels had no effect on " + dp.part.partInfo.title);
                return false;
            }
            Announce(dp.part, string.Format("Debris broke the {0}!", dp.part.partInfo.title));
            AddBreakSpike(dp.part);
            return true;
        }

        /// <summary>A broken deployable sheds fragments: a smaller version of an explosion spike.</summary>
        private static void AddBreakSpike(Part part)
        {
            Vessel v = part.vessel;
            KesslerScenario scn = KesslerScenario.Instance;
            if (scn == null || v == null || v.situation != Vessel.Situations.ORBITING || Settings.BreakSpike <= 0) return;

            CelestialBody body = v.mainBody;
            scn.AddSpike(body, (part.transform.position - body.position).magnitude, Settings.BreakSpike);
        }

        private static void Puncture(ModuleKesslerDamage dmg, Vector3 point, Vector3 normal, double minPct, double maxPct)
        {
            dmg.Puncture(point, normal, minPct, maxPct);
            Announce(dmg.part, string.Format("Debris punctured the {0}: it's leaking!", dmg.part.partInfo.title));
        }

        /// <summary>Message + red glow + damage alarm (ignoring the cooldown).</summary>
        private static void Announce(Part part, string text)
        {
            ScreenMessages.PostScreenMessage(text, 6f, ScreenMessageStyle.UPPER_CENTER, DamageColor);
            Encounters.PlayPartAlarm();
            DamageGlow.Flash(part);
            Log.Info(text);
        }

        // ------------------------------------------------------------ debug helpers

        /// <summary>Break a random intact deployable on the vessel. Returns what happened.</summary>
        public static string ForceBreak(Vessel v)
        {
            List<ModuleDeployablePart> candidates = new List<ModuleDeployablePart>();
            foreach (Part p in v.parts)
            {
                ModuleDeployablePart dp = p.FindModuleImplementing<ModuleDeployablePart>();
                if (dp != null && dp.deployState != ModuleDeployablePart.DeployState.BROKEN) candidates.Add(dp);
            }
            if (candidates.Count == 0) return "no intact panels, antennas or radiators";
            ModuleDeployablePart pick = candidates[Random.Range(0, candidates.Count)];
            return Break(pick) ? "broke " + pick.part.partInfo.title : "stock breakage refused " + pick.part.partInfo.title;
        }

        /// <summary>Puncture a random intact tank on the vessel. Returns what happened.</summary>
        public static string ForcePuncture(Vessel v)
        {
            List<ModuleKesslerDamage> candidates = new List<ModuleKesslerDamage>();
            foreach (Part p in v.parts)
            {
                ModuleKesslerDamage dmg = p.FindModuleImplementing<ModuleKesslerDamage>();
                if (dmg != null && !dmg.Leaking && dmg.CanPuncture) candidates.Add(dmg);
            }
            if (candidates.Count == 0) return "no intact tanks with leakable contents";
            ModuleKesslerDamage pick = candidates[Random.Range(0, candidates.Count)];
            Vector3 point, normal;
            Encounters.PickSurfacePoint(pick.part, null, out point, out normal);
            Puncture(pick, point, normal, Settings.LeakRateMinPctPerMin, Settings.LeakRateMaxPctPerMin);
            return "punctured " + pick.part.partInfo.title;
        }

        /// <summary>Make a tier 3 failure happen on a random part that can suffer it.</summary>
        public static string ForceFailure(Vessel v, Failure f)
        {
            List<Part> candidates = new List<Part>();
            foreach (Part p in v.parts)
                if (Possible(p, 3).Contains(f)) candidates.Add(p);
            if (candidates.Count == 0) return "no part on this vessel can take that (" + f + ")";
            Part pick = candidates[Random.Range(0, candidates.Count)];
            Vector3 point, normal;
            Encounters.PickSurfacePoint(pick, null, out point, out normal);
            return Apply(f, pick, point, normal, 3) ?? "nothing happened";
        }
    }

    /// <summary>Pulses a part red for a few seconds, then restores its normal highlighting.</summary>
    public class DamageGlow : MonoBehaviour
    {
        private const float Duration = 4f;
        private Part part;
        private float age;

        public static void Flash(Part p)
        {
            // Explicit null check: ?? doesn't respect Unity's destroyed-object null.
            DamageGlow glow = p.gameObject.GetComponent<DamageGlow>();
            if (glow == null) glow = p.gameObject.AddComponent<DamageGlow>();
            glow.part = p;
            glow.age = 0f;
        }

        public void Update()
        {
            if (part == null)
            {
                Destroy(this);
                return;
            }
            age += Time.unscaledDeltaTime;
            if (age >= Duration)
            {
                part.SetHighlightDefault();
                Destroy(this);
                return;
            }
            float pulse = 0.5f + 0.5f * Mathf.Sin(age * Mathf.PI * 3f);
            part.SetHighlightType(Part.HighlightType.AlwaysOn);
            part.SetHighlightColor(Color.Lerp(new Color(0.5f, 0f, 0f), Color.red, pulse));
            part.SetHighlight(true, false);
        }
    }
}
