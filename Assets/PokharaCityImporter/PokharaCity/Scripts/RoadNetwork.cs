// =============================================================================
//  RoadNetwork.cs  —  the "road map" that traffic cars will read
// =============================================================================
//  Think of this like the route map a bus driver keeps in their head:
//  for every road it knows which way you are allowed to drive and where
//  the road leads next. The importer fills this in automatically from
//  OpenStreetMap, and later (Week 4) your TrafficCar script will ask it
//  "I reached the end of this lane, which lanes can I take next?".
//
//  IMPORTANT: Nepal drives on the LEFT, so every lane stored here already
//  sits on the left-hand side of its road.
//
//  You do not need to edit this file to use the importer.
// =============================================================================

using System.Collections.Generic;
using UnityEngine;

// One lane = one direction of travel along one piece of road,
// from one junction to the next junction.
[System.Serializable]
public class Lane
{
    public string roadName;        // e.g. "Lakeside Marg" (empty if the road has no name)
    public string roadType;        // OpenStreetMap type: primary, secondary, residential...
    public float speedLimitKmh;    // a GAME setting chosen by road type (not an official limit)
    public long fromNode;          // OpenStreetMap id of the junction where the lane starts
    public long toNode;            // OpenStreetMap id of the junction where the lane ends
    public List<Vector3> points = new List<Vector3>();   // the path, in Unity metres
}

public class RoadNetwork : MonoBehaviour
{
    [Header("Where (0,0,0) is on the real map — filled in by the importer")]
    public double originLatitude;
    public double originLongitude;

    [Header("All drivable lanes (left-hand traffic)")]
    public List<Lane> lanes = new List<Lane>();

    [Header("Gizmos (only visible in the Scene view)")]
    public bool showLanesWhenSelected = true;

    // A quick lookup: junction id -> lanes that START at that junction.
    // Built the first time someone asks for it (like making an index for a book).
    private Dictionary<long, List<int>> lanesStartingAt;

    // -------------------------------------------------------------------------
    // Convert a real-world GPS position into a Unity position.
    // Same maths the importer used, so routes and lanes line up perfectly.
    // -------------------------------------------------------------------------
    public Vector3 LatLonToWorld(double latitude, double longitude, float height = 0f)
    {
        const double metresPerDegreeLat = 110540.0;
        const double metresPerDegreeLon = 111320.0;
        double cosLat = System.Math.Cos(originLatitude * System.Math.PI / 180.0);

        float x = (float)((longitude - originLongitude) * cosLat * metresPerDegreeLon); // east
        float z = (float)((latitude - originLatitude) * metresPerDegreeLat);            // north
        return new Vector3(x, height, z);
    }

    // -------------------------------------------------------------------------
    // "I just finished lane number laneIndex — where can I go now?"
    // Returns the indexes of all lanes that start where this lane ends,
    // except the lane that simply drives back the way we came (no U-turns).
    // -------------------------------------------------------------------------
    public List<int> GetNextLanes(int laneIndex)
    {
        if (lanesStartingAt == null) BuildLookup();

        var result = new List<int>();
        Lane current = lanes[laneIndex];

        List<int> candidates;
        if (!lanesStartingAt.TryGetValue(current.toNode, out candidates)) return result; // dead end

        foreach (int i in candidates)
        {
            bool isUTurn = lanes[i].toNode == current.fromNode;
            if (!isUTurn) result.Add(i);
        }

        // At a dead end the only way out is turning around, so allow it there.
        if (result.Count == 0) result.AddRange(candidates);
        return result;
    }

    // Find the lane whose path passes closest to a position (handy for spawning cars).
    public int FindNearestLane(Vector3 position)
    {
        int best = -1;
        float bestDistance = float.MaxValue;
        for (int i = 0; i < lanes.Count; i++)
        {
            foreach (Vector3 p in lanes[i].points)
            {
                float d = (p - position).sqrMagnitude;
                if (d < bestDistance) { bestDistance = d; best = i; }
            }
        }
        return best;
    }

    private void BuildLookup()
    {
        lanesStartingAt = new Dictionary<long, List<int>>();
        for (int i = 0; i < lanes.Count; i++)
        {
            long start = lanes[i].fromNode;
            if (!lanesStartingAt.ContainsKey(start)) lanesStartingAt[start] = new List<int>();
            lanesStartingAt[start].Add(i);
        }
    }

    // Draw every lane as a cyan line with a small arrow showing the driving direction.
    private void OnDrawGizmosSelected()
    {
        if (!showLanesWhenSelected || lanes == null) return;

        Gizmos.color = Color.cyan;
        foreach (Lane lane in lanes)
        {
            for (int i = 0; i < lane.points.Count - 1; i++)
            {
                Vector3 a = lane.points[i] + Vector3.up * 0.3f;
                Vector3 b = lane.points[i + 1] + Vector3.up * 0.3f;
                Gizmos.DrawLine(a, b);
            }

            // Arrow head at the end of the lane.
            int n = lane.points.Count;
            if (n >= 2)
            {
                Vector3 end = lane.points[n - 1] + Vector3.up * 0.3f;
                Vector3 dir = (lane.points[n - 1] - lane.points[n - 2]).normalized;
                Vector3 side = new Vector3(dir.z, 0f, -dir.x);
                Gizmos.DrawLine(end, end - dir * 2f + side * 1f);
                Gizmos.DrawLine(end, end - dir * 2f - side * 1f);
            }
        }
    }
}
