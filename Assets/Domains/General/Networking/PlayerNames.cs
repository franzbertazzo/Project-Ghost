using System.Collections.Generic;
using System.Text;
using PurrNet;
using UnityEngine;

namespace Game.Networking
{
    /// <summary>
    /// The name a player chose, and everyone else's.
    ///
    /// Two halves of one problem. <see cref="Local"/> is what this player typed into the menu, kept
    /// in PlayerPrefs so it survives a restart; the registry is every player's name as it arrives
    /// over the network, keyed by <see cref="PlayerID"/>.
    ///
    /// Keyed by PlayerID rather than held on the player object because <see cref="PlayerPalette"/>
    /// is called from four places that have an id and nothing else - the scoreboard, the percentage
    /// cells, the on-screen diamonds and the radar rings. Routing names through the same lookup the
    /// colours already use means every one of them picked up nicknames without being touched, and
    /// means there is still exactly one answer to "what is that player called".
    ///
    /// Not authoritative. Names arrive owner-authoritative (see <see cref="IdentityModule"/>) and a
    /// client could send anything, so nothing is decided by them - they are a label on a coloured
    /// square. <see cref="Sanitise"/> runs on the way in and on the way out, so a hostile name can
    /// be ugly but cannot be long enough or shaped badly enough to break a HUD.
    /// </summary>
    public static class PlayerNames
    {
        /// <summary>
        /// Short enough to fit under a diamond at arm's length across the arena, which is where
        /// names are read from. Longer names are cut rather than rejected.
        /// </summary>
        public const int MaxLength = 14;

        /// <summary>
        /// What the name field starts with before anyone types anything.
        ///
        /// A word rather than nothing, so the field reads as having a value to replace rather than
        /// as broken. Note that it is the same word for everybody: four players who all leave it
        /// alone are four players called "player", and telling them apart falls to the palette
        /// colours. The per-id fallback in <see cref="PlayerPalette.NameOf"/> only applies to a
        /// player who sent no name at all, which now means one running an older build.
        /// </summary>
        public const string Default = "player";

        private const string PrefsKey = "ghost.nickname";

        private static readonly Dictionary<PlayerID, string> Known = new();

        private static string _local;
        private static bool _loaded;

        /// <summary>
        /// This player's chosen name. Empty means "no choice made", and every readout falls back to
        /// Player N rather than showing a blank.
        /// </summary>
        public static string Local
        {
            get
            {
                if (!_loaded)
                {
                    _local = Sanitise(PlayerPrefs.GetString(PrefsKey, string.Empty));
                    _loaded = true;

                    if (string.IsNullOrEmpty(_local))
                        _local = Default;
                }

                return _local;
            }

            set
            {
                _local = Sanitise(value);
                _loaded = true;

                // SetString only. This is written on every keystroke as the field is typed into,
                // and PlayerPrefs.Save() is a disk write - see Persist() for when that happens.
                PlayerPrefs.SetString(PrefsKey, _local);
            }
        }

        /// <summary>
        /// Flushes the name to disk. Called once when a session actually starts, rather than per
        /// keystroke: Unity writes PlayerPrefs on quit anyway, so this only covers the case where
        /// the game dies mid-match and the name would otherwise be lost.
        /// </summary>
        public static void Persist() => PlayerPrefs.Save();

        /// <summary>
        /// Trims, collapses whitespace, drops control characters and clamps the length.
        ///
        /// Run on names received as well as sent: the sending client is the one that chose the
        /// name, so it is also the one that could choose a 4KB name made of newlines.
        /// </summary>
        public static string Sanitise(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return string.Empty;

            var built = new StringBuilder(MaxLength);
            bool lastWasSpace = false;

            foreach (char c in raw.Trim())
            {
                if (built.Length >= MaxLength)
                    break;

                if (char.IsControl(c))
                    continue;

                if (char.IsWhiteSpace(c))
                {
                    // One space, never a run, and never leading - a name padded with spaces would
                    // centre wrong in every cell it appears in.
                    if (lastWasSpace || built.Length == 0)
                        continue;

                    built.Append(' ');
                    lastWasSpace = true;
                    continue;
                }

                built.Append(c);
                lastWasSpace = false;
            }

            return built.ToString().TrimEnd();
        }

        /// <summary>Called on every peer as names replicate. Empty clears, rather than storing one.</summary>
        public static void Register(PlayerID player, string name)
        {
            var clean = Sanitise(name);

            if (string.IsNullOrEmpty(clean))
            {
                Known.Remove(player);
                return;
            }

            Known[player] = clean;
        }

        public static void Forget(PlayerID player) => Known.Remove(player);

        public static bool TryGet(PlayerID player, out string name) =>
            Known.TryGetValue(player, out name);

        /// <summary>
        /// Cleared between sessions: ids are handed out per connection, so a name left behind from
        /// the last match would be attached to whoever inherits that id in the next one.
        /// </summary>
        public static void Clear() => Known.Clear();
    }
}
