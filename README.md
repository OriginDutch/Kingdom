# Kingdom Game (working title)

A real-time 3D kingdom builder: start as one hamlet among many on a procedurally generated continent and grow into a kingdom through war, subjugation, trade and diplomacy. Offline, player vs bots.

Design decisions live in the Game Design Document: https://claude.ai/artifact/JY5cwFnAVTKcBNQHus9chD

## Layout

| Path | What |
| --- | --- |
| `game/` | Godot project: rendering, input, UI. Open `game/project.godot` in Godot. |
| `src/Kingdom.Core/` | Pure C# simulation core (world generation, economy, agents, AI). No Godot references. |
| `tests/Kingdom.Core.Tests/` | Dependency-free test runner for the core. |
| `tools/Kingdom.MapPreview/` | Generates a world from a seed and writes a top-down image + stats. |
| `CHANGELOG.md` | Version history and patch notes. |

## Requirements

- **Godot 4.7.2 — .NET edition** (the standard Godot download has no C# support).
- **.NET SDK 8.0 or newer.**

## Run

1. Open Godot (.NET edition), import `game/project.godot`.
2. Press Play (F5). A random world is generated.

### Controls

| Input | Action |
| --- | --- |
| WASD / arrow keys | Move the camera (Shift = faster) |
| Mouse wheel | Zoom |
| Right mouse drag, Q / E | Rotate |
| Left click | Found your village / move it |
| Seed field + Generate | Generate a specific world |
| New random seed | Generate a random world |

## Tests

From the repo root:

```
dotnet run --project tests/Kingdom.Core.Tests
```

Exit code `0` means all tests passed.

## Map preview

```
dotnet run --project tools/Kingdom.MapPreview -- MYSEED map.ppm
```

Writes a top-down image (PPM) of the world for that seed and prints statistics.

## Versioning

The version number lives in one place: `Directory.Build.props` (`<Version>`). Every release gets an entry in `CHANGELOG.md`.
