# Writing the User Guide

Rules for anyone writing or changing pages in Vista's in-plugin User Guide. Read this before you
touch a page.

The pages live in `src/Vista.Plugin/Guide/`. `index.md` holds the topic tree, and each page is its
own `.md` file.

## Who you are writing for

People who make screenshots and videos in FFXIV and want better camera shots. They are not
programmers. Most will open a page because they are stuck or curious about one thing, read it,
and go back to the game.

Write the way a patient friend would explain it sitting next to them.

## Clear and simple, not short

Aim for clear and simple. That is not the same as concise. A slightly longer sentence that is easy
to follow beats a short one the reader has to decode.

- Use plain, everyday words. Say "use", not "utilise". Say "start", not "initiate".
- Keep sentences short, and put one idea in each.
- Write full sentences. Don't write in fragments or shorthand.
- Speak to the reader as "you", in the present tense and the active voice: "Press `Space` to play
  the track", not "The track can be played by pressing Space".
- Say what something is for before you say how to use it.
- If a word needs explaining, explain it the first time you use it on that page.

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
- Bold used for emphasis. Bold is kept for the names of controls (see below).

## Reasonable lengths

- A page should be readable in a minute or two. If it runs longer, split it into sub-topics.
- Keep paragraphs to two to four sentences.
- Use a numbered list for steps the reader follows in order, and a bullet list for things that
  don't have an order.
- Stop once the reader knows what to do. Don't add a summary of what you just said.

## It's a user guide, not a specification

Describe what the reader sees and does. Leave out:

- How it works inside: class names, file names, maths, data formats.
- History and provenance: what it used to do, why it was built this way, phase numbers, versions.
- Plans and features that don't exist yet. Only document what is in the current build.
- Long explanations of edge cases. If one matters to the reader, give it one sentence.

## Words and formatting

- Name every control exactly as it appears on screen or in its tooltip, in bold: "click **Fit**",
  "open the **Camera** window".
- Put keys in code formatting and name them in words: `Backtick`, `Space`, `Ctrl + Q`. Write key
  combinations with spaces around the plus.
- Use the same word for the same thing every time. Vista's words are: scene, track, point, anchor,
  scene anchor, track anchor, leg, hold, key (in the timing graph), playlist, gizmo, and the modes
  Off, View, Edit and Live. The windows are the **Vista** window, the **Point** window, the
  **Camera** window, the **Timing** window and the **User Guide**.
- Each page starts with a `#` heading that matches its title in `index.md`. Use `##` and `###` for
  sections within it.

## What the page renderer supports

Only use this Markdown. Anything else shows up as plain text:

- `#`, `##` and `###` headings
- paragraphs, separated by a blank line
- bullet lists (`-` or `*`) and numbered lists (`1.`), one level only
- tables written with pipes, with a `---` row under the header
- `---` on its own line, for a divider
- `**bold**`, which shows in the accent colour
- `` `code` ``, which shows in a muted colour

Links show their text but don't go anywhere yet, so don't rely on them. There are no images and
no nested lists.

## Adding, moving or removing a page

- To add a page, create the `.md` file and add a line to `index.md`. Indent the line under another
  entry to make it a sub-topic.
- To remove a page, delete the file and its line in `index.md`.
- File names are lowercase words joined by hyphens, like `timing-graph.md`.

## Keep it true

- Check every instruction against the current plugin before you write it. If you can't confirm how
  something behaves, ask rather than guess.
- When a change to Vista adds, removes or changes something a user can see or do, update the pages
  that describe it in the same change.
- `hotkeys.md` repeats the keys table in `README.md`. If a key changes, update both.

## An example

Too technical, and full of AI speak:

> The timing graph now seamlessly supports zoom — simply scroll to leverage the new TimingView,
> which fits the distance axis to the visible range for a more intuitive editing experience.

Clear and simple:

> To see keys that sit close together, zoom in. Hold the mouse over the graph and scroll up. The
> graph zooms in around the mouse, and the keys spread apart. Click **Fit** to see the whole track
> again.
