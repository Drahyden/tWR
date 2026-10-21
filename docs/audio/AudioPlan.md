# The White Room — Audio & Voice Pass: Plan, Music Brief and Voice Script

> Working document for the audio phase. Source of truth for decisions stays `docs/GDD.md`; anything here marked **PROPOSAL** becomes a D-### only after you approve it.
> Relevant GDD: D-006, D-024, D-030, D-031, D-056, D-071, D-075, D-076, D-077, D-078, D-079, D-080, D-081, D-082, D-083, D-084, D-089, D-150, D-166, D-168, D-178, D-185, D-193, D-199, D-201

---

## 0. Phase plan (build order)

| Step | What | Who |
|---|---|---|
| **A0** | Produce assets: music (SUNO → FADR stems), SFX (libraries), voice lines (your recordings) | You, with this doc |
| **A1** | Audio Mixer + snapshots (Default, Silent, Combat-duck, Ending), replace the D-168 shared "silence" switch; ambient bed + fluorescent hum + reverb zones (D-077) | Claude Code |
| **A2** | Adaptive music system: 4 states (Exploration, Puzzle, Combat, Boss) with stem layering and crossfades, driven by existing events | Claude Code |
| **A3** | SFX pass: player, guns, Seeker, bosses, mechanisms, UI; license log (settles D-083) | You (files) + Claude Code (wiring) |
| **A4** | Inner-voice system: VoiceLine data (key, clip, subtitle, tone, priority, cooldown), the D-030 tone selection, ducking, subtitles; the D-071 erratic post-effect | Claude Code |
| **A5** | Jump-scare stingers (D-081): 2 authored moments + stinger SFX | Claude Code |

Do **A0** first, away from Unity. Placeholder files are fine for anything not ready — the systems can be built and tested with temp audio and swapped later.

---

## 1. Music brief (SUNO + FADR)

### 1.1 How adaptive music will work (keep generation simple)
- **One track per state**, each 2–3 minutes, **instrumental**, designed to loop.
- Split each track into stems in **FADR** (drums, bass, melody/other). In game, **layers fade in/out** inside a state (vertical layering), and **states crossfade** (~2 s) when they change. No beat-matching between states is needed — that keeps SUNO generation forgiving.
- Export everything as **WAV, 48 kHz**. Trim loops on a zero-crossing; leave the beat-sync to the crossfade.

### 1.2 States, triggers and prompts
Style family for all (D-076): *dark ambient horror + industrial synth + distorted bossa nova ("office muzak" gone wrong)*.

| State | Plays when (existing events) | Feel | SUNO style prompt (instrumental) |
|---|---|---|---|
| **Lobby muzak** | Lobby / hub, run start | Uncanny calm, ironic | `instrumental, distorted bossa nova elevator music, warped tape, detuned Rhodes, soft nylon guitar, lo-fi hiss, slightly out of tune, unsettling calm, 90 bpm` |
| **Exploration** | Section start, no threat (SectionStarted, Seeker Patrol) | Dread, space, hum | `instrumental, dark ambient drone, deep sub bass, fluorescent hum texture, distant metallic resonances, sparse reversed piano, slow evolving pads, no drums, cold and sterile` |
| **Puzzle** | PuzzleEvents.Started → Solved | Focused tension, ticking | `instrumental, minimal industrial synth, ticking clock pulse, muted plucked synth arpeggio, glassy textures, tense but restrained, 100 bpm, no heavy drums` |
| **Combat** | Seeker Chase/Attack, EnemyEvents.AwarenessChanged high | Doom energy, frantic | `instrumental, aggressive industrial electronic, distorted pounding drums, gritty bass synth, metallic hits, alarm-like synth stabs, frantic, 140 bpm` |
| **Boss — Bull** | BossEvents.Started (Escape) | Relentless, stampede | `instrumental, heavy industrial metal electronic, stomping tribal drums, distorted low brass, charging rhythm, relentless, 150 bpm` |
| **Boss — Preacher** | BossEvents.Started (Discover) | Ritual, unholy | `instrumental, dark ritual industrial, distorted pipe organ, choir pads processed, deep drones, irregular percussion, sacred and corrupted, 120 bpm` |
| **Ending stingers** | Each ending scene (D-199) | One per ending, 20–40 s | Clock Out: `bittersweet detuned bossa nova fading into static` · Overtime: `empty hum, single sustained synth note, no melody` · Floor Plan: `ethereal ambient revelation, warm pads, distant choir` · Absorbed: `body horror ambient, wet textures, crushing low drones` |

**Boss state:** this resolves the §14 open question "dedicated boss music state" — **PROPOSAL**: add a 4th state *Boss* with one track per boss, phase 2 (BossPhaseChanged) adds the top stem layer.

**Silent room (D-082/D-168):** no music, no hum — the Silent snapshot handles it.

### 1.3 Stems in FADR
For each track, split into **Drums / Bass / Other(melody+pads)**. In-game layer rules:
- Exploration: Other + Bass only (drums muted, if any).
- Puzzle: Other + Bass; drums layer fades in after 30 s unsolved.
- Combat: all layers; on Seeker Search, drums fade out first.
- Boss: Bass + Other at start; drums enter on first hit; phase 2 = full.

### 1.4 Generation tips
- Generate 3–4 versions per state; keep the one with the most **even energy** (no big intro/outro) — loops better.
- Avoid vocals entirely (keep "instrumental" in every prompt).
- Name files: `mus_<state>_<variant>_<stem>.wav` (e.g. `mus_combat_a_drums.wav`).
- Keep a line in the license log per track (SUNO plan/tier at generation time — commercial use depends on your SUNO plan; check its terms before release on Steam).

---

## 2. Ambience & SFX list (A1/A3)

| Group | Sounds | Suggested source |
|---|---|---|
| Ambience | Fluorescent hum (loop, 2–3 variants), room tone, distant office sounds (phone ring, printer, keyboard — very sparse, randomized) | Sonniss GDC bundles; Freesound (CC0) |
| Player | Footsteps (walk/sprint/crouch, 6+ variants), jump, land (soft/hard), breathing, low-HP heartbeat (D-079), interact click | Sonniss; Kenney |
| Pistol | Shot (3 variants), dry fire, reload out/in, impact on wall | Sonniss |
| Mechanisms | Switch on/off, wrong-order reset, door unlock, door locked rattle, pickup | Kenney; Sonniss |
| Seeker | Idle breathing loop, patrol steps (wet), suspicious grunt, chase screech, attack wind-up, hit, death | Own recordings (food/fabric, pitched down) or paid-tier AI SFX |
| Bull | Paw/scrape, roar, charge rumble, wall impact, stun groan, stomp, column crack/break | Sonniss + own recordings |
| Preacher | Teleport flicker/hum, arrival, volley cast, projectile, phase-2 wail | Paid-tier AI SFX + processing |
| Stingers | 3–4 sharp stingers (D-081) | Sonniss |
| UI | Menu move/confirm/back, end-screen reveal | Kenney |

**License log** (settles D-083): keep `docs/audio/LICENSES.md` with one row per file: file name · source · URL/pack · license · attribution needed (y/n).

---

## 3. Inner-voice system

### 3.1 Tone model — **PROPOSAL** for D-030 (exact mapping is still ⚪ open)
Three tones, recorded as separate takes where a line has variants:
- **N — Neutral** (honest, confused): before the fork, and default.
- **R — Resolute** (clear, determined): after the fork when HP ≥ 50%.
- **E — Erratic** (contradictory, fragmented): after the fork when HP < 35%. Between 35–49% keep the previous tone (hysteresis, avoids flip-flopping).

Path flavours the *words* (Escape lines talk about getting out; Discover lines about understanding); HP drives the *tone*. Erratic tone also drives the D-071 blur/darken effect.

### 3.2 Delivery rules — **PROPOSAL**
- Max one line every ~8 s (global cooldown), except priority lines (fork, boss, ending).
- Music and ambience duck by ~6 dB while a line plays.
- Suppressed in the silent room (D-168).
- Subtitles shown when enabled (D-089).
- Repeat-run lines (endings saved, D-201) play at most once per run.

### 3.3 Voice script — first set (≈26 lines)
Keys follow `vo.<area>.<moment>[.<variant>]`. Tone column: which takes to record. Trigger = existing event in the code.

| # | Key | Trigger | Tone(s) | Line |
|---|---|---|---|---|
| 1 | vo.intro.door | Opening, as the black door opens (D-021) | N | "That door wasn't there yesterday. …Was it?" |
| 2 | vo.lobby.first | First lobby entry, first run | N | "Okay. Bright. Too bright. This is not the break room." |
| 3 | vo.lobby.hum | Lobby, ~20 s idle | N | "That humming. Like the lights are thinking." |
| 4 | vo.section.start.1 | SectionStarted (generic, random of 3) | N | "Another room. Same white. Different… something." |
| 5 | vo.section.start.2 | SectionStarted | N | "I could swear the hallway was shorter." |
| 6 | vo.section.start.3 | SectionStarted | N | "Keep moving. Rooms don't hurt you. Probably." |
| 7 | vo.puzzle.start | PuzzleEvents.Started | N/R/E | N: "Switches. Of course there are switches." · R: "Fine. Figure it out, get out." · E: "Click them, click them, no — wait — click them." |
| 8 | vo.puzzle.hint.allactive | Hint, AllActive family (D-178) | N | "Every light has to be on. Every single one." |
| 9 | vo.puzzle.hint.sequence | Hint, Sequence family | N | "Order matters here. Look for the pattern." |
| 10 | vo.puzzle.hint.toggle | Hint, Toggle family | N | "Hit it twice and it forgets. Don't overthink it." |
| 11 | vo.puzzle.solved | PuzzleEvents.Solved | N/R/E | N: "Huh. That actually worked." · R: "Good. Next." · E: "It *let* me. Why did it let me?" |
| 12 | vo.enemy.first_seen | First Seeker sighting in a run | N | "That's… a person? No. No, that's not a person." |
| 13 | vo.enemy.killed | First kill of the run | N/R/E | N: "I shot it. I actually shot it." · R: "Stay down." · E: "It looked at me like it knew my name." |
| 14 | vo.health.low | HP drops below 35% | E | "Okay — okay — breathe — where's the — breathe." |
| 15 | vo.fork.enter | Fork room entered | N | "Two doors. Of course it's two doors." |
| 16 | vo.fork.exit_seen | EXIT door in view (D-150) | N | "Exit. Just walk out. Easy. Too easy?" |
| 17 | vo.fork.basement_seen | BASEMENT door in view | N | "Basement. Nobody good ever says 'let's check the basement.'" |
| 18 | vo.path.escape | Path committed: Escape | R/E | R: "I'm leaving. Whatever this is, it's not my job." · E: "Out out out out — just get out." |
| 19 | vo.path.discover | Path committed: Discover | R/E | R: "I need to know what this place is." · E: "It's calling me down. Why is it calling me down?" |
| 20 | vo.boss.bull | BossEvents.Started (Bull) | R/E | R: "That's not a bull. That's a stampede with a grudge." · E: "Too big too big too big —" |
| 21 | vo.boss.preacher | BossEvents.Started (Preacher) | R/E | R: "It's preaching. To me? About what?" · E: "Four hands. Why does it need four hands?" |
| 22 | vo.boss.phase2 | BossEvents.PhaseChanged | R/E | R: "It's angry now. Good. Means it's working." · E: "It's everywhere. It's behind me. Is it behind me?" |
| 23 | vo.ending.clockout | Clock Out scene (D-195) | N | "Monday again. Everyone's smiling. Why is everyone smiling?" |
| 24 | vo.ending.floorplan | Floor Plan scene | N | "So that's what it is. …I think I'll stay a while." |
| 25 | vo.ending.absorbed | Absorbed scene | E | "I can't feel where I end. The walls feel warm." |
| 26 | vo.repeat.lobby | Lobby, a run after ≥1 ending saved (D-201) | N | "…This door again. I've been here. Haven't I?" |

*(Overtime has no line on purpose — the silence and the button are the ending.)*

**Total takes to record:** 26 lines, ≈ 40 takes including tone variants.

### 3.4 Recording guide (D-031)
- Record **dry**: no reverb, quiet room, soft furnishings around you; the game adds space.
- **WAV, 48 kHz, 24-bit, mono.**
- 2–3 takes per line; keep the best; leave ~0.3 s of silence at start and end.
- Keep peaks around **−3 dBFS**; aim for consistent loudness across lines (normalize all to the same loudness in your editor).
- Name each file by key and tone: `vo.puzzle.start_R.wav` (neutral-only lines: `vo.intro.door_N.wav`).
- Inner-monologue delivery: close to the mic, quieter than speaking to someone, a little breathy — it's in Daniel's head.
- Record a 10 s **room tone** clip too (for gaps/edits).

### 3.5 Subtitles (D-084, D-089)
Every key above gets an entry in the localization string table (English now, Spanish in the later sprint). The subtitle text is the line text; tone variants share one key per tone (`…_R`, `…_E`).

---

## 4. Decisions (approved — recorded in docs/GDD.md)
1. 🟢 **D-213** 4th music state **Boss** with one track per boss; phase 2 adds the top layer (resolves the §14 boss-music question).
2. 🟢 **D-214** Music structure: one track per state, FADR stems as vertical layers, ~2 s crossfades between states.
3. 🟢 **D-215** D-030 tone mapping: Neutral before fork; after fork Resolute at HP ≥ 50%, Erratic at HP < 35%, hysteresis between; path changes content, HP changes tone.
4. 🟢 **D-216** Voice delivery: ~8 s global cooldown (priority lines exempt), −6 dB ducking, suppressed in silent rooms, repeat-run lines once per run.
5. 🟢 **D-217** Overtime ending has no voice line.
6. 🟢 **D-218** License log at `docs/audio/LICENSES.md` (settles D-083 together with the source list above).
7. 🟢 **D-236** A5 scares: "Relief" (puzzle room, after the solve, lights out at the exit door) and "Behind you" (impossible-geometry hall, figure by the return portal when seen), once per run each.
8. 🟢 **D-237** Stingers on SFX (2D, no noise events); Relief cuts music + hum with the lights, Behind you dips music ≈ −12 dB; ≈ 2 s recovery; code-side dip, no new snapshot.
9. 🟢 **D-238** Rules: held during combat / voice lines, skipped if still held at section end; ≥ 120 s apart; never in silent rooms, boss arenas, endings, lobby, fork; flicker ≤ 3 flashes/s, ≈ 1 s.
10. 🟢 **D-239** ScareData per scare (stinger ID swappable), ScareAnchor in the scene, ScareEvents.Fired, F1 Fire/Reset, [TWR] validation.
