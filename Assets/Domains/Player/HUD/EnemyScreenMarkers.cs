using Game.Networking;
using Game.Weapons;
using UnityEngine;

namespace Game.Player
{
    /// <summary>
    /// A diamond over every living enemy player inside the view.
    ///
    /// Six players drifting in a zero-g arena, all sharing one silhouette, is close to unreadable:
    /// there is no ground to judge position against, no upright pose to pick out, and a player
    /// tumbling 80m away is a few dark pixels against arena geometry. The marker is what makes an
    /// enemy a target rather than scenery.
    ///
    /// Scope is deliberately on-screen only. Enemies behind you are the radar's job - it has the
    /// rings and the 360 degrees for it - and duplicating that here would mean edge-clamped arrows
    /// competing with the rings for the same glance. So: in view, you get a diamond; out of view,
    /// you brake and read the radar.
    ///
    /// Owner-only by construction: it lives on the player camera, and NetworkPlayer disables the
    /// whole camera rig on remote players.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public class EnemyScreenMarkers : MonoBehaviour
    {
        [SerializeField] private RocketLauncher launcher;

        [Header("Size")]
        [Tooltip("Half-width in pixels at the reference distance. Scaled by distance from there.")]
        [SerializeField] private float baseSize = 13f;

        [Tooltip("Distance at which the marker is drawn at baseSize.")]
        [SerializeField] private float referenceDistance = 30f;

        [Tooltip("Floor on the size, so a distant enemy stays a readable shape rather than fading " +
                 "into a smudge the eye skips over.")]
        [SerializeField] private float minSize = 6f;

        [Tooltip("Ceiling on the size, so a point-blank enemy does not frame the whole screen.")]
        [SerializeField] private float maxSize = 22f;

        [SerializeField] private float thickness = 2f;

        [Header("Appearance")]
        [Tooltip("Opacity of an enemy in view but not locked. Their colour comes from the shared " +
                 "palette, so the marker names them without reading the text.")]
        [Range(0.1f, 1f)]
        [SerializeField] private float idleAlpha = 0.7f;

        [Tooltip("Colour of the marker over the current lock target. Matches the reticle's locked " +
                 "colour, so the ring going red and this going red are the same signal.")]
        [SerializeField] private Color lockedColor = new(1f, 0.25f, 0.2f, 1f);

        [Tooltip("Shows each enemy's accumulated percentage under their name. This is the single " +
                 "number that decides whether a hit launches them, so it belongs where you are " +
                 "already looking.")]
        [SerializeField] private bool showDamagePercent = true;

        private Camera _camera;
        private PlayerVitals _self;
        private GUIStyle _percentStyle;
        private GUIStyle _nameStyle;

        private void Awake()
        {
            _camera = GetComponent<Camera>();
            _self = GetComponentInParent<PlayerVitals>();

            if (launcher == null)
                launcher = GetComponentInParent<RocketLauncher>();
        }

        private void OnGUI()
        {
            if (_camera == null)
                return;

            // Read from the launcher's cached poll rather than testing the lock here. Both HUD
            // pieces want the same answer in the same frame, and the test costs an OverlapSphere
            // plus a raycast per candidate - running it twice would also let the circle and the
            // diamond disagree about who is locked, which is worse than either being slightly late.
            var locked = launcher != null ? launcher.CurrentLock : null;

            var players = PlayerVitals.All;

            for (int i = 0; i < players.Count; i++)
            {
                var vitals = players[i];

                if (vitals == null || vitals == _self || !vitals.isSpawned)
                    continue;

                // Dead players keep their body - the match system reuses it for respawns - so
                // "exists" is not "is a target". Without this check the arena keeps a diamond
                // hanging over every corpse until the round ends.
                if (!vitals.Vitals.IsAlive)
                    continue;

                var viewport = _camera.WorldToViewportPoint(vitals.MarkerPosition);

                // z is distance along the camera's forward axis: negative means behind the camera,
                // where the viewport x/y still land inside 0-1 and would draw a marker for someone
                // directly at your back.
                if (viewport.z <= 0.5f)
                    continue;

                if (viewport.x < 0f || viewport.x > 1f || viewport.y < 0f || viewport.y > 1f)
                    continue;

                var point = new Vector2(viewport.x * Screen.width, (1f - viewport.y) * Screen.height);

                // Scaled by distance so the marker reads as depth. A fixed-size marker in a zero-g
                // arena is actively misleading: with no horizon to judge against, size is most of
                // what tells you whether someone is a threat now or a threat in two seconds.
                float size = Mathf.Clamp(baseSize * referenceDistance / viewport.z, minSize, maxSize);

                bool isLocked = locked.HasValue && vitals.owner.HasValue && vitals.owner.Value == locked.Value;

                // Player colour normally, red the moment they are the lock target.
                //
                // Red overrides identity here on purpose: while locked, which player it is matters
                // less than the fact that this is the one the rocket will chase, and red is the
                // colour nothing else in this HUD uses except incoming danger. The second ring
                // stays as well, so the lock still reads for a player whose own colour is warm.
                var colour = isLocked ? lockedColor : PlayerPalette.ColorOf(vitals.owner);
                colour.a = isLocked ? 1f : idleAlpha;

                GUI.color = colour;
                HudDraw.Diamond(point, size, thickness);

                if (isLocked)
                    HudDraw.Diamond(point, size + 5f, thickness);

                DrawLabel(point, size, vitals, colour);
            }

            GUI.color = Color.white;
        }

        /// <summary>
        /// The player's name under the diamond, in their own colour, with the percentage beneath.
        ///
        /// The name is the point: four identical silhouettes tumbling in a dark arena are not
        /// distinguishable by sight, and "who is that" decides whether you commit a 3-second
        /// charge at them. Colour alone cannot carry it when a marker is small or clipped at the
        /// screen edge, so the two together do the work.
        /// </summary>
        private void DrawLabel(Vector2 point, float size, PlayerVitals vitals, Color colour)
        {
            _nameStyle ??= new GUIStyle(GUI.skin.label)
            {
                fontSize = 12,
                alignment = TextAnchor.UpperCenter,
                fontStyle = FontStyle.Bold
            };

            _percentStyle ??= new GUIStyle(GUI.skin.label)
            {
                fontSize = 11,
                alignment = TextAnchor.UpperCenter
            };

            GUI.color = Color.white;

            float y = point.y + size + 3f;

            _nameStyle.normal.textColor = colour;
            GUI.Label(new Rect(point.x - 60f, y, 120f, 16f),
                PlayerPalette.NameOf(vitals.owner), _nameStyle);

            if (!showDamagePercent)
                return;

            // Percentage keeps the green-to-red damage ramp rather than the player colour. It
            // answers a different question - how close they are to being launched - and the
            // bottom-centre readout uses the same ramp, so one habit reads both.
            float percent = vitals.Vitals.DamagePercent;
            _percentStyle.normal.textColor = Color.Lerp(new Color(0.6f, 1f, 0.6f),
                new Color(1f, 0.3f, 0.2f), Mathf.Clamp01(percent / 200f));

            GUI.Label(new Rect(point.x - 60f, y + 15f, 120f, 16f),
                $"{Mathf.RoundToInt(percent)}%", _percentStyle);
        }
    }
}
