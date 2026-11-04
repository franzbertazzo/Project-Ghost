using System.Collections.Generic;
using PurrNet;
using UnityEngine;

namespace Game.Networking
{
    /// <summary>
    /// Owns the player's alive/dead state and turns a death into something you can see.
    ///
    /// Death does NOT destroy the object, per the roadmap: the body has to survive so the match
    /// system can respawn it (frag mode) or leave it out until the next round (elimination), and
    /// so a reconnecting player has something to re-own.
    /// </summary>
    public class PlayerVitals : NetworkBehaviour
    {
        [Header("Death")]
        [Tooltip("Spawned locally on every client where the player died. Never networked.")]
        [SerializeField] private GameObject deathExplosionPrefab;

        [Header("Rocket hit")]
        [Tooltip("Trail attached to the body while it is flying from a rocket hit. Spawned " +
                 "locally on every peer from one networked event, per CLAUDE.md - the VFX itself " +
                 "never goes on the wire.")]
        [SerializeField] private GameObject launchTrailPrefab;

        [Tooltip("Knockback force below which no trail appears. Gates the trail on actually being " +
                 "launched rather than on being shot: the laser's knockback is 0.2, so without " +
                 "this every single bolt would puff smoke. Matches the movement script's own " +
                 "minimum launch speed, so the trail means the same thing the launch state does.")]
        [SerializeField] private float trailMinimumForce = 18f;

        [Tooltip("Seconds of trail per unit of knockback force, so a harder hit smokes longer.")]
        [SerializeField] private float trailSecondsPerForce = 0.03f;

        [SerializeField] private float maxTrailSeconds = 3f;


        /// <summary>
        /// Public so the rocket can reach it. The module is the networked state; this component is
        /// the local reaction to it.
        /// </summary>
        public VitalsModule Vitals = new();

        /// <summary>
        /// This player's chosen name, replicated. Lives here rather than on its own component so
        /// that adding names needed no change to the player prefab - this component is already on
        /// the body, already networked, and already the thing every readout looks a player up by.
        /// </summary>
        public IdentityModule Identity = new();

        private PlayerZeroGMovement _movement;
        private Rigidbody _body;
        private Renderer[] _renderers;
        private bool[] _rendererWasEnabled;
        private ParticleSystem[] _particles;
        private bool[] _particleWasEmitting;
        private Canvas[] _canvases;
        private bool[] _canvasWasEnabled;
        private Collider[] _colliders;

        /// <summary>
        /// One collider standing in for this player's body, for rollback lookups.
        ///
        /// The rollback module records history per collider, so validating a claimed hit against
        /// where this player *was* needs a specific collider to ask about. Any of them would do -
        /// they all ride the same body - so the first is taken and cached.
        /// </summary>
        public Collider HitCollider
        {
            get
            {
                if (_hitCollider != null)
                    return _hitCollider;

                foreach (var collider in GetComponentsInChildren<Collider>(true))
                {
                    if (collider == null || collider.isTrigger)
                        continue;

                    _hitCollider = collider;
                    break;
                }

                return _hitCollider;
            }
        }

        private Collider _hitCollider;
        private bool[] _colliderWasEnabled;
        private PlayerSpawner _spawner;
        private ThirdPersonCameraRig _cameraRig;

        private static readonly List<PlayerVitals> _all = new();

        /// <summary>
        /// Every player currently in the arena, local and remote.
        ///
        /// A registry rather than a physics query, because the HUD and radar need to enumerate
        /// players several times a frame and an OverlapSphere per consumer is both wasteful and
        /// fragile - it depends on collider layers staying correct, and silently finds nothing when
        /// they do not. This list is also the only place that knows a player exists but is dead,
        /// which is exactly the distinction a marker needs to draw.
        /// </summary>
        public static IReadOnlyList<PlayerVitals> All => _all;

        /// <summary>
        /// Where a marker should point: the moving body, not the root.
        ///
        /// The root transform never moves - the body is a child driven by its own NetworkTransform -
        /// so anything tracking transform.position tracks the spawn point forever.
        /// </summary>
        public Vector3 MarkerPosition
        {
            get
            {
                EnsureBody();
                return _body != null ? _body.worldCenterOfMass : transform.position;
            }
        }

        /// <summary>
        /// The transform that actually moves. Same reason as MarkerPosition, for callers that need
        /// to hand a Transform to something that will track it over time.
        /// </summary>
        public Transform BodyTransform
        {
            get
            {
                EnsureBody();
                return _movement != null ? _movement.transform : transform;
            }
        }

        /// <summary>
        /// Resolves on demand as well as in Awake, because Unity does not order Awake between
        /// components. A caller that asked first would otherwise be handed the root transform -
        /// which never moves - and would keep it for the rest of the match.
        /// </summary>
        private void EnsureBody()
        {
            if (_movement != null)
                return;

            _movement = GetComponentInChildren<PlayerZeroGMovement>(true);
            if (_movement != null && _body == null)
                _body = _movement.GetComponent<Rigidbody>();
        }

        private void Awake()
        {
            _movement = GetComponentInChildren<PlayerZeroGMovement>(true);
            if (_movement != null)
                _body = _movement.GetComponent<Rigidbody>();

        }

        // Registered on enable rather than on spawn so the entry disappears with the object no
        // matter how it ends - a despawn, a scene teardown, or a destroy that never despawned.
        private void OnEnable()
        {
            if (!_all.Contains(this))
                _all.Add(this);
        }

        private void OnDisable()
        {
            _all.Remove(this);
        }

        protected override void OnSpawned()
        {
            base.OnSpawned();
            Vitals.OnDied += HandleDied;
            Vitals.OnRespawned += HandleRespawned;
        }

        protected override void OnDespawned()
        {
            base.OnDespawned();
            Vitals.OnDied -= HandleDied;
            Vitals.OnRespawned -= HandleRespawned;
        }

        private void HandleDied(PlayerID? killer)
        {
            Debug.Log(killer.HasValue
                ? $"[Vitals] {owner} killed by {killer}."
                : $"[Vitals] {owner} died.");

            if (deathExplosionPrefab != null)
            {
                var position = _body != null ? _body.worldCenterOfMass : transform.position;
                var explosion = Instantiate(deathExplosionPrefab, position, Quaternion.identity);
                var particles = explosion.GetComponentInChildren<ParticleSystem>();
                Destroy(explosion, particles != null ? particles.main.duration + 1f : 3f);
            }

            // Before the body is hidden. The trail is an instantiated child, not one of the
            // renderers SetVisible toggles, so without this a corpse keeps streaming smoke from
            // the spawn point it gets parked on.
            StopLaunchTrail();

            SetVisible(false);
            SetSolid(false);

            // Control is cut only for the player who owns this body. Remote copies already have
            // their movement disabled by NetworkPlayer's owner gating.
            if (isOwner && _movement != null)
                _movement.enabled = false;

            // Hold the camera where it happened. Set BEFORE the corpse is parked below, or the
            // rig would follow the body to its spawn point in the same frame and the player would
            // never see their own death.
            SetDeathCamera(true);

            // Stopped on the OWNER, not the server. The body is owner-authoritative, so a
            // host-side velocity write is overwritten on the owner's next update and the corpse
            // keeps drifting across the arena.
            if (isOwner && _body != null)
            {
                _body.linearVelocity = Vector3.zero;
                _body.angularVelocity = Vector3.zero;
            }

            // The host decides where the corpse goes, immediately.
            if (isServer)
                ParkCorpse();
        }

        /// <summary>
        /// Moves the body to its future spawn point the moment it dies, instead of leaving it
        /// where it was killed.
        ///
        /// A player killed by a ring-out dies OUTSIDE the boundary, and the body used to stay
        /// there for the whole respawn delay. Everything downstream then had to cope with a corpse
        /// in illegal space: the boundary check saw it the instant the player revived and killed
        /// them a second time, the revive and the teleport raced each other, and the owner watched
        /// two deaths and a jump for one mistake.
        ///
        /// Parking at death makes the respawn a visibility change rather than a move - by the time
        /// anyone is alive again, the body has been standing in a legal position for seconds.
        /// </summary>
        private void ParkCorpse()
        {
            if (!owner.HasValue)
                return;

            if (_spawner == null)
                _spawner = FindFirstObjectByType<PlayerSpawner>();

            if (_spawner == null)
                return;

            if (!_spawner.ReserveSpawnPose(owner.Value, out var position, out var rotation))
            {
                Debug.LogWarning($"[Vitals] No spawn point to park {owner} on; the corpse stays " +
                                 "where it died and the respawn will have to move it.");
                return;
            }

            Debug.Log($"[Vitals] Parked {owner} at {position} for respawn.");
            BroadcastPark(position, rotation);
        }

        /// <summary>
        /// Host only. Puts a living player back on a spawn point.
        ///
        /// A correction, not a game mechanic: it exists for the case where the host believes a
        /// player is somewhere they cannot legally be, which can only happen if a position update
        /// went missing. Pulling them back in is always better than the alternative, which is
        /// killing someone for a packet they never sent.
        /// </summary>
        public void ReturnToSpawn()
        {
            if (!isServer || !owner.HasValue)
                return;

            if (_spawner == null)
                _spawner = FindFirstObjectByType<PlayerSpawner>();

            if (_spawner == null)
                return;

            if (!_spawner.ReserveSpawnPose(owner.Value, out var position, out var rotation))
                return;

            BroadcastPark(position, rotation);
        }

        /// <summary>
        /// Host entry point for a hit: adds damage, then launches.
        ///
        /// Knockback scales with accumulated damage rather than with the weapon, Smash-style. A
        /// clean hit on a fresh player barely moves them; the same hit at 150% throws them across
        /// the arena. In zero-g there is no friction to arrest it, so launches read harder here
        /// than they would on a ground stage - expect to tune scaling down, not up.
        /// </summary>
        public void TakeHit(float damage, Vector3 direction, float baseKnockback, float knockbackScaling,
            PlayerID? attacker)
        {
            // Covers alive, server, and the respawn invulnerability window in one test. Blocks the
            // knockback as well as the damage: being shoved across the arena while untouchable
            // would make the spawn window a liability rather than a protection.
            if (!Vitals.CanBeHit)
                return;

            float percent = Vitals.AddDamage(damage, attacker);
            float force = baseKnockback + percent * knockbackScaling;

            if (direction.sqrMagnitude < 0.0001f)
                direction = Random.onUnitSphere;

            var impulse = direction.normalized * force;

            // A bot's owner is a PlayerID with no connection behind it, so a TargetRpc addressed
            // to it is delivered nowhere - which is why a bot sat perfectly still through rocket
            // hits. The host is the peer simulating it, so it applies the impulse itself.
            if (owner.HasValue && owner.Value.isBot)
                ApplyLaunchLocally(impulse);
            else
                LaunchOwner(owner ?? default, impulse);

            // Separate from the launch itself, which is a TargetRpc because only the owner may
            // move an owner-authoritative body. The trail has to be seen by everyone, so it needs
            // its own observers event - deriving it locally is not possible, since no remote peer
            // simulates the launch state at all.
            if (force >= trailMinimumForce)
                BroadcastLaunchTrail(Mathf.Min(maxTrailSeconds, force * trailSecondsPerForce));
        }

        [ObserversRpc(runLocally: true)]
        private void BroadcastLaunchTrail(float seconds)
        {
            ShowLaunchTrail(seconds);
        }

        /// <summary>
        /// Attaches the trail to the body, or extends one already streaming.
        ///
        /// Extended rather than restarted, and never duplicated: being hit twice mid-flight is
        /// exactly when this is most likely, and two overlapping copies of a looping emitter read
        /// as a bug rather than as a harder hit.
        /// </summary>
        private void ShowLaunchTrail(float seconds)
        {
            if (launchTrailPrefab == null)
                return;

            EnsureBody();

            var anchor = BodyTransform;
            if (anchor == null)
                return;

            _launchTrailUntil = Mathf.Max(_launchTrailUntil, Time.time + seconds);

            if (_launchTrail != null)
                return;

            _launchTrail = Instantiate(launchTrailPrefab, anchor.position, anchor.rotation, anchor);
        }

        /// <summary>
        /// Stops emitting and lets what is already in the air fade, rather than deleting the
        /// smoke mid-puff.
        /// </summary>
        private void StopLaunchTrail()
        {
            var trail = _launchTrail;
            _launchTrail = null;
            _launchTrailUntil = 0f;

            if (trail == null)
                return;

            float linger = 0.5f;

            foreach (var particles in trail.GetComponentsInChildren<ParticleSystem>())
            {
                particles.Stop(true, ParticleSystemStopBehavior.StopEmitting);
                linger = Mathf.Max(linger, particles.main.startLifetime.constantMax);
            }

            Destroy(trail, linger);
        }

        private GameObject _launchTrail;
        private float _launchTrailUntil;

        /// <summary>
        /// Applied on the owning client, for the same reason respawn is: the body is
        /// owner-authoritative, so an impulse added host-side is discarded on the owner's next
        /// update and the player never moves.
        /// </summary>
        [TargetRpc]
        private void LaunchOwner(PlayerID player, Vector3 impulse)
        {
            ApplyLaunchLocally(impulse);
        }

        private void ApplyLaunchLocally(Vector3 impulse)
        {
            EnsureBody();

            // Through the movement script rather than straight onto the Rigidbody. A raw
            // AddForce was correct and useless: the arcade movement erased the new velocity on
            // the very next FixedUpdate, so knockback never travelled regardless of its size.
            // ApplyLaunch suspends that for a moment proportional to the hit.
            if (_movement != null)
            {
                _movement.ApplyLaunch(impulse);
                return;
            }

            if (_body != null)
                _body.AddForce(impulse, ForceMode.VelocityChange);
        }

        /// <summary>
        /// Host entry point for a respawn. Call instead of moving the transform directly.
        ///
        /// The reveal and the position travel in ONE message, and every peer applies both.
        ///
        /// It used to be two: a TargetRpc that moved the body on its owner, plus the alive SyncVar
        /// that revealed it everywhere. Nothing ordered those against each other, and only the
        /// owner ever moved - every other peer, host included, had to wait for the owner's next
        /// transform update to learn the new position. So the common case was that a player was
        /// revealed, at the place they died, for most of a round trip. After a ring-out that place
        /// is out of bounds, which is what made the respawn look like it had not moved at all.
        ///
        /// Writing a remote body's transform is normally wrong here - it is owner-authoritative and
        /// the owner's stream wins - but it is right for a teleport that every peer was told about
        /// explicitly: each one snaps to the same pose immediately, and the owner's next update
        /// carries that same pose rather than contradicting it.
        /// </summary>
        public void RespawnAt(Vector3 position, Quaternion rotation)
        {
            if (!isServer)
                return;

            Vitals.Revive();
            BroadcastRespawn(position, rotation);
        }

        /// <summary>
        /// Host only. Puts this player back on the line for a new round, alive or not.
        ///
        /// Uses the same broadcast as a respawn, so a round reset and a respawn are the same move
        /// as far as every peer is concerned - there is no second code path that could drift.
        /// </summary>
        public void ResetForRound(Vector3 position, Quaternion rotation)
        {
            if (!isServer)
                return;

            Vitals.ResetForNewLife();
            BroadcastRespawn(position, rotation);
        }

        /// <summary>
        /// runLocally so the host moves and reveals in the same call as everyone else.
        /// </summary>
        [ObserversRpc(runLocally: true)]
        private void BroadcastRespawn(Vector3 position, Quaternion rotation)
        {
            ApplyTeleport(position, rotation);
            RestoreAfterRespawn();
        }

        /// <summary>
        /// Parks the corpse on every peer, for the same reason the respawn broadcasts: a teleport
        /// only the owner knows about leaves everyone else rendering the body where it died until
        /// the owner's stream catches up.
        /// </summary>
        [ObserversRpc(runLocally: true)]
        private void BroadcastPark(Vector3 position, Quaternion rotation)
        {
            ApplyTeleport(position, rotation);
        }

        private void ApplyTeleport(Vector3 position, Quaternion rotation)
        {
            // Resolved on demand first. If _movement were ever still null here the fallback below
            // would move the networked ROOT, which never moves anything visible - the body is a
            // child with its own transform sync - and the teleport would silently do nothing.
            EnsureBody();

            var bodyTransform = _movement != null ? _movement.transform : transform;

            if (_movement == null)
                Debug.LogError($"[Vitals] {name} has no PlayerZeroGMovement; teleporting the root, " +
                               "which will not move the visible body.");

            if (_body != null)
            {
                _body.linearVelocity = Vector3.zero;
                _body.angularVelocity = Vector3.zero;
            }

            bodyTransform.SetPositionAndRotation(position, rotation);

            var networkTransform = bodyTransform.GetComponent<NetworkTransform>();
            if (networkTransform == null)
                return;

            // Without this the move is interpolated and every other client renders the respawn as
            // a very fast flight across the arena.
            networkTransform.ClearInterpolation(position, rotation, null);

            // And this is what makes the move survive the network.
            //
            // A dead body is STATIC. The normal transform stream is delta-based and unreliable: it
            // notices the teleport, sends one packet, and then goes quiet because nothing is
            // changing any more. Lose that single packet - the latency simulator drops 2% - and
            // every other peer keeps the position the body had before the teleport, forever, with
            // no further update coming to correct it. On the host that means a respawned player is
            // still standing outside the boundary as far as the only peer that decides ring-outs
            // is concerned.
            //
            // ForceSync is PurrNet's answer to exactly this: it voids the compression and pushes
            // the state over the reliable RPC path, teleporting every observer rather than feeding
            // their interpolation buffer. Only the controller may call it, which is the owner here.
            if (networkTransform.isController)
                networkTransform.ForceSync();
        }

        [Header("Respawn")]
        [Tooltip("On/off cycles per second while invulnerable after a respawn. Smash's tell: the " +
                 "player is visible and solid-looking but obviously not a target yet.")]
        [SerializeField] private float blinkRate = 9f;

        private bool _blinking;

        /// <summary>
        /// Blinks the model while the respawn invulnerability lasts.
        ///
        /// Borrowed wholesale from Smash because it solves a problem nothing else does: an
        /// untouchable player looks exactly like a touchable one, so without a tell, opponents
        /// waste a 3-second charge on someone who cannot be hit and read it as the game eating
        /// their input. Blinking is also legible at any distance and in peripheral vision, which a
        /// HUD icon or a timer would not be.
        ///
        /// Runs on every peer: the shooter needs to see it at least as much as the respawner does.
        /// </summary>
        private void Update()
        {
            if (!isSpawned)
                return;

            if (_launchTrail != null && Time.time >= _launchTrailUntil)
                StopLaunchTrail();

            bool invulnerable = Vitals.IsAlive && Vitals.IsInvulnerable;

            if (!invulnerable)
            {
                // Restore once on the way out, rather than every frame.
                if (_blinking)
                {
                    _blinking = false;
                    SetRenderersVisible(true);
                }

                return;
            }

            _blinking = true;

            // Square wave rather than a fade: a sine would spend most of its time at partial
            // alpha, which needs transparent materials to read at all. Toggling renderers works
            // with whatever shader the character happens to use.
            bool on = Mathf.Repeat(Time.time * blinkRate, 1f) > 0.5f;
            SetRenderersVisible(on);
        }

        /// <summary>
        /// Turns the corpse's colliders off and back on.
        ///
        /// A dead body was still solid, so an invisible wall sat wherever someone was killed -
        /// and parking corpses on the spawn points would have put those walls exactly where
        /// players return. Also stops rockets detonating on the dead and dash surface checks
        /// finding them.
        ///
        /// Runs on every peer, not just the owner: collision is simulated locally, so a remote
        /// copy left solid still blocks the player sitting in front of it.
        /// </summary>
        private void SetSolid(bool solid)
        {
            if (_colliders == null)
            {
                _colliders = GetComponentsInChildren<Collider>(true);
                _colliderWasEnabled = new bool[_colliders.Length];
                for (int i = 0; i < _colliders.Length; i++)
                    _colliderWasEnabled[i] = _colliders[i] != null && _colliders[i].enabled;
            }

            for (int i = 0; i < _colliders.Length; i++)
            {
                if (_colliders[i] != null)
                    _colliders[i].enabled = solid && _colliderWasEnabled[i];
            }
        }

        /// <summary>
        /// Raised off the alive flag. A safety net for the respawn broadcast, which normally gets
        /// here first and brings the position with it.
        ///
        /// Running twice is harmless, and running EARLY is now harmless too: the body was parked
        /// on its spawn point at the moment of death, so whichever message arrives first is
        /// revealing a body that is already in the right place. That was the whole point of
        /// parking - it makes the ordering of these two messages stop mattering.
        /// </summary>
        private void HandleRespawned()
        {
            RestoreAfterRespawn();
        }

        /// <summary>
        /// Death camera. Owner only - nobody else has a rig to hold.
        ///
        /// The rig keeps its look input either way, so a dead player can turn and watch the round
        /// continue from where they were killed. Releasing it re-attaches to the body, which by
        /// then is already standing on its spawn point, so the view arrives with control.
        /// </summary>
        private void SetDeathCamera(bool holding)
        {
            if (!isOwner)
                return;

            if (_cameraRig == null)
                _cameraRig = GetComponentInChildren<ThirdPersonCameraRig>(true);

            if (_cameraRig != null)
                _cameraRig.HoldPosition = holding;
        }

        private void RestoreAfterRespawn()
        {
            SetVisible(true);
            SetSolid(true);
            SetDeathCamera(false);

            if (isOwner && _movement != null)
            {
                _movement.enabled = true;

                // Cleared explicitly: dying mid-launch would otherwise leave the stun running and
                // the respawned player would start the next life unable to dash.
                _movement.ClearLaunch();
            }
        }

        /// <summary>
        /// Disables renderers rather than deactivating GameObjects.
        ///
        /// SetActive(false) on a branch containing the animated rig fights everything else that
        /// expects it to exist - the Animator, the rig constraints, NetworkAnimator - and an
        /// inactive object cannot be reliably reactivated into the same pose. Turning renderers
        /// off leaves the hierarchy intact and simply stops drawing it, which is all death needs
        /// before the body is recycled on respawn.
        /// </summary>
        /// <summary>
        /// Just the model. Split out from SetVisible because the respawn blink toggles this many
        /// times a second, and cycling particle emission or canvases at 9Hz would restart effects
        /// rather than flicker them.
        /// </summary>
        private void SetRenderersVisible(bool visible)
        {
            if (_renderers == null)
            {
                _renderers = GetComponentsInChildren<Renderer>(true);

                // Captured once, before anything is hidden. Some renderers are deliberately off in
                // the prefab - the body carries a placeholder capsule mesh under the character
                // model - and blanket-enabling everything on respawn turned the player into that
                // capsule. Restore what each one was, not what we assume it should be.
                _rendererWasEnabled = new bool[_renderers.Length];
                for (int i = 0; i < _renderers.Length; i++)
                    _rendererWasEnabled[i] = _renderers[i] != null && _renderers[i].enabled;

                if (_renderers.Length == 0)
                    Debug.LogWarning($"[Vitals] No renderers found under {name}; death will be invisible.");
            }

            for (int i = 0; i < _renderers.Length; i++)
            {
                // The lock-on reticle and any other UI are not renderers, so nothing here can
                // blank the local player's HUD.
                if (_renderers[i] != null)
                    _renderers[i].enabled = visible && _rendererWasEnabled[i];
            }
        }

        private void SetVisible(bool visible)
        {
            SetRenderersVisible(visible);

            // World-space canvases: the holograms on the character.
            //
            // UI does not draw through a Renderer - it goes through CanvasRenderer, which is not
            // one - so the loop above could never hide them and the dash hologram on the player's
            // back stayed lit over their corpse. Disabling the Canvas itself hides everything
            // under it in one step.
            //
            // World-space only. A screen-space canvas under the player would be that player's own
            // HUD, and blanking your HUD the moment you die is the opposite of helpful.
            if (_canvases == null)
            {
                var all = GetComponentsInChildren<Canvas>(true);
                var world = new List<Canvas>();

                foreach (var canvas in all)
                {
                    if (canvas != null && canvas.renderMode == RenderMode.WorldSpace)
                        world.Add(canvas);
                }

                _canvases = world.ToArray();
                _canvasWasEnabled = new bool[_canvases.Length];
                for (int i = 0; i < _canvases.Length; i++)
                    _canvasWasEnabled[i] = _canvases[i].enabled;
            }

            for (int i = 0; i < _canvases.Length; i++)
            {
                if (_canvases[i] != null)
                    _canvases[i].enabled = visible && _canvasWasEnabled[i];
            }

            // Trails and boosters keep emitting from a hidden body otherwise. Original states are
            // captured for the same reason as renderers: several of these are off until the
            // movement code turns them on.
            if (_particles == null)
            {
                _particles = GetComponentsInChildren<ParticleSystem>(true);
                _particleWasEmitting = new bool[_particles.Length];
                for (int i = 0; i < _particles.Length; i++)
                    _particleWasEmitting[i] = _particles[i] != null && _particles[i].emission.enabled;
            }

            for (int i = 0; i < _particles.Length; i++)
            {
                if (_particles[i] == null)
                    continue;

                var emission = _particles[i].emission;
                emission.enabled = visible && _particleWasEmitting[i];
            }
        }

    }
}
