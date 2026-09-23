# Vista — zoom and pan in the timing graph (Phase 4.7)

Date: 2026-09-23
Status: Approved

The timing graph always shows the whole shot. On a long track with some keys close together, those
keys pile on top of each other and can't be picked out. The graph gains a zoomable, pannable view
of time, with the distance axis fitting whatever is in view.

## Behaviour

- **Mouse wheel over the graph zooms time around the cursor.** The time under the cursor stays
  under the cursor. Each notch changes the visible span by a factor of 1.25. The view never shows
  less than 0.2 s, nor more than the whole shot.
- **The distance axis fits the view.** It runs from the curve's distance at the view's first second
  to its distance at the last. The curve only climbs, so that range holds every key and every
  stretch of curve in view, and zooming in on keys crowded in time also spreads them vertically.
  Inside a hold, where the curve is flat, the range widens to one yalm around it.
- **Click and drag on empty graph space pans.** A left press that lands on no handle, key or curve,
  inside the plot, grabs the view; dragging slides it, and it stops at either end of the shot. The
  time strip below the plot still scrubs.
- **Fit** — a button at the right of the Timing window's top row, enabled only while zoomed —
  returns to the whole shot. So does switching the edited track.
- The wheel over a Vista window already leaves fly speed alone, so nothing competes for it.

At the whole view every mapping is exactly what it is today.

## Drawing in a zoomed view

- Keys, and the point grid lines and numbers on the left, are drawn only when they fall inside
  the view; keys outside it can't be clicked. Nothing needs clipping, because the fitted distance
  range keeps everything in view inside the plot.
- The curve and the selected leg are drawn over their overlap with the view only.
- The playhead is drawn only while the scrub head is in view.
- **Ticks.** Time and yalm ticks both use one "nice step" rule — the smallest of 1, 2 or 5 times a
  power of ten giving at most N ticks — which is the rule the yalm scale already uses, moved to
  Core so both axes share it. Time gets up to 16 ticks, labelled with as many decimals as the step
  needs; yalms keep 6. At the whole view that changes today's ticks a little: shots of 8–16 s
  keep 1 s steps, shorter shots get 0.5 s, 16–32 s get 2 s instead of 1, 32–80 s keep 5 s, and
  longer shots get 10 s. Labels that would overlap are still skipped, as now.
- **The right inset** for yalm labels is sized for the total distance written to two decimals, so
  the plot doesn't shift sideways as zooming changes the labels.
- Dragging a key while zoomed stops at the view's left edge. Past the right edge it carries on at
  the view's scale, as a drag past the plot already lengthens the shot today; that mapping moves to
  `TimingGraph.TimeAtOpenEnded`, which at the whole view is today's formula exactly. No
  auto-scroll.

## Core

- `TimingGraph` gains the view as init-only properties — `TimeFrom`, `TimeTo`, `DistanceFrom`,
  `DistanceTo` — defaulting to the whole shot, so every existing construction and test is
  unchanged. `ToScreen`, `TimeAt`, `DistanceAt`, `HandleEnd` and `SlopeFromHandle` work over the
  view's spans.
- `TimingView(From, To)` holds the visible time: `Whole`, `Zoom(anchor, factor, duration)`,
  `Pan(seconds, duration)`, `Clamp(duration)` for when the shot changes length, `IsWhole`, and
  `Distances(evaluator)` for the fitted range. `MinSpan` is 0.2 s.
- `Ticks.Step(range, most)` is the nice-step rule.

The plugin keeps only the wiring: the wheel, the pan drag, the Fit button, and skipping what is
out of view.

## Testing

All in Core, with expected values worked out from the definitions:

- A zoomed `TimingGraph` — plot 100 × 50 at the origin, view 2–6 s and 4–12 yalms — puts (4 s,
  8 y) at pixel (50, 25), reads pixel column 75 as 5 s, clamps columns outside the plot to the
  view's ends, and reads a handle 25 px right and 25 px up as a slope of 4 yalms per second.
- Zooming 0–10 s by one half around 5 s gives 2.5–7.5; around 2 s gives 1–6. Zooming out past the
  whole shot stops at 0–10. Zooming a 1 s view by one hundredth stops at 0.2 s, centred as the
  anchor dictates.
- Panning 2–6 by +3 on an 8 s shot stops at 4–8, and by −5 at 0–4.
- A 4–8 view on a shot that shrinks to 6 s becomes 2–6, and on one that shrinks to 3 s, 0–3.
- On the three-point fixture (keys at 0, 5, 10 s and 0, 10, 20 yalms, linear), a 2.5–7.5 s view
  fits 5–15 yalms. Inside a two-second hold at 10 yalms, the range is 9.5–10.5.
- `Ticks.Step`: 60 over 6 ticks is 10; 15.3 over 16 is 1; 2 over 16 is 0.2.

The wheel, the drag and Fit go on an in-game checklist.

## Build order

1. Core: `Ticks`, `TimingView`, and the view on `TimingGraph` — source, then its tests.
2. Plugin: the view field and its reset, fitted graph construction, the drawing filters and ticks,
   the wheel, the pan drag and Fit.
3. The checklist.
