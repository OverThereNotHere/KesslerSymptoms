using System;
using System.Collections.Generic;
using UnityEngine;

namespace KesslerSymptoms
{
    /// <summary>A part destroyed in orbit, remembered so it can raise local density for a while.</summary>
    public class ExplosionSpike
    {
        public string BodyName;
        /// <summary>Distance from the body's centre where the part died.</summary>
        public double Radius;
        public double Magnitude;
        public double UT;

        public double ValueAt(double now, double halfLifeSeconds)
        {
            return Magnitude * Math.Pow(0.5, (now - UT) / halfLifeSeconds);
        }
    }

    /// <summary>
    /// Per-save hub: owns the band sets, rescans debris periodically, deletes debris that has
    /// outlived its orbital lifetime, records explosion spikes, and persists spikes plus when
    /// each piece of debris was first seen.
    /// </summary>
    [KSPScenario(ScenarioCreationOptions.AddToAllGames,
        GameScenes.FLIGHT, GameScenes.TRACKSTATION, GameScenes.SPACECENTER)]
    public class KesslerScenario : ScenarioModule
    {
        public static KesslerScenario Instance { get; private set; }

        /// <summary>Spikes below this are dropped.</summary>
        private const double SpikePruneBelow = 0.01;

        private static bool settingsLoaded;

        private readonly Dictionary<string, BandSet> bands = new Dictionary<string, BandSet>();
        private readonly List<ExplosionSpike> spikes = new List<ExplosionSpike>();
        /// <summary>Debris persistentId → UT it was first seen; its age for decay.</summary>
        private readonly Dictionary<uint, double> firstSeen = new Dictionary<uint, double>();
        private readonly List<Vessel> expired = new List<Vessel>();
        /// <summary>Set while we delete debris, so its destruction doesn't count as an explosion.</summary>
        private bool deleting;
        private float nextScan;

        public double LastScanUT { get; private set; }
        public int LastScanDebrisCount { get; private set; }
        public int SpikeCount { get { return spikes.Count; } }
        public int DecayedThisSession { get; private set; }

        public override void OnAwake()
        {
            Instance = this;
            if (!settingsLoaded)
            {
                Settings.Load();
                settingsLoaded = true;
            }
            GameEvents.onPartDie.Add(OnPartDie);
        }

        public void OnDestroy()
        {
            GameEvents.onPartDie.Remove(OnPartDie);
            if (Instance == this) Instance = null;
        }

        public void Update()
        {
            if (Time.realtimeSinceStartup < nextScan) return;
            nextScan = Time.realtimeSinceStartup + Settings.ScanIntervalSeconds;
            Rescan();
        }

        /// <summary>Band set for a body, built on first use.</summary>
        public BandSet GetBands(CelestialBody body)
        {
            BandSet set;
            if (!bands.TryGetValue(body.bodyName, out set))
            {
                set = new BandSet(body, Settings.BandsPerBody, Settings.CeilingRadii, Settings.BandGrowth);
                bands[body.bodyName] = set;
            }
            return set;
        }

        /// <summary>Throw away band geometry (e.g. after a settings reload) and rescan.</summary>
        public void RebuildBands()
        {
            bands.Clear();
            Rescan();
        }

        public void Rescan()
        {
            double now = Planetarium.GetUniversalTime();
            foreach (BandSet set in bands.Values)
            {
                set.ClearDebris();
                Array.Clear(set.Spike, 0, set.Spike.Length);
            }

            int debris = 0;
            expired.Clear();
            foreach (Vessel v in FlightGlobals.Vessels)
            {
                if (v == null || v.vesselType != VesselType.Debris) continue;
                if (v.situation != Vessel.Situations.ORBITING) continue;
                Orbit o = v.orbit;
                if (o == null || o.referenceBody == null || o.eccentricity >= 1.0) continue;

                double seen;
                if (!firstSeen.TryGetValue(v.persistentId, out seen))
                {
                    seen = now;
                    firstSeen[v.persistentId] = now;
                }
                if (Settings.DebrisDecayEnabled && CanDelete(v) &&
                    now - seen > Decay.LifetimeSeconds(o.referenceBody, o.PeR) * Decay.RandomFactor(v.persistentId))
                {
                    expired.Add(v);
                    continue;
                }

                GetBands(o.referenceBody).AddOrbit(o.PeR, o.ApR, 1.0);
                debris++;
            }
            DeleteExpired(now);

            double halfLife = Settings.ExplosionHalfLifeDays * KSPUtil.dateTimeFormatter.Day;
            spikes.RemoveAll(s => s.ValueAt(now, halfLife) < SpikePruneBelow);
            foreach (ExplosionSpike s in spikes)
            {
                CelestialBody body = FlightGlobals.GetBodyByName(s.BodyName);
                if (body == null) continue;
                BandSet set = GetBands(body);
                int i = set.IndexOf(s.Radius);
                if (i >= 0) set.Spike[i] += s.ValueAt(now, halfLife);
            }

            LastScanUT = now;
            LastScanDebrisCount = debris;
        }

        /// <summary>
        /// Only debris the player can't see go: unloaded (not near the active vessel), and not
        /// the active vessel or the current target.
        /// </summary>
        private static bool CanDelete(Vessel v)
        {
            if (v.loaded || v == FlightGlobals.ActiveVessel) return false;
            ITargetable target = FlightGlobals.fetch != null ? FlightGlobals.fetch.VesselTarget : null;
            return target == null || target.GetVessel() != v;
        }

        private void DeleteExpired(double now)
        {
            if (expired.Count == 0) return;
            deleting = true;
            try
            {
                foreach (Vessel v in expired)
                {
                    double seen = firstSeen[v.persistentId];
                    Log.Info(string.Format("Debris '{0}' around {1} decayed after {2} (Pe {3:N0} m)",
                        v.vesselName, v.mainBody.bodyName, Decay.Format(now - seen), v.orbit.PeA));
                    firstSeen.Remove(v.persistentId);
                    v.Die();
                    DecayedThisSession++;
                }
            }
            finally
            {
                deleting = false;
                expired.Clear();
            }
        }

        public void AddSpike(CelestialBody body, double radius, double magnitude)
        {
            spikes.Add(new ExplosionSpike
            {
                BodyName = body.bodyName,
                Radius = radius,
                Magnitude = magnitude,
                UT = Planetarium.GetUniversalTime(),
            });
        }

        /// <summary>
        /// Take up to <paramref name="amount"/> of current spike value out of the spikes lying
        /// in [inner, outer) around a body, newest first. Pass PositiveInfinity to clear them.
        /// </summary>
        public void ReduceSpikes(CelestialBody body, double inner, double outer, double amount)
        {
            double now = Planetarium.GetUniversalTime();
            double halfLife = Settings.ExplosionHalfLifeDays * KSPUtil.dateTimeFormatter.Day;
            for (int i = spikes.Count - 1; i >= 0 && amount > 0; i--)
            {
                ExplosionSpike s = spikes[i];
                if (s.BodyName != body.bodyName || s.Radius < inner || s.Radius >= outer) continue;

                double value = s.ValueAt(now, halfLife);
                if (value <= amount)
                {
                    spikes.RemoveAt(i);
                }
                else
                {
                    s.Magnitude *= (value - amount) / value;
                }
                amount -= value;
            }
        }

        private void OnPartDie(Part p)
        {
            if (deleting) return;
            Vessel v = p != null ? p.vessel : null;
            if (v == null || v.situation != Vessel.Situations.ORBITING) return;

            CelestialBody body = v.mainBody;
            double radius = (p.transform.position - body.position).magnitude;
            AddSpike(body, radius, Settings.ExplosionSpike);
            Log.Info(string.Format("Part {0} destroyed in orbit of {1} at {2:F0} m; spike added",
                p.partInfo != null ? p.partInfo.name : p.name, body.bodyName, radius - body.Radius));
        }

        public override void OnSave(ConfigNode node)
        {
            foreach (ExplosionSpike s in spikes)
            {
                ConfigNode n = node.AddNode("SPIKE");
                n.AddValue("body", s.BodyName);
                n.AddValue("radius", s.Radius);
                n.AddValue("magnitude", s.Magnitude);
                n.AddValue("ut", s.UT);
            }

            // First-seen times, only for debris that still exists.
            HashSet<uint> alive = new HashSet<uint>();
            foreach (Vessel v in FlightGlobals.Vessels)
                if (v != null) alive.Add(v.persistentId);
            ConfigNode ages = node.AddNode("DEBRIS_SEEN");
            foreach (KeyValuePair<uint, double> kv in firstSeen)
                if (alive.Contains(kv.Key))
                    ages.AddValue("v", kv.Key + " " + kv.Value.ToString("R", System.Globalization.CultureInfo.InvariantCulture));
        }

        public override void OnLoad(ConfigNode node)
        {
            spikes.Clear();
            foreach (ConfigNode n in node.GetNodes("SPIKE"))
            {
                ExplosionSpike s = new ExplosionSpike();
                s.BodyName = n.GetValue("body");
                if (string.IsNullOrEmpty(s.BodyName)) continue;
                n.TryGetValue("radius", ref s.Radius);
                n.TryGetValue("magnitude", ref s.Magnitude);
                n.TryGetValue("ut", ref s.UT);
                spikes.Add(s);
            }
            firstSeen.Clear();
            ConfigNode ages = node.GetNode("DEBRIS_SEEN");
            if (ages != null)
            {
                foreach (string entry in ages.GetValues("v"))
                {
                    string[] parts = entry.Split(' ');
                    uint id;
                    double ut;
                    if (parts.Length == 2 && uint.TryParse(parts[0], out id) &&
                        double.TryParse(parts[1], System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture, out ut))
                        firstSeen[id] = ut;
                }
            }

            nextScan = 0f; // rescan on the next frame with the loaded state
        }
    }
}
