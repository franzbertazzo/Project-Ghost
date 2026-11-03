using Game.Networking;
using PurrNet;
using UnityEngine;

// PurrNet ships its own PlayerSpawner, so the bare name is ambiguous wherever both namespaces are
// imported. Same alias as MatchManager uses, for the same reason.
using PlayerSpawner = Game.Networking.PlayerSpawner;

namespace Game.Match
{
    /// <summary>
    /// The arena boundary. Leaving it is what kills you.
    ///
    /// This is the half of the Smash model that makes percentage mean anything: damage never
    /// kills, it only decides how far a hit throws you, and the boundary decides whether that
    /// throw was fatal. Without this, percentage climbs forever and nobody ever dies.
    ///
    /// Host-only. Clients do not get to decide they are still in bounds.
    /// </summary>
    public class BlastZone : MonoBehaviour
    {
        [Header("Bounds")]
        [Tooltip("Distance from this object's position beyond which a player is out. Measured to " +
                 "the player's centre of mass.\n\n" +
                 "Sized against the arena, not picked round: the island including its debris " +
                 "reaches about 60m, so this leaves roughly one more island-radius of empty space " +
                 "to fight your way back through. Wide enough that being launched is a problem " +
                 "rather than a death sentence, tight enough that a hit above ~50% is lethal from " +
                 "the island's edge.")]
        [SerializeField] private float radius = 110f;

        /// <summary>Read by the HUD so its warning matches the real boundary exactly.</summary>
        public float Radius => radius;

        [Tooltip("How often to test, in seconds. Players travel fast after a launch but not fast " +
                 "enough to need a per-frame check.")]
        [SerializeField] private float checkInterval = 0.2f;

        [Tooltip("Seconds of immunity after a respawn.\n\n" +
                 "Not a gameplay nicety - it is required for correctness. The respawn teleport is " +
                 "an RPC to the owning client, who moves the body and streams the position back, " +
                 "so for about a round trip the host still sees the player where they died. After " +
                 "a ring-out that is outside the boundary, and this check killed them again the " +
                 "instant they revived. Must comfortably exceed worst-case RTT.")]
        [SerializeField] private float respawnGrace = 1f;

        [Header("Debug")]
        [SerializeField] private bool logRingOuts = true;

        private NetworkManager _manager;
        private PlayerSpawner _spawner;
        private float _nextCheckTime;

        private void Awake()
        {
            _manager = FindFirstObjectByType<NetworkManager>();
            _spawner = FindFirstObjectByType<PlayerSpawner>();

            if (_spawner == null)
                Debug.LogError("[BlastZone] No PlayerSpawner found; ring-outs are disabled.");
        }

        private void Update()
        {
            if (_manager == null || !_manager.isServer || _spawner == null)
                return;

            if (Time.time < _nextCheckTime)
                return;

            _nextCheckTime = Time.time + checkInterval;

            foreach (var player in _manager.players)
            {
                if (!_spawner.TryGetBody(player, out var body))
                    continue;

                var vitals = body.GetComponentInChildren<PlayerVitals>(true);
                if (vitals == null || !vitals.Vitals.IsAlive)
                    continue;

                // MarkerPosition, NOT body.transform.position.
                //
                // The spawner hands back the networked ROOT, and the root never moves - only its
                // child body does, driven by its own NetworkTransform. So this was measuring the
                // distance from the arena centre to each player's spawn point, every time, and
                // nobody was ever out of bounds. The boundary was not disabled, it was blind.
                bool outside = Vector3.Distance(vitals.MarkerPosition, transform.position) > radius;

                // Freshly respawned players are never killed here: the invulnerability window
                // applies to the boundary as much as to weapons, and the host's copy of a body may
                // still be catching up with the respawn teleport.
                bool protectedNow = vitals.Vitals.IsInvulnerable ||
                                    (vitals.Vitals.RevivedAt > 0f &&
                                     Time.time - vitals.Vitals.RevivedAt < respawnGrace);

                if (protectedNow)
                {
                    // A protected player who reads as out of bounds means the host never received
                    // the respawn position - it cannot be real, because they were just placed on a
                    // spawn point. Correct it rather than waiting out the window and killing them
                    // for it. Logged loudly: if this ever fires, a position update went missing.
                    if (outside)
                    {
                        Debug.LogWarning($"[BlastZone] {player} reads as out of bounds while " +
                                         "protected; the host missed their respawn position. " +
                                         "Returning them to a spawn point.");
                        vitals.ReturnToSpawn();
                    }

                    continue;
                }

                if (!outside)
                    continue;

                // Credited to whoever last damaged them, not to the boundary. Being launched off
                // the map by someone else is their kill; flying out unaided is your own fault.
                var killer = vitals.Vitals.LastAttacker;
                vitals.Vitals.Kill(killer);

                if (logRingOuts)
                {
                    Debug.Log(killer.HasValue
                        ? $"[BlastZone] {player} knocked out by {killer.Value} at {vitals.Vitals.DamagePercent:F0}%."
                        : $"[BlastZone] {player} flew out unaided at {vitals.Vitals.DamagePercent:F0}%.");
                }
            }
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(1f, 0.3f, 0.2f, 0.35f);
            Gizmos.DrawWireSphere(transform.position, radius);
        }
    }
}
