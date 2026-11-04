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
        [Tooltip("Degrees per second a LOCKED rocket can turn. This is the dial that decides " +
                 "whether dodging still works: high enough and no dash escapes, which would undo " +
                 "the core skill the design is built on.")]
        // Turn radius, not turn rate, is what decides whether a rocket tracks: at speed 35 this is
        // speed / (rate in radians). 90 deg/s is a 22m circle - wider than half the island, so it
        // could not follow anyone who moved. 280 was about 7m and read as unavoidable. 150 is about
        // 13m: a locked rocket visibly hunts without being able to sit on a target.
        [SerializeField] private float turnRateDegrees = 150f;

        [Tooltip("Degrees per second an UNLOCKED rocket can turn. Every rocket seeks now, but one " +
                 "fired without a lock is a dumb seeker: it will drift onto whoever is ahead of it " +
                 "and can be out-turned by anyone who notices, which is what keeps the lock worth " +
                 "earning.")]
        [SerializeField] private float seekTurnRateDegrees = 90f;

        [Tooltip("Half-angle of the seeker head. An unlocked rocket only acquires what is roughly " +
                 "ahead of it - without this it would U-turn off the muzzle and hunt backwards, " +
                 "which no missile does and which would make firing one a hazard to yourself.")]
        [Range(5f, 90f)]
        [SerializeField] private float seekConeDegrees = 55f;

        [Tooltip("How far an unlocked seeker can see. Deliberately much shorter than the locked " +
                 "homing range and shorter than the lock range itself: an unlocked rocket is not " +
                 "aware of the arena, it notices someone it gets close to.")]
        [SerializeField] private float seekRange = 35f;

        [Tooltip("Layers that block a seeker's sight. Arena geometry, not players - an unlocked " +
                 "rocket cannot acquire through a wall, and loses whatever it had when one comes " +
                 "between them.")]
        [SerializeField] private LayerMask sightBlockers = 1 << 6;

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

            // Whether this rocket was aimed or merely pointed. A locked rocket turns hard for the
            // whole flight; an unlocked one is a seeker that has to find something itself and turns
            // at the old lazy rate when it does. Recorded once, because a seeker that acquired a
            // target must not graduate to lock-grade agility - the lock is the thing you earn by
            // holding the reticle on someone, and it has to stay worth earning.
            _locked = homingTarget != null;

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
            if (_age < homingDelay)
                return;

            // Death does not destroy the body - the match system reuses it - so a lock acquired on
            // someone who then dies would keep steering toward an invisible corpse parked wherever
            // they fell. That is the "rocket curves toward nothing" case: drop the target instead.
            // A seeker then looks for another; a locked rocket flies on straight, because its lock
            // was a decision the shooter made and inventing a replacement would not be.
            if (_targetVitals != null && !_targetVitals.Vitals.IsAlive)
            {
                _homingTarget = null;
                _targetVitals = null;
            }

            // Seekers re-run this every frame, not only when empty: the point of an unlocked rocket
            // is that it is not aware of the arena, so it has to keep *earning* the target it has
            // and lose it the moment the target goes out of sight or out of range.
            if (!_locked)
                Seek();

            if (_homingTarget == null)
                return;

            Vector3 toTarget = _homingTarget.position - transform.position;
            float distance = toTarget.magnitude;

            if (distance > homingMaxRange || distance < 0.01f)
                return;

            float rate = _locked ? turnRateDegrees : seekTurnRateDegrees;

            var desired = Quaternion.LookRotation(toTarget / distance);
            transform.rotation = Quaternion.RotateTowards(
                transform.rotation, desired, rate * deltaTime);
        }

        /// <summary>
        /// Finds something to chase, for a rocket fired without a lock.
        ///
        /// Every rocket seeks now, so a shot taken without holding the reticle on anyone still
        /// drifts onto whoever is ahead of it. What it does not get is the locked rocket's turn
        /// rate: an unlocked seeker is out-turned by anyone who notices it, which is what keeps the
        /// lock worth earning rather than making the aim pointless.
        ///
        /// Constrained to a cone ahead rather than the nearest enemy anywhere. A seeker head sees
        /// forwards; without that limit a rocket would U-turn off the muzzle and hunt backwards,
        /// which would make firing one a hazard to its own shooter.
        ///
        /// Re-run periodically rather than once, so a seeker that loses its target finds another,
        /// but not per frame - this walks every player and the rocket lives for seconds.
        /// </summary>
        private void Seek()
        {
            if (Time.time < _nextSeek)
                return;

            // Throttled because this walks every player and casts a ray per candidate, and the
            // rocket lives for seconds. Fast enough that losing sight costs a fifth of a second.
            _nextSeek = Time.time + 0.2f;

            // The target it already has has to keep qualifying. Break sight or open the range and
            // the seeker forgets you - which is real counterplay, and the thing that separates
            // "notices someone nearby" from "is aware of the arena".
            if (_homingTarget != null && !CanSee(_targetVitals))
            {
                _homingTarget = null;
                _targetVitals = null;
            }

            if (_homingTarget != null)
                return;

            float bestDot = Mathf.Cos(seekConeDegrees * Mathf.Deg2Rad);

            foreach (var candidate in Game.Networking.PlayerVitals.All)
            {
                if (!CanSee(candidate))
                    continue;

                Vector3 toCandidate = candidate.MarkerPosition - transform.position;
                float distance = toCandidate.magnitude;

                // Most centred rather than nearest: a seeker locks what it is looking at, and
                // "nearest" would have it swing onto someone off to the side it could never reach.
                float dot = Vector3.Dot(transform.forward, toCandidate / distance);
                if (dot <= bestDot)
                    continue;

                bestDot = dot;
                _homingTarget = candidate.BodyTransform;
                _targetVitals = candidate;
            }
        }

        /// <summary>
        /// Whether an unlocked seeker can actually see this player right now: alive, close, inside
        /// the seeker cone, and with nothing solid in the way.
        ///
        /// Used for acquiring and for keeping, which is deliberate - a single test means a seeker
        /// can never hold a target it would not have been allowed to acquire.
        /// </summary>
        private bool CanSee(Game.Networking.PlayerVitals candidate)
        {
            if (candidate == null || !candidate.isSpawned || !candidate.Vitals.IsAlive)
                return false;

            // Never the shooter. A seeker that could acquire its own launcher would turn the heavy
            // attack into a way to kill yourself.
            if (_firedBy.HasValue && candidate.owner.HasValue &&
                candidate.owner.Value == _firedBy.Value)
                return false;

            Vector3 toCandidate = candidate.MarkerPosition - transform.position;
            float distance = toCandidate.magnitude;

            if (distance > seekRange || distance < 0.01f)
                return false;

            Vector3 direction = toCandidate / distance;

            if (Vector3.Dot(transform.forward, direction) <= Mathf.Cos(seekConeDegrees * Mathf.Deg2Rad))
                return false;

            // Pulled up short of the body so the player's own collider does not count as the thing
            // blocking sight of them.
            return !Physics.Raycast(transform.position, direction,
                Mathf.Max(0.01f, distance - 1f), sightBlockers, QueryTriggerInteraction.Ignore);
        }

        private bool _locked;
        private float _nextSeek;

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
