// =============================================================================
//  CityMeshBuilder.cs  —  turns map data into 3D shapes
// =============================================================================
//  Real-life analogy: OsmData.cs is the paper map. This file is the
//  construction crew that reads the map and pours concrete:
//
//    roads      -> flat strips ("ribbons") laid along each road line
//    junctions  -> round patches so corners have no gaps
//    buildings  -> the outline is pulled straight up into walls + a flat roof
//    lake/parks -> flat coloured shapes on the ground
//    lanes      -> invisible driving lines on the LEFT side (Nepal) for traffic AI
//
//  Nothing here touches the scene. It only makes meshes and lane data;
//  PokharaCityImporterWindow.cs puts them into your scene.
// =============================================================================

using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.Rendering;

// All the options from the importer window, in one box.
[System.Serializable]
public class CityImportSettings
{
    public bool roads = true;
    public bool footpaths = true;
    public bool centreLines = true;
    public bool buildings = true;
    public bool water = true;
    public bool greenAreas = true;
    public bool trafficLanes = true;
    public bool shopFronts = true;     // shops on ground floors that face a street
    public bool roofDetails = true;    // low roof walls + black water tanks

    public float metresPerFloor = 3.2f;
    public int minFloors = 2;       // used when OpenStreetMap does not know a building's height
    public int maxFloors = 4;
    public float buildingTileSize = 250f;   // buildings are merged into 250 m x 250 m blocks for speed
    public float groundMargin = 300f;       // extra ground around the exported box
}

// A finished mesh plus the names of the materials each part should use.
public class BuiltMesh
{
    public string name;
    public Mesh mesh;
    public string[] materialKeys;   // one per sub-mesh, e.g. { "Roof", "Wall0", "Wall1" }
    public bool addCollider;
    public bool hidden;         // true = collider only, nothing drawn (invisible lake wall)
    public bool followCamera;   // true = moves with the camera (far-away mountains)
}

// Everything the builder produces.
public class CityBuildResult
{
    public List<BuiltMesh> meshes = new List<BuiltMesh>();
    public List<Lane> lanes = new List<Lane>();
    public int roadCount, buildingCount, waterCount, greenCount;
}

public static partial class CityMeshBuilder   // "partial" = the props code (CityPropsBuilder.cs) is part of this class too
{
    // -------------------------------------------------------------------------
    // Road look-up table: how wide each road type is (metres) and how high
    // above the ground it sits. Bigger roads sit a few millimetres higher so
    // they are drawn on top where two roads overlap (stops flickering).
    // -------------------------------------------------------------------------
    private struct RoadStyle
    {
        public float width, height, speedKmh; public bool drivable; public string materialKey;
        public RoadStyle(float w, float h, float s, bool d, string m) { width = w; height = h; speedKmh = s; drivable = d; materialKey = m; }
    }

    private static readonly Dictionary<string, RoadStyle> RoadStyles = new Dictionary<string, RoadStyle>
    {
        //                     width  height  speed(game) drivable  material
        { "trunk",         new RoadStyle(10f, 0.060f, 50f, true,  "RoadMain") },
        { "primary",       new RoadStyle( 9f, 0.055f, 40f, true,  "RoadMain") },
        { "secondary",     new RoadStyle( 8f, 0.050f, 40f, true,  "RoadMain") },
        { "tertiary",      new RoadStyle( 7f, 0.045f, 30f, true,  "Road") },
        { "unclassified",  new RoadStyle( 6f, 0.040f, 25f, true,  "Road") },
        { "residential",   new RoadStyle( 6f, 0.040f, 25f, true,  "Road") },
        { "living_street", new RoadStyle( 5f, 0.035f, 15f, true,  "Road") },
        { "service",       new RoadStyle( 4f, 0.030f, 15f, true,  "Road") },
        { "track",         new RoadStyle(3.5f,0.025f, 15f, false, "Dirt") },
        { "pedestrian",    new RoadStyle( 4f, 0.020f,  0f, false, "Footpath") },
        { "footway",       new RoadStyle( 2f, 0.020f,  0f, false, "Footpath") },
        { "path",          new RoadStyle( 2f, 0.020f,  0f, false, "Footpath") },
        { "steps",         new RoadStyle( 2f, 0.020f,  0f, false, "Footpath") },
        { "cycleway",      new RoadStyle( 2f, 0.020f,  0f, false, "Footpath") },
    };

    private const int WallColourCount = 5;   // buildings get one of 5 wall colours (Pokhara is colourful!)

    // =========================================================================
    //  MAIN ENTRY: build everything that is switched on in the settings.
    // =========================================================================
    public static CityBuildResult Build(OsmData data, CityImportSettings settings)
    {
        var result = new CityBuildResult();

        result.meshes.Add(BuildGround(data, settings));
        if (settings.roads) BuildRoads(data, settings, result);
        if (settings.buildings) BuildBuildings(data, settings, result);
        if (settings.water) BuildWater(data, result);
        if (settings.greenAreas) BuildGreenAreas(data, result);
        if (settings.trafficLanes) result.lanes = BuildLanes(data);

        return result;
    }

    // =========================================================================
    //  GROUND: one big flat rectangle under the whole map (with a collider,
    //  so your car has something to drive on).
    // =========================================================================
    private static BuiltMesh BuildGround(OsmData data, CityImportSettings settings)
    {
        Vector3 a = data.ToUnity(data.minLat, data.minLon);
        Vector3 b = data.ToUnity(data.maxLat, data.maxLon);
        float m = settings.groundMargin;
        float x0 = a.x - m, x1 = b.x + m, z0 = a.z - m, z1 = b.z + m;

        var md = new MeshData(1);
        // Four corners, listed clockwise when seen from above so the face points up.
        int i0 = md.AddVertex(new Vector3(x0, 0, z0), new Vector2(x0, z0) / 25f);   // texture repeats every 25 m
        int i1 = md.AddVertex(new Vector3(x0, 0, z1), new Vector2(x0, z1) / 25f);
        int i2 = md.AddVertex(new Vector3(x1, 0, z1), new Vector2(x1, z1) / 25f);
        int i3 = md.AddVertex(new Vector3(x1, 0, z0), new Vector2(x1, z0) / 25f);
        md.AddTriangle(0, i0, i1, i2);
        md.AddTriangle(0, i0, i2, i3);

        return new BuiltMesh { name = "Ground", mesh = md.ToMesh("Ground"), materialKeys = new[] { "Ground" }, addCollider = true };
    }

    // =========================================================================
    //  ROADS
    // =========================================================================
    private static void BuildRoads(OsmData data, CityImportSettings settings, CityBuildResult result)
    {
        // One MeshData per road material, so all main roads become one mesh, etc.
        var byMaterial = new Dictionary<string, MeshData>();
        var lines = new MeshData(1);

        // Remember the widest road touching each node, to patch junctions later.
        var junctionWidth = new Dictionary<long, float>();
        var junctionStyle = new Dictionary<long, RoadStyle>();
        var nodeUseCount = new Dictionary<long, int>();

        foreach (OsmWay way in data.ways)
        {
            string type = way.Tag("highway");
            if (type == null || !RoadStyles.ContainsKey(type)) continue;

            RoadStyle style = RoadStyles[type];
            if (style.materialKey == "Footpath" && !settings.footpaths) continue;

            float width = RoadWidth(way, style);
            List<Vector3> points = CleanPoints(data.WayPoints(way, style.height));
            if (points.Count < 2) continue;

            if (!byMaterial.ContainsKey(style.materialKey)) byMaterial[style.materialKey] = new MeshData(1);
            AddRibbon(byMaterial[style.materialKey], points, width, 0f);
            result.roadCount++;

            // Dashed white centre line on bigger two-way roads.
            bool oneWay = IsOneWay(way) != 0;
            if (settings.centreLines && !oneWay && style.materialKey == "RoadMain")
                AddDashedLine(lines, points, 0.15f, 3f, 3f, 0.012f);

            // Junction bookkeeping (only for real roads, not footpaths).
            if (style.materialKey == "Footpath") continue;
            for (int i = 0; i < way.nodeIds.Count; i++)
            {
                long id = way.nodeIds[i];
                bool isEnd = i == 0 || i == way.nodeIds.Count - 1;
                nodeUseCount[id] = (nodeUseCount.ContainsKey(id) ? nodeUseCount[id] : 0) + (isEnd ? 1 : 2);
                if (!junctionWidth.ContainsKey(id) || width > junctionWidth[id])
                {
                    junctionWidth[id] = width;
                    junctionStyle[id] = style;
                }
            }
        }

        // Round patches where roads meet, like the round concrete at a chowk,
        // so there are no triangle-shaped gaps at the corners.
        foreach (var pair in nodeUseCount)
        {
            if (pair.Value < 3 || !data.nodes.ContainsKey(pair.Key)) continue; // not a junction
            RoadStyle style = junctionStyle[pair.Key];
            Vector3 centre = data.NodeToUnity(pair.Key, style.height);
            AddDisc(byMaterial[style.materialKey], centre, junctionWidth[pair.Key] / 2f, 16);
        }

        foreach (var pair in byMaterial)
            result.meshes.Add(new BuiltMesh { name = "Roads - " + pair.Key, mesh = pair.Value.ToMesh("Roads_" + pair.Key), materialKeys = new[] { pair.Key } });

        if (lines.VertexCount > 0)
            result.meshes.Add(new BuiltMesh { name = "Road centre lines", mesh = lines.ToMesh("RoadLines"), materialKeys = new[] { "RoadLine" } });
    }

    // Width from the map if it says so ("width=7" or "lanes=2"), otherwise from the table.
    private static float RoadWidth(OsmWay way, RoadStyle style)
    {
        float w;
        if (TryParseNumber(way.Tag("width"), out w) && w > 1f && w < 30f) return w;
        float lanes;
        if (TryParseNumber(way.Tag("lanes"), out lanes) && lanes >= 1f && lanes <= 6f) return Mathf.Max(style.width, lanes * 3.2f);
        return style.width;
    }

    // 1 = one-way in drawing direction, -1 = one-way backwards, 0 = two-way.
    private static int IsOneWay(OsmWay way)
    {
        string o = way.Tag("oneway");
        if (o == "yes" || o == "true" || o == "1") return 1;
        if (o == "-1" || o == "reverse") return -1;
        if (way.Tag("junction") == "roundabout") return 1;
        return 0;
    }

    // =========================================================================
    //  BUILDINGS
    // =========================================================================
    // Sub-mesh (material) numbers used inside every building tile mesh.
    private const int SubRoof = 0;                                  // concrete roof
    private const int SubWallFirst = 1;                             // 1..5  coloured walls with windows
    private const int SubShopFirst = SubWallFirst + WallColourCount; // 6..8  shop shutters + sign boards
    private const int ShopVariants = 3;
    private const int SubTank = SubShopFirst + ShopVariants;        // 9     black water tanks
    private const int BuildingSubMeshes = SubTank + 1;

    private static void BuildBuildings(OsmData data, CityImportSettings settings, CityBuildResult result)
    {
        // Buildings are grouped into square "tiles" (like city blocks on graph
        // paper). One mesh per tile keeps Unity fast: 70 big objects instead
        // of 5,000 small ones.
        var tiles = new Dictionary<string, MeshData>();

        // A quick way to ask "is this wall next to a road?" (for shop fronts).
        SegmentGrid streets = settings.shopFronts ? BuildStreetGrid(data) : null;

        foreach (OsmWay way in data.ways)
        {
            string b = way.Tag("building");
            if (b == null || b == "no" || !way.IsClosed) continue;

            List<Vector3> outline = CleanPoints(data.WayPoints(way));
            if (outline.Count > 1 && (outline[0] - outline[outline.Count - 1]).sqrMagnitude < 0.01f)
                outline.RemoveAt(outline.Count - 1);   // last point repeats the first one
            if (outline.Count < 3) continue;

            MakeClockwise(outline);
            float height = BuildingHeight(way, settings);

            // Which tile does this building belong to? Use its first corner.
            int tx = Mathf.FloorToInt(outline[0].x / settings.buildingTileSize);
            int tz = Mathf.FloorToInt(outline[0].z / settings.buildingTileSize);
            string key = tx + "_" + tz;
            if (!tiles.ContainsKey(key)) tiles[key] = new MeshData(BuildingSubMeshes);

            // Pick colour, shop sign and water tank from the building's id, so the
            // same house always looks the same (like a house number never changes).
            long id = System.Math.Abs(way.id);
            int wallSub = SubWallFirst + (int)(id % WallColourCount);
            int shopSub = SubShopFirst + (int)((id / 7) % ShopVariants);
            bool tank = settings.roofDetails && (id / 3) % 2 == 0;

            var look = new BuildingLook
            {
                height = height,
                floorHeight = settings.metresPerFloor,
                wallSubMesh = wallSub,
                shopSubMesh = shopSub,
                streets = streets,
                parapet = settings.roofDetails,
                waterTank = tank
            };
            if (AddBuilding(tiles[key], outline, look)) result.buildingCount++;
        }

        var keys = new string[BuildingSubMeshes];
        keys[SubRoof] = "Roof";
        for (int i = 0; i < WallColourCount; i++) keys[SubWallFirst + i] = "Wall" + i;
        for (int i = 0; i < ShopVariants; i++) keys[SubShopFirst + i] = "Shop" + i;
        keys[SubTank] = "Tank";

        foreach (var pair in tiles)
            result.meshes.Add(new BuiltMesh { name = "Buildings " + pair.Key, mesh = pair.Value.ToMesh("Buildings_" + pair.Key), materialKeys = keys, addCollider = true });
    }

    private static float BuildingHeight(OsmWay way, CityImportSettings settings)
    {
        float h;
        if (TryParseNumber(way.Tag("height"), out h) && h > 2f && h < 100f) return h;
        float levels;
        if (TryParseNumber(way.Tag("building:levels"), out levels) && levels >= 1f && levels < 30f) return levels * settings.metresPerFloor;

        // Unknown: pick 2-4 floors, but always the SAME number for the same
        // building (based on its id), so the city looks the same every import.
        int range = Mathf.Max(1, settings.maxFloors - settings.minFloors + 1);
        int floors = settings.minFloors + (int)System.Math.Abs((way.id * 7919) % range);
        return floors * settings.metresPerFloor;
    }

    // Everything AddBuilding needs to know about one house.
    private class BuildingLook
    {
        public float height, floorHeight;
        public int wallSubMesh, shopSubMesh;
        public SegmentGrid streets;   // null = no shops
        public bool parapet, waterTank;
    }

    // Texture layout (see the Textures folder):
    //   PokharaFacade.png = ONE floor, one window, 3 m wide
    //   PokharaShopN.png  = one shop shutter with a sign board, 3.5 m wide
    // So a wall 9 m wide and 3 floors tall shows the facade 3 times across
    // and 3 times up = 9 windows, like tiles on a bathroom wall.
    private const float WindowSpacing = 3f;
    private const float ShopWidth = 3.5f;
    private const float ParapetHeight = 0.9f;

    // Walls + roof + extras. Outline must be clockwise (seen from above).
    private static bool AddBuilding(MeshData md, List<Vector3> outline, BuildingLook look)
    {
        // Roof first: if the shape is too broken to triangulate, skip the building.
        List<int> roofTriangles = Triangulator.Triangulate(outline);
        if (roofTriangles.Count == 0) return false;

        float h = look.height;
        float floors = h / look.floorHeight;
        bool tallEnoughForShop = floors >= 1.5f;

        for (int i = 0; i < outline.Count; i++)
        {
            Vector3 a = outline[i];
            Vector3 b = outline[(i + 1) % outline.Count];
            float edge = Vector3.Distance(a, b);

            // How many windows fit on this wall? Round to a whole number so a
            // window never gets cut in half at the corner. Tiny walls (under
            // 1.5 m) get plain plaster (the left edge of the texture has no window).
            float windowsAcross = edge < 1.5f ? 0.1f : Mathf.Max(1f, Mathf.Round(edge / WindowSpacing));

            // Is this wall facing a street (within 8 m of the road edge)?
            // Then the ground floor is a shop, like along Lakeside.
            // For a clockwise outline, "outside" is to the LEFT of a -> b.
            Vector3 middle = (a + b) / 2f;
            Vector3 along = (b - a).normalized;
            Vector3 outward = new Vector3(-along.z, 0f, along.x);
            bool shop = look.streets != null && tallEnoughForShop && edge >= 2.5f && look.streets.IsFacingStreet(middle, outward, 8f);

            if (shop)
            {
                float shopsAcross = Mathf.Max(1f, Mathf.Round(edge / ShopWidth));
                AddWallQuad(md, look.shopSubMesh, a, b, 0f, look.floorHeight, 0f, shopsAcross, 0f, 1f);
                AddWallQuad(md, look.wallSubMesh, a, b, look.floorHeight, h, 0f, windowsAcross, 1f, floors);
            }
            else
            {
                AddWallQuad(md, look.wallSubMesh, a, b, 0f, h, 0f, windowsAcross, 0f, floors);
            }

        }

        // Low wall around the roof (a "parapet"), like on most Nepali concrete
        // houses. It is 20 cm thick: an outside face, an inside face and a top,
        // like a real brick wall. It uses the plain plaster strip at the top of
        // the facade texture (no windows there).
        if (look.parapet) AddParapet(md, outline, look.wallSubMesh, h);

        // Roof: the outline lifted to the top, filled with triangles.
        int first = md.VertexCount;
        foreach (Vector3 p in outline)
            md.AddVertex(p + Vector3.up * h, new Vector2(p.x, p.z) / 6f);
        for (int t = 0; t < roofTriangles.Count; t += 3)
            md.AddTriangle(SubRoof, first + roofTriangles[t], first + roofTriangles[t + 1], first + roofTriangles[t + 2]);

        // The black plastic water tank you see on almost every roof in Nepal.
        if (look.waterTank && Mathf.Abs(Triangulator.SignedArea(outline)) > 30f)
        {
            Vector3 centre = Vector3.zero;
            foreach (Vector3 p in outline) centre = centre + p;
            centre = centre / outline.Count;
            if (Triangulator.ContainsPoint(outline, centre))
                AddCylinder(md, SubTank, centre + Vector3.up * h, 0.65f, 1.3f, 10);
        }
        return true;
    }

    private static void AddParapet(MeshData md, List<Vector3> outline, int subMesh, float roofHeight)
    {
        const float thickness = 0.2f;
        List<Vector3> inner = InsetOutline(outline, thickness);
        float top = roofHeight + ParapetHeight;
        int n = outline.Count;

        for (int i = 0; i < n; i++)
        {
            int j = (i + 1) % n;
            Vector3 a = outline[i], b = outline[j];
            Vector3 ia = inner[i], ib = inner[j];
            float across = 0.1f;   // tiny: stay on the plain left edge of the texture

            AddWallQuad(md, subMesh, a, b, roofHeight, top, 0f, across, 0.80f, 0.94f);   // outside face
            AddWallQuad(md, subMesh, ib, ia, roofHeight, top, 0f, across, 0.80f, 0.94f); // inside face (looks at the roof)

            // Top of the wall: a thin strip between the outside and inside edges.
            int p0 = md.AddVertex(a + Vector3.up * top, new Vector2(0f, 0.85f));
            int p1 = md.AddVertex(b + Vector3.up * top, new Vector2(across, 0.85f));
            int p2 = md.AddVertex(ib + Vector3.up * top, new Vector2(across, 0.88f));
            int p3 = md.AddVertex(ia + Vector3.up * top, new Vector2(0f, 0.88f));
            md.AddTriangleFacingUp(subMesh, p0, p1, p2);
            md.AddTriangleFacingUp(subMesh, p0, p2, p3);
        }
    }

    // Shrink a clockwise outline inwards by 'distance' metres (inside = to the RIGHT).
    private static List<Vector3> InsetOutline(List<Vector3> outline, float distance)
    {
        var result = new List<Vector3>();
        int n = outline.Count;
        for (int i = 0; i < n; i++)
        {
            Vector3 prev = outline[(i + n - 1) % n], cur = outline[i], next = outline[(i + 1) % n];
            Vector3 r1 = RightOf(cur - prev), r2 = RightOf(next - cur);
            Vector3 right = (r1 + r2).normalized;
            if (right.sqrMagnitude < 0.5f) right = r2;
            float stretch = 1f / Mathf.Max(Vector3.Dot(right, r2), 0.4f);
            result.Add(cur + right * distance * stretch);
        }
        return result;
    }

    // One flat wall rectangle from corner a to corner b, between heights y0 and y1.
    // u0..u1 and v0..v1 say which part of the texture to stretch over it.
    private static void AddWallQuad(MeshData md, int subMesh, Vector3 a, Vector3 b, float y0, float y1,
                                    float u0, float u1, float v0, float v1)
    {
        int a0 = md.AddVertex(a + Vector3.up * y0, new Vector2(u0, v0));
        int b0 = md.AddVertex(b + Vector3.up * y0, new Vector2(u1, v0));
        int b1 = md.AddVertex(b + Vector3.up * y1, new Vector2(u1, v1));
        int a1 = md.AddVertex(a + Vector3.up * y1, new Vector2(u0, v1));
        md.AddTriangle(subMesh, a0, b0, b1);
        md.AddTriangle(subMesh, a0, b1, a1);
    }

    // A standing cylinder (water tank) with a flat lid.
    private static void AddCylinder(MeshData md, int subMesh, Vector3 bottomCentre, float radius, float height, int sides)
    {
        for (int i = 0; i < sides; i++)
        {
            // Going clockwise around the outline (seen from above) makes the walls face outward.
            float a1 = -i * Mathf.PI * 2f / sides, a2 = -(i + 1) * Mathf.PI * 2f / sides;
            Vector3 p1 = bottomCentre + new Vector3(Mathf.Cos(a1), 0f, Mathf.Sin(a1)) * radius;
            Vector3 p2 = bottomCentre + new Vector3(Mathf.Cos(a2), 0f, Mathf.Sin(a2)) * radius;
            AddWallQuad(md, subMesh, p1, p2, 0f, height, i / (float)sides, (i + 1) / (float)sides, 0f, 1f);
        }
        AddDiscFace(md, subMesh, bottomCentre + Vector3.up * height, radius, sides);
    }

    private static void AddDiscFace(MeshData md, int subMesh, Vector3 centre, float radius, int sides)
    {
        int c = md.AddVertex(centre, new Vector2(0.5f, 0.5f));
        int first = md.VertexCount;
        for (int i = 0; i < sides; i++)
        {
            float angle = -i * Mathf.PI * 2f / sides;
            md.AddVertex(centre + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * radius, new Vector2(0.5f, 0.5f));
        }
        for (int i = 0; i < sides; i++)
            md.AddTriangle(subMesh, c, first + i, first + (i + 1) % sides);
    }

    // All street centre lines (not footpaths), stored in a grid for fast "is it near?" checks.
    private static SegmentGrid BuildStreetGrid(OsmData data)
    {
        var grid = new SegmentGrid(40f);
        foreach (OsmWay way in data.ways)
        {
            string type = way.Tag("highway");
            if (type == null || !RoadStyles.ContainsKey(type) || !RoadStyles[type].drivable) continue;
            if (type == "service" || type == "track") continue;   // back lanes rarely have shops
            float halfWidth = RoadWidth(way, RoadStyles[type]) / 2f;
            List<Vector3> pts = data.WayPoints(way);
            for (int i = 0; i < pts.Count - 1; i++) grid.Add(pts[i], pts[i + 1], halfWidth);
        }
        return grid;
    }

    // =========================================================================
    //  WATER (Phewa Lake, ponds)
    // =========================================================================
    private static void BuildWater(OsmData data, CityBuildResult result)
    {
        var md = new MeshData(1);
        const float waterHeight = 0.015f;

        // 1) Big water bodies are "relations" made of several ways (like puzzle
        //    pieces). Join the pieces into one outline.
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
                // Pieces outside your export box are missing, so the outline may
                // not close. Closing it with a straight line works well when the
                // gap is far away (true for Phewa Lake in this export).
                var outline = new List<Vector3>();
                foreach (long id in ring)
                    if (data.nodes.ContainsKey(id)) outline.Add(data.NodeToUnity(id, waterHeight));
                if (AddFlatPolygon(md, outline)) result.waterCount++;
            }
        }

        // 2) Small ponds are single closed ways.
        foreach (OsmWay way in data.ways)
        {
            if (way.Tag("natural") != "water" || !way.IsClosed) continue;
            if (AddFlatPolygon(md, data.WayPoints(way, waterHeight))) result.waterCount++;
        }

        if (md.VertexCount > 0)
            result.meshes.Add(new BuiltMesh { name = "Water (Phewa Lake)", mesh = md.ToMesh("Water"), materialKeys = new[] { "Water" } });
    }

    // Glue way pieces end-to-end, like joining train tracks, until no more fit.
    private static List<List<long>> JoinPieces(List<List<long>> pieces)
    {
        var rings = new List<List<long>>();
        var remaining = new List<List<long>>(pieces);

        while (remaining.Count > 0)
        {
            List<long> current = remaining[0];
            remaining.RemoveAt(0);

            bool joined = true;
            while (joined && current[0] != current[current.Count - 1])
            {
                joined = false;
                for (int i = 0; i < remaining.Count; i++)
                {
                    List<long> p = remaining[i];
                    long head = current[0], tail = current[current.Count - 1];

                    if (p[0] == tail) { current.AddRange(p.GetRange(1, p.Count - 1)); }
                    else if (p[p.Count - 1] == tail) { p.Reverse(); current.AddRange(p.GetRange(1, p.Count - 1)); }
                    else if (p[p.Count - 1] == head) { current.InsertRange(0, p.GetRange(0, p.Count - 1)); }
                    else if (p[0] == head) { p.Reverse(); current.InsertRange(0, p.GetRange(0, p.Count - 1)); }
                    else continue;

                    remaining.RemoveAt(i);
                    joined = true;
                    break;
                }
            }
            rings.Add(current);
        }
        return rings;
    }

    // =========================================================================
    //  GREEN AREAS (parks, grass, forest)
    // =========================================================================
    private static readonly HashSet<string> GreenTags = new HashSet<string>
    {
        "leisure=park", "leisure=garden", "leisure=common", "landuse=grass", "landuse=recreation_ground",
        "landuse=forest", "landuse=meadow", "landuse=village_green", "natural=grassland",
        "natural=scrub", "natural=wood"
    };

    private static void BuildGreenAreas(OsmData data, CityBuildResult result)
    {
        var md = new MeshData(1);
        foreach (OsmWay way in data.ways)
        {
            if (!way.IsClosed) continue;
            bool green = false;
            foreach (var tag in way.tags)
                if (GreenTags.Contains(tag.Key + "=" + tag.Value)) { green = true; break; }
            if (!green) continue;
            if (AddFlatPolygon(md, data.WayPoints(way, 0.008f))) result.greenCount++;
        }
        if (md.VertexCount > 0)
            result.meshes.Add(new BuiltMesh { name = "Green areas", mesh = md.ToMesh("Green"), materialKeys = new[] { "Grass" } });
    }

    // =========================================================================
    //  TRAFFIC LANES (for the AI cars in Week 4)
    // =========================================================================
    private static List<Lane> BuildLanes(OsmData data)
    {
        // Step 1: which ways can cars drive on?
        var drivable = new List<OsmWay>();
        foreach (OsmWay way in data.ways)
        {
            string type = way.Tag("highway");
            if (type == null || !RoadStyles.ContainsKey(type) || !RoadStyles[type].drivable) continue;
            string service = way.Tag("service");
            if (service == "parking_aisle" || service == "driveway" || service == "drive-through") continue;
            if (way.Tag("access") == "no" || way.Tag("motor_vehicle") == "no") continue;
            drivable.Add(way);
        }

        // Step 2: count how many roads use each node. A node used more than
        // once is a junction (a chowk) — that is where lanes must be cut so
        // a car can choose where to go next.
        var useCount = new Dictionary<long, int>();
        foreach (OsmWay way in drivable)
            foreach (long id in way.nodeIds)
                useCount[id] = (useCount.ContainsKey(id) ? useCount[id] : 0) + 1;

        var lanes = new List<Lane>();
        foreach (OsmWay way in drivable)
        {
            RoadStyle style = RoadStyles[way.Tag("highway")];
            float width = RoadWidth(way, style);
            int oneWay = IsOneWay(way);
            string name = way.Tag("name") ?? "";

            // Step 3: cut the way into pieces at every junction.
            var piece = new List<long>();
            for (int i = 0; i < way.nodeIds.Count; i++)
            {
                long id = way.nodeIds[i];
                if (!data.nodes.ContainsKey(id)) { piece.Clear(); continue; } // outside the export box
                piece.Add(id);

                bool isLast = i == way.nodeIds.Count - 1;
                bool isJunction = useCount[id] > 1;
                if ((isJunction || isLast) && piece.Count >= 2)
                {
                    // Step 4: make the lane(s) for this piece.
                    var forward = new List<long>(piece);
                    var backward = new List<long>(piece); backward.Reverse();

                    // Two-way road: each direction drives a quarter of the road
                    // width to the LEFT of the middle (Nepal = left-hand traffic).
                    // One-way road: drive down the middle.
                    float offset = oneWay == 0 ? width / 4f : 0f;

                    if (oneWay >= 0) lanes.Add(MakeLane(data, forward, offset, name, way.Tag("highway"), style.speedKmh));
                    if (oneWay <= 0) lanes.Add(MakeLane(data, backward, offset, name, way.Tag("highway"), style.speedKmh));

                    piece = new List<long> { id }; // next piece starts at this junction
                }
            }
        }
        return lanes;
    }

    private static Lane MakeLane(OsmData data, List<long> nodeIds, float leftOffset, string name, string type, float speed)
    {
        var centre = new List<Vector3>();
        foreach (long id in nodeIds) centre.Add(data.NodeToUnity(id));

        var lane = new Lane
        {
            roadName = name,
            roadType = type,
            speedLimitKmh = speed,
            fromNode = nodeIds[0],
            toNode = nodeIds[nodeIds.Count - 1]
        };

        List<Vector3> shifted = OffsetLine(CleanPoints(centre), -leftOffset); // negative = to the left
        lane.points = Subdivide(shifted, 8f);  // a point at least every 8 m for smooth steering
        return lane;
    }

    // =========================================================================
    //  GEOMETRY HELPERS
    // =========================================================================

    // Remove points that sit on top of each other (they break the maths).
    private static List<Vector3> CleanPoints(List<Vector3> points)
    {
        var clean = new List<Vector3>();
        foreach (Vector3 p in points)
            if (clean.Count == 0 || (p - clean[clean.Count - 1]).sqrMagnitude > 0.0025f) clean.Add(p);
        return clean;
    }

    // The direction 90 degrees to the RIGHT of travel, flat on the ground.
    private static Vector3 RightOf(Vector3 forward)
    {
        Vector3 f = new Vector3(forward.x, 0f, forward.z).normalized;
        return new Vector3(f.z, 0f, -f.x);
    }

    // At each point, which way is "right", averaged between the road piece
    // before and after it, and how much to stretch at bends (the "miter")
    // so the road keeps the same width around corners.
    private static void SideAt(List<Vector3> pts, int i, out Vector3 right, out float stretch)
    {
        Vector3 before = i > 0 ? (pts[i] - pts[i - 1]) : (pts[i + 1] - pts[i]);
        Vector3 after = i < pts.Count - 1 ? (pts[i + 1] - pts[i]) : (pts[i] - pts[i - 1]);
        Vector3 r1 = RightOf(before), r2 = RightOf(after);
        right = (r1 + r2).normalized;
        if (right.sqrMagnitude < 0.5f) right = r2; // a 180-degree turn: just use one side

        float cos = Vector3.Dot(right, r2);
        stretch = 1f / Mathf.Max(cos, 0.4f);   // never stretch more than 2.5x (very sharp bends)
    }

    // A flat strip of the given width along a line (a road).
    private static void AddRibbon(MeshData md, List<Vector3> pts, float width, float lift)
    {
        if (pts.Count < 2) return;   // a strip needs at least a start and an end
        int start = md.VertexCount;
        float distance = 0f;
        for (int i = 0; i < pts.Count; i++)
        {
            Vector3 right; float stretch;
            SideAt(pts, i, out right, out stretch);
            if (i > 0) distance += Vector3.Distance(pts[i], pts[i - 1]);

            Vector3 half = right * (width / 2f) * stretch;
            Vector3 up = Vector3.up * lift;
            md.AddVertex(pts[i] - half + up, new Vector2(0f, distance / width)); // left edge
            md.AddVertex(pts[i] + half + up, new Vector2(1f, distance / width)); // right edge
        }
        for (int i = 0; i < pts.Count - 1; i++)
        {
            int l0 = start + i * 2, r0 = l0 + 1, l1 = l0 + 2, r1 = l0 + 3;
            md.AddTriangleFacingUp(0, l0, l1, r1);   // clockwise from above = faces up
            md.AddTriangleFacingUp(0, l0, r1, r0);
        }
    }

    // White road markings: 3 m painted, 3 m gap, repeated along the line.
    // We count dashes with a whole number (dash 0, 1, 2...) so the loop
    // always finishes, like counting fence posts instead of walking and
    // hoping you notice the end of the fence.
    private static void AddDashedLine(MeshData md, List<Vector3> pts, float width, float dash, float gap, float lift)
    {
        if (pts.Count < 2) return;

        // How far along the road each point is (0 m, 12.5 m, 30 m ...).
        var along = new List<float> { 0f };
        for (int i = 1; i < pts.Count; i++) along.Add(along[i - 1] + Vector3.Distance(pts[i - 1], pts[i]));
        float total = along[along.Count - 1];

        int dashCount = Mathf.FloorToInt(total / (dash + gap)) + 1;
        for (int d = 0; d < dashCount; d++)
        {
            float from = d * (dash + gap);
            float to = Mathf.Min(from + dash, total);
            if (to - from < 0.1f) continue;   // too short to see

            // The dash: its start point, any bends inside it, its end point.
            var piece = new List<Vector3> { PointAlong(pts, along, from) };
            for (int i = 0; i < pts.Count; i++)
                if (along[i] > from && along[i] < to) piece.Add(pts[i]);
            piece.Add(PointAlong(pts, along, to));

            AddRibbon(md, CleanPoints(piece), width, lift);
        }
    }

    // The position a given number of metres along a line.
    private static Vector3 PointAlong(List<Vector3> pts, List<float> along, float distance)
    {
        for (int i = 1; i < pts.Count; i++)
        {
            if (along[i] >= distance)
            {
                float segment = along[i] - along[i - 1];
                float t = segment > 0.0001f ? (distance - along[i - 1]) / segment : 0f;
                return Vector3.Lerp(pts[i - 1], pts[i], t);
            }
        }
        return pts[pts.Count - 1];
    }

    // A round patch (approximated with 'sides' triangles), facing up.
    private static void AddDisc(MeshData md, Vector3 centre, float radius, int sides)
    {
        int c = md.AddVertex(centre, new Vector2(0.5f, 0.5f));
        int first = md.VertexCount;
        for (int i = 0; i < sides; i++)
        {
            float angle = -i * Mathf.PI * 2f / sides;   // going clockwise
            md.AddVertex(centre + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * radius, new Vector2(0.5f, 0.5f));
        }
        for (int i = 0; i < sides; i++)
            md.AddTriangle(0, c, first + i, first + (i + 1) % sides);
    }

    // Any flat outline (lake, park) filled with triangles, facing up.
    private static bool AddFlatPolygon(MeshData md, List<Vector3> outline)
    {
        outline = CleanPoints(outline);
        if (outline.Count > 1 && (outline[0] - outline[outline.Count - 1]).sqrMagnitude < 0.01f)
            outline.RemoveAt(outline.Count - 1);
        if (outline.Count < 3) return false;

        MakeClockwise(outline);
        List<int> tris = Triangulator.Triangulate(outline);
        if (tris.Count == 0) return false;

        int first = md.VertexCount;
        foreach (Vector3 p in outline) md.AddVertex(p, new Vector2(p.x, p.z) / 10f);
        for (int t = 0; t < tris.Count; t += 3)
            md.AddTriangle(0, first + tris[t], first + tris[t + 1], first + tris[t + 2]);
        return true;
    }

    // Move a whole line sideways (positive = right, negative = left).
    private static List<Vector3> OffsetLine(List<Vector3> pts, float amount)
    {
        if (pts.Count < 2 || Mathf.Abs(amount) < 0.001f) return new List<Vector3>(pts);
        var result = new List<Vector3>();
        for (int i = 0; i < pts.Count; i++)
        {
            Vector3 right; float stretch;
            SideAt(pts, i, out right, out stretch);
            result.Add(pts[i] + right * amount * stretch);
        }
        return result;
    }

    // Add extra points so no two points are more than maxGap metres apart.
    private static List<Vector3> Subdivide(List<Vector3> pts, float maxGap)
    {
        var result = new List<Vector3>();
        for (int i = 0; i < pts.Count; i++)
        {
            result.Add(pts[i]);
            if (i == pts.Count - 1) break;
            float d = Vector3.Distance(pts[i], pts[i + 1]);
            int extra = Mathf.FloorToInt(d / maxGap);
            for (int k = 1; k <= extra; k++)
            {
                float t = k / (float)(extra + 1);
                result.Add(Vector3.Lerp(pts[i], pts[i + 1], t));
            }
        }
        return result;
    }

    // Shoelace formula: positive area = anticlockwise (seen from above).
    private static void MakeClockwise(List<Vector3> outline)
    {
        if (Triangulator.SignedArea(outline) > 0f) outline.Reverse();
    }

    // Reads numbers like "7", "7.5", "12 m" or "3;4" (takes the first number).
    private static bool TryParseNumber(string text, out float value)
    {
        value = 0f;
        if (string.IsNullOrEmpty(text)) return false;
        int end = 0;
        while (end < text.Length && (char.IsDigit(text[end]) || text[end] == '.')) end++;
        return end > 0 && float.TryParse(text.Substring(0, end), NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }
}

// =============================================================================
//  MeshData — a shopping list for a mesh: we keep adding corners (vertices)
//  and triangles, and at the end turn the list into a real Unity Mesh.
// =============================================================================
public class MeshData
{
    private readonly List<Vector3> vertices = new List<Vector3>();
    private readonly List<Vector2> uvs = new List<Vector2>();
    private readonly List<int>[] triangles;   // one list per sub-mesh (material)

    public MeshData(int subMeshCount)
    {
        triangles = new List<int>[subMeshCount];
        for (int i = 0; i < subMeshCount; i++) triangles[i] = new List<int>();
    }

    public int VertexCount => vertices.Count;

    public int AddVertex(Vector3 position, Vector2 uv)
    {
        // Safety net: a normal Pokhara map needs well under 1 million corners
        // per mesh. If we ever go past 5 million, something is looping —
        // stop with a clear message instead of eating all the memory.
        if (vertices.Count > 5000000)
            throw new System.Exception("Mesh too big (over 5 million corners). Try a smaller map area or untick some options.");
        vertices.Add(position);
        uvs.Add(uv);
        return vertices.Count - 1;
    }

    // Unity shows the side of a triangle whose corners go CLOCKWISE when you look at it.
    public void AddTriangle(int subMesh, int a, int b, int c)
    {
        triangles[subMesh].Add(a);
        triangles[subMesh].Add(b);
        triangles[subMesh].Add(c);
    }

    // Same, but if a very sharp bend folded the triangle upside down,
    // flip it so it is still visible from above.
    public void AddTriangleFacingUp(int subMesh, int a, int b, int c)
    {
        Vector3 u = vertices[b] - vertices[a], v = vertices[c] - vertices[a];
        float upness = u.z * v.x - u.x * v.z;   // the "y" part of the triangle's normal
        if (upness < 0f) AddTriangle(subMesh, a, c, b);
        else AddTriangle(subMesh, a, b, c);
    }

    public Mesh ToMesh(string name)
    {
        var mesh = new Mesh { name = name };
        mesh.indexFormat = IndexFormat.UInt32;   // allows more than 65,000 corners per mesh
        mesh.SetVertices(vertices);
        mesh.SetUVs(0, uvs);
        mesh.subMeshCount = triangles.Length;
        for (int i = 0; i < triangles.Length; i++) mesh.SetTriangles(triangles[i], i);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }
}

// =============================================================================
//  Triangulator — fills any flat outline with triangles using "ear clipping".
//  Analogy: cutting a paper shape into triangles by repeatedly snipping off
//  a corner (an "ear") that has no other corner inside it.
// =============================================================================
public static class Triangulator
{
    public static float SignedArea(List<Vector3> pts)
    {
        float area = 0f;
        for (int i = 0; i < pts.Count; i++)
        {
            Vector3 a = pts[i], b = pts[(i + 1) % pts.Count];
            area += a.x * b.z - b.x * a.z;
        }
        return area / 2f;
    }

    // Input must be CLOCKWISE (seen from above). Returns index triples, also clockwise.
    public static List<int> Triangulate(List<Vector3> pts)
    {
        var result = new List<int>();
        int n = pts.Count;
        if (n < 3) return result;

        var remaining = new List<int>();
        for (int i = 0; i < n; i++) remaining.Add(i);

        int guard = 0, limit = n * n + 10;  // stops forever-loops on broken shapes
        int index = 0;
        while (remaining.Count > 3 && guard++ < limit)
        {
            int count = remaining.Count;
            int prev = remaining[(index + count - 1) % count];
            int cur = remaining[index % count];
            int next = remaining[(index + 1) % count];

            if (IsEar(pts, remaining, prev, cur, next))
            {
                result.Add(prev); result.Add(cur); result.Add(next);
                remaining.RemoveAt(index % count);
                guard = 0;
            }
            else index++;

            if (index >= remaining.Count) index = 0;
        }

        if (remaining.Count == 3)
        {
            result.Add(remaining[0]); result.Add(remaining[1]); result.Add(remaining[2]);
        }
        else if (remaining.Count > 3)
        {
            return new List<int>(); // shape too broken: give up rather than draw garbage
        }
        return result;
    }

    // Is point p inside the outline? Count how many edges a line going east
    // from p crosses: odd = inside, even = outside (like counting fences).
    public static bool ContainsPoint(List<Vector3> pts, Vector3 p)
    {
        bool inside = false;
        for (int i = 0, j = pts.Count - 1; i < pts.Count; j = i++)
        {
            Vector3 a = pts[i], b = pts[j];
            if ((a.z > p.z) != (b.z > p.z) && p.x < (b.x - a.x) * (p.z - a.z) / (b.z - a.z) + a.x) inside = !inside;
        }
        return inside;
    }

    private static float Cross(Vector3 a, Vector3 b, Vector3 c)
    {
        return (b.x - a.x) * (c.z - b.z) - (b.z - a.z) * (c.x - b.x);
    }

    private static bool IsEar(List<Vector3> pts, List<int> remaining, int prev, int cur, int next)
    {
        Vector3 a = pts[prev], b = pts[cur], c = pts[next];
        if (Cross(a, b, c) >= -1e-6f) return false; // not a convex (clockwise) corner

        foreach (int i in remaining)
        {
            if (i == prev || i == cur || i == next) continue;
            if (InsideTriangle(pts[i], a, b, c)) return false;
        }
        return true;
    }

    private static bool InsideTriangle(Vector3 p, Vector3 a, Vector3 b, Vector3 c)
    {
        // For a clockwise triangle, a point inside is on the right of all 3 edges.
        return Cross(a, b, p) <= 0f && Cross(b, c, p) <= 0f && Cross(c, a, p) <= 0f;
    }
}

// =============================================================================
//  SegmentGrid — remembers road pieces in square "buckets" so we only check
//  the few roads close to a building, not all 465 (like a phone book sorted
//  by area instead of one long list).
// =============================================================================
public class SegmentGrid
{
    private struct Segment { public Vector3 a, b; public float halfWidth; }

    private readonly float cellSize;
    private readonly Dictionary<long, List<Segment>> cells = new Dictionary<long, List<Segment>>();

    public SegmentGrid(float cellSize) { this.cellSize = cellSize; }

    private long Key(int x, int z) { return ((long)x << 32) ^ (uint)z; }

    public void Add(Vector3 a, Vector3 b, float halfWidth)
    {
        var s = new Segment { a = a, b = b, halfWidth = halfWidth };
        int x0 = Mathf.FloorToInt(Mathf.Min(a.x, b.x) / cellSize), x1 = Mathf.FloorToInt(Mathf.Max(a.x, b.x) / cellSize);
        int z0 = Mathf.FloorToInt(Mathf.Min(a.z, b.z) / cellSize), z1 = Mathf.FloorToInt(Mathf.Max(a.z, b.z) / cellSize);
        for (int x = x0; x <= x1; x++)
            for (int z = z0; z <= z1; z++)
            {
                long k = Key(x, z);
                if (!cells.ContainsKey(k)) cells[k] = new List<Segment>();
                cells[k].Add(s);
            }
    }

    // True if a wall at p, whose outside faces 'outward', looks onto a street
    // that is at most 'extra' metres beyond the road's edge.
    public bool IsFacingStreet(Vector3 p, Vector3 outward, float extra)
    {
        int cx = Mathf.FloorToInt(p.x / cellSize), cz = Mathf.FloorToInt(p.z / cellSize);
        for (int x = cx - 1; x <= cx + 1; x++)
            for (int z = cz - 1; z <= cz + 1; z++)
            {
                List<Segment> list;
                if (!cells.TryGetValue(Key(x, z), out list)) continue;
                foreach (Segment s in list)
                {
                    Vector3 closest = ClosestPoint(p, s.a, s.b);
                    Vector3 toRoad = closest - p;
                    float distance = toRoad.magnitude;
                    if (distance > s.halfWidth + extra || distance < 0.01f) continue;
                    // Facing the road = the wall's outside points towards it.
                    if (Vector3.Dot(toRoad / distance, outward) > 0.5f) return true;
                }
            }
        return false;
    }

    private static Vector3 ClosestPoint(Vector3 p, Vector3 a, Vector3 b)
    {
        Vector3 ab = b - a; ab.y = 0f;
        Vector3 ap = p - a; ap.y = 0f;
        float lengthSq = ab.sqrMagnitude;
        float t = lengthSq < 1e-6f ? 0f : Mathf.Clamp01(Vector3.Dot(ap, ab) / lengthSq);
        Vector3 closest = a + ab * t; closest.y = p.y;
        return closest;
    }

    // True if p is within (road half-width + extra) metres of any road centre line.
    public bool IsNearStreet(Vector3 p, float extra)
    {
        int cx = Mathf.FloorToInt(p.x / cellSize), cz = Mathf.FloorToInt(p.z / cellSize);
        for (int x = cx - 1; x <= cx + 1; x++)
            for (int z = cz - 1; z <= cz + 1; z++)
            {
                List<Segment> list;
                if (!cells.TryGetValue(Key(x, z), out list)) continue;
                foreach (Segment s in list)
                {
                    float limit = s.halfWidth + extra;
                    if (DistanceToSegment(p, s.a, s.b) < limit) return true;
                }
            }
        return false;
    }

    private static float DistanceToSegment(Vector3 p, Vector3 a, Vector3 b)
    {
        Vector3 ab = b - a; ab.y = 0f;
        Vector3 ap = p - a; ap.y = 0f;
        float lengthSq = ab.sqrMagnitude;
        float t = lengthSq < 1e-6f ? 0f : Mathf.Clamp01(Vector3.Dot(ap, ab) / lengthSq);
        Vector3 closest = a + ab * t; closest.y = p.y;
        return Vector3.Distance(p, closest);
    }
}
