# Hikers' Diary — context

This is the glossary. It gives the correct meaning of each term. Format: `docs/formats/context-format.md`.

**Save** — the persisted state of one world: objects, vehicles, lights, time of day, and the **Player State** of each player who was in it. The **Host** writes it. It has a title, a player count (2, 3, or 4), and an optional password.

**Host** — the player whose game runs the Mirror server for the session. The host is the only authority for the **Save**. Clients never write it.

**Player State** — the data that returns a player to the location where they left: position, pose (stand, crouch, sit), facing direction, held item, equipped items, and color. The **Host** stores it by Steam ID.

**Vessel** — the body of a player who logged out. It stays in the world, at the pose of its **Player State**, until that player joins again.

**Save Switch** — the change from one **Save** to a different one without the title screen: sit, type a command in text chat, fall asleep, and wake in the other save.

**Known Build** — a game build on which we tested the mod. On a build that is not known, the mod installs no hooks.
