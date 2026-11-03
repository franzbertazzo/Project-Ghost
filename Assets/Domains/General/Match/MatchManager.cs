using System.Collections.Generic;
using Game.Networking;
using PurrNet;
using UnityEngine;

// PurrNet ships its own PlayerSpawner, so the bare name is ambiguous wherever both namespaces are
// imported. Aliased rather than fully qualified at each use so the clash is stated once, here.
using PlayerSpawner = Game.Networking.PlayerSpawner;

namespace Game.Match
{
    /// <summary>
    /// Where a match is in its life: the one piece of state every other flow decision reads.
    /// </summary>
    public enum MatchState
    {
        /// <summary>
        /// Before the match. A warmup, not a waiting room: you can fly and shoot, but nothing
        /// scores and death puts you straight back. Doubles as the pre-match lobby.
        /// </summary>
        Lobby = 0,

        /// <summary>ROUND N on screen. Everyone has already been repositioned and healed.</summary>
        RoundIntro = 1,

        /// <summary>Live. The only state in which deaths count.</summary>
        Round = 2,

        /// <summary>The round has been decided; sitting on the result before the next one.</summary>
        RoundEnd = 3,

        /// <summary>Someone reached the win condition. Waiting on a rematch.</summary>
        MatchEnd = 4
    }

    /// <summary>
    /// The match: rounds, scores, respawns, and who won.
    ///
    /// Host-authoritative throughout. The host already decides who dies, so it is the only peer
    /// that can count honestly; clients receive state as events and render it, and never compute
    /// it, because a client that counts its own kills is a client that can invent them.
    ///
    /// Both rulesets run through ONE state machine, per CLAUDE.md's "ruleset is data, not a
    /// branch". The modes differ in exactly two answers:
    ///
    ///   - what a death does      (Frag: score a kill and respawn. Elimination: stay dead.)
    ///   - what ends the round    (Frag: never - the match ends on the frag limit.
    ///                             Elimination: one player left standing.)
    ///
    /// Everything else - intro, reset, scoring, match end, rematch - is shared. Adding a third
    /// mode should mean a new asset and an answer to those two questions, not a new state.
    /// </summary>
    public class MatchManager : NetworkBehaviour
    {
        [SerializeField] private MatchRules rules;

        [Tooltip("Rulesets the host can pick between in the lobby. The one above is the starting " +
                 "choice; these are what cycling moves through.")]
        [SerializeField] private MatchRules[] selectableRules;

        [SerializeField] private PlayerSpawner spawner;

        [Header("Debug")]
        [SerializeField] private bool logFlow = true;

        /// <summary>Authoritative on the host; a replicated mirror on clients, for the HUD.</summary>
        private readonly Dictionary<PlayerID, int> _scores = new();

        private readonly List<PendingRespawn> _pendingRespawns = new();

        private MatchState _state = MatchState.Lobby;
        private int _round;
        private bool _matchOver;
        private PlayerID _winner;
        private PlayerID _roundWinner;
        private bool _hasRoundWinner;

        /// <summary>Host-side clock for the timed states. Clients get a mirror for the countdown.</summary>
        private float _stateUntil;

        public IReadOnlyDictionary<PlayerID, int> Scores => _scores;
        public bool MatchOver => _matchOver;
        public PlayerID Winner => _winner;
        public MatchRules Rules => rules;
        public MatchState State => _state;
        public int Round => _round;

        /// <summary>The round's winner, or null for a draw (everyone died together).</summary>
        public PlayerID? RoundWinner => _hasRoundWinner ? _roundWinner : null;

        /// <summary>Seconds left in a timed state, for the HUD. Local estimate.</summary>
        public float StateRemaining => Mathf.Max(0f, _stateUntil - Time.time);

        /// <summary>True when the local player may start or restart the match.</summary>
        public bool LocalPlayerCanControlFlow => IsHost;

        private struct PendingRespawn
        {
            public PlayerID Player;
            public float DueTime;
        }

        private void Awake()
        {
            if (spawner == null)
                // Searched scene-wide, not on this object: the spawner lives on the NetworkManager
                // rig while this deliberately sits on a separate GameObject.
                spawner = FindFirstObjectByType<PlayerSpawner>();

            // The banners live in their own component but are guaranteed rather than wired, so a
            // match in any scene always has its feedback. Separate class to keep presentation out
            // of the match logic; auto-added so there is no setup step to forget.
            if (GetComponent<MatchHud>() == null)
                gameObject.AddComponent<MatchHud>();
        }

        /// <summary>
        /// Subscribed in OnEnable, not OnSpawned.
        ///
        /// OnSpawned only runs once PurrNet has spawned this scene identity, and everything here
        /// was gated behind isServer - which is `isSpawned && manager.isServer`. When the identity
        /// did not spawn, scoring and respawns silently did nothing: deaths logged correctly and
        /// nobody ever came back, with no error to explain it. The Unity lifecycle always runs, so
        /// authority is checked against the NetworkManager directly instead.
        /// </summary>
        private void OnEnable()
        {
            if (rules == null)
                Debug.LogError("[Match] No MatchRules assigned; scoring and respawns are disabled.");

            if (spawner == null)
            {
                Debug.LogError("[Match] No PlayerSpawner found; scoring and respawns are disabled.");
                return;
            }

            spawner.OnPlayerSpawned += HandlePlayerSpawned;

            // Local mirror from the start, so the lobby reads correctly before anything is cycled
            // and on a host whose scene identity never spawned.
            if (rules != null)
                ApplyRulesName(rules.displayName, rules.scoreToWin, rules.mode);
        }

        /// <summary>
        /// Pushed on spawn so a client that arrives after the host has already cycled the ruleset
        /// sees what is actually about to be played.
        /// </summary>
        protected override void OnSpawned()
        {
            base.OnSpawned();

            if (IsHost && rules != null)
                BroadcastRulesName(rules.displayName, rules.scoreToWin, rules.mode);
        }

        private void OnDisable()
        {
            if (spawner != null)
                spawner.OnPlayerSpawned -= HandlePlayerSpawned;
        }

        /// <summary>
        /// Authority from the manager rather than from this identity's spawn state, so the match
        /// runs whether or not PurrNet has gotten around to spawning this scene object.
        /// </summary>
        private bool IsHost => networkManager != null && networkManager.isServer;

        /// <summary>
        /// Subscribes to each body as it appears. Done per-spawn rather than by scanning the scene
        /// so players who join mid-match are picked up without a polling loop.
        /// </summary>
        private void HandlePlayerSpawned(PlayerID player, GameObject body)
        {
            if (!IsHost)
                return;

            var vitals = body.GetComponent<PlayerVitals>();
            if (vitals == null)
            {
                Debug.LogError($"[Match] Spawned body for {player} has no PlayerVitals; deaths will not score.");
                return;
            }

            if (!_scores.ContainsKey(player))
                SetScore(player, 0);

            // Captured so the handler knows who died without searching for them again.
            vitals.Vitals.OnDied += killer => HandleDeath(player, killer);
        }

        // =====================================================================
        //  Deaths
        // =====================================================================

        private void HandleDeath(PlayerID victim, PlayerID? killer)
        {
            if (!IsHost || rules == null)
                return;

            // Warmup: deaths are free. Nothing scores and you come straight back, which is what
            // makes the lobby useful for practice instead of a screen people stare at.
            if (_state == MatchState.Lobby)
            {
                ScheduleRespawn(victim, rules.respawnDelay);
                return;
            }

            // Anything outside a live round - a body still settling during the intro, a stray
            // rocket landing after the round was decided - must not count. Without this, a kill
            // that arrives one frame late can score a point in a round that is already over.
            if (_state != MatchState.Round)
                return;

            bool selfKill = !killer.HasValue || killer.Value == victim;

            if (rules.mode == MatchMode.FragLimit)
            {
                if (selfKill)
                {
                    if (rules.selfKillsCostAPoint)
                        SetScore(victim, GetScore(victim) - 1);
                }
                else
                {
                    SetScore(killer.Value, GetScore(killer.Value) + 1);

                    if (GetScore(killer.Value) >= rules.scoreToWin)
                    {
                        EnterMatchEnd(killer.Value);
                        return;
                    }
                }

                ScheduleRespawn(victim, rules.respawnDelay);
                return;
            }

            // Elimination: the death itself scores nothing. Rounds are the currency, and the round
            // is settled by who is left - checked in Update rather than here, because the last two
            // players can die in the same blast and the answer depends on both.
        }

        private void ScheduleRespawn(PlayerID player, float delay)
        {
            _pendingRespawns.Add(new PendingRespawn
            {
                Player = player,
                DueTime = Time.time + delay
            });
        }

        // =====================================================================
        //  Flow
        // =====================================================================

        private void Update()
        {
            bool host = IsHost;

            // Ending a session leaves this component frozen wherever it was: IsHost goes false, the
            // switch below stops running, and _state, _scores and _winner all keep their values.
            // Hosting again then resumes in - typically - MatchEnd, with the previous match's
            // winner still declared and its scores still on the board, which from a player's seat
            // looks like the new room being broken rather than the old one not having been cleared.
            // Caught on the transition rather than from the session layer, so Game.Match keeps
            // owning its own state and does not need to know what a SessionController is.
            if (host != _wasHost)
            {
                _wasHost = host;

                if (host)
                    ResetForNewSession();
            }

            if (!host || rules == null)
                return;

            ServiceRespawns();

            switch (_state)
            {
                case MatchState.Lobby:
                    if (rules.autoStartPlayers > 0 && CountPlayers() >= rules.autoStartPlayers)
                        RequestStart();
                    break;

                case MatchState.RoundIntro:
                    if (Time.time >= _stateUntil)
                        EnterRound();
                    break;

                case MatchState.Round:
                    CheckRoundDecided();
                    break;

                case MatchState.RoundEnd:
                    if (Time.time >= _stateUntil)
                        EnterRoundIntro(_round + 1);
                    break;
            }
        }

        private void ServiceRespawns()
        {
            if (_pendingRespawns.Count == 0)
                return;

            // Iterated backwards so entries can be removed while walking the list.
            for (int i = _pendingRespawns.Count - 1; i >= 0; i--)
            {
                if (Time.time < _pendingRespawns[i].DueTime)
                    continue;

                var player = _pendingRespawns[i].Player;
                _pendingRespawns.RemoveAt(i);

                // Dropped between dying and respawning, or the round reset already revived them.
                if (spawner != null && !spawner.TryRespawn(player))
                    Debug.LogWarning($"[Match] Could not respawn {player}; they may have disconnected.");
            }
        }

        /// <summary>Host only. Starts the match from the lobby.</summary>
        public void RequestStart()
        {
            if (!IsHost || _state != MatchState.Lobby)
                return;

            _scores.Clear();
            foreach (var player in Players())
                SetScore(player, 0);

            EnterRoundIntro(1);
        }

        /// <summary>Host only. Replays the same ruleset without rehosting.</summary>
        public void RequestRematch()
        {
            if (!IsHost || _state != MatchState.MatchEnd)
                return;

            _matchOver = false;
            _winner = default;
            _scores.Clear();

            foreach (var player in Players())
                SetScore(player, 0);

            if (logFlow)
                Debug.Log("[Match] Rematch.");

            EnterRoundIntro(1);
        }

        /// <summary>
        /// Host only, lobby only. Moves to the next ruleset in the list.
        ///
        /// The whole mode selection is "which asset is in this field", which is the payoff of
        /// keeping the ruleset as data: switching between Elimination and Frag Limit changes no
        /// code path and needs no reload. Replicated by name so clients can show what they are
        /// about to play - they never need the asset itself, since every rule that matters is
        /// enforced on the host.
        /// </summary>
        public void CycleRules()
        {
            if (!IsHost || _state != MatchState.Lobby)
                return;

            if (selectableRules == null || selectableRules.Length == 0)
                return;

            int current = System.Array.IndexOf(selectableRules, rules);
            rules = selectableRules[(current + 1 + selectableRules.Length) % selectableRules.Length];

            if (logFlow)
                Debug.Log($"[Match] Ruleset: {rules.displayName}.");

            if (isSpawned)
                BroadcastRulesName(rules.displayName, rules.scoreToWin, rules.mode);
            else
                ApplyRulesName(rules.displayName, rules.scoreToWin, rules.mode);
        }

        /// <summary>
        /// What clients display in the lobby. Deliberately just the label, the target score and the
        /// mode: a client that held the whole ruleset could believe a different win condition than
        /// the host is enforcing, and the host is the only peer that decides.
        /// </summary>
        public string RulesLabel { get; private set; }

        public int RulesScoreToWin { get; private set; }

        public MatchMode RulesMode { get; private set; }

        [ObserversRpc(runLocally: true, bufferLast: true)]
        private void BroadcastRulesName(string label, int scoreToWin, MatchMode mode)
        {
            ApplyRulesName(label, scoreToWin, mode);
        }

        private void ApplyRulesName(string label, int scoreToWin, MatchMode mode)
        {
            RulesLabel = label;
            RulesScoreToWin = scoreToWin;
            RulesMode = mode;
        }

        /// <summary>Host only. Drops back to warmup, so the arena stays usable after a match.</summary>
        private bool _wasHost;

        /// <summary>
        /// Wipes the match back to a fresh warmup when a new session begins.
        ///
        /// Deliberately does not touch the players: at the moment hosting starts there are none
        /// yet, and the warmup resets everyone as they arrive anyway. This is only about the
        /// bookkeeping that would otherwise survive from the last room.
        /// </summary>
        private void ResetForNewSession()
        {
            bool alreadyClean = _state == MatchState.Lobby && !_matchOver && _round == 0 &&
                                _scores.Count == 0;

            if (alreadyClean)
                return;

            if (logFlow)
                Debug.Log("[Match] New session - clearing the previous match and returning to " +
                          "warmup.");

            _matchOver = false;
            _round = 0;
            _hasRoundWinner = false;
            _pendingRespawns.Clear();
            _scores.Clear();

            SetState(MatchState.Lobby, 0f, null);
        }

        public void RequestReturnToLobby()
        {
            if (!IsHost)
                return;

            _matchOver = false;
            _round = 0;
            _pendingRespawns.Clear();
            ResetEveryone();
            SetState(MatchState.Lobby, 0f, null);
        }

        private void EnterRoundIntro(int round)
        {
            _round = round;
            _pendingRespawns.Clear();

            // Reset BEFORE the banner, not after: the intro is there so the players can see where
            // they have been put, which only works if they are already there.
            ResetEveryone();

            if (logFlow)
                Debug.Log($"[Match] Round {round} starting.");

            SetState(MatchState.RoundIntro, rules.roundIntroSeconds, null);
        }

        private void EnterRound()
        {
            SetState(MatchState.Round, 0f, null);
        }

        /// <summary>
        /// Elimination's round-end test: the round is over when one player is left, or none.
        ///
        /// A draw is a real outcome - two players caught in one blast - and it awards nothing
        /// rather than crediting the explosion. Guarded on having had at least two players so a
        /// solo host in Elimination does not win a round every time they respawn.
        /// </summary>
        private void CheckRoundDecided()
        {
            if (rules.mode != MatchMode.Elimination)
                return;

            int players = CountPlayers();
            if (players < 2)
                return;

            PlayerID? survivor = null;
            int alive = 0;

            foreach (var vitals in PlayerVitals.All)
            {
                if (vitals == null || !vitals.isSpawned || !vitals.Vitals.IsAlive)
                    continue;

                alive++;
                survivor = vitals.owner;
            }

            if (alive > 1)
                return;

            EnterRoundEnd(alive == 1 ? survivor : null);
        }

        private void EnterRoundEnd(PlayerID? roundWinner)
        {
            _pendingRespawns.Clear();

            if (roundWinner.HasValue)
            {
                SetScore(roundWinner.Value, GetScore(roundWinner.Value) + 1);

                if (GetScore(roundWinner.Value) >= rules.scoreToWin)
                {
                    EnterMatchEnd(roundWinner.Value);
                    return;
                }
            }

            if (logFlow)
            {
                Debug.Log(roundWinner.HasValue
                    ? $"[Match] Round {_round} to {roundWinner.Value}."
                    : $"[Match] Round {_round} drawn.");
            }

            SetState(MatchState.RoundEnd, rules.roundEndSeconds, roundWinner);
        }

        private void EnterMatchEnd(PlayerID winner)
        {
            _matchOver = true;
            _winner = winner;
            _pendingRespawns.Clear();

            if (logFlow)
                Debug.Log($"[Match] {winner} wins the match with {GetScore(winner)}.");

            SetState(MatchState.MatchEnd, 0f, winner);
        }

        private void ResetEveryone()
        {
            if (spawner != null)
                spawner.ResetAllForRound();
        }

        // =====================================================================
        //  State replication
        // =====================================================================

        private void SetState(MatchState state, float duration, PlayerID? actor)
        {
            _stateUntil = duration > 0f ? Time.time + duration : 0f;

            // Duration travels rather than an absolute time: Time.time is each peer's own clock,
            // so a deadline computed here would be meaningless anywhere else.
            if (isSpawned)
                BroadcastState(state, _round, duration, actor ?? default, actor.HasValue);
            else
                ApplyState(state, _round, duration, actor ?? default, actor.HasValue);
        }

        /// <summary>
        /// bufferLast so a player who joins mid-match is told the current state on arrival instead
        /// of sitting in a lobby that everyone else left.
        /// </summary>
        [ObserversRpc(runLocally: true, bufferLast: true)]
        private void BroadcastState(MatchState state, int round, float duration, PlayerID actor, bool hasActor)
        {
            ApplyState(state, round, duration, actor, hasActor);
        }

        private void ApplyState(MatchState state, int round, float duration, PlayerID actor, bool hasActor)
        {
            _state = state;
            _round = round;
            _stateUntil = duration > 0f ? Time.time + duration : 0f;

            _hasRoundWinner = hasActor && state == MatchState.RoundEnd;
            if (_hasRoundWinner)
                _roundWinner = actor;

            if (state == MatchState.MatchEnd && hasActor)
            {
                _matchOver = true;
                _winner = actor;
            }
            else if (state != MatchState.MatchEnd)
            {
                _matchOver = false;
            }
        }

        // =====================================================================
        //  Scores
        // =====================================================================

        private int GetScore(PlayerID player) => _scores.TryGetValue(player, out var score) ? score : 0;

        private void SetScore(PlayerID player, int score)
        {
            _scores[player] = score;

            // The RPC needs a spawned identity. If this scene object has not spawned, the match
            // still runs - the host just keeps the score to itself rather than the whole thing
            // falling over.
            if (isSpawned)
                BroadcastScore(player, score);
        }

        /// <summary>
        /// Scores are pushed as events rather than held in a SyncDictionary: they change rarely
        /// (once per kill) and the HUD only ever needs the latest value.
        /// </summary>
        [ObserversRpc(runLocally: true, bufferLast: true)]
        private void BroadcastScore(PlayerID player, int score)
        {
            _scores[player] = score;
        }

        private int CountPlayers()
        {
            int count = 0;

            foreach (var vitals in PlayerVitals.All)
            {
                if (vitals != null && vitals.isSpawned)
                    count++;
            }

            return count;
        }

        private IEnumerable<PlayerID> Players()
        {
            foreach (var vitals in PlayerVitals.All)
            {
                if (vitals != null && vitals.isSpawned && vitals.owner.HasValue)
                    yield return vitals.owner.Value;
            }
        }
    }
}
