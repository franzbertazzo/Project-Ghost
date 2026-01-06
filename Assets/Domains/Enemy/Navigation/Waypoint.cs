using System.Collections.Generic;
using UnityEngine;

public class Waypoint : MonoBehaviour
{
    [Header("Connected waypoints")]
    public List<Waypoint> connectedWaypoints = new List<Waypoint>();

    [Header("Optional Behavior")]
    public float waitTime = 0f;
    public bool lookAtTarget = false;
    public Transform lookTarget;

    private void OnDrawGizmos()
    {
        Gizmos.color = Color.green;
        foreach (var wp in connectedWaypoints)
        {
            if (wp != null)
                Gizmos.DrawLine(transform.position, wp.transform.position);
        }

        Gizmos.color = Color.yellow;
        Gizmos.DrawSphere(transform.position, 0.1f);
    }
}