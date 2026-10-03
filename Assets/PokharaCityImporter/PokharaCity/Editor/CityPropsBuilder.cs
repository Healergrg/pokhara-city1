// =============================================================================
//  CityPropsBuilder.cs  —  Week 2: the things that make it feel like Pokhara
// =============================================================================
//  Builds, from the same map.osm file:
//
//    1. LAKE BARRIER  a green railing along the Phewa Lake shore, plus an
//                     invisible wall behind it so your car can't drive into
//                     the lake (like the railing along Lakeside's walkway).
//    2. KERBS         black-and-yellow kerbs along the edges of the big roads,
//                     with gaps at every junction so you can still turn.
//    3. POLES + WIRES concrete electric poles on the left side of the bigger
//                     roads, with sagging wires between them. Nothing says
//                     "Nepali street" like a pole with a tangle of cables!
//    4. TREES         along roads, around the lake, in parks and forests,
//                     plus every single tree that is marked on the map.
//    5. MOUNTAINS     Machhapuchhre, the Annapurna range, Dhaulagiri and
//                     Manaslu in the sky, in their REAL directions.
//
//  This file is "partial class CityMeshBuilder", which means it is the same
//  class as CityMeshBuilder.cs, just written in a second file (like chapter 2
//  of the same book). That lets it reuse the road widths and helpers there.
//
//  The window you click is PokharaPropsWindow.cs.
// =============================================================================

using System.Collections.Generic;
using UnityEngine;

public class PropsSettings
{
    public bool lakeBarrier = true;
    public bool kerbs = true;
    public bool kerbsSolid = true;     // car bumps over them (false = only painted on)
    public bool poles = true;
    public bool wires = true;
    public bool trees = true;
    public int maxTrees = 9000;        // safety limit so the scene stays fast
    public bool mountains = true;
}

public class PropsResult
{
    public List<BuiltMesh> meshes = new List<BuiltMesh>();
    public int treeCount, poleCount;
    public float kerbMetres, barrierMetres;
}

public static partial class CityMeshBuilder
{
    // =========================================================================
    //  MAIN ENTRY
    // =========================================================================
    public static PropsResult BuildProps(OsmData data, PropsSettings settings)
    {
        var result = new PropsResult();

        // A fixed "seed" = the random numbers come out the same every time,
        // so your trees don't jump around when you rebuild.
        var random = new System.Random(2026);

        // First, a map of where things already are (roads, buildings, water),
        // so we never put a tree in the middle of a road or inside a house.
        var world = new WorldMap(data);

        // The order matters: things placed first get the best spots.
        if (settings.lakeBarrier) BuildLakeBarrier(data, world, result);
        if (settings.kerbs) BuildKerbs(data, world, settings, result);
        if (settings.poles) BuildPoles(data, world, settings, random, result);
        if (settings.trees) BuildTrees(data, world, settings, random, result);
        if (settings.mountains) result.meshes.Add(BuildMountains());

        return result;
    }

    // =========================================================================
    //  WORLD MAP — "is this spot free?" questions
    // =========================================================================
    private class WorldMap
    {
        public readonly SegmentGrid allRoads = new SegmentGrid(40f);       // roads + footpaths
        public readonly SegmentGrid drivableRoads = new SegmentGrid(40f);  // only roads cars use
        public readonly SegmentGrid buildingWalls = new SegmentGrid(30f);
        public readonly PolygonGrid buildings = new PolygonGrid(40f);
        public readonly PolygonGrid water = new PolygonGrid(60f);
        public readonly PointGrid placed = new PointGrid(10f);              // trees, poles, railing
        public readonly List<List<Vector3>> shorelines = new List<List<Vector3>>();
        public float minX, maxX, minZ, maxZ;

        public WorldMap(OsmData data)
        {
            Vector3 a = data.ToUnity(data.minLat, data.minLon);
            Vector3 b = data.ToUnity(data.maxLat, data.maxLon);
            minX = a.x; minZ = a.z; maxX = b.x; maxZ = b.z;

            foreach (OsmWay way in data.ways)
            {
                string type = way.Tag("highway");
                if (type != null && RoadStyles.ContainsKey(type))
                {
                    RoadStyle style = RoadStyles[type];
                    float half = RoadWidth(way, style) / 2f;
                    List<Vector3> pts = data.WayPoints(way);
                    for (int i = 0; i < pts.Count - 1; i++)
                    {
                        allRoads.Add(pts[i], pts[i + 1], half);
                        if (style.drivable) drivableRoads.Add(pts[i], pts[i + 1], half);
                    }
                }

                string building = way.Tag("building");
                if (building != null && building != "no" && way.IsClosed)
                {
                    List<Vector3> outline = data.WayPoints(way);
                    buildings.Add(outline);
                    for (int i = 0; i < outline.Count - 1; i++) buildingWalls.Add(outline[i], outline[i + 1], 0f);
                }

                if (way.Tag("natural") == "water" && way.IsClosed)
                {
                    List<Vector3> pond = data.WayPoints(way);
                    water.Add(pond);
                    shorelines.Add(pond);
                }
            }

            // Phewa Lake: the same puzzle pieces the lake mesh is made from.
            foreach (OsmRelation rel in data.relations)
            {
                if (rel.Tag("natural") != "water" && rel.Tag("water") == null) continue;
                var pieces = new List<List<long>>();
                foreach (OsmMember m in rel.members)
                {
                    OsmWay w;
                    if (m.type == "way" && m.role == "outer" && data.waysById.TryGetValue(m.refId, out w))
                        pieces.Add(new List<long>(w.nodeIds));
                }
                foreach (List<long> ring in JoinPieces(pieces))
                {
                    var polygon = new List<Vector3>();
                    var shore = new List<Vector3>();
                    foreach (long id in ring)
                    {
                        if (data.nodes.ContainsKey(id))
                        {
                            Vector3 p = data.NodeToUnity(id);
                            polygon.Add(p);
                            shore.Add(p);
                        }
                        else if (shore.Count > 0)
                        {
                            // A missing piece (outside your map box): the shore line
                            // stops here, so no railing is built across dry land.
                            shorelines.Add(shore);
                            shore = new List<Vector3>();
                        }
                    }
                    if (shore.Count > 1) shorelines.Add(shore);
                    if (polygon.Count > 2) water.Add(polygon);
                }
            }
        }

        public bool InsideMap(Vector3 p, float margin = 0f)
        {
            return p.x > minX + margin && p.x < maxX - margin && p.z > minZ + margin && p.z < maxZ - margin;
        }

        // The usual checklist before putting something down, like checking a
        // parking space: inside the map, not on a road, not in a house, not
        // in the lake, and not on top of something already there.
        public bool IsFree(Vector3 p, float roadGap, float buildingGap, float spacing)
        {
            if (!InsideMap(p, 2f)) return false;
            if (allRoads.IsNearStreet(p, roadGap)) return false;
            if (buildings.Contains(p) || buildingWalls.IsNearStreet(p, buildingGap)) return false;
            if (water.Contains(p)) return false;
            if (!placed.IsClear(p, spacing)) return false;
            return true;
        }
    }

    // =========================================================================
    //  1. LAKE BARRIER
    // =========================================================================
    private static void BuildLakeBarrier(OsmData data, WorldMap world, PropsResult result)
    {
        var railing = new MeshData(1);
        var wall = new MeshData(1);

        foreach (List<Vector3> rawShore in world.shorelines)
        {
            List<Vector3> shore = Subdivide(CleanPoints(rawShore), 2.5f);
            if (shore.Count < 2) continue;

            var run = new List<Vector3>();
            Vector3 lastLandSide = Vector3.zero;
            for (int i = 0; i < shore.Count; i++)
            {
                Vector3 p = shore[i];
                Vector3 forward = i < shore.Count - 1 ? shore[i + 1] - p : p - shore[i - 1];
                Vector3 right = RightOf(forward);

                // Which side is land? Look one metre to each side: the side
                // that is NOT water is land. (Remember the last answer for
                // the rare spots where both sides look the same.)
                bool waterRight = world.water.Contains(p + right * 1.5f);
                bool waterLeft = world.water.Contains(p - right * 1.5f);
                Vector3 landSide = waterRight && !waterLeft ? -right : (!waterRight && waterLeft ? right : lastLandSide);
                if (landSide.sqrMagnitude < 0.5f) landSide = -right;
                lastLandSide = landSide;

                // Put the railing 1 m onto the land. If a road runs right
                // along the shore, move it towards the water instead so it
                // never blocks the road.
                Vector3 spot = Vector3.zero;
                bool found = false;
                float[] tries = { 1.0f, 0f, -1.5f, -3f, -5f };
                foreach (float t in tries)
                {
                    Vector3 q = p + landSide * t;
                    if (!world.drivableRoads.IsNearStreet(q, 1.0f)) { spot = q; found = true; break; }
                }

                if (!found || !world.InsideMap(p, 1f))
                {
                    // A road really goes into the water here (or we're at the
                    // map edge): finish this piece of railing and start a new one.
                    AddRailing(railing, wall, run, result);
                    run = new List<Vector3>();
                    continue;
                }
                run.Add(spot);
            }
            AddRailing(railing, wall, run, result);
        }

        if (railing.VertexCount == 0) return;
        result.meshes.Add(new BuiltMesh { name = "Lake railing", mesh = railing.ToMesh("LakeRailing"), materialKeys = new[] { "Railing" } });
        result.meshes.Add(new BuiltMesh { name = "Lake barrier (invisible wall)", mesh = wall.ToMesh("LakeWall"), materialKeys = new[] { "Railing" }, addCollider = true, hidden = true });
    }

    private static void AddRailing(MeshData railing, MeshData wall, List<Vector3> run, PropsResult result)
    {
        if (run.Count < 2) return;
        Vector3 up = Vector3.up;
        for (int i = 0; i < run.Count; i++)
        {
            Vector3 p = run[i];
            Vector3 along = i < run.Count - 1 ? run[i + 1] - p : p - run[i - 1];

            // A post at every other point (about every 5 m) and at the very end.
            if (i % 2 == 0 || i == run.Count - 1)
                AddTube(railing, 0, p, p + up * 1.05f, 0.05f, 0.05f, 4, along, true, 45f);
            if (i == run.Count - 1) break;

            Vector3 q = run[i + 1];
            // Two rails: top bar and middle bar.
            AddTube(railing, 0, p + up * 1.0f, q + up * 1.0f, 0.035f, 0.035f, 4, up, false, 45f);
            AddTube(railing, 0, p + up * 0.55f, q + up * 0.55f, 0.025f, 0.025f, 4, up, false, 45f);
            // The invisible wall: a thick block (about 1.4 m wide and tall,
            // reaching just below the ground) that the car bumps into.
            AddTube(wall, 0, p + up * 0.72f, q + up * 0.72f, 1.0f, 1.0f, 4, up, true, 45f);
            result.barrierMetres += Vector3.Distance(p, q);
        }
    }

    // =========================================================================
    //  2. KERBS  (black and yellow, like on Nepali main roads)
    // =========================================================================
    private static readonly HashSet<string> KerbRoads = new HashSet<string> { "trunk", "primary", "secondary", "tertiary" };

    private static void BuildKerbs(OsmData data, WorldMap world, PropsSettings settings, PropsResult result)
    {
        // Step 1: find every junction (a node shared by two or more roads,
        // including footpaths and lanes) and how wide the widest road there is.
        var useCount = new Dictionary<long, int>();
        var widest = new Dictionary<long, float>();
        foreach (OsmWay way in data.ways)
        {
            string type = way.Tag("highway");
            if (type == null || !RoadStyles.ContainsKey(type)) continue;
            float width = RoadWidth(way, RoadStyles[type]);
            for (int i = 0; i < way.nodeIds.Count; i++)
            {
                long id = way.nodeIds[i];
                bool isEnd = i == 0 || i == way.nodeIds.Count - 1;
                useCount[id] = (useCount.ContainsKey(id) ? useCount[id] : 0) + (isEnd ? 1 : 2);
                if (!widest.ContainsKey(id) || width > widest[id]) widest[id] = width;
            }
        }

        var md = new MeshData(1);
        foreach (OsmWay way in data.ways)
        {
            string type = way.Tag("highway");
            if (type == null || !KerbRoads.Contains(type)) continue;
            float half = RoadWidth(way, RoadStyles[type]) / 2f;

            // Step 2: the gaps. Around each junction (and each end of the
            // road) leave the kerb open, so cars can turn in and out.
            var gaps = new List<Vector3>();
            var gapSizes = new List<float>();
            for (int i = 0; i < way.nodeIds.Count; i++)
            {
                long id = way.nodeIds[i];
                if (!data.nodes.ContainsKey(id)) continue;
                bool isEnd = i == 0 || i == way.nodeIds.Count - 1;
                if (isEnd || useCount[id] >= 3)
                {
                    gaps.Add(data.NodeToUnity(id));
                    gapSizes.Add(widest[id] / 2f + 3f);
                }
            }

            List<Vector3> centre = Subdivide(CleanPoints(data.WayPoints(way)), 2f);
            if (centre.Count < 2) continue;

            // Step 3: one kerb on each side, cut into pieces at the gaps.
            for (int side = -1; side <= 1; side += 2)
            {
                List<Vector3> edge = OffsetLine(centre, side * (half + 0.15f));
                var run = new List<Vector3>();
                for (int i = 0; i < centre.Count; i++)
                {
                    bool open = false;
                    for (int g = 0; g < gaps.Count && !open; g++)
                        if (Vector3.Distance(centre[i], gaps[g]) < gapSizes[g]) open = true;

                    Vector3 q = edge[i];
                    // Also no kerb on top of another road, in a house or in the lake.
                    if (!open && (world.allRoads.IsNearStreet(q, -0.2f) || world.buildings.Contains(q) ||
                                  world.water.Contains(q) || !world.InsideMap(q))) open = true;

                    if (open) { result.kerbMetres += AddKerb(md, run); run = new List<Vector3>(); }
                    else run.Add(q);
                }
                result.kerbMetres += AddKerb(md, run);
            }
        }

        if (md.VertexCount > 0)
            result.meshes.Add(new BuiltMesh { name = "Kerbs", mesh = md.ToMesh("Kerbs"), materialKeys = new[] { "Kerb" }, addCollider = settings.kerbsSolid });
    }

    // A kerb stone strip: 30 cm wide, 15 cm tall. Returns its length.
    private static float AddKerb(MeshData md, List<Vector3> run)
    {
        run = CleanPoints(run);
        if (run.Count < 2) return 0f;
        const float height = 0.17f;   // roads are 4-6 cm thick, so 11-13 cm shows above them

        List<Vector3> left = OffsetLine(run, -0.15f);
        List<Vector3> right = OffsetLine(run, 0.15f);
        var along = new List<float> { 0f };
        for (int i = 1; i < run.Count; i++) along.Add(along[i - 1] + Vector3.Distance(run[i - 1], run[i]));

        // Top: the texture is 1 m black + 1 m yellow, repeated (u = metres / 2).
        int start = md.VertexCount;
        for (int i = 0; i < run.Count; i++)
        {
            md.AddVertex(left[i] + Vector3.up * height, new Vector2(along[i] / 2f, 0f));
            md.AddVertex(right[i] + Vector3.up * height, new Vector2(along[i] / 2f, 1f));
        }
        for (int i = 0; i < run.Count - 1; i++)
        {
            int l0 = start + i * 2, r0 = l0 + 1, l1 = l0 + 2, r1 = l0 + 3;
            md.AddTriangleFacingUp(0, l0, l1, r1);
            md.AddTriangleFacingUp(0, l0, r1, r0);
        }

        // Sides: the left side faces left, the right side faces right.
        for (int i = 0; i < run.Count - 1; i++)
        {
            float u0 = along[i] / 2f, u1 = along[i + 1] / 2f;
            AddWallQuad(md, 0, left[i], left[i + 1], 0f, height, u0, u1, 0f, 1f);
            AddWallQuad(md, 0, right[i + 1], right[i], 0f, height, u1, u0, 0f, 1f);
        }
        return along[along.Count - 1];
    }

    // =========================================================================
    //  3. ELECTRIC POLES AND WIRES
    // =========================================================================
    private static readonly HashSet<string> PoleRoads = new HashSet<string> { "trunk", "primary", "secondary", "tertiary", "unclassified", "residential" };
    private const float PoleHeight = 9f;
    private const float PoleSpacing = 38f;

    private static void BuildPoles(OsmData data, WorldMap world, PropsSettings settings, System.Random random, PropsResult result)
    {
        var poles = new MeshData(1);
        var wires = new MeshData(1);

        foreach (OsmWay way in data.ways)
        {
            string type = way.Tag("highway");
            if (type == null || !PoleRoads.Contains(type)) continue;
            List<Vector3> pts = CleanPoints(data.WayPoints(way));
            if (pts.Count < 2) continue;

            float half = RoadWidth(way, RoadStyles[type]) / 2f;
            var along = new List<float> { 0f };
            for (int i = 1; i < pts.Count; i++) along.Add(along[i - 1] + Vector3.Distance(pts[i - 1], pts[i]));
            float total = along[along.Count - 1];
            if (total < 30f) continue;

            // Walk along the road, one pole every ~38 m, on the LEFT side
            // (the side Nepali traffic drives on).
            var placedHere = new List<Vector3>();
            var acrossHere = new List<Vector3>();
            int count = Mathf.FloorToInt((total - 16f) / PoleSpacing) + 1;
            for (int k = 0; k < count; k++)
            {
                float d = 8f + k * PoleSpacing;
                bool done = false;
                // If the perfect spot is blocked, try a few metres either way.
                float[] nudges = { 0f, 4f, -4f, 8f, -8f };
                foreach (float n in nudges)
                {
                    float dd = Mathf.Max(1f, Mathf.Min(total - 1f, d + n));
                    Vector3 p = PointAlong(pts, along, dd);
                    Vector3 forward = PointAlong(pts, along, Mathf.Min(total, dd + 1f)) - PointAlong(pts, along, Mathf.Max(0f, dd - 1f));
                    Vector3 left = -RightOf(forward);
                    Vector3 spot = p + left * (half + 0.8f);

                    if (!world.InsideMap(spot, 2f) || world.allRoads.IsNearStreet(spot, 0.2f) ||
                        world.buildings.Contains(spot) || world.water.Contains(spot) || !world.placed.IsClear(spot, 6f)) continue;

                    AddPole(poles, spot, forward, left);
                    world.placed.Add(spot, 1.2f);
                    placedHere.Add(spot);
                    acrossHere.Add(left);
                    result.poleCount++;
                    done = true;
                    break;
                }
                if (!done)
                {
                    // No pole here: start a new line of wires after the gap.
                    if (settings.wires) AddWireLine(wires, placedHere, acrossHere);
                    placedHere.Clear(); acrossHere.Clear();
                }
            }
            if (settings.wires) AddWireLine(wires, placedHere, acrossHere);
        }

        if (poles.VertexCount > 0)
            result.meshes.Add(new BuiltMesh { name = "Electric poles", mesh = poles.ToMesh("Poles"), materialKeys = new[] { "Pole" }, addCollider = true });
        if (wires.VertexCount > 0)
            result.meshes.Add(new BuiltMesh { name = "Electric wires", mesh = wires.ToMesh("Wires"), materialKeys = new[] { "Wire" } });
    }

    // A tapered concrete pole with a cross-arm at the top and a small
    // transformer-style box halfway up on some poles.
    private static void AddPole(MeshData md, Vector3 bottom, Vector3 forward, Vector3 left)
    {
        Vector3 up = Vector3.up;
        AddTube(md, 0, bottom, bottom + up * PoleHeight, 0.2f, 0.11f, 4, forward, true, 45f);
        Vector3 armCentre = bottom + up * (PoleHeight - 0.35f);
        AddTube(md, 0, armCentre - left * 0.9f, armCentre + left * 0.9f, 0.06f, 0.06f, 4, up, true, 45f);
    }

    // Wires from pole to pole: 3 thin wires on the cross-arm and one thick
    // black bundle lower down (internet and TV cables), all sagging.
    private static void AddWireLine(MeshData md, List<Vector3> poles, List<Vector3> across)
    {
        for (int i = 0; i < poles.Count - 1; i++)
        {
            Vector3 a = poles[i], b = poles[i + 1];
            float span = Vector3.Distance(a, b);
            if (span > 60f) continue;   // too far apart (the road bent a lot)

            float top = PoleHeight - 0.27f;
            for (int w = -1; w <= 1; w++)
                AddSaggingWire(md, a + across[i] * (0.75f * w) + Vector3.up * top,
                                   b + across[i + 1] * (0.75f * w) + Vector3.up * top, 0.3f + span * 0.008f, 0.012f);
            AddSaggingWire(md, a + across[i] * 0.2f + Vector3.up * 6.6f,
                               b + across[i + 1] * 0.2f + Vector3.up * 6.6f, 0.6f + span * 0.015f, 0.035f);
        }
    }

    // A wire hangs in a curve (like a skipping rope held at both ends):
    // lowest in the middle. 4 * t * (1 - t) is 0 at the ends and 1 in the middle.
    private static void AddSaggingWire(MeshData md, Vector3 a, Vector3 b, float sag, float radius)
    {
        const int pieces = 6;   // 6 straight pieces look like a smooth curve from the road
        Vector3 previous = a;
        for (int k = 1; k <= pieces; k++)
        {
            float t = k / (float)pieces;
            Vector3 p = Vector3.Lerp(a, b, t) - Vector3.up * (sag * 4f * t * (1f - t));
            AddTube(md, 0, previous, p, radius, radius, 3, Vector3.up, false, 0f);   // 3-sided: thin wires don't need more
            previous = p;
        }
    }

    // =========================================================================
    //  4. TREES
    // =========================================================================
    private const float TreeTileSize = 250f;
    private const int SubTrunk = 0, SubLeavesFirst = 1, LeafColours = 3;

    private static readonly HashSet<string> ForestTags = new HashSet<string> { "landuse=forest", "natural=wood", "natural=scrub" };
    private static readonly HashSet<string> ParkTags = new HashSet<string> { "leisure=park", "leisure=garden", "landuse=grass", "landuse=meadow", "landuse=village_green", "leisure=common" };
    private static readonly HashSet<string> EdgeOnlyTags = new HashSet<string> { "landuse=recreation_ground", "natural=grassland" };   // open fields: trees only around the edge

    private static void BuildTrees(OsmData data, WorldMap world, PropsSettings settings, System.Random random, PropsResult result)
    {
        var tiles = new Dictionary<string, MeshData>();

        // Local helper: place one tree if the spot is free.
        System.Func<Vector3, float, float, bool> tryPlant = (p, scale, spacing) =>
        {
            if (result.treeCount >= settings.maxTrees) return false;
            if (!world.IsFree(p, 1.0f, 2.0f, spacing)) return false;

            int tx = Mathf.FloorToInt(p.x / TreeTileSize), tz = Mathf.FloorToInt(p.z / TreeTileSize);
            string key = tx + "_" + tz;
            if (!tiles.ContainsKey(key)) tiles[key] = new MeshData(1 + LeafColours);
            AddTree(tiles[key], p, scale, random);
            world.placed.Add(p, 2.5f);
            result.treeCount++;
            return true;
        };

        // (a) Every tree that someone has mapped on OpenStreetMap.
        foreach (OsmNode node in data.nodes.Values)
            if (node.Tag("natural") == "tree") tryPlant(data.ToUnity(node.lat, node.lon), 1.1f, 2f);

        // (b) Along the roads, on both sides, about every 14 m (not every spot is free).
        foreach (OsmWay way in data.ways)
        {
            string type = way.Tag("highway");
            if (type == null || !RoadStyles.ContainsKey(type) || !RoadStyles[type].drivable) continue;
            if (type == "service" || type == "track") continue;
            float half = RoadWidth(way, RoadStyles[type]) / 2f;
            List<Vector3> pts = CleanPoints(data.WayPoints(way));
            if (pts.Count < 2) continue;
            var along = new List<float> { 0f };
            for (int i = 1; i < pts.Count; i++) along.Add(along[i - 1] + Vector3.Distance(pts[i - 1], pts[i]));
            float total = along[along.Count - 1];
            float chance = RoadStyles[type].materialKey == "RoadMain" ? 0.6f : 0.35f;

            for (float d = 5f; d < total - 5f; d += 14f)
            {
                Vector3 p = PointAlong(pts, along, d);
                Vector3 right = RightOf(PointAlong(pts, along, Mathf.Min(total, d + 1f)) - PointAlong(pts, along, Mathf.Max(0f, d - 1f)));
                for (int side = -1; side <= 1; side += 2)
                {
                    if (random.NextDouble() > chance) continue;
                    float offset = half + 1.8f + (float)random.NextDouble() * 1.5f;
                    tryPlant(p + right * (side * offset), 0.9f + (float)random.NextDouble() * 0.4f, 7f);
                }
            }
        }

        // (c) Around the lake shore, a few metres onto the land.
        foreach (List<Vector3> rawShore in world.shorelines)
        {
            List<Vector3> shore = Subdivide(CleanPoints(rawShore), 9f);
            for (int i = 0; i < shore.Count - 1; i++)
            {
                if (random.NextDouble() > 0.65) continue;
                Vector3 right = RightOf(shore[i + 1] - shore[i]);
                float inland = 4f + (float)random.NextDouble() * 4f;
                // Try both sides: the one that is land will pass the "not in water" test.
                if (!tryPlant(shore[i] + right * inland, 1.0f + (float)random.NextDouble() * 0.4f, 6f))
                    tryPlant(shore[i] - right * inland, 1.0f + (float)random.NextDouble() * 0.4f, 6f);
            }
        }

        // (d) Parks and gardens (a few trees), then forests (lots of trees).
        PlantInAreas(data, world, ParkTags, 16f, 1.0f, random, tryPlant);
        PlantAroundEdges(data, EdgeOnlyTags, random, tryPlant);
        PlantInAreas(data, world, ForestTags, 7f, 1.2f, random, tryPlant);

        string[] keys = new string[1 + LeafColours];
        keys[SubTrunk] = "Trunk";
        for (int i = 0; i < LeafColours; i++) keys[SubLeavesFirst + i] = "Leaves" + i;
        foreach (var pair in tiles)
            result.meshes.Add(new BuiltMesh { name = "Trees " + pair.Key, mesh = pair.Value.ToMesh("Trees_" + pair.Key), materialKeys = keys, addCollider = true });
    }

    private static bool HasAnyTag(OsmWay way, HashSet<string> wanted)
    {
        foreach (var tag in way.tags)
            if (wanted.Contains(tag.Key + "=" + tag.Value)) return true;
        return false;
    }

    // Fill an area with trees on a wobbly grid (a perfect grid looks like an orchard).
    private static void PlantInAreas(OsmData data, WorldMap world, HashSet<string> tags, float spacing, float scale,
                                     System.Random random, System.Func<Vector3, float, float, bool> tryPlant)
    {
        foreach (OsmWay way in data.ways)
        {
            if (!way.IsClosed || !HasAnyTag(way, tags)) continue;
            List<Vector3> area = data.WayPoints(way);
            if (area.Count < 3) continue;

            float x0 = float.MaxValue, x1 = float.MinValue, z0 = float.MaxValue, z1 = float.MinValue;
            foreach (Vector3 p in area) { x0 = Mathf.Min(x0, p.x); x1 = Mathf.Max(x1, p.x); z0 = Mathf.Min(z0, p.z); z1 = Mathf.Max(z1, p.z); }
            // Only the part inside your map box.
            x0 = Mathf.Max(x0, world.minX); x1 = Mathf.Min(x1, world.maxX);
            z0 = Mathf.Max(z0, world.minZ); z1 = Mathf.Min(z1, world.maxZ);

            for (float x = x0; x < x1; x += spacing)
                for (float z = z0; z < z1; z += spacing)
                {
                    float jx = ((float)random.NextDouble() - 0.5f) * spacing * 0.8f;
                    float jz = ((float)random.NextDouble() - 0.5f) * spacing * 0.8f;
                    Vector3 p = new Vector3(x + jx, 0f, z + jz);
                    if (!Triangulator.ContainsPoint(area, p)) continue;
                    tryPlant(p, scale * (0.8f + (float)random.NextDouble() * 0.5f), spacing * 0.6f);
                }
        }
    }

    private static void PlantAroundEdges(OsmData data, HashSet<string> tags, System.Random random,
                                         System.Func<Vector3, float, float, bool> tryPlant)
    {
        foreach (OsmWay way in data.ways)
        {
            if (!way.IsClosed || !HasAnyTag(way, tags)) continue;
            List<Vector3> edge = CleanPoints(data.WayPoints(way));
            if (edge.Count < 4) continue;
            if (edge.Count > 1 && (edge[0] - edge[edge.Count - 1]).sqrMagnitude < 0.01f) edge.RemoveAt(edge.Count - 1);
            MakeClockwise(edge);   // clockwise = the inside is on the right
            edge.Add(edge[0]);
            edge = Subdivide(edge, 10f);
            for (int i = 0; i < edge.Count - 1; i++)
            {
                if (random.NextDouble() > 0.7) continue;
                Vector3 inward = RightOf(edge[i + 1] - edge[i]);
                tryPlant(edge[i] + inward * 3f, 1.0f + (float)random.NextDouble() * 0.3f, 6f);
            }
        }
    }

    // One low-poly tree: a brown trunk + a lumpy ball of leaves.
    // Three shapes, picked at random:
    //   round  (most common, like the big trees along Lakeside)
    //   tall   (narrow, like the Ashoka trees planted along roads)
    //   double (two blobs, a bigger older tree)
    private static void AddTree(MeshData md, Vector3 ground, float scale, System.Random random)
    {
        int leaves = SubLeavesFirst + random.Next(LeafColours);
        double shape = random.NextDouble();
        Vector3 up = Vector3.up;
        Vector3 turn = new Vector3((float)random.NextDouble() - 0.5f, 0f, (float)random.NextDouble() - 0.5f);

        if (shape < 0.25)
        {
            // Tall and narrow.
            float trunk = 1.6f * scale;
            AddTube(md, SubTrunk, ground, ground + up * (trunk + 0.5f), 0.14f * scale, 0.09f * scale, 5, turn, true, 0f);
            float height = (3.2f + (float)random.NextDouble() * 1.2f) * scale;
            AddBlob(md, leaves, ground + up * (trunk + height * 0.5f), new Vector3(1.1f, height * 0.5f, 1.1f) * scale, random);
            return;
        }

        float trunkHeight = (2.7f + (float)random.NextDouble() * 0.9f) * scale;
        AddTube(md, SubTrunk, ground, ground + up * (trunkHeight + 0.6f), 0.2f * scale, 0.12f * scale, 5, turn, true, 0f);
        float r = (1.9f + (float)random.NextDouble() * 0.9f) * scale;
        Vector3 centre = ground + up * (trunkHeight + r * 0.65f);

        if (shape < 0.85)
        {
            AddBlob(md, leaves, centre, new Vector3(r, r * 0.8f, r), random);
        }
        else
        {
            Vector3 side = new Vector3(turn.z, 0f, -turn.x).normalized * (r * 0.55f);
            AddBlob(md, leaves, centre + side, new Vector3(r * 0.85f, r * 0.7f, r * 0.85f), random);
            AddBlob(md, leaves, centre - side + up * (r * 0.3f), new Vector3(r * 0.75f, r * 0.7f, r * 0.75f), random);
        }
    }

    // A squashed, bumpy ball (like a crumpled paper ball) made of
    // rings of points, from the top to the bottom.
    private static void AddBlob(MeshData md, int subMesh, Vector3 centre, Vector3 size, System.Random random)
    {
        const int rings = 4, around = 7;
        var positions = new List<Vector3>();
        var index = new List<int>();

        // Top point, then 'rings' rings of 'around' points, then the bottom point.
        System.Action<Vector3> add = p => { positions.Add(p); index.Add(md.AddVertex(p, new Vector2(p.x * 0.3f, p.y * 0.3f))); };
        add(centre + new Vector3(0f, size.y, 0f));
        float twist = (float)random.NextDouble() * Mathf.PI;
        for (int k = 1; k <= rings; k++)
        {
            float lat = Mathf.PI * k / (rings + 1);   // 0 = top, PI = bottom
            for (int j = 0; j < around; j++)
            {
                float lon = twist + j * Mathf.PI * 2f / around + (k % 2) * Mathf.PI / around;
                float bump = 0.82f + (float)random.NextDouble() * 0.3f;
                add(centre + new Vector3(Mathf.Sin(lat) * Mathf.Cos(lon) * size.x,
                                         Mathf.Cos(lat) * size.y,
                                         Mathf.Sin(lat) * Mathf.Sin(lon) * size.z) * bump);
            }
        }
        add(centre + new Vector3(0f, -size.y * 0.75f, 0f));
        int bottom = positions.Count - 1;

        // Top cap, the bands between rings, and the bottom cap.
        for (int j = 0; j < around; j++)
            AddTriangleOutward(md, subMesh, positions, index, centre, 0, 1 + j, 1 + (j + 1) % around);
        for (int k = 0; k < rings - 1; k++)
            for (int j = 0; j < around; j++)
            {
                int a = 1 + k * around + j, b = 1 + k * around + (j + 1) % around;
                int c = 1 + (k + 1) * around + j, d = 1 + (k + 1) * around + (j + 1) % around;
                AddTriangleOutward(md, subMesh, positions, index, centre, a, c, b);
                AddTriangleOutward(md, subMesh, positions, index, centre, b, c, d);
            }
        int last = 1 + (rings - 1) * around;
        for (int j = 0; j < around; j++)
            AddTriangleOutward(md, subMesh, positions, index, centre, bottom, last + j, last + (j + 1) % around);
    }

    // =========================================================================
    //  5. MOUNTAINS
    // =========================================================================
    //  The real Himalaya is 30-70 km away, far beyond the camera's view
    //  distance (3 km). So we build a SMALL model of them about 2.5-3 km
    //  away that moves along with the camera (MountainBackdrop.cs). Because
    //  it always stays the same distance away, it looks infinitely far,
    //  just like the moon seems to follow your car at night.
    //
    //  Each peak is placed by its real compass direction from Lakeside and
    //  its real size in the sky (angle above the horizon), worked out from
    //  its height and distance:   angle = atan(height above Pokhara / distance)
    // =========================================================================
    private struct Peak
    {
        public float bearing, angle, width;   // degrees: 0 = north, 90 = east
        public Peak(float b, float a, float w) { bearing = b; angle = a; width = w; }
    }

    private static readonly Peak[] Himalaya =
    {
        new Peak(-40.5f, 6.0f, 5.0f),   // Dhaulagiri       8167 m, 71 km
        new Peak(-24.0f, 9.7f, 3.5f),   // Annapurna South  7219 m, 37 km
        new Peak(-20.5f, 9.0f, 2.5f),   // Hiunchuli        6441 m, 32 km
        new Peak(-16.5f, 9.1f, 3.0f),   // Annapurna I      8091 m, 45 km
        new Peak( -1.9f, 11.1f, 1.7f),  // Machhapuchhre    6993 m, 31 km (the "Fishtail")
        new Peak( -0.9f, 10.5f, 1.0f),  //   its second summit (the other half of the fishtail)
        new Peak(  6.0f, 9.2f, 3.0f),   // Annapurna III    7555 m, 42 km
        new Peak( 19.0f, 10.0f, 3.0f),  // Annapurna IV     7525 m, 39 km
        new Peak( 23.5f, 10.1f, 3.5f),  // Annapurna II     7937 m, 40 km
        new Peak( 39.0f, 9.6f, 4.0f),   // Lamjung Himal    6983 m, 33 km
        new Peak( 57.0f, 6.1f, 5.0f),   // Manaslu          8163 m, 70 km
    };

    private const float MountainNear = 2450f, MountainFar = 2950f;
    private const float SnowRidge = 2850f, HillRidge = 2620f;

    private static BuiltMesh BuildMountains()
    {
        // Sub-meshes: 0 = green hills, 1 = bare rock, 2 = snow.
        var md = new MeshData(3);
        const int columns = 720;     // one every half degree, all the way round
        const int rows = 41;         // from near (2450 m) to far (2950 m)

        var heights = new float[columns, rows];
        var snowy = new bool[columns, rows];
        var range = new bool[columns, rows];   // true = part of the far snow range, false = green hills
        var grid = new int[columns, rows];

        for (int c = 0; c < columns; c++)
        {
            float bearing = c * 0.5f;             // 0..360
            float b = bearing > 180f ? bearing - 360f : bearing;   // -180..180
            float rad = bearing * Mathf.PI / 180f;
            Vector3 dir = new Vector3(Mathf.Sin(rad), 0f, Mathf.Cos(rad));   // 0 = north (+z)

            float snowAngle = SnowRangeAngle(b);
            float hillAngle = HillAngle(b);

            for (int r = 0; r < rows; r++)
            {
                float dist = Mathf.Lerp(MountainNear, MountainFar, r / (float)(rows - 1));

                // Snow range: a ridge at 2850 m, rising from 2500 m.
                float snowRise = Mathf.Clamp01((dist - 2500f) / (SnowRidge - 2500f));
                float snowH = SnowRidge * Tan(snowAngle) * Mathf.Pow(snowRise, 1.4f);

                // Hills: a bump centred at 2620 m.
                float hillBump = Mathf.Clamp01(1f - Mathf.Abs(dist - HillRidge) / 380f);
                hillBump = hillBump * hillBump * (3f - 2f * hillBump);   // smooth bump
                float hillH = HillRidge * Tan(hillAngle) * hillBump * (0.85f + 0.3f * Noise(b * 0.7f + dist * 0.013f));

                float front = Mathf.Clamp01((dist - MountainNear) / 80f);   // nearest row sits on the ground
                float h = Mathf.Max(snowH, hillH) * front;

                // Rugged rock: little random cracks and ridges.
                h *= 0.95f + 0.1f * Noise(b * 1.2f + dist * 0.02f) * Noise(dist * 0.03f - b * 0.4f + 17f) * 2f;

                heights[c, r] = h;
                // Snow above ~6 degrees on the snow range (the snow line).
                range[c, r] = snowH >= hillH && snowH > 1f;
                snowy[c, r] = range[c, r] && h > dist * Tan(6.2f);

                Vector3 p = dir * dist + Vector3.up * (h - 2f);   // 2 m down so no gap at the bottom
                grid[c, r] = md.AddVertex(p, new Vector2(c / 8f, h / 50f));
            }
        }

        for (int c = 0; c < columns; c++)
        {
            int c2 = (c + 1) % columns;
            for (int r = 0; r < rows - 1; r++)
            {
                int a = grid[c, r], b = grid[c2, r], d = grid[c, r + 1], e = grid[c2, r + 1];
                float average = (heights[c, r] + heights[c2, r] + heights[c, r + 1] + heights[c2, r + 1]) / 4f;
                int sub;
                if (snowy[c, r] || snowy[c2, r] || snowy[c, r + 1] || snowy[c2, r + 1]) sub = 2;
                else if ((range[c, r] || range[c2, r] || range[c, r + 1] || range[c2, r + 1]) && average > 120f) sub = 1;
                else sub = 0;
                // Like the ground, mountain triangles face up (towards the sky).
                md.AddTriangleFacingUp(sub, a, d, b);
                md.AddTriangleFacingUp(sub, b, d, e);
            }
        }

        return new BuiltMesh
        {
            name = "Mountains (Machhapuchhre + Annapurna)",
            mesh = md.ToMesh("Mountains"),
            materialKeys = new[] { "MountainHills", "MountainRock", "MountainSnow" },
            followCamera = true
        };
    }

    // How high the snow range rises in the sky (degrees) in a given direction.
    private static float SnowRangeAngle(float bearing)
    {
        // A base ridge from Dhaulagiri to Manaslu, with peaks on top.
        float baseAngle = 0f;
        if (bearing > -52f && bearing < 66f)
        {
            float fade = Mathf.Clamp01(Mathf.Min(bearing + 52f, 66f - bearing) / 10f);
            baseAngle = (5.6f + 1.2f * Noise(bearing * 0.35f)) * fade;
        }
        float best = baseAngle;
        foreach (Peak p in Himalaya)
        {
            float d = Mathf.Abs(bearing - p.bearing) / p.width;
            if (d > 3f) continue;
            // A pointy peak: highest at the centre, sliding down to 4.5 degrees.
            float a = p.angle - (p.angle - 4.5f) * Mathf.Pow(Mathf.Min(d / 3f, 1f), 0.75f);
            best = Mathf.Max(best, a);
        }
        return best;
    }

    // The green hills all around the Pokhara valley (degrees above the horizon).
    private static float HillAngle(float bearing)
    {
        float a = 2.6f + 1.6f * Noise(bearing * 0.12f) + 0.8f * Noise(bearing * 0.5f + 40f);
        // Sarangkot ridge (north-west, above the lake) and the Peace Pagoda
        // hills (south, across the lake) are bigger and closer.
        a += 4.5f * Bell(bearing, -38f, 14f);
        a += 3.0f * Bell(bearing, 180f, 25f) + 3.0f * Bell(bearing, -180f, 25f);
        // The valley opens out towards the east (Pokhara city), so lower hills there.
        a -= 1.2f * Bell(bearing, 100f, 30f);
        return Mathf.Max(a, 1.2f);
    }

    private static float Bell(float x, float centre, float width)
    {
        float d = (x - centre) / width;
        return (float)System.Math.Exp(-d * d);
    }

    private static float Tan(float degrees) { return (float)System.Math.Tan(degrees * System.Math.PI / 180.0); }

    // Smooth random wiggle between 0 and 1 (the same input always gives the
    // same answer, so mountains don't change shape every import).
    private static float Noise(float x)
    {
        int i = Mathf.FloorToInt(x);
        float f = x - i;
        f = f * f * (3f - 2f * f);
        return Mathf.Lerp(Hash01(i), Hash01(i + 1), f);
    }

    private static float Hash01(int n)
    {
        unchecked
        {
            uint h = (uint)n * 747796405u + 2891336453u;
            h = ((h >> (int)((h >> 28) + 4u)) ^ h) * 277803737u;
            h = (h >> 22) ^ h;
            return (h & 0xFFFFFF) / (float)0xFFFFFF;
        }
    }

    // =========================================================================
    //  SHAPE HELPERS
    // =========================================================================

    // A tube (or square beam) from a to b. r0 = radius at a, r1 = radius at b.
    // 'sideHint' says which way the flat sides point (for square poles).
    private static void AddTube(MeshData md, int subMesh, Vector3 a, Vector3 b, float r0, float r1, int sides,
                                Vector3 sideHint, bool caps, float angleOffsetDegrees)
    {
        Vector3 dir = b - a;
        if (dir.sqrMagnitude < 1e-6f) return;
        dir = dir.normalized;

        // Two directions at right angles to the tube, like the x and y of a clock face.
        Vector3 side = sideHint - dir * Vector3.Dot(sideHint, dir);
        if (side.sqrMagnitude < 1e-4f)
        {
            side = new Vector3(1f, 0f, 0f) - dir * dir.x;
            if (side.sqrMagnitude < 1e-4f) side = new Vector3(0f, 0f, 1f) - dir * dir.z;
        }
        side = side.normalized;
        Vector3 other = Vector3.Cross(dir, side).normalized;

        var ringA = new Vector3[sides];
        var ringB = new Vector3[sides];
        float offset = angleOffsetDegrees * Mathf.PI / 180f;
        for (int i = 0; i < sides; i++)
        {
            float angle = offset + i * Mathf.PI * 2f / sides;
            Vector3 o = side * Mathf.Cos(angle) + other * Mathf.Sin(angle);
            ringA[i] = a + o * r0;
            ringB[i] = b + o * r1;
        }

        Vector3 middle = (a + b) / 2f;
        for (int i = 0; i < sides; i++)
        {
            int j = (i + 1) % sides;
            AddQuadOutward(md, subMesh, ringA[i], ringA[j], ringB[j], ringB[i], middle,
                           i / (float)sides, (i + 1) / (float)sides);
        }

        if (!caps) return;
        for (int i = 1; i < sides - 1; i++)
        {
            AddTriangleOutward(md, subMesh, ringA[0], ringA[i], ringA[i + 1], middle);
            AddTriangleOutward(md, subMesh, ringB[0], ringB[i], ringB[i + 1], middle);
        }
    }

    // A four-cornered face that is visible from the side away from 'inside'.
    private static void AddQuadOutward(MeshData md, int subMesh, Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3,
                                       Vector3 inside, float u0, float u1)
    {
        int i0 = md.AddVertex(p0, new Vector2(u0, 0f));
        int i1 = md.AddVertex(p1, new Vector2(u1, 0f));
        int i2 = md.AddVertex(p2, new Vector2(u1, 1f));
        int i3 = md.AddVertex(p3, new Vector2(u0, 1f));
        // Unity shows a triangle from the side its "normal" arrow points to.
        // normal = Cross(p1 - p0, p2 - p0). If it points inwards, flip the order.
        Vector3 normal = Vector3.Cross(p1 - p0, p2 - p0);
        bool flip = Vector3.Dot(normal, (p0 + p1 + p2 + p3) / 4f - inside) < 0f;
        if (flip) { md.AddTriangle(subMesh, i0, i2, i1); md.AddTriangle(subMesh, i0, i3, i2); }
        else { md.AddTriangle(subMesh, i0, i1, i2); md.AddTriangle(subMesh, i0, i2, i3); }
    }

    private static void AddTriangleOutward(MeshData md, int subMesh, Vector3 p0, Vector3 p1, Vector3 p2, Vector3 inside)
    {
        int i0 = md.AddVertex(p0, new Vector2(0.5f, 0.5f));
        int i1 = md.AddVertex(p1, new Vector2(0.5f, 0.5f));
        int i2 = md.AddVertex(p2, new Vector2(0.5f, 0.5f));
        Vector3 normal = Vector3.Cross(p1 - p0, p2 - p0);
        if (Vector3.Dot(normal, (p0 + p1 + p2) / 3f - inside) < 0f) md.AddTriangle(subMesh, i0, i2, i1);
        else md.AddTriangle(subMesh, i0, i1, i2);
    }

    // Same, for corners that were already added (shared, for smooth shading).
    private static void AddTriangleOutward(MeshData md, int subMesh, List<Vector3> positions, List<int> index,
                                           Vector3 inside, int a, int b, int c)
    {
        Vector3 p0 = positions[a], p1 = positions[b], p2 = positions[c];
        Vector3 normal = Vector3.Cross(p1 - p0, p2 - p0);
        if (normal.sqrMagnitude < 1e-10f) return;   // squashed flat, invisible anyway
        if (Vector3.Dot(normal, (p0 + p1 + p2) / 3f - inside) < 0f) md.AddTriangle(subMesh, index[a], index[c], index[b]);
        else md.AddTriangle(subMesh, index[a], index[b], index[c]);
    }
}

// =============================================================================
//  PolygonGrid — "which building (or lake) is this point inside?"
//  Shapes are sorted into square buckets, so we only test the few nearby.
// =============================================================================
public class PolygonGrid
{
    private readonly float cellSize;
    private readonly List<List<Vector3>> polygons = new List<List<Vector3>>();
    private readonly List<Vector4> boxes = new List<Vector4>();   // minX, minZ, maxX, maxZ
    private readonly Dictionary<long, List<int>> cells = new Dictionary<long, List<int>>();

    public PolygonGrid(float cellSize) { this.cellSize = cellSize; }
    private static long Key(int x, int z) { return ((long)x << 32) ^ (uint)z; }

    public void Add(List<Vector3> polygon)
    {
        if (polygon.Count < 3) return;
        float x0 = float.MaxValue, x1 = float.MinValue, z0 = float.MaxValue, z1 = float.MinValue;
        foreach (Vector3 p in polygon) { x0 = Mathf.Min(x0, p.x); x1 = Mathf.Max(x1, p.x); z0 = Mathf.Min(z0, p.z); z1 = Mathf.Max(z1, p.z); }
        int id = polygons.Count;
        polygons.Add(polygon);
        boxes.Add(new Vector4(x0, z0, x1, z1));
        for (int x = Mathf.FloorToInt(x0 / cellSize); x <= Mathf.FloorToInt(x1 / cellSize); x++)
            for (int z = Mathf.FloorToInt(z0 / cellSize); z <= Mathf.FloorToInt(z1 / cellSize); z++)
            {
                long k = Key(x, z);
                if (!cells.ContainsKey(k)) cells[k] = new List<int>();
                cells[k].Add(id);
            }
    }

    public bool Contains(Vector3 p)
    {
        List<int> list;
        if (!cells.TryGetValue(Key(Mathf.FloorToInt(p.x / cellSize), Mathf.FloorToInt(p.z / cellSize)), out list)) return false;
        foreach (int id in list)
        {
            Vector4 b = boxes[id];
            if (p.x < b.x || p.z < b.y || p.x > b.z || p.z > b.w) continue;
            if (Triangulator.ContainsPoint(polygons[id], p)) return true;
        }
        return false;
    }
}

// =============================================================================
//  PointGrid — remembers where trees and poles already stand, so two
//  things are never planted in the same spot.
// =============================================================================
public class PointGrid
{
    private readonly float cellSize;
    private readonly Dictionary<long, List<Vector4>> cells = new Dictionary<long, List<Vector4>>();   // x, z, radius

    public PointGrid(float cellSize) { this.cellSize = cellSize; }
    private static long Key(int x, int z) { return ((long)x << 32) ^ (uint)z; }

    public void Add(Vector3 p, float radius)
    {
        long k = Key(Mathf.FloorToInt(p.x / cellSize), Mathf.FloorToInt(p.z / cellSize));
        if (!cells.ContainsKey(k)) cells[k] = new List<Vector4>();
        cells[k].Add(new Vector4(p.x, p.z, radius, 0f));
    }

    // Free if nothing is closer than 'spacing' (or than the other thing's own size).
    public bool IsClear(Vector3 p, float spacing)
    {
        int cx = Mathf.FloorToInt(p.x / cellSize), cz = Mathf.FloorToInt(p.z / cellSize);
        int reach = Mathf.Max(1, Mathf.FloorToInt(spacing / cellSize) + 1);
        for (int x = cx - reach; x <= cx + reach; x++)
            for (int z = cz - reach; z <= cz + reach; z++)
            {
                List<Vector4> list;
                if (!cells.TryGetValue(Key(x, z), out list)) continue;
                foreach (Vector4 q in list)
                {
                    float dx = q.x - p.x, dz = q.y - p.z;
                    float need = Mathf.Max(spacing, q.z);
                    if (dx * dx + dz * dz < need * need) return false;
                }
            }
        return true;
    }
}
