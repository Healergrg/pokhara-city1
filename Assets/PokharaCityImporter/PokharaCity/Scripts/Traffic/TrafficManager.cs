// =============================================================================
//  TrafficManager.cs  —  fills the streets around you with traffic
// =============================================================================
//  Like a stage manager in a theatre: actors (vehicles) only need to be on
//  stage where the audience (you) can see them. So:
//
//    - it keeps about 40 vehicles within ~260 m of your car
//    - new ones appear 70-260 m away (never right in front of your eyes)
//    - ones that get more than 330 m away disappear
//
//  It also answers the vehicles' questions:
//    "Where can I go next?"   "How fast is this road?"
//    "How much free road is in front of me?" (cars, you, red lights, zebras)
// =============================================================================

using System.Collections.Generic;
using UnityEngine;

public class TrafficManager : MonoBehaviour
{
    [Header("Found automatically when you press Play")]
    public RoadNetwork network;
    public Transform player;

    [Header("Vehicle templates (made by the builder) and how common each is")]
    public List<GameObject> templates = new List<GameObject>();
    public List<float> weights = new List<float>();

    [Header("How much traffic")]
    public int maxVehicles = 40;
    public float spawnMinDistance = 70f;
    public float spawnMaxDistance = 260f;
    public float despawnDistance = 330f;

    public AudioClip HornClip { get; private set; }
    public AudioClip EngineClip { get; private set; }   // shared by every AI vehicle
    public static TrafficManager Instance { get; private set; }   // so pedestrians can check for traffic
    public readonly List<TrafficCar> vehicles = new List<TrafficCar>();

    private TrafficSignal[] signals = new TrafficSignal[0];
    private ZebraCrossing[] crossings = new ZebraCrossing[0];
    private Rigidbody playerBody;
    private float spawnTimer;
    private float[] laneLength;
    private bool[] laneIsMain;
    private float totalLength;

    private static readonly HashSet<string> MainTypes = new HashSet<string> { "trunk", "primary", "secondary", "tertiary" };

    // =========================================================================
    private void Start()
    {
        if (network == null) network = FindFirstObjectByType<RoadNetwork>();
        if (player == null)
        {
            PokharaCar car = FindFirstObjectByType<PokharaCar>();
            if (car != null) player = car.transform;
        }
        signals = FindObjectsByType<TrafficSignal>(FindObjectsSortMode.None);
        crossings = FindObjectsByType<ZebraCrossing>(FindObjectsSortMode.None);
        if (player != null) playerBody = player.GetComponent<Rigidbody>();
        HornClip = CarSounds.Horn();
        EngineClip = CarSounds.Engine();

        if (network == null || network.lanes.Count == 0)
        {
            Debug.LogWarning("Traffic: no Road Network found. Build the city first.");
            enabled = false;
            return;
        }
        Setup(network, signals, crossings);

        // Fill the streets straight away (closer is OK at the start: you
        // haven't looked around yet).
        for (int i = 0; i < maxVehicles * 3 && vehicles.Count < maxVehicles; i++) TrySpawn(25f);
    }

    // Separate from Start so the test program can call it too.
    public void Setup(RoadNetwork roads, TrafficSignal[] allSignals, ZebraCrossing[] allCrossings)
    {
        Instance = this;
        network = roads;
        signals = allSignals ?? new TrafficSignal[0];
        crossings = allCrossings ?? new ZebraCrossing[0];
        int n = network.lanes.Count;
        laneLength = new float[n];
        laneIsMain = new bool[n];
        totalLength = 0f;
        for (int i = 0; i < n; i++)
        {
            List<Vector3> p = network.lanes[i].points;
            float len = 0f;
            for (int k = 1; k < p.Count; k++) len += Vector3.Distance(p[k - 1], p[k]);
            laneLength[i] = len;
            laneIsMain[i] = MainTypes.Contains(network.lanes[i].roadType);
            totalLength += len;
        }
    }

    private void Update()
    {
        Vector3 centre = PlayerPosition();

        // Remove vehicles that drove far away or reached a dead end.
        for (int i = vehicles.Count - 1; i >= 0; i--)
        {
            TrafficCar v = vehicles[i];
            if (v == null) { vehicles.RemoveAt(i); continue; }
            Vector3 d = v.Position - centre; d.y = 0f;
            if (v.Finished || d.magnitude > despawnDistance)
            {
                vehicles.RemoveAt(i);
                Destroy(v.gameObject);
            }
        }

        // Add new ones, a couple at a time.
        spawnTimer -= Time.deltaTime;
        if (spawnTimer <= 0f && vehicles.Count < maxVehicles)
        {
            spawnTimer = 0.3f;
            TrySpawn(spawnMinDistance);
        }
    }

    // Remove traffic near a spot (used when a mission puts your car at its start).
    public void ClearAround(Vector3 spot, float radius)
    {
        for (int i = vehicles.Count - 1; i >= 0; i--)
        {
            TrafficCar v = vehicles[i];
            if (v == null) { vehicles.RemoveAt(i); continue; }
            Vector3 d = v.Position - spot; d.y = 0f;
            if (d.magnitude < radius) { vehicles.RemoveAt(i); Destroy(v.gameObject); }
        }
    }

    public Vector3 PlayerPosition()
    {
        if (player != null) return player.position;
        if (Camera.main != null) return Camera.main.transform.position;
        return Vector3.zero;
    }

    // =========================================================================
    //  Spawning
    // =========================================================================
    public bool TrySpawn(float minDistance)
    {
        if (templates.Count == 0) return false;
        int template = PickTemplate();
        TrafficCar prefab = templates[template].GetComponent<TrafficCar>();
        Vector3 centre = PlayerPosition();

        for (int attempt = 0; attempt < 25; attempt++)
        {
            // Pick a random spot on a random lane (longer lanes more likely).
            int lane = RandomLane();
            if (lane < 0 || laneLength[lane] < 12f) continue;
            if (prefab != null && prefab.mainRoadsOnly && !laneIsMain[lane]) continue;
            float d = Random.Range(5f, laneLength[lane] - 5f);
            Vector3 p = PointOnLane(lane, d);

            float fromPlayer = Vector3.Distance(new Vector3(p.x, 0f, p.z), new Vector3(centre.x, 0f, centre.z));
            if (fromPlayer < minDistance || fromPlayer > spawnMaxDistance) continue;
            if (!SpotIsFree(p, 14f)) continue;

            TrafficCar car = CreateVehicle(template);
            if (car == null) return false;
            car.Begin(this, lane, d, Random.Range(0.8f, 1.05f));
            vehicles.Add(car);
            return true;
        }
        return false;
    }

    // Overridden by the test program (which has no real Unity objects).
    protected virtual TrafficCar CreateVehicle(int template)
    {
        GameObject go = Instantiate(templates[template], transform);
        go.name = "Traffic - " + templates[template].name;
        go.SetActive(true);
        return go.GetComponent<TrafficCar>();
    }

    private int PickTemplate()
    {
        float total = 0f;
        for (int i = 0; i < templates.Count; i++) total += i < weights.Count ? weights[i] : 1f;
        float r = Random.Range(0f, total);
        for (int i = 0; i < templates.Count; i++)
        {
            r -= i < weights.Count ? weights[i] : 1f;
            if (r <= 0f) return i;
        }
        return templates.Count - 1;
    }

    private int RandomLane()
    {
        float r = Random.Range(0f, totalLength);
        for (int i = 0; i < laneLength.Length; i++)
        {
            r -= laneLength[i];
            if (r <= 0f) return i;
        }
        return laneLength.Length - 1;
    }

    private bool SpotIsFree(Vector3 p, float radius)
    {
        foreach (TrafficCar v in vehicles)
        {
            if (v == null) continue;
            Vector3 d = v.Position - p; d.y = 0f;
            if (d.sqrMagnitude < radius * radius) return false;
        }
        return true;
    }

    // =========================================================================
    //  Questions the vehicles ask
    // =========================================================================
    public List<Vector3> LanePoints(int lane) { return network.lanes[lane].points; }

    public float LaneSpeed(int lane)   // metres per second
    {
        float kmh = network.lanes[lane].speedLimitKmh;
        if (kmh <= 0f) kmh = 20f;
        return kmh / 3.6f;
    }

    public Vector3 LaneStartDirection(int lane)
    {
        List<Vector3> p = network.lanes[lane].points;
        int k = Mathf.Min(p.Count - 1, 2);
        Vector3 d = p[k] - p[0]; d.y = 0f;
        return d.sqrMagnitude > 0.0001f ? d.normalized : new Vector3(0f, 0f, 1f);
    }

    private Vector3 LaneEndDirection(int lane)
    {
        List<Vector3> p = network.lanes[lane].points;
        int k = Mathf.Max(0, p.Count - 3);
        Vector3 d = p[p.Count - 1] - p[k]; d.y = 0f;
        return d.sqrMagnitude > 0.0001f ? d.normalized : new Vector3(0f, 0f, 1f);
    }

    // -1 = turning left, +1 = turning right, 0 = (nearly) straight on.
    public int TurnDirection(int fromLane, int toLane)
    {
        Vector3 a = LaneEndDirection(fromLane), b = LaneStartDirection(toLane);
        float angle = Vector3.SignedAngle(a, b, Vector3.up);   // + = clockwise = right
        if (angle < -30f) return -1;
        if (angle > 30f) return 1;
        return 0;
    }

    // Where next? Straight on is the most likely, like real traffic.
    public int ChooseNextLane(TrafficCar car, int lane)
    {
        List<int> options = network.GetNextLanes(lane);
        if (options.Count == 0) return -1;

        // Dead end (the only way is back): vanish if you can't see it, else turn round.
        Lane current = network.lanes[lane];
        bool onlyUTurn = true;
        foreach (int o in options) if (network.lanes[o].toNode != current.fromNode) onlyUTurn = false;
        if (onlyUTurn)
        {
            float seen = Vector3.Distance(car.Position, PlayerPosition());
            if (seen > 60f) return -1;
        }

        Vector3 dirIn = LaneEndDirection(lane);
        float total = 0f;
        var weightList = new List<float>();
        foreach (int o in options)
        {
            float w = 1f + 2.5f * Mathf.Max(0f, Vector3.Dot(dirIn, LaneStartDirection(o)));   // straight = up to 3.5x
            if (car.mainRoadsOnly && !laneIsMain[o]) w *= 0.02f;   // buses avoid the galli
            if (laneLength[o] < 3f) w *= 0.2f;
            weightList.Add(w);
            total += w;
        }
        float r = Random.Range(0f, total);
        for (int i = 0; i < options.Count; i++)
        {
            r -= weightList[i];
            if (r <= 0f) return options[i];
        }
        return options[options.Count - 1];
    }

    // How many metres of free road are in front of this vehicle?
    // (99 = nothing in the way)
    public float ClearDistanceAhead(TrafficCar me, Object ignore, out Object blocker, out bool blockedByPlayer)
    {
        blocker = null;
        blockedByPlayer = false;
        float best = 99f;
        Vector3 pos = me.Position, heading = me.Heading;
        Vector3 right = new Vector3(heading.z, 0f, -heading.x);
        const float lookAhead = 40f;

        // 1) Other traffic in my path.
        foreach (TrafficCar other in vehicles)
        {
            if (other == null || other == me || other == ignore) continue;
            Vector3 rel = other.Position - pos; rel.y = 0f;
            float ahead = Vector3.Dot(rel, heading);
            if (ahead <= 0f || ahead > lookAhead) continue;
            float side = Mathf.Abs(Vector3.Dot(rel, right));
            if (side > me.width / 2f + other.width / 2f + 0.3f) continue;
            float gap = ahead - me.length / 2f - other.length / 2f - 2f;
            if (gap < best) { best = gap; blocker = other; blockedByPlayer = false; }
        }

        // 2) You! (a bit wider margin: AI drivers are scared of learners)
        if (player != null)
        {
            Vector3 rel = player.position - pos; rel.y = 0f;
            float ahead = Vector3.Dot(rel, heading);
            float side = Mathf.Abs(Vector3.Dot(rel, right));
            if (ahead > 0f && ahead < lookAhead && side < me.width / 2f + 1.4f)
            {
                float gap = ahead - me.length / 2f - 2.2f - 2.5f;
                if (gap < best) { best = gap; blocker = null; blockedByPlayer = true; }
            }
        }

        // 3) Red lights: stop where my route reaches the white line.
        foreach (TrafficSignal s in signals)
        {
            if (s == null) continue;
            Vector3 to = s.transform.position - pos; to.y = 0f;
            if (to.magnitude > 70f) continue;   // too far to matter
            float metres; Vector3 dirThere;
            if (!me.RouteEnters(s.transform.position, s.stopRadius, 50f, out metres, out dirThere)) continue;
            float stopAt = metres - me.length / 2f - 0.5f;
            if (stopAt < -0.5f) continue;   // nose already over the line: keep going
            LightColour light = s.GetLightFor(dirThere);
            // Amber: stop only if there is room to stop comfortably.
            bool mustStop = light == LightColour.Red || (light == LightColour.Amber && stopAt > Speed2Stop(me));
            if (mustStop && stopAt < best) { best = Mathf.Max(0f, stopAt); blocker = null; blockedByPlayer = false; }
        }

        // 4) People on zebra crossings: stop before the stripes.
        foreach (ZebraCrossing z in crossings)
        {
            if (z == null || !z.PedestrianOnRoad) continue;
            Vector3 to = z.transform.position - pos; to.y = 0f;
            if (to.magnitude > 60f) continue;
            float metres; Vector3 dirThere;
            if (!me.RouteEnters(z.transform.position, z.roadHalfWidth + 0.5f, 45f, out metres, out dirThere)) continue;
            float stopAt = metres - me.length / 2f - 1f;
            if (stopAt < -0.5f) continue;
            if (stopAt < best) { best = Mathf.Max(0f, stopAt); blocker = null; blockedByPlayer = false; }
        }
        return best;
    }

    private static float Speed2Stop(TrafficCar me)
    {
        // distance needed to stop gently from the current speed: v^2 / (2 x 3 m/s^2)
        return me.Speed * me.Speed / 6f;
    }

    private Vector3 PointOnLane(int lane, float d)
    {
        List<Vector3> p = network.lanes[lane].points;
        float run = 0f;
        for (int i = 1; i < p.Count; i++)
        {
            float seg = Vector3.Distance(p[i - 1], p[i]);
            if (run + seg >= d) return Vector3.Lerp(p[i - 1], p[i], seg > 0f ? (d - run) / seg : 0f);
            run += seg;
        }
        return p[p.Count - 1];
    }
}
