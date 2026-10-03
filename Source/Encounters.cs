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
    /// Impact: sound + sparks + flash at a random spot on a random part, plus a push into the
    /// surface for tiers with an impulse set (tier 1 has none). Tiers 2 and 3 get real damage later.
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
            if (!Encounters.PickImpactPoint(vessel, out part, out point, out normal))
                return "no part to hit";

            ImpactFx.Spawn(part, point, normal, scale);
            Sfx.PlayAt(Sfx.Pick(sounds, Sfx.ImpactFallback), part.transform, point,
                (float)Settings.PingVolume, Random.Range(0.9f, 1.15f));

            string result = "impact on " + part.partInfo.title;
            double impulse = Settings.ImpulseFor(tier);
            if (impulse > 0 && Push(part, point, -normal, (float)impulse))
                result += string.Format(", {0:F2} t*m/s push", impulse);
            return result;
        }

        /// <summary>
        /// Instant push at the impact point. Parts without their own rigidbody (physicsless
        /// parts) pass it to the nearest parent that has one.
        /// </summary>
        private static bool Push(Part part, Vector3 point, Vector3 direction, float impulse)
        {
            Part p = part;
            while (p != null && p.rb == null) p = p.parent;
            if (p == null) return false;
            p.rb.AddForceAtPosition(direction.normalized * impulse, point, ForceMode.Impulse);
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
            // Tiers 2/3: heavier visuals plus a push; part damage comes later.
            { 2, new ImpactEffect(Sfx.HardImpacts, 1.6f) },
            { 3, new ImpactEffect(Sfx.HardImpacts, 2.2f) },
        };

        /// <summary>
        /// Random part, then a random point on its surface: cast a ray at one of its colliders
        /// from a random direction outside it. Falls back to the part's origin.
        /// </summary>
        public static bool PickImpactPoint(Vessel vessel, out Part part, out Vector3 point, out Vector3 normal)
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
        /// Run a tier's effect on the vessel, then raise the impact alert unless
        /// <paramref name="alert"/> is false (hits inside a debris field). Forced encounters
        /// (debug buttons) skip the effect toggles and the alert cooldown but still require a
        /// loaded, off-rails vessel in flight.
        /// </summary>
        public static bool Trigger(Vessel vessel, int tier, bool forced, bool alert = true)
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
            if (alert)
                Alert(string.Format("Debris impact! Tier {0}: {1}", tier, TierNames[tier]), forced);
            return true;
        }

        /// <summary>Sound-only micro pelt somewhere on the vessel: quieter and higher than a hit.</summary>
        public static void Pelt(Vessel vessel)
        {
            Part part;
            Vector3 point, normal;
            if (!PickImpactPoint(vessel, out part, out point, out normal)) return;
            Sfx.PlayAt(Sfx.Pick(Sfx.LightImpacts, Sfx.ImpactFallback), part.transform, point,
                (float)(Settings.PingVolume * Settings.PeltVolume) * Random.Range(0.6f, 1f),
                Random.Range(1.3f, 1.8f));
        }

        /// <summary>
        /// Alarm + orange text, rate-limited by AlertCooldownSeconds unless
        /// <paramref name="force"/> is set.
        /// </summary>
        public static void Alert(string text, bool force)
        {
            float now = Time.realtimeSinceStartup;
            if (!force && now - lastAlert < Settings.AlertCooldownSeconds) return;
            lastAlert = now;

            ScreenMessages.PostScreenMessage(text, 4f, ScreenMessageStyle.UPPER_CENTER, AlertColor);
            Sfx.Play2D(Sfx.Get(Sfx.Alarm, Sfx.AlarmFallback), (float)Settings.AlarmVolume);
        }
    }
}
