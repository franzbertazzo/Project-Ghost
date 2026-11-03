using System;
using System.Collections.Generic;
using Game.Networking;
using Game.UI;
using PurrNet;
using UnityEngine;

namespace Game.Match
{
    /// <summary>
    /// Everything the match flow has to say.
    ///
    /// Separate from MatchManager so the match logic has no presentation in it, but added
    /// automatically by it rather than wired, because a round that nobody is told about reads as
    /// the game freezing.
    ///
    /// Two shapes, for two jobs. During a fight it is banners only - a word and a countdown, big,
    /// centred, gone in two seconds, because anything else competes with the reticle at the moment
    /// you need it most. In the warmup it is a panel, because that is the one point in a match with
    /// time to read: who is here, what the ruleset is, what the code is and which keys do what. The
    /// warmup was previously three lines of text, which meant a player who had just joined a
    /// friend's room had no way to find out either who else was in it or how to fly.
    ///
    /// IMGUI, through <see cref="MenuSkin"/> - see there for the palette and the reasoning.
    /// </summary>
    public class MatchHud : MonoBehaviour
    {
        [SerializeField] private KeyCode startKey = KeyCode.Return;
        [SerializeField] private KeyCode rematchKey = KeyCode.R;
        [SerializeField] private KeyCode lobbyKey = KeyCode.L;
        [SerializeField] private KeyCode modeKey = KeyCode.M;

        [Tooltip("Folds the warmup panel down to a one-line strip at the top of the screen, so the " +
                 "arena can be flown while waiting. Available to everyone, not just the host.")]
        [SerializeField] private KeyCode hidePanelKey = KeyCode.Tab;

        /// <summary>
        /// Left column is the binding, right column is what it does. Hardcoded rather than read
        /// back from the input asset: the asset has no display names, and generating "leftShift"
        /// from a binding path would read worse than writing it out.
        /// </summary>
        private static readonly string[,] Controls =
        {
            { "MOUSE", "look" },
            { "W A S D", "thrust" },
            { "SPACE / CTRL", "up / down" },
            { "Q / E", "roll" },
            { "SHIFT", "dash off a surface" },
            { "MIDDLE MOUSE", "radar" },
            { "LEFT MOUSE", "fire" },
            { "HOLD RIGHT MOUSE", "rocket" }
        };

        /// <summary>
        /// Width reserved for the room code in the panel's title row. Shared with the title's
        /// layout, since the two sit in one row and the title has to know what is already taken.
        /// </summary>
        private const float CodeWidth = 230f;

        [Tooltip("Seconds the KILLED BY line stays up after you die.")]
        [SerializeField] private float deathNoticeSeconds = 4f;

        private bool _wasAlive;
        private float _deathNoticeUntil;
        private PlayerID? _killedBy;

        /// <summary>
        /// Watches the local player's alive flag for a true-to-false edge.
        ///
        /// Polled rather than subscribed to <c>OnDied</c> because the body carrying that event is
        /// destroyed and recreated on every respawn, so a subscription would need re-establishing
        /// each time and would be a leak the one time it was missed. The alive flag is a SyncVar
        /// every peer already has, and an edge on it is exactly "I just died".
        /// </summary>
        private void WatchForOwnDeath()
        {
            var mine = LocalVitals();
            bool alive = mine != null && mine.Vitals.IsAlive;

            if (_wasAlive && !alive && mine != null)
            {
                // Read at the moment of death: LastAttacker is cleared when the next life starts,
                // and is the same credit the match uses for scoring, so the name shown here is
                // always the one that got the point.
                _killedBy = mine.Vitals.LastAttacker;
                _deathNoticeUntil = Time.time + deathNoticeSeconds;
            }

            // Cleared on the way back so the notice never outlives the respawn that answers it.
            if (!_wasAlive && alive)
                _deathNoticeUntil = 0f;

            _wasAlive = alive;
        }

        private Game.Networking.PlayerVitals LocalVitals()
        {
            foreach (var vitals in Game.Networking.PlayerVitals.All)
            {
                if (vitals != null && vitals.isSpawned && vitals.isOwner)
                    return vitals;
            }

            return null;
        }

        /// <summary>
        /// "KILLED BY <name>", in the killer's own colour.
        ///
        /// Centred under the banner line rather than in a corner: you are dead, nothing else is
        /// competing for that space, and the one thing you want to know is who did it. A ring-out
        /// with nobody to credit says so instead of naming a player, since blaming the boundary on
        /// a player who never touched you would be worse than saying nothing.
        /// </summary>
        private void DrawDeathNotice(Rect screen)
        {
            if (Time.time >= _deathNoticeUntil)
                return;

            var row = new Rect(0f, screen.height * 0.62f, screen.width, 34f);

            if (_killedBy.HasValue)
            {
                MenuSkin.Text(row, $"KILLED BY {PlayerPalette.NameOf(_killedBy.Value).ToUpperInvariant()}",
                    MenuSkin.Heading, PlayerPalette.ColorOf(_killedBy.Value));
            }
            else
            {
                MenuSkin.Text(row, "OUT OF BOUNDS", MenuSkin.Heading, MenuSkin.Danger);
            }
        }

        /// <summary>True when there is a code to show, which only the host ever has.</summary>
        private bool HostingWithCode =>
            _session != null && _session.IsHost && !string.IsNullOrEmpty(_session.Code);

        private MatchManager _match;
        private SessionController _session;
        private bool _panelHidden;
        private readonly List<PlayerVitals> _roster = new();

        /// <summary>Cached so sorting the roster every frame does not allocate a delegate.</summary>
        private static readonly Comparison<PlayerVitals> ByPlayerId =
            (a, b) => a.owner.Value.id.value.CompareTo(b.owner.Value.id.value);

        private void Awake()
        {
            _match = GetComponent<MatchManager>();
            if (_match == null)
                _match = FindFirstObjectByType<MatchManager>();
        }

        /// <summary>
        /// Start, not Awake: the session controller is created at runtime by the bootstrap, and this
        /// component is itself added during MatchManager.Awake - so searching for it any earlier
        /// finds nothing roughly half the time, depending on which object Unity wakes first.
        /// </summary>
        private void Start()
        {
            // Optional: an MPPM instance has no session controller, and the lobby panel simply
            // leaves the code out when there is nothing to show.
            _session = FindFirstObjectByType<SessionController>();
        }

        private void Update()
        {
            if (_match == null)
                return;

            // Before the host-only gate below, deliberately: the panel covers the middle of the
            // screen, which is where you are looking while flying, and a client in the warmup needs
            // to see past it every bit as much as the host does.
            if (_match.State == MatchState.Lobby && Input.GetKeyDown(hidePanelKey))
                _panelHidden = !_panelHidden;

            WatchForOwnDeath();

            if (!_match.LocalPlayerCanControlFlow)
                return;

            // Host-only keys. A client pressing these does nothing rather than sending a request:
            // starting the match is the host's call, and routing it from clients would need an
            // authority check at the other end for no gain while the host is always present.
            switch (_match.State)
            {
                case MatchState.Lobby when Input.GetKeyDown(startKey):
                    _match.RequestStart();
                    break;

                case MatchState.Lobby when Input.GetKeyDown(modeKey):
                    _match.CycleRules();
                    break;

                case MatchState.MatchEnd when Input.GetKeyDown(rematchKey):
                    _match.RequestRematch();
                    break;

                case MatchState.MatchEnd when Input.GetKeyDown(lobbyKey):
                    _match.RequestReturnToLobby();
                    break;
            }
        }

        private void OnGUI()
        {
            if (_match == null)
                return;

            // Nothing to say before there is a session. The match sits in Lobby from the first
            // frame, so without this the warmup panel - roster, ruleset, controls - was drawn
            // underneath the Host/Join menu for a match nobody had started yet. MPPM instances have
            // no menu and are connected from the start, so they are unaffected.
            if (_session != null && _session.MenuDriven && _session.State != SessionState.Online)
                return;

            // Behind the session menu, which sits at 0. Lower depth draws nearer the front, and
            // OnGUI order between components is otherwise undefined - which is how the Escape
            // prompt ended up underneath the warmup strip.
            GUI.depth = 1;

            var screen = MenuSkin.Begin();

            switch (_match.State)
            {
                case MatchState.Lobby:
                    DrawLobby(screen);
                    break;

                case MatchState.RoundIntro:
                    DrawRoundIntro(screen);
                    break;

                case MatchState.RoundEnd:
                    DrawRoundEnd(screen);
                    break;

                case MatchState.MatchEnd:
                    DrawMatchEnd(screen);
                    break;
            }

            // Outside the switch: you die during Round, which draws nothing, and the notice has to
            // survive into RoundEnd rather than being replaced by the round banner.
            DrawDeathNotice(screen);

            MenuSkin.End();
        }

        // =====================================================================
        //  Warmup
        // =====================================================================

        private void DrawLobby(Rect screen)
        {
            if (_panelHidden)
            {
                DrawCollapsedLobby(screen);
                return;
            }

            const float width = 640f;

            // 530 is the six-player worst case plus a margin: header 82, roster 6x28, three section
            // rules and their gaps, the ruleset block, four rows of controls and the hint line.
            const float height = 530f;
            var panel = new Rect((screen.width - width) * 0.5f, screen.height * 0.5f - height * 0.5f,
                width, height);

            MenuSkin.Panel(panel);

            float x = panel.x + 32f;
            float inner = panel.width - 64f;

            // 44 high for a 40pt line: GUI.Label clips to its rect, and tracked caps in a 34-high
            // box lose their top. Measured against what the code leaves of the row, since the two
            // share it - the wordmark is the one that would otherwise overflow into the code.
            float titleWidth = inner - (HostingWithCode ? CodeWidth + 20f : 0f);

            MenuSkin.Text(new Rect(x, panel.y + 22f, titleWidth, 44f),
                MenuSkin.TrackToFit("WARMUP", MenuSkin.Title, titleWidth, 2), MenuSkin.Title,
                MenuSkin.Accent, TextAnchor.MiddleLeft);

            DrawRoomCode(new Rect(x, panel.y + 22f, inner, 44f));

            float y = panel.y + 82f;

            MenuSkin.SectionRule(new Rect(x, y, inner, 1f), "IN THE ROOM");
            y += 18f;

            y = DrawRoster(new Rect(x, y, inner, 0f));

            y += 20f;
            MenuSkin.SectionRule(new Rect(x, y, inner, 1f), "RULESET");
            y += 16f;

            y = DrawRuleset(new Rect(x, y, inner, 0f));

            y += 20f;
            MenuSkin.SectionRule(new Rect(x, y, inner, 1f), "CONTROLS");
            y += 16f;

            y = DrawControls(new Rect(x, y, inner, 0f));

            // Body-sized and lit rather than small and faint: it is the only thing telling a player
            // that the panel in front of them can be got out of the way, so it has to be read
            // before it is needed, not found afterwards.
            MenuSkin.Text(new Rect(x, y + 8f, inner, 22f), $"[{KeyLabel(hidePanelKey)}] hide this",
                MenuSkin.Body, MenuSkin.Key, TextAnchor.MiddleRight);
        }

        /// <summary>
        /// The code a friend types in, top right of the warmup panel.
        ///
        /// On the panel rather than the always-visible strip, because it is read out once, at the
        /// start, while you are sitting still waiting for someone - not something to carry on
        /// screen for the rest of the warmup. The strip is what stays up while you fly around, so
        /// it holds nothing but the word.
        /// </summary>
        private void DrawRoomCode(Rect row)
        {
            if (!HostingWithCode)
                return;

            var rect = new Rect(row.xMax - CodeWidth, row.y, CodeWidth, row.height);

            MenuSkin.Fill(rect, new Color(1f, 1f, 1f, 0.05f));
            MenuSkin.Frame(rect, MenuSkin.Accent);

            MenuSkin.Text(new Rect(rect.x + 12f, rect.y, 46f, rect.height), "CODE", MenuSkin.Small,
                MenuSkin.Dim, TextAnchor.MiddleLeft);

            // Code-sized: this is the one string on screen that gets read out loud to someone
            // else, usually off a monitor across the room.
            MenuSkin.Text(new Rect(rect.x + 58f, rect.y, rect.width - 70f, rect.height),
                JoinCode.ForDisplay(_session.Code), MenuSkin.Code, MenuSkin.Ink);
        }

        /// <summary>
        /// The whole panel reduced to one line at the top of the screen.
        ///
        /// The warmup is played in, not read in - the panel sits over the middle of the screen,
        /// which is exactly where you are looking while flying. So it folds away to a strip that
        /// stays out of the fight and says only what is still true: this is the warmup, and Tab
        /// brings everything back. The room code is deliberately not here; it belongs on the panel,
        /// where you are sitting still long enough to read it out.
        /// </summary>
        private void DrawCollapsedLobby(Rect screen)
        {
            const float width = 240f;
            var strip = new Rect((screen.width - width) * 0.5f, 10f, width, 36f);

            MenuSkin.Fill(strip, new Color(0.02f, 0.03f, 0.05f, 0.82f));
            MenuSkin.Frame(strip, MenuSkin.Faint);

            MenuSkin.Text(new Rect(strip.x + 16f, strip.y, 150f, strip.height),
                MenuSkin.Track("WARMUP", 1), MenuSkin.Heading, MenuSkin.Accent,
                TextAnchor.MiddleLeft);

            MenuSkin.Text(new Rect(strip.x, strip.y, strip.width - 16f, strip.height),
                $"[{KeyLabel(hidePanelKey)}]", MenuSkin.Small, MenuSkin.Key,
                TextAnchor.MiddleRight);
        }

        /// <summary>
        /// Who is actually here, which is the one thing the warmup could not tell you before.
        ///
        /// Read from the spawned player bodies rather than from the score table: a body is a
        /// networked object, so every peer sees the same list, while the scores only reach a client
        /// when one of them changes.
        /// </summary>
        private float DrawRoster(Rect row)
        {
            BuildRoster();

            const float lineHeight = 28f;
            float y = row.y;

            if (_roster.Count == 0)
            {
                MenuSkin.Text(new Rect(row.x, y, row.width, lineHeight), "nobody here yet",
                    MenuSkin.Body, MenuSkin.Faint, TextAnchor.MiddleLeft);

                return y + lineHeight;
            }

            foreach (var vitals in _roster)
            {
                var colour = PlayerPalette.ColorOf(vitals.owner);

                // A filled square in the player's colour, which is the same colour their marker,
                // ring and score cell use. The palette is the only name that works mid-fight.
                MenuSkin.Fill(new Rect(row.x + 2f, y + 9f, 9f, 9f), colour);

                MenuSkin.Text(new Rect(row.x + 20f, y, row.width - 20f, lineHeight),
                    PlayerPalette.NameOf(vitals.owner), MenuSkin.Body, colour,
                    TextAnchor.MiddleLeft);

                if (vitals.isOwner)
                    MenuSkin.Text(new Rect(row.x + 20f, y, row.width - 20f, lineHeight), "you",
                        MenuSkin.Small, MenuSkin.Dim, TextAnchor.MiddleRight);

                y += lineHeight;
            }

            return y;
        }

        private float DrawRuleset(Rect row)
        {
            var rules = _match.Rules;

            // Read from the replicated label, not the local asset: the host can cycle the ruleset
            // in the lobby and a client holding its own copy of the asset would keep showing
            // whichever one the scene happened to ship with.
            string label = !string.IsNullOrEmpty(_match.RulesLabel)
                ? _match.RulesLabel
                : rules != null ? rules.displayName : null;

            if (label == null)
            {
                MenuSkin.Text(new Rect(row.x, row.y, row.width, 24f), "No match rules assigned.",
                    MenuSkin.Body, MenuSkin.Danger, TextAnchor.MiddleLeft);

                return row.y + 24f;
            }

            string target = _match.RulesMode == MatchMode.Elimination
                ? $"first to {_match.RulesScoreToWin} rounds"
                : $"first to {_match.RulesScoreToWin} kills";

            MenuSkin.Text(new Rect(row.x, row.y, row.width, 26f), label.ToUpperInvariant(),
                MenuSkin.Heading, MenuSkin.Ink, TextAnchor.MiddleLeft);

            MenuSkin.Text(new Rect(row.x, row.y, row.width, 26f), target, MenuSkin.Body,
                MenuSkin.Dim, TextAnchor.MiddleRight);

            float y = row.y + 32f;

            if (_match.LocalPlayerCanControlFlow)
            {
                MenuSkin.Text(new Rect(row.x, y, row.width, 20f),
                    $"[{KeyLabel(startKey)}] start now     [{KeyLabel(modeKey)}] change mode",
                    MenuSkin.Small, MenuSkin.Key, TextAnchor.MiddleLeft);

                if (rules != null && rules.autoStartPlayers > 0)
                {
                    int waiting = Mathf.Max(0, rules.autoStartPlayers - _roster.Count);

                    MenuSkin.Text(new Rect(row.x, y, row.width, 20f),
                        waiting > 0
                            ? $"starts on its own in {waiting} more"
                            : "ready when you are",
                        MenuSkin.Small, MenuSkin.Dim, TextAnchor.MiddleRight);
                }
            }
            else
            {
                MenuSkin.Text(new Rect(row.x, y, row.width, 20f), "waiting for the host",
                    MenuSkin.Small, MenuSkin.Dim, TextAnchor.MiddleLeft);
            }

            return y + 20f;
        }

        /// <summary>
        /// Two columns of bindings. Here rather than in a separate screen because the warmup is
        /// where a player who was invited into someone else's room finds out how to fly - and until
        /// now nothing in the game said so anywhere.
        /// </summary>
        private float DrawControls(Rect row)
        {
            int rows = (Controls.GetLength(0) + 1) / 2;
            float columnWidth = (row.width - 28f) * 0.5f;
            const float lineHeight = 22f;

            for (int i = 0; i < Controls.GetLength(0); i++)
            {
                float columnX = row.x + (i < rows ? 0f : columnWidth + 28f);
                float y = row.y + lineHeight * (i < rows ? i : i - rows);

                MenuSkin.Text(new Rect(columnX, y, columnWidth * 0.6f, lineHeight),
                    Controls[i, 0], MenuSkin.Small, MenuSkin.Ink, TextAnchor.MiddleLeft);

                MenuSkin.Text(new Rect(columnX + columnWidth * 0.6f, y, columnWidth * 0.4f,
                        lineHeight),
                    Controls[i, 1], MenuSkin.Small, MenuSkin.Dim, TextAnchor.MiddleLeft);
            }

            return row.y + lineHeight * rows;
        }

        // =====================================================================
        //  Banners
        // =====================================================================

        private void DrawRoundIntro(Rect screen)
        {
            // Frag Limit has no rounds - the whole match is one - so "ROUND 1" would be a label
            // for a thing that never increments. Same state, different word.
            bool rounds = _match.Rules != null && _match.Rules.mode == MatchMode.Elimination;

            Banner(screen, rounds ? $"ROUND {_match.Round}" : "FIGHT", MenuSkin.Ink);

            float total = _match.Rules != null ? _match.Rules.roundIntroSeconds : 0f;
            float remaining = _match.StateRemaining;

            if (total <= 0f || remaining <= 0.05f)
                return;

            // The countdown drains rather than just counting: a bar emptying says "about to start"
            // at a glance, which a number does not until you have read it.
            var bar = new Rect(screen.width * 0.5f - 90f, screen.height * 0.44f, 180f, 3f);
            MenuSkin.Meter(bar, remaining / total, MenuSkin.Key);
        }

        private void DrawRoundEnd(Rect screen)
        {
            var winner = _match.RoundWinner;

            if (winner.HasValue)
                Banner(screen, $"{PlayerPalette.NameOf(winner.Value)} TAKES THE ROUND".ToUpperInvariant(),
                    PlayerPalette.ColorOf(winner.Value));
            else
                // Both last players died together. Naming it stops it reading as a bug.
                Banner(screen, "DRAWN ROUND", MenuSkin.Dim);

            DrawScoreCells(screen, screen.height * 0.45f);
        }

        private void DrawMatchEnd(Rect screen)
        {
            Banner(screen, $"{PlayerPalette.NameOf(_match.Winner)} WINS".ToUpperInvariant(),
                PlayerPalette.ColorOf(_match.Winner));

            DrawScoreCells(screen, screen.height * 0.45f);

            var prompt = new Rect(screen.width * 0.5f - 180f, screen.height * 0.56f, 360f, 28f);

            if (_match.LocalPlayerCanControlFlow)
            {
                MenuSkin.Fill(prompt, new Color(0.02f, 0.03f, 0.05f, 0.8f));
                MenuSkin.Frame(prompt, MenuSkin.Faint);

                MenuSkin.Text(prompt,
                    $"[{KeyLabel(rematchKey)}] rematch     [{KeyLabel(lobbyKey)}] back to warmup",
                    MenuSkin.Small, MenuSkin.Key, TextAnchor.MiddleCenter);
            }
            else
            {
                MenuSkin.Text(prompt, "waiting for the host", MenuSkin.Small, MenuSkin.Dim,
                    TextAnchor.MiddleCenter);
            }
        }

        /// <summary>
        /// The one big line, over a band dark enough to read against the arena. Without the band a
        /// white banner disappears into the skybox, which is the only reason it exists.
        /// </summary>
        private void Banner(Rect screen, string text, Color colour)
        {
            var band = new Rect(0f, screen.height * 0.34f, screen.width, 64f);

            MenuSkin.Fill(band, new Color(0.02f, 0.03f, 0.05f, 0.55f));
            MenuSkin.Fill(new Rect(band.x, band.y, band.width, 1f), new Color(1f, 1f, 1f, 0.08f));
            MenuSkin.Fill(new Rect(band.x, band.yMax - 1f, band.width, 1f),
                new Color(1f, 1f, 1f, 0.08f));

            MenuSkin.Text(band, text, MenuSkin.Banner, colour);
        }

        /// <summary>
        /// Scores as one cell per player in their own colour, rather than a rich-text string. Same
        /// shape as the percentage readout at the bottom of the screen, so the two read as one
        /// scoreboard shown in two places rather than two different lists of the same players.
        /// </summary>
        private void DrawScoreCells(Rect screen, float y)
        {
            BuildRoster();

            if (_roster.Count == 0)
                return;

            const float cell = 110f;
            const float gap = 8f;
            float total = _roster.Count * cell + (_roster.Count - 1) * gap;
            float x = (screen.width - total) * 0.5f;

            foreach (var vitals in _roster)
            {
                var rect = new Rect(x, y, cell, 64f);
                var colour = PlayerPalette.ColorOf(vitals.owner);

                MenuSkin.Fill(rect, new Color(0.02f, 0.03f, 0.05f, 0.8f));
                MenuSkin.Frame(rect, new Color(colour.r, colour.g, colour.b, 0.5f));
                MenuSkin.Fill(new Rect(rect.x, rect.y, rect.width, 2f), colour);

                MenuSkin.Text(new Rect(rect.x, rect.y + 6f, rect.width, 38f),
                    ScoreOf(vitals.owner).ToString(), MenuSkin.Score, MenuSkin.Ink);

                MenuSkin.Text(new Rect(rect.x, rect.y + 44f, rect.width, 16f),
                    PlayerPalette.NameOf(vitals.owner).ToUpperInvariant(), MenuSkin.Small, colour,
                    TextAnchor.MiddleCenter);

                x += cell + gap;
            }
        }

        // =====================================================================
        //  Helpers
        // =====================================================================

        /// <summary>
        /// Sorted by player id, so the order is the same on every screen and does not reshuffle
        /// between frames the way the score dictionary's own order can.
        /// </summary>
        private void BuildRoster()
        {
            _roster.Clear();

            foreach (var vitals in PlayerVitals.All)
            {
                // isSpawned as well as non-null: an identity that has not spawned yet has no
                // meaningful owner, and listing it would put an unnamed row in the roster.
                if (vitals == null || !vitals.isSpawned || !vitals.owner.HasValue)
                    continue;

                _roster.Add(vitals);
            }

            _roster.Sort(ByPlayerId);
        }

        private int ScoreOf(PlayerID? player)
        {
            if (!player.HasValue)
                return 0;

            return _match.Scores.TryGetValue(player.Value, out int score) ? score : 0;
        }

        /// <summary>KeyCode names are not what is printed on the key. Only the ones used here.</summary>
        private static string KeyLabel(KeyCode key)
        {
            return key switch
            {
                KeyCode.Return => "ENTER",
                KeyCode.KeypadEnter => "ENTER",
                KeyCode.Escape => "ESC",
                _ => key.ToString().ToUpperInvariant()
            };
        }
    }
}
