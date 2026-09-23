# Look At

With **Look At**, the camera always looks at one point in the world, wherever it is on the track. Use it to keep a building, a spot on the ground or anything else that stays still in the middle of the shot.

## Where the Look At point starts

The first time you switch a track to **Look At**, Vista places the Look At point 10 yalms in front of the track's first point, in the direction that point was looking. If the track has no points yet, the Look At point goes 10 yalms in front of the camera.

The track keeps its Look At point if you switch to another aim and back again.

## Seeing and selecting it

In View and Edit modes, the Look At point shows as a crosshair in the world, with a faint line to the track's first point. Look At points on other tracks show dimmer.

In Edit mode, click the crosshair to select it. If it belongs to another track, Vista switches to that track first. Selecting the Look At point opens the **Point** window, titled **Look At point**.

## Moving it

There are two ways to move the Look At point once it is selected:

- Drag the gizmo's arrows. The Look At point only moves, so the gizmo never switches to rotate for it.
- Drag **X**, **Y** or **Z** in the **Look At point** window.

The track's points turn to face it as you drag. Each drag is one step for `Ctrl + Z`.

## What is greyed out

The Look At point has a place but no direction, so in the **Look At point** window the **Rotate** button is greyed out, and **Pitch**, **Yaw**, **Roll** and **FoV** show a dash. The copy, paste and delete buttons are greyed out too.

While a track uses **Look At**, its points take their aim from the Look At point. When you select one of its points, **Pitch** and **Yaw** are greyed out in the **Point** window, and the rotate gizmo shows only the roll ring. **Roll** and **FoV** still work as usual.
