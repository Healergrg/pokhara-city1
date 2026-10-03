// =============================================================================
//  CarDashboard.cs  —  speed, gear and indicator arrows on the screen
// =============================================================================
//  Uses OnGUI, Unity's simplest way to draw text on screen: no Canvas or UI
//  setup needed. Later (Week 7) you can replace it with a nicer UI Canvas.
// =============================================================================

using UnityEngine;

[RequireComponent(typeof(PokharaCar))]
public class CarDashboard : MonoBehaviour
{
    public bool showControlsHelp = true;

    private PokharaCar car;
    private GUIStyle big, small, arrowOn, arrowOff;

    private void Awake()
    {
        car = GetComponent<PokharaCar>();
    }

    private void OnGUI()
    {
        if (big == null) MakeStyles();
        float s = Screen.height / 1080f;   // scale everything with the screen size

        // Dark panel in the bottom-left corner
        Rect panel = new Rect(20 * s, Screen.height - 190 * s, 330 * s, 170 * s);
        GUI.Box(panel, GUIContent.none);

        big.fontSize = Mathf.RoundToInt(64 * s);
        small.fontSize = Mathf.RoundToInt(22 * s);
        arrowOn.fontSize = arrowOff.fontSize = Mathf.RoundToInt(48 * s);

        int speed = Mathf.RoundToInt(Mathf.Abs(car.SpeedKmh));
        GUI.Label(new Rect(panel.x + 20 * s, panel.y + 15 * s, 200 * s, 80 * s), speed.ToString(), big);
        GUI.Label(new Rect(panel.x + 25 * s, panel.y + 95 * s, 200 * s, 30 * s), "km/h", small);
        GUI.Label(new Rect(panel.x + 230 * s, panel.y + 30 * s, 80 * s, 70 * s), car.Gear, big);

        // Indicator arrows blink green, like on a real dashboard.
        bool lit = car.IndicatorLampLit;
        GUI.Label(new Rect(panel.x + 20 * s, panel.y + 120 * s, 60 * s, 50 * s), "◀", car.LeftIndicatorOn && lit ? arrowOn : arrowOff);
        GUI.Label(new Rect(panel.x + 260 * s, panel.y + 120 * s, 60 * s, 50 * s), "▶", car.RightIndicatorOn && lit ? arrowOn : arrowOff);

        if (showControlsHelp)
        {
            GUI.Label(new Rect(20 * s, 20 * s, 700 * s, 30 * s),
                "W/S gas & brake (hold S to reverse)   A/D steer   Space handbrake   Q/E indicators   H horn   C camera", small);
        }
    }

    private void MakeStyles()
    {
        big = new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold };
        big.normal.textColor = Color.white;
        small = new GUIStyle(GUI.skin.label);
        small.normal.textColor = new Color(0.85f, 0.85f, 0.85f);
        arrowOn = new GUIStyle(GUI.skin.label);
        arrowOn.normal.textColor = new Color(0.2f, 1f, 0.3f);
        arrowOff = new GUIStyle(GUI.skin.label);
        arrowOff.normal.textColor = new Color(0.3f, 0.3f, 0.3f);
    }
}
