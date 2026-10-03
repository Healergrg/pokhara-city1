// =============================================================================
//  OsmData.cs  —  reads the map.osm file you downloaded from openstreetmap.org
// =============================================================================
//  A .osm file is a big list written in XML with three kinds of things:
//
//    <node>      a single GPS point (latitude + longitude)
//                -> like one pin stuck in a paper map
//    <way>       a list of nodes joined together, plus "tags" that say what it is
//                -> a road (highway=primary), a building (building=yes), a park...
//    <relation>  a group of ways that make one bigger thing
//                -> Phewa Lake is made of several ways glued together
//
//  This file only READS the data. Turning it into 3D shapes happens in
//  CityMeshBuilder.cs, and the Unity window is PokharaCityImporterWindow.cs.
// =============================================================================

using System.Collections.Generic;
using System.Globalization;
using System.Xml;
using UnityEngine;

public class OsmNode
{
    public long id;
    public double lat;
    public double lon;
    public Dictionary<string, string> tags;   // usually empty; set for things like natural=tree

    public string Tag(string key)
    {
        string value;
        return tags != null && tags.TryGetValue(key, out value) ? value : null;
    }
}

public class OsmWay
{
    public long id;
    public List<long> nodeIds = new List<long>();
    public Dictionary<string, string> tags = new Dictionary<string, string>();

    // Small helper so we can write way.Tag("highway") instead of a TryGetValue every time.
    public string Tag(string key)
    {
        string value;
        return tags.TryGetValue(key, out value) ? value : null;
    }

    // A "closed" way starts and ends at the same node (buildings, parks, ponds).
    public bool IsClosed => nodeIds.Count > 3 && nodeIds[0] == nodeIds[nodeIds.Count - 1];
}

public class OsmMember
{
    public string type;   // "way", "node" or "relation"
    public long refId;    // id of the way/node it points to
    public string role;   // "outer" (the edge of the lake) or "inner" (an island)
}

public class OsmRelation
{
    public long id;
    public List<OsmMember> members = new List<OsmMember>();
    public Dictionary<string, string> tags = new Dictionary<string, string>();

    public string Tag(string key)
    {
        string value;
        return tags.TryGetValue(key, out value) ? value : null;
    }
}

public class OsmData
{
    public Dictionary<long, OsmNode> nodes = new Dictionary<long, OsmNode>();
    public List<OsmWay> ways = new List<OsmWay>();
    public Dictionary<long, OsmWay> waysById = new Dictionary<long, OsmWay>();
    public List<OsmRelation> relations = new List<OsmRelation>();

    // The box you exported. Its centre becomes (0,0,0) in Unity.
    public double minLat, minLon, maxLat, maxLon;
    public double originLat, originLon;

    // -------------------------------------------------------------------------
    // Read the whole file. XmlReader walks through it line by line,
    // like reading a book page by page instead of photocopying it all first.
    // -------------------------------------------------------------------------
    public static OsmData Load(string filePath)
    {
        var data = new OsmData();
        OsmWay currentWay = null;
        OsmNode currentNode = null;   // a node that has tags inside it (e.g. a single tree)
        OsmRelation currentRelation = null;
        bool haveBounds = false;

        using (XmlReader reader = XmlReader.Create(filePath))
        {
            while (reader.Read())
            {
                if (reader.NodeType == XmlNodeType.EndElement)
                {
                    if (reader.Name == "way") currentWay = null;
                    if (reader.Name == "node") currentNode = null;
                    if (reader.Name == "relation") currentRelation = null;
                    continue;
                }
                if (reader.NodeType != XmlNodeType.Element) continue;

                switch (reader.Name)
                {
                    case "bounds":
                        data.minLat = ReadDouble(reader, "minlat");
                        data.minLon = ReadDouble(reader, "minlon");
                        data.maxLat = ReadDouble(reader, "maxlat");
                        data.maxLon = ReadDouble(reader, "maxlon");
                        haveBounds = true;
                        break;

                    case "node":
                        var node = new OsmNode
                        {
                            id = ReadLong(reader, "id"),
                            lat = ReadDouble(reader, "lat"),
                            lon = ReadDouble(reader, "lon")
                        };
                        data.nodes[node.id] = node;
                        currentNode = reader.IsEmptyElement ? null : node;   // <node ... /> has no tags
                        break;

                    case "way":
                        currentWay = new OsmWay { id = ReadLong(reader, "id") };
                        data.ways.Add(currentWay);
                        data.waysById[currentWay.id] = currentWay;
                        if (reader.IsEmptyElement) currentWay = null;
                        break;

                    case "nd": // a node reference inside a way
                        if (currentWay != null) currentWay.nodeIds.Add(ReadLong(reader, "ref"));
                        break;

                    case "relation":
                        currentRelation = new OsmRelation { id = ReadLong(reader, "id") };
                        data.relations.Add(currentRelation);
                        if (reader.IsEmptyElement) currentRelation = null;
                        break;

                    case "member":
                        if (currentRelation != null)
                        {
                            currentRelation.members.Add(new OsmMember
                            {
                                type = reader.GetAttribute("type"),
                                refId = ReadLong(reader, "ref"),
                                role = reader.GetAttribute("role") ?? ""
                            });
                        }
                        break;

                    case "tag": // key = value, e.g. highway = primary
                        string k = reader.GetAttribute("k");
                        string v = reader.GetAttribute("v");
                        if (currentNode != null)
                        {
                            if (currentNode.tags == null) currentNode.tags = new Dictionary<string, string>();
                            currentNode.tags[k] = v;
                        }
                        else if (currentWay != null) currentWay.tags[k] = v;
                        else if (currentRelation != null) currentRelation.tags[k] = v;
                        break;
                }
            }
        }

        // If the file has no <bounds>, use the spread of all nodes instead.
        if (!haveBounds && data.nodes.Count > 0)
        {
            data.minLat = data.minLon = double.MaxValue;
            data.maxLat = data.maxLon = double.MinValue;
            foreach (OsmNode n in data.nodes.Values)
            {
                if (n.lat < data.minLat) data.minLat = n.lat;
                if (n.lat > data.maxLat) data.maxLat = n.lat;
                if (n.lon < data.minLon) data.minLon = n.lon;
                if (n.lon > data.maxLon) data.maxLon = n.lon;
            }
        }

        data.originLat = (data.minLat + data.maxLat) / 2.0;
        data.originLon = (data.minLon + data.maxLon) / 2.0;
        return data;
    }

    // -------------------------------------------------------------------------
    // GPS -> Unity metres.
    // Over a 2 km area the Earth is flat enough that we can treat it like
    // graph paper: 1 degree north is about 110.5 km, 1 degree east is
    // 111.3 km times cos(latitude) (the lines get closer near the poles).
    // x = east, z = north, y = up.
    // -------------------------------------------------------------------------
    public Vector3 ToUnity(double lat, double lon, float height = 0f)
    {
        const double metresPerDegreeLat = 110540.0;
        const double metresPerDegreeLon = 111320.0;
        double cosLat = System.Math.Cos(originLat * System.Math.PI / 180.0);

        float x = (float)((lon - originLon) * cosLat * metresPerDegreeLon);
        float z = (float)((lat - originLat) * metresPerDegreeLat);
        return new Vector3(x, height, z);
    }

    public Vector3 NodeToUnity(long nodeId, float height = 0f)
    {
        OsmNode n = nodes[nodeId];
        return ToUnity(n.lat, n.lon, height);
    }

    // The positions of all nodes of a way that actually exist in the file.
    public List<Vector3> WayPoints(OsmWay way, float height = 0f)
    {
        var points = new List<Vector3>();
        foreach (long id in way.nodeIds)
        {
            if (nodes.ContainsKey(id)) points.Add(NodeToUnity(id, height));
        }
        return points;
    }

    // Numbers in the file always use a dot (28.21), so read them that way
    // no matter what language your computer is set to.
    private static double ReadDouble(XmlReader reader, string attribute)
    {
        return double.Parse(reader.GetAttribute(attribute), CultureInfo.InvariantCulture);
    }

    private static long ReadLong(XmlReader reader, string attribute)
    {
        return long.Parse(reader.GetAttribute(attribute), CultureInfo.InvariantCulture);
    }
}
