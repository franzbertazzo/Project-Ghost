using System;
using System.Reflection;
using PurrNet;
using UnityEngine;

namespace Game.Networking
{
    /// <summary>
    /// Artificial latency and packet loss, on by default, per CLAUDE.md: "Test with 100-150ms
    /// artificial latency from day one."
    ///
    /// PurrNet's UDPTransport runs on LiteNetLib, whose NetManager has built-in simulation fields.
    /// They're private inside the transport, so this reaches them by reflection rather than by
    /// forking PurrNet — which would be lost on the next package update.
    ///
    /// Two things to know:
    ///
    /// 1. LiteNetLib guards the simulation with #if DEBUG. Unity defines DEBUG in the editor and in
    ///    development builds, so this works while testing and silently does nothing in a release
    ///    build. That is the behaviour we want, but it means you cannot use this to lag a shipped
    ///    build for a demo.
    ///
    /// 2. Delay is applied by the RECEIVING NetManager, and it is per-manager, not per-peer. So a
    ///    host that simulates lag also lags against its own local client, which no real host does.
    ///    Hence <see cref="applyOn"/> defaults to clients only: the host stays honest, remote
    ///    clients feel a delayed inbound stream. Switch to Everyone when you want to feel both
    ///    directions and can accept the host lagging against itself.
    /// </summary>
    [RequireComponent(typeof(NetworkManager))]
    public class NetworkLatencySimulator : MonoBehaviour
    {
        public enum ApplyTo
        {
            /// <summary>Only client-role processes lag. The host is clean, like a real listen server.</summary>
            ClientsOnly = 0,

            /// <summary>Every process lags, including the host against its own local client.</summary>
            Everyone = 1
        }

        [Header("Simulation")]
        [Tooltip("Master switch, off by default. Toggle at runtime with the hotkey below when you " +
                 "want to see how the game holds up on a bad connection.")]
        // Renamed from `simulate` on purpose, and not back: the old name is still serialized as
        // `simulate: 1` in scenes saved before this default changed, and Unity binds by name - so
        // keeping it would have meant the scene's `true` quietly overriding this `false` in exactly
        // the way that has caught this project out repeatedly. A new name has no stale value to
        // inherit, so the default here is the one that actually applies.
        [SerializeField] private bool simulateLatency;

        [SerializeField] private ApplyTo applyOn = ApplyTo.ClientsOnly;

        [Tooltip("One-way delay floor in ms. Round-trip is roughly double this.")]
        [Range(0, 500)]
        [SerializeField] private int minLatencyMs = 60;

        [Tooltip("One-way delay ceiling in ms. Round-trip is roughly double this.")]
        [Range(0, 500)]
        [SerializeField] private int maxLatencyMs = 80;

        [Tooltip("Percent of packets dropped. A few percent is realistic; 0 isolates latency alone.")]
        [Range(0, 50)]
        [SerializeField] private int packetLossPercent = 2;

        [Header("Runtime")]
        [Tooltip("F8, not F9: MPPM claims Ctrl+F9 through Ctrl+F12 for switching instance focus.")]
        [SerializeField] private KeyCode toggleKey = KeyCode.F8;

        private NetworkManager _manager;
        private MultiplayerBootstrap _bootstrap;
        private bool _applied;
        private bool _warnedNoTransport;

        /// <summary>True when simulation is on AND this process is one that should apply it.</summary>
        public bool IsSimulating => simulateLatency && ShouldApplyHere();

        public string Summary
        {
            get
            {
                if (!simulateLatency)
                    return "off";

                // Distinguish "switched off" from "on, but this instance is deliberately excluded".
                // Without this the host reports plain "off" and reads like a broken toggle.
                if (!ShouldApplyHere())
                    return "on, clients only (this instance is the host)";

                return $"{minLatencyMs}-{maxLatencyMs}ms one-way " +
                       $"(~{minLatencyMs * 2}-{maxLatencyMs * 2}ms RTT), {packetLossPercent}% loss";
            }
        }

        private void Awake()
        {
            _manager = GetComponent<NetworkManager>();
            _bootstrap = GetComponent<MultiplayerBootstrap>();
        }

        private void Start()
        {
            Apply();
        }

        private void Update()
        {
            // Ignore the chord form so this never fights a modifier-based editor shortcut.
            bool modifierHeld = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl) ||
                                Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt);

            if (toggleKey != KeyCode.None && !modifierHeld && Input.GetKeyDown(toggleKey))
            {
                simulateLatency = !simulateLatency;
                Apply();
                Debug.Log($"[Latency] Simulation {(simulateLatency ? "ON" : "OFF")} - {Summary}");
            }

            // The transport recreates its NetManagers on enable, and a client may connect later than
            // this component starts, so keep the settings pinned rather than applying once.
            if (!_applied)
                Apply();
        }

        private void OnValidate()
        {
            if (minLatencyMs > maxLatencyMs)
                maxLatencyMs = minLatencyMs;

            if (Application.isPlaying && _manager != null)
                Apply();
        }

        private void Apply()
        {
            var transport = _manager != null ? _manager.transport : null;
            if (transport == null)
            {
                if (!_warnedNoTransport)
                {
                    Debug.LogWarning("[Latency] No transport on the NetworkManager, simulation inactive.");
                    _warnedNoTransport = true;
                }

                return;
            }

            bool active = IsSimulating;
            bool touchedAny = false;

            touchedAny |= ApplyToManagerField(transport, "_client", active);
            touchedAny |= ApplyToManagerField(transport, "_server", active);

            if (!touchedAny)
                return;

            _applied = true;

            if (active)
                Debug.Log($"[Latency] Simulating {Summary} on {transport.GetType().Name}.");
        }

        /// <summary>
        /// Sets the LiteNetLib simulation fields on one of UDPTransport's private NetManagers.
        /// Returns false if the field or the manager isn't there yet, which is normal early on and
        /// normal for transports that aren't LiteNetLib-based.
        /// </summary>
        private bool ApplyToManagerField(object transport, string fieldName, bool active)
        {
            try
            {
                var field = transport.GetType().GetField(fieldName,
                    BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);

                var netManager = field?.GetValue(transport);
                if (netManager == null)
                    return false;

                var type = netManager.GetType();
                if (type.Name != "NetManager")
                    return false;

                SetField(netManager, type, "SimulateLatency", active);
                SetField(netManager, type, "SimulationMinLatency", Mathf.Max(0, minLatencyMs));
                SetField(netManager, type, "SimulationMaxLatency", Mathf.Max(1, maxLatencyMs));
                SetField(netManager, type, "SimulatePacketLoss", active && packetLossPercent > 0);
                SetField(netManager, type, "SimulationPacketLossChance", Mathf.Clamp(packetLossPercent, 0, 100));
                return true;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Latency] Could not apply simulation to '{fieldName}': {e.Message}");
                return false;
            }
        }

        private static void SetField(object target, Type type, string name, object value)
        {
            type.GetField(name, BindingFlags.Instance | BindingFlags.Public)?.SetValue(target, value);
        }

        private bool ShouldApplyHere()
        {
            if (applyOn == ApplyTo.Everyone)
                return true;

            // Clients only. Before the bootstrap resolves, assume client so we never miss the window
            // where a client is connecting.
            var role = _bootstrap != null ? _bootstrap.ActiveRole : MultiplayerRole.Client;
            return role == MultiplayerRole.Client;
        }
    }
}
