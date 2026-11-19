# Changelog

All notable changes to this package are documented here.

## [0.3.0] - 2026-11-19

### Breaking
- `GridLevel.halfExtent` and `GridLevelData.halfExtent` are replaced by `gridSize`, the total number of cells across (the old value doubled). Scenes, Level Data assets and JSON files from 0.2.0 convert automatically when loaded; scripts that read `halfExtent` need to switch to `gridSize`.
- The Snap to Object toggle and its O shortcut are removed. Snapping is always on in freeform.

### Added
- Rotation on every axis: an axis selector (X / Y / Z, key /) and a Reset button. Tilted pieces are centred and seated on the grid from their rotated bounds.
- Freeform snap points: the piece under the cursor shows dots on its real faces (nine on the top, one in the centre of each side facing the camera and one under the bottom). Moving the cursor near a dot attaches the matching face of the next piece to it, centred, so pipe openings line up.
- Fit to Cell: a palette entry option, and a Fit button in the overlay, that scale a prefab so it fills one grid cell.
- 3D wireframe of the hovered cell, so placement is easy to read on vertical grids.
- F focuses the camera on the selected pieces, or on the last piece you placed, picked or selected; Shift+F frames the whole grid.

### Changed
- Footprints are calculated automatically for the current grid's plane and cell size. Tick Manual Footprint on a palette entry to set one yourself; Fill Manual Footprint From Bounds fills it in.
- In freeform, Stack decides whether the next piece drops: on, it lands on top of whatever is under the cursor; off, it stays at the layer's height and ignores pieces under it.
- With several grids in the scene, only the selected grid (or the grid of the selected piece) is drawn.
- The grid size field is shown as Grid Size in the Inspector.

### Fixed
- Shortcuts stopped working after clicking the palette window or the overlay.
- Footprints were far too large on grids whose cell size differed from when the prefab was added, and were measured on the wrong axes on XY and YZ grids.

## [0.2.0] - 2026-11-18

### Added
- Freeform placement: turn **Snap to Grid** off (button or N) to place pieces exactly at the cursor in Paint mode, with an optional Fine snap increment.
- Freeform with Stack on places pieces on the surface of whatever is under the cursor; with Stack off, placement is blocked where it would overlap an existing piece.
- Freeform erase: click or drag over pieces to remove the one under the cursor.
- Scale tool: Scale Down / Scale Up buttons (- and =) with an adjustable step, for the next piece or the selected pieces.
- Scale setting on each palette entry. The scale tool starts from the selected prefab's scale and resets when a different prefab is picked.
- Placement type shown in the overlay (GRID or FREEFORM) next to the current piece.
- Demo scene now includes resized, off-grid pieces: a row of growing pillars and scaled crates.

### Changed
- Select mode picks the actual object under the cursor and box-selects by on-screen position, so stacked and freeform pieces can be selected directly.
- The eyedropper (I) picks the exact object under the cursor.
- Auto Footprint From Bounds takes the entry's Scale into account.
- Turning Snap to Grid off switches to Paint; in other modes the button shows "(Paint only)".
- Saved Level Data and JSON exports record whether each piece is freeform.

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
