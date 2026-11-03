using PurrNet;
using PurrNet.Transports;
using UnityEngine;

namespace Game.Networking
{
    public enum SessionState
    {
        /// <summary>Nothing started. The menu is up.</summary>
        Offline = 0,

        /// <summary>Asking the relay for a room.</summary>
        Opening = 1,

        /// <summary>Connecting to someone else's room.</summary>
        Joining = 2,

        /// <summary>In a match.</summary>
        Online = 3,

        /// <summary>The attempt failed. Status says why.</summary>
        Failed = 4
    }

    /// <summary>
    /// Hosting and joining by code.
    ///
    /// Runs on PurrNet's bundled relay (<see cref="PurrTransport"/>), which addresses rooms by
    /// name - so the code a player types IS the room, and there is no address to discover, no port
    /// to forward and no NAT to punch through. That was the open question in CLAUDE.md and this is
    /// the answer: the relay costs a hop of latency and buys a join path that works between two
    /// people on home connections, which direct connect does not.
    ///
    /// The transport is chosen at runtime rather than baked into the scene, for two reasons. The
    /// MPPM test flow still wants <see cref="UDPTransport"/> on localhost, where a relay would add
    /// latency and an internet dependency to every editor run. And Steam, later, is a third
    /// transport behind this same "type a code, connect" path - PurrNet ships SteamTransport as a
    /// sibling addon, so that is a swap here rather than a rewrite of the join flow.
    ///
    /// Deliberately not a NetworkBehaviour: it has to work before there is a connection.
    ///
    /// On the timeout: opening a relay room is three network round trips, not one. PurrTransport
    /// asks the master server for a server list, pings the candidates, then allocates the room -
    /// and it wraps the list and the allocation in <c>Retry(10, ...)</c> with a second between
    /// attempts, so a flaky relay can legitimately take over twenty seconds before it either works
    /// or throws. A timeout under that turns "slow" into "failed", and worse, the StopServer this
    /// class issues on failure cancels the attempt that was still in progress. Hence 35s.
    ///
    /// When it does fail, PurrNet logs the actual reason itself through PurrLogger - usually
    /// "Failed to allocate room: ..." with the server's response. The status line here only says
    /// that it failed, so the Console is the place to look for why.
    /// </summary>
    [RequireComponent(typeof(NetworkManager))]
    public class SessionController : MonoBehaviour
    {
        [Tooltip("Seconds to wait for the relay before giving up. Has to clear PurrNet's own retry " +
                 "chain - see the note in the class comment - or a slow relay reads as a failure.")]
        [SerializeField] private float connectTimeout = 35f;

        [Tooltip("Host over the relay. Off uses whatever transport the scene already has, which " +
                 "is how the MPPM instances talk to each other over localhost.")]
        [SerializeField] private bool useRelay = true;

        private NetworkManager _manager;
        private PurrTransport _relay;
        private GenericTransport _sceneTransport;
        private float _deadline;

        /// <summary>This attempt is on the scene transport (localhost/LAN), not the relay.</summary>
        private bool _local;

        private Attempt _queued;

        /// <summary>
        /// What has been asked for but not started yet. Attempts are queued rather than acted on
        /// immediately because the transport can only be changed while the manager is idle, and
        /// PurrNet is not idle the instant you stop it - see <see cref="TryStartQueued"/>.
        /// </summary>
        private enum Attempt
        {
            None = 0,
            HostRelay = 1,
            HostLocal = 2,
            JoinRelay = 3,
            JoinLocal = 4
        }

        public SessionState State { get; private set; } = SessionState.Offline;

        /// <summary>The code being hosted or joined. Empty when offline.</summary>
        public string Code { get; private set; } = string.Empty;

        /// <summary>One line for the menu to show. Always set alongside State.</summary>
        public string Status { get; private set; } = string.Empty;

        /// <summary>
        /// True when this process is driving its own connection through the menu, rather than
        /// having been launched into a role by MPPM. The menu only draws itself when this is set.
        /// </summary>
        public bool MenuDriven { get; private set; }

        public bool IsHost { get; private set; }

        /// <summary>Mid-attempt. The menu swaps its buttons for progress while this is set.</summary>
        public bool Busy => State == SessionState.Opening || State == SessionState.Joining;

        /// <summary>
        /// Seconds spent on the current attempt, and how far through the timeout that is.
        ///
        /// Shown rather than kept internal because fifteen seconds of a motionless "Opening a
        /// room..." reads as a hang. A number that moves and a bar that fills say the same thing as
        /// a spinner, and also say how much patience is left before it gives up.
        /// </summary>
        public float AttemptElapsed =>
            Busy ? Mathf.Max(0f, connectTimeout - (_deadline - Time.time)) : 0f;

        public float AttemptProgress =>
            Busy && connectTimeout > 0f ? Mathf.Clamp01(AttemptElapsed / connectTimeout) : 0f;

        private void Awake()
        {
            _manager = GetComponent<NetworkManager>();
            _sceneTransport = _manager.transport;

            // Every instance gets the menu, so that whether this process hosts or joins is the
            // player's choice on one screen rather than something inferred from how it was
            // launched. Only two things take the menu away, and both are deliberate: an inspector
            // role override, and an MPPM player tag naming a role. MPPM's own `-name PlayerN`
            // argument is explicitly not one of them - it is passed to every virtual player whether
            // or not anyone asked, and treating it as a role is what left clients with no way to
            // enter a code.
            MenuDriven = !LaunchedIntoRole();

            // Before the role check, so every launch has a camera. An MPPM instance has no menu but
            // still spends a second or two connecting before its player exists, and that gap was
            // "No cameras rendering" too - just short enough to be ignored.
            EnsureBackdropCamera();

            if (!MenuDriven)
            {
                Status = "Launched into a role; menu disabled.";
                return;
            }

            if (GetComponent<SessionMenu>() == null)
                gameObject.AddComponent<SessionMenu>();
        }

        /// <summary>
        /// True when something deliberately said what this process is, and the menu should keep out
        /// of the way. See the note in Awake for why MPPM's instance name is not one of them.
        /// </summary>
        private bool LaunchedIntoRole()
        {
            var bootstrap = GetComponent<MultiplayerBootstrap>();

            if (bootstrap != null && bootstrap.RoleOverride != MultiplayerRole.Auto)
                return true;

            // A tag of "menu" resolves to None, which is a request for this screen, not a role.
            return MultiplayerRoleResolver.TryResolveTag(out var tagged, out _)
                   && tagged != MultiplayerRole.None;
        }

        /// <summary>
        /// The menu needs something to be drawn in front of.
        ///
        /// Every camera in this game is on the player prefab, so between "press play" and "a player
        /// exists" the scene has none at all - which Unity reports as "No cameras rendering" and
        /// which left the menu floating over an undefined frame. Only created when menu-driven: an
        /// MPPM instance spawns its player immediately and never needs one.
        ///
        /// Its own object rather than this one, so it can be positioned without moving the network
        /// rig, and so deactivating it takes its audio listener with it.
        /// </summary>
        private void EnsureBackdropCamera()
        {
            if (FindFirstObjectByType<MenuBackdropCamera>() != null)
                return;

            var backdrop = new GameObject("Menu Backdrop Camera");
            backdrop.transform.SetParent(null);
            backdrop.AddComponent<MenuBackdropCamera>();
        }

        private void Update()
        {
            // Queued attempts come first and nothing else runs while one is waiting: the success
            // checks below read isServer/isClient, which could still be describing the connection
            // being torn down rather than the one being asked for.
            if (_queued != Attempt.None)
            {
                TryStartQueued();
                return;
            }

            switch (State)
            {
                case SessionState.Opening:
                    // isServer flips once the listener is actually up, which for the relay means
                    // the room was allocated - so this doubles as "the relay answered".
                    if (_manager.isServer)
                        Succeed(_local
                            ? "Hosting locally"
                            : $"Hosting  {JoinCode.ForDisplay(Code)}");
                    else if (Time.time > _deadline)
                        Fail(_local
                            ? "Could not open a local server. Something else may hold the port."
                            : "Could not reach the relay. See the console for the reason.");
                    break;

                case SessionState.Joining:
                    // isClient only flips once the connection is actually established, which is
                    // the same signal the MPPM bootstrap already relies on for its retry loop.
                    if (_manager.isClient)
                        Succeed(_local
                            ? "Joined the local host"
                            : $"Joined  {JoinCode.ForDisplay(Code)}");
                    else if (Time.time > _deadline)
                        Fail(_local
                            ? "No local host found. Start one in another instance first."
                            : $"No room called {JoinCode.ForDisplay(Code)}. Check the code.");
                    break;

                case SessionState.Online:
                    // Dropped: the host closed the room, or the connection died. Back to the menu
                    // rather than a frozen arena.
                    if (!IsHost && !_manager.isClient)
                        Fail("Disconnected from the host.");
                    break;
            }
        }

        /// <summary>Opens a new room and returns the code to read out.</summary>
        public void HostNew()
        {
            if (!CanBeginAttempt())
                return;

            Code = JoinCode.Generate();
            Queue(Attempt.HostRelay, "Opening a room...");
        }

        public void JoinWithCode(string raw)
        {
            if (!CanBeginAttempt())
                return;

            var code = JoinCode.Normalise(raw);

            if (!JoinCode.IsComplete(code))
            {
                Fail($"A code is {JoinCode.RequiredLength} characters.");
                return;
            }

            Code = code;
            Queue(Attempt.JoinRelay, $"Joining {JoinCode.ForDisplay(code)}...");
        }

        /// <summary>
        /// Hosts on the scene's own transport - localhost and LAN - with no relay and no code.
        ///
        /// This exists because the menu and the MPPM test flow were quietly incompatible. MPPM
        /// launches its virtual players with <c>-name PlayerN</c>, so they resolve to Client, skip
        /// the menu and auto-connect on the scene's UDPTransport; the main editor gets no such
        /// argument, so it is menu-driven, and hosting from the menu moved it onto the relay. The
        /// clones were then knocking on localhost while the host was on the internet, with nothing
        /// on either screen to say so. This button is the connection they are waiting for.
        ///
        /// Not editor-only in itself: two machines on one network can use it, and it is the only
        /// path that works with the relay unreachable.
        /// </summary>
        public void HostLocal()
        {
            if (!CanBeginAttempt())
                return;

            Code = string.Empty;
            Queue(Attempt.HostLocal, "Opening a local server...");
        }

        /// <summary>
        /// Joins a local host. No code: on the scene transport the address is already configured,
        /// which is the whole reason the MPPM flow needs no typing.
        /// </summary>
        public void JoinLocal()
        {
            if (!CanBeginAttempt())
                return;

            Code = string.Empty;
            Queue(Attempt.JoinLocal, "Connecting to a local host...");
        }

        private bool CanBeginAttempt() =>
            State == SessionState.Offline || State == SessionState.Failed;

        /// <summary>
        /// Records what was asked for and shows it as in progress. The work happens in
        /// <see cref="TryStartQueued"/> on the next frame that the manager is actually idle.
        /// </summary>
        private void Queue(Attempt attempt, string status)
        {
            _queued = attempt;
            _local = attempt == Attempt.HostLocal || attempt == Attempt.JoinLocal;
            IsHost = attempt == Attempt.HostRelay || attempt == Attempt.HostLocal;

            State = IsHost ? SessionState.Opening : SessionState.Joining;
            Status = status;
            _deadline = Time.time + connectTimeout;
        }

        /// <summary>
        /// Starts the queued attempt, once the manager will let us.
        ///
        /// This waiting is the whole point. The transport can only be assigned while both the
        /// server and the client read Disconnected, and PurrNet does not reach that the moment you
        /// stop it: <c>serverState</c> and <c>clientState</c> report Disconnecting for as long as
        /// its internal cleanup flags are set. So a second attempt made straight after a failed or
        /// cancelled one threw "Cannot change transport while it is being used" - which is not a
        /// connection problem at all, and reads to a player as the join being broken. Queueing also
        /// means the switch between the relay and the scene transport happens in exactly one place,
        /// at the one moment it is legal.
        /// </summary>
        private void TryStartQueued()
        {
            var attempt = _queued;

            bool host = attempt == Attempt.HostRelay || attempt == Attempt.HostLocal;
            bool relay = useRelay && (attempt == Attempt.HostRelay || attempt == Attempt.JoinRelay);

            var target = relay ? EnsureRelay() : _sceneTransport;

            if (target == null)
            {
                _queued = Attempt.None;
                Fail("No transport available. Add one to the NetworkManager.");
                return;
            }

            // Only a *change* of transport needs the manager idle - that is the assignment PurrNet
            // refuses mid-shutdown. Hosting twice in a row reuses the same transport object, so
            // that case no longer waits on anything, which is most of why leaving a room and
            // hosting again used to go nowhere.
            bool swapping = !ReferenceEquals(_manager.transport, target);

            // StartServer goes straight to the transport with no wait of its own (StartClient has a
            // coroutine that waits for cleanup, StartServer does not), so hosting still has to see
            // a settled manager before it begins.
            if (swapping || host)
            {
                var serverState = _manager.serverState;
                var clientState = _manager.clientState;

                if (serverState != ConnectionState.Disconnected ||
                    clientState != ConnectionState.Disconnected)
                {
                    // Named in the status rather than hidden, because "nothing happens when I press
                    // host" is otherwise indistinguishable from a dead button.
                    Status = $"Closing the previous session... ({serverState}/{clientState})";

                    if (Time.time > _deadline)
                    {
                        _queued = Attempt.None;
                        Debug.LogWarning("[Session] Gave up waiting for the previous session to " +
                                         $"close. serverState={serverState}, " +
                                         $"clientState={clientState}, swapping={swapping}.");
                        Fail("The previous session will not close. Restart to host again.");
                    }

                    return;
                }
            }

            _queued = Attempt.None;

            if (!PrepareTransport(relay, swapping))
                return;

            Debug.Log(relay
                ? $"[Session] {(host ? "Hosting" : "Joining")} room '{JoinCode.ToRoomName(Code)}'."
                : $"[Session] {(host ? "Hosting" : "Joining")} on the scene transport.");

            if (host)
                _manager.StartHost();
            else
                _manager.StartClient();
        }

        /// <summary>
        /// Closes the room or leaves it, and puts the menu back. Also what the CANCEL button on a
        /// connection attempt calls: abandoning an attempt and leaving a match wind back the same
        /// things, and the only difference - whether there is an error to show - is that this one
        /// clears the status instead of setting it.
        /// </summary>
        public void Leave()
        {
            if (State == SessionState.Offline)
                return;

            if (_manager.isServer)
                _manager.StopServer();

            if (_manager.isClient)
                _manager.StopClient();

            // The transport is deliberately left alone. Putting it back here is what used to make
            // the next attempt throw: the manager is mid-shutdown at this exact moment, which is
            // precisely when it refuses to be reassigned. TryStartQueued sets the one it needs.
            _queued = Attempt.None;

            // PlayerIDs are handed out per connection, so names left over from this session would
            // be worn by whoever inherits those ids in the next one. The modules forget their own
            // on despawn; this covers a drop where no despawn arrives.
            PlayerNames.Clear();

            Code = string.Empty;
            IsHost = false;
            _local = false;
            State = SessionState.Offline;
            Status = string.Empty;
        }

        /// <summary>
        /// Points the NetworkManager at the transport this attempt needs: the relay for a coded
        /// room, the scene's own for a local one.
        ///
        /// Only called from <see cref="TryStartQueued"/>, which has already established that the
        /// manager is idle - so the assignment is legal here and nowhere else. The PurrTransport is
        /// created on demand rather than required in the scene, so nothing has to be wired for this
        /// to work, but an existing one is reused: adding it by hand is how you pick a region.
        /// </summary>
        private bool PrepareTransport(bool relay, bool swapping)
        {
            var target = relay ? EnsureRelay() : _sceneTransport;

            if (target == null)
            {
                Fail("No transport available. Add one to the NetworkManager.");
                return false;
            }

            // Set every time, not only on a swap: hosting again produces a new code, and the room
            // name is what carries it.
            if (relay)
                _relay.roomName = JoinCode.ToRoomName(Code);

            // Skipped when it is already the right one. Assigning the same transport back onto the
            // manager only unsubscribes and resubscribes the same four handlers, and it is the one
            // operation here that can throw.
            return !swapping || Assign(target);
        }

        /// <summary>
        /// The relay transport, created on demand rather than required in the scene so nothing has
        /// to be wired for this to work. An existing one is reused, so adding it by hand is how you
        /// pick a region.
        /// </summary>
        private PurrTransport EnsureRelay()
        {
            if (_relay != null)
                return _relay;

            _relay = GetComponent<PurrTransport>();

            if (_relay == null)
            {
                _relay = gameObject.AddComponent<PurrTransport>();
                Debug.Log("[Session] Added a PurrTransport for relay play. Add one to the " +
                          "NetworkManager in the scene if you want to pick a region.");
            }

            return _relay;
        }

        /// <summary>
        /// Still wrapped, even though the caller checked. The guard reads two live connection
        /// states, and being wrong about them should surface as a message in the menu rather than
        /// an exception that leaves the attempt stuck on "connecting" forever.
        /// </summary>
        private bool Assign(GenericTransport target)
        {
            try
            {
                _manager.transport = target;
                return true;
            }
            catch (System.Exception e)
            {
                Fail($"Could not switch transport: {e.Message}");
                return false;
            }
        }

        private void Succeed(string status)
        {
            // The one moment worth a disk write: the name has been committed to a real session.
            PlayerNames.Persist();

            State = SessionState.Online;
            Status = status;
            Debug.Log($"[Session] {status}");
        }

        private void Fail(string reason)
        {
            // Wound back, but the transport is left where it is: see Leave. The retry reassigns it
            // once the manager is idle, which is the only moment that is allowed to.
            if (_manager.isServer)
                _manager.StopServer();

            if (_manager.isClient)
                _manager.StopClient();

            _queued = Attempt.None;

            State = SessionState.Failed;
            Status = reason;
            IsHost = false;
            Debug.LogWarning($"[Session] {reason}");
        }
    }
}
