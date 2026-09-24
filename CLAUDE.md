# Vista

FFXIV Dalamud plugin: camera tracks, organised into scenes and played back live.

## Requirements

**Requirements come from the spec, not from reasoning about the use case.** Background explains motivation; it doesn't create requirements. If the spec is ambiguous, ask rather than adding a feature.

**Verify Dalamud and FFXIVClientStructs APIs against their source before use.** Don't rely on recall, or on how other plugins use an API, since they may predate the current API level. Read the source at the version Vista builds against, from a local clone. If there isn't one, ask the user before cloning it.

## Writing

**Never hard-wrap prose in document files.** One line per paragraph or list item. Unwrap any wrapped paragraph in a file you edit.

**Code comments.** Doc comments are one line saying plainly what a thing is or does. No essays, no restating the code, no defending the design; that belongs in the spec.

    /// <summary>Where the camera is, what it looks at, and its field of view.</summary>

Go longer only for what a reader can't infer, such as a non-obvious unit, a constraint that will bite, or a game-bug workaround. Inline comments follow the same rule; most code needs none.

**Commits.** One line: conventional prefix, lowercase, no trailing period. Say what changed, then stop. No body, no rationale, no co-author trailers.

    fix(camera) stop hook deadlocking on load

## Structure

- `src/Vista.Core`: pure logic. No Dalamud, no FFXIVClientStructs, no `unsafe`; the project file enforces this.
- `src/Vista.Plugin`: Dalamud, hooks and ImGui. The only place with `unsafe`.
- `tests/Vista.Tests`: references Core only.

Namespaces match folders. Never name a namespace, or a member reached by its simple name, after a type or namespace used beside it: a `Camera` namespace would shadow FFXIVClientStructs' `Camera`.

Everything targets .NET 10, as Dalamud does.

## Tests

**Derive expected values; never compute them with the code under test.** `Assert.Equal(f(x), result)` proves nothing. Work the number out from the maths or the documented behaviour, write it as a literal, and show the derivation in a comment. If you can't derive it, say so. Round trips are the exception, since the round trip is the property.

**Pin the value, with an explicit tolerance.** Where the answer is computable, assert it; sign, range, finiteness and null checks are only for values that genuinely aren't determined. Write float tolerances at the assertion.

**Test a behaviour once, at the layer that owns it.** Grep the other test directories before adding one.

**Property tests (CsCheck) are for invariants over any valid input**, such as continuity and round trips. They add to derived examples and never replace them. Generators build inputs through the same editing calls the UI makes. A failure is a real counterexample: never rerun for a pass; fix the cause and keep the case as an example test. Pass each property's `print` through `Fixtures.Kept` and tag it `[Trait("Category", "Property")]`.

**Soak and mutation-test a feature once, in its final review:** `make soak`, then `make mutate SINCE=<plan's base commit>`. Each survivor gets a test or a line in the plan saying why it changes nothing. There's no score to reach.

**Every fixed camera bug gets a case in the camera regression scene** (`tests/Vista.Tests/Regression/RegressionScene.cs`) with its expected number of snaps. Run `make regression-scene` and commit the rewritten file with it.

**Shared fixtures** live in a fixtures file per test area, or `tests/Vista.Tests/Fixtures.cs` when used across areas. Move a helper there rather than copying it.

## Keeping the code honest

**One owner per constant.** Declare a limit, default or list once and reference it everywhere else, making it public if another project needs it.

**No interface without two implementations, a test double, or a crossing of the Core/Dalamud boundary.** The boundary is where game types appear, not where the `interface` keyword does. Judge each member the same way.

**Code with no production caller doesn't outlive the phase that added it.** Tests don't count. Core may land ahead of its UI if the commit names the phase that will use it; whatever is still uncalled when that phase closes is deleted, with a `FEATURES.md` entry if the idea is still wanted. Framework entry points (window overrides, command handlers, `Dispose`) only look dead.

**Replacing a design deletes the old one in the same change.** Grep the old name across `src/`; if only its declaration and tests remain, it goes.

**Keep `Vista.Plugin` thin; it has no tests.** A decision that can be asserted without ImGui or the game belongs in Core with a test. The plugin draws, hooks and handles input. If it must hold a decision, say so in the commit and put it on the next in-game checklist.

## Build

    make verify            # Format, lint, test and check coverage
    make build             # Debug plugin build
    make test              # Core tests
    make help              # Every other target

**Run `make verify` before every commit, and commit only when it passes.** Commit what it formatted. If coverage falls below the floor, add tests rather than lowering it.

Turn lint rules off in `.editorconfig` with a comment saying why, never with `#pragma`. Never build with bare `dotnet build`; the scripts set `DALAMUD_HOME`. Formatting-only commits go in `.git-blame-ignore-revs`.

## Releases

    make bump VERSION=X.Y.Z.N   # Set the version and name the pending changelog section
    make testing                # Ship to opted-in testers (test-vX.Y.Z.N)
    make release                # Ship to everyone (prod-vX.Y.Z.N)

Pushing a tag runs the release workflow. Only the user pushes tags.

Versions are SemVer's `X.Y.Z` plus `N`, which goes up by one for every shipped build of that `X.Y.Z`. A test build that holds up is promoted by releasing the same version.

`CHANGELOG.md` has a section per version, newest first: a few short bullets for players, in `GUIDES.md`'s voice. When a change a player would notice lands on `main`, add a bullet to the pending section, headed literally `## X.Y.Z.N`, in the same commit. Never add to a section with a real version. Show the section to the user before shipping.

## In-game checks

In-game verification is the user's. For big work, write a checklist in `tests/in-game/cases/` (gitignored) and list it in `cases/manifest.json`; `tests/in-game/README.md` gives the format. Never overwrite one that hasn't been run. Delete it once its results are in.

A checklist for any change to aim, timing or paths includes one pass of the camera regression scene (`tests/scenes/Vista - Camera Regression.json`), copied into the save folder's `vistaxiv/scenes/` and played in Live. Point at the scene rather than repeating its cases.

Game logs: `~/Library/Application Support/XIV on Mac/logs/dalamud.log`.

## User Guide

The in-plugin User Guide is Markdown in `src/Vista.Plugin/Guide/`. **Read `GUIDES.md` before writing or changing any page.**

When a change affects what a user can see or do, update the guide in the same change. If a key changes, update `hotkeys.md` and the README's keys table together.

## Docs

- Specs and plans: `docs/superpowers/specs/` and `docs/superpowers/plans/`. `docs/` is gitignored; never commit it.
- Feature ideas: `FEATURES.md`, a heading and two or three sentences each.
