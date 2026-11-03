using UnityEngine;

/// <summary>
/// Positions the transform the spine rig aims at.
///
/// Cosmetic only. Three MultiAimConstraints on the spine, chest and head point at this object so the
/// character looks where you look; nothing about shooting depends on it, because
/// <c>RocketLauncher.TryGetAimRay</c> deliberately fires from the camera instead. That is worth
/// knowing before changing anything here: this file decides how the body is posed, not where shots
/// go, so it can be made stable without touching aim accuracy.
///
/// It used to raycast from the camera and place the target on whatever it hit, falling back to a
/// point 100m away when the hit was too close to the player. Two things were wrong with that, and
/// they produced the two symptoms it had - flickering in tight spaces, and the body aiming at the
/// floor while flying forward:
///
/// 1. The fallback was a *branch*. The target was either the hit point, possibly two metres away,
///    or a point a hundred metres away, with nothing in between and no hysteresis. In a confined
///    arena the test flips frame to frame and the target teleports between the two.
/// 2. The smoothing was applied to a *world-space position*. Easing a world point toward a target
///    while the player translates at 30 m/s leaves the point behind in the world, so the spine
///    points at where you were rather than where you are looking.
///
/// So the direction is now always exactly the camera's forward - it cannot lag or flicker, because
/// it is not interpolated at all - and the only smoothed quantity is a clamped scalar distance
/// along that ray. A scalar cannot jump between two places, and shortening it does not drag the
/// aim behind the player.
///
/// Left in the global namespace on purpose: the prefab's serialized reference is to this file's
/// script asset, and moving a MonoBehaviour into a namespace is churn with nothing to gain here.
/// </summary>
public class Aim : MonoBehaviour
{
    public Camera playerCamera;

    [Tooltip("How far the raycast looks for a surface. Only used when surfaceAware is on.")]
    public float maxShootDistance = 100f;

    [Tooltip("The player's own transform, used to keep the aim point in front of the body - the " +
             "camera is behind the character, so a ray from it passes through them first.")]
    public Transform playerPosition;

    public GameObject aimIndicatorPrefab;

    [Tooltip("Clearance past the player that the aim point is never allowed inside.")]
    public float buffer = 0.5f;

    [Tooltip("Where the aim point sits along the camera's forward when nothing is in the way. " +
             "Far enough that the spine reads as 'looking ahead' rather than at a spot.")]
    public float aimDistance = 30f;

    [Tooltip("Let nearby surfaces pull the aim point closer, so the character glances at a wall " +
             "they are about to hit. Off by default: it is the only thing here that can introduce " +
             "a discontinuity, and the weapon fires along the camera ray regardless.")]
    public bool surfaceAware;

    [Tooltip("Layers that count as a surface. Only used when surfaceAware is on.")]
    public LayerMask surfaceMask = ~0;

    [Tooltip("How fast the distance eases, as an exponential rate. Higher is snappier; 0 snaps.")]
    public float smoothing = 10f;

    private float _distance;

    private void Awake()
    {
        // Started at rest rather than zero, or the first frame eases the aim point out from inside
        // the camera.
        _distance = aimDistance;
    }

    /// <summary>
    /// LateUpdate, because the camera rigs move in LateUpdate and this reads the camera.
    ///
    /// The Animator is in Normal mode, so the rig evaluates between Update and LateUpdate - which
    /// means the constraint always reads a target computed from the previous frame's camera, no
    /// matter where this runs. One frame on a cosmetic pose is invisible, and doing it here at least
    /// leaves the object in the right place for anything that renders it this frame.
    /// </summary>
    private void LateUpdate()
    {
        if (playerCamera == null || aimIndicatorPrefab == null)
            return;

        var cam = playerCamera.transform;
        var origin = cam.position;
        var forward = cam.forward;

        float target = surfaceAware ? SurfaceDistance(origin, forward) : aimDistance;

        // Exponential, so the easing is the same at 60fps and 144fps. The old `dt * speed` was
        // used directly as a lerp factor, which is frame-rate dependent by construction.
        _distance = smoothing > 0f
            ? Mathf.Lerp(_distance, target, 1f - Mathf.Exp(-smoothing * Time.deltaTime))
            : target;

        aimIndicatorPrefab.transform.position = origin + forward * _distance;
    }

    /// <summary>
    /// How far along the ray the aim point should sit, clamped rather than branched.
    ///
    /// Every outcome lands in [minimum, aimDistance], and a miss returns the same value as a hit
    /// beyond aimDistance - so the far end of the range is continuous and there is no pair of
    /// values for the old flicker to alternate between. A hit closer than the minimum is clamped
    /// up instead of discarded, which is also what handles the ray passing through the player's
    /// own body on the way out of a third-person camera.
    /// </summary>
    private float SurfaceDistance(Vector3 origin, Vector3 forward)
    {
        float minimum = playerPosition != null
            ? Vector3.Distance(origin, playerPosition.position) + buffer
            : buffer;

        minimum = Mathf.Min(minimum, aimDistance);

        // Triggers ignored deliberately: the blast zone is a volume around the whole arena, and
        // aiming at its shell would be meaningless.
        if (!Physics.Raycast(origin, forward, out var hit, maxShootDistance, surfaceMask,
                QueryTriggerInteraction.Ignore))
            return aimDistance;

        return Mathf.Clamp(hit.distance, minimum, aimDistance);
    }
}
