// =============================================================================
//  CityRulesPlanner.cs  —  Week 3: decides WHERE the road rules go
// =============================================================================
//  Reads map.osm and plans:
//    - TRAFFIC SIGNALS at the busiest junctions (where main roads meet).
//      Your map has no traffic lights marked, so we pick the biggest chowks.
//    - ZEBRA CROSSINGS where footpaths meet main roads, where the map marks
//      a crossing, and on the main road next to every school.
//    - SCHOOL ZONES (20 km/h, no horn) around schools and kindergartens.
//    - SILENT ZONES (no horn) around hospitals and clinics.
//
//  Only the plan is made here (positions and directions). RulesBuilder.cs
//  turns the plan into real objects in your scene.
//  Like an architect's drawing: this file draws, the builder builds.
// =============================================================================

using System.Collections.Generic;
using UnityEngine;

public class SignalPlan
{
    public string name;
    public Vector3 centre;
    public float stopRadius;                                  // stop line distance from the centre
    public List<Vector3> approachDirections = new List<Vector3>();   // pointing INTO the junction
    public List<float> approachHalfWidths = new List<float>();
}

public class CrossingPlan
{
    public Vector3 centre;
    public Vector3 along;       // the direction the ROAD goes (people walk across it)
    public float halfWidth;     // half the road width
    public string reason;
}

public class ZonePlan
{
    public string name;
    public bool school;         // true = school zone (20 km/h + no horn), false = silent zone (no horn)
    public Vector3 centre;
    public float radius;
    public Vector3 signPosition;
    public Vector3 signFacing;  // the sign's face points this way (along the road)
}

public class RulesPlan
{
    public List<SignalPlan> signals = new List<SignalPlan>();
    public List<CrossingPlan> crossings = new List<CrossingPlan>();
    public List<ZonePlan> zones = new List<ZonePlan>();
}

public static partial class CityMeshBuilder
{
    public const int MaxSignals = 6;
    public const int MaxCrossings = 30;

    private static bool IsMainRoad(string type) { return type == "trunk" || type == "primary" || type == "secondary"; }
    private static bool IsFootway(string type)
    {
        return type == "footway" || type == "path" || type == "steps" || type == "pedestrian" || type == "cycleway";
    }

    public static RulesPlan PlanRules(OsmData data)
    {
        var plan = new RulesPlan();
        Vector3 lo = data.ToUnity(data.minLat, data.minLon), hi = data.ToUnity(data.maxLat, data.maxLon);
        System.Func<Vector3, float, bool> inside = (p, m) => p.x > lo.x + m && p.x < hi.x - m && p.z > lo.z + m && p.z < hi.z - m;

        // ---------------------------------------------------------------------
        // Step 1: for every node, collect the roads arriving at it.
        // ---------------------------------------------------------------------
        var arrivals = new Dictionary<long, List<Arrival>>();
        var roadUse = new Dictionary<long, int>();          // how many drivable ways touch a node
        var footwayNodes = new HashSet<long>();
        var mainWays = new List<OsmWay>();

        foreach (OsmWay way in data.ways)
        {
            string type = way.Tag("highway");
            if (type == null || !RoadStyles.ContainsKey(type)) continue;
            if (IsFootway(type)) { foreach (long id in way.nodeIds) footwayNodes.Add(id); continue; }
            if (!RoadStyles[type].drivable) continue;
            if (IsMainRoad(type) || type == "tertiary") mainWays.Add(way);

            float half = RoadWidth(way, RoadStyles[type]) / 2f;
            string name = way.Tag("name") ?? "";
            var ids = way.nodeIds;
            for (int i = 0; i < ids.Count; i++)
            {
                long id = ids[i];
                if (!data.nodes.ContainsKey(id)) continue;
                roadUse[id] = (roadUse.ContainsKey(id) ? roadUse[id] : 0) + 1;
                Vector3 here = data.NodeToUnity(id);

                // Arriving from the previous node, and (going backwards) from the next one.
                for (int step = -1; step <= 1; step += 2)
                {
                    Vector3 from;
                    if (!PointBack(data, ids, i, step, here, out from)) continue;
                    if (!arrivals.ContainsKey(id)) arrivals[id] = new List<Arrival>();
                    arrivals[id].Add(new Arrival { direction = (here - from).normalized, halfWidth = half, main = IsMainRoad(type), name = name });
                }
            }
        }

        // ---------------------------------------------------------------------
        // Step 2: TRAFFIC SIGNALS at the biggest main-road junctions.
        // ---------------------------------------------------------------------
        var candidates = new List<SignalPlan>();
        var candidateScore = new List<float>();
        foreach (var pair in arrivals)
        {
            List<Arrival> list = MergeSameDirection(pair.Value);
            if (list.Count < 3) continue;
            int mainCount = 0; float score = 0f, widest = 0f;
            var names = new List<string>();
            foreach (Arrival a in list)
            {
                if (a.main) mainCount++;
                score += a.halfWidth * (a.main ? 2f : 1f);
                widest = Mathf.Max(widest, a.halfWidth);
                if (a.main && a.name != "" && !names.Contains(a.name) && names.Count < 2) names.Add(a.name);   // name it after the main roads
            }
            if (mainCount < 2) continue;
            Vector3 centre = data.NodeToUnity(pair.Key);
            if (!inside(centre, 60f)) continue;

            var signal = new SignalPlan
            {
                name = names.Count > 0 ? string.Join(" / ", names.ToArray()) : "Chowk",
                centre = centre,
                stopRadius = widest + 4f
            };
            foreach (Arrival a in list) { signal.approachDirections.Add(a.direction); signal.approachHalfWidths.Add(a.halfWidth); }
            candidates.Add(signal);
            candidateScore.Add(score + list.Count * 3f);
        }
        // Best first, at least 250 m apart (real signals are not on every corner).
        var order = new List<int>();
        for (int i = 0; i < candidates.Count; i++) order.Add(i);
        order.Sort((a, b) => candidateScore[b].CompareTo(candidateScore[a]));
        foreach (int i in order)
        {
            if (plan.signals.Count >= MaxSignals) break;
            bool tooClose = false;
            foreach (SignalPlan s in plan.signals)
                if (Vector3.Distance(s.centre, candidates[i].centre) < 250f) tooClose = true;
            if (!tooClose) plan.signals.Add(candidates[i]);
        }

        // ---------------------------------------------------------------------
        // Step 3: ZONES around schools and hospitals.
        // ---------------------------------------------------------------------
        var places = new List<KeyValuePair<string, Vector3>>();   // amenity, position
        var placeNames = new List<string>();
        foreach (OsmNode node in data.nodes.Values)
        {
            string amenity = node.Tag("amenity");
            if (IsZoneAmenity(amenity)) { places.Add(new KeyValuePair<string, Vector3>(amenity, data.ToUnity(node.lat, node.lon))); placeNames.Add(node.Tag("name") ?? ""); }
        }
        foreach (OsmWay way in data.ways)
        {
            string amenity = way.Tag("amenity");
            if (!IsZoneAmenity(amenity) || !way.IsClosed) continue;
            List<Vector3> pts = data.WayPoints(way);
            if (pts.Count == 0) continue;
            Vector3 sum = Vector3.zero;
            foreach (Vector3 p in pts) sum = sum + p;
            places.Add(new KeyValuePair<string, Vector3>(amenity, sum / pts.Count));
            placeNames.Add(way.Tag("name") ?? "");
        }

        for (int i = 0; i < places.Count; i++)
        {
            bool school = places[i].Key == "school" || places[i].Key == "kindergarten" || places[i].Key == "college";
            Vector3 p = places[i].Value;
            if (!inside(p, 20f)) continue;

            bool merged = false;   // two schools next to each other = one zone
            foreach (ZonePlan z in plan.zones)
                if (z.school == school && Vector3.Distance(z.centre, p) < 60f) merged = true;
            if (merged) continue;

            var zone = new ZonePlan
            {
                name = placeNames[i] != "" ? placeNames[i] : (school ? "School" : "Hospital"),
                school = school,
                centre = p,
                radius = school ? 70f : 80f
            };
            // The sign stands at the edge of the nearest road.
            Vector3 roadPoint, roadDir; float roadHalf;
            if (NearestRoadPoint(data, p, 120f, false, out roadPoint, out roadDir, out roadHalf))
            {
                Vector3 toPlace = p - roadPoint; toPlace.y = 0f;
                Vector3 side = RightOf(roadDir);
                if (Vector3.Dot(side, toPlace) < 0f) side = -side;   // the side the school is on
                zone.signPosition = roadPoint + side * (roadHalf + 1.2f);
                zone.signFacing = roadDir;
            }
            else
            {
                zone.signPosition = p;
                zone.signFacing = new Vector3(0f, 0f, 1f);
            }
            plan.zones.Add(zone);
        }

        // ---------------------------------------------------------------------
        // Step 4: ZEBRA CROSSINGS.
        // ---------------------------------------------------------------------
        System.Action<Vector3, Vector3, float, string> addCrossing = (centre, along, half, reason) =>
        {
            if (plan.crossings.Count >= MaxCrossings || !inside(centre, 30f)) return;
            foreach (SignalPlan s in plan.signals) if (Vector3.Distance(s.centre, centre) < s.stopRadius + 15f) return;
            foreach (CrossingPlan c in plan.crossings) if (Vector3.Distance(c.centre, centre) < 90f) return;
            plan.crossings.Add(new CrossingPlan { centre = centre, along = along, halfWidth = half, reason = reason });
        };

        // (a) crossings marked on the map, (b) footpaths joining a main road,
        // walking along each main road so we know its direction there.
        foreach (OsmWay way in mainWays)
        {
            string type = way.Tag("highway");
            float half = RoadWidth(way, RoadStyles[type]) / 2f;
            var ids = way.nodeIds;
            for (int i = 1; i < ids.Count - 1; i++)
            {
                long id = ids[i];
                if (!data.nodes.ContainsKey(id)) continue;
                OsmNode node = data.nodes[id];
                bool marked = node.Tag("highway") == "crossing" || node.Tag("crossing") != null;
                bool footpath = footwayNodes.Contains(id) && (roadUse.ContainsKey(id) && roadUse[id] == 1);
                if (!marked && !footpath) continue;
                Vector3 here = data.NodeToUnity(id), back;
                if (!PointBack(data, ids, i, -1, here, out back)) continue;
                addCrossing(here, (here - back).normalized, half, marked ? "marked on the map" : "footpath meets road");
            }
        }

        // (c) the main road beside each school, away from junctions.
        foreach (ZonePlan z in plan.zones)
        {
            if (!z.school) continue;
            Vector3 roadPoint, roadDir; float roadHalf;
            if (NearestRoadPoint(data, z.centre, 120f, true, out roadPoint, out roadDir, out roadHalf) && !NearJunction(data, roadUse, roadPoint, roadHalf + 12f))
                addCrossing(roadPoint, roadDir, roadHalf, "near " + z.name);
        }

        return plan;
    }

    private class Arrival { public Vector3 direction; public float halfWidth; public bool main; public string name; }

    private static bool IsZoneAmenity(string a)
    {
        return a == "school" || a == "kindergarten" || a == "college" || a == "hospital" || a == "clinic";
    }

    // Walk back along a way from point i (step -1 = towards the start) until
    // we are at least 4 m away, so very short pieces don't give a wobbly direction.
    private static bool PointBack(OsmData data, List<long> ids, int i, int step, Vector3 here, out Vector3 point)
    {
        point = here;
        for (int j = i + step; j >= 0 && j < ids.Count; j += step)
        {
            if (!data.nodes.ContainsKey(ids[j])) return false;
            point = data.NodeToUnity(ids[j]);
            if (Vector3.Distance(point, here) >= 4f) return true;
        }
        return Vector3.Distance(point, here) > 0.5f;
    }

    // Approaches that point the same way (two map lines on top of each other) count once.
    private static List<Arrival> MergeSameDirection(List<Arrival> all)
    {
        var result = new List<Arrival>();
        foreach (Arrival a in all)
        {
            bool duplicate = false;
            foreach (Arrival r in result)
                if (Vector3.Dot(a.direction, r.direction) > 0.95f) { duplicate = true; if (a.halfWidth > r.halfWidth) { r.halfWidth = a.halfWidth; r.main |= a.main; } }
            if (!duplicate) result.Add(a);
        }
        return result;
    }

    private static bool NearestRoadPoint(OsmData data, Vector3 p, float maxDistance, bool mainOnly,
                                         out Vector3 point, out Vector3 direction, out float halfWidth)
    {
        point = p; direction = new Vector3(0f, 0f, 1f); halfWidth = 3f;
        float best = maxDistance;
        bool found = false;
        foreach (OsmWay way in data.ways)
        {
            string type = way.Tag("highway");
            if (type == null || !RoadStyles.ContainsKey(type) || !RoadStyles[type].drivable) continue;
            if (mainOnly && !(IsMainRoad(type) || type == "tertiary")) continue;
            if (type == "service" || type == "track") continue;
            List<Vector3> pts = data.WayPoints(way);
            for (int i = 0; i < pts.Count - 1; i++)
            {
                Vector3 ab = pts[i + 1] - pts[i];
                float len2 = ab.sqrMagnitude;
                if (len2 < 0.01f) continue;
                float t = Mathf.Clamp01(Vector3.Dot(p - pts[i], ab) / len2);
                Vector3 q = pts[i] + ab * t;
                float d = Vector3.Distance(p, q);
                if (d < best)
                {
                    best = d; point = q; direction = ab.normalized;
                    halfWidth = RoadWidth(way, RoadStyles[type]) / 2f; found = true;
                }
            }
        }
        return found;
    }

    private static bool NearJunction(OsmData data, Dictionary<long, int> roadUse, Vector3 p, float distance)
    {
        foreach (var pair in roadUse)
            if (pair.Value >= 2 && Vector3.Distance(data.NodeToUnity(pair.Key), p) < distance) return true;
        return false;
    }

    // =========================================================================
    //  White paint for the rules: zebra stripes + stop lines at signals.
    // =========================================================================
    public static Mesh BuildRuleMarkings(RulesPlan plan)
    {
        var md = new MeshData(1);
        const float y = 0.075f;   // just above the road surface and centre lines

        // Zebra: white bars 0.5 m wide, 3 m long (along the road), 1 m apart.
        foreach (CrossingPlan c in plan.crossings)
        {
            Vector3 along = new Vector3(c.along.x, 0f, c.along.z).normalized;
            Vector3 across = RightOf(along);
            int bars = Mathf.FloorToInt(c.halfWidth * 2f / 1.0f);
            float start = -(bars - 1) * 0.5f;
            for (int b = 0; b < bars; b++)
            {
                Vector3 mid = c.centre + across * (start + b);
                AddFlatRectangle(md, mid + Vector3.up * y, along, across, 1.5f, 0.25f);
            }
        }

        // Stop line on the LEFT half of each road arriving at a signal.
        foreach (SignalPlan s in plan.signals)
            for (int i = 0; i < s.approachDirections.Count; i++)
            {
                Vector3 dir = s.approachDirections[i];
                Vector3 left = -RightOf(dir);
                float half = s.approachHalfWidths[i];
                Vector3 mid = s.centre - dir * s.stopRadius + left * (half / 2f);
                AddFlatRectangle(md, mid + Vector3.up * y, dir, left, 0.2f, half / 2f);
            }

        return md.ToMesh("RuleMarkings");
    }

    // A flat rectangle facing up: half-length along 'a', half-length along 'b'.
    private static void AddFlatRectangle(MeshData md, Vector3 centre, Vector3 a, Vector3 b, float halfA, float halfB)
    {
        a = a.normalized; b = b.normalized;
        int i0 = md.AddVertex(centre - a * halfA - b * halfB, new Vector2(0f, 0f));
        int i1 = md.AddVertex(centre + a * halfA - b * halfB, new Vector2(1f, 0f));
        int i2 = md.AddVertex(centre + a * halfA + b * halfB, new Vector2(1f, 1f));
        int i3 = md.AddVertex(centre - a * halfA + b * halfB, new Vector2(0f, 1f));
        md.AddTriangleFacingUp(0, i0, i1, i2);
        md.AddTriangleFacingUp(0, i0, i2, i3);
    }
}
