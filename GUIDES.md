# Writing the User Guide

Rules for anyone writing or changing pages in Vista's in-plugin User Guide. Read this before you
touch a page.

The pages live in `src/Vista.Plugin/Guide/`. `index.md` holds the topic tree, and each page is its
own `.md` file.

## Who you are writing for

People who make screenshots and videos in FFXIV and want better camera shots. They are not
programmers. Most will open a page because they are stuck or curious about one thing, read it,
and go back to the game.

Write the way a patient friend would explain it sitting next to them, briefly.

## Clear, simple and short

- Use plain, everyday words. Say "use", not "utilise". Say "start", not "initiate".
- Keep sentences short, and put one idea in each.
- Write full sentences. Don't write in fragments or shorthand.
- Speak to the reader as "you", in the present tense and the active voice: "Press `Space` to play
  the track", not "The track can be played by pressing Space".
- Say what something is for before you say how to use it.
- If a word needs explaining, explain it the first time you use it on that page.

## What to leave out

- **Error handling and edge cases.** Don't describe what happens when a character isn't found, a
  button is greyed out, an action is refused, or a value is clamped. The reader finds these out by
  using Vista, and they rarely matter.
- **Limits and ranges**, unless the reader has to choose a value and would otherwise guess wrong.
- **Where a control is and what it looks like** when an icon already shows it. Show the icon.
- **Obvious consequences.** "**Level camera roll** levels the camera's roll" is the whole
  description. Don't add "so the horizon is flat" or "you don't need the window open to do this".
- **How it works inside:** class names, file names, maths, data formats.
- **History and plans:** what it used to do, phase numbers, versions, features that don't exist
  yet.

## Lengths

- A sub-page aims for 150 to 250 words. A topic page can be a little longer.
- Keep paragraphs to one to three sentences.
- A control gets one line saying what it does. Where a section covers several controls, put them in
  a table (see below) rather than a paragraph each.
- Use a numbered list for steps the reader follows in order, and a bullet list for things that
  don't have an order.
- Stop once the reader knows what to do. Don't add a summary.

## No AI speak

The guide should read as if a person wrote it. Leave out:

- **Em dashes and en dashes** used as punctuation. Use a full stop, a comma, or brackets instead.
  Hyphens inside words, like "ping-pong", are fine.
- Filler openers and closers: "It's worth noting that", "Simply", "Just", "In summary",
  "Overall", "In conclusion".
- Hype words: seamless, powerful, robust, intuitive, effortless, elevate, unlock, leverage,
  harness, delve, game-changer, cutting-edge.
- Padding in threes, and "not just X, but Y".
- Rhetorical questions, exclamation marks, and emoji.
- Bold used for emphasis. Bold is kept for the names of controls.

## Words

- Name every control exactly as it appears on screen or in its tooltip, in bold: "click **Fit**",
  "open the **Camera** window".
- Use the same word for the same thing every time. Vista's words are: scene, track, point, anchor,
  scene anchor, track anchor, leg, hold, key (in the timing graph), playlist, gizmo, and the modes
  Off, View, Edit and Live. The windows are the **Vista** window, the **Point** window, the
  **Camera** window, the **Timing** window and the **User Guide**. The main window's side panels
  are the Hierarchy and the Playlist.
- Each page starts with a `#` heading that matches its title in `index.md`. Use `##` for sections
  and `###` rarely.

## Formatting you can use

Use these to make a page easy to scan. Anything not listed here shows as plain text.

| Write | Shows as | Use it for |
|---|---|---|
| `# Title` | a large heading | the page title, once, at the top |
| `## Section` | a heading with a faint line above it | each section; the line is added for you |
| `### Sub-section` | a smaller heading | rarely, inside a long section |
| a blank line | a new paragraph | separating paragraphs |
| `- item` or `* item` | a bullet list, one level | things in no particular order |
| `1. step` | a numbered list, one level | steps in order |
| a pipe table with a `---` row under the header | a table | controls and what they do, keys, options |
| `---` alone on a line | a faint divider | separating parts of a page that has no `##` between them |
| `**Name**` | the accent colour | a control's name, exactly as on screen |
| `` `Space` `` | a keycap | a key; `` `Ctrl + Space` `` shows two caps joined by + |
| `` `/vista` `` | muted text, no keycap | a chat command, anything starting with `/` |
| `{icon:Camera}` | the icon itself | an icon-only button, next to or instead of its name |
| `[Playlist](playlist.md)` | an underlined link that opens the page | pointing to another page |

There are no images, no nested lists and no callout boxes.

### Icons

Every button in the **Vista** window that shows only an icon should appear on the page as its icon,
so the reader can match it to the screen. Write the icon with its name first:
"{icon:ChartLine} **Timing** opens the **Timing** window."

`{icon:Name}` takes a name from Dalamud's `FontAwesomeIcon`. To find a control's icon, search
`src/Vista.Plugin` for its tooltip text; the icon is the `FontAwesomeIcon` on the same line. A name
that doesn't exist shows as its tag, `{icon:Name}`, so check the page in game.

### Tables of controls

When a section covers several buttons, a table is clearer than prose:

```markdown
| | Button | Does |
|---|---|---|
| {icon:Undo} | **Undo** | Undoes your last change. |
| {icon:Redo} | **Redo** | Redoes it. |
```

Keep each "Does" cell to one short sentence.

### Links

Link to another page by its file name, as it appears in `index.md`: `[Timing](timing.md)`. Write
"See [Timing](timing.md)." rather than "The Timing page explains this." A link to a file that isn't
in `index.md` does nothing when clicked.

## Adding, moving or removing a page

- To add a page, create the `.md` file and add a line to `index.md`. Indent the line under another
  entry to make it a sub-topic.
- To remove a page, delete the file and its line in `index.md`, and fix any links to it.
- File names are lowercase words joined by hyphens, like `timing-graph.md`.

## Keep it true

- Check every instruction against the current plugin before you write it. If you can't confirm how
  something behaves, ask rather than guess.
- When a change to Vista adds, removes or changes something a user can see or do, update the pages
  that describe it in the same change.
- `hotkeys.md` repeats the keys table in `README.md`. If a key changes, update both.

## An example

Too long, and it explains the obvious:

> The button at the start of the Rotation row is **Level roll**. It sets the roll back to zero, so
> the horizon is flat. You don't need the window open to level the camera. The **Level camera
> roll** button on the top row of the **Vista** window does the same as **Level roll**.

Clear and short:

> | | Button | Does |
> |---|---|---|
> | {icon:RulerHorizontal} | **Level camera roll** | Levels the camera's roll. |
