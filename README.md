# Hikers' Diary for Big Walk

Hikers' Diary is a [Big Walk](https://store.steampowered.com/app/1478500/Big_Walk/) mod that keeps the world as you left it. These items stay across game loads:

- objects
- vehicles
- lights
- the time of day
- the pose and the items of each player
- the bodies of players who logged out

The mod is in development. Revision (a) (persistence) is the first goal. See [docs/DESIGN.md](docs/DESIGN.md).
Contributors and dev-agents start at [AGENTS.md](AGENTS.md).

## Requirements

- Big Walk on Windows (Steam), with [BepInEx 6 IL2CPP](https://thunderstore.io/c/big-walk/p/BepInEx/BepInExPack_IL2CPP/) installed (build be.755 or newer).

## Build

```
dotnet build HikersDiary.sln -c Release
```

The plugin references the Il2CppInterop proxy assemblies in `Big Walk/BepInEx/interop/`. These exist after the game starts one time with BepInEx. If your installation is in a different folder, set `GameDir` in a git-ignored `Directory.Build.props.user`. `-p:DeployToGame=true` copies the plugin into the game.

Before you declare that the work is complete, run every gate:

```
pwsh scripts/gates.ps1
```

Requirements are in `traceable-reqs.toml` and the [traceable-reqs](https://github.com/BigscreenVR/traceable-reqs) gate checks them. See [docs/TRACEABILITY.md](docs/TRACEABILITY.md).

## License

MIT. See [LICENSE](LICENSE).
