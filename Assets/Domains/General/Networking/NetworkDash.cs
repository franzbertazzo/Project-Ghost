using PurrNet;
using PurrNet.Transports;
using UnityEngine;

namespace Game.Networking
{
    /// <summary>
    /// Makes dashes look right on other people's screens.
    ///
    /// Why this is needed at all: cruise speed is ~14 m/s but a surface dash is 150 m/s held for
    /// 0.2s. Remote bodies are positioned by NetworkTransform interpolating buffered samples, so
    /// at 60Hz one tick of lag is 2.5 metres at dash speed - and a client-to-client path pays that
    /// buffer twice, plus two network hops. A dashing player can therefore render several metres
    /// behind where they actually are, arriving late and gliding instead of snapping.
    ///
    /// The model (per CLAUDE.md): fire locally, broadcast the impulse, let the host correct.
    /// The owner dashes immediately and tells everyone the impulse. Remotes extrapolate along that
    /// impulse so the dash starts on time, and the authoritative transform stream pulls them back
    /// as it arrives.
    ///
    /// The extrapolation is deliberately incapable of causing drift: NetworkTransform rewrites the
    /// transform every Update, and this only nudges it in LateUpdate. Stop receiving dashes and
    /// everything falls back to plain interpolation.
    /// </summary>
    public class NetworkDash : NetworkBehaviour
    {
        [Header("Extrapolation")]
        [Tooltip("Scales the lead applied to remote dashes. 1 = lead by the full measured delay. " +
                 "Below 1 under-leads (dash looks slightly late), above 1 overshoots.")]
        [Range(0f, 1.5f)]
        [SerializeField] private float leadScale = 1f;

        [Tooltip("Upper bound on the lead, in seconds, in case a measured round trip spikes.")]
        [SerializeField] private float maxLeadSeconds = 0.3f;

        [Tooltip("Must match Min Buffer Size on the body's NetworkTransform. Holding that many " +
                 "samples before playback is a real delay the lead has to cover; the setup tool " +
                 "keeps the two in sync.")]
        [SerializeField] private int interpolationBufferTicks = 2;

        [Tooltip("How fast the lead offset eases in and out. Purely cosmetic smoothing.")]
        [SerializeField] private float offsetSmoothing = 25f;

        [Tooltip("Safety clamp. A prediction further than this from the authoritative position is " +
                 "treated as bad data and ignored rather than teleporting the body.")]
        [SerializeField] private float maxCorrectionDistance = 40f;

        [Header("Debug")]
        [SerializeField] private bool logDashes;

        private PlayerZeroGMovement _movement;
        private Transform _body;

        private Vector3 _appliedOffset;
        private Vector3 _dashVelocity;
        private float _dashStartTime;
        private float _dashDuration;
        private float _senderLatency;
        private bool _dashActive;

        private void Awake()
        {
            _movement = GetComponentInChildren<PlayerZeroGMovement>(true);
            if (_movement != null)
                _body = _movement.transform;
        }

        protected override void OnSpawned()
        {
            base.OnSpawned();

            if (_movement == null)
            {
                Debug.LogWarning($"[NetworkDash] No PlayerZeroGMovement under {name}; dashes will not replicate.");
                return;
            }

            // Only the owner reports dashes. Remote copies of the movement script never fire the
            // event anyway - they have no input - but subscribing on them would be meaningless.
            if (isOwner)
                _movement.OnDashPerformed += HandleLocalDash;
        }

        protected override void OnDespawned()
        {
            base.OnDespawned();

            if (_movement != null)
                _movement.OnDashPerformed -= HandleLocalDash;
        }

        /// <summary>
        /// The dash event carries direction and surface-ness but not speed, and speed varies a lot
        /// (chain bonus, rebound, escape and glide multipliers all scale it). Reading the
        /// Rigidbody is exact and needs no change to the movement code: ApplyDashImpulse has just
        /// set linearVelocity to the dash vector when this fires.
        /// </summary>
        private void HandleLocalDash(Vector3 direction, bool isSurface)
        {
            if (_body == null)
                return;

            var rigidbody = _movement.GetComponent<Rigidbody>();
            Vector3 velocity = rigidbody != null
                ? rigidbody.linearVelocity
                : direction.normalized * 100f;

            // Stamp our own latency: the observer cannot measure this leg of the path.
            var tick = networkManager != null ? networkManager.tickModule : null;
            float ownLatency = tick != null ? MeasureOneWay(tick) : 0f;

            ReportDash(velocity, _movement.dashGraceTime, ownLatency);

            if (logDashes)
                Debug.Log($"[NetworkDash] Local dash {(isSurface ? "off surface" : "in air")} at {velocity.magnitude:F0} m/s.");
        }

        /// <summary>
        /// Unreliable on purpose: a dash is only useful while it is happening. A resend arriving
        /// after the 0.2s window would apply a stale impulse, which is worse than missing one -
        /// the transform stream covers the gap either way.
        /// </summary>
        [ServerRpc(channel: Channel.Unreliable, requireOwnership: true)]
        private void ReportDash(Vector3 velocity, float duration, float senderLatency)
        {
            BroadcastDash(velocity, duration, senderLatency);
        }

        [ObserversRpc(channel: Channel.Unreliable, excludeOwner: true)]
        private void BroadcastDash(Vector3 velocity, float duration, float senderLatency)
        {
            if (isOwner)
                return;

            _dashVelocity = velocity;
            _dashDuration = duration;
            _senderLatency = Mathf.Max(0f, senderLatency);
            _dashStartTime = Time.time;
            _dashActive = true;

            if (logDashes)
                Debug.Log($"[NetworkDash] Remote dash received: {velocity.magnitude:F0} m/s for {duration:F2}s.");
        }

        /// <summary>
        /// Applies a lead OFFSET to the authoritative position rather than predicting an absolute
        /// one.
        ///
        /// Predicting absolutely (origin + velocity * elapsed) reads the dash twice: when the
        /// impulse arrives, the interpolated stream is still showing the body *before* the dash,
        /// so jumping to the predicted point moves it forward by the whole network delay - then
        /// the real dash data arrives and drags it back through the same distance. A rubber band,
        /// two dashes for the price of one.
        ///
        /// The offset model has no such seam. The stream lags true position by roughly one-way
        /// latency, so during the dash the true position is simply `stream + velocity * latency`.
        /// Add exactly that and the render tracks reality continuously - there is no second
        /// acceleration to see, because nothing is ever recomputed from an origin the receiver
        /// never agreed on.
        ///
        /// Runs in LateUpdate, after NetworkTransform writes its interpolated position in Update,
        /// so the authoritative value is reasserted every frame and this can never accumulate.
        /// </summary>
        private void LateUpdate()
        {
            if (_body == null || isOwner)
                return;

            Vector3 targetOffset = Vector3.zero;

            if (_dashActive)
            {
                float elapsed = Time.time - _dashStartTime;
                float lead = GetLeadSeconds();

                // Hold the lead for the dash itself plus the delay it takes the stream to show the
                // dash ending. Dropping it earlier would snap the body backwards.
                if (elapsed <= _dashDuration + lead)
                    targetOffset = _dashVelocity * lead;
                else
                    _dashActive = false;

                if (targetOffset.magnitude > maxCorrectionDistance)
                    targetOffset = Vector3.zero;
            }

            _appliedOffset = Vector3.Lerp(
                _appliedOffset,
                targetOffset,
                1f - Mathf.Exp(-offsetSmoothing * Time.deltaTime));

            if (_appliedOffset.sqrMagnitude > 0.0001f)
                _body.position += _appliedOffset;
        }

        /// <summary>
        /// How far ahead of the interpolated stream the real body is, in seconds.
        ///
        /// The whole delay between player A dashing and player B rendering it is:
        ///
        ///     (A -> host) + (host -> B) + B's interpolation buffer
        ///
        /// B can only measure its own leg. The A -> host leg is invisible to it, which is why
        /// client-to-client dashes still read late while host-to-client ones look fine: leading by
        /// B's own latency alone covers barely half the real gap. So the dashing player stamps its
        /// own measured latency into the impulse and B adds the two together.
        ///
        /// The buffer is counted too - holding minBufferSize samples before playback is a real
        /// delay (2 ticks = 33ms at 60Hz) that no amount of network measurement reveals.
        ///
        /// PurrNet's rtt bakes in two ticks of pack/unpack overhead, so that is removed before
        /// halving; leaving it in would over-lead and make dashes overshoot and settle backwards.
        /// </summary>
        private float GetLeadSeconds()
        {
            var tick = networkManager != null ? networkManager.tickModule : null;
            if (tick == null)
                return Mathf.Clamp(_senderLatency * leadScale, 0f, maxLeadSeconds);

            float bufferDelay = interpolationBufferTicks * tick.tickDelta;
            float total = _senderLatency + MeasureOneWay(tick) + bufferDelay;

            return Mathf.Clamp(total * leadScale, 0f, maxLeadSeconds);
        }

        /// <summary>
        /// One-way network time to the host, with PurrNet's two ticks of pack/unpack overhead
        /// removed. Returns 0 on the host, which is correct: it has no leg of its own to cover.
        /// </summary>
        private static float MeasureOneWay(PurrNet.Modules.TickManager tick)
        {
            float rtt = (float)tick.rtt;
            if (rtt <= 0f)
                return 0f;

            return Mathf.Max(0f, (rtt - tick.tickDelta * 2f) * 0.5f);
        }
    }
}
