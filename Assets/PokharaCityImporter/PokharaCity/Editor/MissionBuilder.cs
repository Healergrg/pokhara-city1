// =============================================================================
//  MissionBuilder.cs  —  Week 5: Tools > Pokhara City > Add Missions
// =============================================================================
//  Makes the MissionManager with missions that use REAL places on your map:
//
//    1. Driving test: Lakeside to Damside        (pass mark 70)
//    2. School run: through 3 school zones       (pass mark 80, 20 km/h zones!)
//    3. Hospital trip: Damside to CIWEC Hospital (faster, silent zone at the end)
//    4. Chowk challenge: through the signals     (pass mark 80)
//    5. Your imported GeoJSON route, if you have one in the scene
//
//  Plus the checkpoint gate, the chequered finish gate and the yellow route
//  line. Every mission can be edited in the Inspector afterwards (start,
//  finish, places to drive through, time limit, pass mark).
// =============================================================================

using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

public static class MissionBuilder
{
    private const string RootName = "Pokhara Missions (generated)";
    private const string MaterialsFolder = "Assets/PokharaCity/Generated/Materials/Missions";
    private const string PathPrefKey = "PokharaCity.OsmPath";

    [MenuItem("Tools/Pokhara City/Add Missions (driving tests)")]
    public static void Build()
    {
        if (Object.FindFirstObjectByType<RoadNetwork>() == null)
        {
            EditorUtility.DisplayDialog("No roads yet", "Build the city first (Tools > Pokhara City > Import OSM Map).", "OK");
            return;
        }
        string osmPath = EditorPrefs.GetString(PathPrefKey, "");
        if (!File.Exists(osmPath))
        {
            osmPath = EditorUtility.OpenFilePanel("Choose the same map.osm you used for the city", "", "osm,xml,");
            if (string.IsNullOrEmpty(osmPath)) return;
            EditorPrefs.SetString(PathPrefKey, osmPath);
        }

        OsmData data = OsmData.Load(osmPath);
        RulesPlan places = CityMeshBuilder.PlanRules(data);   // signals, schools, hospitals

        GameObject old = GameObject.Find(RootName);
        if (old != null) Undo.DestroyObjectImmediate(old);
        var root = new GameObject(RootName);
        Undo.RegisterCreatedObjectUndo(root, "Add Missions");
        MissionManager manager = root.AddComponent<MissionManager>();

        manager.missions = MakeMissions(places, Object.FindFirstObjectByType<MissionRoute>(), data);

        // Gates + route line look.
        var templates = new GameObject("Templates (hidden)");
        templates.transform.SetParent(root.transform, false);
        templates.SetActive(false);
        manager.checkpointTemplate = MakeGate("Checkpoint gate", Mat("GateYellow", new Color(1f, 0.8f, 0.05f), true), templates);
        manager.finishTemplate = MakeGate("Finish gate", Mat("GateFinish", Color.white, false, "PokharaFinish"), templates);
        manager.routeLineMaterial = LineMat();

        Selection.activeGameObject = root;
        string list = "";
        for (int i = 0; i < manager.missions.Count; i++) list += (i + 1) + ". " + manager.missions[i].title + "\n";
        EditorUtility.DisplayDialog("Missions added", list + "\nPress Play, then M to open the mission menu.", "Great!");
    }

    // -------------------------------------------------------------------------
    private static List<MissionDefinition> MakeMissions(RulesPlan places, MissionRoute imported, OsmData data)
    {
        var missions = new List<MissionDefinition>();
        List<SignalPlan> signals = places.signals;
        if (signals.Count == 0) return missions;

        // Find Lakeside (by name), and Damside = the signal furthest from it.
        SignalPlan lakeside = signals[0];
        foreach (SignalPlan sp in signals) if (sp.name.Contains("Lakeside")) { lakeside = sp; break; }
        SignalPlan damside = lakeside; float far = 0f;
        foreach (SignalPlan sp in signals)
        {
            float d = Vector3.Distance(sp.centre, lakeside.centre);
            if (d > far) { far = d; damside = sp; }
        }

        // 1. The classic.
        missions.Add(new MissionDefinition
        {
            title = "Driving test: Lakeside to Damside",
            description = "Main roads, signals and chowks. Keep left and indicate.",
            start = StartNear(lakeside, damside.centre),
            finish = damside.centre,
            averageSpeedKmh = 22f,
            passScore = 70
        });

        // 2. School run: the 3 schools nearest Lakeside, then a 4th as the finish.
        var schools = new List<ZonePlan>();
        foreach (ZonePlan z in places.zones) if (z.school) schools.Add(z);
        schools.Sort((a, b) => Vector3.Distance(a.centre, lakeside.centre).CompareTo(Vector3.Distance(b.centre, lakeside.centre)));
        if (schools.Count >= 2)
        {
            var school = new MissionDefinition
            {
                title = "School run",
                description = "Drive past " + Mathf.Min(3, schools.Count - 1) + " schools. 20 km/h and no horn in school zones!",
                start = StartNear(lakeside, schools[0].centre),
                averageSpeedKmh = 15f,
                passScore = 80
            };
            int count = Mathf.Min(4, schools.Count);
            for (int i = 0; i < count - 1; i++) school.via.Add(schools[i].signPosition);
            school.finish = schools[count - 1].signPosition;
            missions.Add(school);
        }

        // 3. Hospital trip (prefer CIWEC, else the first hospital or clinic).
        ZonePlan hospital = null;
        foreach (ZonePlan z in places.zones)
            if (!z.school && (hospital == null || z.name.Contains("CIWEC"))) hospital = z;
        if (hospital != null)
        {
            missions.Add(new MissionDefinition
            {
                title = "Hospital trip: " + hospital.name,
                description = "Take a passenger to the hospital. Be quick but safe, and no horn at the end.",
                start = StartNear(damside, hospital.centre),
                finish = hospital.signPosition,
                averageSpeedKmh = 27f,
                passScore = 70
            });
        }

        // 4. Chowk challenge: every signal except Damside, west to east.
        var chowks = new List<SignalPlan>();
        foreach (SignalPlan sp in signals) if (sp != damside) chowks.Add(sp);
        chowks.Sort((a, b) => a.centre.x.CompareTo(b.centre.x));
        if (chowks.Count >= 3)
        {
            var challenge = new MissionDefinition
            {
                title = "Chowk challenge",
                description = "Through " + chowks.Count + " traffic signals. Stop on red, never on the zebra.",
                start = StartNear(chowks[0], chowks[1].centre),
                finish = chowks[chowks.Count - 1].centre,
                averageSpeedKmh = 20f,
                passScore = 80
            };
            for (int i = 1; i < chowks.Count - 1; i++) challenge.via.Add(chowks[i].centre);
            missions.Add(challenge);
        }

        // 5. The route you imported from GeoJSON (only the part inside the map).
        if (imported != null && imported.points.Count >= 2)
        {
            Vector3 lo = data.ToUnity(data.minLat, data.minLon), hi = data.ToUnity(data.maxLat, data.maxLon);
            var inside = new List<Vector3>();
            foreach (Vector3 p in imported.points)
                if (p.x > lo.x + 20f && p.x < hi.x - 20f && p.z > lo.z + 20f && p.z < hi.z - 20f) inside.Add(p);
            if (inside.Count >= 2)
            {
                var mine = new MissionDefinition
                {
                    title = "Your route: " + imported.missionName,
                    description = "The route you imported (the part inside your map).",
                    start = inside[0],
                    finish = inside[inside.Count - 1],
                    averageSpeedKmh = 22f,
                    passScore = 70
                };
                // A few points along it, so the planner follows YOUR way.
                for (int k = 1; k <= 3; k++) mine.via.Add(inside[inside.Count * k / 4]);
                missions.Add(mine);
            }
        }
        return missions;
    }

    // Start 45 m before a junction, on the road that heads most towards 'goal',
    // on the LEFT half of the road (so the car starts in the right lane).
    private static Vector3 StartNear(SignalPlan junction, Vector3 goal)
    {
        Vector3 toGoal = goal - junction.centre; toGoal.y = 0f;
        int best = 0; float bestDot = -2f;
        for (int i = 0; i < junction.approachDirections.Count; i++)
        {
            float d = Vector3.Dot(junction.approachDirections[i], toGoal.normalized);
            if (d > bestDot) { bestDot = d; best = i; }
        }
        Vector3 dir = junction.approachDirections[best];
        Vector3 left = new Vector3(-dir.z, 0f, dir.x);
        return junction.centre - dir * (junction.stopRadius + 45f) + left * (junction.approachHalfWidths[best] / 2f);
    }

    // -------------------------------------------------------------------------
    //  A gate over the road: two posts and a banner, no colliders.
    // -------------------------------------------------------------------------
    private static GameObject MakeGate(string name, Material banner, GameObject parent)
    {
        var gate = new GameObject(name);
        gate.transform.SetParent(parent.transform, false);
        Material post = Mat("GatePost", new Color(0.15f, 0.15f, 0.15f), false);
        for (int side = -1; side <= 1; side += 2)
            Part(gate, PrimitiveType.Cylinder, "Post", new Vector3(side * 4.2f, 2.5f, 0f), new Vector3(0.25f, 2.5f, 0.25f), post);
        Part(gate, PrimitiveType.Cube, "Banner", new Vector3(0f, 5.2f, 0f), new Vector3(8.6f, 0.9f, 0.15f), banner);
        return gate;
    }

    private static GameObject Part(GameObject parent, PrimitiveType type, string name, Vector3 pos, Vector3 scale, Material m)
    {
        GameObject go = GameObject.CreatePrimitive(type);
        go.name = name;
        go.transform.SetParent(parent.transform, false);
        go.transform.localPosition = pos;
        go.transform.localScale = scale;
        go.GetComponent<Renderer>().sharedMaterial = m;
        Object.DestroyImmediate(go.GetComponent<Collider>());   // you drive THROUGH gates
        return go;
    }

    private static Material Mat(string name, Color colour, bool glows, string textureName = null)
    {
        string path = MaterialsFolder + "/" + name + ".mat";
        Material existing = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (existing != null) return existing;
        PokharaCityImporterWindow.EnsureFolder(MaterialsFolder);
        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null) shader = Shader.Find("Standard");
        var mat = new Material(shader) { name = name, color = colour };
        if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", colour);
        if (glows) { mat.EnableKeyword("_EMISSION"); mat.SetColor("_EmissionColor", colour * 1.5f); }
        if (textureName != null)
        {
            foreach (string guid in AssetDatabase.FindAssets(textureName + " t:Texture2D"))
            {
                string p = AssetDatabase.GUIDToAssetPath(guid);
                if (Path.GetFileNameWithoutExtension(p) != textureName) continue;
                Texture2D t = AssetDatabase.LoadAssetAtPath<Texture2D>(p);
                mat.mainTexture = t;
                if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", t);
            }
        }
        AssetDatabase.CreateAsset(mat, path);
        return mat;
    }

    // The yellow route line: "Unlit" so it is bright even in shadow,
    // and drawn on both sides so it can never be invisible.
    private static Material LineMat()
    {
        string path = MaterialsFolder + "/RouteLine.mat";
        Material existing = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (existing != null) return existing;
        PokharaCityImporterWindow.EnsureFolder(MaterialsFolder);
        Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null) shader = Shader.Find("Unlit/Color");
        var colour = new Color(1f, 0.82f, 0.1f);
        var mat = new Material(shader) { name = "RouteLine", color = colour };
        if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", colour);
        if (mat.HasProperty("_Cull")) mat.SetFloat("_Cull", 0f);   // 0 = show both sides
        AssetDatabase.CreateAsset(mat, path);
        return mat;
    }
}
