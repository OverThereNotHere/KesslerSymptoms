using UnityEngine;

namespace KesslerSymptoms
{
    /// <summary>
    /// One-shot sound playback. Clips are looked up in KSP's GameDatabase by URL (path under
    /// GameData without extension); the first URL that resolves wins, so stock sounds can be
    /// listed after ours as fallbacks.
    /// </summary>
    public static class Sfx
    {
        public static readonly string[] LightImpacts =
        {
            "KesslerSymptoms/Sounds/litepelt1",
            "KesslerSymptoms/Sounds/litepelt2",
        };
        public static readonly string[] HardImpacts =
        {
            "KesslerSymptoms/Sounds/hardpelt1",
            "KesslerSymptoms/Sounds/hardpelt2",
        };
        public static readonly string[] ImpactFallback =
        {
            "Squad/Sounds/sound_click_tick",
            "Squad/Sounds/sound_click_tock",
        };
        public static readonly string[] SmallLeaks =
        {
            "KesslerSymptoms/Sounds/leak1",
            "KesslerSymptoms/Sounds/leak2",
        };
        public const string BigLeak = "KesslerSymptoms/Sounds/leak3";
        /// <summary>Metal snapping when something breaks: stock's strut-disconnect sound.</summary>
        public static readonly string[] Snaps = { "Squad/Sounds/ksp1_strunts_disconnect_v3_pitched2" };
        /// <summary>Brittle cell shattering, layered on a snap when a solar panel breaks.</summary>
        public static readonly string[] Shatters = { "KesslerSymptoms/Sounds/glass1" };
        // Tier 3 failure sounds: drop files with these names in Sounds/ (any that exist are used).
        public static readonly string[] Zaps = { "KesslerSymptoms/Sounds/zap1", "KesslerSymptoms/Sounds/zap2" };
        public static readonly string[] ZapFallback = { "Squad/Sounds/sound_click_sharp" };
        public static readonly string[] Pops = { "KesslerSymptoms/Sounds/pop1", "KesslerSymptoms/Sounds/pop2" };
        public static readonly string[] Statics = { "KesslerSymptoms/Sounds/static1", "KesslerSymptoms/Sounds/static2" };
        /// <summary>Geiger counter clicking after an RTG is cracked (a few seconds, fades out).</summary>
        public const string Geiger = "KesslerSymptoms/Sounds/geiger";
        /// <summary>Alarm for a part actually being damaged (distinct from the impact alert).</summary>
        public const string PartAlarm = "KesslerSymptoms/Sounds/partalarm";
        public const string Alarm = "KesslerSymptoms/Sounds/alert";
        public const string AlarmFallback = "Squad/Alarms/Sounds/ComputerShort";

        /// <summary>
        /// Low-pass cutoffs (Hz) for sounds heard through the hull: in vacuum, impacts and leaks
        /// only reach the crew through the structure, which dulls the high end.
        /// </summary>
        public const float ImpactMuffleHz = 4500f;
        public const float PeltMuffleHz = 3500f;
        public const float LeakMuffleHz = 3000f;

        /// <summary>
        /// A random clip from those in the set that actually exist, or from the fallback set if
        /// none do. So a set can list more files than are installed yet.
        /// </summary>
        public static AudioClip Pick(string[] urls, string[] fallback)
        {
            AudioClip clip = PickExisting(urls);
            if (clip == null && fallback != null) clip = PickExisting(fallback);
            return clip;
        }

        private static AudioClip PickExisting(string[] urls)
        {
            int start = Random.Range(0, urls.Length);
            for (int i = 0; i < urls.Length; i++)
            {
                AudioClip clip = GameDatabase.Instance.GetAudioClip(urls[(start + i) % urls.Length]);
                if (clip != null) return clip;
            }
            return null;
        }

        public static AudioClip Get(string url, string fallback)
        {
            AudioClip clip = GameDatabase.Instance.GetAudioClip(url);
            if (clip == null && fallback != null)
                clip = GameDatabase.Instance.GetAudioClip(fallback);
            return clip;
        }

        /// <summary>
        /// Play a clip from a point that follows <paramref name="parent"/>. Half-3D so it's
        /// positioned on the ship but still audible from a zoomed-out camera.
        /// </summary>
        /// <param name="lowPassHz">If above 0, muffle above this frequency: sound heard through the hull.</param>
        public static void PlayAt(AudioClip clip, Transform parent, Vector3 worldPos, float volume, float pitch, float lowPassHz = 0f)
        {
            if (clip == null) return;
            GameObject go = new GameObject("KesslerSymptoms_Sfx");
            go.transform.SetParent(parent, false);
            go.transform.position = worldPos;

            AudioSource src = go.AddComponent<AudioSource>();
            src.clip = clip;
            src.volume = volume * GameSettings.SHIP_VOLUME;
            src.pitch = pitch;
            src.spatialBlend = 0.5f;
            src.rolloffMode = AudioRolloffMode.Logarithmic;
            src.minDistance = 15f;
            src.maxDistance = 1000f;
            src.dopplerLevel = 0f;
            if (lowPassHz > 0f) go.AddComponent<AudioLowPassFilter>().cutoffFrequency = lowPassHz;
            src.Play();
            Object.Destroy(go, clip.length / Mathf.Max(0.1f, pitch) + 0.1f);
        }

        /// <summary>
        /// Something on <paramref name="part"/> just broke: a muffled metal snap, plus faint
        /// shattering for solar panels. Muffled because in vacuum the only way to hear it is
        /// through the structure, which loses the high end.
        /// </summary>
        public static void PlayBreak(Part part, bool brittle)
        {
            Vector3 pos = part.transform.position;
            PlayAt(Pick(Snaps, null), part.transform, pos,
                (float)Settings.PingVolume * 0.8f, Random.Range(0.9f, 1.2f), 2500f);
            if (brittle)
                PlayAt(Pick(Shatters, null), part.transform, pos,
                    (float)Settings.PingVolume * 0.45f, Random.Range(0.95f, 1.15f), 4000f);
        }

        /// <summary>One-shot from the part's position (no-op if the clip is missing).</summary>
        public static void PlayAtPart(Part part, AudioClip clip, float volumeScale, float lowPassHz)
        {
            PlayAt(clip, part.transform, part.transform.position, (float)Settings.PingVolume * volumeScale,
                Random.Range(0.92f, 1.08f), lowPassHz);
        }

        /// <summary>
        /// Loop a clip from the part for <paramref name="seconds"/>, then fade it out over a
        /// quarter second, e.g. radio static for exactly as long as the signal is gone.
        /// </summary>
        public static void PlayFor(Part part, AudioClip clip, float seconds, float volumeScale, float lowPassHz)
        {
            if (clip == null || seconds <= 0f) return;
            GameObject go = new GameObject("KesslerSymptoms_SfxLoop");
            go.transform.SetParent(part.transform, false);

            AudioSource src = go.AddComponent<AudioSource>();
            src.clip = clip;
            src.loop = true;
            src.volume = (float)Settings.PingVolume * volumeScale * GameSettings.SHIP_VOLUME;
            src.spatialBlend = 0.5f;
            src.rolloffMode = AudioRolloffMode.Logarithmic;
            src.minDistance = 15f;
            src.maxDistance = 1000f;
            src.dopplerLevel = 0f;
            src.time = Random.Range(0f, clip.length);
            if (lowPassHz > 0f) go.AddComponent<AudioLowPassFilter>().cutoffFrequency = lowPassHz;
            src.Play();
            go.AddComponent<SfxFadeOut>().Init(src, seconds);
        }

        private static AudioSource source2D;

        /// <summary>
        /// Play a non-positional clip, e.g. an alarm, on one persistent source at top priority.
        /// Unity only plays a limited number of voices at once and silently drops the
        /// lowest-priority ones, which a big ship's part sounds plus field pelts can exhaust.
        /// </summary>
        public static void Play2D(AudioClip clip, float volume)
        {
            if (clip == null)
            {
                Log.Warn("Play2D: no clip");
                return;
            }
            if (source2D == null)
            {
                GameObject go = new GameObject("KesslerSymptoms_Sfx2D");
                Object.DontDestroyOnLoad(go);
                source2D = go.AddComponent<AudioSource>();
                source2D.playOnAwake = false;
                source2D.spatialBlend = 0f;
                source2D.priority = 0;
                source2D.dopplerLevel = 0f;
                source2D.bypassEffects = true;
                source2D.bypassListenerEffects = true;
                source2D.bypassReverbZones = true;
            }
            float v = volume * GameSettings.UI_VOLUME;
            source2D.PlayOneShot(clip, v);
            Log.Info(string.Format("Alarm: {0} ({1:F2} s) at volume {2:F2}", clip.name, clip.length, v));
        }
    }

    /// <summary>Plays for a while, fades out over a quarter second, then removes itself.</summary>
    public class SfxFadeOut : MonoBehaviour
    {
        private const float Fade = 0.25f;
        private AudioSource src;
        private float playFor;
        private float startVolume;
        private float age;

        public void Init(AudioSource source, float seconds)
        {
            src = source;
            playFor = seconds;
            startVolume = source.volume;
        }

        public void Update()
        {
            age += Time.deltaTime;
            if (src != null && age > playFor)
                src.volume = startVolume * Mathf.Clamp01(1f - (age - playFor) / Fade);
            if (age >= playFor + Fade) Destroy(gameObject);
        }
    }
}
