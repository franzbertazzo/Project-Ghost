using PurrNet;
using UnityEngine;

namespace Game.Networking
{
    /// <summary>
    /// Replicates where a player is aiming, so the spine MultiAimConstraint bends the same way on
    /// every screen.
    ///
    /// The rig aims the spine at a `Target` transform that `Aim.cs` drives by raycasting through
    /// the local camera. That works for the player who owns the camera and for nobody else: remote
    /// players have no camera (it is switched off with the rest of the CameraRig), so their Target
    /// never moves and the constraint twists the spine toward a stale point.
    ///
    /// Aim direction is not cosmetic in this game. Where someone is pointing is the telegraph that
    /// says a rocket is coming, so it is worth the bandwidth - unlike muzzle flashes and particles,
    /// which CLAUDE.md keeps local.
    ///
    /// Owner authority: the aiming player is the only one who knows where they are looking, and a
    /// wrong aim pose is a visual artefact rather than an exploit. When rockets arrive in P3 the
    /// host resolves the shot itself and does not trust this value for hit detection.
    /// </summary>
    public class NetworkAim : NetworkBehaviour
    {
        [Header("Wiring")]
        [Tooltip("The transform the spine's MultiAimConstraint aims at. Found by name if unset.")]
        [SerializeField] private Transform aimTarget;

        [SerializeField] private string aimTargetName = "Target";

        [Header("Smoothing")]
        [Tooltip("How fast a remote player's aim catches up. Too high looks jittery under packet " +
                 "loss, too low makes them look like they are aiming behind you.")]
        [SerializeField] private float remoteFollowSpeed = 18f;

        /// <summary>
        /// The world point being aimed at. Sent a few times per second rather than every tick:
        /// the constraint is smoothed on arrival, so a faster rate spends bandwidth on detail
        /// nobody can see.
        /// </summary>
        /// Deliberately not [SerializeField]: Unity would then persist the constructor arguments
        /// into the prefab, and a later edit to them here would be silently ignored in favour of
        /// the stale serialized copy - including ownerAuth, whose default is false.
        /// Not readonly either, because PurrNet's codegen assigns these fields.
        private SyncVar<Vector3> _aimPoint = new(default, 0.05f, ownerAuth: true);

        private void Awake()
        {
            ResolveAimTarget();
        }

        private void Update()
        {
            if (aimTarget == null)
                return;

            if (isOwner)
            {
                // Aim.cs already moved the target this frame; publish where it landed.
                _aimPoint.value = aimTarget.position;
                return;
            }

            if (!isSpawned)
                return;

            // Remote player: drive the target from the replicated point. Smoothed rather than
            // snapped, because the send interval is coarser than the frame rate.
            aimTarget.position = Vector3.Lerp(
                aimTarget.position,
                _aimPoint.value,
                1f - Mathf.Exp(-remoteFollowSpeed * Time.deltaTime));
        }

        private void ResolveAimTarget()
        {
            if (aimTarget != null)
                return;

            var aim = GetComponentInChildren<Aim>(true);
            if (aim != null && aim.aimIndicatorPrefab != null)
            {
                aimTarget = aim.aimIndicatorPrefab.transform;
                return;
            }

            var found = FindChildByName(transform, aimTargetName);
            if (found != null)
            {
                aimTarget = found;
                return;
            }

            Debug.LogWarning($"[NetworkAim] No aim target found on {name}. " +
                             "Remote players will show a broken spine.");
        }

        private static Transform FindChildByName(Transform root, string childName)
        {
            foreach (var child in root.GetComponentsInChildren<Transform>(true))
            {
                if (child.name == childName)
                    return child;
            }

            return null;
        }
    }
}
