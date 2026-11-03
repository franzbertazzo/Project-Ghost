using System;
using PurrNet;

namespace Game.Networking
{
    /// <summary>
    /// Carries a player's chosen name to every other peer.
    ///
    /// A NetworkModule rather than its own NetworkBehaviour for the reason CLAUDE.md gives - small
    /// reusable networked state belongs in a module - and because it rides on a component the player
    /// prefab already has, so adding names needed no change to the prefab at all.
    ///
    /// Owner-authoritative, which is the one place in this game that is the right answer. Everything
    /// that decides a fight is the host's call, but a display name decides nothing: the worst a
    /// lying client can do is call itself something rude, and routing names through the host would
    /// mean an RPC and a validation path to protect a label. <see cref="PlayerNames.Sanitise"/>
    /// still runs on arrival, so a name cannot be long or malformed enough to break a readout.
    /// </summary>
    [Serializable]
    public class IdentityModule : NetworkModule
    {
        private SyncVar<string> _nickname = new(string.Empty, ownerAuth: true);

        /// <summary>The replicated name, or empty if this player never chose one.</summary>
        public string Nickname => _nickname.value;

        /// <summary>Raised on every peer when the name arrives or changes.</summary>
        public event Action<string> OnNicknameChanged;

        public override void OnSpawn()
        {
            base.OnSpawn();

            _nickname.onChanged += HandleChanged;

            PublishLocalName();

            // Published immediately as well as on change: a peer joining later receives the current
            // value as initial state rather than as a change, so onChanged never fires for it.
            HandleChanged(_nickname.value);
        }

        /// <summary>
        /// Also from here, because spawn and ownership are two events with no guaranteed order.
        ///
        /// The SyncVar is owner-authoritative, so a write from a peer that is not yet the owner goes
        /// nowhere - and if ownership arrives after OnSpawn, writing only there would mean the name
        /// was never sent at all and every other screen kept showing "Player 2". Both paths write;
        /// whichever happens second is the one that lands, and writing the same value twice costs
        /// nothing because a SyncVar only sends on change.
        /// </summary>
        public override void OnOwnerChanged(PlayerID? oldOwner, PlayerID? newOwner, bool asServer)
        {
            base.OnOwnerChanged(oldOwner, newOwner, asServer);

            PublishLocalName();

            // The registry is keyed by owner, so an id that moved has to be re-keyed: the old entry
            // would otherwise name a player who no longer exists.
            if (oldOwner.HasValue && oldOwner != newOwner)
                PlayerNames.Forget(oldOwner.Value);

            HandleChanged(_nickname.value);
        }

        private void PublishLocalName()
        {
            // The owner is the only peer that knows what this player typed.
            if (isOwner)
                _nickname.value = PlayerNames.Local;
        }

        public override void OnDespawned()
        {
            base.OnDespawned();

            _nickname.onChanged -= HandleChanged;

            // Dropped from the registry on leave, because PlayerIDs are reused: a name left behind
            // would be worn by whoever inherits the id next.
            if (parent != null && parent.owner.HasValue)
                PlayerNames.Forget(parent.owner.Value);
        }

        private void HandleChanged(string value)
        {
            if (parent == null || !parent.owner.HasValue)
                return;

            PlayerNames.Register(parent.owner.Value, value);
            OnNicknameChanged?.Invoke(value);
        }
    }
}
