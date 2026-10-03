using System.Collections.Generic;
using UnityEngine;

namespace KesslerSymptoms
{
    /// <summary>
    /// A small scorch/pit mark stuck to the hull where a hit landed, fading out over
    /// MarkFadeSeconds. Not a true decal (KSP has no decal system): a flat textured quad a few
    /// millimetres above the surface, which reads fine at impact size. Not saved; capped in count.
    /// </summary>
    public class ImpactMark : MonoBehaviour
    {
        private const int MaxMarks = 40;
        /// <summary>Fraction of the lifetime spent at full strength before fading starts.</summary>
        private const float HoldFraction = 0.2f;

        private static readonly LinkedList<ImpactMark> live = new LinkedList<ImpactMark>();
        private static Mesh quad;
        private static Material sharedMaterial;

        private Material material;
        private float age;
        private float lifetime;
        private LinkedListNode<ImpactMark> node;

        /// <param name="scale">Same scale as the impact's sparks: 1 = tier 1, larger = heavier hits.</param>
        public static void Spawn(Part part, Vector3 point, Vector3 normal, float scale)
        {
            if (!Settings.ImpactMarksEnabled || Settings.MarkFadeSeconds <= 0) return;

            while (live.Count >= MaxMarks)
            {
                // Destroyed marks unlist themselves in OnDestroy, so everything here is alive.
                ImpactMark oldest = live.First.Value;
                live.RemoveFirst();
                oldest.node = null;
                Destroy(oldest.gameObject);
            }

            GameObject go = new GameObject("KesslerSymptoms_Mark");
            go.transform.SetParent(part.transform, false);
            go.transform.position = point + normal * 0.004f; // just above the hull: no z-fighting
            go.transform.rotation = Quaternion.LookRotation(normal) * Quaternion.Euler(0f, 0f, Random.Range(0f, 360f));
            float size = Random.Range(0.06f, 0.11f) * scale * scale; // tier 3 marks come out ~5x a ping's
            go.transform.localScale = new Vector3(size, size, size);

            go.AddComponent<MeshFilter>().sharedMesh = Quad;
            MeshRenderer r = go.AddComponent<MeshRenderer>();
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;

            ImpactMark mark = go.AddComponent<ImpactMark>();
            mark.material = new Material(SharedMaterial); // own copy so each mark fades on its own
            r.sharedMaterial = mark.material;
            mark.lifetime = (float)Settings.MarkFadeSeconds;
            mark.node = live.AddLast(mark);
        }

        public void Update()
        {
            age += Time.deltaTime;
            if (age >= lifetime)
            {
                Destroy(gameObject);
                return;
            }
            float t = Mathf.InverseLerp(lifetime * HoldFraction, lifetime, age);
            SetAlpha(1f - t * t * (3f - 2f * t)); // smoothstep fade
        }

        public void OnDestroy()
        {
            if (node != null && node.List != null) live.Remove(node);
            node = null;
            if (material != null) Destroy(material);
        }

        private void SetAlpha(float a)
        {
            Color c = new Color(1f, 1f, 1f, a);
            if (material.HasProperty("_TintColor")) material.SetColor("_TintColor", new Color(0.5f, 0.5f, 0.5f, 0.5f * a));
            if (material.HasProperty("_Color")) material.color = c;
        }

        /// <summary>Unit quad in the XY plane facing +Z (the surface normal after rotation).</summary>
        private static Mesh Quad
        {
            get
            {
                if (quad != null) return quad;
                quad = new Mesh { name = "KesslerSymptoms_MarkQuad" };
                quad.vertices = new[]
                {
                    new Vector3(-0.5f, -0.5f, 0f), new Vector3(0.5f, -0.5f, 0f),
                    new Vector3(0.5f, 0.5f, 0f), new Vector3(-0.5f, 0.5f, 0f),
                };
                quad.uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) };
                // Particle shaders multiply by vertex colour; don't leave it undefined.
                quad.colors = new[] { Color.white, Color.white, Color.white, Color.white };
                // Both windings, so it shows whichever way the shader culls.
                quad.triangles = new[] { 0, 2, 1, 0, 3, 2, 0, 1, 2, 0, 2, 3 };
                quad.RecalculateNormals();
                quad.RecalculateBounds();
                return quad;
            }
        }

        /// <summary>Procedural placeholder: dark pit, sooty scorch ring, ragged edge.</summary>
        private static Material SharedMaterial
        {
            get
            {
                if (sharedMaterial != null) return sharedMaterial;

                Shader shader = Shader.Find("KSP/Particles/Alpha Blended");
                if (shader == null)
                {
                    Log.Error("Shader KSP/Particles/Alpha Blended not found; impact marks will render wrong");
                    shader = Shader.Find("Sprites/Default");
                }

                const int size = 64;
                Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, true);
                float seed = Random.Range(0f, 100f);
                for (int y = 0; y < size; y++)
                {
                    for (int x = 0; x < size; x++)
                    {
                        float dx = (x + 0.5f) / size * 2f - 1f;
                        float dy = (y + 0.5f) / size * 2f - 1f;
                        float angle = Mathf.Atan2(dy, dx);
                        // Ragged rim: radius wobbles with angle.
                        float rim = 0.85f + 0.12f * (Mathf.PerlinNoise(seed + Mathf.Cos(angle) * 2f, seed + Mathf.Sin(angle) * 2f) - 0.5f) * 2f;
                        float r = Mathf.Sqrt(dx * dx + dy * dy) / rim;

                        float alpha = Mathf.Clamp01((1f - r) * 3f);         // soft outer edge
                        float soot = Mathf.Lerp(0.05f, 0.35f, Mathf.Clamp01((r - 0.25f) / 0.6f));
                        if (r < 0.22f) soot = 0.02f;                          // the pit itself
                        float grain = Mathf.PerlinNoise(seed + x * 0.3f, seed + y * 0.3f) * 0.1f;
                        float v = Mathf.Clamp01(soot + grain);
                        tex.SetPixel(x, y, new Color(v, v * 0.95f, v * 0.9f, alpha * (r < 0.22f ? 1f : 0.85f)));
                    }
                }
                tex.Apply();

                sharedMaterial = new Material(shader) { mainTexture = tex };
                return sharedMaterial;
            }
        }
    }
}
