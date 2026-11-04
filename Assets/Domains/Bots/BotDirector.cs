using System.Collections.Generic;
using Game.Networking;
using PurrNet;
using PurrNet.Modules;
using UnityEngine;

namespace Game.Bots
{
    /// <summary>
    /// Adds and removes bots. Host-only.
    ///
    /// Bots are **real players** here, not a parallel system: <c>PlayersManager.CreateBot</c> hands
    /// out a genuine <see cref="PlayerID"/> with <c>isBot</c> set, registers it in the connected
    /// players list and fires the same joined event a connection does. Everything downstream then
    /// works with no special cases at all - the spawner gives it a body, the match gives it a score
    /// entry and respawns it, the palette gives it a name and a colour, kill credit names it, and
    /// the boundary rings it out. That is why this file is short: the integration was already
    /// written, for players.
    ///
    /// The one thing that does need handling is control. <c>isController</c> is
    /// <c>hasConnectedOwner ? isOwner : isServer</c>, and a bot has no connection - so the host is
    /// the controller of a bot-owned body automatically, which is exactly what makes its transform
    /// replicate and its teleports force-sync without a line of special code. But <c>isOwner</c> is
    /// still false on every peer, which is why the gating needed a separate notion of "drives".
    /// </summary>
    [RequireComponent(typeof(NetworkManager))]
    public class BotDirector : MonoBehaviour
    {
        [Tooltip("Profiles to draw from, in order. Fewer profiles than bots cycles the list.")]
        [SerializeField] private BotProfile[] roster;

        [Tooltip("Bots to add automatically once hosting. 0 means bots are added by hand from the " +
                 "warmup screen.")]
        [SerializeField] private int autoFill;

        [Tooltip("Seconds after hosting before an auto-filled bot appears, so it does not join " +
                 "before the arena and the local player exist.")]
        [SerializeField] private float startDelay = 2f;

        [SerializeField] private bool logBots = true;

        private NetworkManager _manager;
        private Game.Networking.PlayerSpawner _spawner;
        private readonly List<PlayerID> _bots = new();
        private float _autoFillAt;
        private bool _hooked;

        /// <summary>Bots currently in the match, for the warmup readout.</summary>
        public int Count => _bots.Count;

        public bool CanAdd => roster != null && roster.Length > 0;

        private void Awake()
        {
            _manager = GetComponent<NetworkManager>();
            _spawner = GetComponent<Game.Networking.PlayerSpawner>();

            if (_spawner == null)
                _spawner = FindFirstObjectByType<Game.Networking.PlayerSpawner>();
        }

        private void OnEnable()
        {
            _autoFillAt = Time.time + Mathf.Max(0.5f, startDelay);
        }

        private void Update()
        {
            if (_manager == null || !_manager.isServer)
                return;

            // Subscribed here rather than in Awake because the spawner's event is the only place a
            // bot's body can be caught at the moment it exists, and the spawner may be found after
            // this component wakes.
            if (!_hooked && _spawner != null)
            {
                _hooked = true;
                _spawner.OnPlayerSpawned += HandleSpawned;
            }

            if (autoFill > 0 && _bots.Count < autoFill && Time.time >= _autoFillAt)
            {
                _autoFillAt = Time.time + 1f;
                AddBot();
            }
        }

        private void OnDisable()
        {
            if (_hooked && _spawner != null)
                _spawner.OnPlayerSpawned -= HandleSpawned;

            _hooked = false;
        }

        /// <summary>
        /// Registers one more bot player. The body, the score entry and the name all follow from
        /// the joined event this raises inside PurrNet.
        /// </summary>
        public bool AddBot()
        {
            if (_manager == null || !_manager.isServer)
                return false;

            if (!CanAdd)
            {
                Debug.LogWarning("[BotDirector] No profiles in the roster, so a bot would have no " +
                                 "personality to spawn with.");
                return false;
            }

            if (!_manager.TryGetModule<PlayersManager>(true, out var players))
            {
                Debug.LogError("[BotDirector] No PlayersManager; cannot create a bot.");
                return false;
            }

            var id = players.CreateBot();
            _bots.Add(id);

            // Named before the body arrives, so the nickname is already in place when the identity
            // module publishes it on spawn.
            _pendingProfiles[id] = roster[(_bots.Count - 1) % roster.Length];

            if (logBots)
                Debug.Log($"[BotDirector] Added bot {id} as " +
                          $"'{_pendingProfiles[id].displayName}'.");

            return true;
        }

        /// <summary>
        /// Removes the most recently added bot. Kicked rather than destroyed, so PurrNet unwinds
        /// the player properly - the body despawns and the match drops it the same way it handles
        /// anyone leaving.
        /// </summary>
        public bool RemoveBot()
        {
            if (_manager == null || !_manager.isServer || _bots.Count == 0)
                return false;

            if (!_manager.TryGetModule<PlayersManager>(true, out var players))
                return false;

            var id = _bots[^1];
            _bots.RemoveAt(_bots.Count - 1);
            _pendingProfiles.Remove(id);

            players.KickPlayer(id);

            if (logBots)
                Debug.Log($"[BotDirector] Removed bot {id}.");

            return true;
        }

        /// <summary>
        /// Attaches the brain once the spawner has made a body for a bot.
        ///
        /// Added at runtime rather than living on the player prefab, so a human's body never
        /// carries a brain it does not use and the prefab needs no change to support bots at all.
        /// </summary>
        private void HandleSpawned(PlayerID player, GameObject body)
        {
            if (!player.isBot || body == null)
                return;

            if (!_pendingProfiles.TryGetValue(player, out var profile) || profile == null)
                profile = roster != null && roster.Length > 0 ? roster[0] : null;

            if (profile == null)
                return;

            var controller = body.GetComponent<BotController>();
            if (controller == null)
                controller = body.AddComponent<BotController>();

            controller.SetProfile(profile);

            // The name every readout will show. Written host-side because a bot has no client to
            // write it, and the identity module publishes it from there to every peer.
            var vitals = body.GetComponent<PlayerVitals>();
            if (vitals != null)
                vitals.Identity.SetNicknameFromHost(profile.displayName);

            // The gating caches its answer on spawn, and the brain arrived after it - so without
            // this the body stays switched off as an unowned remote and drifts.
            var networkPlayer = body.GetComponent<NetworkPlayer>();
            if (networkPlayer != null)
                networkPlayer.RefreshGating();

            if (logBots)
                Debug.Log($"[BotDirector] Bot {player} is flying as '{profile.displayName}'.");
        }

        private readonly Dictionary<PlayerID, BotProfile> _pendingProfiles = new();
    }
}
