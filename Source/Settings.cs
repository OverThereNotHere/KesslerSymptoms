using System;
using System.IO;

namespace KesslerSymptoms
{
    /// <summary>
    /// All tunable numbers. Loaded from PluginData/settings.cfg; missing keys keep their
    /// defaults, and the file is written out with defaults if it doesn't exist.
    /// </summary>
    public static class Settings
    {
        public const string NodeName = "KESSLER_SYMPTOMS";

        // --- Bands ---
        /// <summary>Bands generated per body between minOrbitalDistance and the ceiling.</summary>
        public static int BandsPerBody = 12;
        /// <summary>Ceiling is min(SOI, this × body radius), measured from the body's centre.</summary>
        public static double CeilingRadii = 12.0;
        /// <summary>Thickness ratio between consecutive bands (1 = uniform, &gt;1 = thicker going up).</summary>
        public static double BandGrowth = 1.35;

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

            node.TryGetValue("BandsPerBody", ref BandsPerBody);
            node.TryGetValue("CeilingRadii", ref CeilingRadii);
            node.TryGetValue("BandGrowth", ref BandGrowth);
            node.TryGetValue("ScanIntervalSeconds", ref ScanIntervalSeconds);
            node.TryGetValue("ExplosionSpike", ref ExplosionSpike);
            node.TryGetValue("ExplosionHalfLifeDays", ref ExplosionHalfLifeDays);
            node.TryGetValue("Tier1At", ref Tier1At);
            node.TryGetValue("Tier2At", ref Tier2At);
            node.TryGetValue("Tier3At", ref Tier3At);

            BandsPerBody = Math.Max(1, BandsPerBody);
            CeilingRadii = Math.Max(1.1, CeilingRadii);
            BandGrowth = Math.Max(1.0, BandGrowth);
            ScanIntervalSeconds = Math.Max(0.5f, ScanIntervalSeconds);
            ExplosionHalfLifeDays = Math.Max(0.01, ExplosionHalfLifeDays);

            Log.Info("Settings loaded");
        }

        public static void Save()
        {
            ConfigNode node = new ConfigNode(NodeName);
            node.AddValue("BandsPerBody", BandsPerBody);
            node.AddValue("CeilingRadii", CeilingRadii);
            node.AddValue("BandGrowth", BandGrowth);
            node.AddValue("ScanIntervalSeconds", ScanIntervalSeconds);
            node.AddValue("ExplosionSpike", ExplosionSpike);
            node.AddValue("ExplosionHalfLifeDays", ExplosionHalfLifeDays);
            node.AddValue("Tier1At", Tier1At);
            node.AddValue("Tier2At", Tier2At);
            node.AddValue("Tier3At", Tier3At);

            ConfigNode root = new ConfigNode();
            root.AddNode(node);
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
            root.Save(FilePath);
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
