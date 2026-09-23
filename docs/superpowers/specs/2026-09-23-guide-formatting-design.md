# Vista — User Guide formatting (Phase 4.8.1)

Date: 2026-09-23
Status: Approved

The first written pages ran long, and the renderer gave the reader nothing to scan: no pictures of
the controls, key names in a muted colour, and headings with no separation. This adds five things to
the guide's renderer, then rewrites the pages to shorter rules that use them.

## Inline icons

`{icon:Name}` in a page draws the FontAwesome icon of that name, inline with the text, where `Name`
is a member of Dalamud's `FontAwesomeIcon` (`{icon:Crosshairs}`, `{icon:ChartLine}`). The name is
the one the UI code uses, so the guide shows exactly what the button shows.

- Drawn as a plain icon in the text colour, as the buttons are: no frame or background.
- A name that isn't a `FontAwesomeIcon` member shows as its tag, `{icon:Name}`, in plain text, so
  a typo is visible when the page is read.

## Faint dividers

- `---` draws a faint line, not ImGui's full-strength separator.
- Every `##` heading gets the same faint line above it, unless it is the page's first block or a
  divider is already there. Writers don't add these by hand.

## Keycaps

A backtick span is a key and draws as a keycap: its text in the normal text colour inside a small
rounded, bordered box.

- `Ctrl + Space` draws as two caps with a plain `+` between them. The split is on ` + ` (a plus
  with a space each side).
- A span starting with `/` is a chat command, not a key, and keeps the muted text colour with no
  box: `/vista`.
- A cap never wraps across lines.

## Links

`[text](file.md)` shows `text` in the accent colour, underlined. Clicking it shows that page, as
clicking the page in the tree would, and the page starts scrolled to the top. The tree's highlight
follows. A link whose target is not a page in `index.md` does nothing when clicked.

Bold stays the accent colour without an underline, so the underline is what marks a link.

## Spacing

More space above headings and between blocks, so a page reads as sections rather than one run of
text. Exact values are chosen in game.

## Core and plugin

Parsing stays in `Vista.Core.Guide` and is tested; drawing stays in `GuideWindow`.

- `RunStyle` gains `Icon`, `Key` and `Link`, and loses `Code`, which becomes `Command` (the `/` case).
- `Run` gains an optional `Target`, set only on links.
- The parser inserts the automatic dividers, so the rule lives in one tested place.
- Resolving an icon name to `FontAwesomeIcon` happens in the plugin, since Core can't reference
  Dalamud.

## The rewrite

`GUIDES.md` gains these rules, and every page is rewritten to them:

- No error handling, edge cases, refusals or limits, unless the reader has to pick a value.
- A control gets one line saying what it does. Where a page describes several controls, they go in
  a table: icon, name, what it does.
- Show a control's icon wherever the text names an icon-only button.
- Point to other pages with links, not "see the X page".
- A sub-page aims for 150 to 250 words.

## Out of scope

Images, nested lists, callout boxes, and search.
