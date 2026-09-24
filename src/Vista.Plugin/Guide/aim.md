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

With **Direction of travel**, the menu also shows **Look ahead**: how far ahead along the path the camera looks, in seconds. The camera turns into corners before it reaches them. At 0 it faces straight along the path.

Going over a loop with **Direction of travel** turns the camera upside down at the top, the way it would on a rollercoaster. It rights itself again once the path levels out.

## Changing a point's aim

With **Recorded aim**, change a point's aim with **Pitch** and **Yaw** in the [Point window](tracks-point-window.md), or with the gizmo's rotate rings.

## Seeing where it turns

Press `G` in Edit or View to colour the path of the track you're editing by how fast the camera turns. It goes from the path's usual colour to yellow, then red where it turns hardest. Press `G` again to go back.
