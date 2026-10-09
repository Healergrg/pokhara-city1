// =============================================================================
//  GameSettings.cs  —  the player's choices, saved on the Mac
// =============================================================================
//  Like the settings on a phone: they are remembered after you quit
//  (Unity's PlayerPrefs = a small notebook the game keeps on your computer).
//
//  The Settings menu changes these, then calls Apply() so the game reacts
//  straight away (less traffic, lower graphics, quieter sound, ...).
// =============================================================================

using UnityEngine;

public static class GameSettings
{
    // ---- the choices (with their defaults) -----------------------------------
    public static int traffic = 2;          // 0 Off, 1 Light, 2 Normal, 3 Busy
    public static int graphics = 2;         // 0 Low, 1 Medium, 2 High
    public static float volume = 0.8f;      // 0 = silent, 1 = full
    public static bool minimap = true;
    public static bool controlsHelp = true;
    public static bool routeLine = true;

    public static readonly string[] TrafficNames = { "Off", "Light", "Normal", "Busy" };
    public static readonly int[] TrafficCars = { 0, 15, 40, 60 };
    public static readonly string[] GraphicsNames = { "Low", "Medium", "High" };

    private const string Key = "PokharaSettings.";
    private static bool loaded;

    public static void Load()
    {
        traffic = PlayerPrefs.GetInt(Key + "traffic", traffic);
        graphics = PlayerPrefs.GetInt(Key + "graphics", graphics);
        volume = PlayerPrefs.GetFloat(Key + "volume", volume);
        minimap = PlayerPrefs.GetInt(Key + "minimap", minimap ? 1 : 0) == 1;
        controlsHelp = PlayerPrefs.GetInt(Key + "help", controlsHelp ? 1 : 0) == 1;
        routeLine = PlayerPrefs.GetInt(Key + "route", routeLine ? 1 : 0) == 1;
        loaded = true;
    }

    public static void Save()
    {
        PlayerPrefs.SetInt(Key + "traffic", traffic);
        PlayerPrefs.SetInt(Key + "graphics", graphics);
        PlayerPrefs.SetFloat(Key + "volume", volume);
        PlayerPrefs.SetInt(Key + "minimap", minimap ? 1 : 0);
        PlayerPrefs.SetInt(Key + "help", controlsHelp ? 1 : 0);
        PlayerPrefs.SetInt(Key + "route", routeLine ? 1 : 0);
        PlayerPrefs.Save();
    }

    // Make the game match the settings right now.
    public static void Apply()
    {
        if (!loaded) Load();

        // Sound: the "AudioListener" is the game's ears (on the camera).
        AudioListener.volume = volume;

        // Graphics: Unity has quality levels (Project Settings > Quality).
        // We map Low / Medium / High onto the first, middle and last level.
        int levels = QualitySettings.names.Length;
        if (levels > 0)
        {
            int level = graphics == 0 ? 0 : graphics == 1 ? levels / 2 : levels - 1;
            if (QualitySettings.GetQualityLevel() != level) QualitySettings.SetQualityLevel(level, true);
        }

        // Traffic: change the maximum, and remove extra cars if it went down.
        TrafficManager tm = TrafficManager.Instance;
        if (tm == null) tm = Object.FindFirstObjectByType<TrafficManager>();
        if (tm != null)
        {
            tm.maxVehicles = TrafficCars[Mathf.Clamp(traffic, 0, TrafficCars.Length - 1)];
            while (tm.vehicles.Count > tm.maxVehicles)
            {
                TrafficCar v = tm.vehicles[tm.vehicles.Count - 1];
                tm.vehicles.RemoveAt(tm.vehicles.Count - 1);
                if (v != null) Object.Destroy(v.gameObject);
            }
        }

        CarDashboard dash = Object.FindFirstObjectByType<CarDashboard>();
        if (dash != null) dash.showControlsHelp = controlsHelp;

        PerformanceTuner.Apply(graphics);

        MissionManager missions = Object.FindFirstObjectByType<MissionManager>();
        if (missions != null) missions.showRouteLine = routeLine;
    }
}
