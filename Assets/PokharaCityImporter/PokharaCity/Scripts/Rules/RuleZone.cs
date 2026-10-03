// =============================================================================
//  RuleZone.cs  —  a circle on the map with extra rules inside it
// =============================================================================
//  School zone : slow down to 20 km/h, and no horn (children!)
//  Silent zone : no horn (hospitals and clinics: patients are resting)
//
//  Select one in the Scene view to see its circle (yellow = school,
//  blue = silent zone).
// =============================================================================

using UnityEngine;

public class RuleZone : MonoBehaviour
{
    public string placeName = "School";
    public bool isSchoolZone = true;
    public float radius = 70f;
    public float schoolSpeedLimitKmh = 20f;

    public bool NoHorn => true;   // both kinds of zone are no-horn zones
    public float SpeedLimitKmh => isSchoolZone ? schoolSpeedLimitKmh : 0f;   // 0 = no extra limit

    public bool Contains(Vector3 point)
    {
        Vector3 d = point - transform.position; d.y = 0f;
        return d.sqrMagnitude < radius * radius;
    }

    public string Label => isSchoolZone ? "School zone: " + placeName : "Silent zone: " + placeName;

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = isSchoolZone ? Color.yellow : Color.cyan;
        const int sides = 48;
        for (int i = 0; i < sides; i++)
        {
            float a = i * Mathf.PI * 2f / sides, b = (i + 1) * Mathf.PI * 2f / sides;
            Gizmos.DrawLine(transform.position + new Vector3(Mathf.Cos(a) * radius, 0.5f, Mathf.Sin(a) * radius),
                            transform.position + new Vector3(Mathf.Cos(b) * radius, 0.5f, Mathf.Sin(b) * radius));
        }
    }
}
