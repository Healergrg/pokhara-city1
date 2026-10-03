// =============================================================================
//  RulesBuilder.cs  —  Week 3: Tools > Pokhara City > Add Road Rules
// =============================================================================
//  One click builds, from your map.osm:
//    - traffic signals (poles + red/amber/green lamps) at the 6 biggest chowks,
//      with white stop lines on each road arriving there
//    - zebra crossings with a pedestrian who walks across now and then
//    - school zones and hospital "silent zones" with signs at the roadside
//    - the "Road Rules Judge" that checks your driving, and the HUD that
//      shows your score
//
//  Everything goes into "Pokhara Road Rules (generated)". Click again to
//  rebuild it. Your city, props and car are not touched.
// =============================================================================

using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

public static class RulesBuilder
{
    private const string RootName = "Pokhara Road Rules (generated)";
    private const string MeshAssetPath = "Assets/PokharaCity/Generated/PokharaRulesMeshes.asset";
    private const string RuleMaterials = "Assets/PokharaCity/Generated/Materials/Rules";
    private const string PathPrefKey = "PokharaCity.OsmPath";   // same map file as the importer

    [MenuItem("Tools/Pokhara City/Add Road Rules (signals, zebra crossings, zones)")]
    public static void Build()
    {
        string osmPath = EditorPrefs.GetString(PathPrefKey, "");
        if (!File.Exists(osmPath))
        {
            osmPath = EditorUtility.OpenFilePanel("Choose the same map.osm you used for the city", "", "osm,xml,");
            if (string.IsNullOrEmpty(osmPath)) return;
            EditorPrefs.SetString(PathPrefKey, osmPath);
        }

        try
        {
            EditorUtility.DisplayProgressBar("Road Rules", "Reading map and choosing junctions...", 0.2f);
            OsmData data = OsmData.Load(osmPath);
            RulesPlan plan = CityMeshBuilder.PlanRules(data);

            EditorUtility.DisplayProgressBar("Road Rules", "Building signals, crossings and signs...", 0.6f);
            GameObject old = GameObject.Find(RootName);
            if (old != null) Undo.DestroyObjectImmediate(old);
            var root = new GameObject(RootName);
            Undo.RegisterCreatedObjectUndo(root, "Add Road Rules");

            AddMarkings(root, plan);
            var signalsParent = Child(root, "Traffic signals");
            for (int i = 0; i < plan.signals.Count; i++) AddSignal(signalsParent, plan.signals[i], i);
            var crossingsParent = Child(root, "Zebra crossings");
            for (int i = 0; i < plan.crossings.Count; i++) AddCrossing(crossingsParent, plan.crossings[i], i);
            var zonesParent = Child(root, "School and silent zones");
            foreach (ZonePlan z in plan.zones) AddZone(zonesParent, z);

            // The examiner + the score on screen.
            var judge = Child(root, "Road Rules Judge");
            judge.isStatic = false;
            judge.AddComponent<RoadRulesJudge>();
            judge.AddComponent<RulesHUD>();

            Selection.activeGameObject = root;
            int schools = 0;
            foreach (ZonePlan z in plan.zones) if (z.school) schools++;
            string carWarning = Object.FindFirstObjectByType<PokharaCar>() == null
                ? "\n\nNo Player Car found! Use Tools > Pokhara City > Create Player Car." : "";
            EditorUtility.DisplayDialog("Road rules added",
                "Traffic signals: " + plan.signals.Count + "\n" +
                "Zebra crossings: " + plan.crossings.Count + "\n" +
                "School zones: " + schools + "\n" +
                "Silent zones (hospitals): " + (plan.zones.Count - schools) + "\n\n" +
                "Press Play. Your score is in the top-right corner." + carWarning, "Great!");
        }
        catch (System.Exception e)
        {
            Debug.LogException(e);
            EditorUtility.DisplayDialog("Adding road rules failed", "Something went wrong: " + e.Message + "\n\nThe full error is in the Console window.", "OK");
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }
    }

    // ---- white stop lines and zebra stripes ------------------------------------
    private static void AddMarkings(GameObject root, RulesPlan plan)
    {
        Mesh mesh = CityMeshBuilder.BuildRuleMarkings(plan);
        PokharaCityImporterWindow.EnsureFolder("Assets/PokharaCity/Generated");
        if (File.Exists(MeshAssetPath)) AssetDatabase.DeleteAsset(MeshAssetPath);
        AssetDatabase.CreateAsset(mesh, MeshAssetPath);

        var go = Child(root, "Road markings (stop lines + zebras)");
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        go.AddComponent<MeshRenderer>().sharedMaterial = PokharaCityImporterWindow.GetOrCreateMaterial("RoadLine");
    }

    // ---- a traffic signal: one pole with 3 lamps for every road arriving ---------
    private static void AddSignal(GameObject parent, SignalPlan plan, int index)
    {
        var go = Child(parent, "Signal - " + plan.name);
        go.transform.position = plan.centre;
        TrafficSignal signal = go.AddComponent<TrafficSignal>();
        signal.junctionName = plan.name;
        signal.stopRadius = plan.stopRadius;
        signal.timeOffset = index * 7f;   // not all chowks change at the same moment

        signal.redOn = Mat("SignalRedOn", new Color(1f, 0.08f, 0.05f), true);
        signal.redOff = Mat("SignalRedOff", new Color(0.25f, 0.03f, 0.03f), false);
        signal.amberOn = Mat("SignalAmberOn", new Color(1f, 0.6f, 0f), true);
        signal.amberOff = Mat("SignalAmberOff", new Color(0.25f, 0.15f, 0.02f), false);
        signal.greenOn = Mat("SignalGreenOn", new Color(0.1f, 1f, 0.3f), true);
        signal.greenOff = Mat("SignalGreenOff", new Color(0.03f, 0.2f, 0.06f), false);
        Material housing = Mat("SignalHousing", new Color(0.08f, 0.08f, 0.08f), false);
        Material pole = PokharaCityImporterWindow.GetOrCreateMaterial("Pole");

        Vector3 first = plan.approachDirections[0];
        for (int i = 0; i < plan.approachDirections.Count; i++)
        {
            Vector3 dir = plan.approachDirections[i];
            Vector3 left = new Vector3(-dir.z, 0f, dir.x);   // Nepal: signals stand on the LEFT
            float half = plan.approachHalfWidths[i];

            var approach = new SignalApproach
            {
                directionIn = dir,
                // Roads in a straight line with the first road share its turn.
                group = Mathf.Abs(Vector3.Dot(dir, first)) > 0.7f ? 0 : 1
            };

            var arm = Child(go, "Approach " + (i + 1));
            arm.transform.position = plan.centre - dir * plan.stopRadius + left * (half + 0.8f);
            arm.transform.rotation = Quaternion.LookRotation(-dir, Vector3.up);   // lamps face the drivers

            Shape(arm, PrimitiveType.Cylinder, "Pole", new Vector3(0f, 1.7f, 0f), new Vector3(0.16f, 1.7f, 0.16f), pole, true);
            Shape(arm, PrimitiveType.Cube, "Housing", new Vector3(0f, 3.75f, 0f), new Vector3(0.42f, 1.15f, 0.3f), housing, false);
            approach.redLamp = Shape(arm, PrimitiveType.Sphere, "Red", new Vector3(0f, 4.1f, 0.16f), Vector3.one * 0.27f, signal.redOff, false).GetComponent<Renderer>();
            approach.amberLamp = Shape(arm, PrimitiveType.Sphere, "Amber", new Vector3(0f, 3.75f, 0.16f), Vector3.one * 0.27f, signal.amberOff, false).GetComponent<Renderer>();
            approach.greenLamp = Shape(arm, PrimitiveType.Sphere, "Green", new Vector3(0f, 3.4f, 0.16f), Vector3.one * 0.27f, signal.greenOff, false).GetComponent<Renderer>();
            signal.approaches.Add(approach);
        }
    }

    // ---- a zebra crossing with one pedestrian -------------------------------------
    private static readonly Color[] ShirtColours =
    {
        new Color(0.75f, 0.12f, 0.12f), new Color(0.15f, 0.3f, 0.7f), new Color(0.9f, 0.75f, 0.2f), new Color(0.2f, 0.55f, 0.3f)
    };

    private static void AddCrossing(GameObject parent, CrossingPlan plan, int index)
    {
        var go = Child(parent, "Zebra crossing " + (index + 1));
        go.isStatic = false;
        go.transform.position = plan.centre + Vector3.up * 0.06f;
        ZebraCrossing crossing = go.AddComponent<ZebraCrossing>();
        crossing.roadDirection = new Vector3(plan.along.x, 0f, plan.along.z).normalized;
        crossing.roadHalfWidth = plan.halfWidth;

        // A simple person: a capsule body and a round head.
        var person = new GameObject("Pedestrian");
        person.transform.SetParent(go.transform, false);
        Material shirt = Mat("PedestrianShirt" + (index % ShirtColours.Length), ShirtColours[index % ShirtColours.Length], false);
        Material skin = Mat("PedestrianSkin", new Color(0.62f, 0.45f, 0.33f), false);
        Shape(person, PrimitiveType.Capsule, "Body", new Vector3(0f, 0.8f, 0f), new Vector3(0.45f, 0.8f, 0.4f), shirt, true).isStatic = false;
        Shape(person, PrimitiveType.Sphere, "Head", new Vector3(0f, 1.72f, 0f), Vector3.one * 0.26f, skin, false).isStatic = false;
        Rigidbody body = person.AddComponent<Rigidbody>();
        body.isKinematic = true;   // moved by the script, not pushed around by physics
        person.AddComponent<Pedestrian>().crossing = crossing;
        crossing.pedestrian = person.transform;
    }

    // ---- a school or silent zone with a sign ---------------------------------------
    private static void AddZone(GameObject parent, ZonePlan plan)
    {
        var go = Child(parent, (plan.school ? "School zone - " : "Silent zone - ") + plan.name);
        go.transform.position = plan.centre;
        RuleZone zone = go.AddComponent<RuleZone>();
        zone.placeName = plan.name;
        zone.isSchoolZone = plan.school;
        zone.radius = plan.radius;

        var sign = Child(go, "Sign");
        sign.transform.position = plan.signPosition;
        Vector3 facing = new Vector3(plan.signFacing.x, 0f, plan.signFacing.z);
        if (facing.sqrMagnitude < 0.01f) facing = new Vector3(0f, 0f, 1f);
        sign.transform.rotation = Quaternion.LookRotation(facing.normalized, Vector3.up);

        Shape(sign, PrimitiveType.Cylinder, "Post", new Vector3(0f, 1.1f, 0f), new Vector3(0.07f, 1.1f, 0.07f), PokharaCityImporterWindow.GetOrCreateMaterial("Pole"), true);
        Material face = Mat(plan.school ? "SignSchool" : "SignSilent", Color.white, false, plan.school ? "PokharaSignSchool" : "PokharaSignSilent");
        // Two faces back to back, so drivers from both directions can read it.
        // (A Unity Quad is seen from its -z side, which is where the drivers come from.)
        GameObject front = Shape(sign, PrimitiveType.Quad, "Face", new Vector3(0f, 2.6f, -0.02f), new Vector3(0.8f, 1.2f, 1f), face, false);
        GameObject back = Shape(sign, PrimitiveType.Quad, "Face (back)", new Vector3(0f, 2.6f, 0.02f), new Vector3(0.8f, 1.2f, 1f), face, false);
        back.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
    }

    // =========================================================================
    //  Helpers
    // =========================================================================
    private static GameObject Child(GameObject parent, string name)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent.transform, false);
        go.isStatic = true;
        return go;
    }

    // One of Unity's basic shapes, placed inside 'parent' (local position).
    private static GameObject Shape(GameObject parent, PrimitiveType type, string name, Vector3 localPosition,
                                    Vector3 localScale, Material material, bool keepCollider)
    {
        GameObject go = GameObject.CreatePrimitive(type);
        go.name = name;
        go.transform.SetParent(parent.transform, false);
        go.transform.localPosition = localPosition;
        go.transform.localScale = localScale;
        go.GetComponent<Renderer>().sharedMaterial = material;
        go.isStatic = parent.isStatic;
        if (!keepCollider) Object.DestroyImmediate(go.GetComponent<Collider>());
        return go;
    }

    // Lamp and sign materials, saved in Generated/Materials/Rules.
    private static Material Mat(string name, Color colour, bool glows, string textureName = null)
    {
        string path = RuleMaterials + "/" + name + ".mat";
        Material existing = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (existing != null) return existing;

        PokharaCityImporterWindow.EnsureFolder(RuleMaterials);
        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null) shader = Shader.Find("Standard");
        var mat = new Material(shader) { name = name, color = colour };
        if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", colour);
        if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", glows ? 0.8f : 0.3f);
        if (glows)
        {
            // A lit lamp shines even in shadow.
            mat.EnableKeyword("_EMISSION");
            mat.SetColor("_EmissionColor", colour * 3f);
        }
        if (textureName != null)
        {
            Texture2D texture = FindTexture(textureName);
            if (texture != null)
            {
                mat.mainTexture = texture;
                if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", texture);
            }
        }
        AssetDatabase.CreateAsset(mat, path);
        return mat;
    }

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
