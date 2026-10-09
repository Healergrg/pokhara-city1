// =============================================================================
//  CarDashboard.cs  —  the speedometer (bottom-left of the screen)
// =============================================================================
//  A round dial like a real car:
//    - a needle that points at your speed (0-100 km/h)
//    - a small red triangle at the SPEED LIMIT of the road you are on
//    - the big number turns red when you are faster than the limit
//    - gear in the middle: D1-D5, N or R
//    - a rev bar (engine speed) under the dial
//    - green indicator arrows that blink, and a red (P) for the handbrake
//
//  All the pictures (dial, needle, arrows) are drawn by code when the game
//  starts, so there are no image files to import.
// =============================================================================

using UnityEngine;

[RequireComponent(typeof(PokharaCar))]
public class CarDashboard : MonoBehaviour
{
    public bool showControlsHelp = true;
    public float dialMaxKmh = 100f;

    private PokharaCar car;
    private RoadRulesJudge judge;
    private Texture2D dial, needle, marker, white;
    private GUIStyle speedStyle, unitStyle, gearStyle, helpStyle, numberStyle, arrowStyle, parkStyle;

    // The dial goes from -135 degrees (0 km/h, bottom-left) to +135 degrees (max, bottom-right).
    private const float StartAngle = -135f, Sweep = 270f;

    private void Awake()
    {
        car = GetComponent<PokharaCar>();
    }

    private void Start()
    {
        judge = FindFirstObjectByType<RoadRulesJudge>();
    }

    private void OnGUI()
    {
        if (GameMenu.IsOpen) return;   // hide while a menu is on screen
        if (dial == null) MakeArt();
        float s = Screen.height / 1080f;

        float size = 250 * s;
        Rect r = new Rect(28 * s, Screen.height - size - 50 * s, size, size);
        Vector2 c = r.center;

        // ---- the dial and its numbers -----------------------------------------
        GUI.DrawTexture(r, dial);
        numberStyle.fontSize = Mathf.RoundToInt(17 * s);
        for (int kmh = 0; kmh <= (int)dialMaxKmh; kmh += 20)
        {
            Vector2 p = OnDial(c, size * 0.33f, kmh);
            GUI.Label(new Rect(p.x - 20 * s, p.y - 12 * s, 40 * s, 24 * s), kmh.ToString(), numberStyle);
        }

        // ---- speed limit marker ---------------------------------------------------
        float limit = judge != null ? judge.SpeedLimitKmh : 0f;
        if (limit > 0f) DrawRotated(marker, c, AngleFor(limit), new Rect(c.x - 7 * s, r.y + 2 * s, 14 * s, 18 * s));

        // ---- needle -------------------------------------------------------------
        float speed = Mathf.Abs(car.SpeedKmh);
        DrawRotated(needle, c, AngleFor(speed), new Rect(c.x - 4 * s, r.y + 22 * s, 8 * s, size / 2f - 22 * s));
        GUI.DrawTexture(new Rect(c.x - 9 * s, c.y - 9 * s, 18 * s, 18 * s), MakeDot());

        // ---- digital speed, gear ----------------------------------------------------
        bool over = limit > 0f && speed > limit + 2f;
        speedStyle.fontSize = Mathf.RoundToInt(46 * s);
        speedStyle.normal.textColor = over ? new Color(1f, 0.32f, 0.28f) : Color.white;
        GUI.Label(new Rect(c.x - 80 * s, c.y + 22 * s, 160 * s, 54 * s), Mathf.RoundToInt(speed).ToString(), speedStyle);
        unitStyle.fontSize = Mathf.RoundToInt(15 * s);
        GUI.Label(new Rect(c.x - 80 * s, c.y + 68 * s, 160 * s, 20 * s), "km/h", unitStyle);

        string gear = car.Gear == "D" ? "D" + car.GearNumber : car.Gear;
        gearStyle.fontSize = Mathf.RoundToInt(22 * s);
        GUI.Label(new Rect(c.x - 40 * s, c.y + 88 * s, 80 * s, 30 * s), gear, gearStyle);   // in the empty space at the bottom of the dial

        // ---- rev bar under the dial --------------------------------------------------
        Rect bar = new Rect(r.x + 30 * s, r.yMax + 8 * s, size - 60 * s, 8 * s);
        Color old = GUI.color;
        GUI.color = new Color(1f, 1f, 1f, 0.15f);
        GUI.DrawTexture(bar, white);
        float rpm = car.Rpm01;
        GUI.color = Color.Lerp(new Color(0.3f, 0.9f, 0.5f), new Color(1f, 0.3f, 0.25f), Mathf.InverseLerp(0.7f, 1f, rpm));
        GUI.DrawTexture(new Rect(bar.x, bar.y, bar.width * rpm, bar.height), white);
        GUI.color = old;

        // ---- indicators and handbrake -----------------------------------------------
        bool lit = car.IndicatorLampLit;
        arrowStyle.fontSize = Mathf.RoundToInt(34 * s);
        GUI.color = car.LeftIndicatorOn && lit ? new Color(0.25f, 1f, 0.4f) : new Color(1f, 1f, 1f, 0.15f);
        GUI.Label(new Rect(r.x - 4 * s, r.y - 10 * s, 50 * s, 44 * s), "◀", arrowStyle);
        GUI.color = car.RightIndicatorOn && lit ? new Color(0.25f, 1f, 0.4f) : new Color(1f, 1f, 1f, 0.15f);
        GUI.Label(new Rect(r.xMax - 46 * s, r.y - 10 * s, 50 * s, 44 * s), "▶", arrowStyle);
        GUI.color = old;
        if (DriveInput.Handbrake())
        {
            parkStyle.fontSize = Mathf.RoundToInt(20 * s);
            GUI.Label(new Rect(c.x - 30 * s, c.y - 98 * s, 60 * s, 26 * s), "(P)", parkStyle);
        }

        // ---- controls help line (top-left) -----------------------------------------
        if (showControlsHelp)
        {
            helpStyle.fontSize = Mathf.RoundToInt(20 * s);
            GUI.Label(new Rect(20 * s, 20 * s, 1400 * s, 30 * s),
                "W/S gas & brake (hold S to reverse)   A/D steer   Space handbrake   Q/E indicators   H horn   C camera   F fps", helpStyle);
        }
    }

    // km/h -> angle of the needle (degrees, 0 = straight up, clockwise)
    private float AngleFor(float kmh) { return StartAngle + Sweep * Mathf.Clamp01(kmh / dialMaxKmh); }

    // Where on the dial a speed is, at a given distance from the centre.
    private Vector2 OnDial(Vector2 c, float radius, float kmh)
    {
        float a = AngleFor(kmh) * Mathf.Deg2Rad;
        return c + new Vector2(Mathf.Sin(a), -Mathf.Cos(a)) * radius;
    }

    // Draw a picture turned around the dial centre (like turning a clock hand).
    private static void DrawRotated(Texture2D tex, Vector2 pivot, float angle, Rect rect)
    {
        Matrix4x4 saved = GUI.matrix;
        GUIUtility.RotateAroundPivot(angle, pivot);
        GUI.DrawTexture(rect, tex);
        GUI.matrix = saved;
    }

    // =========================================================================
    //  Pictures made in code
    // =========================================================================
    private Texture2D dot;
    private Texture2D MakeDot()
    {
        if (dot != null) return dot;
        dot = Circle(32, new Color(0.9f, 0.92f, 0.95f), 1f, 0f);
        return dot;
    }

    private void MakeArt()
    {
        white = new Texture2D(1, 1); white.SetPixel(0, 0, Color.white); white.Apply();

        // The dial: dark round face, a light ring, tick marks every 10 km/h
        // (longer every 20), and a red zone near the top speed.
        const int N = 512;
        dial = new Texture2D(N, N, TextureFormat.RGBA32, false);
        float c = (N - 1) / 2f;
        for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                float dx = x - c, dy = c - y;                   // dy: up is positive
                float d = Mathf.Sqrt(dx * dx + dy * dy) / c;    // 0 centre, 1 edge
                float ang = Mathf.Atan2(dx, dy) * Mathf.Rad2Deg; // 0 = up, clockwise
                Color col = new Color(0, 0, 0, 0);
                if (d <= 1f) col = new Color(0.05f, 0.06f, 0.09f, 0.88f);                // face
                if (d > 0.93f && d <= 1f) col = new Color(0.75f, 0.8f, 0.9f, 0.9f);       // outer ring
                bool onScale = ang >= StartAngle - 0.5f && ang <= StartAngle + Sweep + 0.5f;
                if (onScale && d > 0.78f && d < 0.9f)
                {
                    float kmh = (ang - StartAngle) / Sweep * dialMaxKmh;
                    float toTick = Mathf.Abs(kmh - Mathf.Round(kmh / 10f) * 10f) / dialMaxKmh * Sweep;   // degrees from tick
                    bool major = Mathf.Abs(kmh - Mathf.Round(kmh / 20f) * 20f) / dialMaxKmh * Sweep < 1.1f;
                    if (toTick < (major ? 1.1f : 0.7f) && (major || d > 0.83f)) col = Color.white;
                    else if (kmh > dialMaxKmh * 0.8f && d > 0.86f) col = new Color(0.85f, 0.2f, 0.18f, 0.9f);   // red zone
                }
                if (d > 0.985f) col.a *= Mathf.Clamp01((1f - d) / 0.015f);   // soft edge
                dial.SetPixel(x, y, col);
            }
        dial.Apply();

        // Needle: a thin orange bar, pointing up, with a fine tip.
        needle = new Texture2D(16, 128, TextureFormat.RGBA32, false);
        for (int y = 0; y < 128; y++)
            for (int x = 0; x < 16; x++)
            {
                float half = Mathf.Lerp(7.5f, 1.2f, y / 127f);   // wide at the centre, thin at the tip
                bool on = Mathf.Abs(x - 7.5f) < half;
                needle.SetPixel(x, y, on ? new Color(1f, 0.45f, 0.1f) : new Color(0, 0, 0, 0));
            }
        needle.Apply();

        // Speed-limit marker: a small red triangle pointing in.
        marker = new Texture2D(16, 20, TextureFormat.RGBA32, false);
        for (int y = 0; y < 20; y++)
            for (int x = 0; x < 16; x++)
            {
                float half = (19 - y) / 19f * 7.5f;   // wide at the top (outside), point at the bottom
                bool on = Mathf.Abs(x - 7.5f) < half;
                marker.SetPixel(x, 19 - y, on ? new Color(1f, 0.2f, 0.18f) : new Color(0, 0, 0, 0));
            }
        marker.Apply();

        speedStyle = Style(TextAnchor.MiddleCenter, true, Color.white);
        unitStyle = Style(TextAnchor.MiddleCenter, false, new Color(0.7f, 0.75f, 0.85f));
        gearStyle = Style(TextAnchor.MiddleCenter, true, new Color(1f, 0.82f, 0.3f));
        numberStyle = Style(TextAnchor.MiddleCenter, true, new Color(0.85f, 0.88f, 0.95f));
        arrowStyle = Style(TextAnchor.MiddleCenter, true, Color.white);
        parkStyle = Style(TextAnchor.MiddleCenter, true, new Color(1f, 0.3f, 0.25f));
        helpStyle = Style(TextAnchor.UpperLeft, false, new Color(0.9f, 0.9f, 0.9f));
    }

    private static GUIStyle Style(TextAnchor anchor, bool bold, Color colour)
    {
        var st = new GUIStyle(GUI.skin.label) { alignment = anchor, fontStyle = bold ? FontStyle.Bold : FontStyle.Normal };
        st.normal.textColor = colour;
        return st;
    }

    private static Texture2D Circle(int size, Color colour, float outer, float inner)
    {
        var t = new Texture2D(size, size, TextureFormat.RGBA32, false);
        float c = (size - 1) / 2f;
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float d = Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c)) / c;
                t.SetPixel(x, y, d <= outer && d >= inner ? colour : new Color(0, 0, 0, 0));
            }
        t.Apply();
        return t;
    }
}
