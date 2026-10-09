// =============================================================================
//  Minimap.cs  —  a little map in the bottom-right corner
// =============================================================================
//  How it works: a second camera floats high above your car, looking straight
//  down (like a drone), and draws into a small picture (a "RenderTexture").
//  The picture is shown in the corner of the screen and turns with your car,
//  so "up" on the map is always the way you are driving, like Google Maps
//  in navigation mode.
//
//  During a mission it also shows a thick yellow route line and a red
//  marker for the next gate. Those are on layer 31, which ONLY the minimap
//  camera can see, so they never appear in the normal view.
//
//  Keys:  - and =   zoom out / in
// =============================================================================

using UnityEngine;
using UnityEngine.Rendering.Universal;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

public class Minimap : MonoBehaviour
{
    public float sizeOnScreen = 260f;    // pixels on a 1080p screen
    public float zoomMetres = 140f;      // how many metres from the middle to the edge
    public float minZoom = 60f, maxZoom = 400f;
    public float cameraHeight = 300f;

    private const int OverlayLayer = 31;   // a layer only the minimap camera draws

    private Camera mapCam;
    private RenderTexture picture;
    private Transform target;
    private MissionManager missions;
    private LineRenderer routeOverlay;
    private GameObject gateMarker;
    private PlannedRoute shownRoute;
    private Texture2D arrow, frame, ring;
    private GUIStyle northStyle;

    private void Start()
    {
        PokharaCar car = FindFirstObjectByType<PokharaCar>();
        if (car == null) { enabled = false; return; }
        target = car.transform;
        missions = FindFirstObjectByType<MissionManager>();

        // The drone camera.
        picture = new RenderTexture(512, 512, 16) { name = "Minimap" };
        var camObject = new GameObject("Minimap camera");
        camObject.transform.SetParent(transform, false);
        mapCam = camObject.AddComponent<Camera>();
        mapCam.orthographic = true;              // flat, like a paper map (no perspective)
        mapCam.orthographicSize = zoomMetres;
        mapCam.clearFlags = CameraClearFlags.SolidColor;
        mapCam.backgroundColor = new Color(0.11f, 0.13f, 0.19f);
        mapCam.nearClipPlane = 1f;
        mapCam.farClipPlane = cameraHeight + 50f;
        mapCam.targetTexture = picture;
        mapCam.depth = -10;                      // draw before the main camera

        // No shadows or post-processing on the minimap: it is tiny, so save the Mac's effort.
        UniversalAdditionalCameraData extra = mapCam.GetUniversalAdditionalCameraData();
        if (extra != null) { extra.renderShadows = false; extra.renderPostProcessing = false; }

        // The main camera must NOT see the minimap-only things.
        if (Camera.main != null) Camera.main.cullingMask &= ~(1 << OverlayLayer);

        // Route line + next-gate marker for the minimap.
        var lineObject = new GameObject("Minimap route");
        lineObject.layer = OverlayLayer;
        lineObject.transform.SetParent(transform, false);
        lineObject.transform.rotation = Quaternion.Euler(-90f, 0f, 0f);   // lie flat (facing up)
        routeOverlay = lineObject.AddComponent<LineRenderer>();
        routeOverlay.useWorldSpace = true;
        routeOverlay.alignment = LineAlignment.TransformZ;
        routeOverlay.widthMultiplier = 7f;
        routeOverlay.numCornerVertices = 3;
        routeOverlay.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        routeOverlay.enabled = false;

        gateMarker = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        gateMarker.name = "Minimap next gate";
        gateMarker.layer = OverlayLayer;
        Destroy(gateMarker.GetComponent<Collider>());
        gateMarker.transform.SetParent(transform, false);
        gateMarker.transform.localScale = new Vector3(16f, 0.5f, 16f);
        gateMarker.SetActive(false);

        if (missions != null && missions.routeLineMaterial != null)
        {
            routeOverlay.sharedMaterial = missions.routeLineMaterial;
            var red = new Material(missions.routeLineMaterial);
            red.color = new Color(1f, 0.25f, 0.2f);
            if (red.HasProperty("_BaseColor")) red.SetColor("_BaseColor", red.color);
            gateMarker.GetComponent<Renderer>().sharedMaterial = red;
        }

        arrow = MakeArrow(64);
        frame = MakeCircle(256, true);
        ring = MakeCircle(256, false);
    }

    private void OnDestroy()
    {
        if (picture != null) picture.Release();
    }

    private void LateUpdate()
    {
        if (mapCam == null) return;
        bool show = GameSettings.minimap && !GameMenu.IsOpen;
        mapCam.enabled = show;
        if (!show) return;

        // Zoom with - and =
        if (KeyMinus()) zoomMetres = Mathf.Min(maxZoom, zoomMetres * 1.25f);
        if (KeyEquals()) zoomMetres = Mathf.Max(minZoom, zoomMetres / 1.25f);
        mapCam.orthographicSize = zoomMetres;

        // Hover above the car, turned the same way as the car.
        Vector3 p = target.position;
        mapCam.transform.SetPositionAndRotation(new Vector3(p.x, p.y + cameraHeight, p.z),
                                                Quaternion.Euler(90f, target.eulerAngles.y, 0f));

        UpdateMissionOverlay();
    }

    private void UpdateMissionOverlay()
    {
        PlannedRoute route = missions != null ? missions.ActiveRoute : null;
        if (route != shownRoute)
        {
            shownRoute = route;
            routeOverlay.enabled = route != null;
            if (route != null)
            {
                routeOverlay.positionCount = route.points.Count;
                for (int i = 0; i < route.points.Count; i++) routeOverlay.SetPosition(i, route.points[i] + Vector3.up * 2f);
            }
        }

        Vector3 gate = Vector3.zero;
        bool hasGate = missions != null && missions.TryGetNextGate(out gate);
        gateMarker.SetActive(hasGate);
        if (hasGate)
        {
            gateMarker.transform.position = gate + Vector3.up * 3f;
            // Keep the marker the same size on screen whatever the zoom.
            float d = zoomMetres / 140f * 16f;
            gateMarker.transform.localScale = new Vector3(d, 0.5f, d);
        }
    }

    // ---- drawing on the screen ----------------------------------------------------
    private void OnGUI()
    {
        if (mapCam == null || !mapCam.enabled || GameMenu.IsOpen) return;
        if (northStyle == null)
        {
            northStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
            northStyle.normal.textColor = new Color(1f, 0.35f, 0.3f);
        }
        float s = Screen.height / 1080f;
        float size = sizeOnScreen * s;
        Rect r = new Rect(Screen.width - size - 24 * s, Screen.height - size - 24 * s, size, size);

        // Map picture, cut into a circle by drawing a frame ring over the corners.
        GUI.DrawTexture(r, picture, ScaleMode.StretchToFill, false);
        GUI.DrawTexture(r, frame);   // fills the 4 corners with the panel colour
        GUI.DrawTexture(r, ring);    // the thin border circle

        // You (always in the middle, always pointing up).
        float a = 30 * s;
        GUI.DrawTexture(new Rect(r.center.x - a / 2f, r.center.y - a / 2f, a, a), arrow);

        // "N" goes round the edge so you always know where north is.
        float heading = target.eulerAngles.y * Mathf.Deg2Rad;
        float rad = size / 2f - 16 * s;
        Vector2 n = r.center + new Vector2(-Mathf.Sin(heading), -Mathf.Cos(heading)) * rad;
        northStyle.fontSize = Mathf.RoundToInt(18 * s);
        GUI.Label(new Rect(n.x - 14 * s, n.y - 14 * s, 28 * s, 28 * s), "N", northStyle);
    }

    // ---- small pictures made in code ---------------------------------------------------
    // A yellow arrow pointing up.
    private static Texture2D MakeArrow(int size)
    {
        var t = new Texture2D(size, size, TextureFormat.RGBA32, false);
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float u = (x + 0.5f) / size - 0.5f, v = (y + 0.5f) / size;   // v = 0 bottom, 1 top
                // Inside the arrow: a triangle with a notch at the bottom.
                bool inside = v > 0.12f && v < 0.95f && Mathf.Abs(u) < (0.95f - v) * 0.55f &&
                              !(v < 0.35f && Mathf.Abs(u) < (0.35f - v) * 1.3f);
                bool edge = !inside && v > 0.06f && v < 0.99f && Mathf.Abs(u) < (1.0f - v) * 0.6f;
                t.SetPixel(x, y, inside ? new Color(1f, 0.82f, 0.2f) : edge ? new Color(0.05f, 0.05f, 0.08f) : new Color(0, 0, 0, 0));
            }
        t.Apply();
        return t;
    }

    // filledOutside = colour everything outside the circle (corners), else just a ring.
    private static Texture2D MakeCircle(int size, bool filledOutside)
    {
        var t = new Texture2D(size, size, TextureFormat.RGBA32, false);
        float c = (size - 1) / 2f;
        Color bg = new Color(0.07f, 0.09f, 0.14f, 1f);   // dark corners: a round map on a dark tile
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float d = Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c)) / c;
                Color col = new Color(0, 0, 0, 0);
                if (filledOutside && d > 1f) col = bg;
                if (!filledOutside && d > 0.955f && d <= 1f) col = new Color(1f, 1f, 1f, 0.85f);
                t.SetPixel(x, y, col);
            }
        t.Apply();
        return t;
    }

    private static bool KeyMinus()
    {
#if ENABLE_INPUT_SYSTEM
        return Keyboard.current != null && Keyboard.current.minusKey.wasPressedThisFrame;
#else
        return Input.GetKeyDown(KeyCode.Minus);
#endif
    }

    private static bool KeyEquals()
    {
#if ENABLE_INPUT_SYSTEM
        return Keyboard.current != null && Keyboard.current.equalsKey.wasPressedThisFrame;
#else
        return Input.GetKeyDown(KeyCode.Equals);
#endif
    }
}
