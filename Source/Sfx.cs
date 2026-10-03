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
        public const string Alarm = "KesslerSymptoms/Sounds/alert";
        public const string AlarmFallback = "Squad/Alarms/Sounds/ComputerShort";

        /// <summary>A random clip from the set, or from the fallback set if none of ours loaded.</summary>
        public static AudioClip Pick(string[] urls, string[] fallback)
        {
            AudioClip clip = GameDatabase.Instance.GetAudioClip(urls[Random.Range(0, urls.Length)]);
            if (clip == null && fallback != null)
                clip = GameDatabase.Instance.GetAudioClip(fallback[Random.Range(0, fallback.Length)]);
            return clip;
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
        public static void PlayAt(AudioClip clip, Transform parent, Vector3 worldPos, float volume, float pitch)
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
            src.Play();
            Object.Destroy(go, clip.length / Mathf.Max(0.1f, pitch) + 0.1f);
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
}
