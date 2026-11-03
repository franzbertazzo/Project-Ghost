using PurrNet;
using UnityEngine;

namespace Game.Networking
{
    /// <summary>
    /// Who each player is, visually: a name and a colour, identical on every screen.
    ///
    /// One source for both, because identity has to survive being shown in four different places.
    /// The scoreboard, the percentage readout, the on-screen diamonds and the radar rings all name
    /// the same four players, and if any of them derives its own colour or label then "the orange
    /// one" stops meaning anything and the player has to re-read a number every time.
    ///
    /// Derived from the PlayerID, never from join order or a local list index. Join order differs
    /// per peer, so an index-based colour would make you orange on your screen and green on mine -
    /// which is worse than no colour at all, because it looks like it works.
    ///
    /// Red is deliberately absent. It belongs to incoming rockets, which is the one contact that
    /// must never be mistaken for anything else.
    /// </summary>
    public static class PlayerPalette
    {
        private static readonly Color[] Colors =
        {
            new(0.25f, 0.85f, 1f),    // cyan
            new(1f, 0.62f, 0.15f),    // orange
            new(0.55f, 1f, 0.35f),    // lime
            new(1f, 0.45f, 0.9f),     // magenta
            new(1f, 0.92f, 0.3f),     // yellow
            new(0.62f, 0.5f, 1f)      // violet
        };

        /// <summary>
        /// Stable per-player colour. Wraps past six players rather than running out, so a seventh
        /// peer shares a colour instead of rendering untinted.
        /// </summary>
        public static Color ColorOf(PlayerID player)
        {
            ulong raw = player.id.value;

            // Ids start at 1, so subtract to put the first player on the first colour. Guarded for
            // the server id (0), which is not a player but can arrive as a default value.
            ulong index = raw > 0 ? raw - 1 : 0;
            return Colors[(int)(index % (ulong)Colors.Length)];
        }

        public static Color ColorOf(PlayerID? player)
        {
            return player.HasValue ? ColorOf(player.Value) : Color.gray;
        }

        /// <summary>
        /// The player's chosen name, or "Player 2" if they never chose one.
        ///
        /// The raw id is what the netcode logs want; it is not what a player reads mid-fight. This
        /// being the only place that decides the wording is what made nicknames a two-line change
        /// rather than a pass over the whole HUD - the scoreboard, the percentage cells, the
        /// on-screen diamonds and the radar rings all ask here, so all four started showing names
        /// without being touched. A Steam persona later is another line in the same spot.
        /// </summary>
        public static string NameOf(PlayerID player)
        {
            if (player.id.value == 0)
                return "Server";

            return PlayerNames.TryGet(player, out var nickname)
                ? nickname
                : $"Player {player.id.value}";
        }

        public static string NameOf(PlayerID? player)
        {
            return player.HasValue ? NameOf(player.Value) : "Nobody";
        }

        /// <summary>Hex for the rich-text labels the debug HUD builds as strings.</summary>
        public static string HexOf(PlayerID player)
        {
            return ColorUtility.ToHtmlStringRGB(ColorOf(player));
        }
    }
}
