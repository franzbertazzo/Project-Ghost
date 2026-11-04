using PurrNet;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Game.Networking
{
    /// <summary>
    /// Owner gating for a spawned player.
    ///
    /// The single most common first-day multiplayer bug is that every spawned player runs its own
    /// camera, audio listener and input handler, so all four instances respond to one person's
    /// mouse and Unity warns about multiple AudioListeners. This turns all of that off on anyone
    /// who is not the local owner.
    ///
    /// It finds what it needs by type rather than by inspector reference: the prefab is deep
    /// (armature, FX, camera rig) and hand-wired references break silently when it is reorganised.
    /// Everything found is logged once on spawn so the gating is visible rather than assumed.
    ///
    /// Lives on the PlayerEntities root, which owns both the CameraRig and the Player body.
    /// </summary>
    public class NetworkPlayer : NetworkBehaviour
    {
        [Header("Local-only branches")]
        [Tooltip("Disabled wholesale on remote players. Holds the camera, audio listener and post " +
                 "process volumes, none of which a remote player should own.")]
        [SerializeField] private GameObject cameraRig;

        [Header("Debug")]
        [SerializeField] private bool logGating = true;

        private bool _gated;

        protected override void OnSpawned()
        {
            base.OnSpawned();
            ApplyOwnerGating();
        }

        /// <summary>
        /// Ownership can arrive after the spawn callback, so re-apply rather than trusting the
        /// first call to have seen the final answer.
        /// </summary>
        protected override void OnOwnerChanged(PlayerID? oldOwner, PlayerID? newOwner, bool asServer)
        {
            base.OnOwnerChanged(oldOwner, newOwner, asServer);
            _gated = false;
            ApplyOwnerGating();
        }

        /// <summary>
        /// Re-runs the gating after something about this body has changed.
        ///
        /// Needed because the gating caches its answer, and a bot's brain is attached *after* the
        /// spawn - so the body would otherwise stay switched off as an unowned remote and drift.
        /// </summary>
        public void RefreshGating()
        {
            _gated = false;
            ApplyOwnerGating();
        }

        private void ApplyOwnerGating()
        {
            if (_gated)
                return;

            _gated = true;
            bool mine = isOwner;

            // A bot's body has no owner, so every gate below would switch it off everywhere - the
            // movement included, which would leave it drifting. The host drives it instead, so on
            // the host it needs the parts a local player needs to *move*, and on no peer does it
            // need the parts a local player needs to *see*: no camera rig, no radar, no device
            // input. Those two lists differ, which is why this is not simply "treat bots as mine".
            var bot = GetComponentInChildren<Game.Bots.BotController>(true);
            bool botDriven = bot != null && isServer;
            bool drives = mine || botDriven;

            name = botDriven
                ? $"PlayerEntities [bot {bot.Profile?.displayName ?? "unnamed"}]"
                : mine
                    ? "PlayerEntities [LOCAL]"
                    : $"PlayerEntities [remote {owner}]";

            if (bot != null)
                bot.enabled = botDriven;

            var rig = ResolveCameraRig();
            if (rig != null)
                rig.SetActive(mine);

            // Input. PlayerInput drives PlayerInputHandler through UnityEvents, and PlayerShooter
            // builds its own action asset, so all three have to be silenced independently.
            SetEnabled(GetComponentInChildren<PlayerInput>(true), mine);
            // The one input the bot needs: the handler is the seam it writes into, so it has to run
            // on the host. PlayerInput above stays off, which is what stops the host's own keyboard
            // driving its bots as well as its player.
            SetEnabled(GetComponentInChildren<PlayerInputHandler>(true), drives);
            SetEnabled(GetComponentInChildren<PlayerShooter>(true), mine);

            // The radar is a first-person aid; a remote player's copy would track the wrong things.
            SetEnabled(GetComponentInChildren<RingRadarController>(true), mine);

            // Aim raycasts through the local camera, which remote players do not have. Left
            // running it would drive the spine constraint from a dead camera. NetworkAim takes
            // over on remotes and drives the same target from the replicated aim point.
            SetEnabled(GetComponentInChildren<Aim>(true), mine);

            // Remote bodies are positioned by NetworkTransform, which puts their Rigidbody to
            // sleep. The movement script writes rb.linearVelocity every FixedUpdate, which wakes
            // it again and then brakes it toward zero - fighting the very transform stream that
            // is meant to be driving it.
            //
            // Trade-off: this also freezes the booster VFX on remote players, since UpdateBoosters
            // lives in the same script. Cheap to fix later by driving boosters from the synced
            // velocity; not worth leaving a physics fight in place for.
            SetEnabled(GetComponentInChildren<PlayerZeroGMovement>(true), drives);

            // Animation state is replicated by NetworkAnimator, so the remote copies of the
            // controller must stop writing to the Animator. Left enabled they would set
            // MoveDirection from their own (dead) input every frame, overwriting the value that
            // just arrived from the owner - the animator would look synced and never move.
            foreach (var animationController in GetComponentsInChildren<PlayerAnimationStateController>(true))
                animationController.enabled = drives;

            if (logGating)
            {
                Debug.Log($"[NetworkPlayer] {(mine ? "LOCAL" : "remote")} player spawned " +
                          $"(owner {owner}); camera rig {(rig != null ? (mine ? "enabled" : "disabled") : "NOT FOUND")}.");
            }
        }

        /// <summary>
        /// Prefers the serialized reference, falls back to finding the camera and walking up to the
        /// branch directly under this root. Warns rather than failing silently, because a missed
        /// camera rig is exactly the bug this class exists to prevent.
        /// </summary>
        private GameObject ResolveCameraRig()
        {
            if (cameraRig != null)
                return cameraRig;

            var camera = GetComponentInChildren<Camera>(true);
            if (camera == null)
            {
                Debug.LogWarning($"[NetworkPlayer] No camera found under {name}. " +
                                 "Owner gating cannot disable the camera rig on remote players.");
                return null;
            }

            var branch = camera.transform;
            while (branch.parent != null && branch.parent != transform)
                branch = branch.parent;

            // The camera sits in a branch beside the body, so this resolves to CameraRig. If the
            // camera is ever parented under the body instead, disabling the branch would disable
            // the body too - refuse rather than blank the player out.
            if (branch == transform)
            {
                Debug.LogWarning($"[NetworkPlayer] Camera on {name} is not in its own branch. " +
                                 "Assign cameraRig explicitly.");
                return null;
            }

            cameraRig = branch.gameObject;
            return cameraRig;
        }

        private static void SetEnabled(Behaviour behaviour, bool enabled)
        {
            if (behaviour != null)
                behaviour.enabled = enabled;
        }
    }
}
