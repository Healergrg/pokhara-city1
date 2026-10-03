// =============================================================================
//  MissionKeys.cs  —  the keys for the mission menu (M, R, Esc, 1-9)
// =============================================================================
//  Works with both Unity input systems, like DriveInput.cs does for driving.
// =============================================================================

using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

public static class MissionKeys
{
    public static bool Menu()    { return Down('M'); }
    public static bool Restart() { return Down('R'); }
    public static bool Escape()  { return Down('\x1b'); }

    // Number keys 1-9: returns 0-8, or -1 if none was pressed this frame.
    public static int Number()
    {
        for (int i = 1; i <= 9; i++) if (Down((char)('0' + i))) return i - 1;
        return -1;
    }

    private static bool Down(char key)
    {
#if ENABLE_INPUT_SYSTEM
        Keyboard k = Keyboard.current;
        if (k == null) return false;
        switch (key)
        {
            case 'M': return k.mKey.wasPressedThisFrame;
            case 'R': return k.rKey.wasPressedThisFrame;
            case '\x1b': return k.escapeKey.wasPressedThisFrame;
            case '1': return k.digit1Key.wasPressedThisFrame;
            case '2': return k.digit2Key.wasPressedThisFrame;
            case '3': return k.digit3Key.wasPressedThisFrame;
            case '4': return k.digit4Key.wasPressedThisFrame;
            case '5': return k.digit5Key.wasPressedThisFrame;
            case '6': return k.digit6Key.wasPressedThisFrame;
            case '7': return k.digit7Key.wasPressedThisFrame;
            case '8': return k.digit8Key.wasPressedThisFrame;
            case '9': return k.digit9Key.wasPressedThisFrame;
        }
        return false;
#else
        switch (key)
        {
            case 'M': return Input.GetKeyDown(KeyCode.M);
            case 'R': return Input.GetKeyDown(KeyCode.R);
            case '\x1b': return Input.GetKeyDown(KeyCode.Escape);
            default: return Input.GetKeyDown(KeyCode.Alpha0 + (key - '0'));
        }
#endif
    }
}
