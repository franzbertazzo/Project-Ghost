using PurrNet;
using UnityEngine;

namespace Game.Weapons
{
    /// <summary>
    /// Tap-fire laser: the chip damage that builds a percentage.
    ///
    /// Hitscan rather than a projectile. At the rate this fires, syncing a projectile per shot
    /// would cost far more bandwidth than it buys, and the host already has to resolve the hit
    /// either way. The visible bolt is a local tracer fired from a networked event - CLAUDE.md
    /// keeps VFX off the wire.
    ///
    /// Knockback is deliberately tiny. The laser's job is to raise percentage so the charged shot
    /// can launch someone; if taps also launched, the charge would have no role.
    /// </summary>
    public class Blaster : NetworkBehaviour
    {
        [Header("Fire")]
        [Tooltip("Seconds between shots while tapping or holding below the charge threshold.")]
        [SerializeField] private float fireInterval = 0.12f;

        [SerializeField] private float range = 300f;

        [Header("Hit")]
        [Tooltip("Percentage added per bolt. Small on purpose - this is the chip damage.")]
        [SerializeField] private float damagePercent = 2.5f;

        [Tooltip("Launch speed per bolt. Near zero on purpose: the laser's job is to build " +
                 "percentage, not to move anyone.")]
        [SerializeField] private float laserKnockback = 0.2f;

        [Tooltip("Extra launch per 1% accumulated. Zero: the laser must not get better at " +
                 "launching as the target's percentage climbs, or it becomes the kill move at high " +
                 "percent and the rocket stops being the thing that finishes people.")]
        [SerializeField] private float laserKnockbackScaling;

        [Header("Layers")]
        [Tooltip("What a bolt can hit. Arena geometry and players.")]
        [SerializeField] private LayerMask hitMask = (1 << 6) | (1 << 9);

        [Tooltip("Layers that block a bolt and never move - arena geometry, not players. The " +
                 "server checks claimed hits against these, which is trustworthy precisely because " +
                 "static geometry is the part of the world every peer agrees on.")]
        [SerializeField] private LayerMask blockingMask = 1 << 6;

        [Header("Hit validation (server)")]
        [Tooltip("How far a claimed impact may sit from where the server currently has that " +
                 "player. This is the whole budget for network staleness: it has to cover one " +
                 "round trip of movement plus the interpolation delay remote bodies are drawn " +
                 "behind by. Too small and clients stop landing shots again; too large and a " +
                 "client can claim hits on someone it was never really aiming at.")]
        [SerializeField] private float claimTolerance = 10f;

        [Tooltip("How far the claimed muzzle may sit from the shooter's own body. Stops a client " +
                 "claiming it fired from somewhere across the arena.")]
        [SerializeField] private float originTolerance = 6f;

        [Tooltip("Validate claims against where the target WAS when the shot was fired, using " +
                 "PurrNet's collider rollback history. Needs a ColliderRollback component on the " +
                 "player. Off falls back to the loose current-position check.")]
        [SerializeField] private bool useRollback = true;

        [Tooltip("Tolerance used when the rollback history answered. Tight on purpose: with the " +
                 "staleness removed, the only slack still needed is the body's own size plus a " +
                 "little for the interpolation estimate, instead of a whole round trip of travel.")]
        [SerializeField] private float rewoundTolerance = 2.5f;

        [Tooltip("Extra ticks to rewind beyond rollbackTick, covering the interpolation buffer " +
                 "remote bodies are rendered behind by. This prefab's NetworkTransform buffers " +
                 "one to two ticks.")]
        [Range(0f, 6f)]
        [SerializeField] private float extraRollbackTicks = 1.5f;

        [Header("Presentation")]
        [Tooltip("Tracer drawn from muzzle to impact. Spawned locally on every client.")]
        [SerializeField] private GameObject tracerPrefab;

        [SerializeField] private GameObject impactPrefab;

        [SerializeField] private float tracerLifetime = 0.08f;

        [Tooltip("Fire the muzzle particles already sitting on the gun models. Off only if the " +
                 "flash ever needs to be replaced wholesale.")]
        [SerializeField] private bool playGunMuzzleVfx = true;

        [Tooltip("Alternate hands shot to shot instead of flashing both guns at once. The rig has " +
                 "two pistols and one bolt leaves per shot, so firing both reads as a doubled gun.")]
        [SerializeField] private bool alternateHands = true;

        private float _nextFireTime;
        private ParticleSystem[] _muzzles;
        private int _nextMuzzle;

        public bool CanFire => Time.time >= _nextFireTime;

        /// <summary>
        /// Called by the owner. Origin and direction come from the shooter because only the
        /// shooter knows where it was aiming when it pulled the trigger.
        /// </summary>
        public void Fire(Vector3 origin, Vector3 direction)
        {
            if (!isOwner || !isSpawned || !CanFire)
                return;

            if (direction.sqrMagnitude < 0.0001f)
                return;

            direction.Normalize();
            _nextFireTime = Time.time + fireInterval;

            // Resolved HERE, on the shooter, against the world the shooter can actually see.
            //
            // This is the whole fix. The server used to do this raycast, using the client's origin
            // and direction but its own, newer positions - so a client aiming dead-on at the enemy
            // it saw missed by however far that enemy had travelled during one trip to the host,
            // plus the interpolation delay every remote body is rendered behind by. At 30 m/s that
            // is metres. The host, with no trip and no delay, hit every time. So the game was
            // playable for exactly one player, and the aim assist made it worse by bending the
            // shot precisely onto a position the server no longer agreed with.
            Vector3 end = origin + direction * range;
            var claimed = default(PlayerID);
            var bodyOffset = Vector3.zero;

            if (TryGetFirstHit(origin, direction, out var hit, out var target))
            {
                end = hit.point;

                if (target != null && target.owner.HasValue)
                {
                    claimed = target.owner.Value;

                    // Where on the body it landed, relative to that body - not a world position.
                    // A world point is only true on the screen it was computed for: every other
                    // peer, the victim above all, has that player somewhere slightly different, so
                    // the impact appeared hanging in space beside them. An offset re-anchors onto
                    // whatever position each peer actually has, which is what makes it land on your
                    // own chest on your own screen.
                    bodyOffset = end - target.MarkerPosition;
                }
            }

            // Drawn immediately rather than waiting for the observers RPC to come back. The host
            // skips this: it draws from the RPC's runLocally instead, so nobody draws twice.
            if (!isServer)
                ShowBoltLocal(origin, end, claimed, bodyOffset);

            // The server tick matching what this client can currently see. PurrNet computes it as
            // syncedPreciseTick minus half the RTT, which is exactly "the state I am looking at" -
            // so the host can judge the shot against the world the shooter had, instead of against
            // a slop radius around the world it has now.
            double firedAt = FiringTick();

            RequestFire(origin, direction, claimed, end, bodyOffset, firedAt);
        }

        /// <summary>
        /// The shooter's claim, checked rather than trusted.
        ///
        /// Favour-the-shooter, which is what every hitscan shooter does and the only thing that
        /// makes a crosshair mean anything over a network: the client says who it hit, and the
        /// server's job is to decide whether that was *possible* rather than to re-run the shot
        /// against positions the shooter never saw. CLAUDE.md's "assume every client lies" is
        /// still honoured - see <see cref="AcceptClaim"/> for exactly which lies are caught - but
        /// the authority has moved from "where was the target" to "could the target have been
        /// there", because the first question has no single true answer across four machines.
        /// </summary>
        [ServerRpc(requireOwnership: true)]
        private void RequestFire(Vector3 origin, Vector3 direction, PlayerID claimed, Vector3 end,
            Vector3 bodyOffset, double firedAt)
        {
            _claimTick = firedAt;

            if (direction.sqrMagnitude < 0.0001f)
                return;

            direction.Normalize();

            if (AcceptClaim(claimed, origin, end, out var target))
            {
                target.TakeHit(damagePercent, direction, laserKnockback, laserKnockbackScaling,
                    owner);
            }
            else
            {
                // No accepted target, so the bolt ends where the geometry says it does. Recomputed
                // rather than trusting the client's endpoint, so a rejected claim cannot still
                // paint an impact on someone's chest.
                claimed = default;
                bodyOffset = Vector3.zero;
                end = origin + direction * range;

                if (Physics.Raycast(origin, direction, out var wall, range, blockingMask,
                        QueryTriggerInteraction.Ignore))
                    end = wall.point;
            }

            // The offset is passed through from the shooter rather than recomputed here. It says
            // where on the body the bolt landed, and only the shooter's view of that body can
            // answer that - recomputing it against the server's position would reintroduce exactly
            // the discrepancy it exists to cancel.
            ShowBolt(origin, end, claimed, bodyOffset);
        }

        /// <summary>
        /// Whether the shooter's claimed hit was possible from where the server stands.
        ///
        /// Four things are checked, and each one is a specific lie: that the named player exists,
        /// is alive and is not the shooter; that the shot was fired from somewhere near the
        /// shooter's own body; that the claimed impact is near where the server currently has that
        /// player, which is what bounds the lie to one round trip of movement; and that no static
        /// geometry stands between the two. That last check is the important one and it is exactly
        /// as reliable as a server-side raycast, because walls are the part of the world every peer
        /// agrees on - only players move. What a client can get away with is a few metres of
        /// staleness, which is the same few metres it was being punished for before.
        /// </summary>
        /// <summary>
        /// The tick whose world the shooter was looking at when it fired.
        ///
        /// <c>rollbackTick</c> is PurrNet's own answer to this: the synced server tick minus half
        /// the RTT. On the host it degenerates to the current tick, since there is no trip and
        /// nothing to rewind - which is exactly right, and means the host needs no special case.
        ///
        /// The extra ticks come off on top, because rollbackTick accounts for the wire but not for
        /// the interpolation buffer the NetworkTransform deliberately renders remote bodies behind
        /// by. Without them the host rewinds slightly less far than the shooter's eyes actually saw.
        /// </summary>
        private double FiringTick()
        {
            var tick = networkManager != null ? networkManager.tickModule : null;

            if (tick == null)
                return 0d;

            return tick.rollbackTick - extraRollbackTicks;
        }

        /// <summary>
        /// Asks the rollback module where the target's collider was at the claimed tick.
        ///
        /// False means the question could not be answered - no module, no registered collider, or
        /// a tick older than the recorded history - and the caller falls back to the loose current
        /// position check rather than rejecting the shot. A client should not lose hits because the
        /// host forgot where everyone was.
        /// </summary>
        private bool TryGetPastPosition(Game.Networking.PlayerVitals target, out Vector3 position)
        {
            position = target.MarkerPosition;

            if (!useRollback || _claimTick <= 0d)
                return false;

            var collider = target.HitCollider;
            if (collider == null)
                return false;

            var module = Rollback();
            if (module == null)
                return false;

            if (!module.TryGetColliderState(_claimTick, collider, out var state))
                return false;

            position = state.position;
            return true;
        }

        /// <summary>
        /// The scene's rollback module, resolved once. Reached through the factory the same way
        /// PurrNet's own ColliderRollback component does.
        /// </summary>
        private PurrNet.Modules.RollbackModule Rollback()
        {
            if (_rollback != null)
                return _rollback;

            if (networkManager == null)
                return null;

            if (!networkManager.TryGetModule<PurrNet.Modules.ScenesModule>(true, out var scenes))
                return null;

            if (!scenes.TryGetSceneID(gameObject.scene, out var sceneId))
                return null;

            if (!networkManager.TryGetModule<PurrNet.Modules.ColliderRollbackFactory>(true,
                    out var factory))
                return null;

            factory.TryGetModule(sceneId, out _rollback);
            return _rollback;
        }

        private PurrNet.Modules.RollbackModule _rollback;
        private double _claimTick;

        private bool AcceptClaim(PlayerID claimed, Vector3 origin, Vector3 point,
            out Game.Networking.PlayerVitals target)
        {
            target = null;

            // Id 0 is the server, never a player, so it doubles as "claimed nothing".
            if (claimed.id.value == 0)
                return false;

            if (owner.HasValue && claimed == owner.Value)
                return false;

            foreach (var vitals in Game.Networking.PlayerVitals.All)
            {
                if (vitals == null || !vitals.isSpawned || vitals.owner != claimed)
                    continue;

                target = vitals;
                break;
            }

            if (target == null || !target.Vitals.CanBeHit)
            {
                target = null;
                return false;
            }

            if (_ownVitals == null)
                _ownVitals = GetComponentInParent<Game.Networking.PlayerVitals>();

            if (_ownVitals != null &&
                Vector3.Distance(origin, _ownVitals.MarkerPosition) > originTolerance)
            {
                target = null;
                return false;
            }

            float distance = Vector3.Distance(origin, point);

            // Where the target was when the shooter fired, if the rollback module has that tick on
            // record. That is the position the shooter was actually looking at, so the claim can be
            // held to the body's own size rather than to a window wide enough to cover a round trip
            // of movement. Falls back to the current position and the loose tolerance when there is
            // no history - a player who just spawned, or a build without the component wired.
            bool rewound = TryGetPastPosition(target, out var reference);
            float tolerance = rewound ? rewoundTolerance : claimTolerance;

            if (distance > range || Vector3.Distance(point, reference) > tolerance)
            {
                target = null;
                return false;
            }

            // Pulled back slightly so the ray stops short of the body it is testing against.
            if (distance > 0.05f && Physics.Raycast(origin, (point - origin) / distance,
                    distance - 0.05f, blockingMask, QueryTriggerInteraction.Ignore))
            {
                target = null;
                return false;
            }

            return true;
        }

        /// <summary>
        /// The nearest thing the bolt hits that is not the shooter's own body.
        ///
        /// A plain Raycast took the nearest hit full stop, and merely declined to *damage* the
        /// shooter if it turned out to be them - which left two real problems. The bolt still
        /// terminated on your own capsule, so the impact effect played on your own back; and it was
        /// still blocked there, so your own collider silently ate shots that would otherwise have
        /// reached whoever was behind it. The gun is held beside a third-person body and aimed
        /// along the camera, so the ray clips that body routinely, particularly while strafing.
        ///
        /// Skipping instead of stopping is the fix: own colliders are not in the way of our own
        /// gun. Every other hit still blocks normally, so cover works as before.
        /// </summary>
        private bool TryGetFirstHit(Vector3 origin, Vector3 direction, out RaycastHit hit,
            out Game.Networking.PlayerVitals target)
        {
            hit = default;
            target = null;

            if (_ownVitals == null)
                _ownVitals = GetComponentInParent<Game.Networking.PlayerVitals>();

            int count = Physics.RaycastNonAlloc(origin, direction, _hits, range, hitMask,
                QueryTriggerInteraction.Ignore);

            float nearest = float.MaxValue;
            bool found = false;

            for (int i = 0; i < count; i++)
            {
                var candidate = _hits[i];
                var vitals = candidate.collider.GetComponentInParent<Game.Networking.PlayerVitals>();

                // Two independent self-checks, same reasoning as the rocket's lock: by object
                // identity, which holds even while ownership is momentarily unset after a respawn,
                // and by owner id for anything the hierarchy walk misses.
                if (vitals != null &&
                    (vitals == _ownVitals || (owner.HasValue && vitals.owner == owner)))
                    continue;

                // RaycastNonAlloc does not sort, so the nearest has to be found rather than taken.
                if (candidate.distance >= nearest)
                    continue;

                nearest = candidate.distance;
                hit = candidate;
                target = vitals;
                found = true;
            }

            return found;
        }

        // Sized for "every collider a bolt could cross in this arena" with room to spare. Reused
        // rather than allocated per shot: this runs up to eight times a second per player.
        private readonly RaycastHit[] _hits = new RaycastHit[16];

        private Game.Networking.PlayerVitals _ownVitals;

        /// <summary>
        /// Only the two endpoints travel; the tracer and impact effects are built locally from
        /// them on each client.
        ///
        /// excludeOwner because the shooter has already drawn its own bolt the instant it pulled
        /// the trigger - waiting for this to come back would mean watching your own tracer appear a
        /// round trip after you fired. runLocally so a host that is *not* the shooter still draws
        /// one; between them every peer draws exactly once.
        /// </summary>
        [ObserversRpc(runLocally: true, excludeOwner: true)]
        private void ShowBolt(Vector3 origin, Vector3 end, PlayerID hit, Vector3 bodyOffset)
        {
            ShowBoltLocal(origin, end, hit, bodyOffset);
        }

        /// <summary>
        /// Draws the bolt, re-anchoring the impact onto the hit player's body as *this* peer has it.
        ///
        /// The transmitted endpoint is only true on the screen that computed it. Every other peer
        /// has that player a little elsewhere - the victim most of all, since they see their own
        /// body at its real position with no interpolation delay at all - so a world-space impact
        /// appeared floating beside them rather than on them. Anchoring to the body instead means
        /// the tracer connects and the effect lands on the chest on every screen.
        /// </summary>
        private void ShowBoltLocal(Vector3 origin, Vector3 end, PlayerID hit, Vector3 bodyOffset)
        {
            if (hit.id.value != 0)
            {
                foreach (var vitals in Game.Networking.PlayerVitals.All)
                {
                    if (vitals == null || !vitals.isSpawned || vitals.owner != hit)
                        continue;

                    end = vitals.MarkerPosition + bodyOffset;
                    break;
                }
            }

            ShowBoltAt(origin, end);
        }

        private void ShowBoltAt(Vector3 origin, Vector3 end)
        {
            PlayMuzzleFlash();

            if (tracerPrefab != null)
            {
                var tracer = Instantiate(tracerPrefab, origin, Quaternion.LookRotation(end - origin));
                tracer.transform.localScale = new Vector3(1f, 1f, Vector3.Distance(origin, end));
                Destroy(tracer, tracerLifetime);
            }

            if (impactPrefab != null)
            {
                var impact = Instantiate(impactPrefab, end, Quaternion.identity);
                var particles = impact.GetComponentInChildren<ParticleSystem>();
                Destroy(impact, particles != null ? particles.main.duration + 0.5f : 2f);
            }
        }

        /// <summary>
        /// Fires the muzzle particles that already live on the gun models.
        ///
        /// Reuses the PvE Weapon's `muzzle` reference rather than spawning a new flash prefab:
        /// the effect is already authored, positioned and parented to each pistol, and a spawned
        /// copy would sit in world space and lag a frame behind a player moving at 12 m/s.
        ///
        /// Called from the observers RPC, so every client sees the gun fire - that flash is the
        /// only cue a player across the arena gets that someone is shooting at them.
        /// </summary>
        private void PlayMuzzleFlash()
        {
            if (!playGunMuzzleVfx)
                return;

            if (_muzzles == null)
                CacheMuzzles();

            if (_muzzles.Length == 0)
                return;

            if (!alternateHands)
            {
                for (int i = 0; i < _muzzles.Length; i++)
                    _muzzles[i].Play(true);

                return;
            }

            _muzzles[_nextMuzzle % _muzzles.Length].Play(true);
            _nextMuzzle++;
        }

        private void CacheMuzzles()
        {
            var guns = GetComponentsInChildren<Weapon>(true);
            var found = new System.Collections.Generic.List<ParticleSystem>();

            foreach (var gun in guns)
            {
                if (gun.muzzle == null)
                    continue;

                // Explicit rather than ?? : null-coalescing does not understand Unity's
                // overloaded null, so it is a trap on anything deriving from UnityEngine.Object.
                var particles = gun.muzzle.GetComponent<ParticleSystem>();
                if (particles == null)
                    particles = gun.muzzle.GetComponentInChildren<ParticleSystem>(true);

                if (particles != null)
                    found.Add(particles);
            }

            _muzzles = found.ToArray();

            if (_muzzles.Length == 0)
                Debug.LogWarning("[Blaster] No muzzle particle systems found on the gun models; " +
                                 "the laser will fire without a flash.");
        }
    }
}
