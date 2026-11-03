using PurrNet;
using UnityEngine;

namespace Game.Networking
{
    /// <summary>
    /// Corner overlay showing what this process is and whether it's actually connected.
    ///
    /// The point of this in P0 is to make the test harness verifiable without a networked player
    /// prefab: if four windows each say what they are and the host says "4 players", the harness
    /// works and P1 can start. It stays useful after that as the at-a-glance connection readout.
    /// </summary>
    [RequireComponent(typeof(NetworkManager))]
    public class NetworkStatusHud : MonoBehaviour
    {
        [Tooltip("The corner diagnostics panel only - role, connection, tick, rtt. Off by default; " +
                 "the scoreboard, the percentages and the boundary warning are not diagnostics and " +
                 "always draw.")]
        // Renamed from `show` deliberately. It used to gate this whole component, and scenes still
        // carry `show: 1`, which Unity binds by name - so reusing the name would have let the old
        // serialized value override this default. It is also just a more honest name now that it
        // governs the panel rather than everything.
        [SerializeField] private bool showDiagnostics;

        [Tooltip("F7, not F10: MPPM claims Ctrl+F9 through Ctrl+F12 for switching instance focus.")]
        [SerializeField] private KeyCode toggleKey = KeyCode.F7;
        [SerializeField] private int fontSize = 13;

        private NetworkManager _manager;
        private MultiplayerBootstrap _bootstrap;
        private NetworkLatencySimulator _latency;
        private GUIStyle _style;
        private Game.Match.MatchManager _match;
        private Game.Match.BlastZone _blastZone;

        private void Awake()
        {
            _manager = GetComponent<NetworkManager>();
            _bootstrap = GetComponent<MultiplayerBootstrap>();
            _latency = GetComponent<NetworkLatencySimulator>();
        }

        private void Update()
        {
            bool modifierHeld = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl) ||
                                Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt);

            if (toggleKey != KeyCode.None && !modifierHeld && Input.GetKeyDown(toggleKey))
                showDiagnostics = !showDiagnostics;
        }

        private void OnGUI()
        {
            if (_manager == null)
                return;

            _style ??= new GUIStyle(GUI.skin.label)
            {
                fontSize = fontSize,
                normal = { textColor = Color.white },
                richText = true
            };

            // The split that F7 used to not make. It gated this entire component, so hiding the
            // debug panel also hid the damage percentages and the out-of-bounds warning - and those
            // are not diagnostics, they are the two readouts a player needs to not die.
            if (showDiagnostics)
                DrawDiagnostics();

            DrawScoreboard();
            DrawPercentages();
            DrawBoundaryWarning();
        }

        private void DrawDiagnostics()
        {
            string role = _bootstrap != null
                ? MultiplayerRoleResolver.Describe(_bootstrap.ActiveRole)
                : "unknown";

            string connection;
            if (_manager.isHost) connection = "<color=#6f6>hosting</color>";
            else if (_manager.isServerOnly) connection = "<color=#6f6>serving</color>";
            else if (_manager.isClient) connection = "<color=#6f6>connected</color>";
            else connection = "<color=#f66>offline</color>";

            string latency = _latency != null ? _latency.Summary : "n/a";

            var tick = _manager.tickModule;

            // PurrNet's rtt bakes in two ticks of pack/unpack delay, so it reads high against the
            // configured simulation. Labelled here so the two numbers aren't mistaken for a bug.
            string rtt = tick != null && tick.rtt > 0
                ? $"{tick.rtt * 1000f:F0}ms (incl. 2 ticks)"
                : "-";

            var lines = string.Join("\n",
                $"<b>{role}</b>  {connection}",
                $"players: {_manager.playerCount}    tick: {tick?.tickRate ?? 0}Hz",
                $"latency sim: {latency}",
                $"measured rtt: {rtt}",
                "<color=#999>F8 latency  F7 hide</color>");

            var rect = new Rect(10, 10, 400, 126);
            GUI.Box(rect, GUIContent.none);
            GUI.Label(new Rect(rect.x + 8, rect.y + 6, rect.width - 16, rect.height - 12), lines, _style);
        }

        /// <summary>
        /// Warns the local player as they approach the blast boundary.
        ///
        /// With no walls, the edge of the world is invisible. Without this, dying reads as the
        /// game arbitrarily killing you rather than as a distance you failed to recover from -
        /// and recovering is supposed to be a skill, which requires knowing you need to.
        /// </summary>
        private void DrawBoundaryWarning()
        {
            if (_blastZone == null)
            {
                _blastZone = FindFirstObjectByType<Game.Match.BlastZone>();
                if (_blastZone == null)
                    return;
            }

            var local = FindLocalBody();
            if (local == null)
                return;

            float distance = Vector3.Distance(local.position, _blastZone.transform.position);
            float fraction = distance / _blastZone.Radius;

            // Quiet until it matters. Below 70% of the way out there is nothing to say.
            if (fraction < 0.7f)
                return;

            float remaining = Mathf.Max(0f, _blastZone.Radius - distance);
            var colour = Color.Lerp(new Color(1f, 0.85f, 0.3f), new Color(1f, 0.2f, 0.15f),
                Mathf.InverseLerp(0.7f, 1f, fraction));

            var style = new GUIStyle(GUI.skin.label)
            {
                fontSize = 22,
                alignment = TextAnchor.MiddleCenter,
                fontStyle = FontStyle.Bold,
                normal = { textColor = colour }
            };

            GUI.Label(new Rect(0, Screen.height * 0.26f, Screen.width, 30f),
                $"OUT OF BOUNDS  —  {remaining:F0}m", style);
        }

        /// <summary>
        /// From the player registry rather than a scene search: this runs inside OnGUI, where
        /// FindObjectsByType allocates an array every frame.
        /// </summary>
        private Transform FindLocalBody()
        {
            var players = PlayerVitals.All;

            for (int i = 0; i < players.Count; i++)
            {
                if (players[i] != null && players[i].isOwner)
                    return players[i].BodyTransform;
            }

            return null;
        }

        /// <summary>
        /// Every player's percentage, bottom centre.
        ///
        /// This is the primary read in a knockback game - you decide whether to commit based on
        /// how launchable your opponent is - so it is shown for everyone, not just yourself, and
        /// coloured by how close to dangerous it has become.
        /// </summary>
        private void DrawPercentages()
        {
            var vitals = PlayerVitals.All;
            if (vitals.Count == 0)
                return;

            const float cell = 150f;
            float totalWidth = vitals.Count * cell;
            float x = (Screen.width - totalWidth) * 0.5f;
            float y = Screen.height - 86f;

            foreach (var player in vitals)
            {
                if (player == null)
                    continue;

                float percent = player.Vitals.DamagePercent;
                bool isLocal = player.isOwner;
                bool dead = !player.Vitals.IsAlive;

                // Green through yellow to red as launch distance climbs. 200% is treated as the
                // top of the scale; past that the colour stops changing but the number keeps going.
                var colour = dead
                    ? new Color(0.4f, 0.4f, 0.4f)
                    : Color.Lerp(new Color(0.6f, 1f, 0.6f), new Color(1f, 0.25f, 0.2f),
                        Mathf.Clamp01(percent / 200f));

                var rect = new Rect(x, y, cell - 10f, 70f);
                GUI.Box(rect, GUIContent.none);

                var style = new GUIStyle(GUI.skin.label)
                {
                    fontSize = 28,
                    alignment = TextAnchor.MiddleCenter,
                    normal = { textColor = colour },
                    fontStyle = isLocal ? FontStyle.Bold : FontStyle.Normal
                };

                GUI.Label(new Rect(rect.x, rect.y + 6f, rect.width, 38f),
                    dead ? "--" : $"{percent:F0}%", style);

                // Named and coloured from the shared palette, so the player who is orange on the
                // radar and orange above their head is orange here too. Deriving a local colour
                // here instead would make the same opponent a different colour in each readout.
                var nameStyle = new GUIStyle(GUI.skin.label)
                {
                    fontSize = 11,
                    alignment = TextAnchor.MiddleCenter,
                    normal = { textColor = PlayerPalette.ColorOf(player.owner) },
                    fontStyle = isLocal ? FontStyle.Bold : FontStyle.Normal
                };

                string label = PlayerPalette.NameOf(player.owner);

                GUI.Label(new Rect(rect.x, rect.y + 44f, rect.width, 18f),
                    isLocal ? $"{label} (you)" : label, nameStyle);

                x += cell;
            }
        }

        /// <summary>
        /// Scores in the opposite corner from the connection readout, so the two do not compete
        /// and the scoreboard can stay up once the net debug panel is hidden.
        /// </summary>
        private void DrawScoreboard()
        {
            if (_match == null)
                _match = FindFirstObjectByType<Game.Match.MatchManager>();

            if (_match == null || _match.Scores.Count == 0)
                return;

            var text = new System.Text.StringBuilder();

            if (_match.MatchOver)
                text.AppendLine($"<b><color=#ffd24a>{PlayerPalette.NameOf(_match.Winner)} WINS</color></b>");
            else if (_match.Rules != null)
                text.AppendLine($"<b>First to {_match.Rules.scoreToWin}</b>");

            foreach (var entry in _match.Scores)
            {
                bool isLocal = _manager.isLocalPlayerReady && entry.Key == _manager.localPlayer;
                string hex = PlayerPalette.HexOf(entry.Key);
                string name = PlayerPalette.NameOf(entry.Key);

                text.AppendLine(isLocal
                    ? $"<color=#{hex}><b>{name}: {entry.Value}</b></color>  (you)"
                    : $"<color=#{hex}>{name}: {entry.Value}</color>");
            }

            float height = 26 + _match.Scores.Count * 18;
            var rect = new Rect(Screen.width - 230, 10, 220, height);
            GUI.Box(rect, GUIContent.none);
            GUI.Label(new Rect(rect.x + 8, rect.y + 6, rect.width - 16, rect.height - 12),
                text.ToString(), _style);
        }
    }
}
