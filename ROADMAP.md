# Roadmap — Zero-G Rocket Versus

Priority-ordered path from the current codebase to a playable 4-player versus match.
Scope and locked decisions live in [CLAUDE.md](CLAUDE.md). This file is the *order of work*.

---

## Where the project stands today

**What already exists and is worth keeping:**

| Area | State |
| --- | --- |
| 6DoF movement | `PlayerZeroGMovement.cs` (564 lines) — Rigidbody-based, dash charges, surface dash, stick window, chain bonus. Deep and tuned. **This is the crown jewel.** |
| Input | `PlayerInputHandler.cs` + `PlayerInputActions` — clean UnityEvent-driven, easy to owner-gate. |
| Camera | Third-person rig plus FOV punch, directional lag, speed pullback, wall avoidance. |
| Player prefab | `Assets/Domains/Player/PlayerEntities.prefab` — self-contained (camera rig, body, world-space UI all inside), confirmed by playtest. This is the one to spawn. |
| Engine / netcode | Unity 6000.3.10f1, PurrNet 1.19.1 installed. |
| Art / VFX / SFX | Polygon Sci-Fi kits, gun SFX, VFX_Klaus, retro shaders. Plenty for a prototype. |

**What does not exist yet (the honest gaps):**

- ~~**Networking is at zero.**~~ Closed by P0: `Arena_Test.unity` now has a working NetworkManager rig, and 1 host + 2 clients connect under simulated lag. Still no *networked player* - that is P1.
- **No player-vs-player damage path anywhere.** `PlayerBullet.cs` only calls `EnemyHealth.TakeDamage`. Players cannot currently hurt each other at all.
- **No rockets.** Bullets are fast straight-line projectiles with no splash and no recoil. The core weapon of the game is unwritten.
- **Health is the wrong shape for PvP.** `PlayerHealth.cs` is a 100HP plus regenerating-shield model whose `Die()` calls `SetActive(false)`. Versus needs lethal, server-authoritative, respawnable.
- **No ammo, no pickups, no match/round/score system, no join flow.**
- **Everything runs unconditionally on every instance.** `PlayerShooter` constructs its own `PlayerInputActions` and fires on input with no ownership check — put two players in a scene today and both respond to your mouse.
- **`Weapon.Shoot()` fires at a `Target` GameObject reference** rather than from aim. Needs to become per-owner aim before it can be networked.

**Bottom line:** the movement game is largely built; the *multiplayer game* is not started. Everything below assumes the movement controller stays and gets a network layer wrapped around it, not rewritten.

---

## P0 — Make multiplayer testable at all — DONE

Do this before writing any netcode. It pays for itself in the first afternoon.

- [x] Add **Multiplayer Play Mode** — `com.unity.multiplayer.playmode` 2.0.2 added to `Packages/manifest.json` (2.0.x is the line that targets Unity 6000.3).
- [x] Scene generator instead of a hand-built scene — `Tools > Ghost > Build Test Arena` writes `Assets/Scenes/Arena_Test.unity`: 60m hollow cube, interior cover, 6 spawn points, full network rig. Rebuildable, so the arena can be thrown away without losing the setup.
- [x] **NetworkManager** wired by the generator: UDPTransport, 30Hz tick, ServerOwner rules, auto-start disabled.
- [x] **Latency simulation on by default** — `NetworkLatencySimulator`, 60–80ms one-way (~120–160ms RTT) plus 2% loss, F8 to toggle in play mode (F9-F12 belong to MPPM).
- [x] `MultiplayerBootstrap` — resolves this process's role and starts host/client explicitly.
- [x] `NetworkStatusHud` — role, connection, player count, measured RTT on screen. Verifies the harness with no networked prefab.
- [x] **In the editor:** package resolved, `Tools > Ghost > Build Test Arena` run, `Arena_Test.unity` generated and verified.
- [x] **In the editor:** Play Mode Scenario created with 2 additional editor instances; roles resolve from MPPM's `-name` with no tag setup.
- [x] **Verified:** host reports `players: 3`; both clients `connected`; measured RTT 116-128ms on clients against none on the host, confirming the simulation is live and host-excluded.

**The gotcha this milestone exists to absorb:** PurrNet decides "am I a clone?" by checking whether the `Assets` folder is a symlink — how ParrelSync works, not how MPPM works. Under MPPM every virtual player looks like a plain editor, so PurrNet's default flags would start a server *and* a client in each one: four separate hosts, no connection, no obvious error.

Auto-start is therefore off and `MultiplayerRoleResolver` decides instead, from MPPM's documented `-name` launch argument: `Player1` is the main editor and hosts, `Player2`+ are virtual players and join. Optional overrides, in priority order above that: a player tag literally named `host`/`client`/`server` (you'd have to create it under **Project Settings > Multiplayer > PlayMode > Player Tags**; Unity's FAQ discourages tags for role assignment), and the inspector's `roleOverride` field on `MultiplayerBootstrap`.

## P1 — Players flying in one scene — DONE (4 players verified)

The single highest-risk item in the project. Nothing after this is real until it works.

**Prefab layout** (surveyed, not guessed): `PlayerEntities` root holds only a Transform; `CameraRig` is a *sibling* of the `Player` body so the camera does not inherit body roll; the Rigidbody, input, movement and health all sit on `Player`; the Canvas is a world-space `3dUi` on the spine. So ownership goes on the root and transform sync goes on the child.

- [x] `NetworkPlayer.cs` — owner gating. Disables the whole CameraRig branch (camera, AudioListener, post-process volumes) plus `PlayerInput`, `PlayerInputHandler`, `PlayerShooter` and `RingRadarController` on remote players. Finds them by type, so reorganising the prefab will not silently break it.
- [x] `PlayerSpawner.cs` — host-only, one body per connection, round-robin across the six spawn points, ownership handed to the joining player, despawn on leave.
- [x] `Tools > Ghost > Setup Networked Player` — adds `NetworkIdentity` + `NetworkPlayer` to the root and `NetworkTransform` to the body, creates the `NetworkPrefabs` registry scanning `Assets/Domains`, and wires the arena. Re-runnable.
- [x] Arena generator now adds and wires the `PlayerSpawner`.
- [x] **In the editor:** arena rebuilt, Setup Networked Player run, 3 instances playtested.
- [x] `NetworkAim.cs` — replicates the spine aim pose. The MultiAimConstraint aims at a `Target` that `Aim.cs` drives via the local camera; remote players have no camera, so their target froze and the spine bent toward a stale point. The setup tool now reparents `Target` from `CameraOffset` to the player root so it survives the camera rig being disabled, and `Aim` is owner-gated.
- [x] Animation replication via PurrNet `NetworkAnimator` rather than a hand-rolled sync: autoSyncParameters carries `MoveDirection` with no gameplay change, and `PlayerAnimationStateController` routes dash CrossFades through it (a state change, not a parameter, so polling cannot see it). Remote copies of the controller are disabled - left running they overwrite the arriving value with their own dead input every frame.
- [x] Tick rate raised 30 -> 60Hz. NetworkTransform buffers interpolation in *ticks*, and a client-to-client path pays that buffer twice (host buffers the owner data on arrival, relays from the buffered state, receiver buffers again) - so client-to-client lagged badly while host-to-client looked fine, and disabling the latency sim did not help. The buffer was already at its 1-2 tick floor, so halving tick duration was the lever. Confirmed much better in playtest.
- [x] **Tested:** 4 players (MPPM ceiling: main editor + 3 additional) connect and play. Spawns distinct, no stutter reported.

**Exit criteria:** four people fly around the test arena and it feels good. Stop and fix the feel here if it doesn't — every later system sits on top of it.

## P2 — Dash under lag — code written, needs an editor pass

**The numbers that make this necessary:** cruise is ~14 m/s, a surface dash is **150 m/s** held for 0.2s — one dash covers ~30m. At 60Hz a single tick of interpolation lag is 2.5m at dash speed, doubled on a client-to-client path. Interpolation alone cannot carry this.

- [x] `NetworkDash.cs` — owner fires locally and broadcasts the impulse (velocity, duration) over an unreliable RPC; remotes apply a lead OFFSET in LateUpdate. Cannot drift: NetworkTransform rewrites the transform every Update, so this only ever leads the render.
- [x] **Offset, not absolute prediction.** The first version predicted `origin + velocity * elapsed`, which played the dash twice: on arrival the stream is still showing the body pre-dash, so jumping to the predicted point moved it forward by the whole delay, then the real dash data dragged it back through the same distance — a rubber band. The stream lags true position by ~one-way latency, so the honest correction is simply `stream + velocity * latency`, with the lead measured from `tickModule.rtt` rather than tuned as a constant.
- [x] **Interpolation buffer deepened 1/2 -> 2/6.** The actual source of the rubber band. `Interpolated.Add` discards samples and resets its timer when the buffer is full, and `Advance` freezes on the last value when it runs dry - so jittery delivery gives freeze, jump, freeze, jump. Invisible at 14 m/s cruise (~2cm), glaring at 150 m/s dash (2.5m+). Costs ~33ms more render latency, which the dash lead offset buys back.
- [x] **Lead now covers the whole client-to-client path.** It was computed from the observer's own rtt, which measures only its leg to the host - the dashing player's leg was invisible to it, so a client watching another client under-led by roughly half the real gap while host-to-client looked fine. The dashing player now stamps its own measured latency into the impulse, and the observer adds sender + own + interpolation buffer delay. PurrNet's two ticks of pack/unpack overhead are removed before halving, so the lead does not overshoot.
- [x] Speed is read from the Rigidbody at dash time rather than added to `OnDashPerformed` — the event carries direction only, and speed varies with chain, rebound, escape and glide multipliers. No gameplay code change needed.
- [x] `PlayerZeroGMovement` disabled on remotes. PurrNet sleeps remote Rigidbodies, but the movement script wrote `linearVelocity` every FixedUpdate, waking them and braking against the sync. Costs booster VFX on remote players — re-drive those from synced velocity if it matters.
- [x] **Arena was undashable.** `surfaceMask` is the `DashSurface` layer (bit 6); the generator was putting all geometry on `Default`, so surface dashes never fired in the test arena. Generator now sets the layer and errors loudly if it is missing.
- [x] Arena resized 60m -> 150m. A 30m dash crossed the old box in one move.
- [ ] **In the editor:** Ctrl+R, rebuild the arena, run Setup Networked Player, play.
- [ ] **Test:** two players chain-dashing off the same wall at 150ms. Positions must converge, not drift.

## P3 — The rocket — first pass written, needs an editor pass

The game's identity. Build it as a new `Rocket` type — do not extend `PlayerBullet`, which is enemy-damage-only and client-side.

- [ ] Rocket projectile: slow, visible, dodgeable travel. Tune against the REAL dash numbers on the prefab — `surfaceDashSpeed = 150`, `airDashSpeed = 100`, `dashGraceTime = 0.2` (so ~30m per dash) against a `maxSpeed = 14` cruise. A rocket must be beatable by a dash but not by drifting.
- [x] **Splash on detonation** — `Rocket.cs`: lethal inside `killRadius` (4m, generous on purpose so netcode error does not decide fights), shove falling off linearly to `shoveRadius` (12m). Damage itself waits for P4's health model; the kill set is already computed and logged, so wiring it in is small.
- [x] **Recoil** — `RocketLauncher` applies it locally and immediately. The shooter owns its own body, so waiting a round trip to be pushed would break the central movement mechanic.
- [x] Aiming fixed — the launcher fires from the weapon fire point toward the same `Target` the spine rig uses, not `Weapon.Target`. Origin and direction are sent by the shooter, since by the time the request reaches the host the shooter has already drifted.
- [ ] **Network model:** host simulates, firing client predicts locally, hits resolve on host. Spawn and detonation are networked events; muzzle flash, smoke, explosion VFX and audio fire locally from those events (never networked).
- [ ] **Test:** all clients see the rocket in the same place. A rocket that exists on one screen and not another is a bug, not a tolerance.

## P4 — Lethality and death — first pass written, needs an editor pass

- [x] `VitalsModule` — alive-or-dead, binary on purpose. CLAUDE.md has death instant and rockets lethal, so a hit-point pool would be a number nobody sees a middle value of, and would invite chip damage and TTK tuning the design rules out. `PlayerHealth` is left alone for single-player scenes.
- [x] Built as a PurrNet **NetworkModule** per CLAUDE.md, so P5 ammo reuses the shape: server-owned state with an owner-readable mirror.
- [x] **Server-authoritative.** `Kill()` refuses to run on a client. Returns false if already dead, so one blast cannot score the same player twice.
- [x] Death goes out as a single RPC carrying killer + hasKiller, not two SyncVars — separate SyncVars are not guaranteed to land together, and a client could pair "dead" with last tick's killer and misattribute the kill.
- [x] `PlayerVitals` — hides the model, cuts owner control, stops the corpse drifting. Does NOT destroy the object: the match system needs it to respawn, and a reconnecting player needs something to re-own.
- [x] Rocket kill seam wired. Self-kills allowed on purpose — rocket-jumping into your own blast should be able to end you, which is what keeps recoil a real decision.
- [ ] **Tighten the network rules.** The arena ships with `ServerOwner` so P1 can move clients freely. Once damage is authoritative, swap the NetworkManager to `ServerStrict` (`Assets/PurrNet/Defaults/NetworkRules/`) and fix whatever breaks — that is the intended cost, paid here rather than at ship.

- [x] Respawn + scoring + victor (pulled forward from P6). `MatchRules` ScriptableObject + `MatchManager`: host counts kills, respawns after a delay, declares a winner at the frag limit. Scores broadcast as buffered RPCs so late joiners get them.
- [x] Death VFX wired; `PlayerSpawner.TryRespawn` reuses the body rather than destroying it, and clears transform interpolation so a respawn is not rendered as a fast flight across the arena.
- [x] **Rocket homing (design addition).** Enemies inside the lock cone with line of sight become homing targets; the cone is drawn on screen by `LockOnReticle`, derived from the launcher angle and camera FOV so the circle cannot drift from the rule. Turn rate is capped so a hard dash still beats a lock - see the note below.

**Homing vs dodging:** CLAUDE.md builds the game on rockets being "slow enough to dodge" with momentum-reading as the core skill. `turnRateDegrees` is the dial that trades one against the other: too high and no dash escapes, and the central skill stops mattering. Starting at 90 deg/s. Tune by playing, and treat a rocket nobody can dodge as a bug.

**Milestone:** at this point four players can kill each other. That is the first genuinely playable build — get it in front of friends before continuing.

## P5 — Charge-commitment economy (replaces the ammo economy)

**Why this replaced ammo:** the game moved to Smash-style knockback, where the heavy attack is a
commitment rather than a consumable. A carry count, recoverable pickups and a scarcity loop all
belonged to the one-hit-kill rocket design and stopped making sense the moment percentage and
ring-outs became the win condition.

- [x] **No ammo at all** - no counter, no reserve, no pickups, nothing to collect.
- [x] **Braked while charging, not frozen.** Holding fire past the charge threshold forces the
  existing brake/precision mode (~40% speed, reduced acceleration) via a new `ExternalBrake` flag
  on `PlayerZeroGMovement`. Freezing outright was the first attempt and was too punishing with
  three other players shooting - it also froze the booster visuals, which are the telegraph that
  should be most visible while someone commits.
- [x] **Cooldown between heavy shots**, server-authoritative, re-checked on the host so a client
  cannot spam. Timed from the shot rather than from the projectile resolving: tying it to the
  projectile meant a rocket that chased someone for ten seconds locked the weapon for ten
  seconds, punishing the shots that were working.
- [x] Charge lengthened (1.3s to arm, 3.0s to full) now that the weapon is always eventually
  available and the charge is the main gate.
- [x] **Full charge or nothing.** The heavy only fires at 3.0s; releasing early discards the whole
  windup and the next press starts from zero. A partial shot let you half-charge and release the
  moment anyone looked at you, which dodged the brake instead of paying it. HUD snaps the ring
  tight and pulses `HEAVY READY` at the threshold, since releasing a frame early now fires nothing.
- [x] **Dashing cancels the charge.** Any dash, surface or air, restarts the hold clock from zero
  and lifts the brake, so the release that follows fires nothing. Without it the brake was a
  formality: you could hold fire, dash out of everything aimed at you, and still land the launch.
  Escaping now costs the shot, and keeping the shot means standing in it.
- [x] Dying mid-charge cancels cleanly, or the brake flag survives death and the next charge
  silently fails to apply it.
- [x] HUD: a ring closing inward as the shot winds up, and a counting-down `HEAVY 2.4s` while
  recharging - a rhythm to play around needs to show where in it you are.
- [ ] **Tune the trade:** charge length, cooldown length, and brake strength. These three together
  decide whether the heavy is ever worth throwing in a four-player fight.

## P6 — Match flow

- [x] `MatchRules` ScriptableObject — win condition, respawn delay, auto-start threshold, intro and
  round-end dwell. Two assets: `FragLimit` and `Elimination`.
- [x] **Host-authoritative state machine**: Lobby → RoundIntro → Round → RoundEnd → MatchEnd →
  rematch. Replicated by one `ObserversRpc` carrying state, round number, dwell *duration* and the
  relevant player. Duration rather than a deadline, because `Time.time` is each peer's own clock.
  `bufferLast` so a mid-match joiner is told the current state instead of sitting in a lobby
  everyone else left.
- [x] **Both rulesets run the same machine.** The modes differ in exactly two answers — what a
  death does, and what ends a round — and nothing else branches. Deaths outside the `Round` state
  score nothing, which is what stops a rocket landing a frame after the round was decided from
  awarding a point in a round that is already over.
- [x] **Elimination**: one life, last player standing takes the round, first to N rounds takes the
  match. A double KO is a drawn round and awards nothing rather than crediting the explosion.
- [x] **Frag limit**: unchanged behaviour, now inside the machine — one long round, kills score,
  the frag limit ends the match. The intro banner reads FIGHT rather than ROUND 1, since nothing
  increments.
- [x] Round reset needed a new path: `VitalsModule.Revive` early-outs on the living, which is
  right for a respawn and wrong for a reset — the Elimination round winner would start the next
  round carrying their percentage, making the winner the easiest player to launch. `ResetForNewLife`
  plus `PlayerSpawner.ResetAllForRound` place everyone on clear points with a fresh life, reusing
  the respawn broadcast so there is no second code path to drift.
- [x] Lobby is a **warmup**, not a waiting room: fly and shoot, nothing scores, death puts you
  straight back. Auto-starts at `autoStartPlayers`, or the host starts it with Enter.
- [x] `MatchHud` — warmup, ROUND N countdown, round winner in their own colour, drawn rounds, match
  end with scores, and rematch/return-to-warmup on the host's keys. Added automatically by
  `MatchManager` so a match always has feedback, but kept a separate class so the match logic holds
  no presentation.
- [ ] Awaitable RPCs for the round handshakes. Not needed yet: the flow is one-way host→clients and
  the host never has to wait on an answer. Worth it when round start has to confirm every client
  is ready before the clock runs.

## P7 — Instances and joining

- [x] **Main menu: Host / Join** (`SessionMenu`), drawn only when the process is *menu-driven* —
  `MultiplayerRoleResolver.TryResolveExplicit` now separates "MPPM told me what I am" from "nothing
  said, so fall back to host". MPPM instances start as before and never see the menu; a normal
  launch gets the menu instead of silently opening a LAN server (which would also have locked the
  transport and made the relay switch throw).
- [x] `CursorLock` gained a `Suspended` gate: it re-grabbed the pointer on every focus change, so a
  menu was visible and unclickable after any alt-tab.
- [x] `MenuBackdropCamera` — every camera in this game lives on the player prefab, so the scene has
  none at all until a connection produces a player. Auto-hosting hid that; deferring the connection
  turned it into Unity's "No cameras rendering" with the menu drawn over an undefined frame. A
  slowly orbiting camera carries the menu and stands down the moment anything else renders, left
  Untagged so `Camera.main` still means the player's. It carries the scene's only `AudioListener`
  until the player brings its own, and is created on every launch, since MPPM instances had the
  same gap for the second or two before their player spawned.
- [x] **Join code** (`JoinCode`): 5 characters from a 25-character alphabet with every misheard or
  misread pair removed (no 0/O, 1/I/L, 5/S, 2/Z, 8/B), ~9.7M combinations, shown grouped for
  reading aloud. The code **is** the relay room name, prefixed so this game cannot collide with
  another PurrNet project on the public relay. Typed input is normalised live, and the two
  genuinely unambiguous shapes (O/0, I/1/L) are mapped rather than rejected — pairs where neither
  side is legal are dropped instead of guessed, since substituting one wrong character for another
  helps nobody.
- [x] `SessionController` — host/join/leave, transport chosen at runtime so the MPPM flow keeps
  using localhost UDP and Steam can later be a third transport under the same code path. Timeouts
  report *why* ("no room called ABC DE") and wind the transport back, so a failed attempt leaves a
  clean manager rather than a half-open socket and a menu stuck on "connecting".
- [x] Host's code is shown in two places, each with a reason: large in the warmup panel, where it
  gets read out, and in the Escape panel with a COPY button, for when a friend asks after the match
  started. Nothing sits permanently on screen during a fight — a readout you cannot use while
  dodging is clutter.
- [x] **UI/UX pass on both menus** (`MenuSkin`, a shared palette, button and scale). The two screens
  were drawn with the default IMGUI skin, which is why they read as a debug overlay, and in raw
  pixels, so they shrank to a postage stamp on a high-resolution monitor. Fixed together: layout is
  written against a 1080p-tall screen and scaled by `GUI.matrix`, and everything shares the HUD's
  palette with red still reserved for danger. Also from that pass:
  - The code is five character cells typed into directly, not a `GUI.TextField`. The old field had
    its contents normalised every frame, which pushed the caret to the end on every keystroke — a
    character in the middle of a code could not be corrected. Owning the keystrokes fixes that and
    shows how many characters are wanted before a key is pressed.
  - Keyboard-first: type anywhere to fill the code, Enter hosts when empty and joins when complete,
    Escape clears, Ctrl+V pastes (codes arrive over chat, so paste is the common case). The cursor
    is locked for the whole game and this is the one screen that unlocks it, so a click to focus a
    field was friction in the one place it could be avoided.
  - Connecting shows elapsed seconds, a bar draining to the timeout, and CANCEL. Fifteen seconds of
    a motionless "Opening a room..." reads as a hang.
  - The warmup panel gained a **roster** — who is actually in the room, in player colours, read from
    the spawned bodies so every peer sees the same list — and a **control list**, because a friend
    invited into someone else's room had nothing anywhere in the game telling them how to fly.
  - `MatchHud` no longer draws while a menu-driven process is offline: the match sits in Lobby from
    the first frame, so the warmup panel was being drawn underneath the Host/Join menu.
  - **Tab folds the warmup panel** to a one-line strip at the top of the screen. The warmup is
    played in, not read in, and the panel sat over the middle of the screen — exactly where you are
    looking while flying. Collapsed, it keeps the two things worth keeping: that this is still the
    warmup, and the host's code, since the usual reason to be in the warmup is that somebody has
    not arrived yet. Handled before the host-only key gate, so clients get it too.
  - Panel widened 560→640 and heightened 470→530.
  - **The room code has exactly one home: the warmup panel's title row**, at full `Code` size. It
    was in three places — the panel, the Escape prompt and the collapsed strip — which made every
    one of them a place to look for the same five characters. The panel is the right one: the code
    is read out once, at the start, while you are sitting still waiting for somebody. The strip is
    what stays up while you fly, so it holds nothing but the word and the key. Losing the Escape
    panel's COPY button is accepted — the code is five characters, and a button needs a cursor the
    warmup does not have.
  - `GUI.depth` set explicitly — `SessionMenu` at 0, `MatchHud` at 1, lower drawing nearer the
    front. OnGUI order between components is undefined, and the Escape prompt was landing *behind*
    the warmup strip, which for a modal asking whether to close the room is not cosmetic. It also
    dims the screen behind itself now, so it reads as a question rather than another HUD layer.
- [x] **Fixed: the menu and the MPPM test flow were incompatible.** MPPM launches virtual players
  with `-name PlayerN`, so they resolve to Client, skip the menu and auto-connect on the scene's
  `UDPTransport`; the main editor gets no such argument, so it is menu-driven, and hosting from the
  menu moved it onto the relay. The clones were knocking on localhost while the host was on the
  internet, with no way to type a code and nothing on either screen to say why. Two additions:
  - `HostLocal` / `JoinLocal` on `SessionController`, surfaced as a **LOCAL TESTING** pair in the
    menu (editor and development builds only). Hosts on the scene transport instead of the relay,
    so one click opens exactly the connection the clones are already retrying for. Also the only
    path that works with the relay unreachable, and usable between two machines on one network.
  - An MPPM player tag **`menu`** resolves to `MultiplayerRole.None`, and `MenuDriven` now treats
    that role as a request for the menu rather than a launched role.
- [x] **Every instance starts on the same screen.** Host or client is now what you pick on that
  screen, not something inferred from how the process was launched. MPPM passes `-name PlayerN` to
  every virtual player whether or not anyone asked for it, and treating that as a role is exactly
  what left clients with no way to type a code — so `TryResolveTag` was split out of
  `TryResolveExplicit`, and only the two *deliberate* markers still take the menu away: an
  inspector `roleOverride`, and an MPPM player tag naming a role. The `-name` fallback survives
  only for a scene with no `SessionController` at all.
- [x] **Fixed: "Cannot change transport while it is being used" on any second attempt.** PurrNet's
  transport setter refuses unless both `serverState` and `clientState` read `Disconnected`, and
  those report `Disconnecting` for as long as its internal cleanup flags are set — so stopping is
  not instant, and `SessionController` was assigning the transport immediately after `StopServer`.
  A host attempt that failed or was cancelled therefore poisoned the next join, which surfaced as
  a transport error rather than anything resembling the actual situation. Attempts are now
  *queued*: `TryStartQueued` waits for the manager to go idle, then assigns the transport and
  starts — one place, at the one moment it is legal. `Leave` and `Fail` no longer restore the
  transport at all, since mid-shutdown is exactly when that throws. Enter in the menu also stopped
  hosting on an empty code: a stray keypress silently opening a relay room is the likeliest way to
  land in that state by accident.
- [x] **Nicknames** (`PlayerNames`, `IdentityModule`). A name field in the menu, persisted to
  `PlayerPrefs`, replicated as an owner-authoritative `SyncVar<string>` on a module hanging off
  `PlayerVitals` — which is why it needed no prefab change. Every readout picked it up for free:
  `PlayerPalette.NameOf` is the single place that decides a player's wording, so the scoreboard,
  the percentage cells, the on-screen diamonds, the radar rings, the warmup roster and the round
  banners all started showing names without being touched, falling back to `Player N` for anyone
  who never chose one. Owner-authoritative on purpose — a display name decides nothing, so routing
  it through the host would mean an RPC and a validation path to protect a label — but sanitised
  on arrival as well as on send, so a hostile name can be ugly and cannot be malformed enough to
  break a HUD. Written from both `OnSpawn` and `OnOwnerChanged`, since those two have no
  guaranteed order and a write before ownership lands goes nowhere. Seeded from the MPPM instance
  name on a first run, because four virtual players share one `PlayerPrefs` store and would
  otherwise all come up called the same thing.
- [x] **QUIT on the main menu**, last in the panel and deliberately narrow and unlit — it is the one
  button there whose job is to end the session rather than start one. Stops play mode in the editor
  rather than calling `Application.Quit`, which does nothing there and would have made the button
  look broken in exactly the place it gets tested.
- [x] Lobby **mode selection**: the host cycles rulesets with M, replicated by label so clients see
  what they are about to play. The payoff of ruleset-as-data — switching Elimination to Frag Limit
  changes no code path and needs no reload. The lobby is the match's own Lobby state, so P6's
  warmup and P7's lobby are one screen rather than two.
- [x] Drop/reconnect: PurrNet's cookies already hand a returning client its old `PlayerID`, and
  the match keys scores by it, so scores survive a reconnect without extra work. Fixed a leak on
  the way — a player who dropped while dead left their spawn point reserved for the rest of the
  match, pushing everyone away from a point nobody was standing on.
- [x] **Fixed: leaving a room left you unable to host or join again.** Two separate causes.
  - `SessionController` waited for the manager to report both server and client `Disconnected`
    before *any* attempt, because assigning the transport throws otherwise. But hosting twice in a
    row reuses the same transport object, so there was nothing to assign — the wait was pure
    blockage. It now only waits when the transport actually has to change, or when hosting (PurrNet's
    `StartClient` has a coroutine that waits for its own cleanup; `StartServer` goes straight to the
    transport with no wait, so that path still needs a settled manager). Rejoining as a client no
    longer waits for anything at all. While it does wait, the status names the two connection
    states, and giving up logs them — "nothing happens when I press host" is otherwise
    indistinguishable from a dead button.
  - `MatchManager` was frozen wherever the last session ended. `IsHost` goes false when the session
    stops, which halts its `Update`, leaving `_state`, `_scores` and `_winner` intact — so hosting
    again resumed in `MatchEnd` with the previous winner still declared. From a player's seat that
    is indistinguishable from the new room being broken. `ResetForNewSession` now fires on the
    offline→host transition, caught inside `Game.Match` so it does not need to know the session
    layer exists.
- [ ] Ready-up. Deferred: auto-start plus the host's Enter covers it, and a ready list is worth
  building once there is a real lobby screen to put it on (with Steam personas, not `Player 2`).
- [ ] Arena selection — needs more than one arena first (P9).
- [x] **Decided: PurrNet's own relay.** `PurrTransport` ships with the package, points at a hosted
  relay, and is addressed by `roomName` — which *is* a join code, with no NAT traversal or port
  forwarding to solve. `SteamTransport` is a sibling addon, so Steam later swaps the transport
  under the same "type a code, connect" path rather than replacing it. `UDPTransport` stays for
  local MPPM testing.
- [ ] Keep the code path and the transport separable, so Steam layers on rather than replaces.

## P8 — Steam

- [ ] Steamworks integration; Steam friend invites and lobbies routed through the **same** join path built in P7.
- [ ] Steam Datagram Relay for NAT traversal.
- [ ] App ID, depot, store scaffolding.

## P9 — Content and feel pass

- [ ] Build the small set of launch arenas. Enclosed volumes, cover, kickable surfaces, no dead space.
- [x] `Assets/Scenes/Arena_01.unity` — the first real level, duplicated from `Arena_Test` so it
  inherits the working rig (`--- NETWORK ---`, `--- MATCH ---`, `--- BLAST ZONE ---`, the six spawn
  points and the light) rather than needing it rewired. The prototype geometry under `Arena` comes
  with it and is meant to be replaced. `Arena_Test` stays as the throwaway.
  - **Latency simulation is off by default everywhere**, and the F7 diagnostics panel with it. Both
    needed a field *rename* rather than a changed default — `simulate` → `simulateLatency`,
    `show` → `showDiagnostics` — because Unity binds serialized values by name, so the scenes'
    existing `simulate: 1` / `show: 1` would have kept overriding the new code default. This is the
    same trap that has caught this project out repeatedly (lock range, blast radius, projectile
    rings); a new name has no stale value to inherit. Press F8 for lag, F7 for the panel.
  - F7 used to gate the *whole* `NetworkStatusHud`, so hiding the debug panel also hid the damage
    percentages and the out-of-bounds warning — which are not diagnostics but the two readouts a
    player needs in order not to die. Split: the panel toggles, the rest always draws.
- [x] Menu wordmark is **ZERO GUN**. Only the wordmark: the relay room prefix (`ghost-`) and the
  PlayerPrefs keys are deliberately unchanged, since they are invisible to players and renaming the
  prefix would stop two builds from finding each other's rooms.
  - Needed `MenuSkin.TrackToFit`, which measures a tracked wordmark and backs the letter-spacing off
    until it fits. Tracking is spaces in the string, so a name one letter longer can overflow its
    panel — and `GUI.Label` clips instead of shrinking, so the first and last letters just vanish
    and it reads as the title failing to draw. `ZERO GUN` at eight characters was doing exactly
    that. The session panel also went 460→560 wide, and the warmup title now measures against what
    the code strip leaves of its row rather than the full width.
- [x] **Repointed `RingRadarController` from PvE to versus.** Tracks opposing players and rockets
  from their own static registries instead of physics layers — rockets carry no collider at all
  (they SphereCast for hits), so the old projectile layer sweep could never have found one.
  Rockets show always; enemy players only while braking. Dropped the enemy-awareness threat model
  (stealth perception, turret line of sight) for plain distance, which also cuts the radar's last
  dependency on the parked prototype enemies, and removed the new-contact ping that would have
  leaked enemy positions past the brake gate.
- [x] **Enemy diamonds on the HUD** (`EnemyScreenMarkers`) — a 45°-rotated square over every living
  enemy in view, scaled by distance, with their name and percentage underneath. On-screen only, on
  purpose: off-screen enemies are the radar's job, and edge-clamped arrows would compete with the
  rings for the same glance.
- [x] **Per-player identity** (`PlayerPalette`) — one source for each player's name (`Player 2`) and
  colour, derived from the `PlayerID` so every peer agrees. Join-order indices would have made the
  same player a different colour on each screen. Used by the diamonds, the radar rings, the
  percentage readout and the scoreboard, so "the orange one" means one thing everywhere. Red is
  reserved for incoming rockets and is deliberately not in the palette.
- [x] Radar rockets: **own shots filtered out** (needed a `SyncVar` for the shooter — `_firedBy` was
  host-only, so clients could not tell their own rocket from an incoming one), forced red rather
  than a distance ramp, and the arrow resized to match the enemy arrow. The size gap was prefab
  data, not code: both markers embed the same arrow, but only the enemy one scaled it down, and the
  rocket marker had no `indicatorRenderer` assigned so nothing could tint it. `Tools > Ghost >
  Setup HUD` fixes both.
- [x] Fixed: the radar resolved its own player with `FindGameObjectWithTag("Player")`, which
  returns an arbitrary player once four exist — the local radar could centre itself on a remote
  player. Also fixed `Rocket`'s `private void OnDestroy` hiding `NetworkIdentity`'s virtual one,
  which skipped PurrNet's despawn bookkeeping on every rocket.
- [x] Blast radius 220m → **110m** (code default and the serialized value in `Arena_Test.unity`).
  The island with its debris reaches ~60m, so the out-of-bounds region is now about one more
  island-radius of empty space rather than four. Percentage starts killing around 50% from the
  island's edge instead of needing 100%+.
- [x] **Fixed: ring-outs never fired.** `BlastZone` measured `body.transform.position`, but the
  spawner hands back the networked root — which never moves. It was testing each player's spawn
  point against the radius every tick, so nobody was ever out. The boundary was blind, not off.
- [x] **Fixed: knockback was erased on landing.** The arcade movement kills "unwanted" velocity at
  18/sec and soft-caps anything over `maxSpeed` (12), so a 50 m/s launch was gone within a tenth
  of a second and percentage appeared to do nothing at any scaling. `PlayerZeroGMovement` now has
  a launch state — brake and cap suspended, dashing blocked, duration scaling with launch speed
  (capped at 2.5s), with 25% thrust for Smash-style directional influence so recovery is a skill.
  Knockback retuned on top: base 12→18, scaling 0.35→0.9.
- [x] Laser fires the gun models' existing muzzle particles, alternating hands, from the observers
  RPC so other players can see someone shooting at them.
- [x] Lock range cut 120m → **20m** (code default *and* the serialized prefab value, which was the
  one actually in effect). At 35 m/s a rocket crossed 120m in 3.4s, so the lock was arena-wide and
  the shot landed after the target had dashed away. 20m is ~0.6s of flight: homing now forgives
  your aim at knife range instead of doing the aiming, and every other shot is a straight one you
  lead yourself. Same value gates the red reticle, so the circle never promises a lock it cannot
  deliver. The circle's size is unchanged — it draws the 12° cone, not the range.
- [x] **Reticle pass, Deadlock-shaped.** Ring scaled to 0.4 of the true cone (still FOV-derived, so
  it stays honest under the dash FOV punch), dot plus four offset ticks instead of a bare dot, and
  the dash charges moved off the character's back onto the HUD under the ring. The hologram was
  also *wrong* on every remote player: dash charges are local state and never networked, so a
  remote copy showed full charges forever. `Tools > Ghost > Setup HUD` disables it.
- [x] Lock marker goes **red** when it is the lock target, player colour otherwise.
- [x] **Fixed, properly: a teleported body could keep its old position on other peers forever.**
  The transform stream is delta-based and unreliable. A dead body is *static*, so the owner sent
  exactly one packet carrying the teleport and then went quiet — nothing was changing any more.
  Lose that single packet (the latency sim drops 2%) and every other peer kept the pre-teleport
  position with no further update coming, which on the host meant a respawned player was still
  outside the boundary as far as the only peer that decides ring-outs was concerned. `ApplyTeleport`
  now calls `NetworkTransform.ForceSync()` on the controller, PurrNet's own escape hatch for this:
  it voids delta compression and pushes the state over the reliable RPC path, teleporting every
  observer instead of feeding their interpolation buffer. Verified the static wiring first — spawn
  points sit at 53.7m inside a 110m radius, the blast zone is at the origin, the NetworkTransform
  is on the body with world-space sync, and the body's prefab offset is zero — so none of those
  were the cause.
- [x] `BlastZone` now **corrects** instead of killing when a protected player reads as out of
  bounds: that cannot be real, since they were just placed on a spawn point, so it returns them to
  one and logs a warning naming the cause. Self-healing, and loud if a position update ever goes
  missing again.
- [x] **Death and respawn moves are broadcast with their position, and applied by every peer.**
  Both used to be a `TargetRpc` that moved the body on its owner only, with the reveal riding the
  separate alive `SyncVar`. Nothing ordered those two against each other, and every other peer —
  host included — had to wait for the owner's next transform update to learn the new position. So
  the normal case was a player revealed at the place they died for most of a round trip, which
  after a ring-out is out of bounds. Now the pose travels inside an `ObserversRpc` and each peer
  snaps to it at once; writing a remote owner-auth body is right here precisely because every peer
  was told the same pose, and the owner's next update confirms it rather than contradicting it.
- [x] **Corpses are parked at death, not left where they fell.** The host reserves the player's
  future spawn point the moment they die and teleports the body there immediately, so the respawn
  3s later is a visibility change rather than a move. This is also what makes the message ordering
  above stop mattering: whichever arrives first is revealing a body already in the right place. Previously a ring-out left the body outside
  the boundary for the whole respawn delay, which is what produced the double-death and the visible
  jump: revive and teleport raced, and the boundary saw the stale position in between. Dead bodies
  also lose their colliders now — a corpse was still solid, so an invisible wall sat wherever
  someone was killed, and parking them on spawn points would have put those walls where players
  return.
- [x] **Death camera.** `ThirdPersonCameraRig.HoldPosition` stops the rig following while leaving
  the look controls live, so the view stays on the spot you died and you can turn and watch the
  round until control returns. Set before the corpse is parked, or the rig would follow the body to
  its spawn point in the same frame and cut away from the death — which was the known tradeoff of
  parking, now resolved. A flag rather than disabling the component, since disabling would take the
  look input with it and leave the player staring at one fixed angle. Releasing it re-attaches to a
  body already standing on its spawn point, so the view arrives together with control.
- [x] **Spawn points are chosen by clearance, not in rotation.** `GetSpawnPose` now scores each
  point against living players *and* the points already reserved by other corpses, taking the
  first with 12m clear and otherwise the emptiest. Round-robin was fine for one death at a time
  and wrong the moment two players died together or someone was floating on the point that came
  up — two bodies arrived in the same place and interpenetrated. Starts from the rotating index so
  an empty arena still cycles.
- [x] **Respawn invulnerability (3s) with a Smash blink.** Enforced in `VitalsModule.CanBeHit`, so
  the rocket, the splash, the laser and the boundary all honour it through one test rather than
  four. Blocks knockback as well as damage — being shoved while untouchable would make the window
  a liability. The blink is a 9Hz renderer toggle on every peer: an untouchable player otherwise
  looks identical to a touchable one, and opponents waste a 3-second charge on someone who cannot
  be hit. Square wave rather than a fade, so it works with whatever shader the character uses.
  The invulnerability timestamp is stamped locally per peer off the alive flag, so nothing extra
  goes on the wire.
- [x] **Fixed: respawn insta-death.** The respawn teleport is an RPC to the owning client, who moves
  the body and streams the position back, but the host flips the player to alive immediately — so
  for about a round trip the host still saw them at the position they died, which after a ring-out
  is out of bounds, and killed them again. `BlastZone` now skips players for 1s after a revive.
  Spawn points themselves were fine (~54m, well inside the 110m boundary).
- [x] **Fixed: the back hologram survived death.** `SetVisible` toggled `Renderer` and
  `ParticleSystem`, but UI draws through `CanvasRenderer`, which is neither — so the hologram
  stayed lit over the corpse. World-space canvases are now included; screen-space ones are
  deliberately left alone, since one of those under the player would be that player's own HUD.
- [x] Centre aim dot on the reticle. The lock circle marks a 12° homing cone, which is the wrong
  thing to aim a hitscan laser with; nothing marked the actual pixel the shot goes through.
- [x] **Fixed: the aim pose flickered in tight spaces and lagged behind at speed.** `Aim.cs` placed
  the spine rig's target on whatever a camera raycast hit, falling back to a point 100m out when
  the hit was too close to the player — a *branch*, with no hysteresis, between a point two metres
  away and one a hundred metres away. In a confined arena that test flips frame to frame and the
  target teleports between the two, which was the flicker. Separately it eased a **world-space
  position**, so translating at 30 m/s left the point behind in the world and the spine pointed at
  where you had been — the "body still aiming at the floor while flying forward". Now the direction
  is always exactly the camera's forward, never interpolated, and the only smoothed quantity is a
  clamped scalar distance along it: a scalar cannot jump between two places, and shortening it
  cannot drag the aim behind the player. Every outcome lands in `[minimum, aimDistance]` and a miss
  equals a far hit, so there is no discontinuity left to alternate across. Surface-awareness is
  kept behind `surfaceAware`, off by default — it is the only part that can reintroduce one, and
  the weapon fires along the camera ray regardless, so the old behaviour was posing the body to
  point somewhere the shot would not go. Also moved to `LateUpdate` (the camera rigs move there;
  the raycast had been sampling a stale camera from `FixedUpdate`), given a layer mask, and set to
  ignore triggers so the arena-sized blast-zone volume is not a surface.
- [x] **Control pass, and the dash economy changed with it.** Shift dashes, middle mouse is the
  radar (the old brake — same mechanic, renamed for what it is actually for), Ctrl is now down.
  Rebound in both `PlayerInputActions.inputactions` *and* the generated `PlayerInputActions.cs`,
  which embeds the same JSON — editing only the asset would have left the generated class
  disagreeing with it until Unity next regenerated.
  - **No more mid-air dash, and surface kicks now cost a charge.** It used to be backwards: the
    free-flight dash spent a charge and the surface kick was free and unlimited, so the resource
    was something you burned in open space while the arena's surfaces — the whole point of building
    it out of kickable geometry — cost nothing. Now the three charges are the budget for how often
    you can redirect off geometry, and open space holds nothing but the momentum you already have.
    The charge is checked before sticking and spent on release, so sticking to a surface you cannot
    afford to leave is impossible.
  - The dash FOV punch moved to `ReleaseStickDash`. It was only ever triggered on the air-dash
    path, so deleting that path would have silently taken the dash's one piece of camera feedback
    with it.
- [x] **Shooting rework: one button, one meaning.** Left mouse is the laser and only the laser,
  held for continuous fire; the rocket moved to right mouse, keeping its charge economy intact
  (windup brakes you, full charge only, dash cancels, cooldown after). The tap/hold split on a
  single button meant every sustained burst eventually stopped firing and began a windup nobody
  asked for — two readings of "holding fire" in direct conflict. Needed a real `Rocket` action in
  `PlayerInputActions.inputactions` *and* all eight insertion points in the generated
  `PlayerInputActions.cs`, since `PlayerShooter` drives shooting through the generated class rather
  than the `PlayerInput` component.
  - **Aim assist on the laser.** 20° half-angle, `assistRange` 70m (well short of the bolt's 300m,
    so it forgives close-quarters tumbling instead of becoming an aimbot). Only the direction is
    bent, and the cone and line-of-sight tests are the *same code* the rocket lock uses —
    `TryFindTargetInCone` is now shared, because two copies would drift and the one that drifted
    would be the one letting you shoot through a wall.
  - The ring is now measured against that assist cone rather than the rocket's lock cone, but kept
    at its old compact screen size: `ringScale` 0.235, since tan(12°)·0.4 / tan(20°) = 0.234. Being
    *inside* the cone is the useful direction to err — everything the ring encloses hits, and the
    assist keeps working a little past its edge. Above 1 it would promise hits the assist cannot
    deliver, which is why the tooltip says not to.
  - **Deleted the `chargeTime` dead zone.** It was 1.3s of holding before the windup "began", and
    it existed only to tell a tap from a hold while the laser and the rocket shared one button.
    With the rocket on its own button a press can only mean one thing, so all it did was swallow
    the first 1.3s: no brake, no ring, no acknowledgement, and then the charge ring popped in
    already half-shrunk — which is what read as the feedback arriving seconds late. It also handed
    out over a second of charging at full speed for free. `ChargeFraction` now measures from the
    press, `IsCharging` is true from the frame the button goes down, and the brake engages
    immediately, because pressing the rocket button *is* the commitment.
  - **The laser cannot fire while a rocket is winding up.** The windup is meant to be the moment
    you are committed and vulnerable — that is what the brake is for — and letting the laser run
    through it made holding both buttons strictly better than either, at no cost to the charge.
  - **Knockback split cleanly between the two weapons.** The laser is now `laserKnockback` 0.2 with
    **zero** percentage scaling: it builds percentage and moves nobody. Scaling in particular had
    to go to zero, or the laser would get better at launching as the target's percentage climbed
    and quietly become the kill move at high percent. The rocket took the whole ring-out job
    instead — base 18 → **26**, scaling 0.9 → **1.3** (~91 m/s at 50%, ~221 m/s at 150%). Needed
    yet another rename, `baseKnockback` → `laserKnockback`, for the prefab's serialized `1.5`; the
    rocket's own fields were never serialized, so its code defaults applied directly.
  - Lock distance doubled, 20m → **40m**, so a rocket can lock across a room.
  - Rocket cooldown label 12pt → **20pt**, now a `labelFontSize` field with the rect height derived
    from it so a larger size cannot clip. Its `GUIStyle` is cached too — it was being rebuilt every
    frame the label was on screen.
  - Impact VFX: `Blaster` already spawned an `impactPrefab` at the hit point — it was simply never
    assigned, like the rocket marker's `indicatorRenderer` before it. Wired to `PistolHit`.
  - **Fixed, and the impact VFX is what exposed it: your own body was eating your own bolts.** The
    raycast took the nearest hit and then merely declined to *damage* the shooter if it turned out
    to be them — but `end = hit.point` was set regardless, so the bolt terminated on your own
    capsule and was blocked there. The gun is held beside a third-person body and aimed along the
    camera, so the ray clips that body routinely, especially while strafing; shots that should have
    reached whoever was behind it were silently absorbed, invisibly until there was an impact
    effect to see. `TryGetFirstHit` now walks a `RaycastNonAlloc` buffer and takes the nearest hit
    that is not the shooter — skipping rather than stopping, since your own collider is not in the
    way of your own gun. Everything else still blocks, so cover is unchanged.
  - **Fixed, and it was the fundamental one: clients could never land a laser shot.** The hit
    raycast ran on the *host*, using the client's origin and direction but the host's own, newer
    positions — so a client aiming dead-on at the enemy it could see missed by however far that
    enemy had travelled during one trip to the host, plus the interpolation delay every remote body
    is rendered behind by. At 30 m/s that is metres. The host, with no trip and no delay, hit every
    time, so the game was playable for exactly one player; and the new aim assist made it *worse*,
    bending the shot precisely onto a position the host no longer agreed with.
    - The shot is now resolved on the shooter, against the world the shooter sees, and the client
      names the player it hit. `AcceptClaim` on the host then decides whether that was possible:
      target alive, not the shooter, muzzle near the shooter's own body, claimed impact within
      `claimTolerance` (10m) of where the host has that player, and no static geometry in between.
      The geometry check is as strong as a host-side raycast, because walls are the part of the
      world every peer agrees on — only players move. A rejected claim recomputes the endpoint
      against geometry, so it cannot still paint an impact on someone's chest.
    - Authority moved from "where was the target" to "could the target have been there", which is
      the only answerable version of the question across four machines. CLAUDE.md's networking
      rules now record this as a deliberate, validated exception rather than a loosening.
    - The shooter also draws its own tracer immediately instead of waiting a round trip for the
      observers RPC; `ShowBolt` gained `excludeOwner` so nobody draws twice, with `runLocally`
      still covering a host that is not the shooter.
    - **The impact is anchored to the hit player's body, not to a world point.** The direct
      consequence of resolving on the shooter: the endpoint is only true on the screen that
      computed it, so on every *other* peer the effect appeared floating beside the victim rather
      than on them — worst of all for the victim, who sees their own body at its real position with
      no interpolation delay at all. `ShowBolt` now carries the hit player plus the offset *from
      that player's body*, and each peer re-anchors onto whatever position it actually has for
      them. The offset is passed through from the shooter rather than recomputed on the host, since
      recomputing it against the host's position would reintroduce exactly the discrepancy it
      exists to cancel. Geometry hits still travel as a world point, which every peer agrees on.
  - Both the doubled lock range and the full-size ring needed **field renames** (`lockRange` →
    `lockDistance`, `reticleScale` → `ringScale`), since the prefab's `lockRange: 20` and
    `reticleScale: 0.4` would otherwise have overridden the new defaults. Fourth time this trap has
    come up; renaming is the only reliable way past it without touching the prefab.
- [x] **Smoke trail on a rocket hit** (`Assets/Domains/VFX/Smoke trail.prefab`, wired to
  `PlayerVitals.launchTrailPrefab`). Attached to the body while it is flying, so a launched player
  reads as launched from across the arena — which the percentage number alone never did.
  - Gated on `force >= trailMinimumForce` (18, matching the movement script's own minimum launch
    speed) rather than on being shot at all. The laser's knockback is 0.2, so without that gate
    every single bolt would puff smoke; with it, the trail means exactly what the launch state
    means. Duration scales with force, capped at 3s.
  - Needed its own `ObserversRpc`, because the launch itself is a `TargetRpc` — only the owner may
    move an owner-authoritative body, so no remote peer simulates the launch state and none of them
    could have derived this locally. The prefab is still spawned locally on each peer from that one
    event, so CLAUDE.md's "don't network VFX" holds.
  - A second hit mid-flight extends the existing trail rather than spawning a second looping
    emitter, and `StopLaunchTrail` stops emission and lets the puffs fade instead of deleting them
    mid-air. Also called from `HandleDied` *before* the body is hidden: the trail is an
    instantiated child, not one of the renderers `SetVisible` toggles, so a corpse would otherwise
    have kept smoking on the spawn point it gets parked on.
- [ ] Character differentiation, if it survives the animation budget (open question).
- [x] **"KILLED BY <name>"** on the victim's screen, in the killer's palette colour, for 4s under
  the banner line. A ring-out with nobody to credit says **OUT OF BOUNDS** instead of blaming a
  player who never touched you — and since percentage never kills, every death is a ring-out, so
  that branch is the "flew out unaided" case rather than an edge.
  - **Fixed on the way: `LastAttacker` was always null on clients.** It read a companion `bool` set
    only inside `AddDamage`, which is server-only, so every other peer returned null however full
    the `SyncVar` beside it was. The host could always name a killer and no client ever could,
    which stays invisible until something tries to tell a victim who shot them. Now derived from
    the SyncVar with id 0 as "nobody", the same convention `Rocket.FiredBy` and the blaster's claim
    already use. No gameplay change — `BlastZone` credits kills on the host, where the old bool was
    correct — but `ResetForNewLife` now clears the SyncVar too, so a client cannot see a stale
    attacker from the previous life.
  - Detected by polling the local player's alive `SyncVar` for a true-to-false edge rather than
    subscribing to `OnDied`: the body carrying that event is destroyed and recreated on every
    respawn, so a subscription would need re-establishing each time and would leak the once it was
    missed. Drawn outside `MatchHud`'s state switch, since you die during `Round` — which draws
    nothing — and the notice has to survive into `RoundEnd` rather than be replaced by the banner.
- [ ] Kill feed, hit confirmation, audio pass, CRT/retro shader pass.
- [ ] Performance at 6 players.

---

## Parked — do not build on

Prototype PvE content in the repo: enemy archetypes (`Assets/Domains/Enemy/` — stealth AI, turret, battleship miniboss with weak points and shield generators), `LevelEvents`, boss logic, `EnemyHealth`. Leave it in place, don't delete it, don't extend it, don't let it pull the versus work off course.

`PlayerBullet.cs` and `Weapon.cs` are PvE-shaped (client-side damage, `EnemyHealth`-only, `Target`-based aim). Treat them as reference, not foundation.
