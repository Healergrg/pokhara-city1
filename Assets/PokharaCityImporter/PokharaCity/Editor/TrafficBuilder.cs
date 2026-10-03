// =============================================================================
//  TrafficBuilder.cs  —  Week 4: Tools > Pokhara City > Add Traffic
// =============================================================================
//  Builds "Pokhara Traffic (generated)" with:
//    - the TrafficManager (fills the streets around you with vehicles)
//    - a hidden folder of vehicle TEMPLATES, made from Unity's basic shapes:
//        hatchbacks in 6 colours, a white taxi, a Hiace microbus,
//        a colourful local bus, a decorated Tata truck and motorbikes.
//      The manager copies these when it needs a new vehicle (like using a
//      cookie cutter again and again).
//
//  Change how common each vehicle is in the TrafficManager's "Weights" list,
//  or how many there are with "Max Vehicles".
// =============================================================================

using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public static class TrafficBuilder
{
    private const string RootName = "Pokhara Traffic (generated)";
    private const string MaterialsFolder = "Assets/PokharaCity/Generated/Materials/Traffic";

    [MenuItem("Tools/Pokhara City/Add Traffic (cars, buses, motorbikes)")]
    public static void Build()
    {
        if (Object.FindFirstObjectByType<RoadNetwork>() == null)
        {
            EditorUtility.DisplayDialog("No roads yet", "Build the city first (Tools > Pokhara City > Import OSM Map). Traffic drives on its Road Network.", "OK");
            return;
        }

        GameObject old = GameObject.Find(RootName);
        if (old != null) Undo.DestroyObjectImmediate(old);
        var root = new GameObject(RootName);
        Undo.RegisterCreatedObjectUndo(root, "Add Traffic");
        TrafficManager manager = root.AddComponent<TrafficManager>();

        // Templates live in a switched-off folder so they never drive themselves.
        var folder = new GameObject("Vehicle templates (hidden)");
        folder.transform.SetParent(root.transform, false);
        folder.SetActive(false);

        string[] colourNames = { "Red", "Blue", "Silver", "White", "Black", "Maroon" };
        Color[] colours =
        {
            new Color(0.75f, 0.08f, 0.08f), new Color(0.12f, 0.25f, 0.6f), new Color(0.72f, 0.73f, 0.75f),
            new Color(0.95f, 0.95f, 0.95f), new Color(0.06f, 0.06f, 0.07f), new Color(0.45f, 0.06f, 0.1f)
        };
        for (int i = 0; i < colours.Length; i++)
            Add(manager, folder, MakeCar("Car " + colourNames[i], Mat("Car" + colourNames[i], colours[i], 0.6f), false), 8f);
        Add(manager, folder, MakeCar("Taxi", Mat("CarWhite", colours[3], 0.6f), true), 10f);
        Add(manager, folder, MakeMicrobus(), 8f);
        Add(manager, folder, MakeBus(), 4f);
        Add(manager, folder, MakeTruck(), 3f);
        Add(manager, folder, MakeMotorbike("Motorbike", new Color(0.1f, 0.1f, 0.12f), new Color(0.75f, 0.1f, 0.1f)), 14f);
        Add(manager, folder, MakeMotorbike("Scooty", new Color(0.85f, 0.85f, 0.88f), new Color(0.2f, 0.35f, 0.7f)), 10f);

        Selection.activeGameObject = root;
        EditorUtility.DisplayDialog("Traffic added",
            manager.templates.Count + " kinds of vehicle, up to " + manager.maxVehicles + " on the road at once.\n\n" +
            "Press Play: traffic appears around your car, drives on the LEFT, stops at red lights and zebra crossings, and honks if you block it!",
            "Great!");
    }

    private static void Add(TrafficManager manager, GameObject folder, GameObject vehicle, float weight)
    {
        vehicle.transform.SetParent(folder.transform, false);
        manager.templates.Add(vehicle);
        manager.weights.Add(weight);
    }

    // =========================================================================
    //  The vehicles. Every one faces +z (forward), with the ground at y = 0.
    // =========================================================================
    private static GameObject MakeCar(string name, Material paint, bool taxi)
    {
        TrafficCar car = NewVehicle(name, VehicleKind.Car, 4.0f, 1.7f, 1.5f);
        if (taxi) car.kind = VehicleKind.Taxi;
        GameObject go = car.gameObject;
        Part(go, PrimitiveType.Cube, "Body", new Vector3(0f, 0.72f, 0f), new Vector3(1.7f, 0.6f, 4.0f), paint);
        Part(go, PrimitiveType.Cube, "Cabin", new Vector3(0f, 1.25f, -0.2f), new Vector3(1.5f, 0.5f, 2.0f), Mat("Glass", new Color(0.12f, 0.16f, 0.2f), 0.9f));
        Part(go, PrimitiveType.Cube, "Roof", new Vector3(0f, 1.52f, -0.3f), new Vector3(1.52f, 0.05f, 1.7f), paint);
        if (taxi)
            Part(go, PrimitiveType.Cube, "Taxi sign", new Vector3(0f, 1.66f, -0.2f), new Vector3(0.6f, 0.22f, 0.25f), Mat("TaxiSign", new Color(1f, 0.82f, 0.1f), 0.4f, true));
        // Nepal: private cars have RED plates, taxis and public vehicles BLACK.
        Material plate = taxi ? Mat("PlateBlack", new Color(0.05f, 0.05f, 0.05f), 0.3f) : Mat("PlateRed", new Color(0.75f, 0.05f, 0.05f), 0.3f);
        Plates(go, plate, 2.0f, 0.55f);
        Lights(car, 0.6f, 0.85f, 2.0f, 0.8f);
        Wheels(car, 0.78f, 1.3f, 0.32f);
        return go;
    }

    private static GameObject MakeMicrobus()
    {
        TrafficCar bus = NewVehicle("Microbus (Hiace)", VehicleKind.Microbus, 5.2f, 1.9f, 2.2f);
        bus.speedFactor = 0.95f;
        GameObject go = bus.gameObject;
        Material white = Mat("MicrobusWhite", new Color(0.93f, 0.94f, 0.95f), 0.5f);
        Part(go, PrimitiveType.Cube, "Body", new Vector3(0f, 1.15f, 0f), new Vector3(1.9f, 1.6f, 5.2f), white);
        Part(go, PrimitiveType.Cube, "Windows", new Vector3(0f, 1.55f, 0.05f), new Vector3(1.92f, 0.55f, 4.9f), Mat("Glass", new Color(0.12f, 0.16f, 0.2f), 0.9f));
        Part(go, PrimitiveType.Cube, "Stripe", new Vector3(0f, 1.0f, 0f), new Vector3(1.93f, 0.18f, 5.22f), Mat("MicrobusStripe", new Color(0.1f, 0.3f, 0.7f), 0.4f));
        Plates(go, Mat("PlateBlack", new Color(0.05f, 0.05f, 0.05f), 0.3f), 2.6f, 0.6f);
        Lights(bus, 0.7f, 0.75f, 2.6f, 0.88f);
        Wheels(bus, 0.85f, 1.8f, 0.36f);
        return go;
    }

    // Local buses in Pokhara are painted in bright colours, often with
    // patterns and writing. Ours: red bottom, yellow middle, decorated stripes.
    private static GameObject MakeBus()
    {
        TrafficCar bus = NewVehicle("Local bus", VehicleKind.Bus, 9.5f, 2.4f, 3.0f);
        bus.speedFactor = 0.8f; bus.acceleration = 1.3f; bus.braking = 4.5f; bus.mainRoadsOnly = true;
        GameObject go = bus.gameObject;
        Part(go, PrimitiveType.Cube, "Lower body", new Vector3(0f, 1.05f, 0f), new Vector3(2.4f, 1.3f, 9.5f), Mat("BusRed", new Color(0.78f, 0.1f, 0.08f), 0.4f));
        Part(go, PrimitiveType.Cube, "Upper body", new Vector3(0f, 2.35f, 0f), new Vector3(2.4f, 1.3f, 9.5f), Mat("BusYellow", new Color(0.98f, 0.78f, 0.15f), 0.4f));
        Part(go, PrimitiveType.Cube, "Windows", new Vector3(0f, 2.3f, 0.1f), new Vector3(2.42f, 0.75f, 9.0f), Mat("Glass", new Color(0.12f, 0.16f, 0.2f), 0.9f));
        Part(go, PrimitiveType.Cube, "Pattern stripe", new Vector3(0f, 1.5f, 0f), new Vector3(2.43f, 0.15f, 9.52f), Mat("BusGreen", new Color(0.1f, 0.55f, 0.25f), 0.4f));
        Part(go, PrimitiveType.Cube, "Pattern stripe 2", new Vector3(0f, 1.75f, 0f), new Vector3(2.43f, 0.1f, 9.52f), Mat("BusBlue", new Color(0.15f, 0.3f, 0.75f), 0.4f));
        Part(go, PrimitiveType.Cube, "Roof rack", new Vector3(0f, 3.1f, -0.5f), new Vector3(2.2f, 0.18f, 6f), Mat("Rack", new Color(0.2f, 0.2f, 0.2f), 0.2f));
        Plates(go, Mat("PlateBlack", new Color(0.05f, 0.05f, 0.05f), 0.3f), 4.76f, 0.6f);
        Lights(bus, 0.9f, 0.8f, 4.76f, 1.05f);
        Wheels(bus, 1.05f, 3.4f, 0.5f);
        return go;
    }

    // Tata trucks: orange cab, a high wooden cargo box painted with colours.
    private static GameObject MakeTruck()
    {
        TrafficCar truck = NewVehicle("Tata truck", VehicleKind.Truck, 7.5f, 2.4f, 3.0f);
        truck.speedFactor = 0.75f; truck.acceleration = 1.1f; truck.braking = 4f; truck.mainRoadsOnly = true;
        GameObject go = truck.gameObject;
        Part(go, PrimitiveType.Cube, "Cab", new Vector3(0f, 1.5f, 2.75f), new Vector3(2.3f, 2.0f, 2.0f), Mat("TruckOrange", new Color(0.95f, 0.45f, 0.08f), 0.4f));
        Part(go, PrimitiveType.Cube, "Windscreen", new Vector3(0f, 2.0f, 3.76f), new Vector3(2.0f, 0.7f, 0.04f), Mat("Glass", new Color(0.12f, 0.16f, 0.2f), 0.9f));
        Part(go, PrimitiveType.Cube, "Cargo floor", new Vector3(0f, 1.0f, -1.0f), new Vector3(2.4f, 0.3f, 5.4f), Mat("Rack", new Color(0.2f, 0.2f, 0.2f), 0.2f));
        Part(go, PrimitiveType.Cube, "Cargo box", new Vector3(0f, 1.95f, -1.0f), new Vector3(2.4f, 1.6f, 5.4f), Mat("TruckBox", new Color(0.55f, 0.32f, 0.15f), 0.2f));
        Part(go, PrimitiveType.Cube, "Painted band", new Vector3(0f, 2.55f, -1.0f), new Vector3(2.42f, 0.3f, 5.42f), Mat("BusYellow", new Color(0.98f, 0.78f, 0.15f), 0.4f));
        Part(go, PrimitiveType.Cube, "Painted band 2", new Vector3(0f, 1.55f, -1.0f), new Vector3(2.42f, 0.2f, 5.42f), Mat("BusGreen", new Color(0.1f, 0.55f, 0.25f), 0.4f));
        Plates(go, Mat("PlateBlack", new Color(0.05f, 0.05f, 0.05f), 0.3f), 3.76f, 0.8f);
        Lights(truck, 0.9f, 1.0f, 3.76f, 1.05f);
        Wheels(truck, 1.0f, 2.6f, 0.5f);
        // Extra rear wheels (trucks have 2 rear axles... or at least look like it)
        var extra = NewVehicleWheels(truck, 1.0f, -2.8f, 0.5f);
        var all = new List<Transform>(truck.wheels); all.AddRange(extra); truck.wheels = all.ToArray();
        return go;
    }

    // A motorbike with a rider (helmets are the law in Nepal!).
    private static GameObject MakeMotorbike(string name, Color bikeColour, Color shirtColour)
    {
        TrafficCar bike = NewVehicle(name, VehicleKind.Motorbike, 1.9f, 0.7f, 1.6f);
        bike.speedFactor = 1.05f; bike.acceleration = 3.5f; bike.braking = 7f;
        bike.sideOffset = -0.9f;   // keeps to the far left of the lane
        bike.wheelRadius = 0.3f;
        GameObject go = bike.gameObject;
        Material paint = Mat("Bike" + name, bikeColour, 0.6f);
        Part(go, PrimitiveType.Cube, "Frame", new Vector3(0f, 0.6f, 0f), new Vector3(0.3f, 0.35f, 1.3f), paint);
        Part(go, PrimitiveType.Cube, "Seat", new Vector3(0f, 0.82f, -0.25f), new Vector3(0.32f, 0.1f, 0.7f), Mat("Seat", new Color(0.05f, 0.05f, 0.05f), 0.3f));
        Part(go, PrimitiveType.Cube, "Handlebar", new Vector3(0f, 1.05f, 0.55f), new Vector3(0.7f, 0.05f, 0.05f), Mat("Rack", new Color(0.2f, 0.2f, 0.2f), 0.2f));
        // Rider
        Part(go, PrimitiveType.Capsule, "Rider", new Vector3(0f, 1.25f, -0.15f), new Vector3(0.45f, 0.45f, 0.35f), Mat("Shirt" + name, shirtColour, 0.2f));
        Part(go, PrimitiveType.Sphere, "Helmet", new Vector3(0f, 1.82f, -0.08f), Vector3.one * 0.3f, Mat("Helmet", new Color(0.95f, 0.95f, 0.95f), 0.7f));
        Part(go, PrimitiveType.Cube, "Headlight", new Vector3(0f, 0.85f, 0.7f), new Vector3(0.14f, 0.12f, 0.05f), Mat("Headlight", new Color(1f, 0.98f, 0.9f), 0.8f, true));
        GameObject brake = Part(go, PrimitiveType.Cube, "Brake light", new Vector3(0f, 0.8f, -0.66f), new Vector3(0.14f, 0.08f, 0.04f), Mat("BrakeOn", new Color(1f, 0.05f, 0.05f), 0.6f, true));
        brake.SetActive(false);
        bike.brakeLights = new[] { brake };
        // Two wheels in a line
        var wheels = new List<Transform>();
        foreach (float z in new[] { 0.62f, -0.62f })
        {
            var holder = new GameObject("Wheel");
            holder.transform.SetParent(go.transform, false);
            holder.transform.localPosition = new Vector3(0f, 0.3f, z);
            Part(holder, PrimitiveType.Cylinder, "Tyre", Vector3.zero, new Vector3(0.6f, 0.06f, 0.6f), Mat("Tyre", new Color(0.05f, 0.05f, 0.05f), 0.1f))
                .transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
            wheels.Add(holder.transform);
        }
        bike.wheels = wheels.ToArray();
        return go;
    }

    // =========================================================================
    //  Shared pieces
    // =========================================================================

    // The vehicle root: script + "kinematic" Rigidbody (moved by the script,
    // but still solid, so you can bump into it) + one box collider.
    private static TrafficCar NewVehicle(string name, VehicleKind kind, float length, float width, float height)
    {
        var go = new GameObject(name);
        TrafficCar car = go.AddComponent<TrafficCar>();
        car.kind = kind; car.length = length; car.width = width;
        Rigidbody body = go.AddComponent<Rigidbody>();
        body.isKinematic = true;
        body.useGravity = false;
        body.interpolation = RigidbodyInterpolation.Interpolate;
        BoxCollider box = go.AddComponent<BoxCollider>();
        box.center = new Vector3(0f, height / 2f + 0.15f, 0f);
        box.size = new Vector3(width, height - 0.3f, length);
        return car;
    }

    private static void Plates(GameObject go, Material plate, float halfLength, float y)
    {
        Part(go, PrimitiveType.Cube, "Plate front", new Vector3(0f, y, halfLength + 0.01f), new Vector3(0.5f, 0.14f, 0.03f), plate);
        Part(go, PrimitiveType.Cube, "Plate rear", new Vector3(0f, y, -halfLength - 0.01f), new Vector3(0.5f, 0.14f, 0.03f), plate);
    }

    // Head, tail, indicator and brake lights at both corners.
    private static void Lights(TrafficCar car, float x, float y, float halfLength, float indicatorX)
    {
        GameObject go = car.gameObject;
        Material head = Mat("Headlight", new Color(1f, 0.98f, 0.9f), 0.8f, true);
        Material tail = Mat("TailLight", new Color(0.45f, 0.03f, 0.03f), 0.6f);
        Material brake = Mat("BrakeOn", new Color(1f, 0.05f, 0.05f), 0.6f, true);
        Material orange = Mat("IndicatorOn", new Color(1f, 0.55f, 0f), 0.6f, true);
        var left = new List<GameObject>(); var right = new List<GameObject>(); var brakes = new List<GameObject>();
        for (int side = -1; side <= 1; side += 2)
        {
            Part(go, PrimitiveType.Cube, "Headlight", new Vector3(side * x, y, halfLength), new Vector3(0.36f, 0.15f, 0.04f), head);
            Part(go, PrimitiveType.Cube, "Tail light", new Vector3(side * x, y, -halfLength), new Vector3(0.36f, 0.15f, 0.04f), tail);
            GameObject b = Part(go, PrimitiveType.Cube, "Brake glow", new Vector3(side * x, y, -halfLength - 0.025f), new Vector3(0.37f, 0.16f, 0.03f), brake);
            b.SetActive(false); brakes.Add(b);
            foreach (float z in new[] { halfLength + 0.025f, -halfLength - 0.025f })
            {
                GameObject ind = Part(go, PrimitiveType.Cube, "Indicator glow", new Vector3(side * indicatorX, y, z), new Vector3(0.14f, 0.12f, 0.03f), orange);
                ind.SetActive(false);
                (side < 0 ? left : right).Add(ind);   // car faces +z, so -x is LEFT
            }
        }
        car.leftIndicators = left.ToArray();
        car.rightIndicators = right.ToArray();
        car.brakeLights = brakes.ToArray();
    }

    private static void Wheels(TrafficCar car, float x, float z, float radius)
    {
        var list = new List<Transform>();
        list.AddRange(NewVehicleWheels(car, x, z, radius));
        list.AddRange(NewVehicleWheels(car, x, -z, radius));
        car.wheels = list.ToArray();
        car.wheelRadius = radius;
    }

    // A pair of wheels (left + right) on one axle.
    private static List<Transform> NewVehicleWheels(TrafficCar car, float x, float z, float radius)
    {
        var result = new List<Transform>();
        Material tyre = Mat("Tyre", new Color(0.05f, 0.05f, 0.05f), 0.1f);
        for (int side = -1; side <= 1; side += 2)
        {
            // The holder spins; the cylinder inside is turned on its side.
            var holder = new GameObject("Wheel");
            holder.transform.SetParent(car.transform, false);
            holder.transform.localPosition = new Vector3(side * x, radius, z);
            Part(holder, PrimitiveType.Cylinder, "Tyre", Vector3.zero, new Vector3(radius * 2f, 0.12f, radius * 2f), tyre)
                .transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
            result.Add(holder.transform);
        }
        return result;
    }

    private static GameObject Part(GameObject parent, PrimitiveType type, string name, Vector3 localPosition, Vector3 localScale, Material material)
    {
        GameObject go = GameObject.CreatePrimitive(type);
        go.name = name;
        go.transform.SetParent(parent.transform, false);
        go.transform.localPosition = localPosition;
        go.transform.localScale = localScale;
        go.GetComponent<Renderer>().sharedMaterial = material;
        Object.DestroyImmediate(go.GetComponent<Collider>());   // the root's box collider is enough
        return go;
    }

    private static Material Mat(string name, Color colour, float smoothness, bool glows = false)
    {
        string path = MaterialsFolder + "/" + name + ".mat";
        Material existing = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (existing != null) return existing;
        PokharaCityImporterWindow.EnsureFolder(MaterialsFolder);
        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null) shader = Shader.Find("Standard");
        var mat = new Material(shader) { name = name, color = colour };
        if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", colour);
        if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", smoothness);
        if (glows)
        {
            mat.EnableKeyword("_EMISSION");
            mat.SetColor("_EmissionColor", colour * 2.5f);
        }
        AssetDatabase.CreateAsset(mat, path);
        return mat;
    }
}
