# Legs and holds

A leg is the stretch of a track between two points. A hold makes the camera wait at a point before it moves on. You set both in the points table.

## The points table

Each row's timing describes the leg arriving at that point from the one above.

| Column | Sets |
|---|---|
| **Duration (s)** | How many seconds the leg takes. |
| **Speed** | How fast the leg travels, in yalms per second. |
| **Hold (s)** | How long the camera waits at this point. |

## Pinning a leg

A leg follows **Track speed** until you pin it. A pinned leg keeps its own speed when the track speed changes.

- Change a **Duration (s)** or **Speed** to pin the leg at that value.
- Click {icon:Thumbtack} **Pin to Leg speed** to pin a leg, or **Pin to Track speed** to unpin it.

Dragging keys in the timing graph pins the legs it changes. See [Keys and easing](timing-keys.md).

## Holds

Set **Hold (s)** to make the camera wait there. A hold makes everything after it happen later.
