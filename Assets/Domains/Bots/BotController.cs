using Game.Networking;
using Game.Weapons;
using UnityEngine;

namespace Game.Bots
{
    /// <summary>
    /// Flies a player body as an opponent. Host-only.
    ///
    /// Deliberately not a second character: it drives the *same* rig a human does, by writing the
    /// values <see cref="PlayerInputHandler"/> would have received from a device and by rotating the
    /// camera rig that the weapons already aim along. So a bot gets the real movement model - the
    /// same momentum, the same brake, the same surface dashes - and any tuning done for players
    /// applies to it unchanged. A bespoke flight model would have been easier to write and would
    /// have made every movement change a thing to port twice.
    ///
    /// It runs only on the host because the host is the authority. Its body has no owner, so there
    /// is no client to predict it and nothing to reconcile: other peers see it exactly as they see
    /// any remote player, through the transform stream.
    ///
    /// The aim trick is worth knowing: the camera rig GameObject is *inactive* on a bot, since
    /// nothing should render from it, but a disabled GameObject's transform can still be rotated and
    /// <c>GetComponentInChildren&lt;Camera&gt;(true)</c> still finds the camera on it. So rotating
    /// that transform aims the weapons without a second camera existing anywhere.
    /// </summary>
    [RequireComponent(typeof(PlayerVitals))]
    public class BotController : MonoBehaviour
    {
        [SerializeField] private BotProfile profile;

        [Header("Avoidance")]
        [Tooltip("How far ahead it looks for geometry. Wants to be about a second of travel - too " +
                 "short and it clips corners, too long and it flinches at walls it was never going " +
                 "to reach.")]
        [SerializeField] private float lookAhead = 14f;

        [Tooltip("Radius of the probe. Roughly the body, plus margin for the fact that it is " +
                 "steering rather than braking.")]
        [SerializeField] private float probeRadius = 2.5f;

        [Tooltip("Layers that count as something to fly around. Arena geometry, not players - " +
                 "bodies are what it is trying to reach.")]
        [SerializeField] private LayerMask obstacleMask = 1 << 6;

        [Tooltip("How much of the thrust may go sideways or vertically rather than along the nose. " +
                 "Low values fly like an aircraft - it has to turn to go somewhere, which is what " +
                 "makes the turn readable. 1 restores free 6DoF sliding.")]
        [Range(0.05f, 1f)]
        [SerializeField] private float lateralThrust = 0.35f;

        [Tooltip("How much less attractive another bot is than a person, as a distance multiplier. " +
                 "1 makes them indifferent, which is what had three of them orbiting each other " +
                 "and ignoring the player.")]
        [Range(1f, 6f)]
        [SerializeField] private float botPreference = 2.5f;

        [Header("Roaming")]
        [Tooltip("How far from the arena centre it will wander when it has nobody to fight. " +
                 "Without this a bot with no target simply stops, which reads as broken.")]
        [SerializeField] private float roamRadius = 40f;

        [Tooltip("Seconds before it picks a new place to wander to, whether or not it arrived.")]
        [SerializeField] private float roamInterval = 6f;

        [Header("Firing")]
        [Tooltip("Degrees it must be within before it shoots at all. Stops it spraying across the " +
                 "arena while it is still turning.")]
        [SerializeField] private float fireConeDegrees = 25f;

        public BotProfile Profile => profile;

        /// <summary>Assigned at spawn by whatever created this bot.</summary>
        public void SetProfile(BotProfile value) => profile = value;

        private PlayerVitals _vitals;
        private PlayerZeroGMovement _movement;
        private PlayerInputHandler _input;
        private Blaster _blaster;
        private RocketLauncher _launcher;
        private Transform _rig;

        private PlayerVitals _target;
        private float _nextTargetScan;
        private float _secondsOnTarget;
        private Vector3 _aimJitter;
        private Vector3 _jitterTarget;
        private float _nextJitter;
        private Vector3 _roamPoint;
        private float _nextRoam;

        private void Awake()
        {
            _vitals = GetComponent<PlayerVitals>();
            _movement = GetComponentInChildren<PlayerZeroGMovement>(true);
            _input = GetComponentInChildren<PlayerInputHandler>(true);
            _blaster = GetComponentInChildren<Blaster>(true);
            _launcher = GetComponentInChildren<RocketLauncher>(true);

            if (_movement != null)
                _rig = _movement.cameraRig;
        }

        private void Update()
        {
            // Host-only, and only while alive. A dead bot is a parked corpse like any other.
            if (_vitals == null || !_vitals.isSpawned || !_vitals.isServer)
                return;

            if (profile == null || _movement == null || _input == null || _rig == null)
                return;

            // Asserted rather than assumed. NetworkPlayer's gating caches its answer, and this
            // brain is attached in the same frame the body spawns - so whether the gating saw a
            // bot depends on which side of it the attachment landed, and losing that race leaves a
            // body that aims and fires (this component runs) but never moves (the movement script
            // stayed off). Two bool checks a frame is a cheap price for not depending on order.
            if (!_movement.enabled)
                _movement.enabled = true;

            if (!_input.enabled)
                _input.enabled = true;

            if (!_vitals.Vitals.IsAlive)
            {
                // Released explicitly, not just abandoned. A windup in progress has already set
                // ExternalBrake, and that flag survives death - so a bot that died mid-charge would
                // respawn permanently slowed, which is the exact trap the launcher's own death
                // check exists to avoid for players.
                _charging = false;
                _launcher?.ReleaseRocketFromHost();

                _input.Drive(Vector2.zero, 0f, 0f, false, false);
                return;
            }

            AcquireTarget();

            var self = _vitals.MarkerPosition;
            Vector3 desired = Steer(self, out float distance);

            // Aim before moving, because the nose is also the thrust axis now - the jet model
            // below pushes along the rig's forward, so the rotation has to be this frame's.
            Aim(self, desired, Time.deltaTime);
            ApplyMovement(desired);
            Shoot(self, distance);
        }

        // =====================================================================
        //  Perception
        // =====================================================================

        /// <summary>
        /// Picks the nearest living opponent, re-checked on the profile's reaction time rather than
        /// every frame - so a bot cannot switch targets faster than it could notice.
        /// </summary>
        private void AcquireTarget()
        {
            bool targetGone = _target == null || !_target.isSpawned || !_target.Vitals.IsAlive;

            if (!targetGone && Time.time < _nextTargetScan)
                return;

            _nextTargetScan = Time.time + Mathf.Max(0.1f, profile.reactionSeconds);

            var self = _vitals.MarkerPosition;
            PlayerVitals best = null;
            float bestScore = float.MaxValue;

            foreach (var candidate in PlayerVitals.All)
            {
                if (candidate == null || candidate == _vitals || !candidate.isSpawned)
                    continue;

                if (!candidate.Vitals.IsAlive)
                    continue;

                float score = Vector3.Distance(self, candidate.MarkerPosition);

                // Scored, not simply nearest. "Nearest" is what made three bots flock: each one's
                // closest enemy was another bot, so they converged on each other and orbited in a
                // knot while the player watched. A bot has to be this much closer than a person
                // before it is worth preferring, so they come for you and fight each other only
                // when you are genuinely far away.
                if (candidate.owner.HasValue && candidate.owner.Value.isBot)
                    score *= botPreference;

                // Stickiness, so a bot does not swap target every time two candidates trade places
                // a metre apart - that produced the circling as much as the scoring did.
                if (candidate == _target)
                    score *= 0.7f;

                if (score >= bestScore)
                    continue;

                bestScore = score;
                best = candidate;
            }

            if (best != _target)
                _secondsOnTarget = 0f;

            _target = best;
        }

        // =====================================================================
        //  Steering
        // =====================================================================

        /// <summary>
        /// Where it wants to be going, as a **velocity** rather than a direction.
        ///
        /// This distinction is the whole difference between a bot that flies and one that bashes
        /// around the level. Steering toward a direction and thrusting along it at full tilt is
        /// fine on the ground, where friction does the braking; in zero-g with momentum
        /// conservation it means accelerating forever, overshooting every approach and ricocheting
        /// off the geometry - which is exactly what it did. CLAUDE.md calls managing your own
        /// momentum the core skill of this game, and a bot that never brakes is not playing it.
        ///
        /// So the output is a target velocity, and <see cref="ApplyMovement"/> thrusts along the
        /// *difference* between that and the current one. Slowing down, turning hard and stopping
        /// all fall out of the same subtraction.
        /// </summary>
        private Vector3 Steer(Vector3 self, out float distance)
        {
            distance = 0f;

            Vector3 wish = Vector3.zero;

            if (_target == null)
            {
                // Nothing to fight, so go somewhere. A bot with no target used to produce a zero
                // wish and simply hold still, which reads as broken rather than as idle.
                if (Time.time >= _nextRoam || (_roamPoint - self).sqrMagnitude < 36f)
                {
                    _nextRoam = Time.time + Mathf.Max(1f, roamInterval);
                    _roamPoint = Random.insideUnitSphere * roamRadius;
                }

                wish = (_roamPoint - self).normalized;
            }
            else
            {
                Vector3 toTarget = _target.MarkerPosition - self;
                distance = toTarget.magnitude;

                if (distance > 0.01f)
                {
                    Vector3 forward = toTarget / distance;

                    // Aggression pulls the fighting range in. A timid bot sits out past its
                    // preferred range; a committed one wants to be inside it.
                    float wanted = profile.preferredRange * Mathf.Lerp(1.4f, 0.55f, profile.aggression);

                    // Signed: positive closes, negative backs off. Normalised by the range so the
                    // response is proportional rather than binary at the threshold, and damped
                    // near the band so it settles instead of oscillating across it.
                    float error = (distance - wanted) / Mathf.Max(1f, wanted);
                    wish = forward * Mathf.Clamp(error, -1f, 1f);

                    // Orbit, around an axis that is stable for this bot rather than recomputed -
                    // otherwise it reverses direction whenever the geometry nudges it and the
                    // result is a shimmy rather than a circle.
                    Vector3 axis = Vector3.Cross(forward, transform.up);
                    if (axis.sqrMagnitude < 0.01f)
                        axis = Vector3.Cross(forward, Vector3.forward);

                    wish += axis.normalized * profile.strafeBias;

                    // Caution reads its own percentage and backs off as it climbs: the same
                    // information a player uses to decide the trade is no longer worth taking.
                    float hurt = Mathf.Clamp01(_vitals.Vitals.DamagePercent / 120f);
                    wish -= forward * (hurt * profile.caution);
                }
            }

            Vector3 avoid = Probe(self, out float clearance);

            // Avoidance dominates when it fires at all. A bot that averages "toward the enemy"
            // with "away from the wall" flies into the wall slowly.
            Vector3 direction = (wish + avoid * 3f).normalized;

            // Boxed in means slow down. This is what lets it handle a corridor instead of treating
            // one as a series of collisions: speed is cut by how little room it has, so it arrives
            // at a tight space already slow enough to turn inside it.
            float ceiling = _movement.maxSpeed * Mathf.Lerp(0.3f, 1f, clearance);

            // Timid bots also cruise slower, which reads as hesitancy rather than as a worse bot.
            ceiling *= Mathf.Lerp(0.65f, 1f, profile.aggression);

            return direction * ceiling;
        }

        /// <summary>
        /// Whisker probes: ahead along velocity, plus a fan around it.
        ///
        /// Cast along velocity rather than along the body's facing, because in 6DoF those are
        /// routinely unrelated - the whole point of the movement model is that you can drift one
        /// way while looking another. Probing where it looks would miss every wall it is actually
        /// about to hit.
        ///
        /// The fan is what a single forward cast could not do: a corridor is mostly walls that are
        /// *beside* you, and something that only looks dead ahead discovers them by collision. Each
        /// hit contributes its own surface normal, weighted by how close it is, so a narrow space
        /// produces a push down its middle rather than a bounce off one side.
        /// </summary>
        private Vector3 Probe(Vector3 self, out float clearance)
        {
            clearance = 1f;

            var body = _vitals.BodyTransform;
            if (body == null)
                return Vector3.zero;

            var rigidbody = body.GetComponent<Rigidbody>();
            Vector3 velocity = rigidbody != null ? rigidbody.linearVelocity : Vector3.zero;

            // Below walking pace there is no heading to probe along, so the fan is built around the
            // way it is looking instead - it still needs to know it is in a corner while stationary.
            Vector3 heading = velocity.sqrMagnitude > 1f ? velocity.normalized : _rig.forward;

            // Scaled by speed: it should start turning earlier when it is moving faster, which is
            // also what stops the probe being useless at 30 m/s.
            float reach = lookAhead * Mathf.Clamp(velocity.magnitude / 15f, 0.6f, 2.5f);

            Vector3 right = Vector3.Cross(heading, Vector3.up);
            if (right.sqrMagnitude < 0.01f)
                right = Vector3.Cross(heading, Vector3.forward);

            right.Normalize();
            Vector3 up = Vector3.Cross(right, heading).normalized;

            _whiskers[0] = heading;
            _whiskers[1] = (heading + right * 0.7f).normalized;
            _whiskers[2] = (heading - right * 0.7f).normalized;
            _whiskers[3] = (heading + up * 0.7f).normalized;
            _whiskers[4] = (heading - up * 0.7f).normalized;

            Vector3 push = Vector3.zero;

            for (int i = 0; i < _whiskers.Length; i++)
            {
                if (!Physics.SphereCast(self, probeRadius, _whiskers[i], out var hit, reach,
                        obstacleMask, QueryTriggerInteraction.Ignore))
                    continue;

                float nearness = 1f - Mathf.Clamp01(hit.distance / Mathf.Max(0.01f, reach));

                // The forward whisker counts double: something straight ahead is the one it will
                // actually hit, and the fan exists to inform the turn rather than to drive it.
                float weight = nearness * (i == 0 ? 2f : 1f);

                // Slide along the surface rather than reflecting off it. Reflecting bounces between
                // two walls in a corridor; sliding carries the momentum it already has around the
                // obstacle, which is what a player does.
                Vector3 along = Vector3.ProjectOnPlane(_whiskers[i], hit.normal);

                push += (hit.normal + along.normalized * 0.8f) * weight;

                clearance = Mathf.Min(clearance, Mathf.Clamp01(hit.distance / Mathf.Max(0.01f, reach)));
            }

            return push.sqrMagnitude > 0.0001f ? push.normalized * (1f - clearance) : Vector3.zero;
        }

        private readonly Vector3[] _whiskers = new Vector3[5];

        /// <summary>
        /// Thrusts along the difference between the velocity it wants and the one it has.
        ///
        /// The subtraction is the point: when it is already going the right way this is near zero
        /// and it coasts, when it has overshot this points backwards and it brakes, and when it
        /// wants to turn this points across its own momentum - which is how a player turns in
        /// zero-g. Thrusting along the raw wish instead, as this did, can only ever add speed.
        ///
        /// The movement script builds its thrust from the rig's own axes, so the correction has to
        /// be inverse-transformed through the rig; handing it world directions would send the bot
        /// wherever it happened to be looking.
        /// </summary>
        private void ApplyMovement(Vector3 wishVelocity)
        {
            var body = _vitals.BodyTransform;
            var rigidbody = body != null ? body.GetComponent<Rigidbody>() : null;
            Vector3 velocity = rigidbody != null ? rigidbody.linearVelocity : Vector3.zero;

            Vector3 correction = wishVelocity - velocity;

            // Normalised against the speed cap rather than its own length, so a small correction
            // produces small input. Normalising the correction itself would mean full thrust for
            // any error at all, which is the all-or-nothing throttle it had before.
            Vector3 thrust = correction / Mathf.Max(1f, _movement.maxSpeed);
            if (thrust.sqrMagnitude > 1f)
                thrust.Normalize();

            Vector3 local = _rig.InverseTransformDirection(thrust);

            // Forward-dominant, the other half of the jet model. Full holonomic thrust is what made
            // bots slide around like they were on strings: they could satisfy any correction
            // without ever changing where they pointed, so they never appeared to turn. Slipping is
            // allowed - this is a thruster suit, not an aircraft - but it is the minority of the
            // push, so turning is how it mostly gets anywhere. Paired with the nose following the
            // flight direction above; either change alone would make it worse, not better.
            local.x *= lateralThrust;
            local.y *= lateralThrust;

            // Hold the brake when the correction is mostly a demand to *stop*. The brake trebles
            // directional braking, which is the only way to shed momentum quickly - and a bot that
            // never used it could not undo its own mistakes.
            bool fighting = velocity.sqrMagnitude > 4f &&
                            Vector3.Dot(correction.normalized, velocity.normalized) < -0.4f;

            TryDash();

            _input.Drive(new Vector2(local.x, local.z), Mathf.Clamp(local.y, -1f, 1f), 0f,
                fighting, false);
        }

        /// <summary>
        /// Kicks off a surface when one is close and the profile is good enough to notice.
        ///
        /// Dashes are the only way to redirect momentum, so this is the whole of a bot's mobility
        /// skill. Rolled against dashSkill rather than taken every time: a bot that dashes off
        /// every surface it passes is both unbeatable at repositioning and visibly robotic.
        /// </summary>
        private void TryDash()
        {
            if (profile.dashSkill <= 0f || _movement.CurrentDashCharges <= 0)
                return;

            var body = _vitals.BodyTransform;
            if (body == null)
                return;

            // The movement's *own* surface test, not an approximation of it. This used to probe a
            // 3.5m sphere while the movement only finds surfaces within surfaceCheckRadius (1.2m),
            // so most presses were made where the movement would find nothing and silently do
            // nothing. Asking the same question with the same radius and the same mask means a
            // press is only made when it will actually produce a dash.
            if (!Physics.CheckSphere(body.position, _movement.surfaceCheckRadius * 1.3f,
                    _movement.surfaceMask, QueryTriggerInteraction.Ignore))
                return;

            // Per-second chance rather than per-frame, so the behaviour does not change with
            // framerate - the mistake that makes bots feel different on different machines.
            if (Random.value >= profile.dashSkill * Time.deltaTime * 3f)
                return;

            // Through the latch rather than the input flag: DashPressed is cleared in LateUpdate
            // but read in the movement's Update, so a press set from here was only seen when the
            // component order happened to favour it.
            _movement.RequestDash();
        }

        // =====================================================================
        //  Aiming and firing
        // =====================================================================

        /// <summary>
        /// Swings the rig toward the target at the profile's turn rate, with error layered on.
        ///
        /// Rate-limited rather than snapped, because a bot that is simply inaccurate still tracks
        /// perfectly - it misses *around* you while always facing you, which reads as cheating. A
        /// bot that cannot keep up with a fast crossing target can be genuinely outmanoeuvred, and
        /// that is the difference between a difficulty number and an opponent.
        /// </summary>
        private void Aim(Vector3 self, Vector3 wishVelocity, float delta)
        {
            if (_target == null)
                return;

            _secondsOnTarget += delta;

            // A new error *direction* is chosen on the reaction clock, but the aim drifts toward it
            // rather than snapping to it. Snapping is what made the aim look wrong: the target
            // orientation jumped every reaction interval and the rig chased a teleporting point, so
            // it read as a cursor being flicked by a script. Drifting gives the slow sway of a hand
            // that is tracking and not quite keeping up, which is what a person's aim looks like.
            if (Time.time >= _nextJitter)
            {
                _nextJitter = Time.time + Mathf.Max(0.05f, profile.reactionSeconds);
                _jitterTarget = Random.onUnitSphere;
            }

            _aimJitter = Vector3.Slerp(_aimJitter.sqrMagnitude < 0.01f ? _jitterTarget : _aimJitter,
                _jitterTarget, 1f - Mathf.Exp(-3f * delta));

            Vector3 toTarget = (_target.MarkerPosition - self).normalized;

            // The nose flies the aircraft. When where it wants to go and where its target is
            // disagree, the nose follows the *flight* direction and brings the guns round after -
            // which is what a fighter does, and what makes the turn visible. When they agree it
            // takes the target so it can shoot. Pointing permanently at the target while thrusting
            // sideways is what made them slide about without ever appearing to turn.
            Vector3 flight = wishVelocity.sqrMagnitude > 0.01f
                ? wishVelocity.normalized
                : toTarget;

            float agreement = Vector3.Dot(flight, toTarget);
            float onTarget = Mathf.InverseLerp(-0.2f, 0.8f, agreement);

            Vector3 nose = Vector3.Slerp(flight, toTarget, onTarget).normalized;

            float error = profile.AimErrorAt(_secondsOnTarget);
            Vector3 wanted = Quaternion.AngleAxis(error, _aimJitter) * nose;

            var look = Quaternion.LookRotation(wanted, Vector3.up);
            _rig.rotation = Quaternion.RotateTowards(_rig.rotation, look,
                profile.aimSpeedDegrees * delta);
        }

        /// <summary>
        /// Fires the laser when it is pointed close enough and can actually see the target.
        ///
        /// Host-side firing through <see cref="Blaster.FireFromHost"/>: the owner path is gated on
        /// isOwner and a bot has no owner to be, and more to the point the host does not need the
        /// claim-and-validate dance - it is already resolving against the authoritative world.
        /// </summary>
        private void Shoot(Vector3 self, float distance)
        {
            if (_target == null || distance <= 0.01f)
            {
                _launcher?.ReleaseRocketFromHost();
                return;
            }

            Vector3 toTarget = (_target.MarkerPosition - self).normalized;

            bool aimed = Vector3.Angle(_rig.forward, toTarget) <= fireConeDegrees;

            // Line of sight, so it does not shoot the wall it is hiding behind. Cheap enough at
            // the blaster's own fire rate.
            bool clear = !Physics.Raycast(self, toTarget, distance - 1f, obstacleMask,
                QueryTriggerInteraction.Ignore);

            if (!clear)
            {
                // Nothing to shoot at, so stop paying for a windup. Holding a charge behind cover
                // would leave the bot braked for no reason - the cost with none of the threat.
                _launcher?.ReleaseRocketFromHost();
                return;
            }

            if (Rocket(self, distance, aimed))
                return;

            if (aimed && _blaster != null)
                _blaster.FireFromHost(self, _rig.forward);
        }

        /// <summary>
        /// Decides whether to commit to a rocket, and holds the windup if so.
        ///
        /// Returns true while charging, because a charging bot must not also be firing the laser -
        /// the player cannot, and the windup is supposed to be a moment of vulnerability rather
        /// than a free upgrade to its damage output.
        ///
        /// `rocketDiscipline` is the willingness to pay: the windup brakes the bot to ~40% speed
        /// and takes three seconds, which against a moving target is a real bet. Committing is
        /// decided once per attempt rather than re-rolled per frame, or a low-discipline bot would
        /// still charge constantly in short useless bursts.
        /// </summary>
        private bool Rocket(Vector3 self, float distance, bool aimed)
        {
            if (_launcher == null || profile.rocketDiscipline <= 0f)
                return false;

            if (_charging)
            {
                // Abandon it if the shot stopped making sense; otherwise ride it out. The launcher
                // tells us when the windup is done, and releasing on that frame is what fires.
                if (!aimed && distance > profile.preferredRange * 2f)
                {
                    _charging = false;
                    _launcher.ReleaseRocketFromHost();
                    return false;
                }

                if (_launcher.HoldRocketFromHost())
                    return true;

                _charging = false;
                _launcher.ReleaseRocketFromHost();
                return false;
            }

            if (!aimed || !_launcher.HeavyAvailable || Time.time < _nextRocketDecision)
                return false;

            _nextRocketDecision = Time.time + 1f;

            // Worth it when the target is inside the range this bot likes to fight at - a rocket
            // thrown across the arena is three braked seconds for a shot that will be dodged.
            if (distance > profile.preferredRange * 1.5f)
                return false;

            if (Random.value > profile.rocketDiscipline)
                return false;

            _charging = true;
            _launcher.HoldRocketFromHost();
            return true;
        }

        private bool _charging;
        private float _nextRocketDecision;
    }
}
