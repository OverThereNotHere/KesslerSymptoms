using System.Collections.Generic;
using UnityEngine;

namespace KesslerSymptoms
{
    /// <summary>What a tier does to a vessel when an encounter happens.</summary>
    public interface IEncounterEffect
    {
        /// <summary>Apply the encounter and return a short description of what happened.</summary>
        string Apply(Vessel vessel, int tier);
    }

    /// <summary>
    /// Harmless impact: sound + sparks + flash at a random spot on a random part.
    /// This is all of tier 1; tiers 2 and 3 use a heavier version until they get real damage.
    /// </summary>
    public class ImpactEffect : IEncounterEffect
    {
        private readonly string[] sounds;
        private readonly float scale;

        public ImpactEffect(string[] sounds, float scale)
        {
            this.sounds = sounds;
            this.scale = scale;
        }

        public string Apply(Vessel vessel, int tier)
        {
            Part part;
            Vector3 point, normal;
            if (!PickImpactPoint(vessel, out part, out point, out normal))
                return "no part to hit";

            ImpactFx.Spawn(part, point, normal, scale);
            Sfx.PlayAt(Sfx.Pick(sounds, Sfx.ImpactFallback), part.transform, point,
                (float)Settings.PingVolume, Random.Range(0.9f, 1.15f));
            return "impact on " + part.partInfo.title;
        }

        /// <summary>
        /// Random part, then a random point on its surface: cast a ray at one of its colliders
        /// from a random direction outside it. Falls back to the part's origin.
        /// </summary>
        private static bool PickImpactPoint(Vessel vessel, out Part part, out Vector3 point, out Vector3 normal)
        {
            part = null;
            point = normal = Vector3.zero;
            if (vessel.parts.Count == 0) return false;

            part = vessel.parts[Random.Range(0, vessel.parts.Count)];
            List<Collider> colliders = new List<Collider>();
            foreach (Collider c in part.GetComponentsInChildren<Collider>())
                if (c.enabled && !c.isTrigger) colliders.Add(c);

            if (colliders.Count > 0)
            {
                Collider c = colliders[Random.Range(0, colliders.Count)];
                Bounds b = c.bounds;
                float reach = b.extents.magnitude + 1f;
                for (int attempt = 0; attempt < 4; attempt++)
                {
                    Vector3 dir = Random.onUnitSphere;
                    RaycastHit hit;
                    if (c.Raycast(new Ray(b.center + dir * reach, -dir), out hit, reach * 2f))
                    {
                        point = hit.point;
                        normal = hit.normal;
                        return true;
                    }
                }
            }

            point = part.transform.position;
            normal = Random.onUnitSphere;
            return true;
        }
    }

    /// <summary>
    /// Entry point for encounters. Holds one effect per tier and decides whether an encounter
    /// is allowed. The EncounterScheduler and the Effects tab's force buttons both call Trigger.
    /// </summary>
    public static class Encounters
    {
        public static readonly string[] TierNames = { "Clear", "Sparse", "Dense", "Debris field" };

        private static readonly Color AlertColor = new Color(1f, 0.45f, 0.2f);
        private static float lastAlert = -1e6f;

        private static readonly Dictionary<int, IEncounterEffect> effects = new Dictionary<int, IEncounterEffect>
        {
            { 1, new ImpactEffect(Sfx.LightImpacts, 1f) },
            // Placeholders: cosmetic only until tiers 2 and 3 get real damage.
            { 2, new ImpactEffect(Sfx.HardImpacts, 1.6f) },
            { 3, new ImpactEffect(Sfx.HardImpacts, 2.2f) },
        };

        public static void Register(int tier, IEncounterEffect effect)
        {
            effects[tier] = effect;
        }

        /// <summary>Why an encounter can't happen to this vessel right now, or null if it can.</summary>
        public static string Blocker(Vessel vessel)
        {
            if (HighLogic.LoadedScene != GameScenes.FLIGHT) return "only in flight";
            if (vessel == null) return "no active vessel";
            if (!vessel.loaded || vessel.packed) return "vessel is on rails";
            return null;
        }

        /// <summary>
        /// Run a tier's effect on the vessel, then raise the impact alert. Forced encounters
        /// (debug buttons) skip the effect toggles and the alert cooldown but still require a
        /// loaded, off-rails vessel in flight.
        /// </summary>
        public static bool Trigger(Vessel vessel, int tier, bool forced)
        {
            string blocker = Blocker(vessel);
            if (blocker != null)
            {
                Log.Info(string.Format("Tier {0} encounter skipped: {1}", tier, blocker));
                return false;
            }
            if (!forced && !Settings.TierEnabled(tier)) return false;

            IEncounterEffect effect;
            if (!effects.TryGetValue(tier, out effect)) return false;

            string result = effect.Apply(vessel, tier);
            Log.Info(string.Format("Tier {0} encounter on {1}{2}: {3}",
                tier, vessel.vesselName, forced ? " (forced)" : "", result));
            Alert(tier, forced);
            return true;
        }

        /// <summary>Alarm + text for an impact, rate-limited by AlertCooldownSeconds.</summary>
        private static void Alert(int tier, bool forced)
        {
            float now = Time.realtimeSinceStartup;
            if (!forced && now - lastAlert < Settings.AlertCooldownSeconds) return;
            lastAlert = now;

            ScreenMessages.PostScreenMessage(
                string.Format("Debris impact! Tier {0}: {1}", tier, TierNames[tier]),
                4f, ScreenMessageStyle.UPPER_CENTER, AlertColor);
            Sfx.Play2D(Sfx.Get(Sfx.Alarm, Sfx.AlarmFallback), (float)Settings.AlarmVolume);
        }
    }
}
