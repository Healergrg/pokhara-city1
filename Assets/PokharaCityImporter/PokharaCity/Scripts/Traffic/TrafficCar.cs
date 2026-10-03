// =============================================================================
//  TrafficCar.cs  —  one AI vehicle (car, taxi, bus, truck or motorbike)
// =============================================================================
//  How it drives, like a careful Pokhara driver:
//
//    1. FOLLOW THE LANE   It slides along the lane's points (always on the
//                         LEFT of the road, the importer put lanes there).
//    2. PICK THE NEXT ROAD  About 55 m before a junction it picks where to go
//                         next (straight on is the most likely) and switches
//                         on its indicator if it is going to turn.
//    3. TURN SMOOTHLY     At the junction it drives a curve onto the next lane.
//    4. SLOW DOWN FOR     - bends
//                         - the vehicle in front (keeps a gap)
//                         - red lights (and amber if it can stop in time)
//                         - people on zebra crossings
//                         - YOU (and it honks if you block it too long!)
//
//  It doesn't use WheelColliders like your car. It is "kinematic": the script
//  moves it exactly where it should be, like a toy car on a track. That is
//  much cheaper, so we can have 40 of them.
// =============================================================================

using System.Collections.Generic;
using UnityEngine;

public enum VehicleKind { Car, Taxi, Microbus, Bus, Truck, Motorbike }

public class TrafficCar : MonoBehaviour
{
    [Header("What kind of vehicle (set by the builder)")]
    public VehicleKind kind = VehicleKind.Car;
    public float length = 4f;
    public float width = 1.7f;
    public float speedFactor = 1f;        // 1 = drives at the speed limit
    public float acceleration = 2.5f;     // m/s per second
    public float braking = 6f;            // m/s per second
    public bool mainRoadsOnly = false;    // buses and trucks stay on big roads
    public float sideOffset = 0f;         // motorbikes ride a little to the left (negative)

    [Header("Moving parts (set by the builder)")]
    public Transform[] wheels;
    public float wheelRadius = 0.32f;
    public GameObject[] leftIndicators, rightIndicators, brakeLights;

    // ---- what other scripts can read ---------------------------------------
    public Vector3 Position { get; private set; }
    public Vector3 Heading { get; private set; } = new Vector3(0f, 0f, 1f);
    public float Speed { get; private set; }            // metres per second
    public int Lane { get; private set; } = -1;
    public bool Finished { get; private set; }          // reached a dead end: remove me

    // ---- private memory ------------------------------------------------------
    private TrafficManager manager;
    private Rigidbody body;
    private AudioSource horn;
    private readonly List<Vector3> path = new List<Vector3>();
    private readonly List<float> along = new List<float>();
    private float distance;          // how far along 'path' we are (metres)
    private int nextLane = -1;
    private int turnSignal;          // -1 left, 0 none, +1 right
    private float signalOffAt;       // stop blinking after this distance on the new lane
    private float mood = 1f;         // some drivers are a bit slower than others
    private float blockedTimer, hornCooldown;
    private Object ignored; private float ignoreTimer;
    private float wheelSpin;
    private const float RoadLift = 0.05f;   // roads are ~5 cm thick

    // Called by the TrafficManager right after the vehicle is made.
    public void Begin(TrafficManager owner, int laneIndex, float startDistance, float driverMood)
    {
        manager = owner;
        mood = driverMood;
        body = GetComponent<Rigidbody>();
        Lane = laneIndex;
        path.Clear();
        AddLanePoints(laneIndex, 0f);
        distance = Mathf.Min(startDistance, PathLength - 0.1f);
        Speed = manager.LaneSpeed(laneIndex) * 0.6f;
        Place(0f, true);

        if (manager.HornClip != null)
        {
            horn = gameObject.AddComponent<AudioSource>();
            horn.clip = manager.HornClip;
            horn.spatialBlend = 1f;       // 3D sound: louder when close
            horn.minDistance = 5f;
            horn.maxDistance = 80f;
            horn.pitch = kind == VehicleKind.Bus || kind == VehicleKind.Truck ? 0.7f : (kind == VehicleKind.Motorbike ? 1.4f : 1f);
            horn.playOnAwake = false;
        }
    }

    private float PathLength => along.Count > 0 ? along[along.Count - 1] : 0f;

    // =========================================================================
    private void FixedUpdate()
    {
        if (manager != null) Drive(Time.fixedDeltaTime);
    }

    // The whole driving brain, one physics step at a time (public for testing).
    public void Drive(float dt)
    {
        if (Finished || path.Count < 2) return;
        float remaining = PathLength - distance;

        // ---- 2. pick the next lane before reaching the junction --------------
        if (nextLane < 0 && remaining < 55f)
        {
            nextLane = manager.ChooseNextLane(this, Lane);
            turnSignal = nextLane >= 0 ? manager.TurnDirection(Lane, nextLane) : 0;
        }

        // ---- 4. how fast should I go? ----------------------------------------
        float target = manager.LaneSpeed(Lane) * speedFactor * mood;
        target = Mathf.Min(target, BendSpeed());

        Object blocker;
        bool blockedByPlayer;
        float gap = manager.ClearDistanceAhead(this, ignored, out blocker, out blockedByPlayer);
        if (gap < 60f)
        {
            // The fastest speed from which we can still stop in 'gap' metres:
            // v = sqrt(2 x braking x distance)  (school physics!)
            float safe = Mathf.Sqrt(Mathf.Max(0f, 2f * braking * 0.6f * Mathf.Max(0f, gap)));
            target = Mathf.Min(target, gap < 0.3f ? 0f : safe);
        }

        float rate = target > Speed ? acceleration : braking;
        Speed = Mathf.MoveTowards(Speed, target, rate * dt);

        // Stuck behind something for a long time?
        if (Speed < 0.3f && gap < 8f)
        {
            blockedTimer += dt;
            if (blockedByPlayer && blockedTimer > 3f && hornCooldown <= 0f)
            {
                if (horn != null) horn.Play();          // beep beep! (very Nepali)
                hornCooldown = 5f + Random.Range(0f, 4f);
            }
            // Two AI cars waiting for each other at a junction: after 5 s,
            // one politely ignores the other for a moment and goes.
            if (!blockedByPlayer && blocker != null && blockedTimer > 5f)
            {
                ignored = blocker;
                ignoreTimer = 3f;
                blockedTimer = 0f;
            }
        }
        else blockedTimer = 0f;
        hornCooldown -= dt;
        if (ignoreTimer > 0f) { ignoreTimer -= dt; if (ignoreTimer <= 0f) ignored = null; }

        // ---- 1 + 3. move along the path, onto the next lane at the end -------
        distance += Speed * dt;
        while (distance >= PathLength)
        {
            if (!SwitchToNextLane()) { Finished = true; return; }
        }

        Place(dt, false);
        UpdateLights(target < Speed - 0.1f || Speed < 0.2f);
    }

    // Slow down for bends: compare the direction now with 12-14 m ahead.
    private float BendSpeed()
    {
        Vector3 now = Heading;
        Vector3 ahead;
        if (distance + 14f < PathLength) ahead = PointAt(distance + 14f) - PointAt(distance + 12f);
        else if (nextLane >= 0) ahead = manager.LaneStartDirection(nextLane);
        else return 99f;
        ahead.y = 0f;
        if (ahead.sqrMagnitude < 0.001f) return 99f;
        float angle = Vector3.Angle(now, ahead);
        if (angle > 120f) return 3f;     // U-turn: walking pace
        if (angle > 70f) return 4.5f;    // a proper corner: about 16 km/h
        if (angle > 35f) return 7f;      // a bend: about 25 km/h
        return 99f;
    }

    // Put the vehicle on the path and turn it to face where it is going.
    private void Place(float dt, bool instantly)
    {
        Vector3 p = PointAt(distance);
        Vector3 look = PointAt(Mathf.Min(distance + 2.5f, PathLength)) - p;
        look.y = 0f;
        if (look.sqrMagnitude < 0.0001f) look = Heading;
        Heading = instantly ? look.normalized : Vector3.Slerp(Heading, look.normalized, Mathf.Clamp01(8f * dt)).normalized;
        Position = p + Vector3.up * RoadLift;

        Quaternion rotation = Quaternion.LookRotation(Heading, Vector3.up);
        if (body != null && !instantly) { body.MovePosition(Position); body.MoveRotation(rotation); }
        else if (transform != null) { transform.position = Position; transform.rotation = rotation; }

        // Spin the wheels: distance / radius = angle in radians.
        if (wheels != null && wheels.Length > 0)
        {
            wheelSpin = (wheelSpin + Speed * dt / wheelRadius * Mathf.Rad2Deg) % 360f;
            foreach (Transform w in wheels) if (w != null) w.localRotation = Quaternion.Euler(wheelSpin, 0f, 0f);
        }
    }

    // At the end of a lane: drive a smooth curve onto the chosen next lane.
    private bool SwitchToNextLane()
    {
        float overflow = distance - PathLength;
        if (nextLane < 0) nextLane = manager.ChooseNextLane(this, Lane);
        if (nextLane < 0) return false;   // dead end: the manager removes us

        Vector3 end = path[path.Count - 1];
        Vector3 dirIn = (path[path.Count - 1] - path[path.Count - 2]); dirIn.y = 0f; dirIn = dirIn.normalized;

        List<Vector3> next = manager.LanePoints(nextLane);
        path.Clear(); along.Clear();

        // Find the point about 6 m into the next lane; the curve ends there.
        int join = 0; float run = 0f;
        while (join < next.Count - 1 && run < 6f) { run += Vector3.Distance(next[join], next[join + 1]); join++; }
        Vector3 target = Offset(next, join);

        // A "Bezier curve": starts at our position, is pulled towards a point
        // straight ahead of us, and ends on the new lane. Like bending a ruler.
        float pull = Vector3.Distance(end, target) * 0.5f;
        Vector3 control = end + dirIn * pull;
        for (int i = 0; i <= 6; i++)
        {
            float t = i / 6f;
            float a = (1 - t) * (1 - t), b = 2 * (1 - t) * t, c = t * t;
            AddPoint(end * a + control * b + target * c);
        }
        for (int i = join + 1; i < next.Count; i++) AddPoint(Offset(next, i));

        Lane = nextLane;
        nextLane = -1;
        distance = Mathf.Min(overflow, PathLength * 0.5f);
        signalOffAt = 12f;   // keep blinking until we are 12 m into the new road
        return PathLength > 0.5f;
    }

    private void UpdateLights(bool braking)
    {
        bool lampTime = (Time.time % 0.8f) < 0.4f;
        bool signalling = turnSignal != 0 && (PathLength - distance < 40f || distance < signalOffAt);
        if (!signalling && distance >= signalOffAt && nextLane < 0) turnSignal = 0;
        SetAll(leftIndicators, signalling && turnSignal < 0 && lampTime);
        SetAll(rightIndicators, signalling && turnSignal > 0 && lampTime);
        SetAll(brakeLights, braking);
    }

    private static void SetAll(GameObject[] lamps, bool on)
    {
        if (lamps == null) return;
        foreach (GameObject g in lamps) if (g != null && g.activeSelf != on) g.SetActive(on);
    }

    // -------------------------------------------------------------------------
    //  "Will my route drive into this circle soon?" Looks up to 'maxAhead'
    //  metres along the road I am ABOUT to drive (including the turn onto my
    //  next lane), like a driver reading the road ahead, not just looking
    //  straight through the windscreen.
    //  Returns how far away the circle starts, and my direction at that point.
    // -------------------------------------------------------------------------
    public bool RouteEnters(Vector3 centre, float radius, float maxAhead, out float metres, out Vector3 direction)
    {
        metres = 0f; direction = Heading;
        float r2 = radius * radius;
        if (FlatDistance2(PointAt(distance), centre) < r2) return false;   // already inside: carry on

        Vector3 previous = PointAt(distance);
        for (float s = 1f; s <= maxAhead; s += 1f)
        {
            Vector3 p = RouteAhead(s);
            if (FlatDistance2(p, centre) < r2)
            {
                metres = s;
                direction = p - previous; direction.y = 0f;
                direction = direction.sqrMagnitude > 0.0001f ? direction.normalized : Heading;
                return true;
            }
            previous = p;
        }
        return false;
    }

    // A point 's' metres ahead: on my path, or (past its end) on the next lane.
    private Vector3 RouteAhead(float s)
    {
        float d = distance + s;
        if (d <= PathLength || nextLane < 0) return PointAt(d);
        List<Vector3> next = manager.LanePoints(nextLane);
        float left = d - PathLength;
        for (int i = 1; i < next.Count; i++)
        {
            float seg = Vector3.Distance(next[i - 1], next[i]);
            if (left <= seg) return Vector3.Lerp(next[i - 1], next[i], seg > 0f ? left / seg : 0f);
            left -= seg;
        }
        return next[next.Count - 1];
    }

    private static float FlatDistance2(Vector3 a, Vector3 b)
    {
        float dx = a.x - b.x, dz = a.z - b.z;
        return dx * dx + dz * dz;
    }

    // ---- path helpers ---------------------------------------------------------
    private void AddLanePoints(int laneIndex, float from)
    {
        List<Vector3> pts = manager.LanePoints(laneIndex);
        for (int i = 0; i < pts.Count; i++) AddPoint(Offset(pts, i));
    }

    // Motorbikes ride a little further left than cars.
    private Vector3 Offset(List<Vector3> pts, int i)
    {
        if (Mathf.Abs(sideOffset) < 0.01f || pts.Count < 2) return pts[i];
        Vector3 dir = i < pts.Count - 1 ? pts[i + 1] - pts[i] : pts[i] - pts[i - 1];
        dir.y = 0f;
        Vector3 right = new Vector3(dir.z, 0f, -dir.x).normalized;
        return pts[i] + right * sideOffset;
    }

    private void AddPoint(Vector3 p)
    {
        if (path.Count > 0 && (p - path[path.Count - 1]).sqrMagnitude < 0.0004f) return;
        along.Add(path.Count == 0 ? 0f : along[along.Count - 1] + Vector3.Distance(path[path.Count - 1], p));
        path.Add(p);
    }

    private Vector3 PointAt(float d)
    {
        if (d <= 0f) return path[0];
        for (int i = 1; i < path.Count; i++)
        {
            if (along[i] >= d)
            {
                float seg = along[i] - along[i - 1];
                float t = seg > 0.0001f ? (d - along[i - 1]) / seg : 0f;
                return Vector3.Lerp(path[i - 1], path[i], t);
            }
        }
        return path[path.Count - 1];
    }
}
