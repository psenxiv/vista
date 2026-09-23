# Timing

Timing decides how fast the camera moves along a track, and so how long the shot lasts. You set it in Edit mode.

The track row in the **Vista** window has two fields for this. The first, with a speedometer icon, is **Track speed**. The second, with a stopwatch icon, is **Track duration**. Hold the mouse over either icon to see its name.

## Track speed

**Track speed** is how fast the camera travels, in yalms per second. A new track starts at 5. You can set anything from 0.01 to 100.

Every leg follows the track speed unless you give it a speed of its own. A leg is the stretch between two points. Giving a leg its own speed is called pinning it, and the Legs and holds page explains how.

## Track duration

**Track duration** is how long the whole track takes to play, in seconds, including any holds.

The two fields work together. When you type a duration, Vista changes the track speed so the track takes that long. When you change the speed, the duration updates to match. Pinned legs and holds keep their own times, so only the other legs speed up or slow down. If those can't make up the difference, Vista gets as close as the speed limits allow.

A typed value takes effect when you click away from the field.

## When the fields are greyed out

If every leg is pinned, no leg follows the track speed, so both fields are greyed out. Unpin a leg to use them again. They are also greyed out on a track with fewer than two points, since it has no legs yet.

## More on timing

- Legs and holds covers each leg's own speed and duration, and pausing the camera at a point.
- The timing graph shows how the camera moves along the track over time.
- Keys and easing covers shaping how the camera speeds up and slows down.
