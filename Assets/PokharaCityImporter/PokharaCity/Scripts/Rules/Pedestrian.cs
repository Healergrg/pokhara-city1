// =============================================================================
//  Pedestrian.cs  —  a label on the person who walks over the zebra crossing
// =============================================================================
//  It doesn't do anything by itself. It is like a name badge: when your car
//  bumps into something, the Road Rules Judge looks for this badge to know
//  "that was a person, not a wall" (a much bigger fine!).
//
//  (Unity rule: every script needs its OWN file with the same name as the
//  class, so this lives here and not inside ZebraCrossing.cs.)
// =============================================================================

using UnityEngine;

public class Pedestrian : MonoBehaviour
{
    public ZebraCrossing crossing;
}
