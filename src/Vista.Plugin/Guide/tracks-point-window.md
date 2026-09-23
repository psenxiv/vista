# The Point window

The **Point** window shows the selected point's numbers and lets you change them. It opens by itself when you select a point in Edit mode, and its title tells you which one, such as "Point 3".

Close the window to clear the selection. It also closes when you leave Edit mode.

The same window opens when you select an anchor or a Look At point, titled **Scene anchor**, **Track anchor** or **Look At point**. Scenes and anchors and Look At cover those.

## The gizmo

When a point is selected, a gizmo appears on it in the game world. The gizmo is a set of handles you drag to move or turn the point. The buttons at the top left of the **Point** window choose what it does:

- **Move (world)** shows arrows that line up with the world, so up is always straight up, whichever way the point faces.
- **Move (local)** shows arrows that line up with the point, so you can slide it forward or back along the way it faces.
- **Rotate** shows rings. Drag a ring to turn the point left and right, up and down, or to roll it.

Press `R` to switch between moving and rotating. It goes back to whichever move button you used last.

When the track's aim is **Direction of travel** or **Look At**, the track decides which way the camera faces, so the rotate gizmo shows only the roll ring.

## The fields

The window has three rows. Hold the mouse over a field to see its name.

- **Position**: **X**, **Y** and **Z**, where the point is in the world.
- **Rotation**: **Pitch**, **Yaw** and **Roll**, in degrees. Pitch tilts the camera up or down, yaw turns it left or right, and roll tilts it to one side.
- **FoV**: the field of view, in degrees. A smaller number zooms in.

To change a field, drag it left or right. The point moves in the game world as you drag. You can also double-click a field and type a number.

When the track's aim is **Direction of travel** or **Look At**, **Pitch** and **Yaw** are greyed out, for the same reason as the rotate gizmo. **Roll** and **FoV** still work.

The button at the start of the FoV row is **Reset to the camera's field of view**. It sets the point's FoV to the one the camera is using now.

Each drag, of a field or of the gizmo, is one step you can undo with `Ctrl + Z`.

## Copy, paste and delete

The three buttons at the top right work on the selected point:

- **Copy position, aim, roll and FoV** remembers the point.
- **Paste position, aim, roll and FoV** puts what you copied onto the selected point. Its timing stays as it was. This is greyed out until you copy something.
- **Delete point** deletes the point.
