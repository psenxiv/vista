# Vista — saving, loading and presets (3.f and 3.g)

Date: 2026-09-23
Status: Approved

Scenes are saved to disk as you work and reopen when the plugin starts. A track can be saved on its
own as a preset and added to any scene. This is `BRAINSPLAT.md`'s Saving and Presets sections, with
the gaps settled by the user on 2026-09-23. Where this spec and `BRAINSPLAT.md` differ, this spec
wins.

## 1. The save folder

The user picks a parent folder. Vista keeps its own folder inside it:

```text
<parent>/vistaxiv/
  scenes/    one JSON file per scene
  presets/   one JSON file per preset
```

There is no default: the user chooses the parent. (Revised 2026-09-23 after the first build; it was
the plugin's config folder.)

### 1.1 The Setup window

- Titled **Vista Setup**. The first `/vista` (or Dalamud's open button) opens it instead of the
  **Vista** window, while no folder has been chosen.
- It says "Vista needs a folder to save your scenes and presets. Choose one to continue.", or, when a
  chosen folder has gone, "Vista can't find its save folder. Choose it again, or a new one, to
  continue."
- Below, a folder icon button, **Choose folder**, opens Dalamud's folder picker
  (`FileDialogManager.OpenFolderDialog`), next to the chosen `vistaxiv` path in the accent colour,
  or "No folder chosen" in a muted colour. Nothing is chosen when it opens.
- A fixed-width **Ok**, right-aligned, disabled until a folder is chosen, creates
  `vistaxiv/scenes/` and `vistaxiv/presets/` if missing, saves the choice, closes Setup and opens the
  **Vista** window.
- It can't be skipped: while there is no working folder it has no close button and ignores Escape.
  Opened from the gear with a working folder, it can be closed without choosing.
- It opens by itself when the plugin loads and the chosen folder's `vistaxiv` folder can't be found,
  and when a save fails because the folder has gone.
- A gear button, **Save folder**, on the main window's top row opens it at any time, showing the
  folder in use. It sits left of
  the **?**. The top row's three width sites count it.

### 1.2 Changing folder

Choosing a different folder saves the open scene where it is, then switches to the new folder and
opens its first scene by name, or makes **Scene 1** there if it has none. Nothing is copied.

## 2. Scenes

### 2.1 Names and files

- A scene's name is its file's name, `<name>.json` in `scenes/`. The name is not stored in the file,
  so renaming a file by hand renames the scene.
- A name is valid when, trimmed, it is 1 to 64 characters, contains none of `< > : " / \ | ? *` or
  control characters, doesn't end in a full stop, isn't a Windows device name (`CON`, `PRN`, `AUX`,
  `NUL`, `COM1`–`COM9`, `LPT1`–`LPT9`), and no other scene has it ignoring case.
- The same rules apply to preset names, among presets.

### 2.2 What a scene file holds

Everything in the `Scene` record: its tracks in Hierarchy order with all their settings, points and
timing, which are hidden, the playlist and its loop setting, and the scene anchor. Not saved: the
edited track, the selection, the scrub head, the mode, and undo history.

The file carries `"format": 1`. A file with another format, or one that can't be read, is left out
of the scene list and logged as a warning. So is one whose values are out of range: speeds, holds,
aim height, smoothing and repeats outside the editor's limits, a point's pitch past straight up or
down or a field of view outside 0 to 180°, anything non-finite, or a track that can't be played. The
same applies to presets. There is no migration: Vista is unreleased.

### 2.3 Loading and switching

- When the plugin starts with a folder chosen, it opens the last scene used (kept in the config). If
  that file is gone, it opens the first scene by name; if there are none, it makes **Scene 1**. With
  no folder chosen yet, the same happens when Setup's **Ok** is pressed.
- The last scene used is updated whenever a scene is loaded, renamed or deleted.
- Loading a scene edits its first track, clears the selection and scrub head, and clears undo
  history. The mode and the camera are left alone.
- Switching is offered only in Edit, from the scene selector. The camera stays where it is.
- The old `ClearScene` path is replaced by loading, and deleted.

### 2.4 Saving

- The open scene saves itself about a second after it last changed (debounced), so a drag saves once
  it stops.
- It also saves at once before switching scene, before changing folder, and when the plugin unloads.
- A save writes a temporary file beside the scene's and then replaces it, so a crash mid-write never
  leaves half a file.

### 2.5 The scene selector

The **Scene** label at the top of the Hierarchy becomes a drop-down showing the open scene's name.
It is enabled only in Edit. Opening it rescans `scenes/`, so a file dropped into the folder appears.
It lists every scene by name, the open one ticked, then:

| Item | Does |
|---|---|
| a scene's name | Saves the open scene and loads that one. |
| **New scene** | Asks for a name (default: the next free **Scene N**), saves the open scene, makes an empty scene with one track, **Track 1**, saves it and loads it. |
| **Rename scene** | Asks for a name (default: the current one) and renames the file. Undo history stays. |
| **Duplicate scene** | Asks for a name (default: **\<name\> copy**, then **\<name\> copy 2** and so on), saves a copy and loads the copy. |
| **Delete scene** | Asks "Delete \<name\>? This can't be undone.", deletes the file, then opens the first remaining scene by name, or makes **Scene 1**. |
| **Open folder** | Opens `scenes/` in the system's file browser, as Dalamud's installer opens folders (`Util.OpenLink`). |

The name prompt is a small modal with a text field and **Ok** / **Cancel**. While the name is
invalid, **Ok** is disabled and one line under the field says why, in red; the line is kept blank
otherwise, so the buttons don't move ("A scene with that name exists",
"That name can't be used as a file name").

## 3. Presets

A preset is one track saved on its own in `presets/`: all its settings, points, timing and Look At
point, relative to its anchor, plus the anchor's yaw in the world. Playlist entries are not part of
it.

### 3.1 Saving a preset

- The Hierarchy's right-click menu on a track gains **Save as preset**, disabled for a track with no
  points.
- It opens the name prompt, prefilled with the track's name. If a preset with that name exists, a
  line says "A preset called \<name\> exists" and **Ok** reads **Replace**.

### 3.2 Adding a preset

- The Hierarchy's **Add track** button opens a menu: **Empty track**, and **From preset**, a submenu
  listing presets by name, rescanned when opened, then **Open folder**, which opens `presets/`. With
  no presets it shows a greyed-out **No presets**.
- Adding one makes a new track with a new id, named after the preset, at the end of the Hierarchy,
  and edits it. It is one undo step.
- Its anchor goes on the ground under the camera, at the preset's saved yaw, and its points and Look
  At point follow. If the scene anchor isn't placed yet, it is placed at the same spot, as a first
  point would place it. If no ground is found, the camera's height is used.

### 3.3 Deleting a preset

Right-click a preset in the **From preset** submenu for **Delete**, which asks "Delete \<name\>? This
can't be undone." first.

## 4. Settings

`Configuration` gains `SaveFolder` (the chosen parent, null until chosen) and `LastScene` (a name).
Both save when they change.

## 5. Core and plugin

Core holds everything decidable without the game, and tests it:

- **Scene and preset JSON** (round trips, the format check).
- **Names** (validity, uniqueness ignoring case, next free **Scene N**, **copy** naming).
- **The folder** (list, load, save with replace, rename, delete, for scenes and presets), tested
  against temporary directories.
- **The debounce** (when a changed scene is due to save).
- **`SessionState.LoadScene`** and **adding a preset** (placement, new ids, one undo step).

The plugin holds the Setup window, the folder picker, the selector and prompts, the preset menus,
the gear button, the config, and calling save on its timer and on unload.

## 6. Also in this change

- `BRAINSPLAT.md`: remove "Bring scene to me" (three places) and "Re-anchor", which were dropped in
  3.e.1; scenes move by dragging the scene anchor.
- User Guide: a **Saving and scenes** page under Scenes and anchors (the selector, the save folder,
  auto-save) and a **Presets** page, both to `GUIDES.md`'s rules. `index.md` gains both.
- An in-game checklist.

## 7. Out of scope

Renaming presets in Vista, importing or exporting (sharing a scene means sharing its file), more
than one playlist per scene, and saving while a file is open in another program.
