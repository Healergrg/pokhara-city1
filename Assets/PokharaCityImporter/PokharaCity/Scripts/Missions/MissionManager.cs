// =============================================================================
//  MissionManager.cs  —  driving missions with checkpoints, a timer and a result
// =============================================================================
//  How a mission works (like a real trial at the Yatayat office, but in the city):
//
//    M          open the mission menu (the game pauses)
//    1-9        start a mission
//    3-2-1-GO   your car waits at the start, then the clock runs
//    drive      follow the yellow line on the road, through the gates
//               (checkpoints), and listen to the directions:
//               "In 80 m turn LEFT onto Baidam Road"  -> use your indicator!
//    finish     PASS if you arrive in time AND your rules score is at least
//               the pass mark (usually 70/100). Hitting a pedestrian = instant FAIL.
//    R          try again      Esc   back to free driving
//
//  Your best time for every mission is saved on your Mac (PlayerPrefs).
// =============================================================================

using System.Collections.Generic;
using UnityEngine;

public class MissionManager : MonoBehaviour
{
    public enum State { FreeRoam, Menu, Countdown, Driving, Result }

    [Header("Missions (made by the builder; edit freely)")]
    public List<MissionDefinition> missions = new List<MissionDefinition>();

    [Header("Look (made by the builder)")]
    public GameObject checkpointTemplate;
    public GameObject finishTemplate;
    public Material routeLineMaterial;
    public bool showRouteLine = true;

    [Header("Rules")]
    public float checkpointSpacing = 250f;   // a gate every 250 m
    public float countdownSeconds = 3f;

    // ---- read by the HUD ------------------------------------------------------
    public State CurrentState { get; private set; } = State.FreeRoam;

    // ---- found automatically ----------------------------------------------------
    private PokharaCar car;
    private Rigidbody carBody;
    private RoadRulesJudge judge;
    private RoadNetwork network;

    // ---- the mission being driven -------------------------------------------------
    private int current = -1;
    private PlannedRoute route;
    private readonly List<float> checkpoints = new List<float>();   // metres along the route
    private int nextCheckpoint;
    private float countdownEnd, startTime, finishTime, timeLimit;
    private float progress;          // how far along the route you are (metres)
    private int progressIndex;
    private float offRoute;          // metres away from the route
    private bool passed;
    private string resultReason = "";
    private GameObject gate, finishGate;
    private LineRenderer line;
    private string message = ""; private float messageUntil;
    private GUIStyle big, bigLeft, normal, small, huge, title;

    // =========================================================================
    private void Start()
    {
        car = FindFirstObjectByType<PokharaCar>();
        judge = FindFirstObjectByType<RoadRulesJudge>();
        network = FindFirstObjectByType<RoadNetwork>();
        if (car != null) carBody = car.GetComponent<Rigidbody>();
        if (judge != null) judge.ViolationAdded += OnViolation;

        if (checkpointTemplate != null) { gate = Instantiate(checkpointTemplate, transform); gate.SetActive(false); }
        if (finishTemplate != null) { finishGate = Instantiate(finishTemplate, transform); finishGate.SetActive(false); }

        var lineObject = new GameObject("Route line");
        lineObject.transform.SetParent(transform, false);
        // Turn the object so its blue (z) arrow points up: the line then lies flat on the road.
        lineObject.transform.rotation = Quaternion.Euler(-90f, 0f, 0f);
        line = lineObject.AddComponent<LineRenderer>();
        line.useWorldSpace = true;
        line.alignment = LineAlignment.TransformZ;
        line.widthMultiplier = 0.45f;
        line.sharedMaterial = routeLineMaterial;
        line.positionCount = 0;
        line.enabled = false;
    }

    private void OnDestroy()
    {
        if (judge != null) judge.ViolationAdded -= OnViolation;
        Time.timeScale = 1f;
    }

    // =========================================================================
    private void Update()
    {
        if (car == null || network == null) return;
        if (GameMenu.IsOpen) return;   // the pause / title menu is on screen

        switch (CurrentState)
        {
            case State.FreeRoam:
                if (MissionKeys.Menu()) OpenMenu();
                break;

            case State.Menu:
                int pick = MissionKeys.Number();
                if (pick >= 0 && pick < missions.Count) StartMission(pick);
                else if (MissionKeys.Menu() || MissionKeys.Escape()) CloseMenu();
                break;

            case State.Countdown:
                if (Time.unscaledTime >= countdownEnd) Go();
                if (MissionKeys.Escape()) EndMission();
                break;

            case State.Driving:
                TrackProgress();
                if (MissionKeys.Restart()) StartMission(current);
                else if (MissionKeys.Escape()) EndMission();
                else if (MissionKeys.Menu()) { EndMission(); OpenMenu(); }
                break;

            case State.Result:
                if (MissionKeys.Restart()) StartMission(current);
                else if (MissionKeys.Menu()) OpenMenu();
                else if (MissionKeys.Escape()) EndMission();
                break;
        }
    }

    // ---- menu -------------------------------------------------------------------
    public void OpenMenu()   // public: the main menu's "Missions" button uses it
    {
        if (CurrentState == State.Countdown || CurrentState == State.Driving || CurrentState == State.Result) EndMission();
        CurrentState = State.Menu;
        Time.timeScale = 0f;   // pause the world while you choose
    }

    private void CloseMenu()
    {
        Time.timeScale = 1f;
        CurrentState = State.FreeRoam;
    }

    // Stop any mission and go back to free driving (used by the main menu).
    public void QuitMission()
    {
        if (CurrentState != State.FreeRoam) EndMission();
    }

    // ---- for the minimap -----------------------------------------------------------
    // The route being driven right now (null when free driving).
    public PlannedRoute ActiveRoute =>
        CurrentState == State.Countdown || CurrentState == State.Driving ? route : null;

    public bool TryGetNextGate(out Vector3 position)
    {
        position = Vector3.zero;
        if (ActiveRoute == null || nextCheckpoint >= checkpoints.Count) return false;
        position = route.PointAt(checkpoints[nextCheckpoint]);
        return true;
    }

    // ---- start ------------------------------------------------------------------
    public void StartMission(int index)
    {
        Time.timeScale = 1f;
        MissionDefinition m = missions[index];
        var stops = new List<Vector3> { m.start };
        stops.AddRange(m.via);
        stops.Add(m.finish);
        PlannedRoute planned = RoutePlanner.Plan(network, stops);
        if (planned == null)
        {
            Say("No road route found for this mission. Check its start and finish.");
            CurrentState = State.FreeRoam;
            return;
        }

        current = index;
        route = planned;
        timeLimit = m.timeLimitSeconds > 0f ? m.timeLimitSeconds : route.Length / (m.averageSpeedKmh / 3.6f) + 60f;

        // Checkpoints every 250 m, and the finish.
        checkpoints.Clear();
        for (float d = checkpointSpacing; d < route.Length - 60f; d += checkpointSpacing) checkpoints.Add(d);
        checkpoints.Add(route.Length);
        nextCheckpoint = 0;
        progress = 0f; progressIndex = 0;
        passed = false; resultReason = "";

        PlaceCarAtStart();
        if (TrafficManager.Instance != null) TrafficManager.Instance.ClearAround(route.points[0], 35f);
        ShowRouteLine();
        ShowGate();

        // Wait for 3-2-1: the car is frozen ("kinematic") until GO.
        if (carBody != null) carBody.isKinematic = true;
        countdownEnd = Time.unscaledTime + countdownSeconds;
        CurrentState = State.Countdown;
    }

    private void PlaceCarAtStart()
    {
        Vector3 p = route.points[0];
        Vector3 dir = route.DirectionAt(0f);
        Quaternion rot = Quaternion.LookRotation(dir, Vector3.up);
        if (carBody != null)
        {
            carBody.isKinematic = false;
            carBody.linearVelocity = Vector3.zero;
            carBody.angularVelocity = Vector3.zero;
            carBody.position = p + Vector3.up * 0.3f;
            carBody.rotation = rot;
        }
        car.transform.SetPositionAndRotation(p + Vector3.up * 0.3f, rot);

        // Move the camera straight behind the car (no long swoop across the city).
        CarCamera cam = FindFirstObjectByType<CarCamera>();
        if (cam != null)
            cam.transform.SetPositionAndRotation(p - dir * cam.distance + Vector3.up * cam.height, Quaternion.LookRotation(dir, Vector3.up));
    }

    private void Go()
    {
        if (carBody != null) carBody.isKinematic = false;
        if (judge != null) judge.ResetScore();
        startTime = Time.time;
        CurrentState = State.Driving;
        Say("GO! Follow the yellow line.");
    }

    // ---- while driving --------------------------------------------------------------
    private void TrackProgress()
    {
        Vector3 pos = car.transform.position;

        // Where on the route am I? Look a little behind and ahead of last time
        // (so a route that passes the same street twice doesn't confuse us).
        float best = float.MaxValue; int bestIndex = progressIndex; float bestProgress = progress;
        int from = Mathf.Max(0, progressIndex - 5), to = Mathf.Min(route.points.Count - 2, progressIndex + 40);
        for (int i = from; i <= to; i++)
        {
            Vector3 a = route.points[i], b = route.points[i + 1];
            Vector3 ab = b - a; ab.y = 0f;
            Vector3 ap = pos - a; ap.y = 0f;
            float t = ab.sqrMagnitude > 0.0001f ? Mathf.Clamp01(Vector3.Dot(ap, ab) / ab.sqrMagnitude) : 0f;
            Vector3 c = a + ab * t;
            float d = new Vector3(pos.x - c.x, 0f, pos.z - c.z).magnitude;
            if (d < best) { best = d; bestIndex = i; bestProgress = route.along[i] + t * ab.magnitude; }
        }
        offRoute = best;
        if (best < 30f) { progressIndex = bestIndex; progress = Mathf.Max(progress, bestProgress); }

        // Reached the next gate?
        if (nextCheckpoint < checkpoints.Count && progress >= checkpoints[nextCheckpoint] - 4f && offRoute < 14f)
        {
            nextCheckpoint++;
            if (nextCheckpoint >= checkpoints.Count) { Finish(); return; }
            Say("Checkpoint " + nextCheckpoint + " / " + (checkpoints.Count - 1));
            ShowGate();
        }

        // Out of time?
        if (Time.time - startTime > timeLimit) Fail("Out of time.");
    }

    private void OnViolation(Violation v)
    {
        if (CurrentState == State.Driving && v.message.Contains("pedestrian"))
            Fail("You hit a pedestrian. In a real test this is an instant fail.");
    }

    // ---- the end ----------------------------------------------------------------------
    private void Finish()
    {
        finishTime = Time.time - startTime;
        MissionDefinition m = missions[current];
        int score = judge != null ? judge.Score : 100;
        passed = score >= m.passScore;
        resultReason = passed ? "Well driven!" : "Score " + score + " is below the pass mark of " + m.passScore + ".";

        if (passed)
        {
            string key = "PokharaMission." + m.title + ".best";
            float bestTime = PlayerPrefs.GetFloat(key, float.MaxValue);
            if (finishTime < bestTime) { PlayerPrefs.SetFloat(key, finishTime); resultReason += "  New best time!"; }
            PlayerPrefs.SetInt("PokharaMission." + m.title + ".passed", 1);
            PlayerPrefs.Save();
        }
        HideRoute();
        CurrentState = State.Result;
    }

    private void Fail(string reason)
    {
        finishTime = Time.time - startTime;
        passed = false;
        resultReason = reason;
        HideRoute();
        CurrentState = State.Result;
    }

    private void EndMission()
    {
        Time.timeScale = 1f;
        if (carBody != null) carBody.isKinematic = false;
        HideRoute();
        current = -1; route = null;
        CurrentState = State.FreeRoam;
    }

    // ---- what you see on the road ------------------------------------------------
    private void ShowRouteLine()
    {
        if (line == null) return;
        line.enabled = showRouteLine && routeLineMaterial != null;
        line.positionCount = route.points.Count;
        for (int i = 0; i < route.points.Count; i++) line.SetPosition(i, route.points[i] + Vector3.up * 0.1f);
    }

    private void ShowGate()
    {
        bool isFinish = nextCheckpoint >= checkpoints.Count - 1;
        GameObject g = isFinish ? finishGate : gate;
        GameObject other = isFinish ? gate : finishGate;
        if (other != null) other.SetActive(false);
        if (g == null) return;
        float at = checkpoints[nextCheckpoint];
        Vector3 dir = route.DirectionAt(Mathf.Max(0f, at - 2f));
        g.transform.SetPositionAndRotation(route.PointAt(at), Quaternion.LookRotation(dir, Vector3.up));
        g.SetActive(true);
    }

    private void HideRoute()
    {
        if (line != null) line.enabled = false;
        if (gate != null) gate.SetActive(false);
        if (finishGate != null) finishGate.SetActive(false);
    }

    private void Say(string text) { message = text; messageUntil = Time.unscaledTime + 2.5f; }

    // =========================================================================
    //  On-screen panels
    // =========================================================================
    private void OnGUI()
    {
        if (car == null || GameMenu.IsOpen) return;
        if (big == null) MakeStyles();
        float s = Screen.height / 1080f;
        huge.fontSize = Mathf.RoundToInt(140 * s);
        title.fontSize = Mathf.RoundToInt(44 * s);
        big.fontSize = Mathf.RoundToInt(32 * s);
        bigLeft.fontSize = big.fontSize;
        normal.fontSize = Mathf.RoundToInt(24 * s);
        small.fontSize = Mathf.RoundToInt(20 * s);

        switch (CurrentState)
        {
            case State.FreeRoam:
                GUI.Label(new Rect(20 * s, 55 * s, 700 * s, 30 * s), "FREE DRIVING   -   M = missions   -   Esc = menu", small);
                break;
            case State.Menu: DrawMenu(s); break;
            case State.Countdown:
                int n = Mathf.CeilToInt(countdownEnd - Time.unscaledTime);
                GUI.Label(new Rect(0, Screen.height * 0.3f, Screen.width, 200 * s), n > 0 ? n.ToString() : "GO!", huge);
                DrawMissionPanel(s);
                break;
            case State.Driving: DrawMissionPanel(s); DrawCompass(s); break;
            case State.Result: DrawResult(s); break;
        }

        if (Time.unscaledTime < messageUntil)
            GUI.Label(new Rect(0, Screen.height * 0.2f, Screen.width, 60 * s), message, big);
    }

    private void DrawMenu(float s)
    {
        float w = 900 * s, h = (180 + missions.Count * 95) * s;
        Rect r = new Rect((Screen.width - w) / 2f, (Screen.height - h) / 2f, w, h);
        GUI.Box(r, GUIContent.none); GUI.Box(r, GUIContent.none);   // twice = darker
        GUI.Label(new Rect(r.x, r.y + 15 * s, w, 60 * s), "Pokhara Driving Missions", title);
        for (int i = 0; i < missions.Count; i++)
        {
            MissionDefinition m = missions[i];
            float y = r.y + (90 + i * 95) * s;
            bool done = PlayerPrefs.GetInt("PokharaMission." + m.title + ".passed", 0) == 1;
            float bestTime = PlayerPrefs.GetFloat("PokharaMission." + m.title + ".best", float.MaxValue);
            string badge = done ? "   PASSED  best " + Clock(bestTime) : "";
            if (GUI.Button(new Rect(r.x + 25 * s, y, 70 * s, 70 * s), (i + 1).ToString())) StartMission(i);
            GUI.Label(new Rect(r.x + 110 * s, y, w - 130 * s, 36 * s), m.title + badge, normal);
            GUI.Label(new Rect(r.x + 110 * s, y + 36 * s, w - 130 * s, 34 * s), m.description + "   (pass mark " + m.passScore + ")", small);
        }
        GUI.Label(new Rect(r.x, r.yMax - 55 * s, w, 40 * s), "Press a number (or click) to start.   M or Esc = keep driving freely", small);
    }

    private void DrawMissionPanel(float s)
    {
        MissionDefinition m = missions[current];
        Rect r = new Rect(20 * s, 55 * s, 620 * s, 215 * s);
        GUI.Box(r, GUIContent.none);
        GUI.Label(new Rect(r.x + 15 * s, r.y + 8 * s, r.width, 40 * s), m.title, normal);

        float used = CurrentState == State.Driving ? Time.time - startTime : 0f;
        float left = timeLimit - used;
        Color old = GUI.color;
        if (left < 30f) GUI.color = new Color(1f, 0.35f, 0.3f);
        GUI.Label(new Rect(r.x + 15 * s, r.y + 45 * s, r.width, 40 * s), "Time " + Clock(used) + " / " + Clock(timeLimit), bigLeft);
        GUI.color = old;

        float togo = Mathf.Max(0f, route.Length - progress);
        GUI.Label(new Rect(r.x + 15 * s, r.y + 90 * s, r.width, 30 * s),
            "Gate " + Mathf.Min(nextCheckpoint + 1, checkpoints.Count) + " / " + checkpoints.Count +
            "     " + (togo >= 1000f ? (togo / 1000f).ToString("0.0") + " km" : Mathf.RoundToInt(togo) + " m") + " to go", normal);

        GUI.Label(new Rect(r.x + 15 * s, r.y + 130 * s, r.width - 25 * s, 70 * s), NextDirection(), normal);

        if (offRoute > 25f && CurrentState == State.Driving)
        {
            GUI.color = new Color(1f, 0.35f, 0.3f);
            GUI.Label(new Rect(r.x + 15 * s, r.yMax + 5 * s, r.width, 30 * s), "You are off the route! Head back to the yellow line.", normal);
            GUI.color = old;
        }
    }

    // "In 120 m turn LEFT onto Baidam Road (indicator: Q)"
    private string NextDirection()
    {
        foreach (RouteInstruction ins in route.instructions)
        {
            float ahead = ins.at - progress;
            if (ahead < -5f) continue;
            if (ahead > 400f) break;
            string side = ins.turn < 0 ? "←  LEFT" : "RIGHT  →";
            string when = ahead < 15f ? "Now" : "In " + (Mathf.RoundToInt(ahead / 10f) * 10) + " m";
            return when + " turn " + side + " onto " + ins.road + "   (indicator: " + (ins.turn < 0 ? "Q" : "E") + ")";
        }
        float finish = route.Length - progress;
        return finish < 400f ? "The finish is " + Mathf.RoundToInt(finish) + " m ahead." : "↑  Follow the road";
    }

    // A big arrow at the top of the screen pointing to the next gate.
    private void DrawCompass(float s)
    {
        if (nextCheckpoint >= checkpoints.Count) return;
        Vector3 target = route.PointAt(checkpoints[nextCheckpoint]) - car.transform.position; target.y = 0f;
        Vector3 forward = car.transform.forward; forward.y = 0f;
        float angle = Vector3.SignedAngle(forward, target, Vector3.up);
        Vector2 pivot = new Vector2(Screen.width / 2f, 175 * s);
        Matrix4x4 saved = GUI.matrix;
        GUIUtility.RotateAroundPivot(angle, pivot);
        GUI.Label(new Rect(pivot.x - 50 * s, pivot.y - 50 * s, 100 * s, 100 * s), "↑", huge);
        GUI.matrix = saved;
    }

    private void DrawResult(float s)
    {
        MissionDefinition m = missions[current];
        float w = 820 * s, h = 520 * s;
        Rect r = new Rect((Screen.width - w) / 2f, (Screen.height - h) / 2f, w, h);
        GUI.Box(r, GUIContent.none); GUI.Box(r, GUIContent.none);
        Color old = GUI.color;
        GUI.color = passed ? new Color(0.3f, 1f, 0.4f) : new Color(1f, 0.3f, 0.25f);
        GUI.Label(new Rect(r.x, r.y + 15 * s, w, 80 * s), passed ? "PASS" : "FAIL", title);
        GUI.color = old;
        GUI.Label(new Rect(r.x, r.y + 80 * s, w, 40 * s), m.title, normal);
        GUI.Label(new Rect(r.x + 40 * s, r.y + 130 * s, w - 80 * s, 40 * s), resultReason, normal);
        int score = judge != null ? judge.Score : 100;
        GUI.Label(new Rect(r.x + 40 * s, r.y + 175 * s, w, 40 * s),
            "Time " + Clock(finishTime) + " (limit " + Clock(timeLimit) + ")      Score " + score + " / 100  (pass " + m.passScore + ")", normal);

        // The fines from this drive.
        if (judge != null)
        {
            int shown = 0;
            for (int i = judge.violations.Count - 1; i >= 0 && shown < 6; i--, shown++)
                GUI.Label(new Rect(r.x + 40 * s, r.y + (225 + shown * 32) * s, w - 80 * s, 32 * s), "-" + judge.violations[i].points + "  " + judge.violations[i].message, small);
            if (judge.violations.Count == 0)
                GUI.Label(new Rect(r.x + 40 * s, r.y + 225 * s, w, 32 * s), "No fines at all. Perfect driving!", small);
        }
        GUI.Label(new Rect(r.x, r.yMax - 55 * s, w, 40 * s), "R = try again      M = missions      Esc = drive freely", small);
    }

    private static string Clock(float seconds)
    {
        if (seconds == float.MaxValue || seconds < 0f) return "-";
        int t = Mathf.FloorToInt(seconds);
        return (t / 60) + ":" + (t % 60).ToString("00");
    }

    private void MakeStyles()
    {
        huge = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
        huge.normal.textColor = new Color(1f, 0.85f, 0.1f);
        title = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
        title.normal.textColor = Color.white;
        big = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
        big.normal.textColor = Color.white;
        bigLeft = new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold };
        bigLeft.normal.textColor = Color.white;
        normal = new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold, wordWrap = true };
        normal.normal.textColor = Color.white;
        small = new GUIStyle(GUI.skin.label) { wordWrap = true };
        small.normal.textColor = new Color(0.9f, 0.9f, 0.9f);
    }
}
