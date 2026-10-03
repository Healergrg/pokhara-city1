// =============================================================================
//  CarRuleSensor.cs  —  tells the judge when the car bumps into something
// =============================================================================
//  Unity only sends "you hit something" messages (OnCollisionEnter) to the
//  object that has the Rigidbody, which is the car. So this tiny script sits
//  on the car and passes the message on, like a doorbell wired to the judge.
//  The judge adds it to the car for you when you press Play.
// =============================================================================

using UnityEngine;

public class CarRuleSensor : MonoBehaviour
{
    public RoadRulesJudge judge;

    private void OnCollisionEnter(Collision collision)
    {
        if (judge != null) judge.ReportCollision(collision);
    }
}
