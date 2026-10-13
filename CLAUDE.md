# CLAUDE.md — The White Room (Unity project)

Claude Code reads this file automatically at the start of every session. Always start `claude` from the repository root (`C:\Dev\tWR`).

## Project
- *The White Room*: 3D first-person psychological puzzle shooter with horror. Unity 6.3 LTS (6000.3.21f1), URP, Windows/Steam.
- Source of truth: `docs/GDD.md` (copy of the claude.ai Project GDD). Every gameplay decision has an ID `D-###`. Cite IDs in code comments and commit messages when a change implements one.
- Current milestone: vertical slice (D-103 / D-107): hub lobby, one room, Seeker enemy, pistol, one switch puzzle, one see-through portal.

## Repository layout
```
C:\Dev\tWR\                  <- Git repository root (start Claude Code here)
├── CLAUDE.md
├── .gitignore / .gitattributes
├── docs\GDD.md
├── .claude\agents\          <- unity-scripter, gdd-guardian
└── TheWhiteRoom\            <- Unity project (open this folder in Unity Hub)
    └── Assets\_Project\{Scripts,Prefabs,Scenes,Art,Audio,Data,Settings}
```
All Unity paths below are relative to `TheWhiteRoom/`.

## Developer
- Solo developer, mid-beginner coder (D-095): understands structure and purpose. Claude writes the code; explain WHAT each script does and WHY in 2–4 plain sentences, not line-by-line.
- For anything that must be done in the Unity Editor (creating objects, assigning components, setting layers), give numbered click-by-click steps.
- Windows + PowerShell. Give commands that work in PowerShell.
- Git (D-096): after each working step, give exact commands to commit, e.g.
  ```
  git add -A
  git commit -m "D-043: Seeker patrol + hearing"
  git push
  ```

## Code conventions
- C#, namespace `TWR.<Area>` (e.g. `TWR.Player`, `TWR.Enemies`, `TWR.Puzzles`).
- Scripts in `TheWhiteRoom/Assets/_Project/Scripts/<Area>/`.
- New Input System. Mouse & keyboard only, no rebinding (D-038).
- Tunable values (gun stats, enemy stats, puzzle data) live in ScriptableObjects, not hard-coded.
- Player-facing text and voice lines go through localization tables from day one (D-084).
- Performance budget: 60 FPS on mid-range PCs (D-100). Cap visible portals and portal recursion (D-097).
- Shared systems to reuse, not duplicate: "is observed" view-cone check (D-045, D-053), noise events (D-043, D-079), voice-trigger events (D-080).

## Git LFS
Large binaries are tracked with LFS via `.gitattributes` (models, textures, audio, video). Never commit `Library/`, `Temp/`, `Logs/`, `UserSettings/` or build output.

## Don'ts
- Don't change a locked (🟢) GDD decision without asking; propose a new D-### instead.
- Don't add hands/arms to the first-person view (D-070) or gore beyond D-051.
