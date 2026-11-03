# Project Context

## The Game

A **zero-g 3D arena versus shooter** — *TowerFall* in six degrees of freedom, with rockets instead of arrows.

4 players (6 if the arena and netcode hold up). One player hosts an instance, invites friends, everyone drops into a small enclosed zero-g arena and fights short, lethal rounds. Pulpy action tone — think *Metal Slug meets Unreal Tournament in space*.

**Core loop:** host or join an instance → spawn into an arena → hunt each other in 6DoF → land a rocket, get a kill → last player standing takes the round → next round starts immediately → first to N rounds wins → rematch or swap arena.

Rounds are short (tens of seconds), death is instant, and the fun comes from reading momentum in a confined space while dodging slow, telegraphed projectiles.

## Core Mechanics

- **Movement:** 6DoF **free-flight** with momentum conservation — players drift continuously and can rotate on all axes at any time. **Dashes are surface-only and cost a charge.** You kick off a nearby surface, paid for from a small recharging pool; there is no mid-air dash, so open space has nothing in it but the momentum you already carry, and the charges are a budget for how often you can redirect. Pre-made directional dash animations exist for forward, back, left, right, up, down. Reading and managing your own momentum is the core skill.
- **Controls:** mouse to look, WASD to thrust, Space/Ctrl for up/down, Q/E to roll, **Shift to dash off a surface**, **middle mouse for radar** (the old brake — it slows you and reveals enemies, which is what it is for), **left mouse for the laser** (hold for continuous fire), **right mouse for the rocket** (hold to charge). One button, one meaning: the heavy used to share the fire button through a tap/hold split, which made every sustained burst eventually commit you to a windup you had not asked for.
- **Aim assist on the laser, inside the reticle circle.** The ring draws the assist cone, so the promise it makes — an enemy inside it is a shot that lands — is literally true. The light attack is the one you fire constantly while both of you tumble through six axes; without assist it asked for precision nobody can deliver, and the rocket's own lock made the heavy *easier* to land than the light. Only the direction is bent, only onto a target already in line of sight, and only within `assistRange` — well short of the bolt's own range, so it forgives close-quarters tumbling rather than removing aiming.
- **Combat:** Rockets are the TowerFall arrow. Slow enough to dodge, lethal on hit, **limited in number**.
- **No ammo. The heavy attack is paid for with commitment.** There is no carry count, no reserve and no pickups. Charging the heavy shot **forces brake mode** - you keep control but move at roughly 40% speed while visibly winding up - and firing starts a **cooldown** before another can be charged. The charge is **all-or-nothing**: it fires only at full charge, and releasing early or **dashing** discards the whole windup, so you cannot both dodge and keep the shot. The cost is the mobility you give up and the rhythm you must play around, not an inventory.
- **Recoil is movement.** Firing pushes you. Rockets are a mobility tool as much as a weapon — rocket-jumping through the arena is intended, not an exploit.
- **Splash damage.** Rockets kill on direct hit and on close proximity. Splash also shoves players, so a near miss is still a play.
- **Characters:** shared movement, shared animations. Differentiation via stats + one small ability, only if it doesn't cost animation budget. Cosmetic-first is acceptable for launch.

## Scope

**Launch scope is Versus, in two rulesets.** Free-for-all, 4 players, 6 if performance and arena readability hold up.

- **Elimination** *(the TowerFall mode)* — one life per round, no respawn. Last player standing takes the round; first to N round wins takes the match. Short, tense, lethal.
- **Frag limit** *(the loose mode)* — respawn on a short timer, first to N kills wins. Continuous, more forgiving, better for uneven skill and for playtesting.

Both modes share the same arenas, movement, and weapons — they differ only in what death does and how the match ends. Build match logic so the ruleset is **data, not a branch**: one `ScriptableObject` per mode.

- A handful of small hand-built arenas — enclosed volumes with cover, surfaces to kick off, and no dead space.
- **Host-and-invite instances.** One player hosts a listen server, friends join. Core-path, not a later nicety.

Everything below is explicitly **out of launch scope**: co-op campaign, roguelike event nodes, horde/survival, boss fights, enemy AI archetypes, perk system, large arsenal, meta progression. Prototype assets for those exist in the repo (enemies, a miniboss, level geometry) — leave them alone, don't build on them, and don't propose features that depend on them.

*Team modes (2v2, 3v3) and co-op content are post-launch, once the versus core is solid.*

## Tech Stack

- **Engine:** Unity 6000.3.10f1 (URP)
- **Networking:** **PurrNet** — Unity-native workflow (Instantiate/Destroy just work, no baking), per-component ownership, awaitable/generic/static RPCs, and a Network Rules system that lets the authority model tighten for PvP without rewriting gameplay code.
- **Platform:** PC (Steam)
- **Topology:** listen server, host is one of the players. 4–6 peers.
- **Joining: share-a-join-code first, Steam invites later.** Host creates an instance and gets a short code; friends paste it to join. Steam friend invites and lobbies layer onto the *same* join path afterwards — do not build the code flow in a way Steam would have to replace.

## Networking Rules

PvP raises the bar over co-op. Assume every client lies.

- **Authority model: server-authoritative** for damage percentage, knockback, ring-outs, projectile spawns, the heavy-attack cooldown, round state, respawns, and scoring. The host decides how far you fly and who died.
- **Hit detection is favour-the-shooter, validated.** The one deliberate exception, and it is not optional: a hitscan shot is resolved on the *shooter*, against the world that shooter can see, and the client names the player it hit. The host then decides whether that claim was *possible* — the target is alive and not the shooter, the muzzle was near the shooter's body, the claimed impact is within `claimTolerance` of where the host currently has that player, and no static geometry stands between the two. Re-running the raycast on the host instead is what we had, and it was unplayable for everyone but the host: a client aiming dead-on missed by however far the target had moved during one trip to the host plus the interpolation delay every remote body is drawn behind by, which at 30 m/s is metres. "Assume every client lies" still holds — the geometry check is as strong as a host-side raycast, because walls are the part of the world every peer agrees on. What a client can get away with is a few metres of staleness, which is exactly what it was being punished for before.
- **Movement:** client-predicted with server reconciliation. A client may drive its own transform, but the host validates — no teleports, no impossible speeds. Start permissive, tighten once the core is playable.
- **Projectiles:** rockets are simulated on the host and predicted locally on the firing client. Hits resolve on the host. Every client must see the same rocket in roughly the same place — a rocket that exists on one screen and not another is a bug, not a tolerance.
- **Network:** transforms, velocities, alive state, damage percentage, last attacker, projectile spawns and detonations, heavy cooldown state, round/score state, respawn timers, player join/leave.
- **Do NOT network:** muzzle flashes, sound effects, particle VFX, camera shake, hit-stop — fire these locally from networked events.
- **Leverage PurrNet specifics:** awaitable RPCs for round-start and round-end handshakes, Network Modules for reusable networked logic (vitals, weapon state), and the cookie system for reconnect mid-match.
- **Test with 100–150ms artificial latency from day one**, and test with the host on a bad connection too.

## Design Constraints

- **Animation budget is tight.** Solo/small team. Prefer mechanics that reuse the existing directional dash anims. Do not propose features requiring new locomotion rigs, procedural animation, or per-character unique movement.
- **Small is the goal.** This is a compact, shippable versus game. When a feature could be cut, cut it. Depth comes from movement and rocket physics interacting, not from content volume.
- **Arenas are hand-built and few.** No procedural generation.

## Code Style

- Namespacing for gameplay scripts (e.g. `Game.Player`, `Game.Weapons`, `Game.Networking`, `Game.Match`)
- Prefer `ScriptableObject` for weapon/character/arena/match-rule data
- PurrNet `NetworkIdentity` / `NetworkBehaviour` classes live in a `Networking/` folder; avoid mixing network and pure gameplay logic in the same class
- Prefer PurrNet Network Modules for reusable networked logic (vitals, weapon state, status effects) rather than duplicating sync code across components

## Current Focus

**Playable 4-player versus match, end to end.** Priority order below; the task-level breakdown, with the current state of each system, lives in [ROADMAP.md](ROADMAP.md).

1. Sync 4+ players moving in the same arena scene (6DoF, momentum intact).
2. Sync the dash-off-surface mechanic (test under lag).
3. Networked rockets: fire, travel, detonate, splash — consistent across all clients, resolved on host.
4. Server-authoritative kills, respawn/elimination, and round state.
5. Charge-commitment economy: braked while charging, cooldown between heavy shots.
6. Elimination ruleset: round scoring, match end, rematch flow.
7. Frag-limit ruleset: respawn timer, kill count, match end — same match logic, different data.
8. Instance hosting: create a match, get a join code, join by code, handle drop/reconnect.
9. Steam integration (friend invites, lobbies, relay) layered onto the same join path.
10. Arena pass — build out the small set of launch arenas.

## Key Decisions Locked

- Genre: **zero-g 3D TowerFall with rockets.** Versus-first, versus-only at launch.
- Netcode: **PurrNet** (over Fish-Net, Mirror, NGO, Photon Fusion).
- Topology: **listen server**, host-authoritative for everything that matters.
- Movement: **6DoF free-flight with dash impulses** (not dash-only, not free-flight-only).
- Combat: **rockets, lethal, recoil-as-movement.**
- Ammo: **none.** The heavy attack is gated by a long charge that brakes you, plus a cooldown between shots.
- Launch modes: **Elimination (last player standing) + Frag limit (respawn).** Both FFA, both data-driven from shared match logic.
- Joining: **join code first, Steam invites later**, over the same underlying path.
- Tone: **Pulpy action** (Metal Slug / Unreal Tournament), not serious sci-fi horror.
- Engine: **Unity 6000.3.10f1.**
- Co-op campaign, roguelike run system, horde mode: **parked, post-launch.**

## Open Questions

- **Player count ceiling** — 4 confirmed, 6 pending a netcode and arena-readability test.
- ~~**Relay/NAT**~~ — settled: PurrNet's bundled `PurrTransport` relay, addressed by `roomName`, which is the join code. No NAT traversal to solve, and `SteamTransport` later swaps the transport under the same code path. `UDPTransport` stays for local MPPM testing.
- **Charge and cooldown tuning** - charge length, cooldown length, and how hard the brake should bite. These three together decide whether the heavy is worth throwing.
- **Respawn tuning (frag mode)** - respawn delay and spawn-point selection.
- **Character differentiation** — stat splits and one ability each, or cosmetic-only at launch.
- **Secondary fire** — does the rocket launcher get a charged/alternate shot, or stay one-button pure.
