using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(SteeringBehavior))]
public class EnemyZeroGWaypointPatrol : MonoBehaviour
{
    public float waypointThreshold = 1f;
    public bool loop = true;
    public bool randomNext = false;

    private SteeringBehavior steeringBehavior;
    private Waypoint currentWaypoint;
    private Waypoint nextWaypoint;
    private bool isWaiting = false;

    void Start()
    {
        steeringBehavior = GetComponent<SteeringBehavior>();

        // Find nearest waypoint at startup
        JumpToClosestWaypoint();
    }

    void Update()
    {
        if (isWaiting || nextWaypoint == null)
            return;

        // Check if reached waypoint threshold
        float distanceToWaypoint = Vector3.Distance(transform.position, nextWaypoint.transform.position);

        if (distanceToWaypoint < waypointThreshold)
        {
            StartCoroutine(HandleArrival(nextWaypoint));
        }
    }

    IEnumerator HandleArrival(Waypoint wp)
    {
        isWaiting = true;
        currentWaypoint = wp;

        if (wp.waitTime > 0f)
        {
            yield return new WaitForSeconds(wp.waitTime);
        }

        PickNextWaypoint();
        isWaiting = false;
    }

    void PickNextWaypoint()
    {
        if (currentWaypoint == null || currentWaypoint.connectedWaypoints.Count == 0)
        {
            nextWaypoint = null;
            return;
        }

        if (randomNext)
        {
            int rand = Random.Range(0, currentWaypoint.connectedWaypoints.Count);
            nextWaypoint = currentWaypoint.connectedWaypoints[rand];
        }
        else
        {
            nextWaypoint = currentWaypoint.connectedWaypoints[0];
        }

        // Update SteeringBehavior target
        if (nextWaypoint != null && steeringBehavior != null)
        {
            steeringBehavior.SetTarget(nextWaypoint.transform);
        }
    }

    /// <summary>
    /// Re-syncs the patrol to the closest waypoint to the enemy's current position
    /// and updates the SteeringBehavior target accordingly.
    /// </summary>
    public void JumpToClosestWaypoint()
    {
        // Find all waypoints in the scene
        Waypoint[] all = FindObjectsByType<Waypoint>(FindObjectsSortMode.None);

        if (all == null || all.Length == 0)
        {
            Debug.LogWarning($"{name}: No waypoints found in the scene for patrol.");
            currentWaypoint = null;
            nextWaypoint = null;
            return;
        }

        float minDist = Mathf.Infinity;
        Waypoint closest = null;
        Vector3 pos = transform.position;

        foreach (var wp in all)
        {
            if (wp == null) continue;

            float dist = Vector3.Distance(pos, wp.transform.position);
            if (dist < minDist)
            {
                minDist = dist;
                closest = wp;
            }
        }

        if (closest != null)
        {
            currentWaypoint = closest;
            isWaiting = false;       // make sure we're not stuck in a waiting state
            PickNextWaypoint();      // this will also set the steering target
            Debug.Log($"{name} JumpToClosestWaypoint → {closest.name}");
        }
        else
        {
            Debug.LogWarning($"{name}: Could not determine closest waypoint.");
            nextWaypoint = null;
        }
    }
}
