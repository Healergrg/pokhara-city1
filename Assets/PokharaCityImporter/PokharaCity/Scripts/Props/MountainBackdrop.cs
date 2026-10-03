// =============================================================================
//  MountainBackdrop.cs  —  keeps Machhapuchhre in the sky wherever you drive
// =============================================================================
//  The mountain model is only about 2.5-3 km away (Unity cannot see the real
//  30-70 km). If it stood still, you would see it get closer as you drive
//  north, which looks wrong. So every frame we slide it to sit around the
//  camera. It never gets closer, exactly like the real Himalaya from a car
//  window (or the moon following you at night).
//
//  The props builder adds this for you. Nothing to set up.
// =============================================================================

using UnityEngine;

public class MountainBackdrop : MonoBehaviour
{
    [Tooltip("Leave empty to follow the Main Camera.")]
    public Transform follow;

    [Tooltip("The camera must see this far, or the mountains get cut off.")]
    public float neededViewDistance = 3200f;

    private void Start()
    {
        Camera cam = Camera.main;
        if (cam != null && cam.farClipPlane < neededViewDistance) cam.farClipPlane = neededViewDistance;
    }

    // LateUpdate = after the car and camera have moved this frame.
    private void LateUpdate()
    {
        Transform target = follow;
        if (target == null && Camera.main != null) target = Camera.main.transform;
        if (target == null) return;

        // Follow left/right and forward/back, but stay on the ground (y = 0).
        Vector3 p = target.position;
        transform.position = new Vector3(p.x, 0f, p.z);
    }
}
