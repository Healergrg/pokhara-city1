// =============================================================================
//  RouteImporter.cs  —  turn a route.geojson into a MissionRoute
// =============================================================================
//  On openstreetmap.org, the Directions tool lets you "Download route as
//  GeoJSON". That file is just a list of GPS points along the route.
//
//  Tools > Pokhara City > Import Route (GeoJSON)
//      -> creates a "Mission Route" object with a red line in the Scene view
//  Tools > Pokhara City > Put Selected Car At Route Start
//      -> moves your selected car to the start, facing the right way
//
//  Build the city first: the route uses the city's RoadNetwork to line up.
// =============================================================================

using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

public static class RouteImporter
{
    [MenuItem("Tools/Pokhara City/Import Route (GeoJSON)")]
    public static void ImportRoute()
    {
        RoadNetwork network = Object.FindFirstObjectByType<RoadNetwork>();
        if (network == null)
        {
            EditorUtility.DisplayDialog("Build the city first", "No Road Network found in this scene. Use Tools > Pokhara City > Import OSM Map first, then import the route.", "OK");
            return;
        }

        string path = EditorUtility.OpenFilePanel("Choose a route .geojson", "", "geojson,json");
        if (string.IsNullOrEmpty(path)) return;

        List<Vector3> points = ReadRoute(File.ReadAllText(path), network);
        if (points.Count < 2)
        {
            EditorUtility.DisplayDialog("No route found", "That file does not contain a LineString route.", "OK");
            return;
        }

        var go = new GameObject("Mission Route - " + Path.GetFileNameWithoutExtension(path));
        Undo.RegisterCreatedObjectUndo(go, "Import Route");
        var route = go.AddComponent<MissionRoute>();
        route.points = points;
        Selection.activeGameObject = go;

        EditorUtility.DisplayDialog("Route imported",
            points.Count + " points, " + (route.LengthMetres() / 1000f).ToString("0.0") + " km long.\n\n" +
            "Parts of the route outside your map file will float over empty ground — export a bigger map area if you need them.", "OK");
    }

    // GeoJSON stores each point as [longitude, latitude] (longitude FIRST — easy to mix up!).
    // We find every "[number, number]" pair after "coordinates" with a pattern search.
    private static List<Vector3> ReadRoute(string json, RoadNetwork network)
    {
        var points = new List<Vector3>();
        int start = json.IndexOf("\"coordinates\"");
        if (start < 0) return points;

        var pair = new Regex(@"\[\s*(-?\d+(?:\.\d+)?)\s*,\s*(-?\d+(?:\.\d+)?)");
        foreach (Match m in pair.Matches(json.Substring(start)))
        {
            double lon = double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
            double lat = double.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
            points.Add(network.LatLonToWorld(lat, lon));
        }
        return points;
    }

    [MenuItem("Tools/Pokhara City/Put Selected Car At Route Start")]
    public static void PutCarAtStart()
    {
        MissionRoute route = Object.FindFirstObjectByType<MissionRoute>();
        GameObject car = Selection.activeGameObject;
        if (route == null || route.points.Count < 2 || car == null || car.GetComponent<MissionRoute>() != null)
        {
            EditorUtility.DisplayDialog("Select your car", "Import a route first, then click your car in the Hierarchy and run this again.", "OK");
            return;
        }

        Undo.RecordObject(car.transform, "Put Car At Route Start");
        Vector3 start = route.points[0];
        Vector3 facing = route.points[1] - start;
        facing.y = 0f;

        // Nepal drives on the left: nudge the car 2 m to the left of the route line.
        Vector3 left = new Vector3(-facing.normalized.z, 0f, facing.normalized.x);
        car.transform.position = start + left * 2f + Vector3.up * 0.5f;
        car.transform.rotation = Quaternion.LookRotation(facing.normalized, Vector3.up);
    }
}
