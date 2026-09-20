# Cinematic Cam

FFXIV Dalamud plugin: camera tracks and a live switchboard.

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

Write them like a lazy dev: say what changed, stop. No body paragraphs, no
rationale, no co-author trailers. Reasoning belongs in the spec or the plan,
not in git.

## Structure

- `src/CinematicCam.Core` — pure logic. Must never reference Dalamud or
  FFXIVClientStructs, and must not use `unsafe`. Enforced by the project file.
- `src/CinematicCam.Plugin` — Dalamud, hooks, ImGui. The only place with
  `unsafe`.
- `tests/CinematicCam.Tests` — references Core only.

All three target .NET 10, because Dalamud 15.0.3.5 is built against net10.0.

## Build and test

    ./build.sh                                              # plugin; sets DALAMUD_HOME
    dotnet test tests/CinematicCam.Tests/CinematicCam.Tests.csproj

Never build the plugin with bare `dotnet build` — `DALAMUD_HOME` must be set.

In-game verification is the user's; see `docs/dev-setup.md`. Read results from
`~/Library/Application Support/XIV on Mac/logs/dalamud.log`.

## Docs

- Design: `docs/superpowers/specs/`
- Plans: `docs/superpowers/plans/`
- Deferred features: `FEATURES.md`. Only features actually agreed as deferred.
