# Vista

FFXIV Dalamud plugin: camera tracks, organised into scenes and played back live.

## Requirements

**Requirements come from the spec, not from reasoning about the use case.** Background explains motivation and does not generate requirements. If the spec is ambiguous, ask. Do not resolve ambiguity by adding a feature.

**Verify Dalamud and FFXIVClientStructs APIs against source before use.** Do not rely on recall, and do not infer an API's behaviour from how Cammy or Hypostasis used it, since both may predate the current API level.

## Writing

**Never hard-wrap prose in document files.** Each paragraph or list item is one line; let the editor flow it. Unwrap any wrapped paragraph in a file you are editing.

**Code comments.** Keep doc comments to one line. State what a thing is or what a method does, plainly. No architectural essays, no restating the code, no explaining why a design is good; that belongs in the spec.

    /// <summary>Where the camera is, what it looks at, and its field of view.</summary>

Go longer only for something a reader cannot infer: a non-obvious unit, a constraint that will bite, a workaround for a game bug. Rare. Inline comments follow the same rule. Most code should not need one.

**Commits.** One line: conventional prefix, lowercase, no trailing period. Say what changed, then stop. No body, no rationale, no co-author trailers. Reasoning belongs in the spec or the plan, not in git.

    feat(bootstrap) setup initial project
    fix(camera) stop hook deadlocking on load
    docs(spec) record fov probe result

## Structure

- `src/Vista.Core`: pure logic. Never references Dalamud or FFXIVClientStructs and never uses `unsafe`; the project file enforces both.
- `src/Vista.Plugin`: Dalamud, hooks, ImGui. The only place with `unsafe`.
- `tests/Vista.Tests`: references Core only.

Namespaces match folders. Never name a namespace, or a member that code reaches as a simple name, after a type or namespace in use beside it: a `Vista.Plugin.Camera` namespace would shadow FFXIVClientStructs' `Camera`, a `Path` namespace `System.IO.Path`, and a `Game` member the `Vista.Plugin.Game` namespace. That's why the folders are `Tracks/Aiming` rather than `Aim` (a `Track` property) and `Tracks/Spline` rather than `Path`.

All three target .NET 10, because Dalamud 15 is built against net10.0.

## Tests

**Derive expected values. Never compute one by calling the code under test.** `Assert.Equal(PlaybackClock.ShotTime(d, 10, 5), playback.ShotTime)` asserts `f(x) == f(x)`. Work the number out from the maths or the documented semantics, write it as a literal, and put the derivation in a comment. That comment counts as something a reader cannot infer; it is not licence to comment freely. If you cannot derive it, leave the assertion alone and say so.

Round trips are the exception: `Assert.Equal(scene, Load(Save(scene)))` is valid because the round trip itself is the property being pinned.

**Pin the value, with an explicit tolerance.** Where the answer is computable, assert it. `Assert.True(x > 0)`, `InRange`, `IsFinite` and `NotNull` are for values that genuinely are not determined; a test that only checks a sign is blind to an inverted one. For floats, write the tolerance at the assertion rather than inheriting it from a file-local helper.

**Test a behaviour where it lives.** Pin it once, at the layer that owns it. A `SessionState` test that re-checks what `SceneEditing` already proves adds a second place to edit and no cover. Before adding a test, grep the behaviour's name across the other test directories.

**Property tests (CsCheck) are for invariants that must hold for any valid input**, such as continuity, round trips and permutations. They add to derived examples and never replace them, and a property that only checks a sign or finiteness doesn't count. Generators build inputs through the editing calls the UI makes and state their assumptions. CsCheck seeds each run randomly, so a failure is a real counterexample: never rerun to get a pass; fix the cause and keep the failing case as an example test. Tag each property `[Trait("Category", "Property")]`.

**Mutation-test a feature once, in its final review.** When every task of a plan is done, run `make mutate SINCE=<the plan's base commit>`. Each surviving mutant in the changed code gets a test, or a line in the plan saying why it changes nothing, such as `<` to `<=` between continuous floats. Never in a single task's review or in `make verify`, and there's no score to reach. Property tests are left out, since their random inputs would change a mutant's result from run to run.

**Shared fixtures live in a fixtures file per test area**, with anything used across areas in `tests/Vista.Tests/Fixtures.cs`. A helper needed by a second file moves there rather than being copied.

## Keeping the code honest

**One owner per constant.** A limit, a default or a list of enum values is declared once and referenced everywhere else. If the Plugin needs a Core list, make the Core one public rather than copying it.

**Do not introduce an interface unless it has two implementations, a test double, or crosses the Core/Dalamud boundary.** That boundary is where the types change, not where the `interface` keyword appears. `Func<Vector3, float?> groundBelow` crosses it because `Ground.Below` needs `BGCollisionModule`. A Core interface whose only implementation is also in Core crosses nothing. Apply the same test to each member: an interface can be justified while one of its members is not.

**Code without a production caller does not survive the phase that introduced it.** Tests do not count as a caller. Landing Core capability ahead of the UI that consumes it is fine if the commit message names the phase that will use it. Anything still uncalled when that phase closes is deleted, with an entry in `FEATURES.md` if the idea is still wanted. Exceptions that only look dead: Dalamud `Window` overrides (`OnClose`, `PreOpenCheck`, `PreDraw`), command handlers, and `IDisposable`.

**Superseding a design means deleting the old path in the same change.** When a commit replaces one way of doing something, grep the old name across `src/`; if the only remaining hits are its declaration and tests, it goes in the same commit. Precedent: `feat(tracks) play playlists and drop snap shots` deleted `SnapPoint.cs`.

**`Vista.Plugin` has no tests, so keep it thin.** If a decision can be asserted without ImGui and without the game running, it belongs in `Vista.Core` and gets a test. What stays in the plugin is drawing, hooking and input. If the plugin must hold a decision, say so in the commit and expect it on the next in-game checklist.

## Build

    make verify     # Format, lint and test: must pass before every commit
    make format     # Format with CSharpier (print width 120)
    make lint       # Build the plugin and tests with analyzer warnings as errors
    make mutate     # Mutation-test Core with Stryker; SINCE=<commit> for changes since it
    make build      # Debug plugin build; sets DALAMUD_HOME
    make test       # Core tests
    make package    # Release build and latest.zip, as CI makes it

**Run `make verify` before every commit, and commit only when it passes.** It formats the code, so commit what it formatted. `make testing` and `make release` run it in check mode and refuse unformatted code.

The lint is .NET's recommended analyzers plus Meziantou.Analyzer, with the rules set in `.editorconfig`. Turn a rule off there, with a comment saying why, rather than with `#pragma` in the code.

Never build the plugin with bare `dotnet build`: `DALAMUD_HOME` must be set. The commands live in `scripts/`. Formatting-only commits go in `.git-blame-ignore-revs`.

## Releases

    make bump VERSION=X.Y.Z.N   # set the version, name the pending changelog section, and commit
    make testing                # ship it to opted-in testers (test-vX.Y.Z.N)
    make release                # ship it to everyone (prod-vX.Y.Z.N)

Releases run from `.github/workflows/release.yml` when a `test-v*` or `prod-v*` tag is pushed. Only the user pushes tags.

Versions are `X.Y.Z.N`: SemVer's major, minor and patch, then N, the build of that X.Y.Z, up by one for every shipped build. A test build that holds up is promoted by releasing the same version, and the workflow reuses its zip if the code hasn't changed since. A fix after a test build is the next N.

`CHANGELOG.md` has a `## X.Y.Z.N` section per version, newest first: a few short bullets for players, in `GUIDES.md`'s voice. The pending section is headed literally `## X.Y.Z.N`. When a change a player would notice lands on `main`, add its bullet there in the same commit, starting it at the top if there isn't one. A section with a real version has been bumped for shipping; never add to it. `make bump` gives the pending section its version. Show the section to the user before shipping. The workflow uses it for the release notes and `repo.json`, and `make testing` / `make release` refuse a version without one.

## In-game checks

In-game verification is the user's. For big work, write a JSON checklist in `tests/in-game/cases/` (gitignored, never committed), named for the work (`phase-4.json`), and list it in `cases/manifest.json`. `tests/in-game/README.md` gives the format; leave the page's own files alone. Never overwrite a checklist that has not been run: several can be pending, each with its own progress. The user sends back the results JSON; delete the checklist once its results are in.

Read game logs from `~/Library/Application Support/XIV on Mac/logs/dalamud.log`.

## User Guide

The in-plugin User Guide is Markdown in `src/Vista.Plugin/Guide/`. **Read `GUIDES.md` before writing or changing any page**; it sets the voice, length and formatting.

When a change adds, removes or changes something a user can see or do, update the guide pages that describe it in the same change. If a key changes, update `hotkeys.md` and the README's keys table together.

## Docs

- Design: `docs/superpowers/specs/` (local only; `docs/` is gitignored, so never commit to it)
- Plans: `docs/superpowers/plans/` (local only)
- Feature ideas: `FEATURES.md`. A heading and two or three sentences each; research and reasoning go in the spec.
