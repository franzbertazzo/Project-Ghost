using System.Text;
using UnityEngine;

namespace Game.Networking
{
    /// <summary>
    /// The short code a host reads out and a friend types in.
    ///
    /// It is not an encoding of an address. PurrNet's relay addresses rooms by name, so the code
    /// IS the room name - which is why there is no NAT traversal, port forwarding or IP sharing
    /// anywhere in the join path, and why Steam can later swap the transport underneath without
    /// changing any of this.
    ///
    /// The alphabet is chosen for being read aloud over voice chat, which is how these codes
    /// actually travel. No 0/O, no 1/I/L, no 5/S, no 2/Z, no 8/B - every pair that gets misheard
    /// or mistyped is gone, at the cost of a slightly longer code for the same number of rooms.
    /// 25 characters over 5 places is ~9.7 million combinations, far more than the number of rooms
    /// that will ever be open at once.
    /// </summary>
    public static class JoinCode
    {
        private const string Alphabet = "ACDEFGHJKMNPQRTUVWXY34679";
        private const int Length = 5;

        /// <summary>
        /// Prefixed on the wire so this game's rooms cannot collide with another PurrNet project
        /// using the same public relay. Players never see it.
        /// </summary>
        private const string RoomPrefix = "ghost-";

        public static string Generate()
        {
            var code = new StringBuilder(Length);

            for (int i = 0; i < Length; i++)
                code.Append(Alphabet[Random.Range(0, Alphabet.Length)]);

            return code.ToString();
        }

        /// <summary>
        /// Cleans up what someone actually typed: lowercase, spaces and dashes go, and the two
        /// genuinely ambiguous shapes are mapped to the character they must have been.
        ///
        /// Only the pairs where exactly one side is a legal code character are mapped, because
        /// those are the ones where the intent is unambiguous: a code can never contain O or 0, so
        /// an O that was typed can only have been the round letter the alphabet does use. Guessing
        /// at pairs where neither side is legal would just substitute one wrong character for
        /// another, so anything else is dropped.
        /// </summary>
        public static string Normalise(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return string.Empty;

            var cleaned = new StringBuilder(Length);

            foreach (char c in raw.Trim().ToUpperInvariant())
            {
                char mapped = c switch
                {
                    'O' => 'Q',
                    '0' => 'Q',
                    'I' => 'J',
                    'L' => 'J',
                    '1' => 'J',
                    _ => c
                };

                if (Alphabet.IndexOf(mapped) >= 0)
                    cleaned.Append(mapped);

                if (cleaned.Length == Length)
                    break;
            }

            return cleaned.ToString();
        }

        public static bool IsComplete(string code)
        {
            return !string.IsNullOrEmpty(code) && code.Length == Length;
        }

        public static int RequiredLength => Length;

        public static string ToRoomName(string code) => RoomPrefix + Normalise(code);

        /// <summary>Grouped for reading aloud: ABCDE shows as ABC DE.</summary>
        public static string ForDisplay(string code)
        {
            if (string.IsNullOrEmpty(code) || code.Length < 4)
                return code;

            return code.Substring(0, 3) + " " + code.Substring(3);
        }
    }
}
