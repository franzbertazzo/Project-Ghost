using UnityEngine;

public class Aim : MonoBehaviour
{
    public Camera playerCamera;
    public float maxShootDistance = 100f;

    public Transform playerPosition; // Reference to PlayerPosition script for potential future use

    public GameObject aimIndicatorPrefab;

    public float buffer = 0.5f; // Buffer distance to prevent aiming too close to the player
    public float speed = 5f; // Speed of the aim indicator movement

    private Vector3 targetPoint;

    void FixedUpdate()
    {
        Ray ray = playerCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f));

        if (Physics.Raycast(ray, out RaycastHit hit, maxShootDistance) && 
        (Vector3.Distance(playerCamera.transform.position, hit.point) > 
        Vector3.Distance(playerCamera.transform.position, playerPosition.position) + buffer))
            targetPoint = hit.point;
        else
            targetPoint = ray.origin + ray.direction * maxShootDistance;

        // if (aimIndicatorPrefab != null)
        // {
        //     aimIndicatorPrefab.transform.position = targetPoint;
        // }
    }


    void Update()
    {
        if (aimIndicatorPrefab != null)
        {
            aimIndicatorPrefab.transform.position = Vector3.Lerp(
                aimIndicatorPrefab.transform.position,
                targetPoint,
                Time.deltaTime * speed
            );
        }
    }
}
