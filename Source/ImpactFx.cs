using UnityEngine;

namespace KesslerSymptoms
{
    /// <summary>
    /// Visual for an impact: a burst of streaking sparks plus a brief light flash, attached to
    /// the hit part so it travels with the ship. Cleans itself up after a second.
    /// </summary>
    public class ImpactFx : MonoBehaviour
    {
        private const float Lifetime = 1.0f;
        private const float FlashTime = 0.15f;

        private static Material sparkMaterial;

        private Light flash;
        private float flashIntensity;
        private float age;

        /// <param name="scale">1 = tier 1 ping; larger = more, faster, bigger sparks.</param>
        public static void Spawn(Part part, Vector3 point, Vector3 normal, float scale)
        {
            GameObject go = new GameObject("KesslerSymptoms_Impact");
            go.transform.SetParent(part.transform, false);
            go.transform.position = point;
            go.transform.rotation = Quaternion.LookRotation(normal);

            ParticleSystem ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear); // settings below need a stopped system

            ParticleSystem.MainModule main = ps.main;
            main.duration = 0.1f;
            main.loop = false;
            main.playOnAwake = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.15f, 0.45f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(2f * scale, 9f * scale);
            main.startSize = new ParticleSystem.MinMaxCurve(0.02f * scale, 0.06f * scale);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.95f, 0.75f), new Color(1f, 0.6f, 0.2f));
            main.gravityModifier = 0f;
            // Local space: sparks ride along with the ship instead of being left behind by
            // KSP's moving reference frame.
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.maxParticles = 128;

            ParticleSystem.EmissionModule emission = ps.emission;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)(12 * scale), (short)(24 * scale)) });

            // Cone points along the transform's +Z, which we aimed along the surface normal.
            ParticleSystem.ShapeModule shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 40f;
            shape.radius = 0.02f;

            ParticleSystem.ColorOverLifetimeModule fade = ps.colorOverLifetime;
            fade.enabled = true;
            Gradient g = new Gradient();
            g.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(new Color(1f, 0.45f, 0.1f), 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });
            fade.color = new ParticleSystem.MinMaxGradient(g);

            ParticleSystemRenderer renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.material = SparkMaterial;
            renderer.renderMode = ParticleSystemRenderMode.Stretch;
            renderer.velocityScale = 0.04f;
            renderer.lengthScale = 2f;

            ImpactFx fx = go.AddComponent<ImpactFx>();
            fx.flash = go.AddComponent<Light>();
            fx.flash.type = LightType.Point;
            fx.flash.color = new Color(1f, 0.8f, 0.5f);
            fx.flash.range = 1f + 2f * scale;
            fx.flashIntensity = 2f * scale;
            fx.flash.intensity = fx.flashIntensity;

            ps.Play();
        }

        public void Update()
        {
            age += Time.deltaTime;
            if (flash != null)
                flash.intensity = flashIntensity * Mathf.Clamp01(1f - age / FlashTime);
            if (age >= Lifetime)
                Destroy(gameObject);
        }

        /// <summary>Additive particle material with a soft round dot texture, built once.</summary>
        private static Material SparkMaterial
        {
            get
            {
                if (sparkMaterial != null) return sparkMaterial;

                Shader shader = Shader.Find("KSP/Particles/Additive");
                if (shader == null)
                {
                    Log.Error("Shader KSP/Particles/Additive not found; sparks will render wrong");
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
                        a *= a;
                        // Fade colour too: additive shaders may ignore alpha entirely.
                        tex.SetPixel(x, y, new Color(a, a, a, a));
                    }
                }
                tex.Apply();

                sparkMaterial = new Material(shader) { mainTexture = tex };
                return sparkMaterial;
            }
        }
    }
}
