# Grid Level Design

An editor tool for Unity that lets you build levels by placing your own 3D prefabs on a grid, straight in the Scene view.

Pick a prefab from a palette, see a ghost preview where it will land, and paint, drag rectangles, draw lines or flood fill. Turn grid snapping off to place pieces freely. Pieces can stack on top of each other, span several cells, rotate by any angle, be resized, and be selected later to rotate, scale or delete. Levels can be saved to an asset or exported as JSON.

![Demo room built with Grid Level Design](Documentation~/demo.png)

Everything runs in edit mode. Only two small data scripts live outside the Editor folder (they need to be components and assets); none of the tool code is included in builds.

## Requirements

- Unity 6 (6000.0) or newer. Developed and tested on 6000.6 with URP.
- Works with any render pipeline. The ghost and highlights use Unity's built-in editor shader.

## Installation

### From Git URL

1. Open **Window > Package Manager**.
2. Click **+** and choose **Install package from git URL...**
3. Enter:

```
https://github.com/Manzot/unity-grid-level-design.git
```

### Embedded

Clone or download this repository into your project's `Packages` folder.

### Try the demo

In Package Manager, select **Grid Level Design**, open **Samples** and import **Demo Scene and Test Prefabs**. It adds:

- **Demo/GridLevelDemo** – the room in the screenshot above, built with the tool, including resized off-grid pieces. Select **Grid Level**, activate the Grid Level Tool and keep building.
- **Demo/DemoPalette** – a palette with every test prefab; the crate entry has a variant and random rotation. Assign it in the palette window.
- **Demo/DemoLevel** – the room saved as Level Data, linked to the scene's Grid Level for trying Save and Load.
- **Prefabs** and **Materials** – simple blocks, walls, floors, planks and crates with 1x1, 2x1 and 2x2 footprints.

The sample materials use URP's Lit shader. In a Built-in or HDRP project they show pink until you switch them to that pipeline's shader; the tool itself works in every pipeline.

## Quick start

1. **GameObject > Grid Level** creates a grid object in the scene.
2. **Window > Grid Level > Palette** opens the palette. Click **New** to create a palette asset, then drag prefabs or a folder into the window, or use **Add Folder**.
3. Select the Grid Level object, then choose **Grid Level Tool** in the Scene view's Tools overlay.
4. Click a prefab in the palette and click or drag in the Scene view to place it.

## Tools

| Mode | Key | Place | Erase (hold Shift or Erase on) |
|---|---|---|---|
| Paint | B | Click or drag; every cell the cursor crosses | Drag over pieces |
| Rect | M | Drag a rectangle, tiled with whole footprints | Clears the rectangle |
| Line | L | Drag a straight line | Clears the line |
| Fill | K | Floods the connected empty area | Clears connected pieces of the same entry |
| Select | U | Click or box select placed pieces | - |

## Shortcuts

| Key | Action |
|---|---|
| Shift + Drag | Erase while held |
| D | Toggle erase mode |
| S | Toggle stacking on top of existing pieces |
| N | Toggle snap to grid (off = freeform placement in Paint mode) |
| I | Pick the prefab and rotation under the cursor |
| , and . | Rotate left / right by the step angle (in Select mode, rotates the selection) |
| - and = | Scale down / up by the scale step (in Select mode, scales the selection) |
| [ and ] | Layer down / up |
| G | Cycle plane XZ / XY / YZ |
| Del | Delete selected pieces (Select mode) |
| Esc | Cancel a drag, or clear the selection |

The **Shortcuts** button in the overlay shows the same list inside the editor.

## Features

- **Grid:** XZ, XY or YZ plane, per-axis cell size, layers step by one cell along the plane's normal.
- **Ghost preview:** a translucent copy of the next piece, drawn on top of whatever is already in the cell, red when the spot is blocked, with a facing arrow and angle label.
- **Stacking:** with Stack on, new pieces rest on top of existing ones, using real mesh height.
- **Footprints:** each palette entry has a footprint in cells, calculated from its bounds when added and editable in the palette window.
- **Rotation:** any step angle, default 90. Multi-cell footprints swap on quarter turns.
- **Scale:** each palette entry has a Scale setting, and the overlay's scale tool resizes the next piece or the selection by an adjustable step.
- **Freeform placement:** with Snap to Grid off, Paint places pieces exactly at the cursor, optionally rounded to a Fine snap increment. With Stack on they land on the surface under the cursor; with Stack off they can't overlap existing pieces.
- **Variants:** each palette entry can list variant prefabs; every placement picks one at random. Optional random rotation.
- **Selection:** click the piece under the cursor, box select on screen or Shift-add, then rotate, scale or delete. Selected pieces are tinted and outlined.
- **Undo:** every stroke, fill, rotation, delete, load and clear is a single undo step.

## Saving and loading

The Grid Level component's Inspector has a **Level** section:

- **Save As...** writes a **Level Data** asset and links it. **Save** overwrites the linked asset.
- **Load** replaces the scene pieces with the linked asset.
- **Export JSON...** and **Import JSON...** use a text file that references prefabs by GUID, with the asset path as a fallback.
- **Clear** removes every piece.

Saved data keeps each piece's prefab, cell, footprint, rotation and exact local transform, so stack heights and hand adjustments survive.

## Pivot convention

Prefabs are placed with their pivot at the bottom centre of their footprint. If a prefab's pivot sits elsewhere, set **Pivot Offset** on its palette entry.

## Known limitations

- Prefabs stay upright on the XY and YZ planes, and stacking always goes along Y.
- Removing a lower piece by hand does not drop the pieces stacked on it.
- Rotating a selection does not check for overlap with neighbouring pieces.
- At angles that aren't multiples of 90, multi-cell pieces can overhang neighbouring cells.
- Grid and freeform pieces ignore each other: freeform pieces don't block grid cells, and grid erase and fill don't touch them.
- The scale tool doesn't change a piece's grid footprint, so an enlarged grid piece can overlap its neighbours.
- Freeform overlap checks use axis-aligned bounds, so rotated pieces count as slightly larger than they look.

## License

MIT. See [LICENSE.md](LICENSE.md).
