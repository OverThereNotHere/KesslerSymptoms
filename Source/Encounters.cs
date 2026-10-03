using System.Collections.Generic;

namespace KesslerSymptoms
{
    /// <summary>What a tier does to a vessel when an encounter happens.</summary>
    public interface IEncounterEffect
    {
        /// <summary>Apply the encounter and return a short description of what happened.</summary>
        string Apply(Vessel vessel, int tier);
    }

    /// <summary>Placeholder until a tier gets real effects: just announces itself.</summary>
    public class DummyEffect : IEncounterEffect
    {
        public string Apply(Vessel vessel, int tier)
        {
            ScreenMessages.PostScreenMessage(
                string.Format("Kessler Symptoms: tier {0} debris encounter (no effects yet)", tier),
                4f, ScreenMessageStyle.UPPER_CENTER);
            return "dummy effect";
        }
    }

    /// <summary>
    /// Entry point for encounters. Holds one effect per tier and decides whether an encounter
    /// is allowed. The future hit scheduler and the Effects tab's force buttons both call Trigger.
    /// </summary>
    public static class Encounters
    {
        private static readonly Dictionary<int, IEncounterEffect> effects = new Dictionary<int, IEncounterEffect>
        {
            { 1, new DummyEffect() },
            { 2, new DummyEffect() },
            { 3, new DummyEffect() },
        };

        public static void Register(int tier, IEncounterEffect effect)
        {
            effects[tier] = effect;
        }

        /// <summary>Why an encounter can't happen to this vessel right now, or null if it can.</summary>
        public static string Blocker(Vessel vessel)
        {
            if (HighLogic.LoadedScene != GameScenes.FLIGHT) return "only in flight";
            if (vessel == null) return "no active vessel";
            if (!vessel.loaded || vessel.packed) return "vessel is on rails";
            return null;
        }

        /// <summary>
        /// Run a tier's effect on the vessel. Forced encounters (debug buttons) skip the effect
        /// toggles but still require a loaded, off-rails vessel in flight.
        /// </summary>
        public static bool Trigger(Vessel vessel, int tier, bool forced)
        {
            string blocker = Blocker(vessel);
            if (blocker != null)
            {
                Log.Info(string.Format("Tier {0} encounter skipped: {1}", tier, blocker));
                return false;
            }
            if (!forced && !Settings.TierEnabled(tier)) return false;

            IEncounterEffect effect;
            if (!effects.TryGetValue(tier, out effect)) return false;

            string result = effect.Apply(vessel, tier);
            Log.Info(string.Format("Tier {0} encounter on {1}{2}: {3}",
                tier, vessel.vesselName, forced ? " (forced)" : "", result));
            return true;
        }
    }
}
