// =============================================================================
//  PokharaCityImporterWindow.cs  —  the window you click in Unity
// =============================================================================
//  Open it from the top menu:  Tools > Pokhara City > Import OSM Map
//
//  1. Click "Choose .osm file" and pick your map.osm
//  2. Tick what you want (roads, buildings, lake...)
//  3. Click "Build Pokhara City"
//
//  It creates one object in your scene called "Pokhara City (generated)".
//  Building again replaces it, so you can tweak settings and rebuild freely.
//  The meshes and materials are saved in Assets/PokharaCity/Generated.
// =============================================================================

using System.IO;
using UnityEditor;
using UnityEngine;

public class PokharaCityImporterWindow : EditorWindow
{
    private const string RootName = "Pokhara City (generated)";
    private const string GeneratedFolder = "Assets/PokharaCity/Generated";
    private const string MaterialsFolder = GeneratedFolder + "/Materials";
    private const string MeshAssetPath = GeneratedFolder + "/PokharaCityMeshes.asset";
    private const string PathPrefKey = "PokharaCity.OsmPath";

    private string osmPath = "";
    private CityImportSettings settings = new CityImportSettings();
    private Vector2 scroll;

    [MenuItem("Tools/Pokhara City/Import OSM Map")]
    public static void Open()
    {
        var window = GetWindow<PokharaCityImporterWindow>("Pokhara City");
        window.minSize = new Vector2(360, 460);
    }

    private void OnEnable()
    {
        osmPath = EditorPrefs.GetString(PathPrefKey, "");   // remember the last file you used
    }

    // Unity calls this to draw the window (every time it repaints).
    private void OnGUI()
    {
        scroll = EditorGUILayout.BeginScrollView(scroll);

        EditorGUILayout.LabelField("1. Map file", EditorStyles.boldLabel);
        EditorGUILayout.LabelField(string.IsNullOrEmpty(osmPath) ? "No file chosen yet" : Path.GetFileName(osmPath), EditorStyles.wordWrappedLabel);
        if (GUILayout.Button("Choose .osm file"))
        {
            string picked = EditorUtility.OpenFilePanel("Choose the map.osm you exported", "", "osm,xml,");
            if (!string.IsNullOrEmpty(picked))
            {
                osmPath = picked;
                EditorPrefs.SetString(PathPrefKey, osmPath);
            }
        }

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("2. What to build", EditorStyles.boldLabel);
        settings.roads = EditorGUILayout.Toggle("Roads", settings.roads);
        settings.footpaths = EditorGUILayout.Toggle("  Footpaths and steps", settings.footpaths);
        settings.centreLines = EditorGUILayout.Toggle("  White centre lines", settings.centreLines);
        settings.buildings = EditorGUILayout.Toggle("Buildings", settings.buildings);
        settings.water = EditorGUILayout.Toggle("Phewa Lake and ponds", settings.water);
        settings.greenAreas = EditorGUILayout.Toggle("Parks and green areas", settings.greenAreas);
        settings.trafficLanes = EditorGUILayout.Toggle("Traffic lanes (for AI cars)", settings.trafficLanes);
        settings.shopFronts = EditorGUILayout.Toggle("  Shops on street-facing walls", settings.shopFronts);
        settings.roofDetails = EditorGUILayout.Toggle("  Roof walls + water tanks", settings.roofDetails);

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("3. Building heights (when the map does not say)", EditorStyles.boldLabel);
        settings.minFloors = EditorGUILayout.IntSlider("Fewest floors", settings.minFloors, 1, 8);
        settings.maxFloors = EditorGUILayout.IntSlider("Most floors", settings.maxFloors, settings.minFloors, 10);
        settings.metresPerFloor = EditorGUILayout.Slider("Metres per floor", settings.metresPerFloor, 2.5f, 4f);

        EditorGUILayout.Space();
        GUI.enabled = File.Exists(osmPath);
        if (GUILayout.Button("Build Pokhara City", GUILayout.Height(36))) BuildCity();
        GUI.enabled = true;

        EditorGUILayout.HelpBox("Map data © OpenStreetMap contributors (ODbL). Keep this credit in your game's credits screen.", MessageType.Info);
        EditorGUILayout.EndScrollView();
    }

    // -------------------------------------------------------------------------
    // The big button: read -> build meshes -> save assets -> put in scene.
    // -------------------------------------------------------------------------
    private void BuildCity()
    {
        try
        {
            EditorUtility.DisplayProgressBar("Pokhara City", "Reading map file...", 0.1f);
            OsmData data = OsmData.Load(osmPath);

            EditorUtility.DisplayProgressBar("Pokhara City", "Building roads, buildings and lake...", 0.4f);
            CityBuildResult result = CityMeshBuilder.Build(data, settings);

            EditorUtility.DisplayProgressBar("Pokhara City", "Saving meshes...", 0.7f);
            SaveMeshes(result);

            EditorUtility.DisplayProgressBar("Pokhara City", "Placing the city in your scene...", 0.9f);
            GameObject root = PlaceInScene(data, result);

            Selection.activeGameObject = root;
            if (SceneView.lastActiveSceneView != null) SceneView.lastActiveSceneView.FrameSelected();

            EditorUtility.DisplayDialog("Pokhara City is ready",
                "Roads: " + result.roadCount + "\n" +
                "Buildings: " + result.buildingCount + "\n" +
                "Water areas: " + result.waterCount + "\n" +
                "Green areas: " + result.greenCount + "\n" +
                "Traffic lanes: " + result.lanes.Count + "\n\n" +
                "Tip: select 'Road Network' to see the traffic lanes (cyan lines).", "Great!");
        }
        catch (System.Exception e)
        {
            Debug.LogException(e);
            EditorUtility.DisplayDialog("Import failed", "Something went wrong: " + e.Message + "\n\nThe full error is in the Console window.", "OK");
        }
        finally
        {
            EditorUtility.ClearProgressBar();   // always hide the progress bar, even after an error
        }
    }

    // Meshes made in code disappear when Unity closes unless we save them
    // as an asset file — like saving a Word document instead of only typing it.
    private static void SaveMeshes(CityBuildResult result)
    {
        EnsureFolder(GeneratedFolder);
        if (File.Exists(MeshAssetPath)) AssetDatabase.DeleteAsset(MeshAssetPath);

        for (int i = 0; i < result.meshes.Count; i++)
        {
            Mesh mesh = result.meshes[i].mesh;
            if (i == 0) AssetDatabase.CreateAsset(mesh, MeshAssetPath);   // first mesh creates the file
            else AssetDatabase.AddObjectToAsset(mesh, MeshAssetPath);    // the rest go inside it
        }
        AssetDatabase.SaveAssets();
    }

    private static GameObject PlaceInScene(OsmData data, CityBuildResult result)
    {
        // Remove the old city if you built one before.
        GameObject old = GameObject.Find(RootName);
        if (old != null) Undo.DestroyObjectImmediate(old);

        var root = new GameObject(RootName);
        Undo.RegisterCreatedObjectUndo(root, "Build Pokhara City");

        foreach (BuiltMesh built in result.meshes)
        {
            var go = new GameObject(built.name);
            go.transform.SetParent(root.transform, false);
            go.isStatic = true;   // tells Unity these never move -> faster lighting and rendering

            go.AddComponent<MeshFilter>().sharedMesh = built.mesh;

            var materials = new Material[built.materialKeys.Length];
            for (int i = 0; i < materials.Length; i++) materials[i] = GetOrCreateMaterial(built.materialKeys[i]);
            go.AddComponent<MeshRenderer>().sharedMaterials = materials;

            if (built.addCollider) go.AddComponent<MeshCollider>().sharedMesh = built.mesh;
        }

        // The invisible lane map for traffic AI.
        var networkObject = new GameObject("Road Network");
        networkObject.transform.SetParent(root.transform, false);
        var network = networkObject.AddComponent<RoadNetwork>();
        network.originLatitude = data.originLat;
        network.originLongitude = data.originLon;
        network.lanes = result.lanes;

        return root;
    }

    // Make a folder the Unity way (so the Project window knows about it),
    // one level at a time: Assets -> Assets/PokharaCity -> .../Generated
    public static void EnsureFolder(string path)   // public: the props window uses it too
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        string parent = Path.GetDirectoryName(path).Replace('\\', '/');
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
    }

    // -------------------------------------------------------------------------
    // Materials = paint + wallpaper for each part of the city.
    // Colour (the paint) and texture (the wallpaper with windows, shutters,
    // asphalt...). They live in Assets/PokharaCity/Generated/Materials and are
    // reused when you rebuild, so any changes you make to them are kept.
    // -------------------------------------------------------------------------
    public static Material GetOrCreateMaterial(string key)   // public: the props window uses it too
    {
        Color colour;
        float smoothness = 0.15f;
        string textureName = null;   // which picture from the Textures folder to use
        switch (key)
        {
            case "Ground":   colour = Color.white; textureName = "PokharaGround"; break;
            case "RoadMain": colour = new Color(0.85f, 0.85f, 0.85f); textureName = "PokharaAsphalt"; break;
            case "Road":     colour = Color.white; textureName = "PokharaAsphalt"; break;
            case "Dirt":     colour = new Color(0.55f, 0.45f, 0.33f); break;
            case "Footpath": colour = new Color(0.68f, 0.66f, 0.62f); break;
            case "RoadLine": colour = new Color(0.95f, 0.95f, 0.92f); break;
            case "Water":    colour = new Color(0.20f, 0.42f, 0.55f); smoothness = 0.9f; break;
            case "Grass":    colour = new Color(0.36f, 0.55f, 0.28f); break;
            case "Roof":     colour = Color.white; textureName = "PokharaRoof"; break;
            // Wall colours seen around Lakeside: the facade texture is almost white,
            // so these colours tint it like paint.
            case "Wall0":    colour = new Color(0.97f, 0.93f, 0.82f); textureName = "PokharaFacade"; break;  // cream
            case "Wall1":    colour = new Color(1.00f, 1.00f, 0.98f); textureName = "PokharaFacade"; break;  // white
            case "Wall2":    colour = new Color(1.00f, 0.82f, 0.52f); textureName = "PokharaFacade"; break;  // orange
            case "Wall3":    colour = new Color(0.70f, 0.85f, 0.95f); textureName = "PokharaFacade"; break;  // sky blue
            case "Wall4":    colour = new Color(0.95f, 0.72f, 0.72f); textureName = "PokharaFacade"; break;  // pink
            case "Shop0":    colour = Color.white; textureName = "PokharaShop0"; break;
            case "Shop1":    colour = Color.white; textureName = "PokharaShop1"; break;
            case "Shop2":    colour = Color.white; textureName = "PokharaShop2"; break;
            case "Tank":     colour = new Color(0.08f, 0.08f, 0.09f); smoothness = 0.45f; break;  // black plastic
            // ---- Week 2 props ----
            case "Railing":  colour = new Color(0.16f, 0.36f, 0.24f); smoothness = 0.35f; break;  // painted green metal
            case "Kerb":     colour = Color.white; textureName = "PokharaKerb"; break;            // black + yellow stripes
            case "Pole":     colour = new Color(0.62f, 0.61f, 0.58f); break;                      // concrete
            case "Wire":     colour = new Color(0.05f, 0.05f, 0.05f); break;
            case "Trunk":    colour = new Color(0.33f, 0.24f, 0.17f); break;
            case "Leaves0":  colour = new Color(0.22f, 0.42f, 0.16f); break;                      // deep green
            case "Leaves1":  colour = new Color(0.33f, 0.50f, 0.20f); break;                      // fresh green
            case "Leaves2":  colour = new Color(0.18f, 0.34f, 0.18f); break;                      // dark green
            // Far away things look bluer and paler (the air in between is hazy).
            case "MountainHills": colour = new Color(0.30f, 0.40f, 0.33f); smoothness = 0f; break;
            case "MountainRock":  colour = new Color(0.47f, 0.52f, 0.60f); smoothness = 0f; break;
            case "MountainSnow":  colour = new Color(0.96f, 0.97f, 1.00f); smoothness = 0.1f; break;
            default:         colour = Color.magenta; break;
        }

        string path = MaterialsFolder + "/" + key + ".mat";
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        bool isNew = mat == null;

        if (isNew)
        {
            EnsureFolder(MaterialsFolder);
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) shader = Shader.Find("Standard");   // project without URP
            mat = new Material(shader) { name = key };
        }

        // Paint a new material, or upgrade an old plain-colour one (from the first
        // version of this tool) that has no texture yet. A material that already
        // has a texture is left alone, in case you changed it yourself.
        Texture2D texture = textureName != null ? FindTexture(textureName) : null;
        bool upgrade = !isNew && texture != null && mat.mainTexture == null;
        if (isNew || upgrade)
        {
            mat.color = colour;
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", colour);
            if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", smoothness);
            if (mat.HasProperty("_Glossiness")) mat.SetFloat("_Glossiness", smoothness);
            if (texture != null)
            {
                mat.mainTexture = texture;
                if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", texture);
            }
        }

        if (isNew) AssetDatabase.CreateAsset(mat, path);
        else EditorUtility.SetDirty(mat);
        return mat;
    }

    // Find a texture by its file name anywhere in the project (so it still
    // works if you move the PokharaCity folder somewhere else).
    private static Texture2D FindTexture(string fileName)
    {
        foreach (string guid in AssetDatabase.FindAssets(fileName + " t:Texture2D"))
        {
            string assetPath = AssetDatabase.GUIDToAssetPath(guid);
            if (Path.GetFileNameWithoutExtension(assetPath) == fileName)
                return AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
        }
        return null;
    }
}
