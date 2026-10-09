// =============================================================================
//  FpsCounter.cs  —  press F to see frames per second
// =============================================================================
//  "FPS" = how many pictures per second the game draws. 60 feels smooth,
//  30 is OK, under 25 feels choppy. The GameMenu adds this by itself.
// =============================================================================

using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

public class FpsCounter : MonoBehaviour
{
    public bool show = false;

    private float smoothed = 60f;
    private GUIStyle style;

    private void Update()
    {
#if ENABLE_INPUT_SYSTEM
        bool toggle = Keyboard.current != null && Keyboard.current.fKey.wasPressedThisFrame;
#else
        bool toggle = Input.GetKeyDown(KeyCode.F);
#endif
        if (toggle) show = !show;

        // Average over the last moments, so the number doesn't flicker.
        if (Time.unscaledDeltaTime > 0f)
            smoothed = Mathf.Lerp(smoothed, 1f / Time.unscaledDeltaTime, 0.05f);
    }

    private void OnGUI()
    {
        if (!show) return;
        if (style == null)
        {
            style = new GUIStyle(GUI.skin.box) { fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
        }
        float s = Screen.height / 1080f;
        style.fontSize = Mathf.RoundToInt(20 * s);
        int fps = Mathf.RoundToInt(smoothed);
        style.normal.textColor = fps >= 50 ? new Color(0.3f, 1f, 0.45f) : fps >= 30 ? new Color(1f, 0.8f, 0.2f) : new Color(1f, 0.35f, 0.3f);
        int cars = TrafficManager.Instance != null ? TrafficManager.Instance.vehicles.Count : 0;
        GUI.Box(new Rect(Screen.width / 2f - 130 * s, 12 * s, 260 * s, 36 * s), fps + " FPS   ·   " + cars + " vehicles", style);
    }
}
