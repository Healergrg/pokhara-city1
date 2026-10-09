// =============================================================================
//  PerformanceTuner.cs  —  keeps the game smooth on your Mac
// =============================================================================
//  "FPS" = how many pictures per second the game draws. 60 feels smooth,
//  30 is OK, under 25 feels choppy.
//
//  The biggest cost in our city is SHADOWS: 9,000 trees and hundreds of
//  wires each throw a shadow. On Low graphics we switch those shadows off
//  (the buildings and cars keep theirs). Like turning off the lights in
//  rooms nobody is using.
// =============================================================================

using UnityEngine;
using UnityEngine.Rendering;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

public static class PerformanceTuner
{
    public static void Apply(int graphics)
    {
        // Draw in step with the screen (no tearing, no wasted frames).
        QualitySettings.vSyncCount = 1;
        Application.targetFrameRate = 60;

        bool cheapShadows = graphics == 0;
        foreach (MeshRenderer r in Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None))
        {
            string n = r.gameObject.name;
            if (n.StartsWith("Trees") || n == "Electric wires" || n == "Lake railing")
                r.shadowCastingMode = cheapShadows || n == "Electric wires" ? ShadowCastingMode.Off : ShadowCastingMode.On;
        }
    }
}
