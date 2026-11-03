using System.Collections.Generic;
using PurrNet;
using UnityEngine;

namespace Game.Networking
{
    /// <summary>
    /// Spawns one player per connection, host-side only.
    ///
    /// Spawning is server-authoritative even though movement is not: the host decides who exists
    /// and where they start, then hands the body to its owner. That split is what CLAUDE.md means
    /// by "client-authoritative for own movement is acceptable" without opening the door to a
    /// client conjuring extra players.
    ///
    /// Spawn points come from a named container in the scene so the arena generator and this
    /// component don't have to agree on a wiring step that is easy to forget.
    /// </summary>
    public class PlayerSpawner : MonoBehaviour
    {
        [Header("What to spawn")]
        [SerializeField] private GameObject playerPrefab;

        [Header("Where")]
        [Tooltip("Parent of the spawn point transforms. Falls back to a scene object with this name.")]
        [SerializeField] private Transform spawnPointRoot;

        [SerializeField] private string spawnPointRootName = "SpawnPoints";

        [Header("Debug")]
        [SerializeField] private bool logSpawns = true;

        private NetworkManager _manager;
        private readonly List<Transform> _spawnPoints = new();
        private readonly Dictionary<PlayerID, GameObject> _spawned = new();
        private int _nextSpawnIndex;

        /// <summary>
        /// Where each dead player will come back, chosen at the moment they died.
        ///
        /// Decided early so the corpse can be parked on it immediately instead of being left
        /// wherever it was killed. Reserved rather than re-rolled at respawn time, so the body is
        /// moved once: picking again would send it to a different point and make the player watch
        /// a second teleport for no reason.
        /// </summary>
        private readonly Dictionary<PlayerID, (Vector3 position, Quaternion rotation)> _reserved = new();

        private void Awake()
        {
            _manager = GetComponent<NetworkManager>();
            if (_manager == null)
                _manager = FindFirstObjectByType<NetworkManager>();

            CollectSpawnPoints();
        }

        private void OnEnable()
        {
            if (_manager == null)
                return;

            _manager.onPlayerJoined += HandlePlayerJoined;
            _manager.onPlayerLeft += HandlePlayerLeft;
        }

        private void OnDisable()
        {
            if (_manager == null)
                return;

            _manager.onPlayerJoined -= HandlePlayerJoined;
            _manager.onPlayerLeft -= HandlePlayerLeft;
        }

        private void CollectSpawnPoints()
        {
            _spawnPoints.Clear();

            if (spawnPointRoot == null && !string.IsNullOrEmpty(spawnPointRootName))
            {
                var found = GameObject.Find(spawnPointRootName);
                if (found != null)
                    spawnPointRoot = found.transform;
            }

            if (spawnPointRoot == null)
            {
                Debug.LogWarning($"[PlayerSpawner] No '{spawnPointRootName}' object in the scene. " +
                                 "Players will spawn at the origin, stacked on top of each other.");
                return;
            }

            foreach (Transform child in spawnPointRoot)
                _spawnPoints.Add(child);

            if (_spawnPoints.Count == 0)
                Debug.LogWarning($"[PlayerSpawner] '{spawnPointRootName}' has no children to spawn at.");
        }

        /// <summary>
        /// Fires on both server and client. Only the server acts: a client spawning its own body
        /// would give every client a different idea of who is in the match.
        /// </summary>
        private void HandlePlayerJoined(PlayerID player, bool isReconnect, bool asServer)
        {
            if (!asServer)
                return;

            if (_spawned.ContainsKey(player))
            {
                if (logSpawns)
                    Debug.Log($"[PlayerSpawner] {player} already has a body, not spawning another.");
                return;
            }

            if (playerPrefab == null)
            {
                Debug.LogError("[PlayerSpawner] No player prefab assigned, nobody can spawn.");
                return;
            }

            GetSpawnPose(out var position, out var rotation);

            var instance = Instantiate(playerPrefab, position, rotation);
            _spawned[player] = instance;

            // PurrNet spawns a registered network prefab on Instantiate; ownership is the separate
            // step that makes the body respond to that player's input rather than nobody's.
            var identity = instance.GetComponent<NetworkIdentity>();
            if (identity == null)
            {
                Debug.LogError($"[PlayerSpawner] '{playerPrefab.name}' has no NetworkIdentity on its " +
                               "root, so it cannot be owned or synced. Run Tools > Ghost > Setup Networked Player.");
                return;
            }

            StartCoroutine(GiveOwnershipWhenSpawned(identity, player));

            if (logSpawns)
                Debug.Log($"[PlayerSpawner] Spawned {player} at {position}{(isReconnect ? " (reconnect)" : "")}.");

            OnPlayerSpawned?.Invoke(player, instance);
        }

        /// <summary>Raised on the host once a body exists for a player. Host-side only.</summary>
        public event System.Action<PlayerID, GameObject> OnPlayerSpawned;

        /// <summary>
        /// Host-side lookup, used by rocket homing to turn a target's PlayerID into a body.
        /// Resolving on the host rather than trusting a transform sent by the shooter keeps the
        /// client from naming an object it does not own.
        /// </summary>
        public bool TryGetBody(PlayerID player, out GameObject body)
        {
            return _spawned.TryGetValue(player, out body) && body != null;
        }

        /// <summary>
        /// Moves an existing body back to a spawn point and brings it back to life.
        ///
        /// Reuses the body rather than destroying and respawning it: the object carries ownership,
        /// the camera rig and the network identity, and churning it every few seconds would mean
        /// re-establishing all three on every death.
        /// </summary>
        public bool TryRespawn(PlayerID player)
        {
            if (!_spawned.TryGetValue(player, out var instance) || instance == null)
                return false;

            var vitals = instance.GetComponentInChildren<PlayerVitals>(true);
            if (vitals == null)
            {
                Debug.LogError($"[PlayerSpawner] {player} has no PlayerVitals; cannot respawn.");
                return false;
            }

            // The point reserved when they died, so the body is already sitting here and the
            // revive is a visibility change rather than a jump.
            Vector3 position;
            Quaternion rotation;

            if (_reserved.TryGetValue(player, out var pose))
            {
                position = pose.position;
                rotation = pose.rotation;
                _reserved.Remove(player);
            }
            else
            {
                GetSpawnPose(out position, out rotation);
            }

            // Delegated rather than moved here: the body is owner-authoritative, so only the
            // owning client can reposition it. Doing it host-side put the player back where they
            // died on the owner's next update.
            vitals.RespawnAt(position, rotation);

            if (logSpawns)
                Debug.Log($"[PlayerSpawner] Respawned {player} at {position}.");

            return true;
        }

        /// <summary>
        /// GiveOwnership hard-fails with "isn't spawned" when the network id has not been assigned
        /// yet, and the spawn does not always complete inside the Instantiate call. Waiting avoids
        /// an unowned body: one that nobody can drive, whose owner sees it as remote, and whose
        /// camera is therefore switched off.
        /// </summary>
        private System.Collections.IEnumerator GiveOwnershipWhenSpawned(NetworkIdentity identity, PlayerID player)
        {
            const float timeoutSeconds = 5f;
            float deadline = Time.time + timeoutSeconds;

            while (identity != null && !identity.isSpawned && Time.time < deadline)
                yield return null;

            if (identity == null)
                yield break;

            if (!identity.isSpawned)
            {
                Debug.LogError($"[PlayerSpawner] '{identity.name}' never finished spawning, so {player} " +
                               "owns nothing. Check that the prefab is in the NetworkPrefabs registry.");
                yield break;
            }

            identity.GiveOwnership(player);

            if (logSpawns)
                Debug.Log($"[PlayerSpawner] Gave ownership of '{identity.name}' to {player}.");
        }

        private void HandlePlayerLeft(PlayerID player, bool asServer)
        {
            if (!asServer)
                return;

            if (!_spawned.TryGetValue(player, out var instance))
                return;

            _spawned.Remove(player);

            // Dropped while dead: their reserved spawn point would otherwise stay blocked for the
            // rest of the match, pushing everyone else away from a point nobody is standing on.
            // Their score survives, because PurrNet's cookies hand a reconnecting player the same
            // PlayerID and the match keeps scores keyed by it.
            _reserved.Remove(player);

            if (instance != null)
                Destroy(instance);

            if (logSpawns)
                Debug.Log($"[PlayerSpawner] Despawned {player}.");
        }

        /// <summary>
        /// Round-robins through the authored spawn points. Good enough while the arena is a test
        /// box; P6 replaces this with spawn selection that avoids dropping players next to someone
        /// holding a rocket.
        /// </summary>
        /// <summary>
        /// Host only. Puts every player on the line for a new round: fresh life, zero percentage,
        /// spread across the spawn points.
        ///
        /// Walks the spawned bodies rather than the connected players so a peer who dropped mid
        /// round is skipped silently. Clears the reservations first, since any corpse parked on a
        /// point is about to be moved anyway and a stale reservation would push the living away
        /// from a point nobody is standing on.
        /// </summary>
        public void ResetAllForRound()
        {
            if (_manager == null || !_manager.isServer)
                return;

            _reserved.Clear();

            foreach (var entry in _spawned)
            {
                if (entry.Value == null)
                    continue;

                var vitals = entry.Value.GetComponentInChildren<PlayerVitals>(true);
                if (vitals == null)
                    continue;

                GetSpawnPose(out var position, out var rotation);
                vitals.ResetForRound(position, rotation);

                if (logSpawns)
                    Debug.Log($"[PlayerSpawner] Reset {entry.Key} to {position} for the round.");
            }
        }

        /// <summary>
        /// Picks and remembers where a dead player will return, so their body can be moved there
        /// at once. Host-side; returns false if the arena has no spawn points.
        /// </summary>
        public bool ReserveSpawnPose(PlayerID player, out Vector3 position, out Quaternion rotation)
        {
            GetSpawnPose(out position, out rotation);

            if (_spawnPoints.Count == 0)
                return false;

            _reserved[player] = (position, rotation);
            return true;
        }

        [Header("Spawn selection")]
        [Tooltip("A spawn point with anyone this close is treated as occupied. Players are ~1m " +
                 "across; this is the radius inside which two bodies would overlap or shove each " +
                 "other on arrival.")]
        [SerializeField] private float spawnClearance = 12f;

        /// <summary>
        /// Picks the emptiest spawn point rather than the next one in order.
        ///
        /// Round-robin was fine with one death at a time and wrong as soon as two players died
        /// together or someone happened to be floating on the point that came up: two bodies
        /// arrived in the same place and interpenetrated, which looks like a bug even though the
        /// physics resolves it a moment later.
        ///
        /// "Emptiest" counts living players AND the points already reserved by other dead players,
        /// because corpses are parked on their reserved point and will materialise there.
        /// </summary>
        private void GetSpawnPose(out Vector3 position, out Quaternion rotation)
        {
            if (_spawnPoints.Count == 0)
            {
                position = Vector3.zero;
                rotation = Quaternion.identity;
                return;
            }

            Transform best = null;
            float bestClearance = float.NegativeInfinity;

            for (int i = 0; i < _spawnPoints.Count; i++)
            {
                // Started from the rotating index so an empty arena still cycles instead of
                // sending everyone to spawn 1 forever.
                var candidate = _spawnPoints[(_nextSpawnIndex + i) % _spawnPoints.Count];
                if (candidate == null)
                    continue;

                float clearance = ClearanceAt(candidate.position);

                // Good enough wins immediately: beyond the clearance radius, further away is not
                // better, and taking the first acceptable point keeps spawns spread around the
                // arena instead of always choosing the one most isolated corner.
                if (clearance >= spawnClearance)
                {
                    best = candidate;
                    break;
                }

                if (clearance > bestClearance)
                {
                    bestClearance = clearance;
                    best = candidate;
                }
            }

            if (best == null)
                best = _spawnPoints[_nextSpawnIndex % _spawnPoints.Count];

            _nextSpawnIndex++;

            position = best.position;
            rotation = best.rotation;
        }

        /// <summary>
        /// Distance from a candidate point to the nearest thing that would collide with a player
        /// arriving there.
        /// </summary>
        private float ClearanceAt(Vector3 position)
        {
            float nearest = float.PositiveInfinity;

            var players = PlayerVitals.All;
            for (int i = 0; i < players.Count; i++)
            {
                var vitals = players[i];

                // Dead players are skipped because they are not where they appear to be - they are
                // parked on their reserved point, which is checked separately below.
                if (vitals == null || !vitals.isSpawned || !vitals.Vitals.IsAlive)
                    continue;

                nearest = Mathf.Min(nearest, Vector3.Distance(vitals.MarkerPosition, position));
            }

            foreach (var reserved in _reserved.Values)
                nearest = Mathf.Min(nearest, Vector3.Distance(reserved.position, position));

            return nearest;
        }
    }
}
