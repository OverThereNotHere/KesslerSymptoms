using UnityEngine;

namespace KesslerSymptoms
{
    /// <summary>
    /// Looping visuals and sound for a punctured tank: a vapour jet out of the hole plus a hiss,
    /// both scaled by how fast it's leaking right now, so they fade as the leak seals.
    /// Owned by ModuleKesslerDamage; lives on a child object at the hole.
    /// </summary>
    public class LeakFx : MonoBehaviour
    {
        /// <summary>Leak rate (%/min) that counts as full intensity.</summary>
        public const double FullRatePctPerMin = 4.0;
        /// <summary>At or above this starting rate, a leak uses the big leak sound.</summary>
        public const double BigLeakPctPerMin = 2.0;

        private static Material vapourMaterial;

        private ParticleSystem ps;
        private AudioSource hiss;
        private float baseVolume;

        public static LeakFx Create(Part part, Vector3 localPos, Vector3 localNormal, bool bigLeak)
        {
            GameObject go = new GameObject("KesslerSymptoms_Leak");
            go.transform.SetParent(part.transform, false);
            go.transform.localPosition = localPos;
            go.transform.localRotation = Quaternion.LookRotation(localNormal);

            LeakFx fx = go.AddComponent<LeakFx>();
            fx.ps = BuildJet(go);
            fx.hiss = BuildHiss(go, bigLeak);
            fx.baseVolume = bigLeak ? 0.12f : 1.0f; // the big leak recording is ~24 dB hotter; keep it only somewhat louder
            return fx;
        }

        /// <param name="ratePctPerMin">Current leak rate, percent of the tank per minute.</param>
        public void SetRate(double ratePctPerMin)
        {
            float k = Mathf.Clamp01((float)(ratePctPerMin / FullRatePctPerMin));

            ParticleSystem.EmissionModule emission = ps.emission;
            emission.rateOverTime = 10f + 110f * k;
            ParticleSystem.MainModule main = ps.main;
            main.startSpeed = new ParticleSystem.MinMaxCurve(1.5f + 4f * k, 3f + 9f * k);
            main.startSize = new ParticleSystem.MinMaxCurve(0.05f + 0.1f * k, 0.12f + 0.25f * k);

            if (hiss != null)
            {
                hiss.volume = baseVolume * (0.3f + 0.7f * k) * (float)Settings.PingVolume * GameSettings.SHIP_VOLUME;
                hiss.pitch = 0.85f + 0.3f * k;
            }
        }

        private static ParticleSystem BuildJet(GameObject go)
        {
            ParticleSystem ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            ParticleSystem.MainModule main = ps.main;
            main.loop = true;
            main.playOnAwake = false;
            main.duration = 1f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.6f, 1.4f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.92f, 0.95f, 1f, 0.55f), new Color(0.8f, 0.85f, 0.9f, 0.35f));
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.gravityModifier = 0f;
            // Local space: the jet stays attached to the ship instead of being smeared by
            // KSP's moving reference frame at orbital speed.
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.maxParticles = 400;

            ParticleSystem.ShapeModule shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 12f;
            shape.radius = 0.03f;

            // Puffs expand and thin out as they leave the hole.
            ParticleSystem.SizeOverLifetimeModule grow = ps.sizeOverLifetime;
            grow.enabled = true;
            grow.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.4f, 1f, 2.5f));

            ParticleSystem.ColorOverLifetimeModule fade = ps.colorOverLifetime;
            fade.enabled = true;
            Gradient g = new Gradient();
            g.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0.9f, 0f), new GradientAlphaKey(0.4f, 0.4f), new GradientAlphaKey(0f, 1f) });
            fade.color = new ParticleSystem.MinMaxGradient(g);

            ParticleSystemRenderer r = go.GetComponent<ParticleSystemRenderer>();
            r.material = VapourMaterial;
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
            src.Play();
            return src;
        }

        /// <summary>Soft round alpha-blended puff, built once.</summary>
        private static Material VapourMaterial
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
