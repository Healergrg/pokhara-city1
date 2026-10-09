// =============================================================================
//  CarBuilder.cs  —  makes a brand-new player car in one click
// =============================================================================
//  Tools > Pokhara City > Create Player Car
//
//  Builds a small white hatchback from Unity's basic shapes (cubes and
//  cylinders), like building a car from LEGO:
//    body, glass cabin, roof, 4 wheels, headlights, tail lights,
//    orange indicators on every corner, red number plates (private car in Nepal)
//  Then it adds the physics (Rigidbody + 4 WheelColliders), the PokharaCar
//  driving script, the dashboard, and sets the Main Camera to follow it.
//
//  If a Mission Route exists, the car is placed at its start, on the LEFT
//  side of the road, facing the right way. Run it again to rebuild the car.
//
//  Later you can swap the shapes for a real 3D car model and keep the scripts.
// =============================================================================

using System.IO;
using UnityEditor;
using UnityEngine;

public static class CarBuilder
{
    private const string CarName = "Player Car";
    private const string MaterialsFolder = "Assets/PokharaCity/Generated/Materials";

    [MenuItem("Tools/Pokhara City/Create Player Car")]
    public static void CreateCar()
    {
        GameObject old = GameObject.Find(CarName);
        if (old != null) Undo.DestroyObjectImmediate(old);

        // ---- the car itself -------------------------------------------------
        var car = new GameObject(CarName);
        Undo.RegisterCreatedObjectUndo(car, "Create Player Car");
        car.AddComponent<Rigidbody>();
        PokharaCar driving = car.AddComponent<PokharaCar>();
        car.AddComponent<CarDashboard>();

        Material paint = GetMaterial("CarPaint", new Color(0.95f, 0.95f, 0.95f), 0.6f, false);
        Material glass = GetMaterial("CarGlass", new Color(0.12f, 0.16f, 0.20f), 0.9f, false);
        Material tyre = GetMaterial("CarTyre", new Color(0.06f, 0.06f, 0.06f), 0.1f, false);
        Material headlight = GetMaterial("CarHeadlight", new Color(1f, 0.98f, 0.9f), 0.8f, true);
        Material tailDim = GetMaterial("CarTailLight", new Color(0.45f, 0.03f, 0.03f), 0.6f, false);
        Material brakeOn = GetMaterial("CarBrakeLightOn", new Color(1f, 0.05f, 0.05f), 0.6f, true);
        Material orangeDim = GetMaterial("CarIndicator", new Color(0.55f, 0.28f, 0.02f), 0.6f, false);
        Material orangeOn = GetMaterial("CarIndicatorOn", new Color(1f, 0.55f, 0f), 0.6f, true);
        Material plate = GetMaterial("CarPlate", new Color(0.75f, 0.05f, 0.05f), 0.3f, false);

        // Body keeps its box collider: that is what hits walls and other cars.
        Part(car, "Body", PrimitiveType.Cube, new Vector3(0f, 0.72f, 0f), new Vector3(1.7f, 0.6f, 4.0f), paint, true);
        Part(car, "Cabin (glass)", PrimitiveType.Cube, new Vector3(0f, 1.27f, -0.25f), new Vector3(1.48f, 0.52f, 2.0f), glass, false);
        Part(car, "Roof", PrimitiveType.Cube, new Vector3(0f, 1.55f, -0.35f), new Vector3(1.5f, 0.06f, 1.65f), paint, false);

        // Lights. Nepali cars: the car faces +z, so LEFT is -x.
        for (int side = -1; side <= 1; side += 2)
        {
            float x = side * 0.6f;
            Part(car, "Headlight", PrimitiveType.Cube, new Vector3(x, 0.85f, 2.0f), new Vector3(0.38f, 0.16f, 0.04f), headlight, false);
            Part(car, "Tail light", PrimitiveType.Cube, new Vector3(x, 0.85f, -2.0f), new Vector3(0.38f, 0.16f, 0.04f), tailDim, false);
            Part(car, "Indicator front", PrimitiveType.Cube, new Vector3(side * 0.8f, 0.85f, 2.0f), new Vector3(0.14f, 0.12f, 0.04f), orangeDim, false);
            Part(car, "Indicator rear", PrimitiveType.Cube, new Vector3(side * 0.8f, 0.85f, -2.0f), new Vector3(0.14f, 0.12f, 0.04f), orangeDim, false);
        }
        Part(car, "Number plate front", PrimitiveType.Cube, new Vector3(0f, 0.55f, 2.01f), new Vector3(0.5f, 0.14f, 0.03f), plate, false);
        Part(car, "Number plate rear", PrimitiveType.Cube, new Vector3(0f, 0.6f, -2.01f), new Vector3(0.5f, 0.14f, 0.03f), plate, false);

        // "Glow" lamps sit just in front of the dim ones and are switched on/off by the script.
        driving.leftIndicatorGlows = new[]
        {
            Glow(car, "Left indicator glow front", new Vector3(-0.8f, 0.85f, 2.03f), new Vector3(0.15f, 0.13f, 0.03f), orangeOn),
            Glow(car, "Left indicator glow rear", new Vector3(-0.8f, 0.85f, -2.03f), new Vector3(0.15f, 0.13f, 0.03f), orangeOn)
        };
        driving.rightIndicatorGlows = new[]
        {
            Glow(car, "Right indicator glow front", new Vector3(0.8f, 0.85f, 2.03f), new Vector3(0.15f, 0.13f, 0.03f), orangeOn),
            Glow(car, "Right indicator glow rear", new Vector3(0.8f, 0.85f, -2.03f), new Vector3(0.15f, 0.13f, 0.03f), orangeOn)
        };
        driving.brakeLightGlows = new[]
        {
            Glow(car, "Brake glow left", new Vector3(-0.6f, 0.85f, -2.03f), new Vector3(0.39f, 0.17f, 0.03f), brakeOn),
            Glow(car, "Brake glow right", new Vector3(0.6f, 0.85f, -2.03f), new Vector3(0.39f, 0.17f, 0.03f), brakeOn)
        };

        // ---- wheels -----------------------------------------------------------
        driving.frontLeft = Wheel(car, "Front left", new Vector3(-0.78f, 0.42f, 1.3f), tyre, out driving.frontLeftVisual);
        driving.frontRight = Wheel(car, "Front right", new Vector3(0.78f, 0.42f, 1.3f), tyre, out driving.frontRightVisual);
        driving.rearLeft = Wheel(car, "Rear left", new Vector3(-0.78f, 0.42f, -1.3f), tyre, out driving.rearLeftVisual);
        driving.rearRight = Wheel(car, "Rear right", new Vector3(0.78f, 0.42f, -1.3f), tyre, out driving.rearRightVisual);

        PlaceAtStart(car);
        SetUpCamera(car);

        Selection.activeGameObject = car;
        EditorUtility.DisplayDialog("Player Car ready",
            "Press Play and drive!\n\nW/S gas and brake (hold S to reverse)\nA/D steer   Space handbrake\nQ/E indicators   H horn   C camera\n\nRemember: in Nepal you drive on the LEFT.", "Let's go");
    }

    // A visible part made from a basic shape.
    private static GameObject Part(GameObject parent, string name, PrimitiveType shape, Vector3 position, Vector3 size, Material mat, bool keepCollider)
    {
        GameObject go = GameObject.CreatePrimitive(shape);
        go.name = name;
        go.transform.SetParent(parent.transform, false);
        go.transform.localPosition = position;
        go.transform.localScale = size;
        go.GetComponent<MeshRenderer>().sharedMaterial = mat;
        if (!keepCollider) Object.DestroyImmediate(go.GetComponent<Collider>());
        return go;
    }

    private static GameObject Glow(GameObject parent, string name, Vector3 position, Vector3 size, Material mat)
    {
        GameObject go = Part(parent, name, PrimitiveType.Cube, position, size, mat, false);
        go.SetActive(false);   // off until the script switches it on
        return go;
    }

    // One wheel = an invisible WheelCollider (the physics) + a visible tyre.
    private static WheelCollider Wheel(GameObject car, string name, Vector3 position, Material tyre, out Transform visual)
    {
        var colliderObject = new GameObject(name + " (WheelCollider)");
        colliderObject.transform.SetParent(car.transform, false);
        colliderObject.transform.localPosition = position;

        WheelCollider wheel = colliderObject.AddComponent<WheelCollider>();
        wheel.radius = 0.32f;
        wheel.mass = 20f;
        wheel.suspensionDistance = 0.2f;

        JointSpring spring = wheel.suspensionSpring;   // the car's springs and shock absorbers
        spring.spring = 35000f;
        spring.damper = 4500f;
        spring.targetPosition = 0.5f;
        wheel.suspensionSpring = spring;

        WheelFrictionCurve grip = wheel.sidewaysFriction;   // stops the car sliding sideways in turns
        grip.stiffness = 2f;
        wheel.sidewaysFriction = grip;
        WheelFrictionCurve forwardGrip = wheel.forwardFriction;
        forwardGrip.stiffness = 1.5f;
        wheel.forwardFriction = forwardGrip;

        // The visible tyre: an empty "holder" that the script moves, with a
        // cylinder inside turned on its side (cylinders stand up by default).
        var holder = new GameObject(name + " (tyre)");
        holder.transform.SetParent(car.transform, false);
        holder.transform.localPosition = position;
        GameObject cylinder = Part(holder, "Tyre shape", PrimitiveType.Cylinder, Vector3.zero, new Vector3(0.64f, 0.11f, 0.64f), tyre, false);
        cylinder.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);

        visual = holder.transform;
        return wheel;
    }

    // Put the car at the start of the mission route, on the left side of the road.
    private static void PlaceAtStart(GameObject car)
    {
        Vector3 start = Vector3.zero, facing = Vector3.forward;

        MissionRoute route = Object.FindFirstObjectByType<MissionRoute>();
        RoadNetwork network = Object.FindFirstObjectByType<RoadNetwork>();
        if (route != null && route.points.Count >= 2)
        {
            start = route.points[0];
            facing = route.points[1] - route.points[0];
            facing.y = 0f;
            facing.Normalize();
            Vector3 left = new Vector3(-facing.z, 0f, facing.x);
            start += left * 1.8f;   // keep left!
        }
        else if (network != null && network.lanes.Count > 0 && network.lanes[0].points.Count >= 2)
        {
            start = network.lanes[0].points[0];   // lanes are already on the left side
            facing = (network.lanes[0].points[1] - start).normalized;
        }

        car.transform.position = start + Vector3.up * 0.6f;
        car.transform.rotation = Quaternion.LookRotation(facing, Vector3.up);
    }

    private static void SetUpCamera(GameObject car)
    {
        Camera cam = Camera.main;
        if (cam == null)
        {
            var camObject = new GameObject("Main Camera");
            camObject.tag = "MainCamera";
            cam = camObject.AddComponent<Camera>();
        }
        Undo.RecordObject(cam.gameObject, "Camera follows car");

        CarCamera follow = cam.GetComponent<CarCamera>();
        if (follow == null) follow = Undo.AddComponent<CarCamera>(cam.gameObject);
        follow.target = car.transform;

        cam.farClipPlane = 3000f;   // see across the whole city and lake
        cam.transform.position = car.transform.position - car.transform.forward * 6.5f + Vector3.up * 2.4f;
        cam.transform.LookAt(car.transform.position + Vector3.up);
    }

    // Simple materials; "glowing" ones (lights) use emission so they shine.
    private static Material GetMaterial(string name, Color colour, float smoothness, bool glows)
    {
        string path = MaterialsFolder + "/" + name + ".mat";
        Material existing = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (existing != null) return existing;

        EnsureFolder(MaterialsFolder);
        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null) shader = Shader.Find("Standard");
        var mat = new Material(shader) { name = name, color = colour };
        if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", colour);
        if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", smoothness);
        if (mat.HasProperty("_Glossiness")) mat.SetFloat("_Glossiness", smoothness);
        if (glows)
        {
            mat.EnableKeyword("_EMISSION");
            mat.SetColor("_EmissionColor", colour * 2.5f);
        }
        AssetDatabase.CreateAsset(mat, path);
        return mat;
    }

    private static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        string parent = Path.GetDirectoryName(path).Replace('\\', '/');
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
    }
}
