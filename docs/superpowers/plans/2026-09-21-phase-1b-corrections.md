# Phase 1b — corrections from the Cammy analysis

Source: `docs/superpowers/specs/2026-09-21-cammy-camera-analysis.md`.
Phase 1 (camera ownership) is done. This is a correction pass before phase 2, not new
features. Nothing here changes what the plugin does for the user except that movement
locking starts working.

Controller support is **out of scope, permanently** — not deferred, unsupported. Every
controller mention in the analysis (`getAxisInput`, controller keybinds, axis blocking)
is dropped and does not go in `FEATURES.md`.

## Decisions taken without asking

None of the analysis' open questions block this work. Recording the calls so they are
reviewable rather than silent:

- **Keep our post-`Update` overwrite architecture.** It bypasses collision for free and
  needs no asm patch. Cammy's in-pipeline approach needs one. No change.
- **Keep the mouse-wheel hook for zoom suppression.** Cammy kills zoom by hooking vfunc 29
  and returning 0. That is probably cleaner, but ClientStructs and Cammy disagree about what
  vfunc 29 *is* (`GetZoomModeToggleSpeedMultiplier` vs the zoom-per-notch delta — both 28
  and 29 are identical-shaped float getters). Our wheel hook is confirmed working in-game.
  Not trading a working thing for an ambiguous one.
- **Keep our empirical up-vector formula** (`CameraOrientation.UpFor`). Hypostasis names
  0x90 `lookAtX`, ClientStructs names it `Vector_1`; our formula was derived from captured
  in-game data and fixed the roll. Evidence beats naming.
- **Keep `Distance` unwritten.** The analysis proves the persistence path
  (`SaveConfigOptions` → `ConfigOption` 179-187). Cammy does *not* solve this; ours is safer.

## Tasks

Ordered. Task 1 gates the rest — until movement locks, nothing else can be tested in flight.

---

### Task 1 — fix `MovementLock`, which cannot currently work

`src/CinematicCam.Plugin/Game/MovementLock.cs`

We pass `offset: 4` to `GetStaticAddressFromSig`. Dalamud treats that as *where the
instruction starts* and decodes from there with Iced (`SigScanner.cs:161-186`). Hypostasis
passes **0** to the scanner and adds 4 to the **resolved address**
(`SigScannerWrapper.cs:267, 294`). Decoding from `match+4` lands mid-`movss`, hits
`and cl,[rdi]`, and returns 0 — so `counter` is null and `Hold()` returns silently.

Change:

```csharp
private const int CounterOffset = 4; // the counter sits one float past the movss target

var address = Plugin.SigScanner.GetStaticAddressFromSig(Signature);
if (address == 0) { /* log error, leave counter null */ }
counter = (int*)(address + CounterOffset);
```

Also treat a zero result as failure rather than silently producing a null pointer, and log
at error level so a future patch break is obvious at load.

**Verify:** log reads `[movement] lock counter at 0x142AA039C` (currently predicted
`0x0`). Then `/ccam fly`, press W — character stays put, camera flies. `/ccam release`,
press W — character walks again.

`fix(input) resolve movement counter with the right offset`

---

### Task 2 — stop `Snapshot`/`Restore`/`ResetToDefaults` touching `Distance`

`src/CinematicCam.Plugin/Game/CameraAccess.cs`

`Snapshot` is documented as "everything `WriteState` touches", but it carries `Distance`
and `InterpDistance` which `WriteState` deliberately no longer writes
(`CameraAccess.cs:31-33, 56-57`). `Restore` therefore writes a field we banned, from a
value captured at takeover that may be stale by release. `ResetToDefaults` is worse — it
writes a hardcoded `Distance = 6f` (`:66-67`), which is exactly the persistence hazard that
corrupted a character.

Change: drop `Distance` and `InterpDistance` from `Snapshot`, `Restore` and
`ResetToDefaults`. `ResetToDefaults` keeps only the FoV reset — which makes `/ccam reset`
a weaker command than its name suggests. It was added as a recovery tool for the corruption
we no longer cause, and it could never have fixed it anyway (that needs the game's own
reset). Keeping it is harmless; deleting it is also fine if you would rather.

The game exposes `Camera.ResetConfigOptions()` (vfunc 26) which is the engine's own
"return to default". It is tempting for a recovery command, but it writes persisted config
and is untested — **not doing it in this pass.** Noted as an open question.

**Verify:** `/ccam hold`, `/ccam nudge 0 2 0`, `/ccam release` — camera returns to normal
and zoom behaves as it did before. Relog and confirm the camera is unchanged.

`fix(camera) keep distance out of snapshot and reset`

---

### Task 3 — take the camera from the world camera, not the active camera

`src/CinematicCam.Plugin/Game/CameraAccess.cs`

`TryGetActiveCamera` uses `CameraManager->GetActiveCamera()`. At the title screen the
active camera is the lobby camera, and `CameraController.TryInstallHook` retries every
frame until it succeeds — so a load on the title screen can hook slot 3 of whichever camera
happens to be active. Cammy uses the world camera explicitly (`FreeCam.cs:99`).

Change: `TryGetWorldCamera` reading `CameraManager.Instance()->Camera` (slot 0). Rename the
method; the call sites are unchanged otherwise.

This is a robustness fix, not a confirmed bug — I have not proven the lobby camera's
`Update` is a different function. It removes the question rather than answering it.

**Verify:** the `[camera] hooking Update at 0x…` line should read **`0x1418AC420`**.
If it reads `0x1418B97B0` we are indexing the decoy vtable described in the spec, whose
slots 2 and 3 are `ret 0` stubs — that would be a real bug, so flag it.

`fix(camera) always take the world camera`

---

### Task 4 — replace input probing with a fixed keybind table

`src/CinematicCam.Plugin/Game/InputBlocker.cs`, `src/CinematicCam.Plugin/Plugin.cs`

The learn-then-block probe was built to discover which ids to suppress. The analysis
settles that it never could: the game does not route character movement through
`IsInputId*`, which is why only `348 JUMP` and `281 CMD_CHAT` ever appeared. Movement is
stopped by the counter in task 1, and input blocking has a different job — stopping flight
keys from firing other actions.

Changes:
- Delete `Learning`, `BlockWhatWasLearned`, `ClearBlocked`, the `seen` set and its lock,
  and the `/ccam inputprobe` and `/ccam inputclear` commands.
- Replace the learned set with a fixed `FrozenSet<InputId>` of the keys we fly with:
  `MOVE_FORE`, `MOVE_BACK`, `MOVE_LEFT`, `MOVE_RIGHT`, `MOVE_STRIFE_L`, `MOVE_STRIFE_R`,
  `JUMP`, `MOVE_DESCENT`, `MOVE_RETENTION`.
- Type the detours on ClientStructs' `InputId` enum instead of raw `uint`.
- Delete the manual `E8`/`E9` follow in `HookBySignature` (`:88-93`). `ScanText` already
  does it (`SigScanner.cs:270+`, helper `ReadJmpCallSig` at `:387-403`). Harmless today —
  every resolved target starts `0x48`/`0x40` — but it would silently double-follow any
  signature landing on a jmp thunk. Keep the resolved-address logging.

**Verify:** `/ccam fly`, then press each of W A S D Space Ctrl and confirm no stray action
fires (no jump, no autorun, no target cycling). Scroll — no zoom. `/ccam release`, confirm
all keys behave normally again.

`refactor(input) block a fixed keybind set instead of probing`

---

### Task 5 — REVERTED, and should never have been a task

Reading the player's own keybinds instead of WASD. Implemented, then reverted in
`5ac33c8`.

**This was not a correction and it was not mine to decide.** I justified it with an
invented claim about what users need. The requirement is: **assume QWERTY WASD.** The two
problems I said it fixed were not fixed by it — A/D turning instead of strafing was the
game still steering the character, which task 1 fixes, and the Cmd/Ctrl behaviour was
already looked at and accepted as a Wine quirk.

Free cam reads `Plugin.KeyState[VirtualKey.W]` and friends. Left alone.

### Task 6 — do not decrement a movement counter someone else cleared

`src/CinematicCam.Plugin/Game/MovementLock.cs`, `Plugin.cs`

The counter is shared. If another plugin or the game zeroes it while we believe we hold it,
our later `Release()` would decrement someone else's hold. Cammy uses the same condition as
a dead-man's switch to exit free cam (`FreeCam.cs:206`).

Change: expose the current count; in `OnFrameworkUpdate`, if `Movement.Held` and the count
is 0, clear `Held` without decrementing. **Keep flying** — an automatic bail-out would drop
a shot mid-take, which is worse than the leak it guards against.

**Verify:** hard to trigger deliberately; covered by the task 1 checks not regressing.
The safety net is that `Dispose` already releases, so disabling the plugin always recovers.

`fix(input) do not decrement a movement counter someone else cleared`

---

## Not doing

| | Why |
|---|---|
| Controller support | unsupported, per your call — not a deferred feature |
| `cameraNoClippyReplacer` / `AsmPatch` | our architecture already passes through geometry |
| Hypostasis scaffolding (`DalamudPlugin`, injection attributes, module manager) | solves scale we do not have |
| Hooking `getZoomDelta` / `canChangePerspective` / `getCameraAutoRotateMode` | all fight the game's camera logic, which our post-`Update` overwrite already sidesteps |
| `cancelEmote` → false (keep emoting while flying) | genuinely useful for event work, but phase 2+ |
| Spectate, view bob, presets, QoLBar IPC | Cammy features, out of scope |

## Risks

- **Task 5 is the only behaviour change a user would notice.** If reading input ids turns
  out to be unreliable in some state (cutscene, `/gpose`, mounted), flight controls die
  where they used to work. Mitigation: it is the last task, separately committed, and
  revertible on its own.
- **Task 1 is unverified until tested.** The predicted address `0x142AA039C` comes from
  static analysis of the on-disk binary; ASLR does not affect the offset from the image
  base, but a wrong prediction means the reasoning is wrong, not just the number.
- **Task 3 depends on an unproven claim.** I have not shown that the lobby camera's slot 3
  differs. If it does not, the change is harmless; it is cheap either way.

## Still open after this pass

1. Where the rest of the camera vtable sits on 7.56 — needed before we hook any camera
   vfunc other than `Update`.
2. Whether `SaveConfigOptions` reads `Distance` or `SavedDistance` (0x124 vs 0x198). Does
   not affect this pass; would matter for a "pin the zoom" feature.
3. When `SaveConfigOptions` fires — sets the size of the crash-corruption window.
4. Whether `Camera.ResetConfigOptions()` (vfunc 26) is a safe recovery command.
5. What 0x90 genuinely is (`Vector_1` vs `lookAtX`). Our formula works; the name does not
   matter until we need the field for something else.
