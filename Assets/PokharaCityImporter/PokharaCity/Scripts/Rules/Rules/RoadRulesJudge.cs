// =============================================================================
//  RoadRulesJudge.cs  —  the "driving examiner" sitting next to you
// =============================================================================
//  Watches your car all the time and gives a fine (minus points) when you:
//
//    Rule                                   Fine   How it knows
//    -------------------------------------  -----  ---------------------------------
//    Drive on the right side (keep left!)    -10   you are closer to the lane going
//                                                  the OTHER way, for 3 seconds
//    Go the wrong way on a one-way road      -15   the only lane here points at you
//    Speed (more than 5 km/h over the limit) -5    for 2 seconds; limit = road type,
//                                                  or 20 km/h in a school zone
//    Turn at a junction without indicating   -5    your car turned more than 65
//                                                  degrees near a chowk and the
//                                                  indicator was never on
//    Indicate the wrong way                  -5    turned left with right indicator on
//    Jump a red light                        -20   crossed the stop line on red
//    Drive over a zebra with someone on it   -20
//    Hit a pedestrian                        -40
//    Crash into a wall, tree, pole...        -10
//    Drive off the road                      -5    for 2.5 seconds
//    Use the horn in a school/silent zone    -3
//
//  You start with 100 points. The RulesHUD shows your score and fines.
//  In Week 5 the missions will read Score to decide pass or fail.
// =============================================================================

using System.Collections.Generic;
using UnityEngine;

public class Violation
{
    public float time;
    public string message;
    public int points;
}

public class RoadRulesJudge : MonoBehaviour
{
    [Header("Found automatically when you press Play")]
    public PokharaCar car;
    public RoadNetwork network;

    [Header("Score")]
    public int startScore = 100;

    [Header("Fines (points taken away)")]
    public int keepLeftFine = 10;
    public int wrongWayFine = 15;
    public int speedingFine = 5;
    public int noIndicatorFine = 5;
    public int wrongIndicatorFine = 5;
    public int redLightFine = 20;
    public int zebraFine = 20;
    public int hitPedestrianFine = 40;
    public int crashFine = 10;
    public int offRoadFine = 5;
    public int hornFine = 3;

    // ---- What the HUD (and later the missions) can read --------------------
    public int Score { get; private set; }
    public readonly List<Violation> violations = new List<Violation>();
    public event System.Action<Violation> ViolationAdded;
    public string RoadName { get; private set; } = "";
    public float SpeedLimitKmh { get; private set; }
    public string ZoneLabel { get; private set; } = "";
    public TrafficSignal SignalAhead { get; private set; }
    public LightColour SignalAheadColour { get; private set; }

    // ---- Things in the city --------------------------------------------------
    private TrafficSignal[] signals;
    private ZebraCrossing[] crossings;
    private RuleZone[] zones;
    private LaneGrid laneGrid;
    private bool[] laneIsOneWay;
    private readonly List<Vector3> junctions = new List<Vector3>();

    // ---- Timers and memory ---------------------------------------------------
    private float keepLeftTimer, wrongWayTimer, offRoadTimer, speedingTimer;
    private bool speedingFined;
    private float[] signalPreviousDistance;
    private readonly Dictionary<Object, float> cooldowns = new Dictionary<Object, float>();
    private readonly Dictionary<string, float> ruleCooldowns = new Dictionary<string, float>();
    private float sampleTimer;
    private readonly List<Sample> history = new List<Sample>();
    private float lastTurnTime = -100f;
    private AudioSource beep;

    private struct Sample
    {
        public float time, yaw;
        public Vector3 position;
        public bool left, right, nearJunction;
    }

    // =========================================================================
    private void Start()
    {
        if (car == null) car = FindFirstObjectByType<PokharaCar>();
        if (network == null) network = FindFirstObjectByType<RoadNetwork>();
        signals = FindObjectsByType<TrafficSignal>(FindObjectsSortMode.None);
        crossings = FindObjectsByType<ZebraCrossing>(FindObjectsSortMode.None);
        zones = FindObjectsByType<RuleZone>(FindObjectsSortMode.None);
        signalPreviousDistance = new float[signals.Length];
        for (int i = 0; i < signals.Length; i++) signalPreviousDistance[i] = float.MaxValue;
        Score = startScore;

        if (car == null || network == null)
        {
            Debug.LogWarning("Road Rules Judge: no Player Car or Road Network in the scene, so no rules are checked.");
            enabled = false;
            return;
        }

        // The car tells us about crashes (OnCollisionEnter only works on the car itself).
        CarRuleSensor sensor = car.GetComponent<CarRuleSensor>();
        if (sensor == null) sensor = car.gameObject.AddComponent<CarRuleSensor>();
        sensor.judge = this;

        PrepareLanes();
        beep = gameObject.AddComponent<AudioSource>();
        beep.clip = MakeBeep();
        beep.playOnAwake = false;
        beep.volume = 0.5f;
    }

    // Start again from 100 points (missions will use this).
    public void ResetScore()
    {
        Score = startScore;
        violations.Clear();
    }

    // =========================================================================
    private void Update()
    {
        Transform t = car.transform;
        Vector3 position = t.position;
        Rigidbody body = car.GetComponent<Rigidbody>();
        Vector3 velocity = body != null ? body.linearVelocity : Vector3.zero;
        velocity.y = 0f;
        float speedKmh = Mathf.Abs(car.SpeedKmh);
        bool forwards = car.SpeedKmh > 3f;

        // Which way is the car really going? (Its nose, unless it is sliding.)
        Vector3 heading = velocity.magnitude > 1f ? velocity.normalized : Flat(t.forward);

        bool nearJunction = IsNearJunction(position, 14f);

        // ---- 1. Where am I? Find the lanes around the car ---------------------
        LaneHit same, opposite;
        laneGrid.Find(position, heading, out same, out opposite);
        LaneHit nearest = same.distance <= opposite.distance ? same : opposite;

        if (nearest.lane >= 0)
        {
            Lane lane = network.lanes[nearest.lane];
            RoadName = lane.roadName != "" ? lane.roadName : NiceType(lane.roadType);
            SpeedLimitKmh = lane.speedLimitKmh;
        }
        else
        {
            RoadName = "Off road";
        }

        // ---- 2. Zones (school / silent) ---------------------------------------
        ZoneLabel = "";
        bool noHorn = false;
        foreach (RuleZone z in zones)
        {
            if (!z.Contains(position)) continue;
            ZoneLabel = z.Label;
            noHorn |= z.NoHorn;
            if (z.SpeedLimitKmh > 0f && (SpeedLimitKmh <= 0f || z.SpeedLimitKmh < SpeedLimitKmh)) SpeedLimitKmh = z.SpeedLimitKmh;
        }
        if (noHorn && DriveInput.Horn() && Ready("horn", 6f))
            Fine(hornFine, "No horn here! (" + ZoneLabel + ")");

        // ---- 3. Keep left / wrong way / off road ------------------------------
        if (forwards && nearest.lane >= 0)
        {
            float width = RoadWidthOf(network.lanes[nearest.lane].roadType);

            // Keep left: the lane coming towards you is closer than your own lane.
            bool onRight = opposite.lane >= 0 && same.lane >= 0 && !laneIsOneWay[opposite.lane]
                           && opposite.distance < same.distance - 0.5f && opposite.distance < width * 0.5f;
            keepLeftTimer = onRight && !nearJunction ? keepLeftTimer + Time.deltaTime : 0f;
            if (keepLeftTimer > 3f && Ready("keepLeft", 10f))
            {
                Fine(keepLeftFine, "Keep LEFT! You are driving on the right side of the road.");
                keepLeftTimer = 0f;
            }

            // Wrong way on a one-way road: the lane against you is much closer than
            // any lane going your way. This also catches the wrong side of a
            // divided road (like Baidam Road, mapped as two one-way roads).
            bool wrongWay = opposite.lane >= 0 && laneIsOneWay[opposite.lane] && opposite.distance < width * 0.6f
                            && (same.lane < 0 || same.distance > opposite.distance + 2.5f);
            wrongWayTimer = wrongWay && !nearJunction ? wrongWayTimer + Time.deltaTime : 0f;
            if (wrongWayTimer > 1.5f && Ready("wrongWay", 10f))
            {
                Fine(wrongWayFine, "WRONG WAY! This is a one-way road.");
                wrongWayTimer = 0f;
            }
        }

        bool offRoad = nearest.lane < 0 || nearest.distance > RoadWidthOf(network.lanes[nearest.lane].roadType) * 0.75f + 1.5f;
        offRoadTimer = offRoad && speedKmh > 3f ? offRoadTimer + Time.deltaTime : 0f;
        if (offRoadTimer > 2.5f && Ready("offRoad", 15f))
        {
            Fine(offRoadFine, "You left the road.");
            offRoadTimer = 0f;
        }

        // ---- 4. Speed -----------------------------------------------------------
        if (SpeedLimitKmh > 0f && speedKmh > SpeedLimitKmh + 5f)
        {
            speedingTimer += Time.deltaTime;
            if (speedingTimer > 2f && !speedingFined)
            {
                Fine(speedingFine, "Too fast! " + Mathf.RoundToInt(speedKmh) + " km/h in a " + Mathf.RoundToInt(SpeedLimitKmh) + " km/h zone.");
                speedingFined = true;   // one fine per time you speed, not one every second
            }
        }
        else
        {
            speedingTimer = 0f;
            if (speedKmh <= SpeedLimitKmh) speedingFined = false;
        }

        // ---- 5. Traffic signals -------------------------------------------------
        CheckSignals(position, velocity, heading);

        // ---- 6. Zebra crossings ------------------------------------------------
        foreach (ZebraCrossing z in crossings)
        {
            if (z.PedestrianOnRoad && velocity.magnitude > 0.5f && z.IsOnCrossing(position, 0.5f) && Ready(z, 8f))
                Fine(zebraFine, "STOP for people on the zebra crossing!");
        }

        // ---- 7. Indicators when turning ----------------------------------------
        CheckTurn(t, position, nearJunction, speedKmh);
    }

    // -------------------------------------------------------------------------
    private void CheckSignals(Vector3 position, Vector3 velocity, Vector3 heading)
    {
        SignalAhead = null;
        float closestAhead = 70f;
        for (int i = 0; i < signals.Length; i++)
        {
            TrafficSignal s = signals[i];
            Vector3 toSignal = s.transform.position - position; toSignal.y = 0f;
            float d = toSignal.magnitude;

            // For the HUD: the nearest signal in front of us.
            if (d < closestAhead && Vector3.Dot(toSignal.normalized, heading) > 0.5f)
            {
                closestAhead = d;
                SignalAhead = s;
                SignalAheadColour = s.GetLightFor(heading);
            }

            // Did we just drive over the stop line (into the junction circle)?
            float before = signalPreviousDistance[i];
            signalPreviousDistance[i] = d;
            if (before > s.stopRadius && d <= s.stopRadius && velocity.magnitude > 1f)
            {
                if (s.GetLightFor(velocity) == LightColour.Red && Ready(s, 5f))
                    Fine(redLightFine, "You jumped a RED light at " + s.junctionName + "!");
            }
        }
    }

    // -------------------------------------------------------------------------
    //  Turning: we keep a short diary of the car's direction (10 entries a
    //  second). If the direction changed a lot in the last 5 seconds near a
    //  junction, that was a turn, and the diary says if you indicated.
    // -------------------------------------------------------------------------
    private void CheckTurn(Transform t, Vector3 position, bool nearJunction, float speedKmh)
    {
        sampleTimer += Time.deltaTime;
        if (sampleTimer < 0.1f) return;
        sampleTimer = 0f;

        float now = Time.time;
        history.Add(new Sample
        {
            time = now, yaw = t.eulerAngles.y, position = position,
            left = car.LeftIndicatorOn, right = car.RightIndicatorOn, nearJunction = nearJunction
        });
        while (history.Count > 0 && history[0].time < now - 10f) history.RemoveAt(0);
        if (speedKmh < 3f) return;

        // Add up the turning in the last 5 seconds (since the last turn we judged).
        float windowStart = Mathf.Max(now - 5f, lastTurnTime);
        float turned = 0f, moved = 0f;
        bool passedJunction = false;
        for (int i = 1; i < history.Count; i++)
        {
            if (history[i - 1].time < windowStart) continue;
            turned += Mathf.DeltaAngle(history[i - 1].yaw, history[i].yaw);   // + = turning right
            moved += Vector3.Distance(history[i - 1].position, history[i].position);
            passedJunction |= history[i].nearJunction;
        }
        if (Mathf.Abs(turned) < 65f || moved < 8f || !passedJunction) return;

        // A turn! Was the right indicator on at any time in the last 10 seconds?
        bool turnedLeft = turned < 0f;
        bool usedCorrect = false, usedWrong = false;
        foreach (Sample s in history)
        {
            if (s.left) { if (turnedLeft) usedCorrect = true; else usedWrong = true; }
            if (s.right) { if (turnedLeft) usedWrong = true; else usedCorrect = true; }
        }
        string side = turnedLeft ? "left" : "right";
        if (!usedCorrect && usedWrong) Fine(wrongIndicatorFine, "Wrong indicator! You turned " + side + ".");
        else if (!usedCorrect) Fine(noIndicatorFine, "Use your indicator before turning " + side + " (" + (turnedLeft ? "Q" : "E") + ").");

        lastTurnTime = now;
        history.Clear();   // start a fresh diary for the next turn
    }

    // -------------------------------------------------------------------------
    //  Crashes (called by CarRuleSensor on the car)
    // -------------------------------------------------------------------------
    public void ReportCollision(Collision collision)
    {
        if (!enabled) return;
        Pedestrian person = collision.collider.GetComponentInParent<Pedestrian>();
        if (person != null)
        {
            if (Ready(person, 5f))
            {
                Fine(hitPedestrianFine, "You hit a pedestrian!");
                if (person.crossing != null) person.crossing.KnockDown();
            }
            return;
        }

        if (collision.relativeVelocity.magnitude < 2.5f) return;   // a gentle bump is not a crash
        string what = collision.collider.gameObject.name;
        if (what.Contains("Kerb") || what == "Ground" || what.StartsWith("Roads")) return;
        if (Ready("crash", 3f)) Fine(crashFine, "Crash! You hit " + NiceThing(what) + ".");
    }

    // =========================================================================
    //  Helpers
    // =========================================================================
    private void Fine(int points, string message)
    {
        Score = Mathf.Max(0, Score - points);
        var v = new Violation { time = Time.time, message = message, points = points };
        violations.Add(v);
        Debug.Log("Road rules: -" + points + "  " + message);
        if (beep != null) beep.Play();
        if (ViolationAdded != null) ViolationAdded(v);
    }

    // "Has it been long enough since the last fine for this?" (so one mistake
    // doesn't take all your points in one second)
    private bool Ready(Object thing, float seconds)
    {
        float last;
        if (cooldowns.TryGetValue(thing, out last) && Time.time - last < seconds) return false;
        cooldowns[thing] = Time.time;
        return true;
    }

    private bool Ready(string rule, float seconds)
    {
        float last;
        if (ruleCooldowns.TryGetValue(rule, out last) && Time.time - last < seconds) return false;
        ruleCooldowns[rule] = Time.time;
        return true;
    }

    private bool IsNearJunction(Vector3 p, float distance)
    {
        foreach (Vector3 j in junctions)
        {
            float dx = j.x - p.x, dz = j.z - p.z;
            if (dx * dx + dz * dz < distance * distance) return true;
        }
        return false;
    }

    private static Vector3 Flat(Vector3 v) { v.y = 0f; return v.normalized; }

    // Same road widths as the city importer.
    public static float RoadWidthOf(string type)
    {
        switch (type)
        {
            case "trunk": return 10f;
            case "primary": return 9f;
            case "secondary": return 8f;
            case "tertiary": return 7f;
            case "living_street": return 5f;
            case "service": return 4f;
            default: return 6f;
        }
    }

    private static string NiceType(string type)
    {
        switch (type)
        {
            case "primary": case "trunk": return "Main road";
            case "secondary": return "City road";
            case "residential": return "Gali (residential lane)";
            case "service": return "Service lane";
            default: return "Road";
        }
    }

    private static string NiceThing(string objectName)
    {
        if (objectName.StartsWith("Traffic")) return "another vehicle";
        if (objectName.StartsWith("Buildings")) return "a building";
        if (objectName.StartsWith("Trees")) return "a tree";
        if (objectName.Contains("pole") || objectName.Contains("Signal")) return "a pole";
        if (objectName.Contains("Lake")) return "the lake railing";
        return "something";
    }

    // Get the lanes ready: a fast search grid, which lanes are one-way,
    // and where the junctions are.
    private void PrepareLanes()
    {
        List<Lane> lanes = network.lanes;
        laneGrid = new LaneGrid(25f);
        var pairs = new HashSet<long>();
        var starts = new Dictionary<long, List<Vector3>>();
        for (int i = 0; i < lanes.Count; i++)
        {
            laneGrid.AddLane(i, lanes[i].points);
            pairs.Add(PairKey(lanes[i].fromNode, lanes[i].toNode));
            if (lanes[i].points.Count == 0) continue;
            if (!starts.ContainsKey(lanes[i].fromNode)) starts[lanes[i].fromNode] = new List<Vector3>();
            starts[lanes[i].fromNode].Add(lanes[i].points[0]);
        }

        laneIsOneWay = new bool[lanes.Count];
        for (int i = 0; i < lanes.Count; i++)
            laneIsOneWay[i] = !pairs.Contains(PairKey(lanes[i].toNode, lanes[i].fromNode));

        // A junction = 3 or more lanes start there. Its middle = the average
        // of where those lanes start (they sit a little to the left of the node).
        foreach (var pair in starts)
        {
            if (pair.Value.Count < 3) continue;
            Vector3 sum = Vector3.zero;
            foreach (Vector3 p in pair.Value) sum = sum + p;
            junctions.Add(sum / pair.Value.Count);
        }
    }

    private static long PairKey(long a, long b) { unchecked { return a * 1000003L ^ b; } }

    // A short "ding-dong" made with maths (no sound file needed).
    private static AudioClip MakeBeep()
    {
        const int rate = 44100;
        int length = rate / 3;
        var data = new float[length];
        for (int i = 0; i < length; i++)
        {
            float t = i / (float)rate;
            float freq = t < 0.15f ? 880f : 660f;
            float fade = 1f - i / (float)length;
            data[i] = Mathf.Sin(2f * Mathf.PI * freq * t) * 0.4f * fade;
        }
        AudioClip clip = AudioClip.Create("RuleBeep", length, 1, rate, false);
        clip.SetData(data, 0);
        return clip;
    }
}

// =============================================================================
//  LaneHit / LaneGrid — "which lane am I on, and which lane goes the other way?"
//  Lane pieces are sorted into 25 m squares, so we only check the few nearby
//  (like looking only at the streets on your page of the city map).
// =============================================================================
public struct LaneHit
{
    public int lane;          // -1 = none found
    public float distance;
}

public class LaneGrid
{
    private struct Piece { public int lane; public Vector3 a, b; }
    private readonly float cellSize;
    private readonly Dictionary<long, List<Piece>> cells = new Dictionary<long, List<Piece>>();

    public LaneGrid(float cellSize) { this.cellSize = cellSize; }
    private static long Key(int x, int z) { return ((long)x << 32) ^ (uint)z; }

    public void AddLane(int laneIndex, List<Vector3> points)
    {
        for (int i = 0; i < points.Count - 1; i++)
        {
            var piece = new Piece { lane = laneIndex, a = points[i], b = points[i + 1] };
            int x0 = Mathf.FloorToInt(Mathf.Min(piece.a.x, piece.b.x) / cellSize), x1 = Mathf.FloorToInt(Mathf.Max(piece.a.x, piece.b.x) / cellSize);
            int z0 = Mathf.FloorToInt(Mathf.Min(piece.a.z, piece.b.z) / cellSize), z1 = Mathf.FloorToInt(Mathf.Max(piece.a.z, piece.b.z) / cellSize);
            for (int x = x0; x <= x1; x++)
                for (int z = z0; z <= z1; z++)
                {
                    long k = Key(x, z);
                    if (!cells.ContainsKey(k)) cells[k] = new List<Piece>();
                    cells[k].Add(piece);
                }
        }
    }

    // same     = nearest lane going roughly the same way as 'heading'
    // opposite = nearest lane going roughly the other way
    public void Find(Vector3 p, Vector3 heading, out LaneHit same, out LaneHit opposite)
    {
        same = new LaneHit { lane = -1, distance = float.MaxValue };
        opposite = new LaneHit { lane = -1, distance = float.MaxValue };
        int cx = Mathf.FloorToInt(p.x / cellSize), cz = Mathf.FloorToInt(p.z / cellSize);
        for (int x = cx - 1; x <= cx + 1; x++)
            for (int z = cz - 1; z <= cz + 1; z++)
            {
                List<Piece> list;
                if (!cells.TryGetValue(Key(x, z), out list)) continue;
                foreach (Piece piece in list)
                {
                    Vector3 ab = piece.b - piece.a; ab.y = 0f;
                    float len2 = ab.sqrMagnitude;
                    if (len2 < 1e-4f) continue;
                    Vector3 ap = p - piece.a; ap.y = 0f;
                    float t = Mathf.Clamp01(Vector3.Dot(ap, ab) / len2);
                    Vector3 closest = piece.a + ab * t;
                    float dx = p.x - closest.x, dz = p.z - closest.z;
                    float d = Mathf.Sqrt(dx * dx + dz * dz);
                    float dot = Vector3.Dot(ab / Mathf.Sqrt(len2), heading);
                    if (dot > 0.3f && d < same.distance) { same.lane = piece.lane; same.distance = d; }
                    else if (dot < -0.3f && d < opposite.distance) { opposite.lane = piece.lane; opposite.distance = d; }
                }
            }
    }
}
