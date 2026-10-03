// =============================================================================
//  CarCamera.cs  —  the camera that follows your car
// =============================================================================
//  Put this on the Main Camera (the car builder does it for you).
//  Press C to switch between:
//    1. Chase view   — behind and above the car, like a video game
//    2. Driver view  — inside, on the RIGHT seat (Nepali cars are right-hand drive)
//
//  The chase camera moves smoothly, like a person on a motorbike following
//  you: it doesn't jump when you turn, it catches up.
// =============================================================================

using UnityEngine;

public class CarCamera : MonoBehaviour
{
    public Transform target;   // the car

    [Header("Chase view")]
    public float distance = 6.5f;
    public float height = 2.4f;
    public float followSmoothTime = 0.15f;
    public float turnSmoothing = 6f;

    [Header("Driver view (position inside the car)")]
    public Vector3 driverSeat = new Vector3(0.35f, 1.25f, -0.1f);   // +x = right side of the car

    private bool driverView;
    private Vector3 velocity;   // used by SmoothDamp to remember how fast the camera moves

    private void LateUpdate()   // LateUpdate = after the car has moved this frame
    {
        if (target == null) return;
        if (DriveInput.SwitchCamera()) driverView = !driverView;

        if (driverView)
        {
            // Sit in the car: exactly follow its position and direction.
            transform.position = target.TransformPoint(driverSeat);
            transform.rotation = target.rotation;
            return;
        }

        // Chase view: aim for a spot behind the car, then glide there.
        Vector3 flatForward = target.forward;
        flatForward.y = 0f;
        if (flatForward.sqrMagnitude < 0.01f) flatForward = Vector3.forward;
        flatForward.Normalize();

        Vector3 wanted = target.position - flatForward * distance + Vector3.up * height;
        transform.position = Vector3.SmoothDamp(transform.position, wanted, ref velocity, followSmoothTime);

        Quaternion look = Quaternion.LookRotation(target.position + Vector3.up * 1.2f - transform.position);
        transform.rotation = Quaternion.Slerp(transform.rotation, look, turnSmoothing * Time.deltaTime);
    }
}
