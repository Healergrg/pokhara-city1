// =============================================================================
//  ZebraCrossing.cs  —  a zebra crossing with a person who walks across
// =============================================================================
//  Every so often the pedestrian steps onto the crossing and walks to the
//  other side. While they are on the road, you MUST stop before the
//  stripes. Driving over the crossing while someone is on it is a big fine,
//  and hitting them is the biggest fine of all.
//
//  To be fair, the pedestrian only starts walking when your car is still far
//  away or stopped, so you always have time to brake.
// =============================================================================

using UnityEngine;

public class ZebraCrossing : MonoBehaviour
{
    [Tooltip("The direction the ROAD goes. People walk across it.")]
    public Vector3 roadDirection = new Vector3(0f, 0f, 1f);
    public float roadHalfWidth = 4.5f;
    public Transform pedestrian;

    [Header("Pedestrian")]
    public float walkSpeed = 1.3f;          // metres per second (normal walking)
    public float minWait = 6f, maxWait = 18f;

    private enum State { Waiting, Walking, Knocked }
    private State state = State.Waiting;
    private float timer;
    private int side = -1;                  // which kerb the person stands on: -1 or +1
    private Transform playerCar;

    public bool PedestrianOnRoad { get; private set; }

    private Vector3 Across => new Vector3(roadDirection.z, 0f, -roadDirection.x).normalized;
    private Vector3 Kerb(int s) => transform.position + Across * (s * (roadHalfWidth + 1.2f));

    private void Start()
    {
        timer = Random.Range(minWait, maxWait);
        if (pedestrian != null) PlacePedestrian(Kerb(side), Across * -side);
        PokharaCar car = FindFirstObjectByType<PokharaCar>();
        if (car != null) playerCar = car.transform;
    }

    // Is a point (the car) on the striped area? (with a little extra in front)
    public bool IsOnCrossing(Vector3 point, float extra)
    {
        Vector3 d = point - transform.position; d.y = 0f;
        float along = Mathf.Abs(Vector3.Dot(d, roadDirection.normalized));
        float across = Mathf.Abs(Vector3.Dot(d, Across));
        return along < 1.5f + extra && across < roadHalfWidth;
    }

    private void Update()
    {
        if (pedestrian == null) return;
        switch (state)
        {
            case State.Waiting:
                PedestrianOnRoad = false;
                timer -= Time.deltaTime;
                if (timer <= 0f && CarGivesTime())
                {
                    state = State.Walking;
                    PedestrianOnRoad = true;
                }
                break;

            case State.Walking:
                Vector3 target = Kerb(-side);
                Vector3 step = Vector3.MoveTowards(pedestrian.position, target, walkSpeed * Time.deltaTime);
                // A little bounce while walking, like real steps.
                float bob = Mathf.Abs(Mathf.Sin(Time.time * 8f)) * 0.05f;
                PlacePedestrian(new Vector3(step.x, transform.position.y + bob, step.z), Across * side * -1f);
                if ((new Vector3(step.x, 0f, step.z) - new Vector3(target.x, 0f, target.z)).magnitude < 0.05f)
                {
                    side = -side;    // now on the other kerb
                    state = State.Waiting;
                    timer = Random.Range(minWait, maxWait);
                    PedestrianOnRoad = false;
                }
                else
                {
                    // Still counts as "on the road" until they reach the kerb.
                    Vector3 fromCentre = pedestrian.position - transform.position; fromCentre.y = 0f;
                    PedestrianOnRoad = Mathf.Abs(Vector3.Dot(fromCentre, Across)) < roadHalfWidth + 0.3f;
                }
                break;

            case State.Knocked:
                PedestrianOnRoad = false;
                timer -= Time.deltaTime;
                if (timer <= 0f)
                {
                    // Gets up again (it's a game!) and goes back to the kerb.
                    Collider c = pedestrian.GetComponent<Collider>();
                    if (c != null) c.enabled = true;
                    PlacePedestrian(Kerb(side), Across * -side);
                    state = State.Waiting;
                    timer = Random.Range(minWait, maxWait);
                }
                break;
        }
    }

    // Only start crossing if the player's car is far away or stopped,
    // and no traffic is about to drive over the stripes.
    private bool CarGivesTime()
    {
        if (playerCar != null)
        {
            float distance = Vector3.Distance(playerCar.position, transform.position);
            Rigidbody body = playerCar.GetComponent<Rigidbody>();
            float speed = body != null ? body.linearVelocity.magnitude : 0f;
            if (distance < 35f && speed > 1f) return false;
        }
        TrafficManager traffic = TrafficManager.Instance;
        if (traffic != null)
        {
            foreach (TrafficCar v in traffic.vehicles)
            {
                if (v == null || v.Speed < 1f) continue;
                Vector3 d = v.Position - transform.position; d.y = 0f;
                if (d.magnitude < 30f) return false;   // a vehicle is coming: wait
            }
        }
        return true;
    }

    // Called by the judge when the car hits the pedestrian: they fall over.
    public void KnockDown()
    {
        if (pedestrian == null || state == State.Knocked) return;
        state = State.Knocked;
        timer = 4f;
        Collider c = pedestrian.GetComponent<Collider>();
        if (c != null) c.enabled = false;
        pedestrian.rotation = Quaternion.LookRotation(Vector3.up, Across) ;
        pedestrian.position = new Vector3(pedestrian.position.x, transform.position.y + 0.3f, pedestrian.position.z);
    }

    private void PlacePedestrian(Vector3 position, Vector3 facing)
    {
        pedestrian.position = position;
        if (facing.sqrMagnitude > 0.01f) pedestrian.rotation = Quaternion.LookRotation(facing, Vector3.up);
    }

    private void OnDrawGizmos()
    {
        Gizmos.color = PedestrianOnRoad ? Color.red : Color.white;
        Gizmos.DrawLine(Kerb(-1) + Vector3.up * 0.2f, Kerb(1) + Vector3.up * 0.2f);
    }
}

// Put on the pedestrian, so the judge knows "that was a person, not a wall".
public class Pedestrian : MonoBehaviour
{
    public ZebraCrossing crossing;
}
