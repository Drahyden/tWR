# The White Room — SUNO Prompts (Audio pass, step A0)

> Music direction: D-006, D-013, D-040, D-075, D-076, D-082, D-199, plus the Boss state proposed in docs/audio/AudioPlan.md §4.
> In SUNO use **Custom mode**, switch **Instrumental ON**, paste the **Style** text into the style field and the **Title**. If your SUNO version has an "Exclude styles" field, paste the **Exclude** text there; if not, the Style text already ends with "no vocals".
> Generate 3–4 versions of each; keep the one with the most even energy (no big intro or ending) so it loops. Export WAV.
> File names: `mus_<state>_<variant>.wav`; stems: `mus_<state>_<variant>_<stem>.wav` (stem = drums / bass / other).

---

## 1. Lobby muzak (also the main menu)
**Title:** `Please Hold`
**Style:**
```
instrumental bossa nova elevator music, warped VHS tape, detuned Rhodes electric piano, soft nylon guitar, brushed drums, gentle wow and flutter, tape hiss, slightly out of tune, uncanny calm office lobby, 90 bpm, loopable, no vocals
```
**Exclude:** `vocals, singing, choir, upbeat, happy, modern pop`
**FADR:** keep Drums / Bass / Other. In game: lobby plays all stems; the main menu plays Other only (thinner, lonelier).

## 2. Exploration
**Title:** `Fluorescent`
**Style:**
```
instrumental dark ambient drone, deep sub bass, electrical fluorescent hum texture, distant metallic resonance, sparse reversed piano notes, slow evolving cold pads, sterile and empty, no drums, no melody hooks, loopable, no vocals
```
**Exclude:** `drums, beat, vocals, choir, uplifting, cinematic hits`
**FADR:** Bass + Other are the layers (there should be little or no drums). If FADR finds a drum stem, discard it.

## 3. Puzzle
**Title:** `Click Them All`
**Style:**
```
instrumental minimal industrial synth, ticking clock pulse, muted plucked synth arpeggio, glassy metallic textures, low pulsing bass, tense but restrained, focused, 100 bpm, steady energy, loopable, no vocals
```
**Exclude:** `vocals, heavy drums, epic, orchestral, happy`
**FADR:** Other (arpeggio + textures) plays from puzzle start; Bass joins at once; Drums/percussion fade in after 30 s unsolved (AudioPlan §1.3).

## 4. Combat
**Title:** `Overtime Shift`
**Style:**
```
instrumental aggressive industrial electronic, distorted pounding drums, gritty saw bass, metallic percussion hits, alarm-like synth stabs, frantic and relentless, dark, 140 bpm, constant intensity, loopable, no vocals
```
**Exclude:** `vocals, screaming, rap, dubstep drop, happy, orchestral`
**FADR:** all three stems. On Seeker Search state the Drums stem fades out first, then the state crossfades back to Exploration.

## 5. Boss — The Bull (Escape path)
**Title:** `Stampede`
**Style:**
```
instrumental heavy industrial electronic metal, stomping tribal floor toms, distorted low brass blasts, chugging detuned bass, charging galloping rhythm, relentless and brutal, 150 bpm, loopable, no vocals
```
**Exclude:** `vocals, screaming, guitar solo, happy, orchestral fanfare`
**FADR:** start with Bass + Other; Drums enter on the first hit on the boss; BossPhaseChanged (phase 2) brings the full mix up.

## 6. Boss — The Preacher (Discover path)
**Title:** `Sermon`
**Style:**
```
instrumental dark ritual industrial, distorted pipe organ, processed choir pads without words, deep drones, irregular metallic percussion, sacred and corrupted, ominous, 120 bpm, loopable, no vocals
```
**Exclude:** `lyrics, sung words, gospel, uplifting, happy, epic trailer`
**FADR:** Other (organ + pads) from the start; Bass on first hit; Drums on phase 2. The "choir pads" stay wordless — if a version has words, discard it.

## 7. Ending — Clock Out (Escape + Good)
**Title:** `Monday Again`
**Style:**
```
instrumental bittersweet slow bossa nova, detuned Rhodes, soft nylon guitar, tape warble, gradually dissolving into radio static, melancholic and slightly wrong, 80 bpm, 40 seconds, no vocals
```
**Exclude:** `vocals, upbeat, happy ending, big finale`
**FADR:** not needed — use the full mix as a one-shot (no loop). Trim to the scene length (~16–18 s) with a fade.

## 8. Ending — Overtime (Escape + Poor)
**Title:** `No Exit`
**Style:**
```
instrumental minimal ambient, one sustained low synth note, faint fluorescent hum, almost silence, no melody, no rhythm, empty and endless, loopable, no vocals
```
**Exclude:** `melody, drums, beat, vocals, choir`
**FADR:** not needed — loop the full mix very quietly while the player is in the white room; cut to silence when the button is pressed.

## 9. Ending — Floor Plan (Discover + Good)
**Title:** `Floor Plan`
**Style:**
```
instrumental ethereal ambient revelation, warm evolving pads, distant wordless choir, soft glassy bells, slow swelling, calm acceptance, mysterious, 60 bpm, 40 seconds, no vocals
```
**Exclude:** `lyrics, drums, epic trailer, happy pop`
**FADR:** not needed — one-shot, trimmed to the scene with a fade to white.

## 10. Ending — Absorbed (Discover + Poor)
**Title:** `Absorbed`
**Style:**
```
instrumental body horror dark ambient, wet organic textures, crushing low drones, slow heartbeat thuds, warped breathing sounds, claustrophobic, dread, 40 seconds, no vocals
```
**Exclude:** `vocals, melody, upbeat, cinematic hits, screaming`
**FADR:** not needed — one-shot, trimmed to the scene.

---

## Not from SUNO
- **Silent room (D-082/D-168):** no music by design.
- **Jump-scare stingers (D-081):** use SFX libraries (Sonniss); SUNO isn't suited to sub-second stingers.
- **Fluorescent hum / ambience (D-077):** use recordings from Sonniss or Freesound (CC0), not SUNO.

## Licensing
Commercial use of SUNO output depends on your SUNO plan at the time of generation — check SUNO's current terms before release on Steam, and log each track in `docs/audio/LICENSES.md` (title, version used, plan, date).
