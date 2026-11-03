using Game.Weapons;
using UnityEngine;

namespace Game.Player
{
    /// <summary>
    /// The crosshair: a dot and cross you aim with, a ring that shows the lock cone and the charge,
    /// and the dash charges directly underneath.
    ///
    /// Built as one component because these are one glance. A shooter reticle has to answer three
    /// questions without the eye leaving centre screen - where does the shot go, is anything
    /// locked, can I still dash - and splitting them across the screen is what makes a HUD feel
    /// scattered. Deadlock's arrangement is the reference: tight cross, small ring, resources
    /// stacked just below.
    ///
    /// The ring is still derived from the launcher's lock angle and the camera FOV rather than
    /// being a fixed pixel size, so it stays honest when either is retuned or the FOV punches on a
    /// dash - reticleScale shrinks it without breaking that relationship.
    ///
    /// Owner-only; NetworkPlayer disables this on remote players along with the rest of the
    /// camera-dependent components.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public class LockOnReticle : MonoBehaviour
    {
        [SerializeField] private RocketLauncher launcher;

        [Tooltip("Read for the dash pips. Resolved from the player hierarchy if left empty.")]
        [SerializeField] private PlayerZeroGMovement movement;

        [Header("Appearance")]
        [SerializeField] private Color idleColor = new(1f, 1f, 1f, 0.25f);
        [SerializeField] private Color lockedColor = new(1f, 0.3f, 0.2f, 0.9f);
        [SerializeField] private float thickness = 2f;
        [SerializeField] private int segments = 48;

        [Tooltip("Scales the ring relative to the aim-assist cone. Below 1 the ring sits inside " +
                 "the cone, so an enemy in the ring is still certainly a hit - the assist simply " +
                 "reaches further than the ring admits. Never go above 1: that promises hits the " +
                 "assist will not deliver.")]
        [Range(0.15f, 1.5f)]
        // Renamed from `reticleScale` on purpose. The prefab carries `reticleScale: 0.4` from when
        // the ring drew the 12-degree lock cone, and Unity binds serialized values by name - so a
        // changed default alone would have been silently overridden by the prefab.
        //
        // 0.235 keeps the compact crosshair that shrunken 12-degree ring gave, now that the ring is
        // measured against the 20-degree assist cone instead: tan(12) * 0.4 / tan(20) = 0.234. So
        // the circle is the same size on screen as before and still never over-promises, because
        // everything it encloses is well inside the cone.
        [SerializeField] private float ringScale = 0.235f;

        [Tooltip("Point size of the ROCKET READY / recharging line under the reticle. It reports " +
                 "the state of the one attack you have to plan around, so it is worth reading " +
                 "from a glance rather than squinting at.")]
        [Range(10, 40)]
        [SerializeField] private int labelFontSize = 20;

        [Header("Aim Cross")]
        [Tooltip("Size in pixels of the dot at the exact centre of the screen - where the laser " +
                 "actually goes. The ring marks a wide cone, which is the wrong thing to aim a " +
                 "hitscan weapon with; this is the precise point.")]
        [SerializeField] private float dotSize = 3f;

        [Tooltip("Dark outline behind the dot and ticks so they stay visible against bright " +
                 "geometry and explosions. One pixel each side is enough.")]
        [SerializeField] private bool dotOutline = true;

        [SerializeField] private Color dotColor = new(1f, 1f, 1f, 0.9f);

        [Tooltip("Gap in pixels between the dot and the start of each tick. The gap is what keeps " +
                 "the centre pixel readable - a solid cross hides the thing you are aiming with.")]
        [SerializeField] private float crossGap = 5f;

        [Tooltip("Length of each of the four ticks around the dot. Zero draws the dot alone.")]
        [SerializeField] private float crossLength = 6f;

        [Header("Dash Pips")]
        [Tooltip("Dash charges, drawn under the ring. They were on a world-space hologram on the " +
                 "character's back, where reading them meant looking away from the fight - and " +
                 "where they were wrong for every remote player, since dash charges are local " +
                 "state and never networked.")]
        [SerializeField] private bool showDashPips = true;

        [SerializeField] private float pipWidth = 14f;
        [SerializeField] private float pipHeight = 4f;
        [SerializeField] private float pipSpacing = 4f;

        [Tooltip("Distance below the ring's bottom edge. Clamped so a small reticleScale does not " +
                 "pull the pips into the dot.")]
        [SerializeField] private float pipOffset = 14f;

        [SerializeField] private Color pipFullColor = new(0.45f, 0.85f, 1f, 0.95f);
        [SerializeField] private Color pipEmptyColor = new(1f, 1f, 1f, 0.18f);

        private Camera _camera;

        private void Awake()
        {
            _camera = GetComponent<Camera>();

            if (launcher == null)
                launcher = GetComponentInParent<RocketLauncher>();

            // From the root, not the parent: the camera rig and the moving body are sibling
            // branches of the player prefab, so the movement script is never above this one.
            if (movement == null)
                movement = transform.root.GetComponentInChildren<PlayerZeroGMovement>(true);
        }

        /// <summary>
        /// Whether a shot would lock, read from the launcher's own cached poll.
        ///
        /// This used to be tested here, from the camera ray, which meant the circle on screen and
        /// the weapon were evaluating different cones and the rocket could lock someone other than
        /// whoever you had circled. Now there is one test, in the place that fires.
        /// </summary>
        private bool HasLock => launcher != null && launcher.CurrentLock.HasValue;

        private void OnGUI()
        {
            if (launcher == null || _camera == null)
                return;

            float radius = GetScreenRadius();
            var centre = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);

            GUI.color = HasLock ? lockedColor : idleColor;
            HudDraw.Circle(centre, radius, thickness, segments);

            DrawAimCross(centre);

            if (showDashPips)
                DrawDashPips(centre, radius);

            // A second ring closing inward as the shot winds up. Drawn here rather than as a bar
            // so the commitment reads without the player looking away from the fight they are
            // currently standing still in the middle of.
            float charge = launcher.ChargeFraction;
            bool ready = launcher.IsFullyCharged;

            if (launcher.IsBraking || charge > 0f)
            {
                if (ready)
                {
                    // Snapped tight to the lock circle and pulsing. The shot is all-or-nothing -
                    // releasing one frame early fires nothing at all - so the instant it becomes
                    // available has to be unmissable. A ring that merely finished shrinking was
                    // not: it looked the same as a ring still shrinking.
                    float pulse = 0.7f + 0.3f * Mathf.Sin(Time.unscaledTime * 12f);
                    GUI.color = new Color(1f, 0.95f, 0.4f, pulse);
                    HudDraw.Circle(centre, radius * 1.05f, thickness, segments);
                    HudDraw.Circle(centre, radius * 1.2f, thickness, segments);
                }
                else
                {
                    GUI.color = Color.Lerp(new Color(0.5f, 0.8f, 1f, 0.6f),
                        new Color(1f, 0.9f, 0.35f, 0.95f), charge);
                    HudDraw.Circle(centre, Mathf.Lerp(radius * 2.2f, radius * 1.3f, charge), thickness, segments);
                }
            }

            // Sits under the dash pips rather than at a fixed offset from the ring. With the
            // reticle scaled down, a label measured from the ring edge landed on top of them.
            float labelY = centre.y + Mathf.Max(radius, crossGap + crossLength) + pipOffset +
                           (showDashPips ? pipHeight + 8f : 0f);

            if (ready)
            {
                GUI.color = Color.white;
                GUI.Label(new Rect(0, labelY, Screen.width, labelFontSize + 8f), "ROCKET READY",
                    LabelStyle(new Color(1f, 0.95f, 0.4f, 0.95f)));
            }
            else if (launcher.HeavyOnCooldown)
            {
                GUI.color = Color.white;

                // Counted down rather than shown as a flat "unavailable": the cooldown is a
                // rhythm the player is meant to play around, so they need to know where in it
                // they are.
                float remaining = launcher.CooldownRemaining;
                GUI.Label(new Rect(0, labelY, Screen.width, labelFontSize + 8f),
                    remaining > 0.05f ? $"ROCKET  {remaining:F1}s" : "ROCKET RECHARGING",
                    LabelStyle(new Color(1f, 0.6f, 0.3f, 0.85f)));
            }

            GUI.color = Color.white;
        }

        /// <summary>
        /// The ROCKET READY / recharging line. Cached rather than rebuilt: it used to allocate a
        /// fresh GUIStyle on every call, which is every frame the label is up.
        /// </summary>
        private GUIStyle LabelStyle(Color color)
        {
            _labelStyle ??= new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter
            };

            // Both set per call: the size so the inspector field takes effect without a restart,
            // the colour because it carries the state - yellow for ready, orange for recharging.
            _labelStyle.fontSize = labelFontSize;
            _labelStyle.normal.textColor = color;

            return _labelStyle;
        }

        private GUIStyle _labelStyle;

        /// <summary>
        /// The crosshair proper: a dot on the exact pixel the weapons fire through, with four
        /// ticks around it.
        ///
        /// The ring was never a crosshair - it draws the boundary of the homing cone, which is a
        /// dozen degrees wide, so aiming by it is aiming at an area. The laser is hitscan and goes
        /// exactly where the centre of the screen points.
        ///
        /// The ticks exist because a lone dot disappears. Against bright geometry, an explosion, or
        /// another player's white armour it is three pixels with nothing to anchor them; four marks
        /// offset by a gap stay findable while leaving the centre pixel itself unobscured, which is
        /// why nearly every shooter converges on this shape.
        /// </summary>
        private void DrawAimCross(Vector2 centre)
        {
            if (crossLength > 0f)
            {
                float inner = crossGap;
                float outer = crossGap + crossLength;

                if (dotOutline)
                {
                    // Drawn first and one pixel fatter, so the ticks read against any background.
                    GUI.color = new Color(0f, 0f, 0f, 0.5f);
                    DrawTicks(centre, inner, outer, thickness + 2f);
                }

                GUI.color = dotColor;
                DrawTicks(centre, inner, outer, thickness);
            }

            if (dotSize <= 0f)
                return;

            float half = dotSize * 0.5f;

            if (dotOutline)
            {
                GUI.color = new Color(0f, 0f, 0f, 0.6f);
                GUI.DrawTexture(new Rect(centre.x - half - 1f, centre.y - half - 1f,
                    dotSize + 2f, dotSize + 2f), HudDraw.Pixel);
            }

            GUI.color = dotColor;
            GUI.DrawTexture(new Rect(centre.x - half, centre.y - half, dotSize, dotSize),
                HudDraw.Pixel);
        }

        private void DrawTicks(Vector2 centre, float inner, float outer, float width)
        {
            HudDraw.Line(new Vector2(centre.x, centre.y - inner),
                new Vector2(centre.x, centre.y - outer), width);
            HudDraw.Line(new Vector2(centre.x, centre.y + inner),
                new Vector2(centre.x, centre.y + outer), width);
            HudDraw.Line(new Vector2(centre.x - inner, centre.y),
                new Vector2(centre.x - outer, centre.y), width);
            HudDraw.Line(new Vector2(centre.x + inner, centre.y),
                new Vector2(centre.x + outer, centre.y), width);
        }

        /// <summary>
        /// Dash charges as a row of pips under the ring, the one recharging filling left to right.
        ///
        /// Read from the movement script directly rather than mirrored into a networked field:
        /// dash charges are the local player's own business, the HUD only ever shows your own, and
        /// this is the authority on them.
        /// </summary>
        private void DrawDashPips(Vector2 centre, float radius)
        {
            if (movement == null)
                return;

            int max = Mathf.Max(0, movement.maxDashCharges);
            if (max == 0)
                return;

            int charges = movement.CurrentDashCharges;
            float recharge = movement.DashRechargeProgress;

            float totalWidth = max * pipWidth + (max - 1) * pipSpacing;
            float x = centre.x - totalWidth * 0.5f;

            // Measured from the ring's edge but floored, so shrinking the reticle does not stack
            // the pips on top of the cross.
            float y = centre.y + Mathf.Max(radius, crossGap + crossLength) + pipOffset;

            for (int i = 0; i < max; i++)
            {
                var slot = new Rect(x, y, pipWidth, pipHeight);

                GUI.color = pipEmptyColor;
                GUI.DrawTexture(slot, HudDraw.Pixel);

                float fill = i < charges
                    ? 1f
                    : (i == charges ? Mathf.Clamp01(recharge) : 0f);

                if (fill > 0f)
                {
                    GUI.color = pipFullColor;
                    GUI.DrawTexture(new Rect(slot.x, slot.y, slot.width * fill, slot.height),
                        HudDraw.Pixel);
                }

                x += pipWidth + pipSpacing;
            }

            GUI.color = Color.white;
        }

        /// <summary>
        /// Converts the launcher's lock half-angle into pixels for this camera's FOV, so the
        /// circle stays truthful when either is retuned or the FOV punches on a dash.
        /// </summary>
        private float GetScreenRadius()
        {
            float halfFov = _camera.fieldOfView * 0.5f * Mathf.Deg2Rad;

            // The assist cone, not the rocket's lock cone. The ring's job is to say where the
            // laser will land, and the laser is what you fire constantly; the rocket's narrower
            // lock gets its own signal by turning the enemy's marker red. Drawn at ringScale of
            // that cone, so the ring is a subset of it - what it encloses hits, and the assist
            // keeps working a little past the edge.
            float coneAngle = launcher.AssistAngleDegrees * Mathf.Deg2Rad;

            return Mathf.Tan(coneAngle) / Mathf.Tan(halfFov) * (Screen.height * 0.5f) * ringScale;
        }

    }
}
