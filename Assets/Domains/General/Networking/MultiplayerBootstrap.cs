using PurrNet;
using UnityEngine;

namespace Game.Networking
{
    /// <summary>
    /// Starts the NetworkManager in the right role for this process.
    ///
    /// The NetworkManager in the scene has its auto-start flags set to None so that this is the only
    /// thing deciding who hosts. See <see cref="MultiplayerRoleResolver"/> for why PurrNet's own
    /// flags can't be trusted under Multiplayer Play Mode.
    /// </summary>
    [RequireComponent(typeof(NetworkManager))]
    public class MultiplayerBootstrap : MonoBehaviour
    {
        [Tooltip("Auto works out the role from MPPM tags / clone markers. Anything else forces it.")]
        [SerializeField] private MultiplayerRole roleOverride = MultiplayerRole.Auto;

        [Tooltip("Seconds a client waits before connecting, giving the host time to open its socket.")]
        [SerializeField] private float clientConnectDelay = 0.5f;

        [Tooltip("Retry connecting if the first attempt finds no host. Editor testing convenience.")]
        [SerializeField] private bool retryUntilConnected = true;

        [SerializeField] private float retryInterval = 2f;

        [Tooltip("Print this instance's launch arguments. Useful when role detection picks wrong.")]
        [SerializeField] private bool logLaunchArguments = true;

        private NetworkManager _manager;
        private float _nextRetryTime;
        private bool _clientStartRequested;
        private int _attempts;

        /// <summary>The role this process actually resolved to. Read by the HUD and latency sim.</summary>
        public MultiplayerRole ActiveRole { get; private set; } = MultiplayerRole.Auto;

        [Tooltip("Create a SessionController if the scene has none, so every launch gets the " +
                 "Host/Join menu instead of silently hosting. Untick to go back to auto-hosting " +
                 "from launch markers.")]
        [SerializeField] private bool ensureSessionController = true;

        /// <summary>
        /// Read by <see cref="SessionController"/> to decide whether to draw the menu at all.
        /// Serialized fields are deserialized before any Awake runs, so this is safe to read from
        /// another component's Awake regardless of which one Unity wakes first.
        /// </summary>
        public MultiplayerRole RoleOverride => roleOverride;

        private void Awake()
        {
            _manager = GetComponent<NetworkManager>();

            // Added here rather than required in the scene so the join flow works with nothing
            // wired. Add the component to the NetworkManager by hand if you want to configure it -
            // this one is created at runtime and so cannot be edited in the inspector.
            if (ensureSessionController && GetComponent<SessionController>() == null)
                gameObject.AddComponent<SessionController>();

            string reason;
            if (roleOverride != MultiplayerRole.Auto)
            {
                ActiveRole = roleOverride;
                reason = "inspector override";
            }
            else if (MultiplayerRoleResolver.TryResolveTag(out var taggedRole, out reason))
            {
                // A tag is the one marker a human set deliberately, so it still wins over the menu.
                ActiveRole = taggedRole;
            }
            else if (GetComponent<SessionController>() != null)
            {
                // The menu is now the default for every instance, including MPPM's virtual players.
                // Whether this process hosts or joins is the player's choice on that screen, not
                // something inferred from a launch argument - inferring it is what left clients
                // with no way to enter a code, auto-connecting on a transport the host had already
                // swapped away from.
                ActiveRole = MultiplayerRole.None;
                reason = "the session menu starts the connection";
            }
            else if (MultiplayerRoleResolver.TryResolveExplicit(out var explicitRole, out reason))
            {
                // No menu in the scene, so fall back to the old marker-driven behaviour.
                ActiveRole = explicitRole;
            }
            else
            {
                // No menu and no markers - keep the old behaviour so a bare scene still comes up
                // as a host for quick testing.
                ActiveRole = MultiplayerRole.Host;
                reason = "main editor / standalone, no instance markers, no session menu";
            }

            Debug.Log($"[Multiplayer] Role: {MultiplayerRoleResolver.Describe(ActiveRole)} ({reason})");

#if UNITY_EDITOR
            // If two instances both claim HOST, the launch arguments are the first thing to check:
            // MPPM should be passing -name PlayerN to each one.
            if (logLaunchArguments)
                Debug.Log($"[Multiplayer] Launch args: {string.Join(" ", System.Environment.GetCommandLineArgs())}");
#endif
        }

        private void Start()
        {
            switch (ActiveRole)
            {
                case MultiplayerRole.Host:
                    _manager.StartHost();
                    break;

                case MultiplayerRole.Server:
                    _manager.StartServer();
                    break;

                case MultiplayerRole.Client:
                    _clientStartRequested = true;
                    _nextRetryTime = Time.time + clientConnectDelay;
                    break;

                case MultiplayerRole.None:
                    Debug.Log("[Multiplayer] Nothing started. Connect manually.");
                    break;
            }
        }

        private void Update()
        {
            if (!_clientStartRequested || Time.time < _nextRetryTime)
                return;

            if (_manager.isClient)
            {
                _clientStartRequested = false;
                Debug.Log($"[Multiplayer] Connected after {_attempts} attempt(s).");
                return;
            }

            _attempts++;
            Debug.Log($"[Multiplayer] Connecting to host (attempt {_attempts})...");

            _manager.StartClient();
            _nextRetryTime = Time.time + retryInterval;

            if (!retryUntilConnected)
                _clientStartRequested = false;
        }
    }
}
