# Scenes and anchors

A scene is everything you are working on: your tracks, listed in the Hierarchy, and the playlist that Live plays. A new scene starts with one empty track, "Track 1".

An anchor is a marker on the ground that other things hang off. When you move or turn an anchor, everything attached to it moves and turns with it. There are two kinds.

- The scene anchor holds every track in the scene. Move it to shift the whole scene to a new spot.
- Each track has its own track anchor, which holds that track's points and its Look At point. Move it to shift one track.

Track anchors hang off the scene anchor, so moving the scene anchor carries the track anchors too.

## Where anchors appear

You don't place anchors yourself. When you add the first point of the scene, Vista puts the scene anchor on the ground under that point. When you add a track's first point, its track anchor goes on the ground under that point in the same way. Until then, the anchor doesn't exist and its button is greyed out.

In Edit and View, anchors are drawn in the game world:

- The scene anchor is a diamond on the ground with an arrow showing which way it faces.
- A track anchor is a small ring on the ground with an arrow, the track's name above it, and a faint line to the track's first point.

The anchor of the track you are editing is drawn in colour, and other tracks' anchors are grey. A selected anchor turns orange. Hidden tracks don't show their anchors.

## Selecting an anchor

In Edit mode, you can select an anchor in three ways:

- Click **Select scene anchor**, the anchor button at the top of the Hierarchy beside **Add track**.
- Click **Select track anchor**, the anchor button on a track's row in the Hierarchy. This also starts editing that track, and the camera stays where it is.
- Click an anchor in the game world. Clicking another track's anchor starts editing that track.

When an anchor is selected, the **Point** window opens with the title **Scene anchor** or **Track anchor**, and a gizmo appears on the anchor. Click empty space in the game world to clear the selection.

## Moving and turning an anchor

Drag the gizmo's arrows to move the anchor. To turn it, click **Rotate** in the **Point** window or press `R`, then drag the ring. An anchor only turns left and right, never tilts.

You can also change the numbers in the **Point** window, the same way as for a point. **X**, **Y** and **Z** set the position, and **Yaw** sets which way it faces. The other fields show a dash because an anchor doesn't have them.

Everything attached to the anchor moves with it, whether you use the gizmo or the numbers. Each drag is one step for `Ctrl + Z` to undo.

To move an anchor on its own, hold `Alt` while you drag the gizmo. The anchor moves and everything attached to it stays where it is. This is useful when an anchor is in an awkward place and you want to turn the scene or a track around a different spot. `Alt` only works with the gizmo, not with the numbers in the **Point** window.

A Follow Target track moves with its character, so you can't select or move its anchor. The anchor isn't drawn in the game world, and its button in the Hierarchy is greyed out.

## Changing zone

Your scene stays when you change zone. Its anchors keep their positions, so in the new zone your tracks sit at the same coordinates as before. To bring them somewhere useful, move the scene anchor.
