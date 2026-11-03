using System.Collections.Generic;
using PurrNet;
using UnityEngine;

namespace Game.Weapons
{
    /// <summary>
    /// A rocket: slow, visible, lethal, and host-authoritative.
    ///
    /// Deliberately NOT built on PlayerBullet, which is PvE-shaped - it damages EnemyHealth only,
    /// resolves hits client-side, and travels fast enough to be undodgeable. This is the opposite
    /// of what the game needs: rockets are TowerFall arrows, slow enough to read and beat.
    ///
    /// Authority: the host moves it, the host decides what it hits, the host applies the splash.
    /// Clients render it from the synced transform and fire their own VFX from the detonation
    /// event. Per CLAUDE.md the explosion, smoke and audio are never networked - only the fact and
    /// place of the detonation.
    ///
    /// Speed is tuned against the dash, not chosen in the abstract. A surface dash covers ~30m in
    /// 0.2s (150 m/s); a rocket must be slow enough that a dash beats it and fast enough that
    /// drifting at the 14 m/s cruise does not.
    /// </summary>
    public class Rocket : NetworkBehaviour
    {
        [Header("Flight")]
        [Tooltip("Metres per second. Against a 150 m/s dash and a 14 m/s cruise, this wants to sit " +
                 "far closer to the cruise: dodgeable by dashing, not by drifting.")]
        [SerializeField] private float speed = 35f;

        [Tooltip("Seconds before the rocket detonates on its own, so strays do not live forever.")]
        [SerializeField] private float lifetime = 8f;

        [Header("Detonation")]
        [Tooltip("Full-strength radius. Inside this, a hit lands the full damage and knockback.")]
        [SerializeField] private float coreRadius = 4f;

        [Tooltip("Outer radius. Damage and knockback fall off linearly from the core to here.")]
        [SerializeField] private float blastRadius = 12f;

        [Header("Hit")]
        [Tooltip("Percentage added by a direct hit. This is the heavy attack, so it should be " +
                 "worth several taps of the laser.")]
        [SerializeField] private float damagePercent = 18f;

        [Tooltip("Launch speed applied regardless of accumulated damage. Survivable on its own - " +
                 "a fresh player should be thrown hard but able to fly back - and now the only " +
                 "weapon that moves anyone at all, since the laser's knockback is near zero.")]
        [SerializeField] private float baseKnockback = 26f;

        [Tooltip("Extra launch speed per 1% of accumulated damage. THE dial that decides when a " +
                 "player starts dying: at 1.3, a clean hit is ~91 m/s at 50% and ~221 m/s at " +
                 "150% - the first is a hard shove you fly back from, the second leaves the arena. " +
                 "Raised with the base because the laser no longer contributes any launch, so the " +
                 "rocket carries the whole ring-out job on its own.")]
        [SerializeField] private float knockbackScaling = 1.3f;

        [Header("Homing")]
        [Tooltip("Degrees per second the rocket can turn toward its target. This is the dial that " +
                 "decides whether dodging still works: high enough and no dash escapes, which " +
                 "would undo the core skill the design is built on.")]
        [SerializeField] private float turnRateDegrees = 90f;

        [Tooltip("Seconds after launch before homing engages, so a rocket does not curl back into " +
                 "the shooter at point blank.")]
        [SerializeField] private float homingDelay = 0.15f;

        [Tooltip("Homing stops if the target gets further away than this, so a lock does not " +
                 "follow someone across the whole arena.")]
        [SerializeField] private float homingMaxRange = 120f;

        [Header("Collision")]
        [Tooltip("What detonates the rocket. Should include the arena surfaces and players.")]
        [SerializeField] private LayerMask collisionMask = ~0;

        [Tooltip("Radius of the travel sweep. Larger is more forgiving at speed and less likely to " +
                 "tunnel through thin geometry between ticks.")]
        [SerializeField] private float collisionRadius = 0.6f;

        [Header("Presentation")]
        [Tooltip("Spawned locally on every client when the rocket detonates. Never networked.")]
        [SerializeField] private GameObject explosionPrefab;

        [Header("Receiver lead")]
        [Tooltip("The mesh, pushed forward on every peer but the host to cancel the delay the " +
                 "synced transform arrives with. Only the visual moves - the networked root, and " +
                 "so every hit the host resolves, is untouched.")]
        [SerializeField] private Transform visual;

        [Tooltip("Multiplier on the computed lead. 1 aims the mesh at where the host actually has " +
                 "the rocket; 0 disables the whole thing and puts the picture back to what it was.")]
        [Range(0f, 2f)]
        [SerializeField] private float visualLead = 1f;

        [Tooltip("Hard cap in metres, so a latency spike cannot throw the mesh across the arena.")]
        [SerializeField] private float maxVisualLead = 8f;

        private static readonly List<Rocket> _active = new();

        /// <summary>
        /// Every rocket currently in the air, on every peer.
        ///
        /// The radar reads this instead of sweeping a physics layer, because rockets deliberately
        /// carry no collider - they resolve hits with a SphereCast, so a trigger volume would only
        /// snag on players they fly past. An OverlapSphere therefore finds nothing at all, which is
        /// how the radar came to be pointed at a projectile layer that nothing was ever on.
        /// </summary>
        public static IReadOnlyList<Rocket> Active => _active;

        /// <summary>Set by the launcher so the shooter is not killed by their own muzzle.</summary>
        private PlayerID? _firedBy;

        /// <summary>
        /// Who fired this, replicated.
        ///
        /// _firedBy alone is host-only - Configure runs on the host after the spawn - so a client
        /// had no way to tell its own rocket from an incoming one. That is exactly the question the
        /// radar has to answer: your own rocket flying away from you is not a threat, and marking
        /// it as one trains you to ignore the marker that matters.
        ///
        /// Id 0 is the server, never a player, so it doubles as "not yet known".
        /// </summary>
        private SyncVar<PlayerID> _shooter = new(default, ownerAuth: false);

        public PlayerID? FiredBy =>
            _shooter.value.id.value != 0 ? _shooter.value : null;
        private Transform _homingTarget;
        private Game.Networking.PlayerVitals _targetVitals;
        private float _armingDelay;
        private float _age;
        private bool _detonated;
        private bool _resolved;

        /// <summary>
        /// Called by the host immediately after spawning. Arming delay stops a rocket detonating
        /// inside the shooter on the frame it appears, which would make firing suicidal at the
        /// exact moment recoil matters most.
        /// </summary>
        public void Configure(PlayerID? firedBy, float armingDelay, Transform homingTarget = null)
        {
            _firedBy = firedBy;
            _armingDelay = armingDelay;
            _homingTarget = homingTarget;
            _targetVitals = homingTarget != null
                ? homingTarget.GetComponentInParent<Game.Networking.PlayerVitals>()
                : null;

            PublishShooter();
        }

        protected override void OnSpawned()
        {
            base.OnSpawned();

            // Re-asserted here as well as in Configure, because Configure can run before the spawn
            // completes and a SyncVar written then has nowhere to go yet.
            PublishShooter();
        }

        private void PublishShooter()
        {
            if (isServer && isSpawned && _firedBy.HasValue)
                _shooter.value = _firedBy.Value;
        }

        /// <summary>
        /// Steers toward the locked target, host-side only.
        ///
        /// Turning is rate-limited rather than instant: a rocket that snaps onto its target is
        /// unavoidable, and CLAUDE.md builds the whole game on rockets being dodgeable. A capped
        /// turn rate means a hard enough dash still beats the lock, so the aim assist forgives
        /// imprecision without removing the counterplay.
        /// </summary>
        private void Steer(float deltaTime)
        {
            if (_homingTarget == null || _age < homingDelay)
                return;

            // Death does not destroy the body - the match system reuses it - so a lock acquired on
            // someone who then dies would keep steering toward an invisible corpse parked wherever
            // they fell. That is the "rocket curves toward nothing" case: drop the lock instead.
            if (_targetVitals != null && !_targetVitals.Vitals.IsAlive)
            {
                _homingTarget = null;
                return;
            }

            Vector3 toTarget = _homingTarget.position - transform.position;
            float distance = toTarget.magnitude;

            if (distance > homingMaxRange || distance < 0.01f)
                return;

            var desired = Quaternion.LookRotation(toTarget / distance);
            transform.rotation = Quaternion.RotateTowards(
                transform.rotation, desired, turnRateDegrees * deltaTime);
        }

        private void Update()
        {
            if (_detonated)
                return;

            // Only the host simulates. Clients render whatever the synced transform says, so a
            // rocket cannot exist in one place on one screen and somewhere else on another - but
            // what it says is always behind, which is what LeadVisual compensates for.
            if (!isServer)
            {
                LeadVisual();
                return;
            }

            _age += Time.deltaTime;
            if (_age >= lifetime)
            {
                Detonate(transform.position);
                return;
            }

            Steer(Time.deltaTime);


            float step = speed * Time.deltaTime;

            // Swept rather than teleported: at 35 m/s a 60Hz frame moves ~0.6m, enough to tunnel
            // straight through a wall if we only tested the destination point.
            if (Physics.SphereCast(transform.position, collisionRadius, transform.forward,
                    out var hit, step, collisionMask, QueryTriggerInteraction.Ignore))
            {
                if (!IsOwnBody(hit.collider))
                {
                    transform.position = hit.point;
                    Detonate(hit.point);
                    return;
                }
            }

            transform.position += transform.forward * step;
        }

        /// <summary>
        /// True while the rocket is still inside the player who fired it and not yet armed.
        /// </summary>
        private bool IsOwnBody(Collider other)
        {
            if (_age >= _armingDelay || !_firedBy.HasValue)
                return false;

            var identity = other.GetComponentInParent<NetworkIdentity>();
            return identity != null && identity.owner == _firedBy;
        }

        /// <summary>
        /// Raised on the host when the shot is spent - impact, timeout or despawn.
        ///
        /// Nothing consumes it now that the heavy attack is gated by a timed cooldown rather than
        /// by one-in-flight, but it is the natural hook for anything that needs to know a shot
        /// ended (scoring a miss, VFX cleanup), so it stays.
        /// </summary>
        public event System.Action OnResolved;

        private void OnEnable()
        {
            if (!_active.Contains(this))
                _active.Add(this);
        }

        private void OnDisable()
        {
            _active.Remove(this);
        }

        /// <summary>
        /// An override, not a new Unity message. This was previously a private OnDestroy, which
        /// hid NetworkIdentity's virtual one and so skipped PurrNet's own despawn bookkeeping
        /// every time a rocket died.
        /// </summary>
        protected override void OnDestroy()
        {
            base.OnDestroy();

            // Covers endings Detonate never reaches, such as the object being torn down with the
            // scene or on a despawn.
            Resolve();
        }

        private void Resolve()
        {
            if (_resolved)
                return;

            _resolved = true;
            OnResolved?.Invoke();
        }

        private void Detonate(Vector3 position)
        {
            if (_detonated)
                return;

            _detonated = true;
            Resolve();

            ApplySplash(position);
            OnDetonated(position);

            // Despawn on the host; PurrNet propagates the destroy to every client.
            Destroy(gameObject);
        }

        /// <summary>
        /// Host-only. Adds damage and launches everyone in range.
        ///
        /// This no longer kills. Percentage accumulates and knockback scales with it, so the blast
        /// decides how far someone flies, and the arena boundary decides whether that was fatal.
        /// </summary>
        private void ApplySplash(Vector3 position)
        {
            // Deduplicated per player: a body capsule plus any child colliders would otherwise
            // land several hits from one blast and launch someone absurdly far.
            var struck = new HashSet<Game.Networking.PlayerVitals>();
            var hits = Physics.OverlapSphere(position, blastRadius, ~0, QueryTriggerInteraction.Ignore);

            foreach (var collider in hits)
            {
                var vitals = collider.GetComponentInParent<Game.Networking.PlayerVitals>();
                if (vitals == null || !struck.Add(vitals))
                    continue;

                var body = collider.attachedRigidbody;
                Vector3 centre = body != null ? body.worldCenterOfMass : collider.bounds.center;

                Vector3 offset = centre - position;
                float distance = offset.magnitude;
                if (distance > blastRadius)
                    continue;

                Vector3 direction = distance > 0.001f ? offset / distance : Random.onUnitSphere;

                // Full strength inside the core, falling off linearly to nothing at the edge.
                float falloff = distance <= coreRadius
                    ? 1f
                    : 1f - Mathf.Clamp01((distance - coreRadius) /
                                         Mathf.Max(0.01f, blastRadius - coreRadius));

                // Self-hits allowed on purpose: launching yourself with your own blast is a
                // mobility option, and at high percentage a real risk.
                vitals.TakeHit(
                    damagePercent * falloff,
                    direction,
                    baseKnockback * falloff,
                    knockbackScaling * falloff,
                    _firedBy);
            }
        }


        /// <summary>
        /// Fires the explosion on every client. Only the position travels - the VFX, the light and
        /// the audio are spawned locally from it.
        /// </summary>
        /// <summary>
        /// Pushes the mesh forward by however far the synced transform is behind.
        ///
        /// The rocket is simulated only on the host, so every other peer renders it from an
        /// interpolated transform that is late by their latency plus the interpolation buffer. At
        /// 35 m/s and 100ms that is three and a half metres, against a four metre kill radius - so
        /// a rocket that visually passed beside you killed you anyway, and the host was the only
        /// player whose eyes told the truth. That is the whole of the host's rocket advantage, and
        /// it is on the receiving end rather than the firing end.
        ///
        /// Only the visual moves. The networked root still carries the authoritative position, so
        /// every hit the host resolves is unchanged - this makes the picture agree with the
        /// decision rather than altering the decision. It also lines the rocket up with its own
        /// explosion, which already spawns at the host's true detonation point because that
        /// position travels inside <see cref="OnDetonated"/>.
        /// </summary>
        private void LeadVisual()
        {
            if (visual == null || visualLead <= 0f)
                return;

            var tick = networkManager != null ? networkManager.tickModule : null;
            if (tick == null || tick.rtt <= 0f)
                return;

            float tickDelta = 1f / Mathf.Max(1, tick.tickRate);

            // PurrNet's rtt bakes in two ticks of pack/unpack delay - the status HUD labels it for
            // the same reason - so that bias comes off before halving it into a one-way estimate.
            float oneWay = Mathf.Max(0f, (float)tick.rtt - 2f * tickDelta) * 0.5f;

            // Plus the buffer the NetworkTransform deliberately renders behind by, which on this
            // prefab is one to two ticks.
            float behind = oneWay + 1.5f * tickDelta;

            visual.localPosition =
                Vector3.forward * Mathf.Min(maxVisualLead, speed * behind * visualLead);
        }

        [ObserversRpc(runLocally: true)]
        private void OnDetonated(Vector3 position)
        {
            if (explosionPrefab == null)
                return;

            var explosion = Instantiate(explosionPrefab, position, Quaternion.identity);

            var particles = explosion.GetComponentInChildren<ParticleSystem>();
            Destroy(explosion, particles != null ? particles.main.duration + 1f : 3f);
        }
    }
}
