# Legs and holds

A leg is the stretch of a track between two points. A track with four points has three legs. You can give any leg its own speed, and you can make the camera stop at a point for a while before it moves on.

You do both in the points table in the **Vista** window, in Edit mode.

## The points table

Each row is one point. The row's timing columns describe the leg that arrives at that point, from the point above it. The first point has no leg arriving at it, so its row only has a hold.

| Column | Shows |
|---|---|
| **Duration (s)** | How many seconds the leg takes |
| **Speed** | How fast the camera travels along the leg, in yalms per second |
| **Hold (s)** | How many seconds the camera stays at this point |

A typed value takes effect when you click away from the field.

## Pinning a leg

A leg normally follows the track's **Track speed**, so changing that speed changes the leg too. A pinned leg keeps its own speed instead.

You pin a leg by typing a new **Duration (s)** or **Speed** for it. Its duration and speed always match each other, so changing one changes the other. A leg can take from 0.1 to 600 seconds, and its speed can be from 0.01 to 100.

The pin icon, to the right of the **Hold (s)** field, shows the leg's state:

- A pinned leg shows the pin in the accent colour all the time. Click it to unpin the leg, so it follows the track speed again. Its tooltip says **Pin to Track speed**.
- An unpinned leg shows the pin only while the mouse is over the row. Click it to pin the leg at the speed it has now. Its tooltip says **Pin to Leg speed**.

Dragging a key in the timing graph also pins the legs it changes. If you pin every leg, the **Track speed** and **Track duration** fields are greyed out, as the Timing page explains.

## Holds

A hold makes the camera stop at a point and stay there before it carries on to the next one. Type the number of seconds into the point's **Hold (s)** field. Type 0 to remove it.

A hold can last up to 600 seconds. A hold adds its time to the whole track, so everything after it happens later. A hold on the first point makes the camera wait before it starts to move. A hold on the last point keeps the camera still at the end.

In the timing graph, a hold shows as a flat stretch that ends in a small dot. You can drag that dot to make the hold longer or shorter. Keys and easing explains how.
