using System;
using PurrNet;
using UnityEngine;

namespace Game.Networking
{
    /// <summary>
    /// Alive-or-dead, server-authoritative.
    ///
    /// Binary on purpose. CLAUDE.md puts this game at "death is instant" with rockets as TowerFall
    /// arrows - a hit kills. A hit-point pool would be a number nobody ever sees a middle value of,
    /// and it would quietly invite chip damage, regeneration and time-to-kill tuning that the
    /// design has already ruled out.
    ///
    /// Built as a NetworkModule rather than a MonoBehaviour because CLAUDE.md asks for reusable
    /// networked logic in modules, and weapon state wants exactly this shape: a small piece of
    /// server-owned state with an owner-readable mirror.
    ///
    /// This replaces PlayerHealth for versus. That class is single-player shaped - a 100HP pool
    /// with a regenerating shield whose Die() calls SetActive(false) - and none of those are
    /// behaviours a round-based versus match wants.
    /// </summary>
    [Serializable]
    public class VitalsModule : NetworkModule
    {
        /// <summary>
        /// Server-authoritative: only the host writes it, everyone reads it. A client claiming to
        /// be alive is exactly the lie CLAUDE.md says to assume.
        /// </summary>
        private SyncVar<bool> _alive = new(true, ownerAuth: false);

        /// <summary>
        /// Accumulated damage, Smash-style: it does not kill, it makes you easier to launch.
        /// Server-authoritative like everything else that decides a fight.
        /// </summary>
        private SyncVar<float> _damagePercent = new(0f, ownerAuth: false);

        /// <summary>
        /// Who last damaged this player, so a ring-out can be credited to whoever launched them
        /// rather than to nobody. This is the Smash model: the kill belongs to the last person who
        /// touched you, not to the boundary that finished the job.
        /// </summary>
        private SyncVar<PlayerID> _lastAttacker = new(default, ownerAuth: false);

        public bool IsAlive => _alive.value;

        public float DamagePercent => _damagePercent.value;

        /// <summary>
        /// Who last damaged this player, or null if nobody has.
        ///
        /// Read off the SyncVar rather than from a companion bool, using id 0 - the server, never a
        /// player - as "nobody". The bool was set in AddDamage, which is server-only, so on every
        /// other peer it stayed false and this property returned null however full the SyncVar was.
        /// The host could always name a killer and no client ever could, which is invisible until
        /// something tries to tell a victim who shot them.
        /// </summary>
        public PlayerID? LastAttacker =>
            _lastAttacker.value.id.value != 0 ? _lastAttacker.value : null;

        /// <summary>Raised on every peer when the percentage changes, for the HUD.</summary>
        public event Action<float> OnDamageChanged;

        /// <summary>
        /// Host only. Adds damage and records the attacker. Returns the new percentage.
        /// </summary>
        public float AddDamage(float amount, PlayerID? attacker)
        {
            if (!isServer || !_alive.value || amount <= 0f)
                return _damagePercent.value;

            if (attacker.HasValue && attacker.Value != parentOwner)
                _lastAttacker.value = attacker.Value;

            _damagePercent.value += amount;
            return _damagePercent.value;
        }

        private PlayerID? parentOwner => parent != null ? parent.owner : null;

        /// <summary>
        /// Raised on every peer when this player dies, carrying the killer if there was one.
        /// The match system listens to this for scoring; presentation listens for the explosion.
        /// </summary>
        public event Action<PlayerID?> OnDied;

        /// <summary>Raised on every peer when this player comes back. Frag mode only.</summary>
        public event Action OnRespawned;

        public override void OnSpawn()
        {
            base.OnSpawn();
            _alive.onChanged += HandleAliveChanged;
            _damagePercent.onChanged += HandleDamageChanged;
        }

        public override void OnDespawned()
        {
            base.OnDespawned();
            _alive.onChanged -= HandleAliveChanged;
            _damagePercent.onChanged -= HandleDamageChanged;
        }

        private void HandleDamageChanged(float percent)
        {
            OnDamageChanged?.Invoke(percent);
        }

        /// <summary>
        /// Host only. Returns true if this call is what killed them, so a rocket caught in a
        /// multi-kill blast does not score the same player twice.
        /// </summary>
        public bool Kill(PlayerID? killer)
        {
            if (!isServer)
            {
                Debug.LogWarning("[Vitals] Kill called on a client; ignored. Deaths are the host's to decide.");
                return false;
            }

            if (!_alive.value)
                return false;

            _alive.value = false;

            // The death event goes out as one RPC rather than as a second SyncVar for the killer.
            // Two SyncVars are not guaranteed to land together, so a client could see "dead" with
            // last tick's killer - and misattribute a kill in the scoreboard.
            BroadcastDeath(killer ?? default, killer.HasValue);
            return true;
        }

        /// <summary>Host only. Used by the frag-limit ruleset and at the start of each round.</summary>
        public void Revive()
        {
            if (!isServer || _alive.value)
                return;

            ResetForNewLife();
        }

        /// <summary>
        /// Host only. Gives this player a fresh life whether or not they were dead.
        ///
        /// Separate from Revive because a round reset has to touch the survivors too: in
        /// Elimination the player who just won the round is still alive, and starting the next one
        /// with their accumulated percentage would mean the round winner is the easiest player to
        /// launch in the round that follows. Revive's early-out on the living is correct for a
        /// respawn and wrong for a reset.
        /// </summary>
        public void ResetForNewLife()
        {
            if (!isServer)
                return;

            // Percentage resets with the life, as in Smash: you come back fresh and hard to
            // launch, and the pressure rebuilds from zero.
            _damagePercent.value = 0f;

            // Cleared to the "nobody" id, so a fresh life is not still credited to whoever ended
            // the last one.
            _lastAttacker.value = default;
            RevivedAt = Time.time;
            _alive.value = true;
        }

        [Tooltip("Seconds of immunity after coming back. Long enough that a player who happens to " +
                 "respawn near a fight is not killed before they have control, short enough that " +
                 "it cannot be used to cross the arena safely.")]
        public float invulnerableSeconds = 3f;

        /// <summary>
        /// True while a freshly respawned player cannot be hurt.
        ///
        /// Read on the host to reject hits, and on every peer to drive the blink. The timestamp
        /// behind it is set locally on each peer when the alive flag flips, rather than synced, so
        /// nothing extra goes on the wire: each peer counts its own three seconds from the moment
        /// it learned the player was back, which is exactly the window during which that peer
        /// would otherwise draw them as a solid target.
        /// </summary>
        public bool IsInvulnerable =>
            _alive.value && RevivedAt > 0f && Time.time - RevivedAt < invulnerableSeconds;

        public float InvulnerableRemaining =>
            RevivedAt > 0f ? Mathf.Max(0f, RevivedAt + invulnerableSeconds - Time.time) : 0f;

        /// <summary>
        /// When this player was last brought back, in local time. Zero until the first
        /// revive.
        ///
        /// Exists because a respawn is not instant across the wire. The teleport is a TargetRpc to
        /// the owner, who moves the body and streams the new position back, while the host flips
        /// this player to alive immediately. For most of a round trip the host therefore still sees
        /// them at the position they died in - which, after a ring-out, is outside the boundary.
        /// The BlastZone was killing them again before they had ever arrived.
        ///
        /// Not synced: only the host reads it, and only the host decides ring-outs.
        /// </summary>
        public float RevivedAt { get; private set; }

        /// <summary>
        /// Host only. Rejects a hit on someone who is still invulnerable.
        ///
        /// Checked here rather than in each weapon: the rocket, the splash and the laser all
        /// arrive through AddDamage, and a rule enforced in three places is a rule that will
        /// eventually be enforced in two.
        /// </summary>
        public bool CanBeHit => isServer && _alive.value && !IsInvulnerable;

        /// <summary>
        /// runLocally so the host reacts in the same call as everyone else rather than a tick
        /// earlier - otherwise the host sees its own explosion before the clients do.
        /// </summary>
        [ObserversRpc(runLocally: true)]
        private void BroadcastDeath(PlayerID killer, bool hasKiller)
        {
            OnDied?.Invoke(hasKiller ? killer : null);
        }

        /// <summary>
        /// Respawn rides the SyncVar rather than an RPC: it carries no extra data, and a late
        /// joiner reading `alive = true` needs no event at all.
        /// </summary>
        private void HandleAliveChanged(bool alive)
        {
            if (!alive)
                return;

            // Stamped on every peer, including the host, so the invulnerability window and its
            // blink start when this machine learned about the revive. Revive() also stamps it for
            // the host, in case a server-side write does not raise onChanged locally.
            RevivedAt = Time.time;

            OnRespawned?.Invoke();
        }
    }
}
