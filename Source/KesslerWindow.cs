using System.Collections.Generic;
using ClickThroughFix;
using KSP.UI.Screens;
using ToolbarControl_NS;
using UnityEngine;

namespace KesslerSymptoms
{
    /// <summary>Registers the mod with ToolbarControl once, at game start.</summary>
    [KSPAddon(KSPAddon.Startup.MainMenu, true)]
    public class ToolbarRegistration : MonoBehaviour
    {
        public void Start()
        {
            ToolbarControl.RegisterMod(KesslerWindow.ModId, "Kessler Symptoms");
        }
    }

    /// <summary>
    /// Toolbar button + the mod's window, drawn with KSP's stock IMGUI skin. Tabs:
    /// Debug (per-band density for a chosen body), Effects (toggles and forced encounters)
    /// and Settings (edit and save settings.cfg).
    /// </summary>
    [KSPAddon(KSPAddon.Startup.AllGameScenes, false)]
    public class KesslerWindow : MonoBehaviour
    {
        public const string ModId = "KesslerSymptoms";

        private enum Tab { Debug, Effects, Settings }
        private static readonly string[] TabNames = { "Debug", "Effects", "Settings" };

        private ToolbarControl toolbar;
        private bool visible;
        private Tab tab = Tab.Debug;
        private Rect rect = new Rect(200, 120, 720, 0);
        private readonly int windowId = "KesslerSymptoms.Window".GetHashCode();

        // Debug tab
        private Vector2 debugScroll;
        private List<CelestialBody> bodies;
        private int bodyIndex;
        private int spikeBand;

        // Settings tab: text being edited, keyed by SettingDef.Key, applied on demand.
        // Refreshed from the live values each time the window opens.
        private readonly Dictionary<string, string> edits = new Dictionary<string, string>();
        private Vector2 settingsScroll;
        private string status = "";
        private string hoverHelp = "";
        private string damageStatus = "";

        // Styles are built from HighLogic.Skin on first OnGUI (they can't be made outside it).
        private GUIStyle headerStyle, cellStyle, badFieldStyle, changedLabelStyle, helpStyle;
        private GUIStyle[] tierStyles;

        public void Start()
        {
            GameScenes scene = HighLogic.LoadedScene;
            if (scene != GameScenes.FLIGHT && scene != GameScenes.TRACKSTATION && scene != GameScenes.SPACECENTER)
            {
                Destroy(this);
                return;
            }

            toolbar = gameObject.AddComponent<ToolbarControl>();
            toolbar.AddToAllToolbars(
                () => { visible = true; ResetEdits(); },
                () => visible = false,
                ApplicationLauncher.AppScenes.FLIGHT | ApplicationLauncher.AppScenes.MAPVIEW |
                ApplicationLauncher.AppScenes.TRACKSTATION | ApplicationLauncher.AppScenes.SPACECENTER,
                ModId, "KesslerSymptomsButton",
                "KesslerSymptoms/Textures/icon_38", "KesslerSymptoms/Textures/icon_24",
                "Kessler Symptoms");

            bodies = new List<CelestialBody>(FlightGlobals.Bodies);
            CelestialBody start = FlightGlobals.ActiveVessel != null
                ? FlightGlobals.ActiveVessel.mainBody
                : FlightGlobals.GetHomeBody();
            bodyIndex = Mathf.Max(0, bodies.IndexOf(start));
        }

        public void OnDestroy()
        {
            if (toolbar != null)
            {
                toolbar.OnDestroy();
                Destroy(toolbar);
            }
        }

        public void OnGUI()
        {
            if (!visible) return;

            GUISkin oldSkin = GUI.skin;
            GUI.skin = HighLogic.Skin;
            if (headerStyle == null) BuildStyles();

            rect = ClickThruBlocker.GUILayoutWindow(windowId, rect, DrawWindow, "Kessler Symptoms",
                GUILayout.Width(720));

            GUI.skin = oldSkin;
        }

        private void BuildStyles()
        {
            GUISkin skin = HighLogic.Skin;
            headerStyle = new GUIStyle(skin.label) { fontStyle = FontStyle.Bold };
            headerStyle.normal.textColor = Color.white;
            cellStyle = new GUIStyle(skin.label);

            Color[] tierColors = { Color.white, Color.yellow, new Color(1f, 0.55f, 0f), new Color(1f, 0.3f, 0.3f) };
            tierStyles = new GUIStyle[tierColors.Length];
            for (int i = 0; i < tierColors.Length; i++)
            {
                tierStyles[i] = new GUIStyle(cellStyle);
                tierStyles[i].normal.textColor = tierColors[i];
            }

            badFieldStyle = new GUIStyle(skin.textField);
            badFieldStyle.normal.textColor = badFieldStyle.focused.textColor = new Color(1f, 0.3f, 0.3f);
            changedLabelStyle = new GUIStyle(skin.label);
            changedLabelStyle.normal.textColor = Color.yellow;
            helpStyle = new GUIStyle(skin.label) { wordWrap = true, fontStyle = FontStyle.Italic };
        }

        private void DrawWindow(int id)
        {
            tab = (Tab)GUILayout.Toolbar((int)tab, TabNames);
            GUILayout.Space(4);

            KesslerScenario scn = KesslerScenario.Instance;
            if (tab == Tab.Debug) DrawDebug(scn);
            else if (tab == Tab.Effects) DrawEffects();
            else DrawSettings(scn);

            GUI.DragWindow();
        }

        // ---------------------------------------------------------------- Debug tab

        private void DrawDebug(KesslerScenario scn)
        {
            if (scn == null || bodies == null || bodies.Count == 0)
            {
                GUILayout.Label("Scenario not loaded.");
                return;
            }

            CelestialBody body = bodies[bodyIndex];
            BandSet set = scn.GetBands(body);

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("<", GUILayout.Width(30))) bodyIndex = (bodyIndex + bodies.Count - 1) % bodies.Count;
            GUILayout.Label(body.bodyName, headerStyle, GUILayout.Width(110));
            if (GUILayout.Button(">", GUILayout.Width(30))) bodyIndex = (bodyIndex + 1) % bodies.Count;
            GUILayout.FlexibleSpace();
            GUILayout.Label(string.Format("Debris in orbit: {0}   Spikes: {1}   Decayed (session): {2}",
                scn.LastScanDebrisCount, scn.SpikeCount, scn.DecayedThisSession));
            GUILayout.EndHorizontal();

            int activeBand = -1;
            Vessel av = FlightGlobals.ActiveVessel;
            if (av != null && av.mainBody == body)
                activeBand = set.IndexOf(av.altitude + body.Radius);

            GUILayout.BeginHorizontal();
            Cell("#", 30, headerStyle); Cell("Altitude (km)", 150, headerStyle); Cell("Debris", 60, headerStyle);
            Cell("Weight", 70, headerStyle); Cell("Spike", 70, headerStyle); Cell("Density", 80, headerStyle);
            Cell("Tier", 40, headerStyle); Cell("Lifetime", 80, headerStyle);
            GUILayout.EndHorizontal();

            debugScroll = GUILayout.BeginScrollView(debugScroll, GUILayout.Height(280));
            for (int i = 0; i < set.Count; i++)
            {
                double density = set.Density(i);
                int tier = Settings.TierFor(density);
                GUIStyle style = tierStyles[tier];

                GUILayout.BeginHorizontal();
                Cell((i == activeBand ? "> " : "") + i, 30, style);
                Cell(string.Format("{0:N0} - {1:N0}", set.InnerAltitude(i) / 1000, set.OuterAltitude(i) / 1000), 150, style);
                Cell(set.DebrisCount[i].ToString(), 60, style);
                Cell(set.DebrisWeight[i].ToString("F2"), 70, style);
                Cell(set.Spike[i].ToString("F2"), 70, style);
                Cell(density.ToString("F2"), 80, style);
                Cell(tier.ToString(), 40, style);
                Cell(Decay.Format(Decay.LifetimeSeconds(body, set.Inner[i])), 80, style);
                GUILayout.EndHorizontal();
            }
            GUILayout.EndScrollView();

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Rescan now")) scn.Rescan();
            GUILayout.EndHorizontal();

            // Debug: inject an explosion spike into a band without blowing anything up.
            GUILayout.BeginHorizontal();
            GUILayout.Label("Test spike in band", GUILayout.Width(140));
            if (GUILayout.Button("-", GUILayout.Width(25))) spikeBand = Mathf.Max(0, spikeBand - 1);
            spikeBand = Mathf.Clamp(spikeBand, 0, set.Count - 1);
            GUILayout.Label(spikeBand.ToString(), GUILayout.Width(25));
            if (GUILayout.Button("+", GUILayout.Width(25))) spikeBand = Mathf.Min(set.Count - 1, spikeBand + 1);
            if (GUILayout.Button("+" + Settings.ExplosionSpike))
            {
                scn.AddSpike(body, (set.Inner[spikeBand] + set.Outer[spikeBand]) / 2, Settings.ExplosionSpike);
                scn.Rescan();
            }
            if (GUILayout.Button("-" + Settings.ExplosionSpike))
            {
                scn.ReduceSpikes(body, set.Inner[spikeBand], set.Outer[spikeBand], Settings.ExplosionSpike);
                scn.Rescan();
            }
            if (GUILayout.Button("Reset band"))
            {
                scn.ReduceSpikes(body, set.Inner[spikeBand], set.Outer[spikeBand], double.PositiveInfinity);
                scn.Rescan();
            }
            GUILayout.EndHorizontal();
        }

        private static void Cell(string text, float width, GUIStyle style)
        {
            GUILayout.Label(text, style, GUILayout.Width(width));
        }

        // ---------------------------------------------------------------- Effects tab

        private void DrawEffects()
        {
            GUILayout.Label("Toggles", headerStyle);
            foreach (SettingDef d in Settings.Defs)
            {
                if (!d.IsToggle) continue;
                bool on = d.Get() == "True";
                bool now = GUILayout.Toggle(on, new GUIContent(" " + d.Label, d.Help));
                if (now != on)
                {
                    d.TrySet(now ? "True" : "False");
                    edits[d.Key] = d.Get();
                    Settings.Save();
                }
            }
            GUILayout.Label("Toggles save to settings.cfg immediately. Debris tracking always runs.", helpStyle);
            if (GUILayout.Button("Test alarm", GUILayout.Width(120))) Encounters.PlayAlarm();

            GUILayout.Space(8);
            GUILayout.Label("Active vessel", headerStyle);
            if (HighLogic.LoadedScene != GameScenes.FLIGHT)
            {
                GUILayout.Label("Not in flight.");
            }
            else if (EncounterScheduler.CurrentBand < 0)
            {
                GUILayout.Label("Outside all debris bands.");
            }
            else
            {
                int tier = EncounterScheduler.CurrentTier;
                GUILayout.Label(string.Format("Band {0}, density {1:F2}, tier {2} ({3})",
                    EncounterScheduler.CurrentBand, EncounterScheduler.CurrentDensity,
                    tier, Encounters.TierNames[tier]), tierStyles[tier]);
                GUILayout.Label(EncounterScheduler.Rolling
                    ? string.Format("Rolling for encounters: ~{0:F1} per game hour, {1:P0} chance each is a field",
                        EncounterScheduler.CurrentHitsPerHour, Settings.FieldChance(EncounterScheduler.CurrentDensity))
                    : "Not rolling for encounters (clear band, tier disabled, or on rails).");
            }
            EncounterScheduler sched = EncounterScheduler.Instance;
            if (sched != null && sched.Field != null)
            {
                GUILayout.Label(string.Format("In a tier {0} debris field: {1:F0} s left",
                    sched.Field.Tier, sched.Field.EndUT - Planetarium.GetUniversalTime()), tierStyles[sched.Field.Tier]);
            }

            GUILayout.Space(8);
            GUILayout.Label("Force an encounter on the active vessel", headerStyle);
            string blocker = Encounters.Blocker(FlightGlobals.ActiveVessel);
            GUI.enabled = blocker == null;
            GUILayout.BeginHorizontal();
            GUILayout.Label("One-off", GUILayout.Width(70));
            for (int tier = 1; tier <= 3; tier++)
            {
                if (GUILayout.Button("Tier " + tier) && sched != null)
                    sched.Request(tier, false, true);
            }
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            GUILayout.Label("Field", GUILayout.Width(70));
            for (int tier = 1; tier <= 3; tier++)
            {
                if (GUILayout.Button("Tier " + tier) && sched != null)
                    sched.Request(tier, true, true);
            }
            GUI.enabled = sched != null && sched.Field != null;
            if (GUILayout.Button("End field", GUILayout.Width(90))) sched.StopField();
            GUI.enabled = blocker == null;
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            GUILayout.Label("Damage", GUILayout.Width(70));
            if (GUILayout.Button("Break a panel/antenna"))
                damageStatus = Damage.ForceBreak(FlightGlobals.ActiveVessel);
            if (GUILayout.Button("Puncture a tank"))
                damageStatus = Damage.ForcePuncture(FlightGlobals.ActiveVessel);
            GUILayout.EndHorizontal();
            if (damageStatus.Length > 0) GUILayout.Label(damageStatus, helpStyle);
            GUI.enabled = true;
            GUILayout.Label(blocker != null
                ? "Unavailable: " + blocker + "."
                : "Forced encounters ignore the tier toggles. In rails warp, tier 2/3 drop you to 1x first.", helpStyle);
        }

        // ---------------------------------------------------------------- Settings tab

        private void DrawSettings(KesslerScenario scn)
        {
            settingsScroll = GUILayout.BeginScrollView(settingsScroll, GUILayout.Height(300));
            foreach (SettingDef d in Settings.Defs)
            {
                if (d.IsToggle) continue;
                string text = edits[d.Key];
                bool changed = text != d.Get();

                GUILayout.BeginHorizontal();
                GUILayout.Label(new GUIContent((changed ? "* " : "") + d.Label, d.Help),
                    changed ? changedLabelStyle : cellStyle, GUILayout.Width(230));
                edits[d.Key] = GUILayout.TextField(text, d.IsValid(text) ? GUI.skin.textField : badFieldStyle,
                    GUILayout.Width(110));
                GUILayout.Label(new GUIContent("default " + d.Default, d.Help), GUILayout.Width(130));
                if (d.AffectsBands) GUILayout.Label(new GUIContent("(rebuilds bands)", d.Help));
                GUILayout.EndHorizontal();
            }
            GUILayout.EndScrollView();

            // Hovered row's help text. GUI.tooltip is only filled in during Repaint, so showing it
            // directly would make the Layout and Repaint passes disagree about this label's size
            // (it got squeezed into a one-letter-wide column). Latch it on Repaint and show it from
            // the next frame on, in a fixed-height box.
            GUILayout.Label(hoverHelp, helpStyle, GUILayout.Height(40), GUILayout.ExpandWidth(true));
            if (Event.current.type == EventType.Repaint) hoverHelp = GUI.tooltip;

            GUILayout.BeginHorizontal();
            if (GUILayout.Button(new GUIContent("Apply", "Use these values now, without saving")))
                Apply(scn, false);
            if (GUILayout.Button(new GUIContent("Apply + Save", "Use these values and write settings.cfg")))
                Apply(scn, true);
            if (GUILayout.Button(new GUIContent("Revert", "Discard edits and reload settings.cfg")))
            {
                Settings.Load();
                ResetEdits();
                if (scn != null) scn.RebuildBands();
                status = "Reloaded settings.cfg";
            }
            if (GUILayout.Button(new GUIContent("Defaults", "Fill in the defaults (still needs Apply)")))
            {
                foreach (SettingDef d in Settings.Defs)
                    if (!d.IsToggle) edits[d.Key] = d.Default;
                status = "Defaults filled in; Apply to use them";
            }
            GUILayout.EndHorizontal();

            GUILayout.Label(status);
        }

        private void ResetEdits()
        {
            foreach (SettingDef d in Settings.Defs)
                edits[d.Key] = d.Get();
        }

        private void Apply(KesslerScenario scn, bool save)
        {
            bool bandsChanged = false;
            int bad = 0;
            foreach (SettingDef d in Settings.Defs)
            {
                if (d.IsToggle) continue;
                string before = d.Get();
                if (!d.TrySet(edits[d.Key]))
                {
                    bad++;
                    continue;
                }
                if (d.AffectsBands && d.Get() != before) bandsChanged = true;
                edits[d.Key] = d.Get(); // show the clamped value
            }

            if (scn != null)
            {
                if (bandsChanged) scn.RebuildBands();
                else scn.Rescan();
            }
            if (save) Settings.Save();

            status = (save ? "Applied and saved" : "Applied (not saved)")
                + (bad > 0 ? string.Format("; {0} invalid field(s) skipped", bad) : "");
        }
    }
}
