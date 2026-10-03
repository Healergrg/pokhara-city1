// =============================================================================
//  DriveInput.cs  —  reads the keyboard for driving
// =============================================================================
//  Unity has two ways to read the keyboard: the NEW Input System (the default
//  in Unity 6) and the OLD Input class. This file works with both, so the car
//  never breaks because of a project setting. Think of it as a plug adapter:
//  the car asks "is the gas pressed?" and this file figures out how to check.
//
//  Controls:
//    W / Up arrow ......... accelerate
//    S / Down arrow ....... brake, then reverse when stopped
//    A / D ................ steer left / right
//    Space ................ handbrake
//    Q / E ................ left / right indicator (press again to switch off)
//    H .................... horn
//    C .................... change camera
// =============================================================================

using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

public static class DriveInput
{
    // 1 = full gas, 0 = no gas
    public static float Throttle()
    {
        return Held(KeyName.W) || Held(KeyName.Up) ? 1f : 0f;
    }

    // 1 = brake / reverse pedal fully pressed
    public static float Brake()
    {
        return Held(KeyName.S) || Held(KeyName.Down) ? 1f : 0f;
    }

    // -1 = full left, +1 = full right
    public static float Steer()
    {
        float steer = 0f;
        if (Held(KeyName.A)) steer -= 1f;
        if (Held(KeyName.D)) steer += 1f;
        return steer;
    }

    public static bool Handbrake()        { return Held(KeyName.Space); }
    public static bool Horn()             { return Held(KeyName.H); }
    public static bool LeftIndicator()    { return Pressed(KeyName.Q); }
    public static bool RightIndicator()   { return Pressed(KeyName.E); }
    public static bool SwitchCamera()     { return Pressed(KeyName.C); }

    // ---- the "plug adapter" part --------------------------------------------
    public enum KeyName { W, S, A, D, Up, Down, Space, Q, E, H, C }

    // Held = the key is down right now (for pedals and steering).
    private static bool Held(KeyName key)
    {
#if ENABLE_INPUT_SYSTEM
        Keyboard k = Keyboard.current;
        return k != null && ToKey(k, key).isPressed;
#else
        return Input.GetKey(ToKeyCode(key));
#endif
    }

    // Pressed = the key went down THIS frame (for switches like indicators).
    private static bool Pressed(KeyName key)
    {
#if ENABLE_INPUT_SYSTEM
        Keyboard k = Keyboard.current;
        return k != null && ToKey(k, key).wasPressedThisFrame;
#else
        return Input.GetKeyDown(ToKeyCode(key));
#endif
    }

#if ENABLE_INPUT_SYSTEM
    private static UnityEngine.InputSystem.Controls.KeyControl ToKey(Keyboard k, KeyName key)
    {
        switch (key)
        {
            case KeyName.W: return k.wKey;
            case KeyName.S: return k.sKey;
            case KeyName.A: return k.aKey;
            case KeyName.D: return k.dKey;
            case KeyName.Up: return k.upArrowKey;
            case KeyName.Down: return k.downArrowKey;
            case KeyName.Space: return k.spaceKey;
            case KeyName.Q: return k.qKey;
            case KeyName.E: return k.eKey;
            case KeyName.H: return k.hKey;
            default: return k.cKey;
        }
    }
#else
    private static KeyCode ToKeyCode(KeyName key)
    {
        switch (key)
        {
            case KeyName.W: return KeyCode.W;
            case KeyName.S: return KeyCode.S;
            case KeyName.A: return KeyCode.A;
            case KeyName.D: return KeyCode.D;
            case KeyName.Up: return KeyCode.UpArrow;
            case KeyName.Down: return KeyCode.DownArrow;
            case KeyName.Space: return KeyCode.Space;
            case KeyName.Q: return KeyCode.Q;
            case KeyName.E: return KeyCode.E;
            case KeyName.H: return KeyCode.H;
            default: return KeyCode.C;
        }
    }
#endif
}
