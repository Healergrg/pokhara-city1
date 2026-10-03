// =============================================================================
//  MissionRoute.cs  —  a route the player must drive (e.g. Damside -> Lakeside)
// =============================================================================
//  Made by "Tools > Pokhara City > Import Route (GeoJSON)".
//  It is just a list of points along the road, like the dotted line
//  Google Maps draws on your phone. In Week 6 the MissionManager will use
//  it for the route arrow, checkpoints and the finish line.
// =============================================================================

using System.Collections.Generic;
using UnityEngine;

public class MissionRoute : MonoBehaviour
{
    public string missionName = "Damside to Lakeside";
    public List<Vector3> points = new List<Vector3>();

    public Vector3 StartPoint  => points.Count > 0 ? points[0] : transform.position;
    public Vector3 FinishPoint => points.Count > 0 ? points[points.Count - 1] : transform.position;

    // Total length in metres (useful for showing "3.9 km" on the mission screen).
    public float LengthMetres()
    {
        float total = 0f;
        for (int i = 0; i < points.Count - 1; i++) total += Vector3.Distance(points[i], points[i + 1]);
        return total;
    }

    // Red line in the Scene view, green ball = start, black ball = finish.
    private void OnDrawGizmos()
    {
        if (points == null || points.Count < 2) return;

        Gizmos.color = Color.red;
        for (int i = 0; i < points.Count - 1; i++)
            Gizmos.DrawLine(points[i] + Vector3.up, points[i + 1] + Vector3.up);

        Gizmos.color = Color.green;
        Gizmos.DrawSphere(StartPoint + Vector3.up, 3f);
        Gizmos.color = Color.black;
        Gizmos.DrawSphere(FinishPoint + Vector3.up, 3f);
    }
}
