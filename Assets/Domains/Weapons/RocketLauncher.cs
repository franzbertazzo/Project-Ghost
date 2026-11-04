using PurrNet;
using UnityEngine;

namespace Game.Weapons
{
    /// <summary>
    /// Fires rockets: the only weapon in the launch scope.
    ///
    /// Replaces the dual-pistol Weapon setup rather than extending it. Weapon.Shoot() aims at a
    /// fixed `Target` reference, instantiates client-side, and damages EnemyHealth only - all
    /// PvE-shaped. It also fires once per hand, which would double both ammo burn and recoil
    /// against a fixed carry count.
    ///
    /// Authority split, per CLAUDE.md: the shooter decides WHEN and WHERE it is aiming, the host
    /// decides whether the shot happens and owns the rocket from then on. Recoil is applied
    /// locally and immediately because the shooter owns its own body - waiting a round trip to be
    /// pushed would make the game's central movement mechanic feel broken.
    /// </summary>
    public class RocketLauncher : NetworkBehaviour
    {
        [Header("Wiring")]
        [Tooltip("Where rockets leave from. Falls back to the first Weapon's fire point.")]
        [SerializeField] private Transform firePoint;

        [Tooltip("What the player is aiming at. This is the same Target the spine rig aims at, " +
                 "moved to the player root in P1 so it survives the camera rig being disabled.")]
        [SerializeField] private Transform aimTarget;

        [SerializeField] private GameObject rocketPrefab;

        [Header("Firing")]
        [Tooltip("Minimum seconds between heavy shots, on top of the charge and one-in-flight gates.")]
        [SerializeField] private float fireInterval = 0.6f;

        [Tooltip("Seconds before a fresh rocket can hurt the player who fired it.")]
        [SerializeField] private float armingDelay = 0.08f;

        [Header("Recoil")]
        [Tooltip("Impulse pushing the shooter away from the shot. Rocket-jumping is intended, so " +
                 "this is a movement stat as much as a weapon stat.")]
        [SerializeField] private float recoilImpulse = 25f;

        [Header("Lock-on")]
        [Tooltip("Half-angle of the lock cone, in degrees. This is the circle drawn on screen: an " +
                 "enemy inside it, with line of sight, becomes the rocket's target. Wider is more " +
                 "forgiving of imprecise aim.")]
        [Range(1f, 45f)]
        [SerializeField] private float lockAngleDegrees = 12f;

        /// <summary>Read by the reticle so the drawn circle matches the rule exactly.</summary>
        public float LockAngleDegrees => lockAngleDegrees;

        [Tooltip("Beyond this distance enemies cannot be locked, however well centred. Also what " +
                 "turns the reticle red, so the circle only promises a lock it can actually " +
                 "deliver. At 30m a rocket is airborne for under a second: close enough that " +
                 "the lock forgives your aim rather than doing the aiming, and anything further " +
                 "is a straight shot you lead yourself - or a dumb seeker if it drifts near one.")]
        // Renamed from `lockRange` on purpose: the prefab carries `lockRange: 20` from the last
        // time this was tuned, and Unity binds serialized values by name - so changing only the
        // default here would have been overridden by the prefab, exactly as it was the first time
        // this value was cut. A new name has no stale value to inherit.
        [SerializeField] private float lockDistance = 30f;

        [Header("Aim Assist (laser)")]
        [Tooltip("Bend the laser onto an enemy inside the reticle circle. Off makes the light " +
                 "attack a pure one-pixel hitscan, which in 6DoF at 30 m/s is a different game.")]
        [SerializeField] private bool aimAssist = true;

        [Tooltip("Half-angle of the assist cone. This is what the on-screen circle draws, so the " +
                 "promise it makes - inside the ring is a hit - is literally true. Wider than the " +
                 "rocket's lock cone, because the laser is the shot you are expected to land " +
                 "constantly rather than the one you commit to.")]
        [Range(1f, 45f)]
        [SerializeField] private float assistAngleDegrees = 20f;

        /// <summary>Read by the reticle so the drawn circle matches the rule exactly.</summary>
        public float AssistAngleDegrees => assistAngleDegrees;

        [Tooltip("Beyond this the laser is unassisted. Shorter than the bolt's own 300m range on " +
                 "purpose: assist at arena distance would make the laser a hitscan aimbot, and the " +
                 "point is to forgive close-quarters tumbling, not to remove aiming.")]
        [SerializeField] private float assistRange = 70f;

        [Tooltip("Layers that break line of sight. Arena geometry should be in here; players " +
                 "should not, or teammates would shield each other.")]
        [SerializeField] private LayerMask sightBlockers = 1 << 6;

        [Tooltip("Layer players live on, used to find lock candidates.")]
        [SerializeField] private LayerMask playerLayer = 1 << 9;

        [Tooltip("How far the aim ray reaches before falling back to a point straight ahead.")]
        [SerializeField] private float aimDistance = 300f;

        [Tooltip("What the aim ray can land on. Arena geometry and players.")]
        [SerializeField] private LayerMask aimMask = (1 << 6) | (1 << 9);

        [Header("Debug")]
        [Tooltip("Logs what the lock picked and what the host resolved it to.")]
        [SerializeField] private bool logLocks = true;

        [Header("Presentation")]
        [Tooltip("Muzzle flash, fired locally and never networked.")]
        [SerializeField] private GameObject muzzleFlashPrefab;

        private Rigidbody _body;
        private PlayerZeroGMovement _movement;
        private Game.Networking.PlayerVitals _vitals;
        private Camera _camera;
        private float _nextFireTime;

        private void Awake()
        {
            _movement = GetComponentInChildren<PlayerZeroGMovement>(true);
            if (_movement != null)
            {
                _body = _movement.GetComponent<Rigidbody>();

                // Dashing cancels the windup. Subscribed to the movement's own event rather than
                // polled, so the charge dies in the same frame the impulse lands and there is no
                // window where a player is both dashing and still holding a valid charge.
                _movement.OnDashPerformed += HandleDash;
            }

            _vitals = GetComponentInParent<Game.Networking.PlayerVitals>();
            ResolveWiring();
        }

        /// <summary>
        /// An override, not a fresh Unity message: NetworkIdentity declares OnDestroy virtual and
        /// does its own despawn bookkeeping in it. Declaring a private one here would hide that
        /// and quietly skip PurrNet's cleanup.
        /// </summary>
        protected override void OnDestroy()
        {
            base.OnDestroy();

            if (_movement != null)
                _movement.OnDashPerformed -= HandleDash;
        }

        /// <summary>
        /// Dashing while winding up throws the charge away: the clock restarts from zero and the
        /// brake lifts, so the release that follows fires nothing.
        ///
        /// This is the trade the charge economy needs. Without it the heavy attack had no downside
        /// at all - you could hold fire, dash out of every rocket aimed at you, and still land the
        /// launch, which made the brake a formality rather than a commitment. Now the mobility you
        /// give up is real: escaping costs you the shot, and keeping the shot means standing in it.
        ///
        /// The hold restarts rather than locking out, so a player who keeps the button down starts
        /// winding up again immediately and the laser resumes in the meantime.
        /// </summary>
        private void HandleDash(Vector3 direction, bool fromSurface)
        {
            if (!isOwner || _holdStartedAt < 0f)
                return;

            _holdStartedAt = Time.time;
            SetBraking(false);
        }

        [Header("Charge")]
        // There used to be a `chargeTime` dead zone here - 1.3s of holding before the windup
        // "began" - and it existed solely to tell a tap from a hold while the laser and the rocket
        // shared one button. The rocket has its own button now, so pressing it can only mean one
        // thing, and the dead zone had become 1.3 seconds in which the game did not acknowledge the
        // press at all: no brake, no ring, nothing, and then the charge ring popped in already
        // half-shrunk. The windup now starts on the frame you press.
        [Tooltip("Seconds of holding before the heavy can be fired at all. Release one frame early " +
                 "and the whole windup is lost. This is the real cost of the shot.")]
        [SerializeField] private float fullChargeTime = 3f;

        [Tooltip("Force brake/precision mode while charging. You keep control but move slowly, " +
                 "which is the cost of the heavy attack: there is no ammo, you pay in mobility.")]
        [SerializeField] private bool brakeWhileCharging = true;

        [Tooltip("Seconds after firing a heavy before another can be charged. Stops spamming " +
                 "without capping how many you may have in the air.")]
        [SerializeField] private float heavyCooldown = 3.5f;

        private Blaster _blaster;
        private float _holdStartedAt = -1f;
        private bool _braking;

        /// <summary>
        /// True while the heavy attack is recharging.
        ///
        /// Server-authoritative, and the whole ammo economy: no counter, no reserve, no pickups -
        /// just a rhythm. A cooldown rather than a one-in-flight cap, so the weapon is always
        /// eventually available and the limit is how OFTEN you may throw it, not how many exist.
        /// </summary>
        private SyncVar<bool> _heavyOnCooldown = new(false, ownerAuth: false);

        public bool HeavyOnCooldown => _heavyOnCooldown.value;

        /// <summary>Seconds left on the cooldown, for the HUD. Local estimate.</summary>
        public float CooldownRemaining =>
            Mathf.Max(0f, _cooldownEndsAt - Time.time);

        /// <summary>True while the player is braked mid-charge. Read by the HUD.</summary>
        public bool IsBraking => _braking;

        private float _cooldownEndsAt;

        /// <summary>
        /// 0 the frame the button goes down, climbing to 1 at full charge. Read by the HUD.
        ///
        /// Starts from the press rather than from a threshold part-way in, so the ring the HUD
        /// draws from this begins moving immediately. It used to sit at exactly 0 for the first
        /// 1.3 seconds, which read as the button not having registered.
        /// </summary>
        public float ChargeFraction =>
            _holdStartedAt < 0f
                ? 0f
                : Mathf.Clamp01((Time.time - _holdStartedAt) / Mathf.Max(0.01f, fullChargeTime));

        /// <summary>True from the frame the rocket button goes down until it is released.</summary>
        public bool IsCharging => _holdStartedAt >= 0f;

        /// <summary>
        /// The only state from which a heavy can be fired.
        ///
        /// The shot is all-or-nothing: there is no weak version for a short hold. A partial charge
        /// would have made the brake cheap - you could tap out a half-strength launch and keep your
        /// mobility - which is exactly the trade the no-ammo economy is built on. So the windup is
        /// either paid in full or wasted.
        /// </summary>
        public bool IsFullyCharged =>
            _holdStartedAt >= 0f && Time.time - _holdStartedAt >= fullChargeTime;

        /// <summary>
        /// Called every frame the fire button is held, by PlayerShooter.
        ///
        /// Left mouse is now the laser and nothing else: holding it keeps shooting at the Blaster's
        /// own interval. The heavy used to share this button through a tap/hold split, which meant
        /// every sustained burst eventually stopped firing and started a windup you had not asked
        /// for - the two readings of "holding fire" were in direct conflict. The rocket moved to its
        /// own button (<see cref="OnRocketHeld"/>), so each button now means one thing.
        /// </summary>
        public void OnFireHeld()
        {
            if (!isOwner)
                return;

            if (_vitals != null && !_vitals.Vitals.IsAlive)
                return;

            // Not while winding up a rocket. The windup is supposed to be the moment you are
            // committed and vulnerable - that is what the brake is for - and letting the laser run
            // through it meant holding both buttons was strictly better than either, with no cost
            // to the charge at all. One or the other.
            if (IsCharging)
                return;

            FireLaser();
        }

        /// <summary>
        /// The laser, with aim assist inside the reticle circle.
        ///
        /// The assist is why the circle is drawn at all: anything inside it is a shot that lands.
        /// Without it the ring was decoration around a one-pixel hitscan, which in a game where
        /// both players are tumbling through six axes at 30 m/s asked for precision nobody can
        /// deliver - and the rocket's own lock made the heavy *easier* to land than the light.
        ///
        /// Only the direction is bent, and only onto a target the shooter could already see: the
        /// cone test and the line-of-sight check are the same ones the rocket lock uses, so it
        /// cannot curve a bolt around cover.
        /// </summary>
        private void FireLaser()
        {
            if (_blaster == null)
                _blaster = GetComponentInChildren<Blaster>(true);

            if (_blaster == null || !TryGetAimRay(out var origin, out var direction))
                return;

            if (aimAssist && TryFindTargetInCone(origin, direction, assistAngleDegrees,
                    assistRange, out _, out var point))
                direction = (point - origin).normalized;

            _blaster.Fire(origin, direction);
        }

        /// <summary>
        /// Slows the player while they wind up, rather than freezing them.
        ///
        /// Reuses the existing brake/precision mode - the same thing the brake button does -
        /// so a charging player keeps control at ~40% speed and reduced acceleration instead of
        /// being rooted. Freezing outright was too punishing with three other players shooting,
        /// and it also killed the booster visuals, which is exactly the telegraph that should be
        /// showing while someone commits.
        ///
        /// Applied locally because the body is owner-authoritative; other players read the
        /// slowdown through normal transform sync.
        /// </summary>
        private void SetBraking(bool braking)
        {
            if (!brakeWhileCharging || _braking == braking)
                return;

            _braking = braking;

            if (_movement != null)
                _movement.ExternalBrake = braking;
        }

        /// <summary>
        /// Called when the fire button is released. Launches only a fully charged shot; anything
        /// shorter is discarded, and the next press starts the windup over from zero.
        ///
        /// Letting a partial hold fire a weaker shot meant the heavy was never really committed to:
        /// the sensible play was to half-charge and release the instant anyone looked at you, which
        /// is the brake dodged rather than paid. Making the threshold binary is what gives the
        /// windup teeth - either you stand in it for the full duration, or you got nothing.
        /// </summary>
        public void OnFireReleased()
        {
            // Nothing to do: the laser is stateless and the windup lives on the rocket button now.
            // Kept so PlayerShooter's release path does not need to know that.
        }

        /// <summary>
        /// Called every frame the rocket button (right mouse) is held.
        ///
        /// This is where the charge economy moved to, unchanged: the windup brakes you to ~40%,
        /// the shot only exists at full charge, and a dash throws the whole thing away. What
        /// changed is only which button pays for it - sharing the laser's button meant you could
        /// not keep up suppressing fire without accidentally committing to a heavy.
        /// </summary>
        public void OnRocketHeld()
        {
            if (!isOwner)
                return;

            // Dying mid-windup cancels it. Without this the brake flag survives the death and the
            // player respawns permanently slowed, because SetBraking thinks it is already applied.
            if (_vitals != null && !_vitals.Vitals.IsAlive)
            {
                _holdStartedAt = -1f;
                SetBraking(false);
                return;
            }

            // On cooldown there is nothing to charge toward. The clock is deliberately left
            // unstarted: charge cannot be banked during the cooldown and then cashed in the instant
            // it lifts - the full windup is paid after.
            if (_heavyOnCooldown.value)
                return;

            if (_holdStartedAt < 0f)
                _holdStartedAt = Time.time;

            // Immediately, not after a threshold. Pressing the rocket button *is* the commitment,
            // and the slowdown is the feedback that says so - waiting to apply it also handed out
            // over a second of charging at full speed for free.
            SetBraking(true);
        }

        /// <summary>
        /// Called when the rocket button is released. Launches only a fully charged shot; anything
        /// shorter is discarded, and the next press starts the windup over from zero.
        ///
        /// Letting a partial hold fire a weaker shot meant the heavy was never really committed to:
        /// the sensible play was to half-charge and release the instant anyone looked at you, which
        /// is the brake dodged rather than paid. Making the threshold binary is what gives the
        /// windup teeth - either you stand in it for the full duration, or you got nothing.
        /// </summary>
        public void OnRocketReleased()
        {
            if (!isOwner)
                return;

            bool ready = IsFullyCharged;

            _holdStartedAt = -1f;
            SetBraking(false);

            if (ready && !_heavyOnCooldown.value)
            {
                // Local estimate so the HUD can count down without waiting for the server.
                _cooldownEndsAt = Time.time + heavyCooldown;
                TryFire();
            }
        }

        /// <summary>True when a heavy could be charged right now. Read by bots.</summary>
        public bool HeavyAvailable => !_heavyOnCooldown.value;

        /// <summary>
        /// Host-side charge and fire, for a bot.
        ///
        /// The owner paths are gated on isOwner and a bot has no owner to be - but the economy is
        /// not bypassed, which is the point. Holding this brakes the bot through the same
        /// ExternalBrake a player pays, the shot only exists once the full windup has elapsed, and
        /// letting go early throws it away. A bot that could fire heavies for free would not be
        /// playing the game the player is playing.
        ///
        /// Returns true while the windup is still running, so the caller knows it is committed.
        /// </summary>
        public bool HoldRocketFromHost()
        {
            if (!isServer || !isSpawned)
                return false;

            if (_vitals != null && !_vitals.Vitals.IsAlive)
            {
                ReleaseRocketFromHost();
                return false;
            }

            if (_heavyOnCooldown.value)
            {
                ReleaseRocketFromHost();
                return false;
            }

            if (_holdStartedAt < 0f)
                _holdStartedAt = Time.time;

            SetBraking(true);
            return !IsFullyCharged;
        }

        /// <summary>
        /// Lets go. Fires if the windup completed, discards it otherwise - the same all-or-nothing
        /// rule a player gets.
        /// </summary>
        public void ReleaseRocketFromHost()
        {
            if (!isServer)
                return;

            bool ready = IsFullyCharged;

            _holdStartedAt = -1f;
            SetBraking(false);

            if (!ready || _heavyOnCooldown.value)
                return;

            _cooldownEndsAt = Time.time + heavyCooldown;
            FireFromHost();
        }

        /// <summary>
        /// The spawn itself, host-side. Mirrors TryFire without its isOwner gate: the lock is
        /// resolved here because the host is the one holding the bot's aim, and RequestFire is a
        /// ServerRpc being invoked on the server, so it simply executes.
        /// </summary>
        private void FireFromHost()
        {
            if (Time.time < _nextFireTime)
                return;

            if (firePoint == null || !TryGetAimRay(out var origin, out var direction))
                return;

            _nextFireTime = Time.time + fireInterval;

            ApplyRecoil(direction);
            PlayMuzzleFlash(origin, direction);

            var target = FindLockTarget(origin, direction);
            RequestFire(origin, direction, target ?? default, target.HasValue);
        }

        /// <summary>
        /// Fires the charged energy ball. Safe to call every frame; it rate-limits itself.
        ///
        /// Takes no charge strength: the shot only exists at full charge, so there is nothing to
        /// scale. It used to accept a fraction that nothing read - a parameter describing a weak
        /// shot the game never fired.
        /// </summary>
        public void TryFire()
        {
            if (!isOwner || !isSpawned || Time.time < _nextFireTime)
                return;

            // Checked locally for responsiveness; the host re-checks nothing here because a dead
            // player's rocket is a cosmetic problem, not an exploit - P4's authority is over who
            // dies, not who shoots. Revisit if firing ever costs ammo that matters post-mortem.
            if (_vitals != null && !_vitals.Vitals.IsAlive)
                return;

            if (firePoint == null || aimTarget == null)
                return;

            if (!TryGetAimRay(out var origin, out var direction))
                return;

            _nextFireTime = Time.time + fireInterval;

            // Local and immediate: recoil and muzzle flash belong to the shooter's own frame.
            ApplyRecoil(direction);
            PlayMuzzleFlash(origin, direction);

            // Only the PlayerID travels. The host resolves it to a body it already owns, so a
            // client cannot nominate an arbitrary object as a homing target.
            var target = FindLockTarget(origin, direction);

            if (logLocks)
            {
                Debug.Log(target.HasValue
                    ? $"[Launcher] {owner} fired at {origin} dir {direction} LOCKED {target.Value}"
                    : $"[Launcher] {owner} fired at {origin} dir {direction} no lock");
            }

            RequestFire(origin, direction, target ?? default, target.HasValue);
        }

        /// <summary>
        /// The ray the lock is tested along: from the muzzle toward the aim point.
        ///
        /// Single source of truth for both firing and the reticle. They used to compute this
        /// separately - the reticle from the camera, the launcher from the fire point - so the
        /// circle on screen and the weapon were testing different cones from different origins,
        /// and the rocket would lock someone other than whoever you had circled.
        /// </summary>
        public bool TryGetAimRay(out Vector3 origin, out Vector3 direction)
        {
            origin = default;
            direction = default;

            if (firePoint == null)
                return false;

            origin = firePoint.position;

            // Aimed from the camera, NOT from Aim.cs's Target transform.
            //
            // That Target is lerped toward the crosshair at speed 10 so the spine rig bends
            // smoothly - deliberately laggy, and correct for animation. Using it to aim the
            // weapon meant that after a kill, while the Target glided from the dead player toward
            // your new crosshair, the lock kept testing a direction pointing at where the old
            // target used to be. The rocket then homed on the previous position.
            if (_camera == null)
                _camera = GetComponentInChildren<Camera>(true);

            if (_camera == null)
            {
                direction = firePoint.forward;
                return true;
            }

            var ray = _camera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
            Vector3 aimPoint = Physics.Raycast(ray, out var hit, aimDistance, aimMask,
                QueryTriggerInteraction.Ignore)
                ? hit.point
                : ray.origin + ray.direction * aimDistance;

            direction = aimPoint - origin;

            if (direction.sqrMagnitude < 0.0001f)
            {
                direction = _camera.transform.forward;
                return true;
            }

            direction.Normalize();
            return true;
        }

        /// <summary>
        /// What the heavy shot would currently home onto, polled a few times a second.
        ///
        /// Cached here rather than tested by each HUD piece. The test costs an OverlapSphere plus a
        /// raycast per candidate, and two consumers running it independently could also land on
        /// different answers in the same frame - the reticle saying a lock exists while the enemy
        /// markers highlight nobody. One poll, one answer.
        /// </summary>
        public PlayerID? CurrentLock { get; private set; }

        private float _nextLockPoll;

        private void Update()
        {
            // Owner-only: a remote copy has no camera to aim from, and nothing reads its lock.
            if (!isOwner || !isSpawned)
                return;

            if (Time.time < _nextLockPoll)
                return;

            _nextLockPoll = Time.time + 0.1f;
            CurrentLock = FindLockTarget();
        }

        /// <summary>Convenience for callers that just want to know what is currently locked.</summary>
        public PlayerID? FindLockTarget()
        {
            return TryGetAimRay(out var origin, out var direction)
                ? FindLockTarget(origin, direction)
                : null;
        }

        /// <summary>
        /// The best enemy inside the lock cone with clear line of sight, or null.
        ///
        /// Run on the shooter because the cone is defined by what the shooter could see when they
        /// fired. "Best" is the most centred rather than the nearest: aiming at someone should
        /// beat merely being close to someone.
        /// </summary>
        public PlayerID? FindLockTarget(Vector3 origin, Vector3 direction)
        {
            return TryFindTargetInCone(origin, direction, lockAngleDegrees, lockDistance,
                out var target, out _)
                ? target
                : null;
        }

        /// <summary>
        /// One cone search, two callers: the rocket's lock and the laser's aim assist.
        ///
        /// They differ only in angle and range, and sharing the body is what keeps them honest
        /// about the same things - the self-skip, the alive check and the line-of-sight test. Two
        /// copies would have drifted, and the one that drifted would be the one that let you lock
        /// through a wall.
        ///
        /// Also returns where to aim, because the laser needs a point and not just an id.
        /// </summary>
        private bool TryFindTargetInCone(Vector3 origin, Vector3 direction, float angleDegrees,
            float range, out PlayerID target, out Vector3 point)
        {
            target = default;
            point = default;

            var candidates = Physics.OverlapSphere(origin, range, playerLayer,
                QueryTriggerInteraction.Ignore);

            float cosLimit = Mathf.Cos(angleDegrees * Mathf.Deg2Rad);
            float bestDot = cosLimit;
            bool found = false;

            foreach (var candidate in candidates)
            {
                // Resolved through PlayerVitals, which exists exactly once per player on the
                // root. The obvious GetComponentInParent<NetworkIdentity>() is wrong here: the
                // body carries a NetworkTransform, which IS a NetworkIdentity, so it returned
                // that instead of the root's NetworkPlayer. Those are separate identities with
                // separately propagated ownership - enough for the self-skip below to miss and
                // for a player to lock onto their own body.
                var vitals = candidate.GetComponentInParent<Game.Networking.PlayerVitals>();
                if (vitals == null)
                    continue;

                var candidateOwner = vitals.owner;
                if (!candidateOwner.HasValue)
                    continue;

                // Two independent self-checks: by owner id, and by object identity in case
                // ownership is momentarily unset right after a respawn.
                if (candidateOwner == owner || vitals.transform == transform)
                    continue;

                if (!vitals.Vitals.IsAlive)
                    continue;

                Vector3 toTarget = candidate.bounds.center - origin;
                float distance = toTarget.magnitude;
                if (distance < 0.01f)
                    continue;

                float dot = Vector3.Dot(direction, toTarget / distance);
                if (dot <= bestDot)
                    continue;

                if (Physics.Raycast(origin, toTarget / distance, distance, sightBlockers,
                        QueryTriggerInteraction.Ignore))
                    continue;

                bestDot = dot;
                target = candidateOwner.Value;
                point = candidate.bounds.center;
                found = true;
            }

            return found;
        }

        private void ApplyRecoil(Vector3 direction)
        {
            if (_body == null)
                return;

            _body.AddForce(-direction * recoilImpulse, ForceMode.VelocityChange);
        }

        private void PlayMuzzleFlash(Vector3 origin, Vector3 direction)
        {
            if (muzzleFlashPrefab == null)
                return;

            var flash = Instantiate(muzzleFlashPrefab, origin, Quaternion.LookRotation(direction));
            Destroy(flash, 2f);
        }

        /// <summary>
        /// The host spawns the rocket, so a client cannot conjure one, and every client sees the
        /// same object from the same instant.
        ///
        /// Origin and direction come from the shooter because only the shooter knows where it was
        /// aiming when it pulled the trigger - by the time this arrives it has already drifted.
        /// That is the cheap half of lag compensation and it matters more than rewinding targets
        /// does for a slow splash projectile.
        /// </summary>
        [ServerRpc(requireOwnership: true)]
        private void RequestFire(Vector3 origin, Vector3 direction, PlayerID target, bool hasTarget)
        {
            if (rocketPrefab == null)
            {
                Debug.LogError("[RocketLauncher] No rocket prefab assigned; nothing to fire.");
                return;
            }

            if (direction.sqrMagnitude < 0.001f)
                return;

            // Re-checked on the host, not trusted from the client. The flag is the entire ammo
            // rule, so a client that ignored it could otherwise spam heavy attacks.
            if (_heavyOnCooldown.value)
                return;

            var rocket = Instantiate(rocketPrefab, origin, Quaternion.LookRotation(direction.normalized));
            _heavyOnCooldown.value = true;

            var component = rocket.GetComponent<Rocket>();
            if (component == null)
            {
                Debug.LogError("[RocketLauncher] Rocket prefab has no Rocket component.");
                return;
            }

            component.Configure(owner, armingDelay, hasTarget ? ResolveTargetBody(target) : null);

            // Timed from the shot, NOT from the projectile resolving. Tying it to the projectile
            // meant a rocket that chased someone for ten seconds locked the weapon for ten
            // seconds, which punished the shots that were working.
            StartCoroutine(CooldownRoutine());
        }

        private System.Collections.IEnumerator CooldownRoutine()
        {
            yield return new WaitForSeconds(heavyCooldown);

            if (isServer)
                _heavyOnCooldown.value = false;
        }

        /// <summary>
        /// Host-side. Turns the shooter's nominated PlayerID into the body the host already owns,
        /// so the rocket homes on a real player rather than whatever the client pointed at.
        /// Returns the moving child, not the root, because the root never moves.
        /// </summary>
        private Transform ResolveTargetBody(PlayerID target)
        {
            var spawner = FindFirstObjectByType<Game.Networking.PlayerSpawner>();
            if (spawner == null || !spawner.TryGetBody(target, out var body))
            {
                Debug.LogWarning($"[Launcher] Host could not resolve a body for locked target {target}; " +
                                 "rocket will fly straight.");
                return null;
            }

            var movement = body.GetComponentInChildren<PlayerZeroGMovement>(true);
            var result = movement != null ? movement.transform : body.transform;

            if (logLocks)
                Debug.Log($"[Launcher] Host resolved target {target} -> '{result.name}' at {result.position}");

            return result;
        }

        /// <summary>
        /// Finds the fire point and aim target without inspector wiring, so reorganising the deep
        /// player prefab does not silently break firing.
        /// </summary>
        private void ResolveWiring()
        {
            if (aimTarget == null)
            {
                var aim = GetComponentInChildren<Aim>(true);
                if (aim != null && aim.aimIndicatorPrefab != null)
                    aimTarget = aim.aimIndicatorPrefab.transform;
            }

            if (firePoint == null)
            {
                var weapon = GetComponentInChildren<Weapon>(true);
                if (weapon != null && weapon.firePoint != null)
                    firePoint = weapon.firePoint;
            }

            if (firePoint == null)
                Debug.LogWarning($"[RocketLauncher] No fire point found on {name}; rockets cannot be fired.");

            if (aimTarget == null)
                Debug.LogWarning($"[RocketLauncher] No aim target found on {name}; rockets cannot be aimed.");
        }
    }
}
