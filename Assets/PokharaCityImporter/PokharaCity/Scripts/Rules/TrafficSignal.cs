// =============================================================================
//  TrafficSignal.cs  —  red, amber, green at a chowk
// =============================================================================
//  Roads arriving at the junction are split into two GROUPS:
//    group 0 = roads in a straight line with the first road (e.g. north + south)
//    group 1 = the crossing roads                          (e.g. east + west)
//  The groups take turns, like two queues at a ticket counter:
//
//    group 0 GREEN 14s -> AMBER 3s -> all RED 2s -> group 1 GREEN 14s -> AMBER 3s -> all RED 2s
//
//  The Road Rules Judge asks GetLightFor(direction) when you drive over the
//  stop line. Crossing on RED is a big fine; AMBER is allowed (you were too
//  close to stop safely).
// =============================================================================

using System.Collections.Generic;
using UnityEngine;

public enum LightColour { Red, Amber, Green }

[System.Serializable]
public class SignalApproach
{
    public Vector3 directionIn;   // the way cars drive when arriving here
    public int group;
    public Renderer redLamp, amberLamp, greenLamp;
}

public class TrafficSignal : MonoBehaviour
{
    public string junctionName = "Chowk";
    [Tooltip("Distance from the middle of the junction to the white stop line (metres).")]
    public float stopRadius = 10f;

    [Header("Timing (seconds)")]
    public float greenTime = 14f;
    public float amberTime = 3f;
    public float allRedTime = 2f;
    [Tooltip("Shifts this signal's timing so all junctions don't change together.")]
    public float timeOffset = 0f;

    [Header("Lamps (filled in by the builder)")]
    public List<SignalApproach> approaches = new List<SignalApproach>();
    public Material redOn, redOff, amberOn, amberOff, greenOn, greenOff;

    private readonly LightColour[] shown = { LightColour.Green, LightColour.Red };
    private bool firstUpdate = true;

    private float CycleLength => 2f * (greenTime + amberTime + allRedTime);

    // What colour does this group see right now?
    public LightColour GetGroupLight(int group)
    {
        float t = (Time.time + timeOffset) % CycleLength;
        float half = greenTime + amberTime + allRedTime;
        // Is it this group's half of the cycle?
        bool myTurn = group == 0 ? t < half : t >= half;
        if (!myTurn) return LightColour.Red;
        float inTurn = group == 0 ? t : t - half;
        if (inTurn < greenTime) return LightColour.Green;
        if (inTurn < greenTime + amberTime) return LightColour.Amber;
        return LightColour.Red;
    }

    // Which group is a car in, if it is travelling in 'travelDirection'?
    // The approach that points most the same way wins.
    public int GroupFor(Vector3 travelDirection)
    {
        int best = 0; float bestDot = -2f;
        foreach (SignalApproach a in approaches)
        {
            float d = Vector3.Dot(a.directionIn.normalized, travelDirection.normalized);
            if (d > bestDot) { bestDot = d; best = a.group; }
        }
        return best;
    }

    public LightColour GetLightFor(Vector3 travelDirection) { return GetGroupLight(GroupFor(travelDirection)); }

    private void Update()
    {
        for (int g = 0; g < 2; g++)
        {
            LightColour now = GetGroupLight(g);
            if (now == shown[g] && !firstUpdate) continue;   // only swap paint when something changes
            shown[g] = now;
            foreach (SignalApproach a in approaches)
            {
                if (a.group != g) continue;
                SetLamp(a.redLamp, now == LightColour.Red ? redOn : redOff);
                SetLamp(a.amberLamp, now == LightColour.Amber ? amberOn : amberOff);
                SetLamp(a.greenLamp, now == LightColour.Green ? greenOn : greenOff);
            }
        }
        firstUpdate = false;
    }

    private static void SetLamp(Renderer lamp, Material material)
    {
        if (lamp != null && material != null) lamp.sharedMaterial = material;
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.red;
        foreach (SignalApproach a in approaches)
            Gizmos.DrawLine(transform.position - a.directionIn.normalized * stopRadius, transform.position);
    }
}
