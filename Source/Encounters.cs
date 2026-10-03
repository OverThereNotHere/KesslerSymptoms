using System.Collections.Generic;
using UnityEngine;

namespace KesslerSymptoms
{
    /// <summary>What a tier does to a vessel when an encounter happens.</summary>
    public interface IEncounterEffect
    {
        /// <summary>
        /// Apply the encounter and return a short description of what happened. <paramref name="flow"/>
        /// is the world direction the debris is travelling (inside a field), or null for any direction.
        /// </summary>
        string Apply(Vessel vessel, int tier, Vector3? flow);
    }

    /// <summary>
    /// Impact: sound + sparks + flash at a random spot on a random part, plus a push into the
    /// surface for tiers with an impulse set (tier 1 has none), and a damage roll on that part
    /// for tier 2+ (see Damage).
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

        public string Apply(Vessel vessel, int tier, Vector3? flow)
        {
            Part part;
            Vector3 point, normal;
            Collider surface;
            if (!Encounters.PickImpactPoint(vessel, flow, out part, out point, out normal, out surface))
                return "no part to hit";

            ImpactFx.Spawn(part, point, normal, scale);
            if (tier >= 3) ImpactFx.SpawnFragments(part, point, normal);
            ImpactMark.Spawn(part, surface, point, normal, scale);
            Sfx.PlayAt(Sfx.Pick(sounds, Sfx.ImpactFallback), part.transform, point,
                (float)Settings.PingVolume, Random.Range(0.9f, 1.15f), Sfx.ImpactMuffleHz);

            string result = "impact on " + part.partInfo.title;
            double impulse = Settings.ImpulseFor(tier);
            if (impulse > 0 && !vessel.packed && Push(part, point, -normal, (float)impulse))
                result += string.Format(", {0:F2} t*m/s push", impulse);
            string damage = Damage.TryDamage(part, point, normal, tier);
            if (damage != null) result += ", " + damage;
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
    /// is allowed. Start encounters through EncounterScheduler.Request, which handles time warp.
    /// </summary>
    public static class Encounters
    {
        public static readonly string[] TierNames = { "Clear", "Sparse", "Dense", "Debris field" };

        private static readonly Color AlertColor = new Color(1f, 0.45f, 0.2f);
        private static float lastAlert = -1e6f;

        private static readonly Dictionary<int, IEncounterEffect> effects = new Dictionary<int, IEncounterEffect>
        {
            { 1, new ImpactEffect(Sfx.LightImpacts, 1f) },
            // Tiers 2/3: heavier visuals, a push, and damage rolls. Tier 3 uses tier 2's
            // damage chances until it gets its own.
            { 2, new ImpactEffect(Sfx.HardImpacts, 1.6f) },
            { 3, new ImpactEffect(Sfx.HardImpacts, 2.2f) },
        };

        /// <summary>
        /// Where a hit lands. With no <paramref name="flow"/>: a random part, random spot on it.
        /// With a flow direction: shoot rays through the vessel from upstream and take the first
        /// part each one meets, so the exposed side gets hit and parts behind others are shielded.
        /// <paramref name="surface"/> is the collider that was hit (null if none was), so effects
        /// can stick to the exact moving piece, e.g. a solar panel blade rather than its base.
        /// </summary>
        public static bool PickImpactPoint(Vessel vessel, Vector3? flow, out Part part, out Vector3 point,
            out Vector3 normal, out Collider surface)
        {
            part = null;
            surface = null;
            point = normal = Vector3.zero;
            if (vessel.parts.Count == 0) return false;

            if (flow.HasValue && RaycastFromUpstream(vessel, flow.Value, out part, out point, out normal, out surface))
                return true;

            part = vessel.parts[Random.Range(0, vessel.parts.Count)];
            surface = PickSurfacePoint(part, flow.HasValue ? -flow.Value : (Vector3?)null, out point, out normal);
            return true;
        }

        private static bool RaycastFromUpstream(Vessel vessel, Vector3 flow, out Part part, out Vector3 point,
            out Vector3 normal, out Collider surface)
        {
            part = null;
            surface = null;
            point = normal = Vector3.zero;

            Vector3 com = vessel.CoM;
            float radius = 2f;
            foreach (Part p in vessel.parts)
                radius = Mathf.Max(radius, (p.transform.position - com).magnitude + 2f);

            // Any two axes perpendicular to the flow, to scatter ray origins across the ship.
            Vector3 side = Vector3.Cross(flow, Mathf.Abs(flow.y) < 0.9f ? Vector3.up : Vector3.right).normalized;
            Vector3 up = Vector3.Cross(flow, side);

            for (int attempt = 0; attempt < 12; attempt++)
            {
                Vector2 disk = Random.insideUnitCircle * radius;
                // A little angular spread so it isn't a perfectly parallel stream.
                Vector3 dir = (flow + Random.insideUnitSphere * 0.15f).normalized;
                Vector3 origin = com - dir * radius * 2f + side * disk.x + up * disk.y;

                RaycastHit[] hits = Physics.RaycastAll(origin, dir, radius * 4f, ~0, QueryTriggerInteraction.Ignore);
                RaycastHit? nearest = null;
                foreach (RaycastHit h in hits)
                    if (!nearest.HasValue || h.distance < nearest.Value.distance) nearest = h;
                if (!nearest.HasValue) continue; // missed the ship

                Part hitPart = nearest.Value.collider.GetComponentInParent<Part>();
                if (hitPart == null || hitPart.vessel != vessel) continue; // something else took it
                part = hitPart;
                point = nearest.Value.point;
                normal = nearest.Value.normal;
                surface = nearest.Value.collider;
                return true;
            }
            return false;
        }

        /// <summary>
        /// Random point on a part's surface: cast a ray at one of its colliders from outside it,
        /// from a random direction or roughly <paramref name="from"/> if given. Falls back to the
        /// part's origin. Returns the collider that was hit, or null.
        /// </summary>
        public static Collider PickSurfacePoint(Part part, Vector3? from, out Vector3 point, out Vector3 normal)
        {
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
                    Vector3 dir = from.HasValue
                        ? (from.Value.normalized + Random.insideUnitSphere * 0.6f).normalized
                        : Random.onUnitSphere;
                    RaycastHit hit;
                    if (c.Raycast(new Ray(b.center + dir * reach, -dir), out hit, reach * 2f))
                    {
                        point = hit.point;
                        normal = hit.normal;
                        return c;
                    }
                }
            }

            point = part.transform.position;
            normal = Random.onUnitSphere;
            return null;
        }

        public static void Register(int tier, IEncounterEffect effect)
        {
            effects[tier] = effect;
        }

        /// <summary>True while rails (on-rails, non-physics) time warp is running.</summary>
        public static bool InRailsWarp
        {
            get { return TimeWarp.WarpMode == TimeWarp.Modes.HIGH && TimeWarp.CurrentRateIndex > 0; }
        }

        /// <summary>
        /// Why an encounter can't happen to this vessel right now, or null if it can. A packed
        /// (on-rails) vessel is allowed only during rails warp with RailsWarpEncounters on;
        /// callers must drop out of warp before anything that needs physics (tier 2/3).
        /// </summary>
        public static string Blocker(Vessel vessel)
        {
            if (HighLogic.LoadedScene != GameScenes.FLIGHT) return "only in flight";
            if (vessel == null) return "no active vessel";
            if (!vessel.loaded) return "vessel is unloaded";
            if (vessel.packed)
            {
                if (!InRailsWarp) return "vessel is on rails";
                if (!Settings.RailsWarpEncounters) return "time warp encounters are off";
            }
            return null;
        }

        /// <summary>
        /// Run a tier's effect on the vessel, then raise the impact alert unless
        /// <paramref name="alert"/> is false (hits inside a debris field). Forced encounters
        /// (debug buttons) skip the effect toggles and the alert cooldown but still require a
        /// loaded, off-rails vessel in flight.
        /// </summary>
        public static bool Trigger(Vessel vessel, int tier, bool forced, bool alert = true, Vector3? flow = null)
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

            string result = effect.Apply(vessel, tier, flow);
            Log.Info(string.Format("Tier {0} encounter on {1}{2}: {3}",
                tier, vessel.vesselName, forced ? " (forced)" : "", result));
            if (alert)
                Alert(string.Format("Debris impact! Tier {0}: {1}", tier, TierNames[tier]), forced);
            return true;
        }

        /// <summary>Sound-only micro pelt somewhere on the vessel: quieter and higher than a hit.</summary>
        public static void Pelt(Vessel vessel, Vector3? flow = null)
        {
            Part part;
            Vector3 point, normal;
            Collider surface;
            if (!PickImpactPoint(vessel, flow, out part, out point, out normal, out surface)) return;
            Sfx.PlayAt(Sfx.Pick(Sfx.LightImpacts, Sfx.ImpactFallback), part.transform, point,
                (float)(Settings.PingVolume * Settings.PeltVolume) * Random.Range(0.6f, 1f),
                Random.Range(1.3f, 1.8f), Sfx.PeltMuffleHz);
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
            PlayAlarm();
        }

        public static void PlayAlarm()
        {
            Sfx.Play2D(Sfx.Get(Sfx.Alarm, Sfx.AlarmFallback), (float)Settings.AlarmVolume);
        }

        /// <summary>The damage alarm: something on the ship just broke or started leaking.</summary>
        public static void PlayPartAlarm()
        {
            Sfx.Play2D(Sfx.Get(Sfx.PartAlarm, Sfx.Alarm), (float)Settings.AlarmVolume);
        }
    }
}
