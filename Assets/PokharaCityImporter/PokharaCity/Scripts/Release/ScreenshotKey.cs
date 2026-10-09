// =============================================================================
//  ScreenshotKey.cs  —  press F12 to take a screenshot
// =============================================================================
//  In the Unity Editor, pictures go to  <your project>/docs/screenshots/
//  (ready for the README on GitHub). In the built app they go to
//  Pictures/Pokhara on your Mac.
//
//  Nothing to set up: [RuntimeInitializeOnLoadMethod] makes Unity create
//  this by itself every time the game starts, like a phone app that is
//  always running in the background.
// =============================================================================

using System;
using System.IO;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

public class ScreenshotKey : MonoBehaviour
{
    private string lastSaved = "";
    private float showUntil;
    private int savedFrame;
    private GUIStyle style;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void CreateAutomatically()
    {
        if (FindFirstObjectByType<ScreenshotKey>() != null) return;
        var go = new GameObject("Screenshot key (F12)");
        DontDestroyOnLoad(go);
        go.AddComponent<ScreenshotKey>();
    }

    private void Update()
    {
#if ENABLE_INPUT_SYSTEM
        bool pressed = Keyboard.current != null && Keyboard.current.f12Key.wasPressedThisFrame;
#else
        bool pressed = Input.GetKeyDown(KeyCode.F12);
#endif
        if (!pressed) return;

#if UNITY_EDITOR
        string folder = Path.Combine(Path.GetDirectoryName(Application.dataPath), "docs", "screenshots");
#else
        string folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Pokhara");
#endif
        Directory.CreateDirectory(folder);
        string file = Path.Combine(folder, "pokhara-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".png");
        ScreenCapture.CaptureScreenshot(file);   // saved at the end of this frame
        lastSaved = file;
        showUntil = Time.unscaledTime + 2f;
        savedFrame = Time.frameCount;
        Debug.Log("Screenshot saved: " + file);
    }

    private void OnGUI()
    {
        // Wait one frame, so the message itself is not in the picture.
        if (Time.unscaledTime > showUntil || Time.frameCount <= savedFrame + 1) return;
        if (style == null)
        {
            style = new GUIStyle(GUI.skin.box) { fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            style.normal.textColor = Color.white;
        }
        float s = Screen.height / 1080f;
        style.fontSize = Mathf.RoundToInt(18 * s);
        GUI.Box(new Rect(Screen.width / 2f - 260 * s, Screen.height - 70 * s, 520 * s, 40 * s),
                "Screenshot saved: " + Path.GetFileName(lastSaved), style);
    }
}
