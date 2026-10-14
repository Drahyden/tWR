# THE WHITE ROOM — Game Design Document (living)

> Source of truth for every chat in the "tWR Game Development" project.
> Each definition gets an ID (D-###). Links between definitions use `→ D-###`.
> Status: 🟢 locked · 🟡 tentative · ⚪ open
> Interview complete (Q1–110). New decisions continue from D-172.

## 0. Index of sections
1. Core identity · 2. Story & world · 3. Player & controls · 4. Combat · 5. Puzzles · 6. Enemies & AI · 7. Level design · 8. Art direction · 9. Audio · 10. UI/UX · 11. Progression & systems · 12. Technical (Unity) · 13. Production & pipeline · 14. Open questions

---

## 1. Core identity
- **D-001** 🟢 Title: *The White Room* → D-010
- **D-002** 🟢 Genre: 3D first-person psychological puzzle shooter with horror → D-012
- **D-003** 🟢 Engine: Unity → D-094
- **D-004** 🟢 Development method: vibecoding with Claude, phase by phase, across multiple chats in this Project
- **D-008** 🟢 Hook: a psychological puzzle FPS about exploring impossible geometry inside a strange liminal space, "The White Room". Back-of-box: *"Daniel opened the black door. On the other side, the lobby of an annoyingly bright place that didn't look familiar. It was The White Room. Only fate knows the horrors that lie ahead."* → D-009, D-010
- **D-011** 🟢 Inspirations and what to take from each:
  - Portal: witty dialogue, creative puzzles → D-024, D-019
  - The Stanley Parable: subverting expectations, surprises → D-030, D-099
  - Doom: dynamic, high-energy combat → D-027, D-040
  - Superliminal: creativity in level building → D-054, D-097
  - F.E.A.R.: the dread of being pursued → D-040, D-043, D-045
- **D-012** 🟢 Pillar balance: Shooting 20 / Horror 40 / Atmosphere 20 / Puzzles 20 → D-002, D-019
- **D-013** 🟢 Horror type: psychological dread dominant; frustration and surrealism; minimal gore (only D-051); a couple of jump scares only → D-018, D-014, D-053, D-081, D-082
- **D-014** 🟢 Emotional arc: start = confused → midpoint = the player has committed to a path (escape vs. discover, D-023, D-060) → end = anxious
- **D-015** 🟢 Run = one complete playthrough, 20–30 min, designed to be replayed for multiple endings → D-025, D-034, D-039, D-059, D-093
- **D-016** 🟢 Platform: PC only (Windows), released on Steam → D-003, D-101
- **D-017** 🟢 Team: solo developer + Claude. No target date; steady progress → D-004, D-007
- **D-018** 🟢 Rating: Mature. No content declared off-limits; gore limited per D-051

## 2. Story & world
- **D-009** 🟢 Protagonist: Daniel → D-008, D-020
- **D-010** 🟢 Setting: "The White Room", an extra-dimensional liminal space with impossible geometry. Entry: a black door opening onto an overly bright, unfamiliar lobby → D-001, D-008, D-021, D-022, D-057, D-064
- **D-020** 🟢 Daniel's character: starts as a blank slate; backstory revealed progressively. A millennial office drone in a low-paying job → D-009, D-026, D-062, D-076
- **D-021** 🟢 The black door: a mystery. The game opens as Daniel is opening it. The White Room is a horror dimension on the other side of an office door → D-010, D-020, D-062, D-066
- **D-022** 🟢 Nature of the White Room: a liminal, abstract dimension with rules; a puzzle box in itself → D-010, D-019, D-041, D-053
- **D-023** 🟢 Goal & central dilemma: escape at any cost **vs.** discover what the White Room is → D-025, D-028, D-014, D-060
- **D-024** 🟢 Narrator: a voice accompanying Daniel that is his inner monologue (Stanley Parable influence) → D-011, D-020, D-026, D-030, D-031, D-056, D-080, D-099
- **D-025** 🟢 Endings: 4 at launch, determined by choice + performance → D-015, D-023, D-028, D-092, D-099
- **D-026** 🟢 Storytelling: environmental details + dialogue exposition through the inner voice → D-024, D-020
- **D-028** 🟢 Ending matrix (2×2): path {Escape, Discover} × performance {Good, Poor} → D-023, D-025, D-029, D-060
- **D-030** 🟢 Inner-voice evolution: starts honest and confused; over the run becomes either clear and resolute, or contradictory and erratic. Drivers: current HP and chosen path → D-024, D-014, D-034, D-060, D-071 (exact mapping ⚪)

## 3. Player & controls
- **D-032** 🟢 Base moveset: walk, sprint, crouch, jump, lean, interact, shoot → D-052, D-079
- **D-033** 🟢 Unlockable abilities via items found during the run: double jump, dash, phase (pass briefly through solid objects) → D-037, D-061; tech: collision/phase
- **D-034** 🟢 Health: HP with a visible HP bar. Death = full restart of the current run; no checkpoints → D-015, D-039, D-079, D-086, D-030
- **D-038** 🟢 Controls: mouse & keyboard only; no key rebinding → D-016, D-053, D-088, D-090
- **D-110** 🟢 Default key map (fixed, no rebinding): WASD move · mouse look · Shift sprint · Ctrl crouch · Space jump · Q/E lean left/right (hold) · F interact · LMB shoot · R reload · 1–5 / wheel switch gun · Esc pause/inventory → D-032, D-038, D-087, D-088, D-115
- **D-111** 🟢 Lean: camera-only offset + roll (no body/hands), collision-checked; sprint cancels lean; allowed while crouched → D-032, D-070
- **D-112** 🟢 Interaction: centre-screen raycast (~2.5 m) to IInteractable; prompt text via localized string table; each interaction raises an event usable by voice triggers and emits an Interact noise → D-032, D-080, D-084, D-091, D-114
- **D-113** 🟢 Player controller tech: Unity CharacterController for the vertical slice, isolated in its own motor component behind `IPlayerMotor` so it can be replaced by a custom kinematic motor when the first gravity-shift room is built; exposes Teleport() for portals and a central input-inversion flag for reversed-control rooms → D-053, D-097, D-033
- **D-115** 🟢 Crouch and sprint are hold-to-use for now; a "toggle crouch/sprint" option is planned for Game settings (a setting, not rebinding) → D-038, D-090, D-110

## 4. Combat
- **D-027** 🟢 The gun: a defensive tool that is also a puzzle element → D-019, D-035, D-036, D-049, D-105
- **D-035** 🟢 Arsenal: start with a pistol; up to 5 guns acquirable during a run → D-049, D-088
- **D-040** 🟢 Combat pacing: quiet exploration broken by short, intense bursts, AND escalating frenzy as the run progresses; mix varies by section. F.E.A.R.-style dread of pursuit → D-011, D-013, D-014, D-075
- **D-049** 🟢 The 5 guns:
  1. Pistol — baseline, starting weapon
  2. Revolver — more damage, less ammo
  3. Shotgun — more damage, slower fire rate
  4. AK — high damage, high fire rate, low accuracy
  5. Scoped rifle — high damage, high precision, slow
  → D-035, D-050, D-070, D-086, D-105
- **D-050** 🟢 Ammo: one shared ammo type for all guns; pickup item held in the inventory → D-037, D-086
- **D-051** 🟢 Enemy death: brief gory animation, then dissolve/disappear. This is the only gore in the game → D-013, D-018
- **D-052** 🟢 Stealth: in some rooms/sections the player may avoid combat through stealth. Avoided enemies do NOT count toward performance (stealth is a trade-off: safer, but lowers the score) → D-029, D-032, D-040, D-079
- **D-117** 🟢 Reload without hands: procedural gun-only animation (gun dips/tilts, magazine mesh slides out of view and back in, gun returns); timings per gun in GunData; switching guns cancels reload → D-070, D-049, D-119
- **D-118** 🟢 Shooting model: hitscan from camera centre for all guns (shotgun = multiple pellet rays later); triggers and the Weapon layer are ignored. Hits resolve to IDamageable (HP holders; raises a killed event with the attacker → D-029) and IShootable (puzzle objects; receive hit point, normal, direction, force, gun and shooter → D-036). "Normal" switches accept any gun; gun-specific switches use an optional required-gun filter (GunRequirement, by gun type) → D-027, D-036, D-105
- **D-119** 🟢 GunData ScriptableObject: localized name key, gun type, damage, fire mode, fire interval, magazine size, range, spread, recoil, reload timings, gunshot noise radius, impact noise radius. Shot = Gunshot noise at the muzzle; hit = Impact noise at the hit point (source = shooter); dry fire is silent. Pistol: 25 dmg, semi-auto, 0.15 s, mag 12, 100 m, gunshot 25 m, impact 6 m → D-049, D-114, D-116
- **D-120** 🟢 Ammo storage: reserve ammo is the shared Ammo item stack in the inventory (cap 99, D-037); loaded rounds are per-gun magazine state kept across gun switches; reload draws from the stack when it completes; an empty trigger pull auto-reloads if reserve > 0. Slice defaults: full mag + 24 reserve at start, pickup +12 → D-050, D-037, D-087
- **D-121** 🟢 Ammo pickups are collected by walking over them (trigger, no prompt); excess beyond the 99 cap stays in the pickup; collecting emits a small Pickup noise (≈2 m) and a picked-up event for voice triggers; placement randomized later per D-058 → D-050, D-058, D-080, D-114

## 5. Puzzles
- **D-019** 🟢 Puzzles are embedded in gameplay and the space itself (not separate challenge rooms) → D-012, D-022, D-027, D-055
- **D-036** 🟢 Gun puzzle interactions: trigger switches, break specific geometry (e.g. windows), knock or move specific objects → D-027, D-105
- **D-055** 🟢 Puzzle types: gun puzzles + physics, pattern, and geometry puzzles → D-019, D-054
- **D-056** 🟢 Hints come from the inner voice (voice only); main-path puzzles cannot be skipped → D-024, D-080, D-104 (⚠️ randomized puzzles D-058 need hint lines per variant or generic hints)
- **D-104** 🟢 Optional puzzles exist in sections; "puzzle completeness" = share of optional puzzles solved → D-029, D-056, D-061
- **D-105** 🟢 Per-gun puzzle roles: Pistol → switches · Revolver → breaks glass · Shotgun → knocks over heavy objects · AK → breaks several targets quickly · Scoped rifle → distant switches → D-036, D-049
- **D-124** 🟢 Puzzle framework: PuzzleData ScriptableObject (puzzle id, isOptional, solve mode AllActive/Sequence, switch behaviour Latch/Toggle, noise radii) + scene PuzzleController that owns all puzzle state and references its switches, start-area trigger and outputs (IPuzzleOutput, e.g. a door lock); in Sequence mode the order is the switch list order and a wrong switch resets all; reused by later puzzles and randomized variants → D-019, D-055, D-058, D-104
- **D-125** 🟢 Shootable switch: IShootable, shoot-only by default (allowInteract flag off; if on, goes through D-112); latches on; state shown by light/emissive only (dark red off, green on); normal hit marker on an accepted hit. Per-switch GunRequirement: empty = normal any-gun switch; the slice puzzle's switches require the Pistol (gun-specific path, D-118). Out-of-reach switches use height or bars, never glass (glass is the revolver's role) → D-036, D-065, D-091, D-105, D-118
- **D-126** 🟢 Puzzle events (static PuzzleEvents): Started (player enters the puzzle-area trigger, once per scene load; fired before any switch event), SwitchActivated, SwitchDeactivated (Toggle mode), Reset (wrong order in Sequence mode), Solved(puzzleId, isOptional). Hooks for voice triggers, the future run-stats tracker and music state; a "first seen" start condition is added once the shared is-observed check exists → D-029, D-045, D-053, D-075, D-080, D-104
- **D-127** 🟢 Locked exit door: locked until its puzzle is solved; on solve it unlocks audibly with a red → green lock light and is opened by the player with F; while locked F shows a localized "Locked" prompt and raises an interaction event. Door settings in DoorData. Locking is separate from where the door leads (later D-053b / D-058) → D-053, D-058, D-084, D-112
- **D-130** 🟢 Main-path puzzles must stay solvable with resources the run guarantees: the slice puzzle uses 3 switches and has an ammo pickup inside its room, so missed shots can't softlock it → D-056, D-120, D-121

## 6. Enemies & AI
- **D-041** 🟢 Enemy fiction: aberration monsters and dimensional demons → D-022, D-013, D-067
- **D-042** 🟢 Roster: 4 common types + 2 bosses → D-043–D-048
- **D-043** 🟢 Seeker: patrols; reacts to sound and movement; melee at close range → D-052, D-079; tech: hearing/noise system
- **D-044** 🟢 Hunter: keeps its distance; if it sees the player for >3 s, attacks with ranged projectiles → tech: line-of-sight timer
- **D-045** 🟢 Lurker: avoids the player's front view; sneaks around to the back; melee at close range → tech: view-cone check (shared with D-053)
- **D-046** 🟢 Boomer: rushes the player and self-destructs for high damage
- **D-047** 🟢 Boss 1: teleports randomly around the arena; ranged attacks → D-068, D-106
- **D-048** 🟢 Boss 2: very large; charges and tramples like a bull → D-069, D-106
- **D-106** 🟢 Boss placement: one boss per path (Escape / Discover) → D-023, D-060 (which boss on which path → D-148)
- **D-148** 🟢 Boss per path: Escape → Boss 2, the bull (D-048, D-069); Discover → Boss 1, the preacher (D-047, D-068). Resolves D-106 → D-023, D-028, D-060, D-106
- **D-132** 🟢 Seeker senses: hearing via D-114/D-131 (ignores enemy-sourced noise) + motion-gated sight — "reacts to movement" (D-043) means sight (confirmed by developer): 110° cone + line of sight to head/chest at ~5 Hz; moving player seen ≤15 m, crouch-moving ≤6 m, still ≤2.5 m; awareness builds over ~0.5 s; toggle `useSight` in SeekerData. Cone/LOS geometry is the shared TWR.Core.Perception.ViewCone helper, reused later by the is-observed system → D-043, D-045, D-052, D-053
- **D-133** 🟢 Seeker states: Patrol (waypoints, 1.6 m/s, 1–2 s pauses) → Suspicious (footstep/jump/land/interact/pickup: stop and turn 1 s) → Investigate (2.5 m/s; gunshot/impact/mechanism or a repeated small noise; 4 s look-around) → Chase (4.5 m/s, between player walk and sprint; on sight, noise ≤3 m, or being damaged) → Attack → Search (5 s without sight → last known position, random points within 6 m for 8 s) → Return (nearest waypoint). Per-noise-type sensitivity in SeekerData; state shown by a coloured band; raises EnemyEvents.AwarenessChanged for music/voice → D-040, D-043, D-075, D-080
- **D-134** 🟢 Seeker combat (SeekerData SO): 100 HP (4 pistol hits); melee 20 dmg, 1.8 m range, 0.4 s telegraphed wind-up (lean + flash + sound; hit lands only if the player is still in range and in front), 1.2 s cooldown. Enemies live on a shootable "Enemy" layer; the player root is on a "Player" layer → D-034, D-118, D-119, D-120, D-130
- **D-135** 🟢 Enemy navigation: Unity AI Navigation NavMeshSurface baked and saved as an asset by the scene builder (agent radius 0.4, height 2); door panels carve with a NavMeshObstacle, so closed doors block paths; enemies cannot open doors in the slice; enemies spawn from spawn points (SeekerSpawnPoint) for later D-058 randomization → D-058, D-100, D-127
- **D-136** 🟢 Enemy death (slice placeholder): marked dead immediately (kill hit marker), Killed raised once with the attacker, AI/agent/colliders off at once; 0.6 s collapse with darker tint + 1.0 s shrink, then destroyed; nothing persists. The final gory animation (D-051) plugs into the DeathStarted hook → D-013, D-051, D-122
- **D-138** 🟢 Enemy audio and noise: enemies ignore enemy-sourced noise; enemy footsteps are 3D audio only (never noise events); per-type sound set SO (idle loop, footsteps, alert, attack wind-up, hurt, death) with null-safe slots; generated placeholder tones until real clips exist (licence log per D-083) → D-078, D-083, D-114
- All enemy types have their own sound set → D-078

## 7. Level design
- **D-053** 🟢 Rules of the White Room: (a) elements change when not observed; (b) the same door can lead to different locations; (c) gravity shifts in specific rooms; (d) some rooms reverse the controls → D-022, D-038, D-045; tech: shared "is observed" system
- **D-054** 🟢 Impossible geometry palette: rooms bigger on the inside, looping corridors, path-shifting labyrinths, seamless doors to strange locations; Superliminal-style creativity in level building → D-010, D-011, D-055, D-097
- **D-057** 🟢 Run structure: hub-and-branch. Every run starts in the hub lobby, which has several doors → D-010, D-060
- **D-058** 🟢 Layout: the starting lobby is fixed. Randomized per run: door destinations, enemy placement, item & ammo locations, room order, puzzles → D-039, D-053, D-056; tech: seeded randomization
- **D-059** 🟢 Each run has 4–6 rooms/sections (≈4–6 min each) → D-015, D-040
- **D-060** 🟢 Path choice is made by choosing which door to go through → D-023, D-028, D-057, D-106
- **D-061** 🟢 Unlockable abilities are not required to progress; they give access to secrets, shortcuts, optional puzzles, and combat advantages → D-033, D-104
- **D-145** 🟢 Slice hub lobby: a bright area (emissive white + bloom, office/home props) in the test scene, physically connected to the test room/puzzle/Seeker area by an open doorway; the player starts here in front of the black entry door (D-021). Doors: the black door (permanently locked), the doorway to the existing rooms (D-103), the portal door, and two permanently locked doors reserved for the D-060 path doors. Locked doors reuse D-127 with a "permanently locked" mode that still shows "Locked" and raises interaction events → D-010, D-021, D-057, D-058, D-060, D-062, D-064, D-127
- **D-146** 🟢 Slice impossible space: the portal door is a freestanding frame-and-wall segment in the lobby that the player can walk around (nothing behind it; one-sided). Its D-127 panel is unlocked, opens with F and swings toward the lobby; the portal renders only while it is open. Through it: a ~30 × 20 × 8 m white hall with office/home props, far bigger than the space it stands in. Two-way: an always-open return portal in the hall leads back. The hall contains a shootable test target and an F test button; nothing required → D-054, D-062, D-064, D-097
- **D-147** 🟢 Portal destinations are data: each portal has a serialized destination (its linked portal) plus SetDestination(), separate from door locking (D-127), so D-053b / D-058 can assign destinations from the run seed later → D-053, D-058, D-127
- **D-149** 🟢 Path choice location: a dedicated fork room after section 2 or 3, around the run's midpoint, where the player commits to Escape or Discover by choosing a door → D-014, D-023, D-060, D-151
- **D-150** 🟢 Fork doors: the Escape door has a clearly readable "EXIT" sign above the frame; the Discover door is a black door (echoing the entry door, D-021) with a rectangular white plate on its upper middle reading "BASEMENT". Both texts are localized (D-084); the inner voice comments on both doors (D-080) → D-021, D-023, D-060, D-080, D-084, D-149
- **D-151** 🟢 Run structure: 2–3 sections before the fork room, then 2 after it (the path section + the boss arena), for 4–6 sections in total (D-059) → D-015, D-057, D-059, D-106, D-148, D-149
- **D-154** 🟢 First run: the very first run (no endings saved yet, D-099) uses a fixed, curated layout; seeded randomization (D-058, D-153) starts from run 2 → D-039, D-058, D-099, D-153
- **D-155** 🟢 Section templates for now (placeholder geometry): puzzle room (existing), combat room, stealth room (D-052), impossible-geometry room using the portal (D-139–D-147), silent room (D-082); plus the fork room (D-149) and two placeholder boss arenas (D-148) → D-052, D-054, D-055, D-058, D-082, D-097, D-151
- **D-156** 🟢 Section count: the hub lobby (D-057) and the fork room (D-149) are connectors, not sections. A run plan has 2–3 sections before the fork + path section + boss arena = 4–5 sections; D-059's upper bound of 6 is headroom. Path section is drawn from templates tagged path=none or the chosen path; boss arenas are path-tagged only (D-148). Both post-fork branches are generated from the seed at run start and the active one is fixed when the player picks a fork door → D-059, D-060, D-148, D-149, D-151, D-155
- **D-157** 🟢 Run reset: death (D-034) and quitting (D-093) end the run, discard the run plan and return to the hub lobby with a new plan (new seed, or the curated plan while no endings are saved). Supersedes "death = scene reload" in D-122 and D-129; section scene state (puzzles, enemies) still resets whenever a section loads → D-034, D-039, D-093, D-122, D-129, D-154
- **D-158** 🟢 RunStats is run-scoped (supersedes "scene-scoped" in D-137): resets at run start, persists across section loads, counts player-credited kills, enemies spawned and optional puzzles solved/available in sections actually loaded this run; bosses excluded from both kill numbers; live score per D-152; a term with a zero denominator scores its full 50 → D-029, D-052, D-104, D-137, D-152
- **D-159** 🟢 Run seed format and first run: 32-bit unsigned, non-zero for random runs, shown as 8 hex characters; seed 0 is reserved for the curated first-run plan (D-154), stored as an authored plan asset, not generated. "No endings saved" is read through an endings-record interface (stub returns 0 until D-099 exists). Debug overlay (run plan, seed, section, score, skip-section, seed entry) exists only in editor/development builds and is exempt from localization (D-084) → D-084, D-099, D-153, D-154
- **D-160** 🟢 Curated first run: puzzle room → combat room → impossible-geometry room → fork room → path section (Escape: stealth room; Discover: silent room) → boss arena per D-148 → D-154, D-155
- **D-161** 🟢 Scene architecture: a persistent Core scene (player, HUD, RunManager, ambient bed, lighting/global volume; Core stays the active scene) plus exactly one content scene loaded additively (Lobby, one per section template, Fork, two boss arenas). No cross-scene serialized references; portals, HUD and RunStats bind to the player at runtime. Each section scene bakes its own NavMesh. Test_PlayerController stays a standalone sandbox → D-097, D-135, D-139, D-141, D-144, D-155, D-157
- **D-162** 🟢 Within-run carry-over: HP, magazine and reserve ammo, guns, inventory and abilities persist across section loads; no automatic HP refill between sections; HP is restored only by health pickups (D-171); everything resets to run-start defaults (D-120) at run start, on death and on quit → D-030, D-034, D-037, D-039, D-079, D-120, D-157, D-171
- **D-163** 🟢 Run lobby scene: built from the slice lobby (D-145) without the white movement room (sandbox only). The open doorway becomes an unlocked "run door" (D-127) leading to section 1; the black door stays permanently locked; the portal door and impossible hall stay as an optional lobby curiosity (D-146); the two locked doors stay permanently locked. Their D-060 path-door reservation is superseded by D-149/D-150, and they may later become extra run doors per D-053b/D-058. Player spawns in front of the black door (D-021) → D-021, D-053, D-057, D-058, D-145, D-146, D-149
- **D-164** 🟢 Impossible-geometry section: its own portal pair and hall (same components as D-146, ≥200 m apart, separate NavMesh per D-144); the section exit is inside the hall, so crossing the portal is required; the return portal stays → D-054, D-140, D-144, D-146, D-147, D-155
- **D-165** 🟢 Section flow: the player enters at an entry spawn in a vestibule; sections are one-way (the entry door behind the player uses D-145's permanently-locked mode). The transition fires when the player passes through the open exit door into a trigger behind it; the transition is a separate component from the door lock (D-127). SectionData exit condition: None / MainPuzzlesSolved (isOptional = false only; optional puzzles never gate, D-104) / BossDefeated. Defaults: puzzle = MainPuzzlesSolved; combat, stealth, silent, impossible-geometry = None (combat exits never lock; combat differs from stealth by enemy count/density); boss arena = BossDefeated (stub) → D-052, D-053, D-056, D-104, D-127, D-129, D-157, D-158
- **D-166** 🟢 Fork room commit: the path is committed (ChoosePath) when the player passes through a fork door; opening a door does not commit; the other door then locks. The EXIT and BASEMENT texts are world-space localized strings; the inner-voice comments are voice-trigger events with placeholder localized subtitle keys and no audio yet → D-080, D-084, D-089, D-150, D-156
- **D-167** 🟢 Section transition: fade to white (~0.4 s), input frozen, async unload/load, teleport via IPlayerMotor.Teleport (D-113) to the entry spawn, fade in; the ambient bed in Core keeps playing; raises SectionStarted/SectionEnded events for voice and music (D-080, D-075). Boss arena exit = run complete: placeholder end screen (D-092), then return to the lobby with a new plan as in D-157 → D-054, D-064, D-075, D-080, D-092, D-113, D-141, D-157
- **D-168** 🟢 Silent room minimum: on entry, music and the ambient bed (including the fluorescent hum) fade to silence via a mixer snapshot and inner-voice triggers are suppressed; player, gun and enemy SFX stay; gameplay noise events are unaffected; the exit is never locked → D-075, D-077, D-080, D-082, D-114
- **D-169** 🟢 Stealth room minimum: at least 2 patrolling Seekers covering the main route plus a crouch/cover route to the exit; the exit is never locked on kills; avoided enemies still count as spawned → D-052, D-132, D-133, D-158
- **D-170** 🟢 Enemy placement in sections uses fixed SeekerSpawnPoints for now; seeded selection (a per-section random generator built from run seed + section index) comes in a later step; seed 0 (curated) always uses fixed placement → D-058, D-135, D-153, D-154, D-159
- **D-171** 🟢 Health pickups: the only in-run HP source (D-162). A placeholder health pickup (reuse the existing ItemPickup/ItemData pickup flow; heal amount in a ScriptableObject, default 25; not consumed when HP is full; can't exceed max HP) placed at fixed points in some sections (e.g. one in combat, one in stealth, one in the fork room); seeded placement later with D-170 → D-034, D-037, D-120, D-162
- Silent rooms are part of the level palette → D-082

## 8. Art direction
- **D-005** 🟢 3D assets produced in Blender and/or AI 3D tools (Tripo, Meshy) → D-072
- **D-062** 🟢 Visual palette: mostly white, with random office or home motifs scattered through the levels → D-010, D-020
- **D-063** 🟢 Style: realistic, with aberration-horror elements → D-041, D-067
- **D-064** 🟢 The white: overexposed, glowing white ("annoyingly bright") → D-008, D-077; tech: emissive surfaces + bloom
- **D-065** 🟢 Lighting: from the walls themselves or from light-source objects. Dark cavities signal danger and may be portals or enemy spawn points → D-053, D-054, D-042
- **D-066** 🟢 Opening: the real office is only briefly visible in the opening animation; not playable → D-021
- **D-067** 🟢 Aberration look: fleshy beige-to-white skin, visible bone structure, lean muscles, pitch-black marble-like eyes; every type is a body variation tailored to its behaviour. Camouflage against white walls is intentional → D-041, D-043–D-048
- **D-068** 🟢 Boss 1 look: preacher-like humanoid, elongated limbs, four arms → D-047 (⚠️ custom rig — Blender task)
- **D-069** 🟢 Boss 2 look: fleshy aberration bull → D-048 (⚠️ quadruped rig — Blender task)
- **D-070** 🟢 First-person view: only the equipped gun is visible (no hands); classic real-world gun looks → D-049 (reload: → D-117; interaction animations without hands ⚪)
- **D-071** 🟢 Erratic-voice effect: vision blurs and light decreases → D-030, D-064; tech: post-processing volume driven by voice state
- **D-072** 🟢 Asset strategy: maximize AI 3D tools (developer is a Blender beginner); Blender only for targeted fixes (cleanup, boss rigs). Architecture built in Unity (ProBuilder) → D-005

## 9. Audio
- **D-006** 🟢 Music & audio generated with SUNO; stems/processing with FADR → D-075, D-076
- **D-031** 🟢 Inner-monologue voice-over recorded locally by the developer → D-024, D-084
- **D-075** 🟢 Adaptive music: exploration / combat / puzzle states → D-040, D-019; tech: music state machine, stem crossfades
- **D-076** 🟢 Music style: dark ambient horror + industrial synth + distorted bossa nova (ironic office "muzak") → D-006, D-020
- **D-077** 🟢 Ambient bed: fluorescent hum; sound effects with high reverb → D-064; tech: reverb zones / mixer
- **D-078** 🟢 Each enemy type has its own sound effects → D-043–D-048, D-052
- **D-079** 🟢 Player sounds: footsteps, interaction, jump, louder sprint; heartbeat at low HP → D-032, D-034, D-043
- **D-080** 🟢 Inner-voice triggers: start/end of rooms, specific object interactions, start/end of puzzles → D-024, D-056; tech: event-driven dialogue triggers
- **D-081** 🟢 Jump scares use sharp audio stingers → D-013
- **D-082** 🟢 Some rooms are completely silent → D-013, D-075
- **D-083** 🟡 SFX sources: Sonniss GDC bundles, Kenney CC0, Freesound (CC0 filter) + paid-tier AI SFX for creature sounds + own recordings; keep a license log → D-078
- **D-084** 🟢 Localization: VO in English first; Spanish VO in a later sprint. Build localization-ready from day one → D-031, D-089

## 10. UI/UX
- **D-085** 🟢 Main menu: classic menu screen
- **D-086** 🟢 HUD: HP, ammo, current gun → D-034, D-049, D-050
- **D-087** 🟢 Inventory: simple grid inside the pause menu (game paused) → D-037
- **D-088** 🟢 Gun switching: number keys or mouse wheel → D-035, D-038
- **D-089** 🟢 Subtitles: optional, toggled in Settings → D-084
- **D-090** 🟢 Settings menu: Graphics, Controls, Sound levels, Game settings. No key rebinding → D-038 (FOV / head bob / motion blur toggles ⚪)
- **D-091** 🟢 Feedback cues: interaction prompts + hit markers (damage direction indicator ⚪)
- **D-092** 🟢 End-of-run screen: run stats + a unique graphic per ending → D-025, D-028, D-029
- **D-093** 🟢 Quitting mid-run exits and resets the run → D-034, D-039
- **D-122** 🟢 Slice HUD: HP bar (PlayerHealth, max HP in PlayerTuning; death = scene reload per D-034), ammo "mag / reserve" via localized format string, current gun name via string table, centre crosshair dot, hit marker (stronger on kill). No damage direction indicator yet → D-086, D-091, D-084, D-034

## 11. Progression & systems
- **D-029** 🟢 Performance metric = optional-puzzle completeness (D-104) + enemies killed (avoided enemies don't count, D-052) → D-028, D-092 (formula → D-152)
- **D-037** 🟢 Inventory: up to 20 item types, max stack 99 each → D-033, D-050, D-087
- **D-039** 🟢 No in-game meta-progression; only player knowledge carries over. Exception: endings seen are saved outside the run (D-099), so the inner voice can react on repeat playthroughs → D-015, D-034, D-058
- **D-129** 🟢 Puzzle state lives only in the scene for the current run; a scene reload (death, D-122) or quit (D-093) resets it; nothing is saved (D-099). Variant selection will come from the run seed (D-058) → D-034, D-039, D-058, D-093
- **D-137** 🟢 RunStats (minimal, scene-scoped): counts player-credited kills of real enemies (CombatEvents.Killed, attacker = player), registered enemies, and puzzles solved/total incl. optional (PuzzleEvents.Solved); resets on reload; debug log only, no score formula yet → D-029, D-052, D-092, D-104, D-126, D-129
- **D-152** 🟢 Performance score (0–100) = (kills ÷ enemies spawned in the run) × 50 + (optional puzzles solved ÷ optional puzzles available) × 50; Good ≥ 60, otherwise Poor (D-028 ending matrix). Resolves the D-029 weighting question; computed from RunStats (D-137) → D-028, D-029, D-052, D-092, D-104, D-137
- **D-153** 🟢 Run seed: every run has a seed that drives its randomization (D-058); shown small on the end-of-run screen (D-092); a seed can be entered only in a debug menu → D-058, D-092, D-147, D-154

## 12. Technical (Unity)
- **D-073** 🟢 Graphics target: standard (non-4K), mid-range PCs → D-016, D-100
- **D-074** 🟢 Render pipeline: URP → D-064, D-071, D-097
- **D-094** 🟢 Unity version: Unity 6.3 LTS, editor 6000.3.21f1 (latest LTS line; supported until Dec 2027). Template: Universal 3D → D-003
- **D-095** 🟢 Developer coding level: mid-beginner; understands structure and purpose. Claude writes code and explains intent
- **D-096** 🟢 Version control: Git + GitHub (account and repo exist). Claude gives specific, direct step-by-step commit instructions; Git LFS for large binaries → D-109
- **D-097** 🟢 Impossible geometry tech: see-through portals (⚠️ cap simultaneous visible portals and recursion to hold D-100) → D-054
- **D-099** 🟢 Save data: settings + endings achieved → D-039, D-093
- **D-100** 🟢 Performance target: 60 FPS on mid-range PCs → D-073, D-097
- **D-101** 🟢 Distribution: Steam (Steamworks integration later) → D-016
- **D-102** 🟢 Development PC: Windows · Intel i7-9700KF · NVIDIA RTX 3060 12 GB · 16 GB RAM → D-007 (16 GB is workable; avoid running Unity + Blender + heavy tools at once)
- **D-109** 🟢 Repository layout: Git repo at `C:\Dev\tWR` (root holds CLAUDE.md, docs/, .claude/, .gitignore, .gitattributes); Unity project in subfolder `TheWhiteRoom/`. Claude Code is always started from the repo root → D-096, D-094
- **D-114** 🟢 Shared noise-event system (TWR.Core): global emit(position, radius, type, source) + listener registration. Player sources: crouch ≈ near-silent (small radius), walk = low, sprint = high, jump + landing (landing scaled by fall speed) = medium; later gunshots/impacts. Radius-only in step 1; wall muffling per D-116 → D-043, D-052, D-079
- **D-116** 🟢 Noise and walls: walls muffle noise (reduce its effective radius) rather than fully blocking it. Implement when building the Seeker → D-043, D-052, D-114
- **D-123** 🟢 First-person gun rendering: weapon holder parented to the player camera (inherits lean, D-111); gun on a "Weapon" layer drawn on top via URP Render Objects renderer features (no overlay camera); Weapon layer excluded from shoot/interact raycasts → D-070, D-074, D-100
- **D-128** 🟢 Mechanism noise type (D-114): switch activation ≈ 4 m at the switch (source = shooter), door unlock ≈ 8 m and door open ≈ 10 m at the door (source = player for the open); values in PuzzleData / DoorData; walls muffle per D-116 → D-043, D-114, D-116
- **D-131** 🟢 Noise muffling (implements D-116): NoiseSystem checks only listeners inside the raw radius and casts one ray from the source (+0.3 m) to each listener on an occluder mask (Default + Interactable; not Player/Enemy/Weapon/Ignore Raycast; own colliders skipped); effective radius = radius × product of per-wall factors (default 0.5; NoiseOccluder override: thin 0.6, thick 0.3, door panel 0.6; minimum 0.1 per wall and in total; max 4 walls counted); open doors don't muffle because the panel is out of the ray path. Values in a NoiseSettings SO loaded from Resources → D-100, D-114, D-116, D-128
- **D-139** 🟢 Portal rendering: render-texture portals (not stencil) in URP 17. One standalone Base camera per portal (never overlay/stack, D-123) renders the destination side into a runtime-allocated HDR render texture (never saved) before the main camera; its pose and an oblique near-plane projection (clips everything between it and the destination portal; skipped within ~0.05 m) are set in LateUpdate after look/lean/crossing. The portal surface is an unlit screen-space shader (PortalSurface.shader). Portal cameras use a dedicated lightweight "Portal" Universal Renderer (no Render Objects features, no depth priming) added to the URP renderer list → D-074, D-097, D-100, D-123
- **D-140** 🟢 Portal budget (PortalSettings SO): a portal renders only while its door is open (or opening), the camera is in front of it and it is inside the view frustum; otherwise its camera is disabled. maxRenderedPortals = 2 (nearest first; over-budget portals show a flat emissive-white fallback). Recursion cap = 0: live portal screens are on a "Portal" layer only the player camera draws, and each portal has a flat white quad on a "PortalFallback" layer only portal cameras draw, so a portal seen inside a portal view is always flat white. resolutionScale 1.0 by default (0.5–1.0; future Graphics setting, D-090) → D-090, D-097, D-100
- **D-141** 🟢 Portal look, gun and HUD: portal cameras copy the player camera's FOV, aspect, clip planes and full rotation (incl. lean roll) every frame, so they follow the FOV setting automatically; HDR with post-processing off, so bloom/tonemapping are applied once by the main camera (no double bloom); global volumes only. Portal cameras never draw the Weapon, Player, Ignore Raycast or Portal layers, so the first-person gun is drawn only by the player camera's Weapon features (D-123) and stays on top while crossing; the HUD is a screen-space overlay and is unaffected. A 2D ambient bed plays across connected spaces so crossing has no audio pop → D-064, D-077, D-090, D-111, D-122, D-123
- **D-142** 🟢 Portal crossing: one-sided portals (front face only); PortalTraveller on the player watches the un-leaned head pivot and, in the same frame it crosses the portal plane inside the opening, teleports via PlayerController.Teleport → IPlayerMotor.Teleport (D-113) with feet position and camera rotation mapped through the pair; portal pairs must be upright and differ only by yaw; view direction and momentum turn with the pair. Within ~0.3 m of the plane the portal screen is drawn as a thin box (~0.25 m deep) behind the plane to hide near-plane clipping. Each exit has a clear vestibule ≥1 m deep at the same floor height; openings ≥1.2 × 2.4 m (slice: 1.4 × 3.0 m); lean treats the portal plane as a blocker. Raises PortalEvents.Crossed(portalId, from, to) for voice/room triggers → D-080, D-111, D-113
- **D-143** 🟢 Rays through portals: a shared one-hop PortalRaycast continues gun hitscan (D-118) and interaction (D-112; 2.5 m total across both legs) through one open portal, so shooting a target seen through a portal hits it (damage, IShootable, hit marker) and impact noise is emitted at the real hit point on the far side; no walk-over pickups within 1.5 m of a portal → D-112, D-118, D-121
- **D-144** 🟢 Noise and enemies don't cross portals (for now): portal-connected spaces are physically ≥200 m apart (slice hall ≈500 m), beyond every noise radius and audio max distance; their NavMeshes are not connected (builder verifies); Seeker sight and hearing stay physical, so an enemy chasing the player goes to the last known position on its side. Revisit per §14 → D-114, D-131, D-132, D-135

## 13. Production & pipeline
- **D-007** 🟢 Early phases in claude.ai browser; switch to desktop (Claude Code + local Unity project) for scripting → D-096, D-102
- **D-103** 🟢 First playable milestone (vertical slice): hub lobby + one room + one enemy + pistol + one puzzle → D-057, D-049
- **D-107** 🟢 Vertical slice contents (confirmed): Seeker (D-043) + pistol switch puzzle (D-105) + one see-through portal door in the lobby to test the riskiest tech early (D-097). Build order: player controller + shared noise-event system (D-032, D-079) → pistol (D-049) → switch puzzle (D-105) → Seeker (D-043) → portal door (D-097) → D-103
- **D-108** 🟢 Agents/skills: `twr-asset-prompts` skill (claude.ai); Claude Code subagents `unity-scripter` and `gdd-guardian` (in repo `.claude/agents/`)

---

## Interview log
| Q# | Question | Answer | → Definitions |
|----|----------|--------|---------------|
| 1 | The hook | Psychological puzzle FPS; impossible geometry; liminal space; back-of-box text | D-008, D-009, D-010 |
| 2 | Meaning of title | The setting | D-010 |
| 3 | Inspirations | Portal, Stanley Parable, Doom, Superliminal, F.E.A.R. | D-011 |
| 4 | Pillar balance | Shooting 20 / Horror 40 / Atmosphere 40 (later refined in Q11/Q102 → D-012) | D-012 |
| 5 | Horror type | Psychological dread, frustration, surreal, minimal gore, few jump scares | D-013 |
| 6 | Emotional arc | Confused → anxious | D-014 |
| 7 | Length | 15–30 min per run | D-015 |
| 8 | Platform | PC only | D-016 |
| 9 | Scope & team | Solo + Claude | D-017 |
| 10 | Rating | Mature | D-018 |
| 11 | Puzzles in split | Embedded; 50% of atmosphere | D-012, D-019 |
| 12 | What is a run | Full playthrough, 20–30 min, replayed for endings | D-015 |
| 13 | Who is Daniel | Blank slate; millennial low-paid office drone | D-020 |
| 14 | The black door | Mystery; horror dimension behind an office | D-021 |
| 15 | Nature of the room | Abstract dimension with rules; puzzle box | D-022 |
| 16 | Daniel's goal | Escape vs. discover | D-023 |
| 17 | Endings | 4, choice + performance | D-025 |
| 18 | Other voices | Inner monologue narrator | D-024 |
| 19 | Storytelling | Environment + inner voice | D-026 |
| 20 | Why a gun | Defence + puzzle tool | D-027 |
| 21 | Ending matrix | 2×2 accepted | D-028 |
| 22 | Performance | Puzzle completeness + enemies defeated | D-029 |
| 23 | Inner voice | Honest → resolute or erratic | D-030 |
| 24 | Voice production | Recorded locally | D-031 |
| 25 | Movement | Base moveset + unlockable double jump, dash, phase | D-032, D-033 |
| 26 | Health & death | HP bar; death restarts run | D-034 |
| 27 | Guns | Pistol → up to 5 | D-035 |
| 28 | Gun as puzzle tool | Switches, break geometry, move objects | D-036 |
| 29 | Inventory | 20 types × 99 | D-037 |
| 30 | Controls | Mouse & keyboard | D-038 |
| F | Follow-ups | Midpoint = path decided; inspirations; no date | D-014, D-011, D-017 |
| 31 | Death penalty | Full run restart | D-034 |
| 32 | Carry-over | Only player knowledge | D-039 |
| 33 | Doom vs dread | Quiet + bursts, escalating | D-040 |
| 34 | Enemy fiction | Aberrations, dimensional demons | D-041 |
| 35 | Variety | 4 common + 2 bosses | D-042 |
| 36 | Behaviours | Seeker, Hunter, Lurker, Boomer, 2 bosses | D-043–D-048 |
| 37 | Five guns | Pistol, Revolver, Shotgun, AK, Scoped rifle | D-049 |
| 38 | Ammo | Pickup item | D-050 |
| 39 | Enemy deaths | Gory, then dissolve | D-051 |
| 40 | Combat & endings | Stealth possible in some sections | D-052 |
| 41 | Room rules | Unobserved shifts, doors, gravity, reversed controls | D-053 |
| 42 | Impossible geometry | Bigger inside, loops, labyrinths, seamless doors | D-054 |
| 43 | Other puzzles | Physics, pattern, geometry | D-055 |
| 44 | Hints & skipping | Voice hints; no skipping | D-056 |
| 45 | Run structure | Hub lobby with doors | D-057 |
| 46 | Same vs varied | Fixed start + randomized parts | D-058 |
| 47 | Sections | 4–6 per run | D-059 |
| 48 | Path choice | Door choice | D-060 |
| 49 | Ability gating | Not required | D-061 |
| 50 | Visual zones | White + office/home motifs | D-062 |
| 51 | Style | Realistic + aberration horror | D-063 |
| 52 | The white | Overexposed glowing | D-064 |
| 53 | Lighting | Walls/objects; dark = danger | D-065 |
| 54 | Office opening | Intro animation only | D-066 |
| 55 | Enemy look | Fleshy beige-white, bones, black eyes | D-067 |
| 56 | Bosses look | Four-armed preacher; fleshy bull | D-068, D-069 |
| 57 | First person | Gun only | D-070 |
| 58 | Screen effects | Erratic voice → blur + darker | D-071 |
| 59 | Asset split | Maximize AI tools | D-072 |
| 60 | Graphics target | Mid-range, non-4K | D-073 |
| 61 | Music role | Adaptive, 3 states | D-075 |
| 62 | Music style | Dark ambient, industrial synth, distorted bossa nova | D-076 |
| 63 | Ambience | Fluorescent hum, reverb | D-077 |
| 64 | Enemy audio | Unique per type | D-078 |
| 65 | Player sounds | Footsteps, sprint, jump, heartbeat | D-079 |
| 66 | Voice triggers | Rooms, objects, puzzles | D-080 |
| 67 | Jump scares | Stingers | D-081 |
| 68 | Silence | Silent rooms | D-082 |
| 69 | SFX sources | Recommendations requested | D-083 |
| 70 | Language | English first, Spanish later | D-084 |
| 71 | Main menu | Classic | D-085 |
| 72 | HUD | HP, ammo, gun | D-086 |
| 73 | Inventory screen | Grid in pause menu | D-087 |
| 74 | Gun switching | Number keys / wheel | D-088 |
| 75 | Subtitles | Optional | D-089 |
| 76 | Settings | Graphics, Controls, Sound, Game | D-090 |
| 77 | Hint display | Voice only | D-056 |
| 78 | End screen | Stats + ending graphic | D-092 |
| 79 | Quitting mid-run | Resets run | D-093 |
| 80 | Feedback cues | Prompts + hit markers | D-091 |
| 81 | Unity version | Current LTS at dev start → 6000.3.21f1 | D-094 |
| 82 | Render pipeline | URP | D-074 |
| 83 | Coding level | Mid-beginner | D-095 |
| 84 | Version control | GitHub; direct commit steps | D-096 |
| 85 | Portals | See-through | D-097 |
| 86 | Save data | Settings + endings | D-099 |
| 87 | Frame rate | 60 FPS | D-100 |
| 88 | Distribution | Steam | D-101 |
| 89 | Dev PC | Windows | D-102 |
| 90 | First milestone | Vertical slice | D-103 |
| 91 | Gore scope | Brief gory death then dissolve; only gore | D-051 |
| 92 | Stealth vs performance | Avoided enemies don't count | D-052, D-029 |
| 93 | Puzzle completeness | Optional puzzles | D-104 |
| 94 | Abilities' purpose | Secrets, shortcuts, optional puzzles, combat | D-061 |
| 95 | Ammo | One shared type | D-050 |
| 96 | Per-gun puzzles | Yes, one puzzle kind per gun | D-105 |
| 97 | Boss placement | One boss per path | D-106 |
| 98 | Voice drivers | HP and path | D-030 |
| 99 | Randomization | Doors, enemies, items/ammo, room order, puzzles | D-058 |
| 100 | Camouflage | Intended | D-067 |
| 101 | Comfort | No key rebinding | D-038, D-090 |
| 102 | Loose ends | Pillars confirmed; Superliminal level creativity; F.E.A.R. pursuit dread; nothing off-limits; PC specs | D-012, D-011, D-018, D-102 |
| 103 | Boss per path | Escape → Boss 2 (bull); Discover → Boss 1 (preacher) | D-148, D-106 |
| 104 | Path choice location | Dedicated fork room after section 2 or 3, around the midpoint | D-149 |
| 105 | Fork doors | Escape: "EXIT" sign above the frame; Discover: black door with a white "BASEMENT" plate; localized; inner voice comments on both | D-150 |
| 106 | Run length | 2–3 sections before the fork, then 2 after (path section + boss arena); 4–6 total | D-151, D-059 |
| 107 | Performance | Score = kills/spawned × 50 + optional puzzles solved/available × 50; Good ≥ 60/100 | D-152, D-029 |
| 108 | Seed | Each run has a seed; shown small on the end screen; enterable only in a debug menu | D-153 |
| 109 | First run | Very first run is fixed and curated; randomization from run 2 | D-154 |
| 110 | Section templates | Puzzle, combat, stealth, impossible-geometry (portal), silent room; fork room; two boss arenas | D-155 |

## 14. Open questions
- Exact HP/path → inner-voice mapping (D-030)
- Hint lines for randomized puzzles: per-variant or generic (D-056, D-058)
- Interaction animations with no visible hands (D-070)
- FOV / head bob / motion blur toggles; damage direction indicator (D-090, D-091)
- Final SFX source selection (D-083)
- Can enemies follow, see or hear through portals / location-changing doors, and can later enemy types open doors? (D-053b, D-097, D-135, D-144)
- Does the "is observed" check (D-045, D-053a) count observation through a portal view? (D-139)
- Portal recursion (portal-in-portal) and sound through portals after the slice (D-140, D-144)
- Health pickup amount and frequency — tune after playtesting R2 (D-171)
