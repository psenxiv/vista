# Follow Target

With **Follow Target**, the camera travels along with a character, staying at the same place next to them as they move. Use it for a shot that tags along behind someone as they walk or ride.

## One point only

A Follow Target track has a single point, which sets where the camera sits next to the character. If the track has more than one point, **Follow Target** is greyed out in the aim menu, and its tooltip says **Follow Target needs a track with one point**. Delete points until one is left, or start a new track.

If the track has no points yet, choose the character first, then add the point. Vista adds the point only when the character is chosen and found nearby, and it won't add a second one.

## Choosing the character

When you switch a track to **Follow Target**, the **Follow Target** window opens. Choose the character from the drop-down at the top, the same way as on the Watch Target page. Click **Done** to close the window.

The first time you choose a character, the camera stays where it is and from then on keeps that place next to them. If you later choose someone else, the camera keeps the same distance, height and angle, so it jumps to the new character.

To open the window again, click the aim button, then the pencil beside **Follow Target** in the menu.

## The Follow Target window

- **Turn with character**: when ticked, the camera swings round with the character as they turn, so a camera behind them stays behind them. When clear, the camera keeps its direction and only moves with them.
- **Look at character**: when ticked, the camera aims at the character. When clear, it keeps the point's own aim.
- **Aim height**: where on the character the camera aims, in yalms above their feet, from 0 to 3.
- **Smoothing**: how gently the camera catches up with the character. At 0 it stays locked to them. Higher values make it lag behind and ease into place, which hides sudden steps and turns.

Below these, three fields place the camera round the character. They are greyed out until the track has its point.

- **Distance**: how far away the camera is along the ground.
- **Height**: how high the camera is above the character's feet.
- **Angle**: where round the character the camera sits, in degrees. 0 is behind them and 90 is to their right.

You can also move the point with the gizmo or in the **Point** window. Vista keeps its new place next to the character.

## The track anchor

A Follow Target track moves with its character, so its track anchor isn't used. The anchor isn't shown in the world, and the anchor button beside the track in the Hierarchy is greyed out, with the tooltip **Follow Target tracks move with their character**. The Scenes and anchors page covers anchors.

## When the character isn't found

If the character isn't loaded nearby, the camera holds still instead of following. The crosshair aim button in the **Vista** window turns red, and its tooltip shows the name followed by **(Not found)**. Before a character is chosen, it is red with the tooltip **Follow Target: choose a character**. Once the character is found, it shows in the accent colour with **Follow Target:** and the name.

In the playlist, a red warning sign appears beside a track whose character isn't found. The Playlist page covers the playlist.
