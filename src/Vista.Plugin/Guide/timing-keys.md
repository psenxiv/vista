# Keys and easing

The line in the timing graph shows how the camera moves along the track. You change its shape to make the camera ease into a move, slow down at a point, or reach a point at an exact moment. The timing graph page explains how to read it. You can only change it in Edit mode.

## Changing how a leg eases

A leg is the stretch of line between two points. Click the line to select a leg. It changes colour, and the top of the window shows its name, such as "Leg 1 → 2", with a drop-down next to it. Choose from the drop-down:

- **Smooth**: the camera's speed changes gently through the points. This is where every leg starts.
- **Linear**: the camera moves at one steady speed across the leg.
- **Ease in**: the camera starts the leg from a stop and speeds up.
- **Ease out**: the camera slows to a stop at the end of the leg.
- **Ease in-out**: the camera starts and ends the leg at a stop.

The drop-down shows **Custom** when the leg matches none of these, for example after you drag a handle. You can't choose **Custom** yourself.

## Changing a key

Click a key to select it. Clicking a point's key also selects that point. The top of the window shows the key's time, and three buttons that change the line on either side of the key:

- **Smooth** makes the line curve gently through the key.
- **Linear** makes the line run straight into and out of the key.
- **Flat** makes the camera come to a stop for a moment at the key.

When you select a hold's end dot, the trash button, **Remove hold**, removes that hold.

## Dragging keys and handles

Drag a key left or right to change when the camera reaches it. The leg before the key, and the leg or hold after it, change to fit. Later keys stay where they are. The legs you change become pinned, as the Legs and holds page explains. You can't move the first key, and a key can't pass its neighbours.

Hold `Ctrl` as you start dragging a key to move every later key with it. Only the leg before the key changes, so the whole track gets longer or shorter. Dragging the last key to the right also makes the track longer.

Drag a hold's end dot to make the hold longer or shorter. Everything after it moves with it.

A selected key shows a handle, a short line ending in a dot, on each side where a leg meets it. Drag a handle to make the line steeper or flatter where it meets the key. The two handles move together, so the line passes smoothly through the key. A handle stops at flat, because the camera never moves backwards.

Right-click a key for more choices:

- **Remove hold** removes the hold, when you right-click a hold's end dot.
- **Break handles** lets each handle move on its own, so the camera can change speed sharply at the key.
- **Unify handles** joins broken handles again.

Press `Ctrl + Z` to undo a change.
