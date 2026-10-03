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
    /// Per-save hub: owns the band sets, rescans debris periodically, records explosion
    /// spikes and persists them.
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
        private float nextScan;

        public double LastScanUT { get; private set; }
        public int LastScanDebrisCount { get; private set; }
        public int SpikeCount { get { return spikes.Count; } }

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
            foreach (Vessel v in FlightGlobals.Vessels)
            {
                if (v == null || v.vesselType != VesselType.Debris) continue;
                if (v.situation != Vessel.Situations.ORBITING) continue;
                Orbit o = v.orbit;
                if (o == null || o.referenceBody == null || o.eccentricity >= 1.0) continue;

                GetBands(o.referenceBody).AddOrbit(o.PeR, o.ApR, 1.0);
                debris++;
            }

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
            nextScan = 0f; // rescan on the next frame with the loaded spikes
        }
    }
}
