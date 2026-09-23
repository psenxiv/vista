# Vista — the User Guide window (Phase 4.8)

Date: 2026-09-23
Status: Approved

Brings forward the manual deferred in `FEATURES.md`: a **User Guide** window opened from a **?**
icon. This phase builds the window and the pipeline that turns Markdown into pages; the writing
comes after. Every page but Hotkeys is a placeholder.

## The ? icon

- A frameless icon button, **?** (`Question`), at the right end of the main window's top row, to
  the right of the Hide game UI eye.
- White while the guide is closed and blue while it is open, like the Camera button.
- Shown in every mode.
- Three places currently line up against the eye icon — the right-align, where the camera tools
  start, and the top row's minimum width — and all three make room for it.

## The window

- Titled **User Guide**, resizable, opening at about 760 × 520 with a sensible minimum. Its close
  button and Escape close it.
- **Left:** a fixed-width panel holding the topic tree. Topics with sub-topics expand with an arrow;
  clicking any entry, a topic or a sub-topic, shows its page. The shown page is highlighted.
- **Right:** a scrolling pane showing the page.
- It opens on the first topic, and remembers the page shown for as long as the plugin runs. Not
  saved between sessions.

## Content

Markdown files in `src/Vista.Plugin/Guide/`, embedded in the plugin at build time, so there is
nothing to install and nothing to go missing.

**The tree** comes from `index.md`, a nested bullet list of links. Order in the file is order in the
tree; an indented entry is a sub-topic of the one above it:

```markdown
- [Getting started](getting-started.md)
- [Aim](aim.md)
  - [Look At](aim-look-at.md)
```

**Pages** are rendered from this subset. Anything else is shown as plain text:

| Markdown | Rendered |
|---|---|
| `#`, `##`, `###` | headings in a larger size of the same font |
| blank-line-separated text | wrapped paragraphs |
| `- ` or `* ` | bullet list, one level |
| `1. ` | numbered list, one level |
| a pipe table with a `---` separator row | a table |
| `---` on its own line | a divider |
| `**bold**` | the accent colour; ImGui has no bold weight |
| `` `code` `` | a muted colour, for key names and values |
| `[text](target)` | the text only; links do not navigate yet |

Headings use Dalamud's default font at larger sizes, built through the plugin's font atlas, so they
stay crisp rather than scaled.

## Core and plugin

Parsing is pure and lives in Core, where it is tested:

- `GuideIndex.Parse(markdown)` returns the topic tree: title, file, children.
- `GuideMarkdown.Parse(markdown)` returns blocks — heading, paragraph, bullet list, numbered list,
  table, divider — whose text is a list of spans: plain, bold, code.

The plugin reads the embedded files, holds the heading fonts, draws the tree, and lays spans out
word by word so that mixed colours still wrap to the pane's width.

A page that fails to load shows a one-line error in the pane instead, and logs it.

## The seed outline

Placeholder pages, each a heading and a line saying it has not been written yet:

- Getting started
- Modes — Edit, Live
- Tracks and points — Adding points, The Point window
- Timing — Legs and holds, The timing graph
- Aim — Look At, Watch Target, Follow Target
- Scenes and anchors
- Playlist
- The Camera window
- **Hotkeys**, top level, as asked

Hotkeys is seeded with the keys table from the README rather than a placeholder: it already exists,
and it is the only page that exercises table rendering. That makes two copies of the table, the
README's and the guide's, to keep in step until one is made to follow the other.

## Not doing

- Links between pages, search, images, nested lists, persistence of the last page.
- Any real content beyond Hotkeys.

`FEATURES.md`'s deferred Manual entry is removed, since it is no longer deferred.

## Testing

Core, with expected block structures written out by hand from each input:

- The index: a flat list; a nested sub-topic under its parent; order kept; a line that is not a
  link ignored.
- Pages: each heading level; a paragraph joined from wrapped lines; blank lines separating
  paragraphs; bullet and numbered lists; a table with its header split from its rows and cells
  trimmed; a divider; bold and code spans inside a paragraph, a list item and a table cell; an
  unclosed `**` or backtick kept as plain text; a link reduced to its text.

The window itself goes on an in-game checklist.

## Build order

1. Core: the index and page parsers, then their tests.
2. Plugin: embedded resources, the ? button, the window, the fonts and the span layout.
3. The seed pages, `FEATURES.md`, and the checklist.
