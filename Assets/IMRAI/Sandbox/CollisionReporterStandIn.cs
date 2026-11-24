using UnityEngine;

// SANDBOX STAND-IN until AJ shares CollisionReporter.cs (Getting Started sheet: ask AJ, don't write
// your own for the apps). Put it on the CarProxy (kinematic Rigidbody + Box Collider). It reacts only
// to objects tagged Obstacle and logs the object's name, which the loader sets to <asset>_<uid>.
public class CollisionReporterStandIn : MonoBehaviour
{
    public string lastHit;

    void OnTriggerEnter(Collider other)
    {
        if (!other.CompareTag("Obstacle")) return;
        lastHit = other.gameObject.name;
        Debug.Log($"COLLISION {name} entered {other.gameObject.name} at {transform.position}");
    }

    void OnTriggerExit(Collider other)
    {
        if (other.CompareTag("Obstacle")) Debug.Log($"COLLISION {name} left {other.gameObject.name}");
    }
}
