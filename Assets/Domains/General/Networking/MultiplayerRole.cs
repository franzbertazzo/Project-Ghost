using System;
using System.Reflection;
using PurrNet.Utils;
using UnityEngine;

namespace Game.Networking
{
    public enum MultiplayerRole
    {
        /// <summary>Work it out from the environment. See <see cref="MultiplayerRoleResolver"/>.</summary>
        Auto = 0,

        /// <summary>Server + client in one process. The listen server.</summary>
        Host = 1,

        /// <summary>Client only, connects to a host.</summary>
        Client = 2,

        /// <summary>Server only, no local player. Dedicated-style, used for testing.</summary>
        Server = 3,

        /// <summary>Start nothing. Useful when a menu will start the connection later (P7).</summary>
        None = 4
    }

    /// <summary>
    /// Decides whether this process should host or join.
    ///
    /// PurrNet's own auto-start flags can't do this for us: its clone detection
    /// (<see cref="ApplicationContext.isClone"/>) looks for a symlinked Assets folder, which is how
    /// ParrelSync works but NOT how Multiplayer Play Mode works. Under MPPM every virtual player
    /// reports as a plain editor, so PurrNet's defaults would start a server AND a client in every
    /// instance — four separate hosts instead of one host and three clients.
    ///
    /// So the NetworkManager's auto-start flags are set to None and this resolver drives it instead.
    /// </summary>
    public static class MultiplayerRoleResolver
    {
        public const string HostTag = "host";
        public const string ClientTag = "client";
        public const string ServerTag = "server";

        /// <summary>
        /// Tag a virtual player "menu" to give it the Host/Join screen instead of auto-joining.
        ///
        /// Untagged MPPM instances auto-connect on localhost, which is the fast iteration path and
        /// involves no typing - but it also means the real join path, the one a player actually
        /// uses, cannot be exercised from the editor. This tag is how you test that: the instance
        /// starts nothing and waits for someone to paste a code into it.
        /// </summary>
        public const string MenuTag = "menu";

        /// <summary>
        /// Resolution order, first match wins:
        /// 1. An MPPM player tag literally named "host" / "client" / "server", if you made one.
        ///    Optional. Unity's own FAQ steers away from tags for role assignment, so this is only
        ///    here as a manual override.
        /// 2. MPPM's -name launch argument. Player1 is the main editor, Player2+ are virtual
        ///    players. This is the documented way to tell instances apart and is what normally
        ///    fires.
        /// 3. ParrelSync-style symlinked clone -> Client.
        /// 4. Anything else (main editor, standalone build) -> Host.
        /// </summary>
        public static MultiplayerRole Resolve(out string reason)
        {
            if (TryResolveExplicit(out var role, out reason))
                return role;

            reason = "main editor / standalone, no instance markers";
            return MultiplayerRole.Host;
        }

        /// <summary>
        /// The same resolution, minus the fallback: true only when something in the environment
        /// actually said what this process is.
        ///
        /// The distinction matters now that there is a menu. Auto-hosting is the right default for
        /// a test instance launched by MPPM, and the wrong one for someone double-clicking the
        /// game - they should get Host/Join, not a silent LAN server. This is how the bootstrap
        /// tells those two apart without parsing the reason string.
        /// </summary>
        public static bool TryResolveExplicit(out MultiplayerRole role, out string reason)
        {
            if (TryResolveTag(out role, out reason))
                return true;

            if (TryGetMppmPlayerName(out var playerName))
            {
                // Player1 is the main editor and hosts. Every other instance joins it.
                //
                // Whitespace is stripped before comparing: Unity's FAQ documents the value as
                // "Player1" but the Scenarios window shows instance names as "Player 2". Rather
                // than bet on which reaches the command line, accept both.
                var normalized = playerName.Replace(" ", string.Empty);
                bool isMainEditor = normalized.Equals("Player1", StringComparison.OrdinalIgnoreCase);
                reason = $"MPPM instance '{playerName}'";
                role = isMainEditor ? MultiplayerRole.Host : MultiplayerRole.Client;
                return true;
            }

            if (ApplicationContext.isClone)
            {
                reason = "symlinked editor clone (ParrelSync)";
                role = MultiplayerRole.Client;
                return true;
            }

            role = MultiplayerRole.Auto;
            reason = null;
            return false;
        }

        /// <summary>
        /// Only the MPPM player tags, which are the one marker a human sets on purpose.
        ///
        /// Split out from <see cref="TryResolveExplicit"/> because the two markers now mean
        /// different things. Every instance gets the Host/Join menu, so the `-name PlayerN`
        /// argument - which MPPM passes to every virtual player whether you asked for it or not -
        /// can no longer be taken as "this instance should auto-connect": that is what made clients
        /// unable to type a code. A tag is different. Someone typed it, so it still overrides.
        /// </summary>
        public static bool TryResolveTag(out MultiplayerRole role, out string reason)
        {
            if (TryGetMppmTagRole(out var taggedRole, out var tag))
            {
                reason = $"MPPM player tag '{tag}'";
                role = taggedRole;
                return true;
            }

            role = MultiplayerRole.Auto;
            reason = null;
            return false;
        }

        /// <summary>
        /// MPPM's API is reached by reflection on purpose: the package is editor-only and its type
        /// names have moved between major versions. Reflection keeps this compiling with the package
        /// absent, uninstalled, or renamed, and simply falls through to the next check.
        /// </summary>
        static bool TryGetMppmTagRole(out MultiplayerRole role, out string matchedTag)
        {
            role = MultiplayerRole.Auto;
            matchedTag = null;

            var tags = GetMppmTags();
            if (tags == null)
                return false;

            foreach (var raw in tags)
            {
                if (string.IsNullOrWhiteSpace(raw))
                    continue;

                var tag = raw.Trim().ToLowerInvariant();
                switch (tag)
                {
                    case HostTag:
                        role = MultiplayerRole.Host;
                        matchedTag = raw;
                        return true;
                    case ClientTag:
                        role = MultiplayerRole.Client;
                        matchedTag = raw;
                        return true;
                    case ServerTag:
                        role = MultiplayerRole.Server;
                        matchedTag = raw;
                        return true;
                    case MenuTag:
                        role = MultiplayerRole.None;
                        matchedTag = raw;
                        return true;
                }
            }

            return false;
        }

        static string[] GetMppmTags()
        {
#if !UNITY_EDITOR
            return null;
#else
            try
            {
                var type = FindMppmCurrentPlayerType();
                if (type == null)
                    return null;

                var method = type.GetMethod("ReadOnlyTags", BindingFlags.Public | BindingFlags.Static);
                if (method != null && method.GetParameters().Length == 0)
                    return method.Invoke(null, null) as string[];

                var property = type.GetProperty("Tags", BindingFlags.Public | BindingFlags.Static);
                return property?.GetValue(null) as string[];
            }
            catch (Exception)
            {
                // MPPM absent or its API moved. Not an error, just fall through.
                return null;
            }
#endif
        }

        static Type FindMppmCurrentPlayerType()
        {
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                var name = assembly.GetName().Name;
                if (name == null || !name.StartsWith("Unity.Multiplayer.Playmode", StringComparison.Ordinal))
                    continue;

                foreach (var type in assembly.GetTypes())
                {
                    if (type.Name == "CurrentPlayer" && type.IsClass)
                        return type;
                }
            }

            return null;
        }

        /// <summary>
        /// Reads MPPM's "-name &lt;PlayerN&gt;" launch argument. Per Unity's MPPM docs this is the
        /// supported way to identify which instance is running: Player1 is the main editor,
        /// Player2 through Player4 are virtual players.
        ///
        /// Returns false when the argument is absent, which is the normal case for an editor
        /// launched straight from the Hub with MPPM idle.
        /// </summary>
        static bool TryGetMppmPlayerName(out string playerName)
        {
            playerName = null;

            try
            {
                var args = Environment.GetCommandLineArgs();
                for (int i = 0; i < args.Length - 1; i++)
                {
                    if (!string.Equals(args[i], "-name", StringComparison.OrdinalIgnoreCase))
                        continue;

                    var value = args[i + 1];
                    if (string.IsNullOrWhiteSpace(value))
                        continue;

                    playerName = value.Trim();
                    return true;
                }
            }
            catch (Exception)
            {
                // Some platforms restrict command line access. Fall through.
            }

            return false;
        }

        public static string Describe(MultiplayerRole role)
        {
            return role switch
            {
                MultiplayerRole.Host => "HOST (server + client)",
                MultiplayerRole.Client => "CLIENT",
                MultiplayerRole.Server => "SERVER (no local player)",
                MultiplayerRole.None => "OFFLINE (nothing started)",
                _ => "AUTO"
            };
        }
    }
}
