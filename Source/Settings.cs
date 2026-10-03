using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace KesslerSymptoms
{
    /// <summary>
    /// One tunable as seen by the config file and the settings tab: a key, a label, and
    /// string conversions in both directions (the setter validates and clamps).
    /// </summary>
    public class SettingDef
    {
        public string Key;
        public string Label;
        public string Help;
        /// <summary>Changing this invalidates band geometry, so bands must be rebuilt.</summary>
        public bool AffectsBands;
        /// <summary>On/off switch: shown in the Effects tab instead of the Settings tab.</summary>
        public bool IsToggle;
        public Func<string> Get;
        public Func<string, bool> TrySet;
        /// <summary>True if the string parses; doesn't change anything.</summary>
        public Func<string, bool> IsValid;
        public string Default;
    }

    /// <summary>
    /// All tunable numbers. Loaded from PluginData/settings.cfg; missing keys keep their
    /// defaults, and the file is written out with defaults if it doesn't exist.
    /// </summary>
    public static class Settings
    {
        public const string NodeName = "KESSLER_SYMPTOMS";

        // --- Effect toggles ---
        /// <summary>Master switch for all encounter effects. Tracking keeps running either way.</summary>
        public static bool EffectsEnabled = true;
        public static bool Tier1Enabled = true;
        public static bool Tier2Enabled = true;
        public static bool Tier3Enabled = true;
        /// <summary>Encounters keep happening during rails time warp (tier 2/3 drop you out of it).</summary>
        public static bool RailsWarpEncounters = true;
        /// <summary>Old debris is deleted after an altitude-based lifetime.</summary>
        public static bool DebrisDecayEnabled = true;

        // --- Debris decay ---
        /// <summary>Lifetime (game hours) of debris with periapsis right at the top of the atmosphere.</summary>
        public static double DecayBaseHours = 120.0;
        /// <summary>Lifetime multiplies by e for every (this × atmosphere depth) of periapsis height.</summary>
        public static double DecayScaleHeightFraction = 0.06;
        /// <summary>Per-debris spread: lifetime × e^(±this), fixed for each piece.</summary>
        public static double DecayRandomness = 0.5;

        // --- Bands ---
        /// <summary>Bands generated per body between minOrbitalDistance and the ceiling.</summary>
        public static int BandsPerBody = 12;
        /// <summary>Ceiling is min(SOI, this × body radius), measured from the body's centre.</summary>
        public static double CeilingRadii = 12.0;
        /// <summary>Thickness ratio between consecutive bands (1 = uniform, &gt;1 = thicker going up).</summary>
        public static double BandGrowth = 1.35;

        // --- Density ---
        /// <summary>How much density one orbiting debris vessel contributes.</summary>
        public static double DebrisWeightMultiplier = 0.25;

        // --- Scanning ---
        /// <summary>Real-time seconds between debris rescans.</summary>
        public static float ScanIntervalSeconds = 5f;

        // --- Explosion spikes ---
        /// <summary>Density added to a band when a part is destroyed in orbit there.</summary>
        public static double ExplosionSpike = 0.5;
        /// <summary>Half-life of an explosion spike, in game days (6 h Kerbin / 24 h Earth calendar).</summary>
        public static double ExplosionHalfLifeDays = 30.0;

        // --- Tiers (density thresholds) ---
        public static double Tier1At = 2.0;
        public static double Tier2At = 8.0;
        public static double Tier3At = 20.0;

        // --- Encounters ---
        /// <summary>Mean hits per game hour for each unit of band density.</summary>
        public static double HitsPerHourPerDensity = 10.0;
        /// <summary>Real seconds after an impact alert (alarm + text) before another can play.</summary>
        public static double AlertCooldownSeconds = 30.0;
        public static double PingVolume = 1.0;
        public static double AlarmVolume = 0.7;
        /// <summary>Impulse (tonne·m/s) pushed into the hit part, per tier. Tier 1 is harmless.</summary>
        public static double Tier2Impulse = 0.1;
        public static double Tier3Impulse = 0.5;

        // --- Debris fields ---
        /// <summary>Field chance = FieldChanceMax × density / (density + FieldHalfDensity).</summary>
        public static double FieldChanceMax = 0.6;
        public static double FieldHalfDensity = 10.0;
        /// <summary>Field length range, in game seconds. Fields block rails warp, so keep them short.</summary>
        public static double FieldDurationMin = 8.0;
        public static double FieldDurationMax = 20.0;
        /// <summary>Mean game seconds between real tier hits inside a field.</summary>
        public static double FieldSecondsPerHit = 2.5;
        /// <summary>Sound-only micro pelts per game second inside a field.</summary>
        public static double FieldPeltsPerSecond = 6.0;
        /// <summary>Micro pelt volume as a fraction of PingVolume.</summary>
        public static double PeltVolume = 0.35;

        /// <summary>Every tunable, in display order. Declared after the fields so defaults capture correctly.</summary>
        public static readonly List<SettingDef> Defs = new List<SettingDef>
        {
            Bool("EffectsEnabled", "All effects", "Master switch; debris tracking keeps running when off",
                () => EffectsEnabled, v => EffectsEnabled = v),
            Bool("Tier1Enabled", "Tier 1: Sparse", "Micrometeorite pings, sounds, warning",
                () => Tier1Enabled, v => Tier1Enabled = v),
            Bool("Tier2Enabled", "Tier 2: Dense", "Panels and antennas can break, rare tank punctures",
                () => Tier2Enabled, v => Tier2Enabled = v),
            Bool("Tier3Enabled", "Tier 3: Debris field", "Large impacts: leaks, shorts, engine jams",
                () => Tier3Enabled, v => Tier3Enabled = v),
            Bool("RailsWarpEncounters", "Encounters during time warp",
                "Roll encounters in rails warp; tier 1 keeps warping, tier 2/3 drop you to 1x",
                () => RailsWarpEncounters, v => RailsWarpEncounters = v),
            Bool("DebrisDecayEnabled", "Debris decay",
                "Delete old debris after a lifetime based on periapsis height (never on airless bodies)",
                () => DebrisDecayEnabled, v => DebrisDecayEnabled = v),
            Dbl("DebrisWeightMultiplier", "Debris weight multiplier",
                "Density one orbiting debris vessel adds to the bands it sweeps",
                () => DebrisWeightMultiplier, v => DebrisWeightMultiplier = v, 0, 100),
            Dbl("ExplosionSpike", "Explosion spike",
                "Density added where a part is destroyed in orbit",
                () => ExplosionSpike, v => ExplosionSpike = v, 0, 100),
            Dbl("ExplosionHalfLifeDays", "Spike half-life (days)",
                "Game days for an explosion spike to fade to half",
                () => ExplosionHalfLifeDays, v => ExplosionHalfLifeDays = v, 0.01, 100000),
            Dbl("Tier1At", "Tier 1 at density", "Sparse: micrometeorite pings",
                () => Tier1At, v => Tier1At = v, 0, 1e6),
            Dbl("Tier2At", "Tier 2 at density", "Dense: panels and antennas can break",
                () => Tier2At, v => Tier2At = v, 0, 1e6),
            Dbl("Tier3At", "Tier 3 at density", "Debris field: large impacts",
                () => Tier3At, v => Tier3At = v, 0, 1e6),
            Dbl("HitsPerHourPerDensity", "Hits/hour per density",
                "Average impacts per game hour = this x band density (off rails only)",
                () => HitsPerHourPerDensity, v => HitsPerHourPerDensity = v, 0, 1e5),
            Dbl("AlertCooldownSeconds", "Alert cooldown (s)",
                "Real seconds between impact alarms; hits in between still ping, just without the alarm",
                () => AlertCooldownSeconds, v => AlertCooldownSeconds = v, 0, 3600),
            Dbl("PingVolume", "Impact volume", "Volume of impact sounds (x ship volume)",
                () => PingVolume, v => PingVolume = v, 0, 2),
            Dbl("AlarmVolume", "Alarm volume", "Volume of the impact alarm (x UI volume)",
                () => AlarmVolume, v => AlarmVolume = v, 0, 2),
            Dbl("Tier2Impulse", "Tier 2 impulse", "Push (tonne m/s) a tier 2 hit gives the part it strikes",
                () => Tier2Impulse, v => Tier2Impulse = v, 0, 1000),
            Dbl("Tier3Impulse", "Tier 3 impulse", "Push (tonne m/s) a tier 3 hit gives the part it strikes",
                () => Tier3Impulse, v => Tier3Impulse = v, 0, 1000),
            Dbl("FieldChanceMax", "Field chance max", "Highest chance (0-1) that an encounter is a debris field",
                () => FieldChanceMax, v => FieldChanceMax = v, 0, 1),
            Dbl("FieldHalfDensity", "Field half density", "Density at which field chance reaches half its max",
                () => FieldHalfDensity, v => FieldHalfDensity = v, 0.01, 1e6),
            Dbl("FieldDurationMin", "Field min length (s)", "Shortest debris field, game seconds",
                () => FieldDurationMin, v => FieldDurationMin = v, 1, 3600),
            Dbl("FieldDurationMax", "Field max length (s)", "Longest debris field, game seconds (rails warp is locked during fields)",
                () => FieldDurationMax, v => FieldDurationMax = v, 1, 3600),
            Dbl("FieldSecondsPerHit", "Field s per hit", "Average game seconds between real hits in a field",
                () => FieldSecondsPerHit, v => FieldSecondsPerHit = v, 0.1, 600),
            Dbl("FieldPeltsPerSecond", "Field pelts/s", "Sound-only micro pelts per game second in a field",
                () => FieldPeltsPerSecond, v => FieldPeltsPerSecond = v, 0, 50),
            Dbl("PeltVolume", "Pelt volume", "Micro pelt volume as a fraction of impact volume",
                () => PeltVolume, v => PeltVolume = v, 0, 2),
            Dbl("DecayBaseHours", "Decay base (hours)",
                "Game hours debris lasts with periapsis at the top of the atmosphere",
                () => DecayBaseHours, v => DecayBaseHours = v, 0.01, 1e9),
            Dbl("DecayScaleHeightFraction", "Decay scale height",
                "Lifetime x e for every (this x atmosphere depth) of periapsis height",
                () => DecayScaleHeightFraction, v => DecayScaleHeightFraction = v, 0.001, 10),
            Dbl("DecayRandomness", "Decay randomness", "Each piece's lifetime is scaled by e^(+/- this)",
                () => DecayRandomness, v => DecayRandomness = v, 0, 5),
            Dbl("ScanIntervalSeconds", "Rescan interval (s)", "Real-time seconds between debris scans",
                () => ScanIntervalSeconds, v => ScanIntervalSeconds = (float)v, 0.5, 600),
            Int("BandsPerBody", "Bands per body", "Altitude bands generated around each body",
                () => BandsPerBody, v => BandsPerBody = v, 1, 64, true),
            Dbl("CeilingRadii", "Ceiling (body radii)", "Top of the band shell, capped at the SOI",
                () => CeilingRadii, v => CeilingRadii = v, 1.1, 1000, true),
            Dbl("BandGrowth", "Band growth", "Thickness ratio between consecutive bands (1 = uniform)",
                () => BandGrowth, v => BandGrowth = v, 1.0, 5.0, true),
        };

        private static SettingDef Dbl(string key, string label, string help,
            Func<double> get, Action<double> set, double min, double max, bool bands = false)
        {
            SettingDef d = new SettingDef
            {
                Key = key, Label = label, Help = help, AffectsBands = bands,
                Get = () => get().ToString("G", CultureInfo.InvariantCulture),
                IsValid = s => { double v; return ParseDouble(s, out v); },
                TrySet = s =>
                {
                    double v;
                    if (!ParseDouble(s, out v)) return false;
                    set(Math.Max(min, Math.Min(max, v)));
                    return true;
                },
            };
            d.Default = d.Get();
            return d;
        }

        private static SettingDef Int(string key, string label, string help,
            Func<int> get, Action<int> set, int min, int max, bool bands = false)
        {
            SettingDef d = new SettingDef
            {
                Key = key, Label = label, Help = help, AffectsBands = bands,
                Get = () => get().ToString(CultureInfo.InvariantCulture),
                IsValid = s => { int v; return ParseInt(s, out v); },
                TrySet = s =>
                {
                    int v;
                    if (!ParseInt(s, out v)) return false;
                    set(Math.Max(min, Math.Min(max, v)));
                    return true;
                },
            };
            d.Default = d.Get();
            return d;
        }

        private static SettingDef Bool(string key, string label, string help,
            Func<bool> get, Action<bool> set)
        {
            SettingDef d = new SettingDef
            {
                Key = key, Label = label, Help = help, IsToggle = true,
                Get = () => get() ? "True" : "False",
                IsValid = s => { bool v; return bool.TryParse(s, out v); },
                TrySet = s =>
                {
                    bool v;
                    if (!bool.TryParse(s, out v)) return false;
                    set(v);
                    return true;
                },
            };
            d.Default = d.Get();
            return d;
        }

        /// <summary>Chance (0-1) that an encounter at this density is a debris field instead of a one-off.</summary>
        public static double FieldChance(double density)
        {
            if (density <= 0) return 0;
            return FieldChanceMax * density / (density + FieldHalfDensity);
        }

        public static double ImpulseFor(int tier)
        {
            switch (tier)
            {
                case 2: return Tier2Impulse;
                case 3: return Tier3Impulse;
                default: return 0;
            }
        }

        /// <summary>Whether effects of this tier (1..3) may fire, honouring the master switch.</summary>
        public static bool TierEnabled(int tier)
        {
            if (!EffectsEnabled) return false;
            switch (tier)
            {
                case 1: return Tier1Enabled;
                case 2: return Tier2Enabled;
                case 3: return Tier3Enabled;
                default: return false;
            }
        }

        private static bool ParseDouble(string s, out double v)
        {
            return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v)
                && !double.IsNaN(v) && !double.IsInfinity(v);
        }

        private static bool ParseInt(string s, out int v)
        {
            return int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out v);
        }

        public static string FilePath
        {
            get { return Path.Combine(KSPUtil.ApplicationRootPath, "GameData/KesslerSymptoms/PluginData/settings.cfg"); }
        }

        public static void Load()
        {
            if (!File.Exists(FilePath))
            {
                Log.Info("No settings.cfg found; writing defaults to " + FilePath);
                Save();
                return;
            }

            ConfigNode root = ConfigNode.Load(FilePath);
            ConfigNode node = root != null ? root.GetNode(NodeName) : null;
            if (node == null)
            {
                Log.Warn("settings.cfg has no " + NodeName + " node; using defaults");
                return;
            }

            foreach (SettingDef d in Defs)
            {
                string s = node.GetValue(d.Key);
                if (s != null && !d.TrySet(s))
                    Log.Warn("settings.cfg: bad value '" + s + "' for " + d.Key + "; keeping " + d.Get());
            }
            Log.Info("Settings loaded");
        }

        public static void Save()
        {
            ConfigNode node = new ConfigNode(NodeName);
            foreach (SettingDef d in Defs)
                node.AddValue(d.Key, d.Get(), d.Help);

            ConfigNode root = new ConfigNode();
            root.AddNode(node);
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
            root.Save(FilePath);
            Log.Info("Settings saved to " + FilePath);
        }

        /// <summary>0 = clear, 1..3 = severity tier from idea.md.</summary>
        public static int TierFor(double density)
        {
            if (density >= Tier3At) return 3;
            if (density >= Tier2At) return 2;
            if (density >= Tier1At) return 1;
            return 0;
        }
    }
}
