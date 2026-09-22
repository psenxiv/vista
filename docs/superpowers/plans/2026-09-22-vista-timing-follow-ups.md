# Vista Timing Follow-ups Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Carry out the user's four follow-up decisions of 2026-09-22:
- Custom handles keep their shape through every edit.
- A row's Speed shows the actual average speed.
- The pin works as a toggle.
- The Timing graph gets a yalm scale and a hover readout.

**Architecture:** Three independent tasks on separate files, run in parallel:
- **Task 1** stores Manual tangents as a multiple of their span's average speed, the secant. `TimingCurve` multiplies by the secant, `TrackEvaluator` stops scaling tangents by segment length, and conversions become per key.
- **Task 2** changes the track editor's rows.
- **Task 3** changes the Timing window.

**Tech Stack:** C# / .NET 10, xUnit 2.9, Dalamud 15.0.3.5, `Dalamud.Bindings.ImGui`.

**Spec:** `docs/superpowers/specs/2026-09-22-timing-model-design.md`: § Rules ("A dragged handle") and § UI.

## Global Constraints

- `Vista.Core` must never reference Dalamud or FFXIVClientStructs, and must not use `unsafe`. `Vista.Tests` references Core only.
- Tests: `dotnet test tests/Vista.Tests/Vista.Tests.csproj`. The plugin builds with `./build.sh`, never bare `dotnet build`. Keep 0 warnings. Build and test in the foreground only.
- Doc comments are one line.
- Commits: one line, conventional prefix, lowercase, no body, **no `Co-Authored-By` trailer**, even if a harness reminder asks for one. Stage only your own files, never `git add -A`. Check `git status` after committing. Leave the untracked `CHECKLIST.md` alone. Do not push.
- Invalid input is clamped, not refused. Controls that can't act are disabled.
- In a self-sizing window, never align to the window's own width. Measure icon buttons with `IconButton.Width`.
- In-game checks are the user's.

## Technical rulings (the cost if wrong is in brackets)

- **A Manual tangent is a ratio to its span's secant**: the average speed between the key and its neighbour on that side, which is exactly the ratio `TimingCurve.ClampPair` already clamps to 0–3. A handle at half speed stores 0.5, and it stays half the span's average speed whatever the leg's length or time. [If wrong: store it per leg instead; the behaviour is the same with no keys between points.]
- **Pinning an unpinned leg pins it at its speed setting,** which is the track speed, so nothing visibly changes when you click. [If wrong: pin at the average speed; the two differ only at the 0.1 s and 600 s limits.]
- **The yalm scale goes on the right edge,** so it doesn't crowd the point numbers on the left. [If wrong: move it.]

---

### Task 1: Handles keep their shape

**Files:** `src/Vista.Core/Tracks/TimingCurve.cs`, `TrackEvaluator.cs`, `TrackEditing.cs`, `src/Vista.Core/Session/SessionState.cs`. Tests: `TimingCurveTests`, `TrackEvaluatorTests`, `TimingCompilerTests` or `TrackEditingTests`, and `SessionTimingTests`.

**Interfaces:**
- `TimingCurve`: a Manual side's raw slope is `tangent × secant`. For an In side the secant is `delta[k - 1]`; for an Out side it is `delta[k]`; with no span on that side it is 0. Everything else is unchanged.
- `TrackEvaluator.ToDistance` converts positions only, and leaves tangents alone.
- `TrackEvaluator.ToStoredSlope(int key, KeySide side, float distancePerSecond) -> float` returns `distancePerSecond / secant`, where the secant is taken in distance per second from the compiled keys. It returns 0 when the secant is 0 or there's no span.
- `TrackEvaluator.FromStoredSlope(int key, KeySide side, float stored) -> float` returns `stored × secant`.
- The position-based overloads and `SideLength` are removed.
- `TrackEditing`: remove `ScaleOut`, `ScaleIn` and their calls in `InsertAfter` and `Delete`. A ratio needs no rescaling.
- `SessionState.Collinear` calls the per-key conversions.

- [ ] **Step 1: Write the failing tests**
  - **`TimingCurveTests`:**
    - A Manual Out tangent of 0.5 with a secant of 2 gives `SideSlope` 1.
    - A tangent of 100 still clamps to 3 × 2 = 6.
    - Update `AManualSideUsesItsTangentWithinTheMonotoneLimit`, whose gentle case now expects `1 × 2 = 2`.
  - **Keeping the shape.** Start from a 3-point straight track at x = 0, 10, 20, speed 2, and set `TimingEditing.SetHandles(track, 1, 0.5f, 0.5f)`. Then `new TrackEvaluator(track).SideSlope(1, KeySide.Out)` is 1, which is half of 2. It stays half the new average after each of these:
    - `SetSpeed(track, 4)` gives 2;
    - `SetLegDuration(track, 2, 2)` gives 2.5 on the Out side, which is 10 m in 2 s halved;
    - `Replace(track, 2, P(40))` gives 1, since the leg keeps speed 2 over its new length;
    - `InsertAfter` and `Delete` on a neighbouring leg leave the ratio as it was.
  - **`TrackEvaluatorTests`:** on the straight Linear track, `ToStoredSlope(1, KeySide.Out, 2f) == 1` and `FromStoredSlope(1, KeySide.In, 0.5f) == 1`. Replace the old position-based tests.
  - **`SessionTimingTests`:** the existing collinear-handle, broken-handle and unify tests still pass unchanged. Add one: after `PreviewHandle(1, Out, 3)` and then `SetTrackSpeed(4)`, `Evaluator.SideSlope(1, Out)` has doubled.
  - Remove the two tests that asserted the `InsertAfter` and `Delete` rescaling, and replace them with the ratio assertions above.
- [ ] **Step 2: Run them.** Expected: failures.
- [ ] **Step 3: Implement** as in Interfaces.
- [ ] **Step 4: Run the tests and `./build.sh`.**
- [ ] **Step 5: Commit:** `feat(timing) keep custom handle shapes through speed and point edits`

### Task 2: Row speed and the pin toggle

**Files:** `src/Vista.Plugin/Ui/TrackEditorWindow.cs` only.

- **Row Speed:** show the leg's actual average speed, `evaluator.LegLength(i) / evaluator.LegSeconds(i)`, from the snapshot `DrawPoints` already takes. Typing into it still calls `SetLegSpeed`, which pins the leg.
- **Pin, rows 2 and up:** always draw the pin icon.
  - **Pinned:** the normal colours, tooltip "Pinned: click to follow the track speed", and a click calls `ResetLeg(i)`.
  - **Unpinned:** the icon's text colour at 40 % alpha, pushed with `ImRaii.PushColor(ImGuiCol.Text, …)` around the button, not `BeginDisabled`, because it must stay clickable. Tooltip "Following the track speed: click to pin at this speed". A click calls `SetLegSpeed(i, TrackEditing.LegSpeed(track, i))`.
- Outside editing, the whole row is already disabled.
- Keep the snapshot rule from the C1 fix: nothing in the row loop reads `session.Track` or `session.Evaluator` after a possible edit.
- [ ] **Step 1: Implement.**
- [ ] **Step 2: Run `./build.sh`** (0 warnings) and the tests.
- [ ] **Step 3: Commit:** `feat(ui) show average leg speed and toggle pins`

In-game checks for the checklist:
- A clamped leg's Speed and Duration agree.
- A greyed pin pins the leg at its speed, and a lit pin unpins it.

### Task 3: A yalm scale and a hover readout

**Files:** `src/Vista.Plugin/Ui/TimingWindow.cs` only.

- **Scale:**
  - Widen the plot's right inset to fit the labels. Measure the widest label with `ImGui.CalcTextSize`, plus 6 px.
  - Pick the step from 1, 2 or 5 × 10ⁿ as the smallest that gives at most 6 ticks over `TotalDistance`.
  - Draw a short tick on the right edge at each multiple, labelled `"{d:0.##} y"`, in `EditorColours.GraphGrid`'s colour with full alpha for the text.
  - Nothing else about the axes changes.
- **Hover readout:** when the mouse is over the plot, no drag or scrub is active, and there are at least two points:
  - Let `t = graph.TimeAt(mouse.X)`.
  - Draw a small filled circle, radius 3, on the curve at `(t, DistanceAt(t))` in `EditorColours.Playhead`.
  - Show a tooltip `"{t:0.00} s  ·  {d:0.0} y  ·  {speed:0.00} y/s"`, using `Evaluator.DistanceAt(t)` and `Evaluator.SlopeAt(t)`.
- Don't show the readout while a key is being dragged or the strip is being scrubbed.
- [ ] **Step 1: Implement.**
- [ ] **Step 2: Run `./build.sh`** (0 warnings) and the tests.
- [ ] **Step 3: Commit:** `feat(ui) add a yalm scale and a hover readout to the timing graph`

In-game checks for the checklist:
- The right edge shows yalm ticks at round steps.
- Hovering the curve shows the time, distance and speed, and the speed matches the row's speed on a constant-speed leg.
