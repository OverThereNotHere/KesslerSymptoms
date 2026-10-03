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
        private static Material fragmentMaterial;
        private static Mesh fragmentMesh;
        private const float FragmentLifetime = 5f;

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

        private float lifetime = Lifetime;

        public void Update()
        {
            age += Time.deltaTime;
            if (flash != null)
                flash.intensity = flashIntensity * Mathf.Clamp01(1f - age / FlashTime);
            if (age >= lifetime)
                Destroy(gameObject);
        }

        /// <summary>
        /// Tier 3 extra: a handful of small dark shards tumbling away from the impact, plus a
        /// second, bigger flash. Purely visual.
        /// </summary>
        public static void SpawnFragments(Part part, Vector3 point, Vector3 normal)
        {
            GameObject go = new GameObject("KesslerSymptoms_Fragments");
            go.transform.SetParent(part.transform, false);
            go.transform.position = point;
            go.transform.rotation = Quaternion.LookRotation(normal);

            ParticleSystem ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            ParticleSystem.MainModule main = ps.main;
            main.duration = 0.1f;
            main.loop = false;
            main.playOnAwake = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(3f, FragmentLifetime);
            main.startSpeed = new ParticleSystem.MinMaxCurve(2f, 8f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.04f, 0.14f);
            main.startRotation3D = true;
            main.startRotationX = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startRotationY = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startRotationZ = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.45f, 0.43f, 0.4f), new Color(0.25f, 0.24f, 0.23f));
            main.gravityModifier = 0f;
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.maxParticles = 16;

            ParticleSystem.EmissionModule emission = ps.emission;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)3, (short)7) });

            ParticleSystem.ShapeModule shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 55f;
            shape.radius = 0.05f;

            ParticleSystem.RotationOverLifetimeModule spin = ps.rotationOverLifetime;
            spin.enabled = true;
            spin.separateAxes = true;
            spin.x = new ParticleSystem.MinMaxCurve(-6f, 6f);
            spin.y = new ParticleSystem.MinMaxCurve(-6f, 6f);
            spin.z = new ParticleSystem.MinMaxCurve(-6f, 6f);

            // Fade out over the last stretch rather than popping out of existence.
            ParticleSystem.ColorOverLifetimeModule fade = ps.colorOverLifetime;
            fade.enabled = true;
            Gradient g = new Gradient();
            g.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 0.7f), new GradientAlphaKey(0f, 1f) });
            fade.color = new ParticleSystem.MinMaxGradient(g);

            ParticleSystemRenderer r = go.GetComponent<ParticleSystemRenderer>();
            r.renderMode = ParticleSystemRenderMode.Mesh;
            r.mesh = FragmentMesh;
            r.material = FragmentMaterial;
            r.alignment = ParticleSystemRenderSpace.Local;

            ImpactFx fx = go.AddComponent<ImpactFx>();
            fx.lifetime = FragmentLifetime + 0.5f;
            fx.flash = go.AddComponent<Light>();
            fx.flash.type = LightType.Point;
            fx.flash.color = new Color(1f, 0.85f, 0.65f);
            fx.flash.range = 8f;
            fx.flashIntensity = 6f;
            fx.flash.intensity = fx.flashIntensity;

            ps.Play();
        }

        /// <summary>A small irregular shard: a squashed, jittered octahedron.</summary>
        private static Mesh FragmentMesh
        {
            get
            {
                if (fragmentMesh != null) return fragmentMesh;
                fragmentMesh = new Mesh { name = "KesslerSymptoms_Shard" };
                fragmentMesh.vertices = new[]
                {
                    new Vector3(0.5f, 0.05f, 0f), new Vector3(-0.4f, -0.05f, 0.1f),
                    new Vector3(0f, 0.25f, 0.05f), new Vector3(0.1f, -0.2f, -0.05f),
                    new Vector3(0.05f, 0f, 0.35f), new Vector3(-0.05f, 0.02f, -0.3f),
                };
                fragmentMesh.triangles = new[]
                {
                    0, 2, 4, 2, 1, 4, 1, 3, 4, 3, 0, 4,
                    2, 0, 5, 1, 2, 5, 3, 1, 5, 0, 3, 5,
                };
                fragmentMesh.colors = new[] { Color.white, Color.white, Color.white, Color.white, Color.white, Color.white };
                fragmentMesh.RecalculateNormals();
                fragmentMesh.RecalculateBounds();
                return fragmentMesh;
            }
        }

        private static Material FragmentMaterial
        {
            get
            {
                if (fragmentMaterial != null) return fragmentMaterial;
                // Particle shaders are unlit, so shade with a plain white texture and vertex colour.
                Shader shader = Shader.Find("KSP/Particles/Alpha Blended");
                if (shader == null) shader = Shader.Find("Sprites/Default");
                fragmentMaterial = new Material(shader) { mainTexture = Texture2D.whiteTexture };
                return fragmentMaterial;
            }
        }

        /// <summary>Additive particle material with a soft round dot texture, built once.</summary>
        public static Material SparkMaterial
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
