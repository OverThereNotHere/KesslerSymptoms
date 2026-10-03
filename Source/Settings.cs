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
        /// <summary>Settings-tab group this belongs to (foldable header).</summary>
        public string Section = "Other";
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
        /// <summary>Tier 2/3 encounters drop you out of rails warp, and fields lock warp while they run.</summary>
        public static bool WarpDropOut = true;
        /// <summary>Old debris is deleted after an altitude-based lifetime.</summary>
        public static bool DebrisDecayEnabled = true;
        /// <summary>Drifting specks around the ship during a debris field.</summary>
        public static bool FieldVisualsEnabled = true;
        /// <summary>Fading scorch marks where hits land.</summary>
        public static bool ImpactMarksEnabled = false;
        /// <summary>On: periodic chance checks. Off: the older continuous per-hour rate.</summary>
        public static bool ChanceEncounters = true;

        // --- Debris decay ---
        /// <summary>Lifetime (calendar months) of debris with periapsis at the bottom of the band shell.</summary>
        public static double DecayMinMonths = 2.0;
        /// <summary>Lifetime (calendar years) of debris with periapsis at the top of the shell (SOI edge).</summary>
        public static double DecayMaxYears = 200.0;
        /// <summary>
        /// Curve shape, in body radii: lifetime climbs with log(1 + height / (this × radius)).
        /// Smaller = lifetimes climb sooner above the floor; larger = low orbits stay short-lived longer.
        /// </summary>
        public static double DecayCurveRadii = 0.01;
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
        /// <summary>Density added when debris breaks a panel/antenna/radiator in orbit (smaller than a part dying).</summary>
        public static double BreakSpike = 0.1;

        // --- Tiers (density thresholds) ---
        public static double Tier1At = 1.5;
        public static double Tier2At = 9.0;
        public static double Tier3At = 25.0;

        // --- Encounters ---
        /// <summary>Mean hits per game hour for each unit of band density.</summary>
        public static double HitsPerHourPerDensity = 10.0;
        /// <summary>Chance mode: game seconds between encounter checks.</summary>
        public static double ChanceCheckSeconds = 30.0;
        /// <summary>Chance mode: per-check chance = ChanceBase + ChancePerDensity × density (tier 1+ only).</summary>
        public static double ChanceBase = 0.02;
        public static double ChancePerDensity = 0.01;
        /// <summary>Chance an encounter is a lower tier than the band's, favouring the next tier down.</summary>
        public static double LowerTierChance = 0.25;
        /// <summary>Real seconds after an impact alert (alarm + text) before another can play.</summary>
        public static double AlertCooldownSeconds = 30.0;
        public static double PingVolume = 1.0;
        public static double AlarmVolume = 0.7;
        /// <summary>Impulse (tonne·m/s) pushed into the hit part, per tier. Tier 1 is harmless.</summary>
        public static double Tier2Impulse = 0.1;
        public static double Tier3Impulse = 0.5;

        // --- Tier 2 damage ---
        /// <summary>Chance a tier 2+ hit on a panel/antenna/radiator breaks it.</summary>
        public static double Tier2BreakChance = 0.35;
        /// <summary>Chance a tier 2+ hit on a tank with leakable contents punctures it.</summary>
        public static double Tier2PunctureChance = 0.05;
        /// <summary>Initial leak rate range, percent of capacity per minute.</summary>
        public static double LeakRateMinPctPerMin = 1.0;
        public static double LeakRateMaxPctPerMin = 4.0;
        /// <summary>Leak rate halves every this many game minutes (self-sealing).</summary>
        public static double LeakSealHalfLifeMinutes = 4.0;
        /// <summary>Exhaust speed (m/s) of venting gas; thrust = mass flow × this.</summary>
        public static double LeakVentSpeed = 800.0;
        /// <summary>Tier 2 partial battery short: chance, and the fraction of current charge it loses.</summary>
        public static double Tier2ShortChance = 0.2;
        public static double Tier2ShortMinLoss = 0.3;
        public static double Tier2ShortMaxLoss = 0.6;
        /// <summary>Tier 2 fixed panel damage: chance, and output lost per hit (gentler than tier 3).</summary>
        public static double Tier2SolarChance = 0.2;
        public static double Tier2SolarLossPerHit = 0.05;
        /// <summary>Tier 2 antenna blip: chance and game seconds.</summary>
        public static double Tier2SignalLossChance = 0.25;
        public static double Tier2SignalLossMinSeconds = 0.5;
        public static double Tier2SignalLossMaxSeconds = 2.0;

        // --- Tier 3 damage (one failure per hit, picked from what the struck part can suffer) ---
        public static double Tier3BreakChance = 0.6;
        public static double Tier3PunctureChance = 0.25;
        public static double Tier3LeakRateMinPctPerMin = 3.0;
        public static double Tier3LeakRateMaxPctPerMin = 8.0;
        public static double BatteryShortChance = 0.35;
        /// <summary>Fraction of capacity a shorted battery loses until repaired.</summary>
        public static double BatteryShortCapacityLoss = 0.3;
        public static double FuelCellChance = 0.4;
        public static double RcsChance = 0.4;
        public static double EngineChance = 0.4;
        public static double GeneratorChance = 0.3;
        public static double FixedSolarChance = 0.3;
        /// <summary>RTG / fixed panel output lost per hit (stacking), and the floor it stops at.</summary>
        public static double OutputLossPerHit = 0.15;
        public static double OutputFloor = 0.4;
        public static double SasChance = 0.5;
        public static double SignalLossChance = 0.4;
        /// <summary>Game seconds a fixed antenna drops out for.</summary>
        public static double SignalLossMinSeconds = 1.0;
        public static double SignalLossMaxSeconds = 7.0;

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
        /// <summary>Real seconds an impact mark takes to fade away.</summary>
        public static double MarkFadeSeconds = 30.0;

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
            Bool("WarpDropOut", "Encounters stop time warp",
                "On: tier 2/3 drop you to 1x and fields lock warp. Off: they play out mid-warp (no pushes or panel breaks while on rails)",
                () => WarpDropOut, v => WarpDropOut = v),
            Bool("DebrisDecayEnabled", "Debris decay",
                "Delete old debris after a lifetime based on periapsis height (a month low down, up to 200 years at the SOI edge)",
                () => DebrisDecayEnabled, v => DebrisDecayEnabled = v),
            Bool("ChanceEncounters", "Chance-based encounters",
                "On: roll a chance every check interval. Off: the older continuous per-hour rate",
                () => ChanceEncounters, v => ChanceEncounters = v),
            Bool("FieldVisualsEnabled", "Debris field visuals", "Specks drift past the ship during a field, denser at higher tiers",
                () => FieldVisualsEnabled, v => FieldVisualsEnabled = v),
            Bool("ImpactMarksEnabled", "Impact marks", "Hits leave a scorch mark that fades out",
                () => ImpactMarksEnabled, v => ImpactMarksEnabled = v),
            Dbl("DebrisWeightMultiplier", "Debris weight multiplier",
                "Density one orbiting debris vessel adds to the bands it sweeps",
                () => DebrisWeightMultiplier, v => DebrisWeightMultiplier = v, 0, 100),
            Dbl("ExplosionSpike", "Explosion spike",
                "Density added where a part is destroyed in orbit",
                () => ExplosionSpike, v => ExplosionSpike = v, 0, 100),
            Dbl("ExplosionHalfLifeDays", "Spike half-life (days)",
                "Game days for an explosion spike to fade to half",
                () => ExplosionHalfLifeDays, v => ExplosionHalfLifeDays = v, 0.01, 100000),
            Dbl("BreakSpike", "Break spike",
                "Density added where debris breaks a panel, antenna or radiator in orbit (fragments)",
                () => BreakSpike, v => BreakSpike = v, 0, 100),
            Dbl("Tier1At", "Tier 1 at density", "Sparse: micrometeorite pings",
                () => Tier1At, v => Tier1At = v, 0, 1e6),
            Dbl("Tier2At", "Tier 2 at density", "Dense: panels and antennas can break",
                () => Tier2At, v => Tier2At = v, 0, 1e6),
            Dbl("Tier3At", "Tier 3 at density", "Debris field: large impacts",
                () => Tier3At, v => Tier3At = v, 0, 1e6),
            Dbl("ChanceCheckSeconds", "Check interval (s)",
                "Chance mode: game seconds between encounter checks",
                () => ChanceCheckSeconds, v => ChanceCheckSeconds = v, 1, 3600),
            Dbl("ChanceBase", "Base chance per check",
                "Chance mode: chance (0-1) per check in any tier 1+ band, before density",
                () => ChanceBase, v => ChanceBase = v, 0, 1),
            Dbl("ChancePerDensity", "Chance per density",
                "Chance mode: added to the per-check chance for each unit of band density",
                () => ChancePerDensity, v => ChancePerDensity = v, 0, 1),
            Dbl("LowerTierChance", "Lower-tier chance",
                "Chance (0-1) an encounter is a lower tier than the band's, mostly the next tier down",
                () => LowerTierChance, v => LowerTierChance = v, 0, 1),
            Dbl("HitsPerHourPerDensity", "Hits/hour per density",
                "Per-hour mode (chance-based encounters off): average encounters per game hour = this x density",
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
            Dbl("Tier2BreakChance", "Break chance (tier 2)",
                "Chance (0-1) a tier 2+ hit on a panel, antenna or radiator breaks it",
                () => Tier2BreakChance, v => Tier2BreakChance = v, 0, 1),
            Dbl("Tier2PunctureChance", "Puncture chance (tier 2)",
                "Chance (0-1) a tier 2+ hit on a tank with fuel/gas punctures it",
                () => Tier2PunctureChance, v => Tier2PunctureChance = v, 0, 1),
            Dbl("LeakRateMinPctPerMin", "Leak rate min (%/min)", "Slowest starting leak, percent of the tank per minute",
                () => LeakRateMinPctPerMin, v => LeakRateMinPctPerMin = v, 0, 100),
            Dbl("LeakRateMaxPctPerMin", "Leak rate max (%/min)", "Fastest starting leak, percent of the tank per minute",
                () => LeakRateMaxPctPerMin, v => LeakRateMaxPctPerMin = v, 0, 100),
            Dbl("LeakSealHalfLifeMinutes", "Leak seal half-life (min)",
                "Game minutes for a leak to slow to half; total loss = rate x half-life x 1.44",
                () => LeakSealHalfLifeMinutes, v => LeakSealHalfLifeMinutes = v, 0.01, 1e5),
            Dbl("LeakVentSpeed", "Leak vent speed (m/s)", "Thrust from a leak = mass flow x this",
                () => LeakVentSpeed, v => LeakVentSpeed = v, 0, 5000),
            Dbl("Tier2ShortChance", "Partial short chance (tier 2)", "Chance (0-1) a tier 2 hit drains part of a battery's charge",
                () => Tier2ShortChance, v => Tier2ShortChance = v, 0, 1),
            Dbl("Tier2ShortMinLoss", "Partial short min loss", "Least fraction (0-1) of current charge a tier 2 short drains",
                () => Tier2ShortMinLoss, v => Tier2ShortMinLoss = v, 0, 1),
            Dbl("Tier2ShortMaxLoss", "Partial short max loss", "Most fraction (0-1) of current charge a tier 2 short drains",
                () => Tier2ShortMaxLoss, v => Tier2ShortMaxLoss = v, 0, 1),
            Dbl("Tier2SolarChance", "Fixed panel chance (tier 2)", "Chance (0-1) a tier 2 hit damages cells on a fixed solar panel",
                () => Tier2SolarChance, v => Tier2SolarChance = v, 0, 1),
            Dbl("Tier2SolarLossPerHit", "Fixed panel loss (tier 2)", "Output a tier 2 hit takes off a fixed panel (0-1)",
                () => Tier2SolarLossPerHit, v => Tier2SolarLossPerHit = v, 0, 1),
            Dbl("Tier2SignalLossChance", "Signal blip chance (tier 2)", "Chance (0-1) a tier 2 hit briefly drops out a fixed antenna",
                () => Tier2SignalLossChance, v => Tier2SignalLossChance = v, 0, 1),
            Dbl("Tier2SignalLossMinSeconds", "Signal blip min (s)", "Shortest tier 2 antenna dropout, game seconds",
                () => Tier2SignalLossMinSeconds, v => Tier2SignalLossMinSeconds = v, 0, 3600),
            Dbl("Tier2SignalLossMaxSeconds", "Signal blip max (s)", "Longest tier 2 antenna dropout, game seconds",
                () => Tier2SignalLossMaxSeconds, v => Tier2SignalLossMaxSeconds = v, 0, 3600),
            Dbl("Tier3BreakChance", "Break chance (tier 3)", "Chance (0-1) a tier 3 hit breaks a panel, antenna or radiator",
                () => Tier3BreakChance, v => Tier3BreakChance = v, 0, 1),
            Dbl("Tier3PunctureChance", "Puncture chance (tier 3)", "Chance (0-1) a tier 3 hit punctures a tank",
                () => Tier3PunctureChance, v => Tier3PunctureChance = v, 0, 1),
            Dbl("Tier3LeakRateMinPctPerMin", "Big leak min (%/min)", "Slowest starting tier 3 leak",
                () => Tier3LeakRateMinPctPerMin, v => Tier3LeakRateMinPctPerMin = v, 0, 100),
            Dbl("Tier3LeakRateMaxPctPerMin", "Big leak max (%/min)", "Fastest starting tier 3 leak",
                () => Tier3LeakRateMaxPctPerMin, v => Tier3LeakRateMaxPctPerMin = v, 0, 100),
            Dbl("BatteryShortChance", "Battery short chance", "Chance (0-1) a tier 3 hit shorts a part's battery (charge to 0)",
                () => BatteryShortChance, v => BatteryShortChance = v, 0, 1),
            Dbl("BatteryShortCapacityLoss", "Short capacity loss", "Fraction (0-1) of capacity a shorted battery loses until repaired",
                () => BatteryShortCapacityLoss, v => BatteryShortCapacityLoss = v, 0, 1),
            Dbl("FuelCellChance", "Fuel cell blowout chance", "Chance (0-1) a tier 3 hit blows out a fuel cell",
                () => FuelCellChance, v => FuelCellChance = v, 0, 1),
            Dbl("RcsChance", "RCS failure chance", "Chance (0-1) a tier 3 hit kills an RCS block",
                () => RcsChance, v => RcsChance = v, 0, 1),
            Dbl("EngineChance", "Engine flameout chance", "Chance (0-1) a tier 3 hit shuts down a running engine",
                () => EngineChance, v => EngineChance = v, 0, 1),
            Dbl("GeneratorChance", "RTG puncture chance", "Chance (0-1) a tier 3 hit cracks an RTG",
                () => GeneratorChance, v => GeneratorChance = v, 0, 1),
            Dbl("FixedSolarChance", "Fixed panel damage chance", "Chance (0-1) a tier 3 hit smashes cells on a fixed solar panel",
                () => FixedSolarChance, v => FixedSolarChance = v, 0, 1),
            Dbl("OutputLossPerHit", "Output loss per hit", "RTG / fixed panel output lost per hit (0-1), stacking",
                () => OutputLossPerHit, v => OutputLossPerHit = v, 0, 1),
            Dbl("OutputFloor", "Output floor", "RTG / fixed panel output never drops below this fraction (0-1)",
                () => OutputFloor, v => OutputFloor = v, 0, 1),
            Dbl("SasChance", "SAS knockout chance", "Chance (0-1) a tier 3 hit on a pod or probe core switches SAS off",
                () => SasChance, v => SasChance = v, 0, 1),
            Dbl("SignalLossChance", "Signal loss chance", "Chance (0-1) a tier 3 hit drops out a fixed antenna",
                () => SignalLossChance, v => SignalLossChance = v, 0, 1),
            Dbl("SignalLossMinSeconds", "Signal loss min (s)", "Shortest antenna dropout, game seconds",
                () => SignalLossMinSeconds, v => SignalLossMinSeconds = v, 0, 3600),
            Dbl("SignalLossMaxSeconds", "Signal loss max (s)", "Longest antenna dropout, game seconds",
                () => SignalLossMaxSeconds, v => SignalLossMaxSeconds = v, 0, 3600),
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
            Dbl("DecayMinMonths", "Shortest lifetime (months)",
                "Calendar months debris lasts with periapsis just above the atmosphere (or surface)",
                () => DecayMinMonths, v => DecayMinMonths = v, 0.01, 1e6),
            Dbl("DecayMaxYears", "Longest lifetime (years)",
                "Calendar years debris lasts with periapsis at the edge of the SOI",
                () => DecayMaxYears, v => DecayMaxYears = v, 0.01, 1e6),
            Dbl("DecayCurveRadii", "Lifetime curve (radii)",
                "Curve shape in body radii; smaller = lifetimes climb sooner above the floor",
                () => DecayCurveRadii, v => DecayCurveRadii = v, 0.001, 100),
            Dbl("DecayRandomness", "Decay randomness", "Each piece's lifetime is scaled by e^(+/- this)",
                () => DecayRandomness, v => DecayRandomness = v, 0, 5),
            Dbl("MarkFadeSeconds", "Impact mark fade (s)", "Seconds an impact mark takes to fade away",
                () => MarkFadeSeconds, v => MarkFadeSeconds = v, 0, 3600),
            Dbl("ScanIntervalSeconds", "Rescan interval (s)", "Real-time seconds between debris scans",
                () => ScanIntervalSeconds, v => ScanIntervalSeconds = (float)v, 0.5, 600),
            Int("BandsPerBody", "Bands per body", "Altitude bands generated around each body",
                () => BandsPerBody, v => BandsPerBody = v, 1, 64, true),
            Dbl("CeilingRadii", "Ceiling (body radii)", "Top of the band shell, capped at the SOI",
                () => CeilingRadii, v => CeilingRadii = v, 1.1, 1000, true),
            Dbl("BandGrowth", "Band growth", "Thickness ratio between consecutive bands (1 = uniform)",
                () => BandGrowth, v => BandGrowth = v, 1.0, 5.0, true),
        };

        /// <summary>Settings-tab sections, in display order.</summary>
        public static readonly string[] Sections =
            { "Density & tiers", "Encounters", "Debris fields", "Damage & leaks", "Tier 3 damage", "Debris decay", "Audio & visuals", "Bands & scanning" };

        static Settings()
        {
            Group("Density & tiers", "DebrisWeightMultiplier", "ExplosionSpike", "ExplosionHalfLifeDays", "BreakSpike",
                "Tier1At", "Tier2At", "Tier3At");
            Group("Encounters", "ChanceCheckSeconds", "ChanceBase", "ChancePerDensity", "LowerTierChance",
                "HitsPerHourPerDensity", "AlertCooldownSeconds", "Tier2Impulse", "Tier3Impulse");
            Group("Debris fields", "FieldChanceMax", "FieldHalfDensity", "FieldDurationMin", "FieldDurationMax",
                "FieldSecondsPerHit", "FieldPeltsPerSecond");
            Group("Damage & leaks", "Tier2BreakChance", "Tier2PunctureChance", "LeakRateMinPctPerMin",
                "LeakRateMaxPctPerMin", "LeakSealHalfLifeMinutes", "LeakVentSpeed", "Tier2ShortChance", "Tier2ShortMinLoss",
                "Tier2ShortMaxLoss", "Tier2SolarChance", "Tier2SolarLossPerHit", "Tier2SignalLossChance",
                "Tier2SignalLossMinSeconds", "Tier2SignalLossMaxSeconds");
            Group("Tier 3 damage", "Tier3BreakChance", "Tier3PunctureChance", "Tier3LeakRateMinPctPerMin",
                "Tier3LeakRateMaxPctPerMin", "BatteryShortChance", "BatteryShortCapacityLoss", "FuelCellChance",
                "RcsChance", "EngineChance", "GeneratorChance", "FixedSolarChance", "OutputLossPerHit", "OutputFloor",
                "SasChance", "SignalLossChance", "SignalLossMinSeconds", "SignalLossMaxSeconds");
            Group("Debris decay", "DecayMinMonths", "DecayMaxYears", "DecayCurveRadii", "DecayRandomness");
            Group("Audio & visuals", "PingVolume", "AlarmVolume", "PeltVolume", "MarkFadeSeconds");
            Group("Bands & scanning", "ScanIntervalSeconds", "BandsPerBody", "CeilingRadii", "BandGrowth");
        }

        private static void Group(string section, params string[] keys)
        {
            foreach (SettingDef d in Defs)
                if (Array.IndexOf(keys, d.Key) >= 0) d.Section = section;
        }

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

        /// <summary>Chance mode: chance (0-1) of an encounter per check at this density. Tier 0 bands: none.</summary>
        public static double CheckChance(double density)
        {
            if (TierFor(density) < 1) return 0;
            return Math.Min(1.0, ChanceBase + ChancePerDensity * density);
        }

        /// <summary>
        /// Tier of an encounter in a band of <paramref name="bandTier"/>: usually the band's own,
        /// with LowerTierChance of a lower one, weighted toward the next tier down (in a tier 3
        /// band, tier 2 is twice as likely as tier 1).
        /// </summary>
        public static int PickEncounterTier(int bandTier)
        {
            if (bandTier <= 1 || UnityEngine.Random.value >= LowerTierChance) return bandTier;
            // Weight tier k (1..bandTier-1) by k.
            int total = bandTier * (bandTier - 1) / 2;
            int roll = UnityEngine.Random.Range(0, total);
            for (int k = bandTier - 1; k >= 1; k--)
            {
                if (roll < k) return k;
                roll -= k;
            }
            return 1;
        }

        /// <summary>RTG / fixed panel output fraction after this many hits.</summary>
        public static double OutputFactor(int hits)
        {
            return Math.Max(OutputFloor, 1.0 - hits * OutputLossPerHit);
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
