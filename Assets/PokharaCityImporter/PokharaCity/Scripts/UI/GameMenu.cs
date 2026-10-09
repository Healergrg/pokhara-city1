// =============================================================================
//  GameMenu.cs  —  start screen, pause menu, settings and credits
// =============================================================================
//  When you press Play:
//    a TITLE SCREEN appears while the camera flies slowly around Pokhara
//    (like the opening shot of a film). Press "Free Drive" and the screen
//    fades to black and back, and you are sitting behind your car.
//
//  While driving:
//    Esc (when no mission is running) or P (any time) = PAUSE MENU
//
//  Everything is drawn with OnGUI, like the dashboard, so there is nothing
//  to set up in the Unity Editor. Other scripts check GameMenu.IsOpen and
//  hide their own on-screen panels while a menu is showing.
// =============================================================================

using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

public class GameMenu : MonoBehaviour
{
    // True while any menu is on screen (the HUDs hide themselves).
    public static bool IsOpen { get; private set; }

    [Tooltip("Show the title screen when the game starts. Untick while testing to jump straight into driving.")]
    public bool showTitleOnStart = true;

    [Header("Title screen camera flight")]
    public float orbitRadius = 260f;
    public float orbitHeight = 110f;
    public float orbitSpeed = 4f;   // degrees per second

    private enum Page { None, Title, Pause, Settings, Credits }
    private Page page = Page.None;
    private Page backPage = Page.Title;   // where "Back" goes from Settings / Credits

    private Camera cam;
    private CarCamera carCam;
    private PokharaCar car;
    private MissionManager missions;
    private MissionManager.State lastMissionState = MissionManager.State.FreeRoam;

    // Fade to black and back (0 = clear, 1 = black)
    private float fade, fadeTarget;
    private System.Action afterFade;

    private GUIStyle titleStyle, subStyle, buttonStyle, panelStyle, labelStyle, smallStyle, headStyle, chipOn, chipOff;
    private Texture2D black, vignette;

    // =========================================================================
    private void Start()
    {
        cam = Camera.main;
        carCam = FindFirstObjectByType<CarCamera>();
        car = FindFirstObjectByType<PokharaCar>();
        missions = FindFirstObjectByType<MissionManager>();

        // The FPS counter (press F) is added automatically.
        if (GetComponent<FpsCounter>() == null) gameObject.AddComponent<FpsCounter>();

        GameSettings.Load();
        GameSettings.Apply();

        if (showTitleOnStart) Open(Page.Title);
    }

    private void OnDestroy()
    {
        IsOpen = false;
        Time.timeScale = 1f;
        AudioListener.pause = false;
    }

    private void Update()
    {
        // ---- fade animation (uses real time: the game is paused in menus) -------
        if (!Mathf.Approximately(fade, fadeTarget))
        {
            fade = Mathf.MoveTowards(fade, fadeTarget, Time.unscaledDeltaTime / 0.35f);
            if (Mathf.Approximately(fade, fadeTarget) && fadeTarget >= 1f && afterFade != null)
            {
                System.Action next = afterFade;
                afterFade = null;
                next();
                fadeTarget = 0f;   // and fade back in
            }
        }

        // ---- opening the pause menu --------------------------------------------
        MissionManager.State missionState = missions != null ? missions.CurrentState : MissionManager.State.FreeRoam;
        if (page == Page.None && fadeTarget == 0f)
        {
            // Esc only while free driving (during a mission Esc means "quit the mission").
            bool freeNow = missionState == MissionManager.State.FreeRoam && lastMissionState == MissionManager.State.FreeRoam;
            if (KeyP() || (KeyEsc() && freeNow)) Open(Page.Pause);
        }
        else if (page == Page.Pause && (KeyEsc() || KeyP())) Resume();
        else if ((page == Page.Settings || page == Page.Credits) && KeyEsc()) page = backPage;
        lastMissionState = missionState;
    }

    // While the title screen shows, the camera flies around the city.
    private void LateUpdate()
    {
        if (page != Page.Title || cam == null) return;
        Vector3 centre = car != null ? car.transform.position : Vector3.zero;
        float angle = Time.unscaledTime * orbitSpeed * Mathf.Deg2Rad;
        Vector3 offset = new Vector3(Mathf.Sin(angle), 0f, -Mathf.Cos(angle)) * orbitRadius + Vector3.up * orbitHeight;
        cam.transform.position = centre + offset;
        // Look a little north of the car, so Machhapuchhre is often in view.
        cam.transform.rotation = Quaternion.LookRotation(centre + new Vector3(0f, 20f, 120f) - cam.transform.position, Vector3.up);
    }

    // ---- opening and closing --------------------------------------------------
    private void Open(Page p)
    {
        page = p;
        IsOpen = true;
        Time.timeScale = 0f;          // freeze the world
        AudioListener.pause = true;   // and its sounds
        if (p == Page.Title && carCam != null) carCam.enabled = false;   // the menu flies the camera
    }

    // Close the menus and go back to driving (with a fade if the camera moved).
    private void Resume()
    {
        if (page == Page.Title)
        {
            FadeThen(() => { CloseNow(); PutCameraBehindCar(); });
        }
        else CloseNow();
    }

    private void CloseNow()
    {
        page = Page.None;
        IsOpen = false;
        Time.timeScale = 1f;
        AudioListener.pause = false;
        if (carCam != null) carCam.enabled = true;
    }

    private void FadeThen(System.Action action)
    {
        afterFade = action;
        fadeTarget = 1f;
    }

    private void PutCameraBehindCar()
    {
        if (cam == null || car == null || carCam == null) return;
        Vector3 back = car.transform.forward; back.y = 0f; back.Normalize();
        cam.transform.position = car.transform.position - back * carCam.distance + Vector3.up * carCam.height;
        cam.transform.rotation = Quaternion.LookRotation(car.transform.position + Vector3.up * 1.2f - cam.transform.position, Vector3.up);
    }

    private void Quit()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;   // stop Play mode in the Editor
#else
        Application.Quit();
#endif
    }

    // =========================================================================
    //  Drawing
    // =========================================================================
    private void OnGUI()
    {
        if (titleStyle == null) MakeStyles();
        float s = Screen.height / 1080f;
        ScaleStyles(s);
        GUI.depth = -100;   // in front of the other HUDs
        GUI.enabled = fadeTarget == 0f;   // no double clicks while the screen fades

        switch (page)
        {
            case Page.Title: DrawTitle(s); break;
            case Page.Pause: DrawPause(s); break;
            case Page.Settings: DrawSettings(s); break;
            case Page.Credits: DrawCredits(s); break;
        }

        GUI.enabled = true;
        if (fade > 0.001f)
        {
            Color old = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, fade);
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), black);
            GUI.color = old;
        }
    }

    private void DrawTitle(float s)
    {
        // A dark gradient on the left so the words are easy to read over the city.
        GUI.DrawTexture(new Rect(0, 0, Screen.width * 0.62f, Screen.height), vignette);

        float x = 110 * s, y = Screen.height * 0.2f;
        GUI.Label(new Rect(x, y, 900 * s, 40 * s), "28.2096 N  ·  83.9856 E  ·  822 m", smallStyle);
        GUI.Label(new Rect(x - 6 * s, y + 30 * s, 1200 * s, 170 * s), "POKHARA", titleStyle);
        GUI.Label(new Rect(x, y + 190 * s, 900 * s, 50 * s), "City Driving Simulator", subStyle);
        GUI.Label(new Rect(x, y + 245 * s, 900 * s, 34 * s), "The real streets of Lakeside and Damside. Keep left!", labelStyle);

        float by = y + 320 * s;
        if (Button(x, by, s, "Free drive")) Resume();
        if (Button(x, by + 74 * s, s, "Missions")) { FadeThen(() => { CloseNow(); PutCameraBehindCar(); if (missions != null) missions.OpenMenu(); }); }
        if (Button(x, by + 148 * s, s, "Settings")) { backPage = Page.Title; page = Page.Settings; }
        if (Button(x, by + 222 * s, s, "Credits")) { backPage = Page.Title; page = Page.Credits; }
        if (Button(x, by + 296 * s, s, "Quit")) Quit();

        GUI.Label(new Rect(x, Screen.height - 60 * s, 1200 * s, 30 * s),
            "Made in Pokhara by Pasang Dorje Gurung   ·   Map data © OpenStreetMap contributors", smallStyle);
    }

    private void DrawPause(float s)
    {
        DimScreen();
        Rect r = Centered(520 * s, 560 * s);
        GUI.Box(r, GUIContent.none, panelStyle);
        GUI.Label(new Rect(r.x, r.y + 30 * s, r.width, 60 * s), "Paused", headStyle);
        float x = r.x + 80 * s, y = r.y + 115 * s;
        if (Button(x, y, s, "Resume")) Resume();
        if (Button(x, y + 74 * s, s, "Missions")) { CloseNow(); if (missions != null) missions.OpenMenu(); }
        if (Button(x, y + 148 * s, s, "Settings")) { backPage = Page.Pause; page = Page.Settings; }
        if (Button(x, y + 222 * s, s, "Credits")) { backPage = Page.Pause; page = Page.Credits; }
        if (Button(x, y + 296 * s, s, "Main menu")) { if (missions != null) missions.QuitMission(); Open(Page.Title); }
        if (Button(x, y + 370 * s, s, "Quit")) Quit();
    }

    private void DrawSettings(float s)
    {
        DimScreen();
        Rect r = Centered(820 * s, 640 * s);
        GUI.Box(r, GUIContent.none, panelStyle);
        GUI.Label(new Rect(r.x, r.y + 28 * s, r.width, 60 * s), "Settings", headStyle);

        float lx = r.x + 60 * s, cx = r.x + 300 * s, y = r.y + 120 * s, row = 70 * s;
        bool changed = false;

        GUI.Label(new Rect(lx, y, 240 * s, 40 * s), "Traffic", labelStyle);
        changed |= Chips(cx, y, s, GameSettings.TrafficNames, ref GameSettings.traffic);
        y += row;

        GUI.Label(new Rect(lx, y, 240 * s, 40 * s), "Graphics", labelStyle);
        changed |= Chips(cx, y, s, GameSettings.GraphicsNames, ref GameSettings.graphics);
        y += row;

        GUI.Label(new Rect(lx, y, 240 * s, 40 * s), "Volume", labelStyle);
        float v = GUI.HorizontalSlider(new Rect(cx, y + 14 * s, 380 * s, 20 * s), GameSettings.volume, 0f, 1f);
        GUI.Label(new Rect(cx + 400 * s, y, 80 * s, 40 * s), Mathf.RoundToInt(v * 100) + "%", labelStyle);
        if (!Mathf.Approximately(v, GameSettings.volume)) { GameSettings.volume = v; changed = true; }
        y += row;

        changed |= Switch(lx, cx, y, s, "Minimap", ref GameSettings.minimap); y += row;
        changed |= Switch(lx, cx, y, s, "Controls help", ref GameSettings.controlsHelp); y += row;
        changed |= Switch(lx, cx, y, s, "Mission route line", ref GameSettings.routeLine); y += row;

        if (changed) { GameSettings.Save(); GameSettings.Apply(); }

        if (Button(r.x + (r.width - 360 * s) / 2f, r.yMax - 95 * s, s, "Back")) page = backPage;
    }

    private void DrawCredits(float s)
    {
        DimScreen();
        Rect r = Centered(860 * s, 660 * s);
        GUI.Box(r, GUIContent.none, panelStyle);
        GUI.Label(new Rect(r.x, r.y + 28 * s, r.width, 60 * s), "Credits", headStyle);
        string text =
            "Pokhara City Driving Simulator\n" +
            "Designed and built by Pasang Dorje Gurung\n" +
            "Map data © OpenStreetMap contributors\n" +
            "Available under the Open Database Licence (ODbL): openstreetmap.org/copyright\n\n" +
            "Made with Unity 6 and C#.\n" +
            "Roads, buildings, the lake, trees, signals and traffic are generated from the\n" +
            "real map of Lakeside and Damside by tools written for this project.\n\n" +
            "Dhanyabad for playing! Drive on the left, use your indicators,\n" +
            "and always stop for people on the zebra crossing.";
        GUI.Label(new Rect(r.x + 60 * s, r.y + 110 * s, r.width - 120 * s, r.height - 220 * s), text, labelStyle);
        if (Button(r.x + (r.width - 360 * s) / 2f, r.yMax - 95 * s, s, "Back")) page = backPage;
    }

    // ---- little building blocks ------------------------------------------------
    private bool Button(float x, float y, float s, string text)
    {
        return GUI.Button(new Rect(x, y, 360 * s, 60 * s), text, buttonStyle);
    }

    // A row of choices where one is highlighted (like radio buttons).
    private bool Chips(float x, float y, float s, string[] names, ref int value)
    {
        bool changed = false;
        for (int i = 0; i < names.Length; i++)
        {
            Rect b = new Rect(x + i * 118 * s, y, 110 * s, 44 * s);
            if (GUI.Button(b, names[i], i == value ? chipOn : chipOff) && value != i) { value = i; changed = true; }
        }
        return changed;
    }

    private bool Switch(float lx, float cx, float y, float s, string label, ref bool value)
    {
        GUI.Label(new Rect(lx, y, 240 * s, 40 * s), label, labelStyle);
        bool changed = false;
        if (GUI.Button(new Rect(cx, y, 110 * s, 44 * s), "On", value ? chipOn : chipOff) && !value) { value = true; changed = true; }
        if (GUI.Button(new Rect(cx + 118 * s, y, 110 * s, 44 * s), "Off", !value ? chipOn : chipOff) && value) { value = false; changed = true; }
        return changed;
    }

    private void DimScreen()
    {
        Color old = GUI.color;
        GUI.color = new Color(0f, 0f, 0f, 0.55f);
        GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), black);
        GUI.color = old;
    }

    private static Rect Centered(float w, float h) { return new Rect((Screen.width - w) / 2f, (Screen.height - h) / 2f, w, h); }

    // ---- keys (both Unity input systems) ---------------------------------------
    private static bool KeyEsc()
    {
#if ENABLE_INPUT_SYSTEM
        return Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame;
#else
        return Input.GetKeyDown(KeyCode.Escape);
#endif
    }

    private static bool KeyP()
    {
#if ENABLE_INPUT_SYSTEM
        return Keyboard.current != null && Keyboard.current.pKey.wasPressedThisFrame;
#else
        return Input.GetKeyDown(KeyCode.P);
#endif
    }

    // ---- looks -----------------------------------------------------------------
    private void MakeStyles()
    {
        black = new Texture2D(1, 1); black.SetPixel(0, 0, Color.black); black.Apply();

        // Left-to-right dark gradient for the title screen.
        vignette = new Texture2D(64, 1);
        for (int i = 0; i < 64; i++) vignette.SetPixel(i, 0, new Color(0.02f, 0.03f, 0.08f, 0.85f * (1f - i / 63f)));
        vignette.Apply();
        vignette.wrapMode = TextureWrapMode.Clamp;

        Texture2D panel = Rounded(new Color(0.07f, 0.09f, 0.14f, 0.96f), new Color(1f, 1f, 1f, 0.12f));
        Texture2D btn = Rounded(new Color(1f, 1f, 1f, 0.08f), new Color(1f, 1f, 1f, 0.18f));
        Texture2D btnHover = Rounded(new Color(0.95f, 0.76f, 0.19f, 0.95f), new Color(1f, 0.85f, 0.4f, 1f));
        Texture2D btnDown = Rounded(new Color(0.8f, 0.6f, 0.1f, 1f), new Color(1f, 0.85f, 0.4f, 1f));
        Texture2D chipSel = Rounded(new Color(0.95f, 0.76f, 0.19f, 1f), new Color(1f, 0.85f, 0.4f, 1f));

        panelStyle = new GUIStyle { border = new RectOffset(16, 16, 16, 16) };
        panelStyle.normal.background = panel;

        buttonStyle = new GUIStyle(GUI.skin.button) { fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft, border = new RectOffset(16, 16, 16, 16) };
        buttonStyle.padding = new RectOffset(26, 16, 0, 0);
        buttonStyle.normal.background = btn; buttonStyle.normal.textColor = Color.white;
        buttonStyle.hover.background = btnHover; buttonStyle.hover.textColor = new Color(0.08f, 0.08f, 0.1f);
        buttonStyle.active.background = btnDown; buttonStyle.active.textColor = new Color(0.08f, 0.08f, 0.1f);
        buttonStyle.focused.background = btn; buttonStyle.focused.textColor = Color.white;

        chipOff = new GUIStyle(buttonStyle) { alignment = TextAnchor.MiddleCenter, padding = new RectOffset(4, 4, 0, 0) };
        chipOn = new GUIStyle(chipOff);
        chipOn.normal.background = chipSel; chipOn.normal.textColor = new Color(0.08f, 0.08f, 0.1f);
        chipOn.hover.background = chipSel; chipOn.hover.textColor = new Color(0.08f, 0.08f, 0.1f);

        titleStyle = new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold };
        titleStyle.normal.textColor = Color.white;
        subStyle = new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold };
        subStyle.normal.textColor = new Color(1f, 0.82f, 0.6f);
        headStyle = new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
        headStyle.normal.textColor = Color.white;
        labelStyle = new GUIStyle(GUI.skin.label) { wordWrap = true };
        labelStyle.normal.textColor = new Color(0.86f, 0.89f, 0.95f);
        smallStyle = new GUIStyle(GUI.skin.label);
        smallStyle.normal.textColor = new Color(0.62f, 0.68f, 0.85f);
    }

    private void ScaleStyles(float s)
    {
        titleStyle.fontSize = Mathf.RoundToInt(150 * s);
        subStyle.fontSize = Mathf.RoundToInt(40 * s);
        headStyle.fontSize = Mathf.RoundToInt(44 * s);
        buttonStyle.fontSize = Mathf.RoundToInt(26 * s);
        chipOff.fontSize = chipOn.fontSize = Mathf.RoundToInt(22 * s);
        labelStyle.fontSize = Mathf.RoundToInt(24 * s);
        smallStyle.fontSize = Mathf.RoundToInt(19 * s);
    }

    // A small picture of a rounded box. With "border" set on the style,
    // Unity stretches only the middle, so the corners stay round at any size.
    private static Texture2D Rounded(Color fill, Color edge)
    {
        const int size = 48, radius = 14;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                // distance outside the rounded rectangle (negative = inside)
                float dx = Mathf.Max(radius - x - 0.5f, x + 0.5f - (size - radius), 0f);
                float dy = Mathf.Max(radius - y - 0.5f, y + 0.5f - (size - radius), 0f);
                float d = Mathf.Sqrt(dx * dx + dy * dy) - radius;
                Color c = d > -1.5f ? edge : fill;           // a thin border line
                c.a *= Mathf.Clamp01(0.5f - d);              // soft, smooth corner
                tex.SetPixel(x, y, c);
            }
        tex.Apply();
        tex.wrapMode = TextureWrapMode.Clamp;
        return tex;
    }
}
