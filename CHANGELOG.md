# Changelog

All notable changes to this package are documented here.

## [0.1.0] - 2026-11-18

First public release.

### Added
- Grid on XZ, XY or YZ with per-axis cell size, layer stepping and adjustable drawn extent.
- Prefab palette asset and window: drag in prefabs or folders, or add a whole folder from a picker.
- Scene view tool with ghost preview, click and drag painting, rectangle, line and flood fill.
- Erase by holding Shift or with an Erase toggle.
- Stacking: place pieces on top of what is already in a cell, measured from real mesh bounds.
- Multi-cell footprints, auto-calculated from mesh bounds, rotating with the piece.
- Rotation by any step angle (default 90), with a facing arrow and angle label on the ghost.
- Random variants and random rotation per palette entry.
- Eyedropper to pick a placed piece's prefab and rotation.
- Select mode: click or box select, rotate and delete selected pieces.
- Resizable Scene view overlay with a preview of the current piece and a shortcut list.
- Save and load levels to a Level Data asset, plus JSON export and import.
- Demo Scene and Test Prefabs sample: a demo room built with the tool, its palette and saved Level Data.
