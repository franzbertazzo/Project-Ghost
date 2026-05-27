# Project Context

## The Game

A **4-player co-op zero-g third-person shooter** with 6 degrees of freedom movement. Pulpy action tone — think *Metal Slug meets Unreal Tournament in space*, with *Witchfire*-inspired roguelike run structure.

**Core loop:** Dash through zero-g environments by pushing off surfaces, scavenge weapons mid-level, clear randomized event nodes, fight memorable boss battles, unlock new gear for future runs.

## Core Mechanics

- **Movement:** 6DoF **free-flight** with momentum conservation — players drift continuously through zero-g and can rotate on all axes at any time. **Dashes** are impulse moments layered on top: kicking off nearby surfaces (unlimited) or mid-air dashes (limited by stamina/fuel) inject or redirect momentum. Pre-made directional dash animations exist for forward, back, left, right, up, down. Weapon recoil also pushes the player within this system — weapon choice is a movement decision. Reading and managing your own momentum is a core skill.
- **Combat:** Weapons are picked up in-level (Metal Slug style) — pistol is infinite baseline, heavier weapons are temporary. Recoil physically pushes the player, making weapon choice a movement decision.
- **Characters:** 4 playable at launch, more unlockable. Shared movement/animations. Differentiated by stats + 1 unique ability each.

## Game Modes (launch scope)

- **Campaign (co-op, 1–4 players)** — primary mode. 8 hand-crafted levels, each with a memorable boss fight and the roguelike run system below.
- **Horde / Survival (co-op, 1–4 players)** — wave-based endless survival on dedicated arenas. Reuses campaign enemies and assets.

Unlocks (weapons, characters) **carry across both modes** — single unified progression track.

*Versus PvP (1v1, 2v2) is a post-launch goal, not launch scope. PurrNet supports the tighter authority models needed for PvP via its Network Rules system, so the door stays open.*

## Roguelike Run System (Campaign)

Each campaign level is a **large hand-crafted map** — persistent geometry, not procedural. Replayability comes from three randomization layers per run:

1. **Random player spawn point.** All players spawn together at one location, but the location is randomly chosen from authored spawn slots each run.
2. **Random event nodes.** Each level has a pool of authored event node slots; a subset is selected per run. Events include wave defense, miniboss, escort/protect, and alternative objectives (TBD pool).
3. **Sequential path ordering.** The selected event nodes form an ordered path. The existing **radar ring system** points players to the next node. Reaching a node physically triggers its event.

**Flow:** spawn → follow radar to next node → clear event → radar updates to next node → repeat until all events cleared → boss node reveals → boss fight → level complete → unlock rewards.

Clearing a level unlocks new weapons (added to the level spawn pool) and new playable characters.

## Perk System

*Design later.* Witchfire-inspired run-scoped buffs offered as a choice after each cleared event node. Will reward precision play (headshots, enemy weak points). Not a launch blocker for the roguelike structure itself.

## Weapon System

**Fire scheme:**

- **Tap/Hold weapons:** single fire button. Quick press = primary fire. Hold-to-charge + release = secondary fire.
- **Sustained-fire weapons:** designed around held trigger. Either primary-only, or extended hold triggers a variant behavior (overcharge, precision mode, etc.) rather than a distinct secondary.

**Planned arsenal (behavior details TBD, design as each is implemented):**

- Tap/Hold: Pistol (baseline, infinite), Flak Cannon, Shock Rifle, Rocket Launcher, Rail Gun, Redeemer
- Sustained-fire: Minigun, Plasma Auto-Rifle, Heavy Machine Gun

Each weapon's exact primary/secondary behavior is designed per-weapon during implementation — do not pre-spec.

## Enemies

4 archetypes, reskinned across levels rather than replaced:

- **Grunt** — numerous, fast to kill, sets combat tempo
- **Anchor** — stationary/heavy, forces repositioning in 3D
- **Hunter** — mobile, mirrors player dash mechanics
- **Exploder** — kamikaze, punishes standing still

Each archetype has a headshot weak point plus an archetype-specific vulnerability zone (for future perk system).

## Tech Stack

- **Engine:** Unity (version TBD — lock in and update this line)
- **Networking:** **PurrNet** — chosen for clean Unity-native workflow (Instantiate/Destroy just work, no baking), per-component ownership, awaitable/generic/static RPCs, and a Network Rules system that lets authority model change without rewriting code. Awaitable RPCs are particularly useful for the event node system (trigger event, wait for all clients to confirm).
- **Platform:** PC (Steam)
- **Multiplayer:** 4-player listen-server for co-op

## Networking Rules

- **Authority model:** host-authoritative for enemies, pickups, damage, event nodes, boss state (use PurrNet's server-strict Network Rules or equivalent). Client-authoritative for own movement is acceptable — this is PvE co-op.
- **Network:** positions, health, weapon states, enemy AI state, pickups, damage events, event node triggers, radar target updates.
- **Do NOT network:** muzzle flashes, sound effects, particle VFX — fire these locally from networked events.
- **Enemy AI runs on the host only.** Clients render, host decides.
- **Leverage PurrNet specifics:** use awaitable RPCs for event node handshakes (all players confirm arrival → trigger event). Use Network Modules for shared component logic across weapons/enemies. Use the cookie system for player reconnect during a run.
- **Test with 100–150ms artificial latency from day one.**

## Design Constraints

- **Animation budget is tight.** Solo/small team. Prefer mechanics that reuse the existing directional dash anims. Do not propose features requiring new locomotion rigs, procedural animation, or per-character unique movement.
- **Narrative delivery:** environmental storytelling, radio chatter, between-level still-art panels (Metal Slug style). No in-engine cutscenes.

## Code Style

*(Fill in as conventions are established.)*

- Namespacing for gameplay scripts (e.g. `Game.Player`, `Game.Weapons`, `Game.Networking`)
- Prefer `ScriptableObject` for weapon/character/enemy/event data
- PurrNet `NetworkIdentity` / `NetworkBehaviour` classes live in a `Networking/` folder; avoid mixing network and pure gameplay logic in the same class
- Prefer PurrNet Network Modules for reusable networked logic (health, ammo, status effects) rather than duplicating sync code across components

## Current Focus

**Multiplayer foundation.** Priority order:

1. Sync two players moving in the same scene.
2. Sync the dash-off-surface mechanic (test with lag).
3. Sync weapon firing + server-authoritative damage.
4. Sync enemy AI (host-only logic).
5. Sync pickups and weapon swapping.
6. Sync event node triggers and radar targeting for one prototype level.
7. Lobbies, matchmaking, Steam integration come later.

## Key Decisions Locked

- Netcode: **PurrNet** (over Fish-Net, Mirror, NGO, Photon Fusion) — chosen for Unity-native workflow and awaitable RPCs that fit the event node system.
- Movement: **6DoF free-flight with dash impulses** (not dash-only, not free-flight-only).
- Tone: **Pulpy action** (Metal Slug / Borderlands), not serious sci-fi horror.
- Campaign structure: **Hand-crafted levels + roguelike event randomization** (Witchfire model).
- Launch modes: **Campaign + Horde only.** Versus is post-launch.
- Progression: **Unified** — unlocks work across all modes.

## Open Questions

- Unity version.
- 4 character archetypes and their unique abilities.
- Alternative objective types for event node pool (beyond wave / miniboss / escort).
- Perk system design (deferred).
- Per-weapon primary/secondary behaviors (designed during implementation).
