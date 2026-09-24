# In-game checklists

The page renders the checklists in `cases/`, listed in `cases/manifest.json`. `cases/` is gitignored: never commit a checklist. The user serves this folder themselves (VS Code Live Server), ticks each row Pass, Fail or N/A, and sends back the results JSON the page downloads. Don't add a serve script.

The page is plain files with no build step, and adding a checklist never needs them touched:

- `index.html`: the shell.
- `checks.css`: the look.
- `checklist.js`: the data, with no DOM: storage key, answers, skip and optional rules, progress, and results files.
- `view.js`: the HTML for the loader, the picker and a checklist.
- `page.js`: loading, events and saving.

They're classic scripts, not ES modules, so the loader still shows when the page is opened as a file.

## Adding a checklist

1. Write `cases/<name>.json`, named for the work (`ux-fixes.json`, `phase-4.json`).
2. Add its file name to `cases/manifest.json`, creating it if needed: `{ "checklists": ["<name>.json"] }`.
3. Never overwrite or reuse a checklist that hasn't been run. Several can be pending at once, and the page keeps each one's progress separately, keyed by file name and `revision`.
4. Delete the file and its manifest entry once its results are in.

Only write rows a Core test can't cover: ImGui, input, hooks, files on disk, or the game itself. The one exception is the camera regression scene pass, which CLAUDE.md asks for on every change to aim, timing or paths: its tests check the maths, and the pass checks how it looks.

## Shape

```json
{
  "title": "Shown as the page heading",
  "revision": 1,
  "intro": ["Paragraph.", "Another paragraph."],
  "sections": [
    {
      "title": "A. Section",
      "blurb": "Optional text under the heading.",
      "skip": "Optional: why this whole section isn't needed this pass.",
      "groups": [
        {
          "id": "A1",
          "title": "Group",
          "need": "Optional 'Why.' line.",
          "steps": [
            { "id": "A1.1", "t": "check", "action": "What to do.", "expect": "What should happen.", "why": "spec §1" }
          ]
        }
      ]
    }
  ]
}
```

- `intro` is optional but expected: a paragraph or two on what the checklist covers and how to start.
- Step and group `id`s must be unique across the file; answers are stored by id.
- Step kinds (`t`):
  - `check`: `action` and `expect`, with Pass / Fail / N/A. `why` is optional.
  - `do`: a tickbox, with `text`.
  - `note`: a plain line, with `text`.
  - `num` and `text`: an input, with `label` and optional `unit` and `placeholder`. A `text` step with `"long": true` is a text area.
  - `pick`: buttons, with `label` and `options`.
  - `row`: several number inputs, with `label` and `cols`.
- Any step, group or section can carry `skip` (a reason) to hide it behind the page's "show what I don't need" toggle, rather than deleting it. Steps and groups can carry `optional: true`. Skipped and optional steps never count towards progress or the results summary.
- Text supports `` `code` `` only; everything else is plain.
- Bump `revision` to make the page start a checklist fresh.
