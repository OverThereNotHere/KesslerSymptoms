using UnityEngine;

namespace KesslerSymptoms
{
    /// <summary>
    /// Looping visuals and sound for a punctured tank: a layered vapour jet out of the hole plus
    /// a hiss, all scaled by how fast it's leaking right now, so they fade as the leak seals.
    /// Owned by ModuleKesslerDamage; lives on a child object at the hole.
    /// </summary>
    public class LeakFx : MonoBehaviour
    {
        /// <summary>Leak rate (%/min) that counts as full intensity.</summary>
        public const double FullRatePctPerMin = 4.0;
        /// <summary>At or above this starting rate, a leak uses the big leak sound.</summary>
        public const double BigLeakPctPerMin = 2.0;

        private static Material vapourMaterial;

        // Three layers: a dense bright core jet, a wide slow plume billowing around it, and a
        // few glinting frozen-propellant specks.
        private ParticleSystem core;
        private ParticleSystem plume;
        private ParticleSystem frost;
        private AudioSource hiss;
        private float baseVolume;

        public static LeakFx Create(Part part, Vector3 localPos, Vector3 localNormal, bool bigLeak)
        {
            GameObject go = new GameObject("KesslerSymptoms_Leak");
            go.transform.SetParent(part.transform, false);
            go.transform.localPosition = localPos;
            go.transform.localRotation = Quaternion.LookRotation(localNormal);

            LeakFx fx = go.AddComponent<LeakFx>();
            fx.core = BuildLayer(Child(go, "Core"), 10f, 0.03f, new Vector2(0.4f, 1.0f), VapourMaterial,
                new Color(0.95f, 0.97f, 1f, 0.75f), new Color(0.85f, 0.9f, 0.95f, 0.55f), 3f);
            fx.plume = BuildLayer(Child(go, "Plume"), 35f, 0.08f, new Vector2(1.2f, 2.6f), VapourMaterial,
                new Color(0.9f, 0.93f, 0.97f, 0.3f), new Color(0.8f, 0.83f, 0.88f, 0.15f), 4.5f);
            fx.frost = BuildLayer(Child(go, "Frost"), 22f, 0.03f, new Vector2(0.6f, 1.6f), ImpactFx.SparkMaterial,
                new Color(0.7f, 0.8f, 0.9f, 1f), new Color(0.5f, 0.6f, 0.7f, 1f), 1f);
            fx.hiss = BuildHiss(go, bigLeak);
            // The big leak recording is ~24 dB hotter; keep it only somewhat louder. Both a bit
            // above the impacts, since the low-pass below takes some loudness away.
            fx.baseVolume = bigLeak ? 0.17f : 1.4f;
            return fx;
        }

        private static GameObject Child(GameObject parent, string name)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(parent.transform, false);
            return go;
        }

        /// <param name="ratePctPerMin">Current leak rate, percent of the tank per minute.</param>
        public void SetRate(double ratePctPerMin)
        {
            float k = Mathf.Clamp01((float)(ratePctPerMin / FullRatePctPerMin));

            Tune(core, 40f + 260f * k, 2f + 6f * k, 5f + 14f * k, 0.06f + 0.12f * k, 0.15f + 0.3f * k);
            Tune(plume, 8f + 60f * k, 0.6f + 1.5f * k, 1.5f + 4f * k, 0.25f + 0.4f * k, 0.6f + 1.2f * k);
            Tune(frost, 4f + 36f * k, 3f + 6f * k, 6f + 14f * k, 0.01f, 0.03f);

            if (hiss != null)
            {
                hiss.volume = baseVolume * (0.3f + 0.7f * k) * (float)Settings.PingVolume * GameSettings.SHIP_VOLUME;
                hiss.pitch = 0.85f + 0.3f * k;
            }
        }

        private static void Tune(ParticleSystem ps, float rate, float speedMin, float speedMax, float sizeMin, float sizeMax)
        {
            ParticleSystem.EmissionModule emission = ps.emission;
            emission.rateOverTime = rate;
            ParticleSystem.MainModule main = ps.main;
            main.startSpeed = new ParticleSystem.MinMaxCurve(speedMin, speedMax);
            main.startSize = new ParticleSystem.MinMaxCurve(sizeMin, sizeMax);
        }

        /// <param name="growth">How much each puff grows over its life (1 = not at all).</param>
        private static ParticleSystem BuildLayer(GameObject go, float coneAngle, float radius, Vector2 lifetime,
            Material material, Color colorA, Color colorB, float growth)
        {
            ParticleSystem ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            ParticleSystem.MainModule main = ps.main;
            main.loop = true;
            main.playOnAwake = false;
            main.duration = 1f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(lifetime.x, lifetime.y);
            main.startColor = new ParticleSystem.MinMaxGradient(colorA, colorB);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.gravityModifier = 0f;
            // Local space: the jet stays attached to the ship instead of being smeared by
            // KSP's moving reference frame at orbital speed.
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.maxParticles = 800;

            ParticleSystem.ShapeModule shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = coneAngle;
            shape.radius = radius;

            if (growth > 1f)
            {
                ParticleSystem.SizeOverLifetimeModule grow = ps.sizeOverLifetime;
                grow.enabled = true;
                grow.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f / growth, 1f, 1f));

                // Puffs slow as they spread out.
                ParticleSystem.LimitVelocityOverLifetimeModule drag = ps.limitVelocityOverLifetime;
                drag.enabled = true;
                drag.limit = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 20f, 1f, 1f));
                drag.dampen = 0.08f;

                ParticleSystem.RotationOverLifetimeModule spin = ps.rotationOverLifetime;
                spin.enabled = true;
                spin.z = new ParticleSystem.MinMaxCurve(-0.8f, 0.8f);
            }

            ParticleSystem.ColorOverLifetimeModule fade = ps.colorOverLifetime;
            fade.enabled = true;
            Gradient g = new Gradient();
            g.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.5f, 0.45f), new GradientAlphaKey(0f, 1f) });
            fade.color = new ParticleSystem.MinMaxGradient(g);

            ParticleSystemRenderer r = go.GetComponent<ParticleSystemRenderer>();
            r.material = material;
            r.renderMode = ParticleSystemRenderMode.Billboard;

            ps.Play();
            return ps;
        }

        private static AudioSource BuildHiss(GameObject go, bool bigLeak)
        {
            AudioClip clip = bigLeak
                ? Sfx.Get(Sfx.BigLeak, null)
                : Sfx.Pick(Sfx.SmallLeaks, null);
            if (clip == null) return null;

            AudioSource src = go.AddComponent<AudioSource>();
            src.clip = clip;
            src.loop = true;
            src.spatialBlend = 0.6f;
            src.rolloffMode = AudioRolloffMode.Logarithmic;
            src.minDistance = 10f;
            src.maxDistance = 600f;
            src.dopplerLevel = 0f;
            src.time = Random.Range(0f, clip.length); // don't start every leak at the same spot
            go.AddComponent<AudioLowPassFilter>().cutoffFrequency = Sfx.LeakMuffleHz;
            src.Play();
            return src;
        }

        /// <summary>Soft round alpha-blended puff, built once.</summary>
        public static Material VapourMaterial
        {
            get
            {
                if (vapourMaterial != null) return vapourMaterial;

                Shader shader = Shader.Find("KSP/Particles/Alpha Blended");
                if (shader == null)
                {
                    Log.Error("Shader KSP/Particles/Alpha Blended not found; leak vapour will render wrong");
                    shader = Shader.Find("Sprites/Default");
                }

                const int size = 32;
                Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
                for (int y = 0; y < size; y++)
                {
                    for (int x = 0; x < size; x++)
                    {
                        float dx = (x + 0.5f) / size * 2f - 1f;
                        float dy = (y + 0.5f) / size * 2f - 1f;
                        float a = Mathf.Clamp01(1f - Mathf.Sqrt(dx * dx + dy * dy));
                        tex.SetPixel(x, y, new Color(1f, 1f, 1f, a * a));
                    }
                }
                tex.Apply();

                vapourMaterial = new Material(shader) { mainTexture = tex };
                return vapourMaterial;
            }
        }
    }
}
