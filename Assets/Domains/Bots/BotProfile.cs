using UnityEngine;

namespace Game.Bots
{
    /// <summary>
    /// One bot's personality and skill, as data.
    ///
    /// Split deliberately down the middle, because the two halves answer different questions and
    /// mixing them is how bot tuning turns into guesswork. **Personality** is what this opponent
    /// wants - how close it fights, whether it presses or circles, whether a hard hit makes it
    /// break off. **Difficulty** is how well it manages to do it - how fast and how accurately it
    /// tracks, how quickly it reacts, how often it commits.
    ///
    /// The distinction matters for a ladder: an early opponent and a late one can share a
    /// personality and differ only in difficulty, which is what makes a roster read as a cast of
    /// characters rather than a set of difficulty sliders. It also means "aggressive but bad" is
    /// expressible, and that is most of what makes a low-tier bot fun rather than merely slow.
    ///
    /// A ScriptableObject per CLAUDE.md's preference for data over branches - the same reason the
    /// rulesets are assets.
    /// </summary>
    [CreateAssetMenu(menuName = "Ghost/Bot Profile", fileName = "BotProfile")]
    public class BotProfile : ScriptableObject
    {
        [Tooltip("Shown wherever a player name would be.")]
        public string displayName = "Bot";

        // =====================================================================
        //  Personality - what it wants
        // =====================================================================

        [Header("Personality")]
        [Tooltip("0 keeps its distance and only fights when it has to; 1 closes relentlessly. " +
                 "Scales how hard it pushes toward its preferred range and how readily it accepts " +
                 "a trade.")]
        [Range(0f, 1f)] public float aggression = 0.5f;

        [Tooltip("0 ignores danger; 1 breaks off when its own percentage climbs and keeps the " +
                 "arena's geometry between itself and whoever is shooting. This is the dial that " +
                 "makes an 'avoidant' opponent, and it fights aggression rather than replacing it " +
                 "- a bot can be both, which reads as one that picks its moments.")]
        [Range(0f, 1f)] public float caution = 0.5f;

        [Tooltip("Metres it tries to hold from its target. Below the rocket's 40m lock range it " +
                 "plays for the heavy; well above it, it chips with the laser and stays out of the " +
                 "way.")]
        public float preferredRange = 25f;

        [Tooltip("0 flies straight at or away from the target; 1 orbits. Orbiting is what makes a " +
                 "bot hard to lead, since the rocket has to be aimed where it will be.")]
        [Range(0f, 1f)] public float strafeBias = 0.5f;

        // =====================================================================
        //  Difficulty - how well it manages it
        // =====================================================================

        [Header("Difficulty")]
        [Tooltip("Degrees of aim error it carries. This is the single most legible difficulty dial " +
                 "- the laser's own assist cone is 20 degrees, so error above that means it " +
                 "genuinely misses, and error near zero means it does not.")]
        [Range(0f, 25f)] public float aimErrorDegrees = 7f;

        [Tooltip("Degrees per second it can swing its aim. Low values let a fast player escape its " +
                 "tracking entirely, which is a more interesting weakness than simply missing.")]
        public float aimSpeedDegrees = 160f;

        [Tooltip("Seconds before it responds to something changing - a target appearing, a hit " +
                 "landing. Human-ish is 0.2-0.3; near zero feels like being read.")]
        [Range(0f, 1.5f)] public float reactionSeconds = 0.3f;

        [Tooltip("How willing it is to commit to a rocket windup, which brakes it and makes it a " +
                 "target. 0 never charges; 1 charges whenever the cooldown allows. The interesting " +
                 "values are in the middle, where it has to be worth it.")]
        [Range(0f, 1f)] public float rocketDiscipline = 0.5f;

        [Tooltip("How well it uses surfaces. Dashes are the only way to redirect momentum, so this " +
                 "is effectively its mobility skill: a bot with none drifts predictably.")]
        [Range(0f, 1f)] public float dashSkill = 0.5f;

        /// <summary>
        /// Aim error in degrees, scaled so a reacting bot is briefly worse than a settled one.
        /// Gives a dodge a real payoff rather than only moving where the shots land.
        /// </summary>
        public float AimErrorAt(float secondsOnTarget)
        {
            float settled = Mathf.Clamp01(secondsOnTarget / Mathf.Max(0.05f, reactionSeconds));
            return aimErrorDegrees * Mathf.Lerp(2.5f, 1f, settled);
        }
    }
}
