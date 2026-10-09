// =============================================================================
//  ReleaseTools.cs  —  Week 8: check the project, then build the Mac app
// =============================================================================
//  Tools > Pokhara City > Check Project
//      Looks through your scene like a pre-flight checklist before a plane
//      takes off: missing scripts, things built twice, things not built yet,
//      camera set up, scene saved. It tells you exactly what to fix.
//
//  Tools > Pokhara City > Build Mac App
//      Runs the check, sets the game's name, version and icon, and builds
//      "Pokhara City Driving Simulator.app" into a Builds folder next to
//      Assets. Double-click it to play without Unity!
// =============================================================================

using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class ReleaseTools
{
    private const string ProductName = "Pokhara City Driving Simulator";
    private const string Version = "1.0.0";
    private const string BuildFolder = "Builds";

    // =========================================================================
    [MenuItem("Tools/Pokhara City/Check Project")]
    public static void CheckProjectMenu()
    {
        int problems;
        string report = CheckProject(out problems);
        Debug.Log("Pokhara project check:\n" + report);
        EditorUtility.DisplayDialog(problems == 0 ? "All good!" : problems + " thing(s) to fix", report, "OK");
    }

    // Returns a readable report; 'problems' counts the things that must be fixed.
    public static string CheckProject(out int problems)
    {
        problems = 0;
        var sb = new StringBuilder();
        Scene scene = SceneManager.GetActiveScene();

        // ---- 1. missing scripts (the yellow "referenced script is missing" warnings)
        var broken = new List<string>();
        foreach (GameObject root in scene.GetRootGameObjects())
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
                if (GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject) > 0)
                    broken.Add(TopName(t));
        var brokenTops = new HashSet<string>(broken);
        if (brokenTops.Count == 0) sb.AppendLine("OK  No missing scripts");
        else
        {
            problems++;
            sb.AppendLine("FIX Missing scripts in: " + string.Join(", ", new List<string>(brokenTops).ToArray()));
            sb.AppendLine("    Delete that object and run its builder again.");
        }

        // ---- 2. each main part: built once, not zero, not twice
        problems += CountCheck<PokharaCar>(sb, "Player car", "Create Player Car");
        problems += CountCheck<RoadNetwork>(sb, "City (road network)", "Import OSM Map");
        problems += CountCheck<RoadRulesJudge>(sb, "Road rules", "Add Road Rules");
        problems += CountCheck<TrafficManager>(sb, "Traffic", "Add Traffic");
        problems += CountCheck<MissionManager>(sb, "Missions", "Add Missions");
        problems += CountCheck<GameMenu>(sb, "Menus", "Add Menus and Minimap");
        problems += CountCheck<MountainBackdrop>(sb, "Mountains (props)", "Add Props");

        // ---- 3. camera
        Camera main = Camera.main;
        if (main == null) { problems++; sb.AppendLine("FIX No camera tagged 'MainCamera'"); }
        else if (main.GetComponent<CarCamera>() == null) { problems++; sb.AppendLine("FIX Main Camera has no CarCamera: run Create Player Car"); }
        else sb.AppendLine("OK  Main Camera follows the car");

        // ---- 4. scene saved
        if (string.IsNullOrEmpty(scene.path)) { problems++; sb.AppendLine("FIX The scene has never been saved (Cmd + S)"); }
        else if (scene.isDirty) sb.AppendLine("--  Unsaved changes: press Cmd + S before building");
        else sb.AppendLine("OK  Scene saved");

        return sb.ToString();
    }

    private static int CountCheck<T>(StringBuilder sb, string label, string builder) where T : Object
    {
        int n = Object.FindObjectsByType<T>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length;
        if (n == 1) { sb.AppendLine("OK  " + label); return 0; }
        if (n == 0) { sb.AppendLine("FIX " + label + " not built: Tools > Pokhara City > " + builder); return 1; }
        sb.AppendLine("FIX " + label + " is in the scene " + n + " times: delete the extra one(s)");
        return 1;
    }

    // The name of the top object in the Hierarchy (e.g. "Pokhara Traffic"),
    // which is what you delete and rebuild.
    private static string TopName(Transform t)
    {
        while (t.parent != null) t = t.parent;
        return t.name;
    }

    // =========================================================================
    [MenuItem("Tools/Pokhara City/Build Mac App")]
    public static void BuildMacApp()
    {
        // 1. Check first, like a pre-flight checklist.
        int problems;
        string report = CheckProject(out problems);
        if (problems > 0 && !EditorUtility.DisplayDialog("Fix these first?",
                report + "\nBuilding now may give a broken app.", "Build anyway", "Cancel"))
            return;

        // 2. Save the scene (the build uses the saved file).
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        Scene scene = SceneManager.GetActiveScene();
        if (string.IsNullOrEmpty(scene.path))
        {
            EditorUtility.DisplayDialog("Save the scene", "Save the scene first (Cmd + S), then try again.", "OK");
            return;
        }

        // 3. The app's name, maker, version and icon (what you see in Finder and the Dock).
        PlayerSettings.companyName = "Pasang Dorje Gurung";
        PlayerSettings.productName = ProductName;
        PlayerSettings.bundleVersion = Version;
        PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Standalone, "com.healergrg.pokharacity");
        PlayerSettings.fullScreenMode = FullScreenMode.FullScreenWindow;   // fills the screen; Cmd + Q to quit
        PlayerSettings.resizableWindow = true;
        Texture2D icon = FindTexture("PokharaIcon");
        if (icon != null) PlayerSettings.SetIcons(NamedBuildTarget.Unknown, new[] { icon }, IconKind.Any);

        // 4. Build!
        string projectFolder = Path.GetDirectoryName(Application.dataPath);
        string appPath = Path.Combine(projectFolder, BuildFolder, ProductName + ".app");
        var options = new BuildPlayerOptions
        {
            scenes = new[] { scene.path },
            locationPathName = appPath,
            target = BuildTarget.StandaloneOSX,
            options = BuildOptions.None
        };
        BuildReport result = BuildPipeline.BuildPlayer(options);

        if (result.summary.result == BuildResult.Succeeded)
        {
            float mb = result.summary.totalSize / (1024f * 1024f);
            EditorUtility.DisplayDialog("Your game is built!",
                ProductName + ".app (" + mb.ToString("0") + " MB)\n\n" +
                "It is in the Builds folder next to Assets (opening in Finder now).\n\n" +
                "First time only: RIGHT-click the app > Open > Open, because macOS does not know apps you make yourself yet.",
                "Great!");
            EditorUtility.RevealInFinder(appPath);
        }
        else
        {
            EditorUtility.DisplayDialog("Build failed",
                "Result: " + result.summary.result + "\n\nThe red errors in the Console say why. Send me a screenshot of them.", "OK");
        }
    }

    private static Texture2D FindTexture(string fileName)
    {
        foreach (string guid in AssetDatabase.FindAssets(fileName + " t:Texture2D"))
        {
            string p = AssetDatabase.GUIDToAssetPath(guid);
            if (Path.GetFileNameWithoutExtension(p) == fileName) return AssetDatabase.LoadAssetAtPath<Texture2D>(p);
        }
        return null;
    }
}
