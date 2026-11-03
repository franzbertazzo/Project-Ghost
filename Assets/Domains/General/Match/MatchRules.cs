using UnityEngine;

namespace Game.Match
{
    public enum MatchMode
    {
        /// <summary>Respawn on a timer, first to the frag limit wins. The loose mode.</summary>
        FragLimit = 0,

        /// <summary>One life per round, last player standing takes it. The TowerFall mode.</summary>
        Elimination = 1
    }

    /// <summary>
    /// One asset per ruleset, per CLAUDE.md: "Build match logic so the ruleset is data, not a
    /// branch."
    ///
    /// Both launch modes share arenas, movement and weapons, and differ only in what death
    /// does and how the match ends - so they differ only in the values here, not in code paths.
    /// </summary>
    [CreateAssetMenu(fileName = "MatchRules", menuName = "Ghost/Match Rules")]
    public class MatchRules : ScriptableObject
    {
        [Header("Identity")]
        public string displayName = "Frag Limit";

        public MatchMode mode = MatchMode.FragLimit;

        [Header("Winning")]
        [Tooltip("Kills needed to win in Frag Limit. Rounds needed to win in Elimination.")]
        [Min(1)]
        public int scoreToWin = 10;

        [Header("Flow")]
        [Tooltip("Players needed before the match starts on its own. The lobby is a warmup: you " +
                 "can fly and shoot, but nothing scores and death puts you straight back. 0 means " +
                 "the host starts it by hand.")]
        [Min(0)]
        public int autoStartPlayers = 2;

        [Tooltip("Seconds of ROUND N banner before the round goes live. Everyone is already " +
                 "repositioned and healed when this begins, so it reads as a breath rather than " +
                 "a loading pause.")]
        [Min(0f)]
        public float roundIntroSeconds = 2f;

        [Tooltip("Seconds to sit on the round result before the next round starts. Long enough to " +
                 "see who took it and why.")]
        [Min(0f)]
        public float roundEndSeconds = 3f;

        [Header("Death")]
        [Tooltip("Seconds between dying and respawning. Ignored in Elimination, where you stay " +
                 "dead until the round ends.")]
        [Min(0f)]
        public float respawnDelay = 3f;

        [Tooltip("Subtract a point for killing yourself. Rocket-jumping into your own blast is a " +
                 "real risk, so it should cost something.")]
        public bool selfKillsCostAPoint = true;
    }
}
