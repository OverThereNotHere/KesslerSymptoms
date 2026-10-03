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
            ToolbarControl.RegisterMod(DebugWindow.ModId, "Kessler Symptoms");
        }
    }

    /// <summary>Toolbar button + diagnostics window showing per-band debris density.</summary>
    [KSPAddon(KSPAddon.Startup.AllGameScenes, false)]
    public class DebugWindow : MonoBehaviour
    {
        public const string ModId = "KesslerSymptoms";

        private ToolbarControl toolbar;
        private bool visible;
        private Rect rect = new Rect(200, 120, 620, 420);
        private Vector2 scroll;
        private readonly int windowId = "KesslerSymptoms.DebugWindow".GetHashCode();

        private List<CelestialBody> bodies;
        private int bodyIndex;
        private int spikeBand;

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
                () => visible = true,
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
            rect = ClickThruBlocker.GUILayoutWindow(windowId, rect, DrawWindow, "Kessler Symptoms - Diagnostics");
        }

        private void DrawWindow(int id)
        {
            KesslerScenario scn = KesslerScenario.Instance;
            if (scn == null || bodies == null || bodies.Count == 0)
            {
                GUILayout.Label("Scenario not loaded.");
                GUI.DragWindow();
                return;
            }

            CelestialBody body = bodies[bodyIndex];
            BandSet set = scn.GetBands(body);

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("<", GUILayout.Width(30))) bodyIndex = (bodyIndex + bodies.Count - 1) % bodies.Count;
            GUILayout.Label(body.bodyName, GUILayout.Width(120));
            if (GUILayout.Button(">", GUILayout.Width(30))) bodyIndex = (bodyIndex + 1) % bodies.Count;
            GUILayout.FlexibleSpace();
            GUILayout.Label(string.Format("Orbiting debris (all bodies): {0}   Spikes: {1}",
                scn.LastScanDebrisCount, scn.SpikeCount));
            GUILayout.EndHorizontal();

            int activeBand = -1;
            Vessel av = FlightGlobals.ActiveVessel;
            if (av != null && av.mainBody == body)
                activeBand = set.IndexOf(av.altitude + body.Radius);

            GUILayout.BeginHorizontal();
            Header("#", 30); Header("Altitude (km)", 150); Header("Debris", 60);
            Header("Weight", 70); Header("Spike", 70); Header("Density", 80); Header("Tier", 40);
            GUILayout.EndHorizontal();

            scroll = GUILayout.BeginScrollView(scroll, GUILayout.Height(260));
            for (int i = 0; i < set.Count; i++)
            {
                double density = set.Density(i);
                int tier = Settings.TierFor(density);
                GUI.contentColor = TierColor(tier);

                GUILayout.BeginHorizontal();
                Cell((i == activeBand ? ">" : "") + i, 30);
                Cell(string.Format("{0:N0} - {1:N0}", set.InnerAltitude(i) / 1000, set.OuterAltitude(i) / 1000), 150);
                Cell(set.DebrisCount[i].ToString(), 60);
                Cell(set.DebrisWeight[i].ToString("F2"), 70);
                Cell(set.Spike[i].ToString("F2"), 70);
                Cell(density.ToString("F2"), 80);
                Cell(tier.ToString(), 40);
                GUILayout.EndHorizontal();
            }
            GUI.contentColor = Color.white;
            GUILayout.EndScrollView();

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Rescan now")) scn.Rescan();
            if (GUILayout.Button("Reload settings"))
            {
                Settings.Load();
                scn.RebuildBands();
            }
            GUILayout.EndHorizontal();

            // Debug: inject an explosion spike into a band without blowing anything up.
            GUILayout.BeginHorizontal();
            GUILayout.Label("Test spike in band", GUILayout.Width(130));
            if (GUILayout.Button("-", GUILayout.Width(25))) spikeBand = Mathf.Max(0, spikeBand - 1);
            spikeBand = Mathf.Clamp(spikeBand, 0, set.Count - 1);
            GUILayout.Label(spikeBand.ToString(), GUILayout.Width(25));
            if (GUILayout.Button("+", GUILayout.Width(25))) spikeBand = Mathf.Min(set.Count - 1, spikeBand + 1);
            if (GUILayout.Button("Add spike (" + Settings.ExplosionSpike + ")"))
            {
                scn.AddSpike(body, (set.Inner[spikeBand] + set.Outer[spikeBand]) / 2, Settings.ExplosionSpike);
                scn.Rescan();
            }
            GUILayout.EndHorizontal();

            GUI.DragWindow();
        }

        private static void Header(string text, float width)
        {
            GUILayout.Label("<b>" + text + "</b>", GUILayout.Width(width));
        }

        private static void Cell(string text, float width)
        {
            GUILayout.Label(text, GUILayout.Width(width));
        }

        private static Color TierColor(int tier)
        {
            switch (tier)
            {
                case 1: return Color.yellow;
                case 2: return new Color(1f, 0.55f, 0f);
                case 3: return Color.red;
                default: return Color.white;
            }
        }
    }
}
