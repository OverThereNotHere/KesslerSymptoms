using UnityEngine;

namespace KesslerSymptoms
{
    /// <summary>
    /// Visible debris field: specks and faint streaks drifting past the vessel along the field's
    /// flow direction, over a faint dusty haze, both denser at higher tiers. Fades in when the
    /// field starts and out when it ends. Simulated in the vessel's local space so it doesn't
    /// smear in KSP's moving reference frame.
    /// </summary>
    public class FieldFx : MonoBehaviour
    {
        private const float FadeSeconds = 1.5f;

        // Index by tier (0 unused): particles per second, brightness, streak length.
        private static readonly float[] Rate = { 0f, 12f, 45f, 140f };
        private static readonly float[] Brightness = { 0f, 0.35f, 0.6f, 0.9f };
        private static readonly float[] Streak = { 0f, 0.02f, 0.035f, 0.05f };
        // Haze: puffs per second and peak opacity.
        private static readonly float[] HazeRate = { 0f, 1.5f, 3f, 6f };
        private static readonly float[] HazeAlpha = { 0f, 0.05f, 0.09f, 0.14f };

        private ParticleSystem ps;
        private ParticleSystem haze;
        private int tier;
        private float fade;
        private bool ending;

        /// <param name="flow">World direction the debris travels.</param>
        public static FieldFx Create(Vessel v, int tier, Vector3 flow)
        {
            if (!Settings.FieldVisualsEnabled || tier < 1 || tier > 3) return null;

            // Size the cloud to the ship: farthest part from the centre of mass, plus margin.
            Vector3 com = v.CoM;
            float radius = 8f;
            foreach (Part p in v.parts)
                radius = Mathf.Max(radius, (p.transform.position - com).magnitude + 6f);
            radius = Mathf.Min(radius, 60f);

            GameObject go = new GameObject("KesslerSymptoms_FieldFx");
            go.transform.SetParent(v.transform, true);
            // Emitter sits upstream and faces downstream; particles cross the ship and die past it.
            go.transform.position = com - flow * radius;
            go.transform.rotation = Quaternion.LookRotation(flow);

            // Haze fills the space around the ship rather than streaming in from upstream.
            GameObject hazeGo = new GameObject("KesslerSymptoms_FieldHaze");
            hazeGo.transform.SetParent(go.transform, true);
            hazeGo.transform.position = com;
            hazeGo.transform.rotation = go.transform.rotation;

            FieldFx fx = go.AddComponent<FieldFx>();
            fx.tier = tier;
            fx.ps = Build(go, tier, radius);
            fx.haze = BuildHaze(hazeGo, tier, radius);
            return fx;
        }

        /// <summary>Stop emitting and remove once the last particles are gone.</summary>
        public void End()
        {
            ending = true;
        }

        public void Update()
        {
            fade = Mathf.MoveTowards(fade, ending ? 0f : 1f, Time.deltaTime / FadeSeconds);
            ParticleSystem.EmissionModule emission = ps.emission;
            emission.rateOverTime = Rate[tier] * fade;
            ParticleSystem.EmissionModule hazeEmission = haze.emission;
            hazeEmission.rateOverTime = HazeRate[tier] * fade;
            if (ending && fade <= 0f && ps.particleCount == 0 && haze.particleCount == 0)
                Destroy(gameObject);
        }

        /// <summary>Big, very faint dusty puffs that drift slowly downstream and fade in and out.</summary>
        private static ParticleSystem BuildHaze(GameObject go, int tier, float radius)
        {
            ParticleSystem ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            ParticleSystem.MainModule main = ps.main;
            main.loop = true;
            main.playOnAwake = false;
            main.duration = 1f;
            main.startSpeed = new ParticleSystem.MinMaxCurve(3f, 8f);
            main.startLifetime = new ParticleSystem.MinMaxCurve(5f, 9f);
            main.startSize = new ParticleSystem.MinMaxCurve(radius * 0.3f, radius * 0.7f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            float a = HazeAlpha[tier];
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.75f, 0.72f, 0.68f, a), new Color(0.6f, 0.6f, 0.62f, a * 0.6f));
            main.gravityModifier = 0f;
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.maxParticles = 120;

            // The whole volume around the ship, drifting along +Z (downstream).
            ParticleSystem.ShapeModule shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(radius * 2f, radius * 2f, radius * 2f);

            ParticleSystem.ColorOverLifetimeModule col = ps.colorOverLifetime;
            col.enabled = true;
            Gradient g = new Gradient();
            g.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.3f), new GradientAlphaKey(1f, 0.7f), new GradientAlphaKey(0f, 1f) });
            col.color = new ParticleSystem.MinMaxGradient(g);

            ParticleSystem.RotationOverLifetimeModule spin = ps.rotationOverLifetime;
            spin.enabled = true;
            spin.z = new ParticleSystem.MinMaxCurve(-0.2f, 0.2f);

            ParticleSystemRenderer r = go.GetComponent<ParticleSystemRenderer>();
            r.material = LeakFx.VapourMaterial;
            r.renderMode = ParticleSystemRenderMode.Billboard;

            ParticleSystem.EmissionModule emission = ps.emission;
            emission.rateOverTime = 0f;
            ps.Play();
            return ps;
        }

        private static ParticleSystem Build(GameObject go, int tier, float radius)
        {
            ParticleSystem ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            float speedMin = 25f, speedMax = 70f;
            ParticleSystem.MainModule main = ps.main;
            main.loop = true;
            main.playOnAwake = false;
            main.duration = 1f;
            main.startSpeed = new ParticleSystem.MinMaxCurve(speedMin, speedMax);
            // Long enough to cross the whole cloud at the slowest speed.
            main.startLifetime = 2f * radius / speedMin;
            main.startSize = new ParticleSystem.MinMaxCurve(0.02f, 0.05f + 0.02f * tier);
            float b = Brightness[tier];
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(b, b, b * 0.95f, 1f), new Color(b * 0.6f, b * 0.6f, b * 0.6f, 1f));
            main.gravityModifier = 0f;
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.maxParticles = 1500;

            // A slab as wide as the ship, emitting along +Z (downstream).
            ParticleSystem.ShapeModule shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(radius * 2f, radius * 2f, 1f);

            // Flicker a little so specks glint rather than glow steadily.
            ParticleSystem.ColorOverLifetimeModule col = ps.colorOverLifetime;
            col.enabled = true;
            Gradient g = new Gradient();
            g.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.1f), new GradientAlphaKey(1f, 0.85f), new GradientAlphaKey(0f, 1f) });
            col.color = new ParticleSystem.MinMaxGradient(g);

            ParticleSystemRenderer r = go.GetComponent<ParticleSystemRenderer>();
            r.material = ImpactFx.SparkMaterial;
            r.renderMode = ParticleSystemRenderMode.Stretch;
            r.velocityScale = Streak[tier];
            r.lengthScale = 1f;

            ParticleSystem.EmissionModule emission = ps.emission;
            emission.rateOverTime = 0f;
            ps.Play();
            return ps;
        }
    }
}
