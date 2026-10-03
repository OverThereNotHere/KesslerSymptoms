using System.Collections.Generic;
using UnityEngine;

namespace KesslerSymptoms
{
    /// <summary>
    /// Part damage from tier 2+ hits. Damage lands on the part that was hit: deployables
    /// (panels, antennas, radiators) break via stock breakage, extended or not; tanks can be
    /// punctured into a self-sealing leak. Every damaging hit is announced loudly.
    /// </summary>
    public static class Damage
    {
        private static readonly Color DamageColor = new Color(1f, 0.3f, 0.2f);

        /// <summary>
        /// Roll damage for a hit on <paramref name="part"/>. Returns a short description for the
        /// log, or null if nothing broke. Needs the vessel in physics (stock won't break packed parts).
        /// </summary>
        public static string TryDamage(Part part, Vector3 point, Vector3 normal, int tier)
        {
            if (tier < 2 || part.packed) return null;

            ModuleDeployablePart dp = part.FindModuleImplementing<ModuleDeployablePart>();
            if (dp != null && dp.deployState != ModuleDeployablePart.DeployState.BROKEN &&
                Random.value < Settings.Tier2BreakChance)
            {
                if (Break(dp)) return "broke " + part.partInfo.title;
            }

            ModuleKesslerDamage dmg = part.FindModuleImplementing<ModuleKesslerDamage>();
            if (dmg != null && !dmg.Leaking && dmg.CanPuncture && Random.value < Settings.Tier2PunctureChance)
            {
                Puncture(dmg, point, normal);
                return "punctured " + part.partInfo.title;
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
            return true;
        }

        private static void Puncture(ModuleKesslerDamage dmg, Vector3 point, Vector3 normal)
        {
            dmg.Puncture(point, normal);
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
            Encounters.PickSurfacePoint(pick.part, out point, out normal);
            Puncture(pick, point, normal);
            return "punctured " + pick.part.partInfo.title;
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
