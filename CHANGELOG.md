# Changelog

All notable changes to this package are documented here.

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
