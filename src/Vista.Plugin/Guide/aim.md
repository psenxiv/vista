# Aim

Aim decides which way the camera points as it moves along a track. The points set where the camera goes, and the aim sets what it looks at. Roll and field of view always come from the points.

Click {icon:Crosshairs} **Select aim** on the track row to choose:

| Aim | The camera looks |
|---|---|
| **Recorded aim** | Where each point looked, turning smoothly between them. New tracks start here. |
| **Direction of travel** | Ahead along the path, like a camera on a rail. |
| **Look At** | At one fixed spot. See [Look At](aim-look-at.md). |
| **Watch Target** | At a character. See [Watch Target](aim-watch-target.md). |
| **Follow Target** | Travels with a character. See [Follow Target](aim-follow-target.md). |

For **Watch Target** and **Follow Target**, click {icon:PencilAlt} beside the choice in the menu to reopen its window.

With **Direction of travel**, the menu also shows **Look ahead**: how far ahead along the path the camera looks, in seconds. It turns into corners before it reaches them, like a camera operator would. At 0 it faces straight along the path.

## Seeing where it turns

Press `G` in Edit or View to colour the track's path by how fast the camera turns. It goes from the path's usual colour to yellow and then red where it turns hardest. Press `G` again to go back.

## Changing a point's aim

With **Recorded aim**, change a point's aim with **Pitch** and **Yaw** in the [Point window](tracks-point-window.md), or with the gizmo's rotate rings.
