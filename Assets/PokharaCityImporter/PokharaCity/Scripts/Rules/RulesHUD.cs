// =============================================================================
//  RulesHUD.cs  —  speed limit sign, score and fines on the screen
// =============================================================================
//  Top-right corner:  a round speed-limit sign (like the real red-ring sign),
//                     the road name, any school/silent zone, the signal ahead
//                     and your score out of 100.
//  Top-middle:        a red banner for 3 seconds whenever you get a fine.
//  Under the score:   your last 4 fines.
//
//  Uses OnGUI (same as the dashboard), so there is nothing to set up.
// =============================================================================

using UnityEngine;

[RequireComponent(typeof(RoadRulesJudge))]
public class RulesHUD : MonoBehaviour
{
    public float bannerSeconds = 3f;

    private RoadRulesJudge judge;
    private Texture2D signTexture, dot;
    private GUIStyle big, normal, small, signNumber, banner;

    private void Awake()
    {
        judge = GetComponent<RoadRulesJudge>();
        signTexture = MakeSpeedSign(128);
        dot = MakeDot(64);
    }

    private void OnGUI()
    {
        if (judge == null || !judge.enabled) return;
        if (big == null) MakeStyles();
        float s = Screen.height / 1080f;
        big.fontSize = Mathf.RoundToInt(40 * s);
        normal.fontSize = Mathf.RoundToInt(22 * s);
        small.fontSize = Mathf.RoundToInt(19 * s);
        signNumber.fontSize = Mathf.RoundToInt(38 * s);
        banner.fontSize = Mathf.RoundToInt(30 * s);

        // ---- Panel in the top-right corner -----------------------------------
        float w = 540 * s, x = Screen.width - w - 20 * s, y = 20 * s;
        GUI.Box(new Rect(x, y, w, 300 * s), GUIContent.none);

        // Speed limit sign
        Rect sign = new Rect(x + 15 * s, y + 15 * s, 100 * s, 100 * s);
        GUI.DrawTexture(sign, signTexture);
        GUI.Label(sign, judge.SpeedLimitKmh > 0f ? Mathf.RoundToInt(judge.SpeedLimitKmh).ToString() : "-", signNumber);

        // Road name, zone, signal
        float tx = x + 130 * s;
        GUI.Label(new Rect(tx, y + 15 * s, w - 140 * s, 30 * s), judge.RoadName, normal);
        if (judge.ZoneLabel != "")
        {
            Color old = GUI.color; GUI.color = Color.yellow;
            GUI.Label(new Rect(tx, y + 45 * s, w - 140 * s, 30 * s), judge.ZoneLabel, small);
            GUI.color = old;
        }
        if (judge.SignalAhead != null)
        {
            Color old = GUI.color;
            GUI.color = judge.SignalAheadColour == LightColour.Red ? new Color(1f, 0.15f, 0.1f)
                      : judge.SignalAheadColour == LightColour.Amber ? new Color(1f, 0.7f, 0f) : new Color(0.2f, 1f, 0.3f);
            GUI.DrawTexture(new Rect(tx, y + 80 * s, 26 * s, 26 * s), dot);
            GUI.color = old;
            GUI.Label(new Rect(tx + 34 * s, y + 78 * s, w - 175 * s, 30 * s), "Signal ahead", small);
        }

        // Score
        GUI.Label(new Rect(x + 15 * s, y + 120 * s, w, 50 * s), "Score " + judge.Score + " / " + judge.startScore, big);

        // Last 4 fines
        int shown = 0;
        for (int i = judge.violations.Count - 1; i >= 0 && shown < 4; i--, shown++)
        {
            Violation v = judge.violations[i];
            GUI.Label(new Rect(x + 15 * s, y + (175 + shown * 30) * s, w - 25 * s, 30 * s), "-" + v.points + "  " + v.message, small);
        }
        if (judge.violations.Count == 0)
            GUI.Label(new Rect(x + 15 * s, y + 175 * s, w - 25 * s, 30 * s), "No fines yet. Drive carefully!", small);

        // ---- Red banner for a new fine ---------------------------------------
        if (judge.violations.Count > 0)
        {
            Violation last = judge.violations[judge.violations.Count - 1];
            float age = Time.time - last.time;
            if (age < bannerSeconds)
            {
                Rect r = new Rect(Screen.width / 2f - 450 * s, 70 * s, 900 * s, 60 * s);
                Color old = GUI.color;
                GUI.color = new Color(0.8f, 0.05f, 0.05f, Mathf.Clamp01((bannerSeconds - age) * 2f));
                GUI.DrawTexture(r, Texture2D.whiteTexture);
                GUI.color = old;
                GUI.Label(r, "-" + last.points + "   " + last.message, banner);
            }
        }
    }

    private void MakeStyles()
    {
        big = new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold };
        big.normal.textColor = Color.white;
        normal = new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold, wordWrap = true };
        normal.normal.textColor = Color.white;
        small = new GUIStyle(GUI.skin.label) { wordWrap = false };
        small.normal.textColor = new Color(0.9f, 0.9f, 0.9f);
        signNumber = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
        signNumber.normal.textColor = Color.black;
        banner = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
        banner.normal.textColor = Color.white;
    }

    // White circle with a thick red ring: the speed-limit sign shape.
    private static Texture2D MakeSpeedSign(int size)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        float c = (size - 1) / 2f;
        for (int py = 0; py < size; py++)
            for (int px = 0; px < size; px++)
            {
                float d = Mathf.Sqrt((px - c) * (px - c) + (py - c) * (py - c)) / c;   // 0 centre, 1 edge
                Color col = d > 1f ? new Color(0, 0, 0, 0) : d > 0.78f ? new Color(0.85f, 0.05f, 0.05f) : Color.white;
                // Soft edge so it doesn't look jagged.
                if (d > 0.97f && d <= 1f) col.a = (1f - d) / 0.03f;
                tex.SetPixel(px, py, col);
            }
        tex.Apply();
        return tex;
    }

    private static Texture2D MakeDot(int size)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        float c = (size - 1) / 2f;
        for (int py = 0; py < size; py++)
            for (int px = 0; px < size; px++)
            {
                float d = Mathf.Sqrt((px - c) * (px - c) + (py - c) * (py - c)) / c;
                tex.SetPixel(px, py, d > 1f ? new Color(1, 1, 1, 0) : Color.white);
            }
        tex.Apply();
        return tex;
    }
}
