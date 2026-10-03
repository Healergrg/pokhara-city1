// =============================================================================
//  RoutePlanner.cs  —  finds the way from A to B along the real roads
// =============================================================================
//  Works like Google Maps: it tries roads in order of "shortest so far"
//  (this is called Dijkstra's algorithm) until it reaches the destination.
//  Because it uses the traffic lanes, it automatically:
//    - stays on the LEFT side of every road
//    - never goes the wrong way down a one-way road
//    - never makes U-turns (except at a dead end)
//
//  It also writes the turn-by-turn directions: "In 120 m turn LEFT onto
//  Baidam Road".
// =============================================================================

using System.Collections.Generic;
using UnityEngine;

[System.Serializable]
public class MissionDefinition
{
    public string title = "New mission";
    [TextArea] public string description = "";
    public Vector3 start;
    public List<Vector3> via = new List<Vector3>();   // places to drive through, in order
    public Vector3 finish;
    [Tooltip("0 = worked out from the route length and the average speed below")]
    public float timeLimitSeconds = 0f;
    public float averageSpeedKmh = 22f;
    public int passScore = 70;
}

public struct RouteInstruction
{
    public float at;        // metres from the start of the route
    public int turn;        // -1 left, +1 right
    public string road;     // the road you turn onto
}

public class PlannedRoute
{
    public readonly List<Vector3> points = new List<Vector3>();
    public readonly List<float> along = new List<float>();
    public readonly List<RouteInstruction> instructions = new List<RouteInstruction>();
    public float Length => along.Count > 0 ? along[along.Count - 1] : 0f;

    public void Add(Vector3 p)
    {
        if (points.Count > 0 && (p - points[points.Count - 1]).sqrMagnitude < 0.04f) return;
        along.Add(points.Count == 0 ? 0f : along[along.Count - 1] + Vector3.Distance(points[points.Count - 1], p));
        points.Add(p);
    }

    public Vector3 PointAt(float d)
    {
        if (points.Count == 0) return Vector3.zero;
        for (int i = 1; i < points.Count; i++)
            if (along[i] >= d)
            {
                float seg = along[i] - along[i - 1];
                return Vector3.Lerp(points[i - 1], points[i], seg > 0.0001f ? (d - along[i - 1]) / seg : 0f);
            }
        return points[points.Count - 1];
    }

    public Vector3 DirectionAt(float d)
    {
        Vector3 a = PointAt(d), b = PointAt(Mathf.Min(d + 3f, Length));
        if ((b - a).sqrMagnitude < 0.001f) { a = PointAt(Mathf.Max(0f, d - 3f)); b = PointAt(d); }
        Vector3 dir = b - a; dir.y = 0f;
        return dir.sqrMagnitude > 0.0001f ? dir.normalized : new Vector3(0f, 0f, 1f);
    }
}

public static class RoutePlanner
{
    // stops = start, places to pass through..., finish.  Returns null if no way exists.
    public static PlannedRoute Plan(RoadNetwork network, List<Vector3> stops)
    {
        List<Lane> lanes = network.lanes;
        if (lanes.Count == 0 || stops.Count < 2) return null;
        float[] length = new float[lanes.Count];
        for (int i = 0; i < lanes.Count; i++) length[i] = LaneLength(lanes[i].points);

        // Start: the nearest lane (prefer lanes long enough to drive along).
        int lane = -1, index = 0;
        NearestLane(lanes, stops[0], 30f, length, out lane, out index);
        if (lane < 0) return null;
        index = Mathf.Min(index, lanes[lane].points.Count - 2);

        var route = new PlannedRoute();
        var laneSequence = new List<int> { lane };
        var cutStart = new List<int> { index };
        var cutEnd = new List<int>();

        for (int s = 1; s < stops.Count; s++)
        {
            // Destination: any lane passing within 15 m of the stop.
            var targets = new HashSet<int>();
            for (int i = 0; i < lanes.Count; i++)
                foreach (Vector3 p in lanes[i].points)
                    if (Flat(p - stops[s]).sqrMagnitude < 15f * 15f) { targets.Add(i); break; }
            if (targets.Count == 0)
            {
                int nl, ni; NearestLane(lanes, stops[s], 0f, length, out nl, out ni);
                if (nl < 0) return null;
                targets.Add(nl);
            }

            // Already on a target lane, and the stop is still ahead of us?
            int current = laneSequence[laneSequence.Count - 1];
            int from = cutStart[cutStart.Count - 1];
            int hereIndex = NearestIndex(lanes[current].points, stops[s]);
            if (targets.Contains(current) && hereIndex > from)
            {
                cutEnd.Add(hereIndex);
                laneSequence.Add(current); cutStart.Add(hereIndex);
                continue;
            }

            List<int> path = ShortestPath(network, current, targets, length);
            if (path == null) return null;
            // path[0] == current lane. Finish it, drive the middle ones, stop in the last.
            cutEnd.Add(lanes[current].points.Count - 1);
            for (int k = 1; k < path.Count - 1; k++)
            {
                laneSequence.Add(path[k]); cutStart.Add(0); cutEnd.Add(lanes[path[k]].points.Count - 1);
            }
            int last = path[path.Count - 1];
            int stopIndex = Mathf.Max(1, NearestIndex(lanes[last].points, stops[s]));
            laneSequence.Add(last); cutStart.Add(0); cutEnd.Add(stopIndex);
            laneSequence.Add(last); cutStart.Add(stopIndex);   // the next leg continues from here
        }

        // Turn the lane pieces into one list of points (+ the directions).
        for (int k = 0; k < cutEnd.Count; k++)
        {
            List<Vector3> pts = lanes[laneSequence[k]].points;
            int a = cutStart[k], b = cutEnd[k];
            bool newLane = k > 0 && laneSequence[k] != laneSequence[k - 1];
            if (newLane)
            {
                int turn = Turn(lanes[laneSequence[k - 1]].points, pts);
                int count = route.instructions.Count;
                bool sameAsLast = count > 0 && route.Length - route.instructions[count - 1].at < 20f;
                if (turn != 0 && sameAsLast)
                {
                    // Two map junctions very close together = one turn for the driver.
                    RouteInstruction merged = route.instructions[count - 1];
                    merged.road = NiceRoadName(lanes[laneSequence[k]].roadName);
                    route.instructions[count - 1] = merged;
                }
                else if (turn != 0)
                    route.instructions.Add(new RouteInstruction { at = route.Length, turn = turn, road = NiceRoadName(lanes[laneSequence[k]].roadName) });
            }
            for (int i = a; i <= b && i < pts.Count; i++) route.Add(pts[i]);
        }
        return route.points.Count >= 2 ? route : null;
    }

    // Dijkstra: always grow the cheapest (shortest) road first.
    private static List<int> ShortestPath(RoadNetwork network, int start, HashSet<int> targets, float[] length)
    {
        int n = network.lanes.Count;
        var cost = new float[n];
        var previous = new int[n];
        var done = new bool[n];
        for (int i = 0; i < n; i++) { cost[i] = float.MaxValue; previous[i] = -1; }
        cost[start] = 0f;
        var open = new List<int> { start };

        while (open.Count > 0)
        {
            // Take the cheapest lane we know about.
            int best = 0;
            for (int i = 1; i < open.Count; i++) if (cost[open[i]] < cost[open[best]]) best = i;
            int lane = open[best];
            open.RemoveAt(best);
            if (done[lane]) continue;
            done[lane] = true;

            if (lane != start && targets.Contains(lane))
            {
                var path = new List<int>();
                for (int at = lane; at != -1; at = previous[at]) path.Add(at);
                path.Reverse();
                return path;
            }

            foreach (int next in network.GetNextLanes(lane))
            {
                // Small lanes "cost" more, so routes prefer the main roads
                // (like a driving examiner would choose).
                float c = cost[lane] + length[next] * RoadCost(network.lanes[next].roadType);
                if (c < cost[next]) { cost[next] = c; previous[next] = lane; open.Add(next); }
            }
        }
        return null;
    }

    private static float RoadCost(string type)
    {
        switch (type)
        {
            case "trunk": case "primary": case "secondary": return 1f;
            case "tertiary": return 1.1f;
            case "living_street": return 2f;
            case "service": return 3f;
            default: return 1.6f;   // residential, unclassified (galli)
        }
    }

    // Nicer road names for the directions ("" -> "the next road", "16" -> "Street 16").
    public static string NiceRoadName(string name)
    {
        if (string.IsNullOrEmpty(name)) return "the next road";
        bool number = true;
        foreach (char c in name) if (!char.IsDigit(c)) number = false;
        return number ? "Street " + name : name;
    }

    private static void NearestLane(List<Lane> lanes, Vector3 p, float minLength, float[] length, out int lane, out int index)
    {
        lane = -1; index = 0;
        float best = float.MaxValue;
        for (int pass = 0; pass < 2 && lane < 0; pass++)
            for (int i = 0; i < lanes.Count; i++)
            {
                if (pass == 0 && length[i] < minLength) continue;
                List<Vector3> pts = lanes[i].points;
                for (int k = 0; k < pts.Count; k++)
                {
                    float d = Flat(pts[k] - p).sqrMagnitude;
                    if (d < best) { best = d; lane = i; index = k; }
                }
            }
    }

    private static int NearestIndex(List<Vector3> pts, Vector3 p)
    {
        int best = 0; float bestD = float.MaxValue;
        for (int k = 0; k < pts.Count; k++)
        {
            float d = Flat(pts[k] - p).sqrMagnitude;
            if (d < bestD) { bestD = d; best = k; }
        }
        return best;
    }

    private static int Turn(List<Vector3> fromLane, List<Vector3> toLane)
    {
        if (fromLane.Count < 2 || toLane.Count < 2) return 0;
        Vector3 a = Flat(fromLane[fromLane.Count - 1] - fromLane[Mathf.Max(0, fromLane.Count - 3)]);
        Vector3 b = Flat(toLane[Mathf.Min(toLane.Count - 1, 2)] - toLane[0]);
        float angle = Vector3.SignedAngle(a, b, Vector3.up);   // + = right
        if (angle < -35f) return -1;
        if (angle > 35f) return 1;
        return 0;
    }

    private static float LaneLength(List<Vector3> pts)
    {
        float total = 0f;
        for (int i = 1; i < pts.Count; i++) total += Vector3.Distance(pts[i - 1], pts[i]);
        return total;
    }

    private static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }
}
