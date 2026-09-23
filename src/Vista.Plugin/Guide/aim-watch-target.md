# Watch Target

With **Watch Target**, the camera moves along the track as usual but keeps turning to look at a character. Use it to keep someone in frame while the camera flies past them, even if they walk around.

## Choosing the character

When you switch a track to **Watch Target**, the **Watch Target** window opens.

1. Click the drop-down at the top. It says **Choose a character** until you pick one.
2. The list shows the characters loaded near you, by name. Players show their home world after the name, and other characters show **NPC**.
3. To find someone quickly, type part of their name in the **Search** box.
4. Click a name to choose it.
5. Click **Done** to close the window.

If nobody is loaded nearby, the list says **No characters nearby**. If your search matches nobody, it says **No matches**.

To open the window again later, click the aim button, then the pencil beside **Watch Target** in the menu.

## Aim height and smoothing

- **Aim height** sets where on the character the camera aims, in yalms above their feet. It goes from 0 to 3, and starts at 1.3. Drag it to change it.
- **Smoothing** sets how gently the camera turns to keep up with the character. At 0 it stays exactly on them. Higher values make it ease after them more slowly, which looks calmer when they move suddenly.

While the character is found, a small crosshair in the world marks the spot on the character the camera aims at. It shows in View and Edit modes.

## The aim button's colour

While a track uses **Watch Target**, the crosshair aim button in the **Vista** window changes colour to show how the character search is going. Hold the mouse over it to see the tooltip.

| Colour | Tooltip | Means |
|---|---|---|
| Accent colour | **Watch Target:** and the name | The character is found. |
| Red | **Watch Target: choose a character** | No character is chosen yet. |
| Red | The name, then **(Not found): using recorded aim** | The character isn't loaded nearby. |

## When the character isn't found

If the character isn't loaded nearby, the camera uses each point's recorded aim instead, the same as **Recorded aim**. When the character comes back, the camera eases from the recorded aim back onto them.

The points keep their own aim for this, so **Pitch** and **Yaw** stay available in the **Point** window. If more than one character matches, Vista watches the one nearest the track anchor.

In the playlist, a red warning sign appears beside a track whose character isn't found. The Playlist page covers the playlist.
