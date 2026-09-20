# Cinematic Cam Phase 0: Toolchain — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Prove that a Dalamud plugin can be built on macOS and loaded into
FFXIV running under Wine, and establish the three-project structure everything
else is built on.

**Architecture:** Three projects. `CinematicCam.Core` holds pure logic and
cannot reference Dalamud, enforced by its project file. `CinematicCam.Plugin`
targets `net10.0-windows` and builds against the XIV on Mac dev assemblies.
`CinematicCam.Tests` references Core only.

No camera code in this phase. Phase 0 exists so that a failure in the build
chain surfaces on day one rather than being mistaken for a hook bug later.

**Tech Stack:** .NET 10 (SDK 10.0.301, runtime 10.0.9), Dalamud 15.0.3.5,
`Dalamud.NET.Sdk/15.0.0`, FFXIVClientStructs, ImGui via `Dalamud.Bindings.ImGui`,
xUnit. Game runs under Wine via XIV on Mac.

**Spec:** `docs/superpowers/specs/2026-09-20-cinematic-cam-design.md`

## Global Constraints

- `CinematicCam.Core` targets `net10.0` and must never reference Dalamud,
  FFXIVClientStructs, or use `unsafe`. This is enforced by the project file.
- `CinematicCam.Plugin` targets `net10.0-windows`.
- `CinematicCam.Tests` targets `net10.0` and references `Core` only.
- Dalamud dev assemblies live at
  `$HOME/Library/Application Support/XIV on Mac/dalamud/Hooks/dev`.
  `DALAMUD_HOME` must point there for every build.
- Dalamud log to read for verification:
  `$HOME/Library/Application Support/XIV on Mac/logs/dalamud.log`.
- No code is copied from Cammy. It is reference only; it ships no license file.
- Every log line from this plugin is tagged `[CinematicCam]` by Dalamud
  automatically via `IPluginLog`.
- Commit after every task. Commit messages end with:
  `Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>`

## A note on who runs what

Claude cannot see or drive the game. Tasks 1, 2 and the pure-logic steps run
entirely on macOS under `dotnet build` and `dotnet test`. Tasks marked
**[IN-GAME]** require the user to launch FFXIV and follow a numbered checklist.
Claude reads `dalamud.log` afterwards to verify.

---

## Task 1: Solution scaffold and the pure Core project

**Files:**
- Create: `CinematicCam.sln`
- Create: `src/CinematicCam.Core/CinematicCam.Core.csproj`
- Create: `src/CinematicCam.Core/CameraState.cs`
- Create: `tests/CinematicCam.Tests/CinematicCam.Tests.csproj`
- Create: `tests/CinematicCam.Tests/CameraStateTests.cs`
- Create: `.gitignore`

**Interfaces:**
- Consumes: nothing.
- Produces: `CinematicCam.Core.CameraState`, a readonly record struct with
  `Vector3 Position`, `Vector3 LookAt`, `float Fov`. Every later task that moves
  a camera produces or consumes this type.

- [ ] **Step 1: Create the .gitignore**

```
bin/
obj/
*.user
.DS_Store
```

- [ ] **Step 2: Create the Core project file**

`src/CinematicCam.Core/CinematicCam.Core.csproj` — note there is deliberately
no Dalamud reference and `AllowUnsafeBlocks` is absent:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <LangVersion>latest</LangVersion>
  </PropertyGroup>
</Project>
```

- [ ] **Step 3: Write the failing test**

`tests/CinematicCam.Tests/CameraStateTests.cs`:

```csharp
using System.Numerics;
using CinematicCam.Core;
using Xunit;

namespace CinematicCam.Tests;

public class CameraStateTests
{
    [Fact]
    public void CameraState_CarriesPositionLookAtAndFov()
    {
        var state = new CameraState(
            Position: new Vector3(1, 2, 3),
            LookAt: new Vector3(4, 5, 6),
            Fov: 1.2f);

        Assert.Equal(new Vector3(1, 2, 3), state.Position);
        Assert.Equal(new Vector3(4, 5, 6), state.LookAt);
        Assert.Equal(1.2f, state.Fov);
    }

    [Fact]
    public void CameraState_EqualityIsByValue()
    {
        var a = new CameraState(Vector3.One, Vector3.Zero, 1f);
        var b = new CameraState(Vector3.One, Vector3.Zero, 1f);

        Assert.Equal(a, b);
    }
}
```

- [ ] **Step 4: Create the test project file**

`tests/CinematicCam.Tests/CinematicCam.Tests.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <IsPackable>false</IsPackable>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.11.1" />
    <PackageReference Include="xunit" Version="2.9.2" />
    <PackageReference Include="xunit.runner.visualstudio" Version="2.8.2" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="../../src/CinematicCam.Core/CinematicCam.Core.csproj" />
  </ItemGroup>
</Project>
```

- [ ] **Step 5: Create the solution and add both projects**

```bash
cd "$(git rev-parse --show-toplevel)"
dotnet new sln -n CinematicCam
dotnet sln add src/CinematicCam.Core/CinematicCam.Core.csproj
dotnet sln add tests/CinematicCam.Tests/CinematicCam.Tests.csproj
```

- [ ] **Step 6: Run the test to verify it fails**

```bash
dotnet test tests/CinematicCam.Tests/CinematicCam.Tests.csproj
```

Expected: build error, `CameraState` could not be found. If instead the error is
that `net10.0` cannot be resolved, stop — SDK 10 is not resolving the .NET 9
targeting pack, and Step 8 addresses it.

- [ ] **Step 7: Write the minimal implementation**

`src/CinematicCam.Core/CameraState.cs`:

```csharp
using System.Numerics;

namespace CinematicCam.Core;

/// <summary>
/// A complete description of where the camera is and what it looks at.
/// This is the boundary between pure logic and game memory: everything above
/// produces one of these, and exactly one class below writes it into the game.
/// </summary>
public readonly record struct CameraState(Vector3 Position, Vector3 LookAt, float Fov);
```

- [ ] **Step 8: Run the test to verify it passes**

```bash
dotnet test tests/CinematicCam.Tests/CinematicCam.Tests.csproj
```

Expected: 2 passed.

**Resolved during execution, twice.** The `global.json` contingency was never
needed: SDK 10 compiles `net10.0` fine. But `net10.0` was the wrong target
entirely — Dalamud 15.0.3.5 is built against **net10.0**, so a net10.0 plugin
fails to compile against it with CS1705. All three projects target `net10.0`,
which matches the installed SDK and runtime, and no roll-forward property is
needed anywhere.

- [ ] **Step 9: Commit**

```bash
git add -A
git commit -m "$(cat <<'EOF'
Add solution, Core project and CameraState

Core targets net10.0 with no Dalamud reference and no unsafe blocks, so
game types cannot leak into the layer under test. CameraState is the
seam between pure logic and game memory.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>
EOF
)"
```

---

## Task 2: Plugin project builds against Dalamud

This is the riskiest task in phase 0. `Dalamud.NET.Sdk` is designed for Windows
and has never been confirmed working from macOS on this machine. Step 5 is the
contingency and is expected to be needed.

**Files:**
- Create: `src/CinematicCam.Plugin/CinematicCam.Plugin.csproj`
- Create: `src/CinematicCam.Plugin/CinematicCam.json`
- Create: `src/CinematicCam.Plugin/Plugin.cs`
- Create: `build.sh`
- Modify: `CinematicCam.sln`

**Interfaces:**
- Consumes: `CinematicCam.Core.CameraState` from Task 1.
- Produces: `CinematicCam.Plugin.Plugin`, an `IDalamudPlugin` with a
  **parameterless** constructor. Dalamud injects `[PluginService]` statics
  before the constructor runs. Do **not** take `IDalamudPluginInterface` as a
  constructor parameter and do **not** call `pluginInterface.Create<Plugin>()` —
  that older pattern deadlocks on Dalamud 15, hanging forever at
  "Creating plugin instance" with no error. Static
  `[PluginService]` accessors used by every later task:
  `Plugin.PluginInterface`, `Plugin.Log`, `Plugin.Framework`,
  `Plugin.CommandManager`, `Plugin.Hooks`, `Plugin.ClientState`,
  `Plugin.KeyState`.

- [ ] **Step 1: Write the plugin manifest**

`src/CinematicCam.Plugin/CinematicCam.json`:

```json
{
  "Name": "Cinematic Cam",
  "Author": "psenxiv",
  "Punchline": "Camera tracks and a live switchboard for cinematic work.",
  "Description": "Define camera paths with controlled aim, save snap points, and cut between them live from a program/preview switchboard.",
  "ApplicableVersion": "any"
}
```

- [ ] **Step 2: Write the plugin project file**

`src/CinematicCam.Plugin/CinematicCam.Plugin.csproj`:

```xml
<Project Sdk="Dalamud.NET.Sdk/15.0.0">
  <PropertyGroup>
    <TargetFramework>net10.0-windows</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <AllowUnsafeBlocks>true</AllowUnsafeBlocks>
    <Version>0.1.0.0</Version>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="../CinematicCam.Core/CinematicCam.Core.csproj" />
  </ItemGroup>
</Project>
```

- [ ] **Step 3: Write the build script**

`build.sh` — every build goes through this so `DALAMUD_HOME` is never forgotten:

```bash
#!/usr/bin/env bash
set -euo pipefail
export DALAMUD_HOME="$HOME/Library/Application Support/XIV on Mac/dalamud/Hooks/dev"
if [ ! -f "$DALAMUD_HOME/Dalamud.dll" ]; then
  echo "Dalamud dev assemblies not found at: $DALAMUD_HOME" >&2
  exit 1
fi
dotnet build src/CinematicCam.Plugin/CinematicCam.Plugin.csproj -c Debug "$@"
```

Then: `chmod +x build.sh`

- [ ] **Step 4: Write the minimal plugin**

`src/CinematicCam.Plugin/Plugin.cs`:

```csharp
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;

namespace CinematicCam.Plugin;

public sealed class Plugin : IDalamudPlugin
{
    [PluginService] internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] internal static IPluginLog Log { get; private set; } = null!;
    [PluginService] internal static IFramework Framework { get; private set; } = null!;
    [PluginService] internal static ICommandManager CommandManager { get; private set; } = null!;
    [PluginService] internal static IGameInteropProvider Hooks { get; private set; } = null!;
    [PluginService] internal static IClientState ClientState { get; private set; } = null!;
    [PluginService] internal static IKeyState KeyState { get; private set; } = null!;

    public Plugin()
    {
        Log.Information("CinematicCam loaded.");
    }

    public void Dispose()
    {
        Log.Information("CinematicCam unloaded.");
    }
}
```

- [ ] **Step 5: Build, and apply the contingency if it fails**

```bash
./build.sh
```

Expected: build succeeds, producing
`src/CinematicCam.Plugin/bin/Debug/CinematicCam.dll`. Note DalamudPackager
flattens output — there is no framework-named subdirectory.

**Result: `Dalamud.NET.Sdk/15.0.0` built on macOS with no modification.** The
fallback below was not needed and is retained only in case a future Dalamud
version breaks it:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0-windows</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <AllowUnsafeBlocks>true</AllowUnsafeBlocks>
    <Version>0.1.0.0</Version>
    <AssemblyName>CinematicCam</AssemblyName>
    <RootNamespace>CinematicCam.Plugin</RootNamespace>
    <CopyLocalLockFileAssemblies>false</CopyLocalLockFileAssemblies>
    <DalamudLibPath>$(DALAMUD_HOME)/</DalamudLibPath>
  </PropertyGroup>
  <ItemGroup>
    <Reference Include="Dalamud">
      <HintPath>$(DalamudLibPath)Dalamud.dll</HintPath>
      <Private>false</Private>
    </Reference>
    <Reference Include="FFXIVClientStructs">
      <HintPath>$(DalamudLibPath)FFXIVClientStructs.dll</HintPath>
      <Private>false</Private>
    </Reference>
    <Reference Include="Dalamud.Bindings.ImGui">
      <HintPath>$(DalamudLibPath)Dalamud.Bindings.ImGui.dll</HintPath>
      <Private>false</Private>
    </Reference>
    <Reference Include="Newtonsoft.Json">
      <HintPath>$(DalamudLibPath)Newtonsoft.Json.dll</HintPath>
      <Private>false</Private>
    </Reference>
    <Reference Include="Lumina">
      <HintPath>$(DalamudLibPath)Lumina.dll</HintPath>
      <Private>false</Private>
    </Reference>
    <Reference Include="Lumina.Excel">
      <HintPath>$(DalamudLibPath)Lumina.Excel.dll</HintPath>
      <Private>false</Private>
    </Reference>
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="../CinematicCam.Core/CinematicCam.Core.csproj" />
  </ItemGroup>
  <ItemGroup>
    <None Include="CinematicCam.json" CopyToOutputDirectory="PreserveNewest" />
  </ItemGroup>
</Project>
```

Record which path worked in the commit message. This answers the spec's open
question about SDK 10 cross-targeting.

- [ ] **Step 6: Verify the output layout**

```bash
ls -la src/CinematicCam.Plugin/bin/Debug/
```

Expected: `CinematicCam.dll`, `CinematicCam.json` and `CinematicCam.Core.dll`
all present. Dalamud requires the `.json` manifest to sit beside the `.dll`
with a matching name. If `CinematicCam.json` is missing, add the `<None
Include>` item shown in the fallback above.

- [ ] **Step 7: Add to solution and commit**

```bash
dotnet sln add src/CinematicCam.Plugin/CinematicCam.Plugin.csproj
git add -A
git commit -m "$(cat <<'EOF'
Add plugin project targeting net10.0-windows

Builds on macOS against the XIV on Mac Dalamud dev assemblies via
DALAMUD_HOME. build.sh wraps the build so the variable is never
forgotten.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>
EOF
)"
```

---

## Task 3: Plugin loads in game **[IN-GAME]**

**Files:**
- Modify: `src/CinematicCam.Plugin/Plugin.cs`
- Create: `docs/dev-setup.md`

**Interfaces:**
- Consumes: `Plugin.CommandManager`, `Plugin.Log` from Task 2.
- Produces: the `/ccam` command, which every later task extends with
  subcommands. Dispatch is by first whitespace-separated argument.

- [ ] **Step 1: Add the command handler**

Replace the body of `Plugin.cs` with:

```csharp
using Dalamud.Game.Command;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;

namespace CinematicCam.Plugin;

public sealed class Plugin : IDalamudPlugin
{
    private const string CommandName = "/ccam";

    [PluginService] internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] internal static IPluginLog Log { get; private set; } = null!;
    [PluginService] internal static IFramework Framework { get; private set; } = null!;
    [PluginService] internal static ICommandManager CommandManager { get; private set; } = null!;
    [PluginService] internal static IGameInteropProvider Hooks { get; private set; } = null!;
    [PluginService] internal static IClientState ClientState { get; private set; } = null!;
    [PluginService] internal static IKeyState KeyState { get; private set; } = null!;

    public Plugin()
    {
        CommandManager.AddHandler(CommandName, new CommandInfo(OnCommand)
        {
            HelpMessage = "/ccam selftest - report camera diagnostics to the log."
        });

        Log.Information("CinematicCam loaded. Build {Build}.", typeof(Plugin).Assembly.GetName().Version);
    }

    private void OnCommand(string command, string args)
    {
        var verb = args.Trim().Split(' ', 2)[0].ToLowerInvariant();
        switch (verb)
        {
            case "selftest":
                Log.Information("[selftest] no diagnostics registered yet.");
                break;
            default:
                Log.Information("[ccam] unknown verb '{Verb}'.", verb);
                break;
        }
    }

    public void Dispose()
    {
        CommandManager.RemoveHandler(CommandName);
        Log.Information("CinematicCam unloaded.");
    }
}
```

- [ ] **Step 2: Build**

```bash
./build.sh
```

Expected: success.

- [ ] **Step 3: Write the dev setup document**

`docs/dev-setup.md` — the user follows this once:

```markdown
# Dev setup (XIV on Mac)

## One-time

1. Launch the game through XIV on Mac.
2. Open Dalamud settings in game: `/xlsettings`.
3. Experimental tab: enable **Plugin dev mode**.
4. Under **Dev Plugin Locations**, add the path to the **DLL itself**. A
   directory is rejected with "not a valid path to a potential Dev Plugin".
   `Z:` maps to the macOS root:

       Z:\Users\<your-macos-username>\code\ffxiv-projects\cinematic-camera\src\CinematicCam.Plugin\bin\Debug\CinematicCam.dll
5. Save and close.

## Each iteration

1. On macOS: `./build.sh`
2. In game: `/xlplugins` -> **Dev Tools** tab -> find **Cinematic Cam** ->
   click the reload icon.
3. No game restart required. If reload does not pick up changes, note it and
   restart the game instead.

## Reading the log

Claude reads this file directly, no game restart needed:

    ~/Library/Application Support/XIV on Mac/logs/dalamud.log

Filter to this plugin:

    grep CinematicCam ~/Library/Application\ Support/XIV\ on\ Mac/logs/dalamud.log | tail -40
```

- [ ] **Step 4: User runs the in-game checklist**

Hand the user these exact steps:

1. Follow `docs/dev-setup.md` one-time setup.
2. Confirm **Cinematic Cam** appears in `/xlplugins` under Dev Tools, loaded
   with no error badge.
3. Type `/ccam selftest` in the game chat.
4. Type `/ccam nonsense` in the game chat.
5. Make a trivial edit — change `"CinematicCam loaded."` to
   `"CinematicCam loaded v2."` — rebuild, and click reload in Dev Tools.
6. Report whether reload worked without a game restart.

- [ ] **Step 5: Claude verifies from the log**

```bash
grep CinematicCam "$HOME/Library/Application Support/XIV on Mac/logs/dalamud.log" | tail -40
```

Expected to find, in order: `CinematicCam loaded.`,
`[selftest] no diagnostics registered yet.`, `[ccam] unknown verb 'nonsense'.`,
`CinematicCam unloaded.`, then `CinematicCam loaded v2.`

If `CinematicCam unloaded.` never appears on reload, hot reload is not working
and every later in-game task costs a game restart. Record that in
`docs/dev-setup.md` so later checklists budget for it.

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "$(cat <<'EOF'
Add /ccam command and dev setup documentation

Confirms the plugin loads under XIV on Mac and that log output reaches
the macOS-side dalamud.log, which is how in-game behaviour gets verified
from the development side.

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>
EOF
)"
```

---

## Phase 0 exit criteria

- `dotnet test` passes on macOS.
- `./build.sh` produces `CinematicCam.dll` and `CinematicCam.json` side by side.
- The plugin appears in `/xlplugins` Dev Tools and loads without an error badge.
- `/ccam selftest` and `/ccam nonsense` both reach `dalamud.log`.
- **Manual reload: works.** ~2 second cycle, no game restart.
- **Automatic reload: does not work under Wine.** Dalamud's `FileSystemWatcher`
  never sees writes made by native macOS processes. Each iteration needs a
  click on the reload icon.
- **Project-file form: `Dalamud.NET.Sdk/15.0.0`**, unmodified. The
  explicit-reference fallback was not needed.

## Phase 0 result

Complete, 2026-09-20. Findings that change later phases:

- Dalamud 15.0.3.5 targets **net10.0**, not net9.0 as the spec assumed. All
  projects retargeted; no roll-forward needed anywhere.
- `Dalamud.NET.Sdk/15.0.0` builds on macOS unmodified.
- Dev plugin locations must point at the **DLL**, not its directory.
- The plugin entry point must use a **parameterless constructor**. Taking
  `IDalamudPluginInterface` as a parameter and calling `Create<Plugin>()`
  deadlocks with no error.
- Manual reload works, so phase 1 checklists can verify one probe at a time.
  Automatic reload does not; each build needs a click.

## What comes next, and why it is not planned yet

Phase 1 takes ownership of the camera. Its plan is written once this phase
lands, because two phase 0 outcomes change how it must be written:

- **If hot reload does not work**, every in-game verification costs a game
  restart. The phase 1 checklists then have to batch several probes into each
  launch rather than verifying one thing at a time.
- **If the `Dalamud.NET.Sdk` fallback was needed**, the reference list may need
  extending as later code pulls in more Dalamud assemblies, and phase 1's tasks
  need to say so.

The intended phase 1 task sequence, each answering one question:

1. Resolve the active camera and confirm its pointers and fields are live.
2. Hook `CameraBase.Update()` as a passthrough that changes nothing.
3. Write position and look-at; confirm the view freezes while the character
   walks away.
4. Probe whether a field-of-view write survives the game's own update. **Open
   question in the spec.**
5. Probe whether the camera passes through geometry. **Open question in the
   spec.**
6. Ownership, panic key, and release on zone change, logout and unload.
7. Free-flying camera driven by the movement keys.

A detailed draft of these exists at
`docs/superpowers/plans/drafts/phase-1-camera-ownership-draft.md`. It is a
starting point for the real plan, not something to execute.
