# M1 — Revision (a): persistence

Milestone: #7. Requests: #1 to #6. Design: `docs/DESIGN.md`. Game facts:
`docs/reference/big-walk-persistence-surfaces.md`.

## Scope

After a game load, these items are the same as before the load:

- the loose props (#3)
- the vehicles, the lights, and the time of day (#4)
- the position, facing, pose, held item, and equipped items of each player (#5)
- the bodies of players who left (#1)
- the player colors, in all saves (#6)

The Diary save (#2) holds this state. Only the host writes it.

At session start, the host moves each object that a player cannot reach in deep water to the
nearest shore (#8).

Out of scope: the title-screen skip, the chat commands, the signs, the auto-join, and the
slideshow (revisions b and c).

## Tasks, in order

<!-- [doc->REQ-BODY-SPIKE] -->
1. **T1 — body spike (#1).** On the host, make a body (`Corpse`) in code. Save the game.
   Load the game. Spawn the body again. A second client joins. Make sure that
   `Corpse.FindMatch` finds the body and the game spawns the player at the body. This is the
   largest risk. If the spike fails, stop. Tell the liaison before you start T2.
<!-- [doc->REQ-DIARY-SAVE] -->
2. **T2 — Diary save (#2).** Write the save format and the atomic write in
   `HikersDiary.Core`. Add unit tests. Save a snapshot at an interval and when the session
   stops. Restore the state after the game completes its own load.
3. **T3 — props (#3).**
4. **T4 — runtime vehicle map, then vehicles, lights, time (#4).**
5. **T5 — player state (#5).** Do the host first. The host has no body. Then do the clients
   with the T1 method.
6. **T6 — colors (#6).**
7. **T7 — review.** Do a two-player test of all items. Then send the review request to the
   liaison.

## Open questions

1. Which identifier is the key: the EOS PUID or the Steam ID? T1 found the answer on
   2026-11-27: the EOS PUID.
2. Is `NetworkIdentity.sceneId` stable across loads? Measure it in T3.
3. What is the second client for two-player tests? On 2026-11-27, the operator agreed to give a second account on
   KITSUBITO (Ubuntu, Proton) or SCELTOUIN (Windows).

## Gate

`pwsh scripts/gates.ps1` is green (build, unit tests, `traceable-reqs check`, docs site).
Each Request activates its requirements in `traceable-reqs.toml` when its work starts.
