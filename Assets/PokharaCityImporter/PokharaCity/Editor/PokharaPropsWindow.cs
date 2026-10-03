// =============================================================================
//  PokharaPropsWindow.cs  —  Week 2: Tools > Pokhara City > Add Props
// =============================================================================
//  1. Build the city first (Tools > Pokhara City > Import OSM Map).
//  2. Open this window, tick what you want, click "Add Props".
//
//  It uses the same map.osm file you picked in the city importer.
//  Everything goes into ONE object, "Pokhara Props (generated)". Clicking
//  the button again replaces it, so you can try different settings freely.
//  Your city and your car are not touched.
// =============================================================================

using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

public class PokharaPropsWindow : EditorWindow
{
    private const string RootName = "Pokhara Props (generated)";
    private const string MeshAssetPath = "Assets/PokharaCity/Generated/PokharaPropsMeshes.asset";
    private const string PathPrefKey = "PokharaCity.OsmPath";   // same key as the city importer

    private string osmPath = "";
    private PropsSettings settings = new PropsSettings();
    private Vector2 scroll;

    [MenuItem("Tools/Pokhara City/Add Props (trees, poles, lake barrier, mountains)")]
    public static void Open()
    {
        var window = GetWindow<PokharaPropsWindow>("Pokhara Props");
        window.minSize = new Vector2(360, 420);
    }

    private void OnEnable()
    {
        osmPath = EditorPrefs.GetString(PathPrefKey, "");
    }

    private void OnGUI()
    {
        scroll = EditorGUILayout.BeginScrollView(scroll);

        EditorGUILayout.LabelField("1. Map file", EditorStyles.boldLabel);
        EditorGUILayout.LabelField(string.IsNullOrEmpty(osmPath) ? "No file chosen yet" : Path.GetFileName(osmPath), EditorStyles.wordWrappedLabel);
        if (GUILayout.Button("Choose .osm file"))
        {
            string picked = EditorUtility.OpenFilePanel("Choose the same map.osm you used for the city", "", "osm,xml,");
            if (!string.IsNullOrEmpty(picked))
            {
                osmPath = picked;
                EditorPrefs.SetString(PathPrefKey, osmPath);
            }
        }

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("2. What to add", EditorStyles.boldLabel);
        settings.lakeBarrier = EditorGUILayout.Toggle("Lake railing + barrier", settings.lakeBarrier);
        settings.kerbs = EditorGUILayout.Toggle("Kerbs on main roads", settings.kerbs);
        settings.kerbsSolid = EditorGUILayout.Toggle("  Car bumps over kerbs", settings.kerbsSolid);
        settings.poles = EditorGUILayout.Toggle("Electric poles", settings.poles);
        settings.wires = EditorGUILayout.Toggle("  Wires between poles", settings.wires);
        settings.trees = EditorGUILayout.Toggle("Trees", settings.trees);
        settings.maxTrees = EditorGUILayout.IntSlider("  Most trees", settings.maxTrees, 500, 20000);
        settings.mountains = EditorGUILayout.Toggle("Machhapuchhre + Annapurna", settings.mountains);

        EditorGUILayout.Space();
        GUI.enabled = File.Exists(osmPath);
        if (GUILayout.Button("Add Props", GUILayout.Height(36))) BuildProps();
        GUI.enabled = true;

        EditorGUILayout.HelpBox("Slow computer? Lower 'Most trees'. Trees are the biggest part.", MessageType.Info);
        EditorGUILayout.EndScrollView();
    }

    private void BuildProps()
    {
        try
        {
            EditorUtility.DisplayProgressBar("Pokhara Props", "Reading map file...", 0.1f);
            OsmData data = OsmData.Load(osmPath);

            EditorUtility.DisplayProgressBar("Pokhara Props", "Planting trees, putting up poles...", 0.4f);
            PropsResult result = CityMeshBuilder.BuildProps(data, settings);

            EditorUtility.DisplayProgressBar("Pokhara Props", "Saving meshes...", 0.7f);
            SaveMeshes(result);

            EditorUtility.DisplayProgressBar("Pokhara Props", "Placing props in your scene...", 0.9f);
            GameObject root = PlaceInScene(result);
            Selection.activeGameObject = root;

            EditorUtility.DisplayDialog("Props added",
                "Trees: " + result.treeCount + "\n" +
                "Electric poles: " + result.poleCount + "\n" +
                "Kerbs: " + Mathf.RoundToInt(result.kerbMetres) + " m\n" +
                "Lake barrier: " + Mathf.RoundToInt(result.barrierMetres) + " m\n\n" +
                "Press Play and look north for Machhapuchhre!", "Great!");
        }
        catch (System.Exception e)
        {
            Debug.LogException(e);
            EditorUtility.DisplayDialog("Adding props failed", "Something went wrong: " + e.Message + "\n\nThe full error is in the Console window.", "OK");
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }
    }

    private static void SaveMeshes(PropsResult result)
    {
        PokharaCityImporterWindow.EnsureFolder(Path.GetDirectoryName(MeshAssetPath).Replace('\\', '/'));
        if (File.Exists(MeshAssetPath)) AssetDatabase.DeleteAsset(MeshAssetPath);
        for (int i = 0; i < result.meshes.Count; i++)
        {
            if (i == 0) AssetDatabase.CreateAsset(result.meshes[i].mesh, MeshAssetPath);
            else AssetDatabase.AddObjectToAsset(result.meshes[i].mesh, MeshAssetPath);
        }
        AssetDatabase.SaveAssets();
    }

    private static GameObject PlaceInScene(PropsResult result)
    {
        GameObject old = GameObject.Find(RootName);
        if (old != null) Undo.DestroyObjectImmediate(old);

        var root = new GameObject(RootName);
        Undo.RegisterCreatedObjectUndo(root, "Add Pokhara Props");

        foreach (BuiltMesh built in result.meshes)
        {
            var go = new GameObject(built.name);
            go.transform.SetParent(root.transform, false);
            // Mountains move with the camera, so they are NOT static.
            go.isStatic = !built.followCamera;

            go.AddComponent<MeshFilter>().sharedMesh = built.mesh;

            if (!built.hidden)   // the invisible lake wall gets no renderer
            {
                var materials = new Material[built.materialKeys.Length];
                for (int i = 0; i < materials.Length; i++) materials[i] = PokharaCityImporterWindow.GetOrCreateMaterial(built.materialKeys[i]);
                var renderer = go.AddComponent<MeshRenderer>();
                renderer.sharedMaterials = materials;
                if (built.followCamera)
                {
                    // A 3 km mountain casting shadows on the city would look odd.
                    renderer.shadowCastingMode = ShadowCastingMode.Off;
                    renderer.receiveShadows = false;
                }
            }

            if (built.addCollider) go.AddComponent<MeshCollider>().sharedMesh = built.mesh;
            if (built.followCamera) go.AddComponent<MountainBackdrop>();
        }
        return root;
    }
}
