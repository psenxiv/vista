# Aim

Aim decides which way the camera points while it moves along a track. The points set where the camera goes, and the aim sets what it looks at on the way. Each track has its own aim.

## Choosing an aim

Click the crosshair button in the **Vista** window. Its tooltip says **Select aim** and names the aim the track uses now. Under **Watch Target** and **Follow Target** it shows the character instead. A menu opens with five choices:

- **Recorded aim**
- **Direction of travel**
- **Look At**
- **Watch Target**
- **Follow Target**

Click one to switch the track to it. A new track starts on **Recorded aim**.

Whatever aim you choose, roll and field of view still come from the points.

## Recorded aim

Every point keeps the way the camera was pointing when you added it. With **Recorded aim**, the camera looks where each point looked, and turns smoothly from one point's aim to the next as it travels.

To change a point's aim, select it and change **Pitch** and **Yaw** in the **Point** window, or switch the gizmo to rotate with `R` and drag its rings. The Point window page covers both.

## Direction of travel

With **Direction of travel**, the camera looks ahead along the path, the way a camera on a rail would. The points' own aim is ignored, so **Pitch** and **Yaw** are greyed out in the **Point** window, and the rotate gizmo shows only the roll ring.

A track with one point has no path to look along, so the camera keeps the aim that point was recorded with.

## Look At, Watch Target and Follow Target

- **Look At** keeps the camera pointed at one spot in the world. See the Look At page.
- **Watch Target** keeps a character in frame while the camera moves along the track. See the Watch Target page.
- **Follow Target** makes the camera travel with a character, on a track with one point. See the Follow Target page.

When you choose **Watch Target** or **Follow Target**, its window opens so you can pick the character. While the track uses one of them, a pencil button sits beside it in the menu. Click the pencil to open the window again.
