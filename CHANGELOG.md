# Changelog

All notable changes to this project are documented here. Newest version first.
Versions follow `MAJOR.MINOR.PATCH`.

## 0.0.2 — 2026-11-16

Milestone 1 (world generator), first playable version.

### Added
- Procedural world from a seed (letters and digits). Same seed = same world.
- 10 x 10 km island surrounded by sea, with a beach ring, light hills, rivers, lakes and forests.
- Resource clusters: stone, berry bushes and apple trees.
- 3D view: low-poly terrain, water, trees and resources.
- RTS camera: WASD/arrows to move, mouse wheel to zoom, right mouse or Q/E to rotate, Shift for speed.
- Seed field, Generate button and New random seed button.
- Click to found your village (temporary site rule: flat, not beach, fresh water within 300 m, 1.5 km from other villages). One rival bot then picks the best remaining site.
- Map preview tool (`tools/Kingdom.MapPreview`) and 21 core tests.

## 0.0.1 — 2026-11-16

### Added
- Project skeleton: Godot 4.7 C# project in `game/`, pure C# simulation library `Kingdom.Core` in `src/`, dependency-free test runner in `tests/`.
- Empty start scene that shows the game version read from `Kingdom.Core`, proving Godot can call into the core library.
