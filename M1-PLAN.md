# M1 — Revision (a): persistence

Milestone: #7. Requests: #1 to #6. Design: `docs/DESIGN.md`. Game facts:
`docs/reference/big-walk-persistence-surfaces.md`.

## Scope

After a game load, these items are the same as before the load:

- the loose props (#3)
- the vehicles, the lights, the time of day, and the props on the moving platforms (#4)
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
   Result on 2026-11-27: the spike passed with a second client on KITSUBITO. See the comment
   on #1.
<!-- [doc->REQ-DIARY-SAVE] -->
2. **T2 — Diary save (#2).** Write the save format and the atomic write in
   `HikersDiary.Core`. Add unit tests. Save a snapshot at an interval and when the session
   stops. Restore the state after the game completes its own load.
<!-- [doc->REQ-PROP-POSE] -->
3. **T3 — props (#3).** The host saves the position and the rotation of each loose prop. A
   prop is loose when no home and no player holds it. The keys are `Prop.savablePropGuid`,
   then `NetworkIdentity.sceneId`, then the GameObject path. The host does not use a path
   that two props have. After a game load, the host moves each prop to its saved position.
   The host does not save a prop on a moving platform. The log shows that prop one time.
   In T4b, the host saves these props.
   Result on 2026-11-27: on the host, 306 of 306 props went to their saved positions. One
   prop that we moved by 3 m in the Diary file went to the new position. See the comment on #3.
<!-- [doc->REQ-VEHICLE-STATE] -->
<!-- [doc->REQ-TIME-OF-DAY] -->
4. **T4 — runtime vehicle map, then vehicles, lights, time (#4).** The host finds each
   `NetworkedTrain` and each `MovementDriver` by its GameObject path. The host does not use a
   path that two vehicles of one kind have. For a `NetworkedTrain`, the host saves the spline
   index, the distance, the speed and the target speed. For a `MovementDriver`, the host saves
   the position and the speed. The host also saves the Enviro date and time. After a game load,
   the host sets these values again.
   Result on 2026-11-27: the vehicle map is in section 4.0 of the reference document. On the host,
   3 of 3 vehicles and the time went to their saved values. Before the game load, we changed the
   monorail distance in the Diary file by 50. After the load, the monorail stayed at that
   distance. The time continued from the saved time. In a light
   test, a test aid changed the red lookout light from 1 to 0. After a game load, the light
   had state 0. Thus, the game save keeps the light states. The host does not save
   them.
   The carousel is the solo gondola (the answer from the operator on 2026-11-27).
<!-- [doc->REQ-PLATFORM-PROPS] -->
   **T4b — props on moving platforms (#4). Do this task after T5.** The operator added it to M1 on
   2026-11-27. The host saves the position of each prop on a moving platform
   (`StickyPlatform`) relative to its platform. After a game load, the host does these steps in this order:
   1. It sets the state of the vehicles again (T4).
   2. It waits one or two physics steps.
   3. It moves each prop to its saved position on its platform.
   4. It attaches each prop to its platform again (`StickyPlatformer.SetPlatform`).

   Test T4b on the monorail and on the gondola.
<!-- [doc->REQ-PLAYER-STATE] -->
5. **T5 — player state (#5).** Do the host first. The host has no body. Then do the clients
   with the T1 method. In each snapshot, the host saves these data of its own player:
   - the position, the rotation, and `headState`
   - the crouch and the sit
   - the GameObject path of the `PlayerPose`
   - the held item, and the items in the backpack, holster and blindfold pockets

   After a game load, the host moves its player with `PlayerGrease.Teleport`. It returns the items with
   `ServerPickUpPropAutomatic` and `Prop.ServerSetPinned`. Then it enters the pose with
   `ServerEnterPoseAuto`.
   Result for the host on 2026-11-27: before a game load, we changed the host record in the Diary
   file. We set a stump seat, a head direction, a torch in the hand, and a map in the holster.
   After the load, the host was in the seat with the torch and the map. The next snapshot read
   these values from the game. The keys of the items are `savablePropGuid`. The path of a held
   item changes, because the item is then below the player in the hierarchy. We did not test the
   ground sit and the crouch yet. A single-PC test cannot make them, because player input is
   necessary for them.

   For a client who left, the body (`Corpse`) holds the items. In each snapshot, the host saves
   the items in the 4 homes of each body. These homes are the grasper (the hand), the backpack,
   the holster and the blindfold. After a game load, the host restores the props first and then
   the bodies. Then it pins the items into the homes of each body. The host does not save a body
   as a loose prop, because the body record keeps it.

   When the client joins again, the game moves the backpack and holster items to the player. It
   drops the held item and the blindfold item at the body (`Corpse.ExpireDueToRevival`). The
   game does the same steps without a game load.

   We did the test for the bodies on 2026-11-27. We added a body record to the Diary file, with
   a torch in the hand and a map in the backpack. After the load, the body held the torch and the
   map. The next snapshot read these items from the homes of the body. The two-player test of the
   join is a part of T7. In M1, a client starts at its body and does not sit again.
<!-- [doc->REQ-PLAYER-COLORS] -->
6. **T6 — colors (#6).** The game keeps the colors of each player in each save slot. The color
   book is one file for all slots: `colors.json` in the Diary folder. For each game identifier, it
   holds the Steam ID and the head, torso and legs colors. These items occur only on the host:
   - `PlayerLooks.LoadOne` reads a color from the slot. If the book has a color for that player
     and part, the host calls `ServerSetLook(color, part, true)` instead. Thus the slot also
     receives the color.
   - If the book has no color, the game method runs. Then the host writes the result to the book.
   - `PlayerLooks.ServerSaveLook` writes a changed color to the slot. Then the host also writes the
     color to the book.

   When the host restores a body and the book has colors for that body, the host uses the colors
   from the book. The book is on the host computer, so it applies to all saves on that computer.
   Result on 2026-11-27: at the first load, the book was empty. The host wrote the colors of the
   slot (19, 0, 8) and the Steam ID to the book. Then we changed the book to 5, 3, 12. After the
   next load, the host had these colors, and the save slot also had them. The two-player test is a
   part of T7.
7. **T7 — review.** Do a two-player test of all items. Then send the review request to the
   liaison.

## Open questions

1. Which identifier is the key: the EOS PUID or the Steam ID? T1 found the answer on
   2026-11-27: the EOS PUID.
2. Is `NetworkIdentity.sceneId` stable across loads? T3 found the answer on 2026-11-27: yes.
   In two loads, all 306 loose props had the same `sceneId` and the same path. All 306 props
   also have a `savablePropGuid`, so `savablePropGuid` is the key that the host uses.
3. What is the second client for two-player tests? The answer on 2026-11-27: KITSUBITO
   (Ubuntu 24.04, Proton Experimental), with a second game account. With the test aids
   `Spike.AutoJoinCode` and `Spike.AutoJoinPassword`, the client joins a session automatically.

## Gate

`pwsh scripts/gates.ps1` is green (build, unit tests, `traceable-reqs check`, docs site).
Each Request activates its requirements in `traceable-reqs.toml` when its work starts.
