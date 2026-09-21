# How Cammy solves the camera problem

Read-only analysis. No plugin code changed.

## 0. Provenance and versions

| Thing | Version | Where |
|---|---|---|
| Cammy | `c9895b2` 2026-05-04 "Bump version" | `~/code/Cammy` |
| Hypostasis (pinned submodule) | `c230760` 2026-04-29 | `~/code/Cammy/Hypostasis` |
| FFXIVClientStructs (upstream, for comparison) | `5287dd5` 2026-09-21 | `~/code/FFXIVClientStructs` |
| Dalamud source, tag matching ours | `e81744f6a` = tag `15.0.3.5` | `~/code/Dalamud` |
| Dalamud we build against | 15.0.3.5 (`Dalamud.deps.json`) | XIV on Mac `Hooks/dev` |
| Game client | `2026.09.15.0000.0000` (7.56 hotfix 2) | Bottles prefix |

Cammy's last commit landed during game patch **7.50** (ClientStructs tagged 7.50 on 2026-04-28).
Four patches have shipped since: 7.50hf1, 7.51 (+2 hotfixes), 7.55 (+2 hotfixes), 7.56 (+1 hotfix).

### Staleness: Cammy is four patches behind and nothing has broken

I scanned the current `ffxiv_dx11.exe` `.text` section for every signature Cammy and
Hypostasis use. **All 23 resolve to exactly one address.**

```
Hypostasis/InputData
  OK  isInputIDHeld                  0x140619BE3 -> 0x140634A60
  OK  isInputIDPressed               0x140619F0D
  OK  isInputIDLongPressed           0x14077E581 -> 0x140634CE0   [CS calls this IsInputIdHeld]
  OK  isInputIDReleased              0x140618EAE -> 0x1406350D0
  OK  getAxisInput                   0x1406837A2 -> 0x140635280
  OK  getMouseWheelStatus            0x1418ACFEF -> 0x140619130
  OK  getInputBinding                0x140635CC0
Cammy/Game
  OK  ForceDisableMovement (static)  0x14182F76E
  OK  FoVDelta (static)              0x1418A7B8E
  OK  cameraNoClippy asm patch site  0x1418A408E
Hypostasis/GameCamera
  OK  GameCamera ctor                0x1418AB710   (== ClientStructs Camera ctor)
  OK  setCameraLookAt (vf15)         0x1418A4FE0
  OK  getCameraAutoRotateMode        0x1418A8992
  OK  getCameraMaxMaintainDistance   0x1418A82AC
  OK  updateLookAtHeightOffset       0x1418A4E80
  OK  shouldDisplayObject            0x1418B1245
  --  getZoomDelta (vf29)            137 matches — by design, see below
Hypostasis/other
  OK  CameraManager ctor, InputData ctor, cancelEmote, getWorldBonePosition,
      contentsReplayModule, getGameObjectFromPronounID
```

`getZoomDelta`'s `F3 0F 10 05 ?? ?? ?? ?? C3` matching 137 times is intentional —
`GameCamera.cs:65` comments "This sig is meant to match multiple things". It is never
scanned globally: `VirtualFunction.ScanAddress` (`Game/VirtualFunction.cs:18-22`) scans
*within the function the vtable slot already points at*, so the signature is a verifier,
not a locator.

Struct layout is equally intact. Diffing ClientStructs' `Camera.cs` and `CameraBase.cs`
from 2026-05-04 to today shows **only additions** (`GetCameraInputSource`,
`CameraControlMode.LockonUnk6`, `CameraInputSource`). No offset and no vtable index moved.

### Vtable indices check out too — but there is a decoy vtable next door

Cammy also hardcodes vtable indices (`GameCamera.cs:53-65`), which are not protected by
signature resilience, so I walked the table in the live binary.

The camera's vtable is **`0x1422E3AE8`**. Confirmed three independent ways:

```
  vtbl[ 3] = 0x1418AC420   Update                    <- what we hook
  vtbl[15] = 0x1418A4FE0   40 53 48 83 EC 30 44 8B 89 84 01 00 00 48 8B DA
                           ^ matches Hypostasis' setCameraLookAt signature exactly
  vtbl[18] = 0x1418A9970   getCameraTarget           <- Hypostasis 18 == ClientStructs 18
  vtbl[28] = 0x1418AE180   F3 0F 10 05 ?? ?? ?? ?? C3   float getter
  vtbl[29] = 0x1418AE190   F3 0F 10 05 ?? ?? ?? ?? C3   float getter
                           ^ both match Hypostasis' getZoomDelta verifier sig, and
                             ClientStructs' GetZoomSpeed / GetZoomModeToggleSpeedMultiplier
```

So Hypostasis' indices and ClientStructs' indices describe the same table and agree.
**Cammy's camera hooks are intact on 7.56.**

**The trap I fell into, recorded so nobody repeats it.** 112 bytes earlier there is a second
vtable at `0x1422E3A78` whose slots 2 and 3 are `C2 00 00` — bare `ret 0` stubs. It is the
one the constructor at `0x1418AB710` installs at `this+0x00` (`lea rax,[0x1422E3A78];
mov rbx,rcx; mov [rcx],rax`), which is the ctor both ClientStructs and Hypostasis label as
the camera ctor. A derived constructor must replace it with `0x1422E3AE8` later; I did not
trace that. Indexing the decoy gives plausible-looking but wrong answers — it is what led me
to briefly and incorrectly conclude Cammy was broken.

**Verification for our own code:** `CameraController.cs:34` logs
`[camera] hooking Update at 0x…`. It should read **`0x1418AC420`**. If it reads
`0x1418B97B0` we are hooking the decoy's empty stub, which would be a real bug.

**Why Cammy still works:** its signatures target stable instruction patterns rather than
absolute addresses, its vtable indices still land on the right functions, and the camera
and input structures have not been touched by SE in four patches. Nothing here is stale.
The staleness risk to plan for is future, not present.

---

## 1. Plugin bootstrap

Hypostasis wraps Dalamud in an abstract base class rather than a service locator.

- `DalamudPlugin` ctor (`Hypostasis/Dalamud/DalamudPlugin.cs:14-80`) runs a two-phase init.
  Phase one (`:20-35`) does `Hypostasis.Initialize` → `SetupConfig` → `PluginCommandManager`,
  and on failure calls `Dispose()` and sets state `Failed` — it does **not** rethrow, so a
  broken plugin unloads cleanly instead of wedging Dalamud. Phase two (`:42-77`) does
  signature injection, the derived `Initialize()`, and module loading.
- Events are wired by reflection (`:52-59`): `Framework.Update`, `UiBuilder.Draw` and
  `UiBuilder.OpenConfigUi` are only subscribed if the derived type actually overrides them
  (`derivedType.DeclaresMethod(...)`).
- `DalamudApi` (`Hypostasis/Dalamud/DalamudApi.cs:12-203`) is a static holder of every
  `[PluginService]`, populated by `pluginInterface.Inject(this)` (`:201`) — an instance
  injection that fills static properties.
- The one genuine addition over stock Dalamud is `SigScannerWrapper`
  (`Hypostasis/Dalamud/SigScannerWrapper.cs`): attribute-driven injection
  (`[HypostasisSignatureInjection]`, `[HypostasisClientStructsInjection<T>]`) that resolves
  a signature or a ClientStructs static and assigns it to a field — pointer, delegate, or
  `Hook<T>` with the detour found by naming convention (`:309-356`). It also tracks hooks
  for disposal (`:375-381`, `:461-466`) and validates hook targets sit on a real function
  boundary (`:395`, checks the preceding byte is `0xCC`).
- Teardown (`DalamudPlugin.cs:98-120`, `Hypostasis.cs:36-44`) unsubscribes events, disposes
  modules, disposes all tracked hooks, then `AsmPatch.DisposeAll()` restores every patched
  byte.

**Verdict: don't lift.** This is generic plugin scaffolding solving problems we don't have
(Cammy has ~15 injected members across 6 files; we have 3). `Plugin.cs` with
`[PluginService]` statics is already the same thing, smaller. The one idea worth stealing is
`AsmPatch`'s "record old bytes, restore on dispose" discipline — and only if we ever need an
asm patch, which (see §4) we may not.

---

## 2. Camera access

- Cammy does **not** sig-scan for the camera. `Common.cs:30-40` uses
  `[HypostasisClientStructsInjection<FFXIVClientStructs...CameraManager>(Required = true)]`,
  which resolves ClientStructs' own `CameraManager.Instance()` and reinterprets it as
  Hypostasis' struct. The `GameStructure` ctor signature on `CameraManager.cs:5` and
  `GameCamera.cs:9` is only used for debug tooling, not for locating anything.
- `CameraManager` (`Hypostasis/Game/Structures/CameraManager.cs:8-13`) overlays
  ClientStructs' struct at `0x0` and re-declares the camera pointers as its own
  `GameCamera*`: `worldCamera` 0x0, `idleCamera` 0x8, `menuCamera` 0x10,
  `spectatorCamera` 0x18. ClientStructs names the same slots
  `Camera` / `LowCutCamera` / `LobbyCamera` / `SpectatorCamera`
  (`Client/Game/Control/CameraManager.cs:17-21`) — note slot 1 and 2 disagree
  (`idleCamera`/`menuCamera` vs `LowCutCamera`/`LobbyCamera`). ClientStructs is current and
  its names match the class hierarchy; **trust ClientStructs here.**
- `GameCamera` (`Hypostasis/Game/Structures/GameCamera.cs:12-45`) is a hand-rolled
  `[StructLayout(Explicit)]` with 26 fields. Every one of them now exists in ClientStructs
  with a better name:

| Hypostasis | offset | ClientStructs equivalent |
|---|---|---|
| `x/y/z` | 0x60 | `CameraBase.SceneCamera.Object.Position` |
| `lookAtX/Y/Z` | 0x90 | `CameraBase.SceneCamera.Vector_1` ⚠ see note |
| `currentZoom` | 0x124 | `Camera.Distance` |
| `minZoom` / `maxZoom` | 0x128/0x12C | `Camera.MinDistance` / `MaxDistance` |
| `currentFoV` / `minFoV` / `maxFoV` | 0x130/0x134/0x138 | `Camera.FoV` / `MinFoV` / `MaxFoV` |
| `currentHRotation` / `currentVRotation` | 0x140/0x144 | `Camera.DirH` / `DirV` |
| `minVRotation` / `maxVRotation` | 0x158/0x15C | `Camera.DirVMin` / `DirVMax` |
| `mode` | 0x180 | `Camera.ZoomMode` (`CameraZoomMode` enum) |
| `controlType` | 0x184 | `Camera.ControlMode` (`CameraControlMode` enum) |
| `interpolatedZoom` | 0x18C | `Camera.InterpDistance` |
| `viewX/Y/Z` | 0x1C0 | `Camera.LastPosition` |
| `tilt` | 0x170 | *(no CS name; `TiltOffset` is 0x1E4, different field)* |
| `lookAtHeightOffset` | 0x234 | *(no CS name)* |
| `lockPosition` | 0x2C0 | *(no CS name; also **past** CS's `Size = 0x2C0`)* |

⚠ Offset 0x90 collision: Hypostasis calls it `lookAtX/Y/Z`, ClientStructs calls the same
address `Vector_1` and puts `LookAtVector` at 0x80. Both cannot be "look at". We already
write 0x90 as the **up vector** and derived that formula empirically from captured in-game
data, and it fixed the roll — so our reading is the one with evidence behind it. Treat
Hypostasis' name as wrong. Worth noting Cammy only writes 0x90 on the **title screen**
(`FreeCam.cs:283-285`), never in-world, so its name was never tested in-world either.

**Verdict: replace with ClientStructs.** Four of Hypostasis' fields have no CS name
(`tilt`, `lookAtHeightOffset`, `lockPosition`, and `0x2C0` sits outside CS's declared struct
size) — those are the only ones worth hand-declaring, and only if we need them. We currently
need none of them.

---

## 3. Camera control: what is hooked, and where in the frame

`Game.Initialize()` (`Cammy/Game.cs:169-184`) installs nine hooks. Five are vtable
functions on the world camera, four are sig-scanned member functions.

| Hook | Index / sig | Why |
|---|---|---|
| `setCameraLookAt` | vf15 | FreeCam: **return without calling original** (`Game.cs:38-42`), so the game never sets look-at |
| `getCameraPosition` | vf16 | FreeCam: **overwrite the out-param** with `FreeCam.Position` (`Game.cs:105-108`) |
| `getCameraTarget` | vf18 | spectate: return focus/soft target instead of the player |
| `canChangePerspective` | vf23 | FreeCam: return false, blocking 1st↔3rd person toggle |
| `getZoomDelta` | vf29 | return the preset's zoom-per-notch; FreeCam preset sets it to **0** |
| `getCameraAutoRotateMode` | sig | return 4 while flying/spectating |
| `getCameraMaxMaintainDistance` | sig | legacy-control distance fix |
| `updateLookAtHeightOffset` | sig | apply preset height offset |
| `shouldDisplayObject` | sig | hide your own character in first person |

Vtable index cross-check against the live binary (see §0): indices **15** and **18** both
hold what Hypostasis says they hold, and ClientStructs agrees at 18. Indices 16 and 23 are
unnamed in ClientStructs, so no conflict. Index **29 is a naming disagreement only**:
ClientStructs calls it `GetZoomModeToggleSpeedMultiplier`, Cammy calls it `getZoomDelta`,
and both point at the same `F3 0F 10 05 ?? ?? ?? ?? C3` float getter.

**Frame position.** Cammy's per-frame work is ordinary `Framework.Update`
(`Cammy.cs:119-133` → `FreeCam.Update()`), which only reads input and integrates
`FreeCam.position`. The actual camera write happens **inside the game's own camera update**,
whenever the engine calls `getCameraPosition`. So Cammy never writes camera memory on a
schedule — it supplies a value when asked. *(Inferred: I can't cite the call site without
disassembling the camera update, but it follows necessarily from `getCameraPosition` being
the only thing that moves the camera, plus the fact that collision still applies to the
result — see §4.)*

---

## 4. FreeCam end to end

**Enable** (`FreeCam.cs:92-128`):
1. `EnableInputBlockers()`
2. capture `gameCamera` (menu camera at title, world camera otherwise, `:99`)
3. seed `position` from `viewX/Y/Z` — i.e. `Camera.LastPosition` 0x1C0, **not** the scene
   camera position (`:103`)
4. save `prevZoom`, `prevFoV`, previous preset (`:105-107`)
5. apply `freeCamPreset` (`:81-90, :110-111`): FoV pinned to current, zoom pinned to
   0.06 via `MinZoom = MaxZoom = StartZoom = 0.06`, `ZoomDelta = 0`, vertical rotation
   opened to ±1.559 rad
6. `gameCamera->mode = 1` (force third person)
7. `Game.cameraNoClippyReplacer.Enable()` — the asm patch
8. `Game.ForceDisableMovement++` (`:117`)

**Position override.** Cammy keeps `position` as a managed `Vector3` and moves it itself
(`:259-286`), then hands it to the game in `GetCameraPositionDetour`. Direction is built
from the game's own angles (`:266-268`):

```csharp
var hAngle = gameCamera->currentHRotation + halfPI;
var direction = new Vector3(MathF.Cos(hAngle) * MathF.Cos(vAngle),
                            MathF.Sin(vAngle),
                           -(MathF.Sin(hAngle) * MathF.Cos(vAngle)));
```

**Rotation is never overridden.** Cammy suppresses `setCameraLookAt` and lets the game's own
mouse-look keep driving `currentHRotation`/`currentVRotation`. This is why Cammy needs no
up-vector maths and never had our roll bug — it never computes an orientation at all.

**Input suppression** (`:372-397`, aptly commented `// Obnoxious`): hooks
`isInputIDHeld`, `isInputIDPressed`, `isInputIDLongPressed`, `isInputIDReleased` to return
false for any id in its keybind table, `getAxisInput` to return 0 for axes 3 and 4, and
`EmoteController.cancelEmote` to return false so your character keeps emoting while you fly.
Cammy then reads the same ids through `.Original(...)` (`:190`, `:224-246`) so it still sees
the keys it just hid from the game.

**Critically: this is not what stops you walking.** Movement is stopped by
`Game.ForceDisableMovement++` (`Game.cs:32-34`, `FreeCam.cs:117`), a static int the game
treats as a refcount. The input hooks exist so movement keys don't fire *other* actions.

**Collision.** `cameraNoClippyReplacer` (`Game.cs:17`) patches a call site to
`xor al, al; nop; nop; nop`, forcing the collision check to return false. Cammy needs this
precisely *because* it feeds position into the pipeline before collision runs.

**Handing control back** (`:130-148`): disable input blockers, decrement
`ForceDisableMovement`, re-apply the default preset, restore `currentZoom`,
`interpolatedZoom` and `currentFoV` from the saved values, null the camera pointer, and
disable the asm patch unless the user wants no-clip permanently. There are three exit paths:
the keybind, `ForceDisableMovement` hitting 0 from outside (`:206` — a nice safety valve),
and plugin dispose (`Cammy.cs:165-166`).

---

## 5. What fights the game, and how Cammy wins

| Game behaviour | Cammy's counter |
|---|---|
| Camera update recomputes position every frame | Supply the position from inside the pipeline (`getCameraPosition` detour) rather than overwriting after |
| Camera wants to look at the target | Suppress `setCameraLookAt` entirely; keep the game's own angles |
| Zoom springs back to a configured distance | Pin `MinDistance = MaxDistance`, and return 0 from `getZoomDelta` |
| Collision pushes the camera out of geometry | Asm-patch the collision check to return false |
| Movement keys walk the character | `ForceDisableMovement++` |
| Movement keys also trigger other actions | Hook the four `isInputID*` queries to lie about those ids |
| First/third person toggle | `canChangePerspective` → false |
| Auto-rotate | `getCameraAutoRotateMode` → 4 |
| Emotes cancel when you press a key | `cancelEmote` → false |

---

## 6. Mapping to our failures

### 6.1 Save corruption — root cause now evidenced

We wrote `Camera.Distance` (0x124) every frame; a character's saved camera settings were
corrupted and survived relog, client restart and disabling Dalamud. Fixed by
Control Settings → Return to Default.

ClientStructs explains the whole chain:

- `Camera` has `[VirtualFunction(24)] SaveConfigOptions()` and
  `[VirtualFunction(25)] LoadConfigOptions()` (`Client/Game/Camera.cs:75-79`).
- It caches nine config-option ids at 0x290–0x2B0 (`Camera.cs:47-55`):
  `ConfigOption_{FirstPerson,ThirdPerson,Lockon}Default{YAngle,Zoom,Distance}`.
- Those map exactly, in order, to `ConfigOption` 179–187 — the block ClientStructs labels
  `// <Game Camera Settings>` (`Client/UI/Misc/ConfigOption.cs:258-267`).

So: the camera persists its live distance into `ThirdPersonDefaultDistance`, which is a
saved character setting, which is what "Return to Default" on Control Settings resets.
*(The last link — that `SaveConfigOptions` reads `Distance` specifically — is inference from
the naming and the observed behaviour, not from disassembly.)*

**Cammy does not solve this.** `FreeCam.Toggle` writes `currentZoom` on enable
(0.06 via the preset) and restores it on disable (`:143`). If a player crashes or the client
dies mid-freecam, Cammy leaves 0.06 in `Distance` with the same persistence hazard — the
exact failure you asked about. Cammy's mitigations are that it writes once rather than every
frame, and that it restores the *captured* previous value rather than a constant.

Our current fix — never write `Distance` at all (`CameraAccess.cs:83-86`) — is **strictly
safer than Cammy's.** Keep it.

### 6.2 WASD never appeared in the input probe

Our probe logged only ids `348` and `281`. ClientStructs names them: `348 = JUMP`,
`281 = CMD_CHAT` (`Client/System/Input/InputData.cs:464, 397`). Movement is
`321-326 = MOVE_FORE/BACK/LEFT/RIGHT/STRIFE_L/STRIFE_R` (`:437-442`).

The probe could never have found them. It logs ids **the game asks about**; the game does
not route character movement through `IsInputId*` — it reads the move controller directly,
which is why `ForceDisableMovement` is the lever and why Cammy's comment places it at
`g_PlayerMoveController + 0x54C` (`Game.cs:32`). Cammy calls `isInputIDHeld(321)` itself and
gets a true answer — the function works fine, the game just isn't the one calling it.

Also: ClientStructs has the **full `InputId` enum**. Cammy's magic-number dictionary
(`FreeCam.cs:61-79`) is obsolete; we should use `InputId.MOVE_FORE` etc.

### 6.3 `IsInputIdHeld` is misnamed in ClientStructs — confirmed

| Signature | ClientStructs name | Hypostasis name | Binary |
|---|---|---|---|
| `E8 ?? ?? ?? ?? 84 C0 74 37 EB 06` | `IsInputIdHeld` | `isInputIDLongPressed` | 0x140634CE0 |
| `E9 ?? ?? ?? ?? B9 4F 01 00 00` | *(absent)* | `isInputIDHeld` | 0x140634A60 |

Two distinct functions. Our earlier conclusion stands, and the addresses confirm they're
different targets. ClientStructs' name is wrong; keep our own scan for the real one.

### 6.4 🔴 `MovementLock` is broken — concrete, untested-but-predictable

`MovementLock.cs:20` calls
`GetStaticAddressFromSig("F3 0F 10 05 ?? ?? ?? ?? 0F 2E C7", offset: 4)`.
We took `Offset = 4` from Hypostasis' attribute. **The two `offset`s are not the same thing.**

Dalamud's implementation (`Dalamud/Game/SigScanner.cs:161-186`) is:

```csharp
var instructionAddress = (byte*)this.ScanText(signature);
instructionAddress += offset;                       // offset = where the INSTRUCTION starts
var decoder = Decoder.Create(64, reader, ...);      // Iced decodes from there
// returns the first operand with OpKind.Memory -> instruction.MemoryDisplacement64
```

Hypostasis passes **offset 0** to the scanner (`SigScannerWrapper.cs:267` omits the
parameter) and then adds 4 to the **resulting static address**
(`SigScannerWrapper.cs:294`: `address += attribute.Offset`).

Resolved against the live binary:

```
match          0x14182F76E   F3 0F 10 05 22 0C 27 01  0F 2E C7 ...
               = movss xmm0, [rip+0x1270C22]
Cammy:  offset 0 -> Iced decodes the movss -> S = 0x142AA0398  (.data)
        then S + 4                          ->   0x142AA039C  (.data)  <- the counter

Ours:   offset 4 -> Iced decodes from 0x14182F772, i.e. the middle of the
                    movss's own disp32:  22 0C 27 -> `and cl, [rdi]`
                    Op1Kind is Memory, displacement 0 -> returns 0
```

So `GetStaticAddressFromSig` returns **0**, no exception. `counter` becomes null,
`Available` is false, and `Hold()` returns silently at `MovementLock.cs:31` without logging
anything. The lock is a no-op and fails quietly.

**Falsifiable prediction:** the log line from `MovementLock.cs:21` reads
`[movement] lock counter at 0x0`. If it says `0x142AA039C` I'm wrong about this.

The fix is to pass offset 0 and add 4 to the result — and to treat a 0 return as failure.

### 6.5 Our manual jmp-follow in `InputBlocker` is redundant

`InputBlocker.cs:88-93` reads the matched byte and follows `E8`/`E9` manually. Dalamud's
`ScanText` already does exactly that (`SigScanner.cs:270+`, helper `ReadJmpCallSig` at
`:387-403`, `sigLocation + 5 + rel32` — byte-for-byte our arithmetic).

It happens to be harmless today: I checked each resolved target's first byte in the binary
and they are all `0x48`/`0x40` (normal prologues), so the second follow never triggers. Our
logged addresses (`0x140634A60`, `0x140619130`) match the single-follow result exactly.

But it is a latent trap — any signature that resolves to a jmp thunk would get followed
twice and hook the wrong function. Delete the block; keep the logging.

### 6.6 Architecture: ours is different, not worse

| | Cammy | Us |
|---|---|---|
| Where the write happens | inside the pipeline, via `getCameraPosition` | after `CameraBase.Update()` returns |
| Rotation | game's own; `setCameraLookAt` suppressed | we compute look-at and up vector |
| Collision | applies → **needs an asm patch** | already bypassed, no patch needed |
| Zoom fights | pinned via preset + `getZoomDelta`→0 | suppressed at the mouse wheel |

Our post-Update overwrite is why the camera passes through walls for free and why we need no
`AsmPatch` machinery. The cost is that we fight the game's own state for anything we don't
overwrite, which is where the roll and zoom bugs came from. Both approaches are defensible;
ours is less code and less invasive. I'd keep it.

The one thing worth borrowing outright is Cammy's **exit-path discipline**: `ForceDisableMovement == 0`
as a dead-man's switch (`FreeCam.cs:206`) is a good pattern for "someone else reset the world,
get out".

---

## 7. Component table

| Component | What it does | Verdict | Reason |
|---|---|---|---|
| `DalamudPlugin` / `DalamudApi` / `PluginCommandManager` | plugin base class, service statics, attribute commands | **not needed** | our `Plugin.cs` does this in 1 file; Hypostasis solves scale we don't have |
| `SigScannerWrapper` injection attributes | attribute-driven sig/hook binding | **not needed** | 3 signatures total; explicit scans are clearer |
| `PluginModuleManager` | enable/disable feature modules | **not needed** | no module system |
| `Hypostasis.GameCamera` struct | 26 hand-rolled offsets | **replace with ClientStructs** | every field has a CS name; CS is current, Hypostasis' `lookAtX` at 0x90 is wrong |
| `Hypostasis.CameraManager` struct | camera slot pointers | **replace with ClientStructs** | CS slot names match the class hierarchy; Hypostasis' `idleCamera`/`menuCamera` disagree |
| `Hypostasis.InputData` struct | input queries | **replace, except one** | CS has all four queries *and* the `InputId` enum; only the real `isInputIDHeld` sig is missing |
| `isInputIDHeld` sig `E9 ?? ?? ?? ?? B9 4F 01 00 00` | the true held-key query | **lift** | absent from CS; CS's `IsInputIdHeld` is the long-press function |
| `getMouseWheelStatus` sig | raw wheel notches | **lift** | absent from CS; already working for us |
| `getAxisInput` sig | controller axes | **lift when we do controller** | absent from CS |
| `ForceDisableMovement` sig + refcount | stops the character walking | **lift, fix the offset** | absent from CS; the only thing that actually stops movement — see §6.4 |
| `FoVDelta` static | FoV step per notch | **defer** | only needed if we expose FoV zoom |
| `cameraNoClippyReplacer` asm patch | disable camera collision | **not needed** | our post-Update write already bypasses collision |
| `AsmPatch` class | patch bytes, restore on dispose | **not needed now** | no patch required; revisit only if we ever need one |
| Input-id blocking (4 hooks + keybind table) | stop move keys firing other actions | **lift, but use `InputId`** | replaces our magic numbers; note it does *not* stop movement |
| `EmoteController.cancelEmote` → false | keep emoting while flying | **lift later** | genuinely useful for event/cinematic work; not v1-critical |
| `canChangePerspective` → false | block 1st/3rd toggle | **lift later** | cheap polish |
| `getCameraAutoRotateMode` → 4 | stop auto-rotate | **evaluate** | we may not need it given we overwrite post-Update |
| `getZoomDelta` → 0 | kill zoom at source | **evaluate vs our wheel hook** | cleaner than hooking the wheel, but see open question on vf29 |
| `shouldDisplayObject` hook | hide own character | **not needed** | first-person only |
| View bob / presets / spectate / QoLBar IPC | Cammy features | **not needed** | out of scope |
| `Graphics.Scene.Camera.WorldToScreenPoint` / `ScreenPointToRay` | world↔screen | **use, from CS** | both verified present and resolving; this is what the 3D overlay and gizmo need |
| `Dalamud.Bindings.ImGuizmo` | gizmo widget | **use, ships with Dalamud** | `Manipulate`, `SetDrawlist`, `SetRect`, `ViewManipulate`, `DrawGrid` all present |

### Dalamud API drift (verified against tag 15.0.3.5 and the shipped DLL)

- **`IClientState.LocalPlayer` and `LocalContentId` no longer exist.** Moved to
  `IObjectTable.LocalPlayer`. Cammy is already correct (`Game.cs:85`); we don't use either.
- `IGameGui.GetAddonByName` now returns `AtkUnitBasePtr`, not `nint`. Cammy's
  `addon.Address` usage (`FreeCam.cs:154-156`) matches.
- `SafeMemory` is `Dalamud.SafeMemory`, not `Dalamud.Memory.SafeMemory` — it does exist and
  `AsmPatch` compiles.
- Our `IClientState` event signatures are correct: `Logout` is
  `LogoutDelegate(int type, int code)`, `TerritoryChanged` is `Action<uint>`
  (`Dalamud/Plugin/Services/IClientState.cs:30, 40, 71`).
- `Hook<T>.FromAddress(nint, T, bool, Assembly)` still matches the 4-arg reflection call
  Hypostasis makes (`SigScannerWrapper.cs:349`).

---

## 8. Open questions

1. **vf29 naming.** Indices 28 and 29 are both `F3 0F 10 05 ?? ?? ?? ?? C3` float
   getters, so I can confirm the shape but not which is which. ClientStructs calls 28
   `GetZoomSpeed` and 29 `GetZoomModeToggleSpeedMultiplier`; Cammy hooks 29 as the
   zoom-per-notch delta (default 0.75). Only reading both in-game settles it. Matters only
   if we ever want to kill zoom at the source instead of at the mouse wheel.
2. **Does `SaveConfigOptions` read `Distance` or `SavedDistance`?** `Camera` has both
   (0x124 and 0x198). Which one feeds `ThirdPersonDefaultDistance` determines whether
   writing `Distance` is *sufficient* to corrupt, or whether something else copies it first.
   Our mitigation doesn't depend on the answer, but a safe "pin the zoom" feature would.
3. **When does `SaveConfigOptions` fire?** Logout, zone change, settings close, on a timer?
   This decides how large the crash-corruption window actually is for any plugin that writes
   `Distance` — including Cammy.
4. **Hypostasis' `lookAtX` vs ClientStructs' `Vector_1` at 0x90.** Our empirical up-vector
   reading works; I'd like to know what the field genuinely is before we rely on it further.
   Cammy only writes it at the title screen, so it's untested there too.
5. **`tilt` (0x170), `lookAtHeightOffset` (0x234), `lockPosition` (0x2C0)** have no
   ClientStructs names, and 0x2C0 is past CS's declared `Size = 0x2C0` for `Camera` —
   meaning it's really in a subclass. Unverified; only matters if we want camera tilt.
6. **Frame position of `getCameraPosition`** is inferred, not cited (§3). If we ever move to
   Cammy's in-pipeline approach, this needs confirming.
7. **Controller support** — `getAxisInput` is verified present but we haven't tested whether
   axes 3/4 are the right ids on the current patch.

---

## 9. Immediate implications

1. Fix `MovementLock`'s offset before testing again (§6.4) — the current build's lock cannot
   work, and it fails silently.
2. Delete the redundant jmp-follow in `InputBlocker` (§6.5).
3. Replace magic input ids with ClientStructs' `InputId` enum (§6.2).
4. Keep our "never write `Distance`" rule; it is safer than Cammy's (§6.1).
5. Keep our post-Update architecture; it buys us collision-free flight without an asm patch
   (§6.6).
