# Vista

FFXIV Dalamud plugin: camera tracks and a live switchboard.

## Requirements

**Requirements come from the spec, not from reasoning about the use case.**
Background explains motivation and does not generate requirements. If the spec
is ambiguous, ask. Do not resolve ambiguity by adding a feature.

**Verify Dalamud and FFXIVClientStructs APIs against source before use.** Do
not rely on recall, and do not infer an API's behaviour from how Cammy or
Hypostasis used it, since both may predate the current API level.

## Comments

Keep doc comments to one line. State what a thing is or what a method does,
plainly. No architectural essays, no restating the code, no explaining why a
design is good — that belongs in the spec.

    /// <summary>Where the camera is, what it looks at, and its field of view.</summary>

Go longer only for something a reader cannot infer: a non-obvious unit, a
constraint that will bite, a workaround for a game bug. Rare.

Inline comments follow the same rule. Most code should not need one.

## Commits

One line. Conventional prefix, lowercase, no trailing period.

    feat(bootstrap) setup initial project
    fix(camera) stop hook deadlocking on load
    docs(spec) record fov probe result

Write them simply: say what changed, stop. No body paragraphs, no
rationale, no co-author trailers. Reasoning belongs in the spec or the plan,
not in git.

## Structure

- `src/Vista.Core` — pure logic. Must never reference Dalamud or
  FFXIVClientStructs, and must not use `unsafe`. Enforced by the project file.
- `src/Vista.Plugin` — Dalamud, hooks, ImGui. The only place with
  `unsafe`. Never create a `Vista.Plugin.Camera` namespace: it shadows
  FFXIVClientStructs' `Camera`.
- `tests/Vista.Tests` — references Core only.

All three target .NET 10, because Dalamud 15.0.3.5 is built against net10.0.

## Tests

**Derive expected values. Never compute one by calling the code under test.**
`Assert.Equal(PlaybackClock.ShotTime(d, 10, 5), playback.ShotTime)` asserts
`f(x) == f(x)`. Work the number out from the maths or the documented semantics,
write it as a literal, and put the derivation in a comment. If you cannot
derive it, leave the assertion alone and say so.

Round trips are the exception: `Assert.Equal(scene, Load(Save(scene)))` is a
valid test even though both sides call the code under test, because the
property being pinned is the round trip itself.

**Pin the value, with an explicit tolerance.** Where the answer is computable,
assert it. `Assert.True(x > 0)`, `InRange`, `IsFinite` and `NotNull` are for
values that genuinely are not determined; a test that only checks a sign is
blind to an inverted one. For floats, write the tolerance at the assertion
rather than inheriting it from a file-local helper, so a reader can see how
strict the test is without scrolling.

**Test a behaviour where it lives.** Pin it once, at the layer that owns it. A
`SessionState` test that re-checks what `SceneEditing` already proves adds a
second place to edit and no cover. Before adding a test, grep the behaviour's
name across the other test directories.

**Shared fixtures live in a fixtures file, one per test area, with anything
used across areas in `tests/Vista.Tests/Fixtures.cs`.** A helper needed by a
second file moves there rather than being copied. Two helpers with one name and
different behaviour is worse than none.

## Keeping the code honest

**One owner per constant.** A limit, a default or a list of enum values is
declared once and referenced everywhere else. If the Plugin needs a Core list,
make the Core one public rather than copying it. Two declarations of the same
value is a bug waiting for someone to change one of them.

**Do not introduce an interface unless it has two implementations, a test
double, or crosses the Core/Dalamud boundary.** That boundary is where the
types change, not where the `interface` keyword appears. `Func<Vector3, float?>
groundBelow` crosses it because `Ground.Below` needs `BGCollisionModule`. A
Core interface whose only implementation is also in Core crosses nothing. Apply
the same test to each member: an interface can be justified while one of its
members is not.

**Code without a production caller does not survive the phase that introduced
it.** Tests do not count as a caller. Landing Core capability ahead of the UI
that consumes it is fine, if the commit message names the phase that will use
it. Anything still uncalled when that phase closes is deleted or moved to
`FEATURES.md`. Exceptions that only look dead: Dalamud `Window` overrides
(`OnClose`, `PreOpenCheck`, `PreDraw`), command handlers, and `IDisposable`.

**Superseding a design means deleting the old path in the same change.** Two
ways to do one thing is the state every finding in this repo's review came
from. When a commit replaces one way of doing something, grep the old name
across `src/`; if the only remaining hits are its declaration and tests, it
goes in the same commit. Precedent: `feat(tracks) play playlists and drop snap
shots` deleted `SnapPoint.cs` in the commit that superseded it.

**`Vista.Plugin` has no tests, so keep it thin.** If a decision can be asserted
without ImGui and without the game running, it belongs in `Vista.Core` and gets
a test. What stays in the plugin is drawing, hooking and input. If the plugin
must hold a decision, say so in the commit and expect it on the next in-game
checklist.

## Build and test

    make build      # Debug plugin build; sets DALAMUD_HOME (./build.sh does the same)
    make test       # Core tests
    make package    # Release build and latest.zip, as CI makes it
    make bump VERSION=X.Y.Z / make rc N=1 / make release   # versioning and publishing

The commands live in `scripts/`. Never build the plugin with bare `dotnet build` —
`DALAMUD_HOME` must be set. Releases run from `.github/workflows/release.yml` when a `v*` tag is
pushed; only the user pushes tags.

In-game verification is the user's; see `docs/dev-setup.md`. Read results from
`~/Library/Application Support/XIV on Mac/logs/dalamud.log`.

A checklist of in-game checks goes in `scripts/checks/`: add a file named for the work
(`phase-4.json`), list it in `manifest.json`, and leave `index.html` alone. Never overwrite a
checklist that has not been run — several can be pending, and the page keeps each one's progress
separately. Don't write a Markdown checklist. The user serves the folder themselves and sends
back the results JSON it downloads. Delete a checklist once its results are in.

## Docs

- Design: `docs/superpowers/specs/`
- Plans: `docs/superpowers/plans/`
- Deferred features: `FEATURES.md`. Only features actually agreed as deferred.
