# User Guide formatting — plan

Spec: `docs/superpowers/specs/2026-09-23-guide-formatting-design.md`.

Small enough to build inline, in order, on `main`. Source and tests commit separately.

1. **Core parser** (`src/Vista.Core/Guide/GuideMarkdown.cs`)
   - `RunStyle { Plain, Bold, Command, Key, Icon, Link }`; `Run(string Text, RunStyle Style, string? Target = null)`.
   - Backtick span: starts with `/` → one `Command` run; otherwise split on ` + ` into `Key` runs
     with `Plain(" + ")` between.
   - `{icon:Name}` → `Icon` run with `Text = Name`; an empty name stays plain text.
   - `[text](target)` → `Link` run with `Target = target`.
   - `Parse` inserts a `Divider` before a level-2 heading unless it is the first block or follows a
     divider.
2. **Core tests** (`tests/Vista.Tests/Guide/GuideMarkdownTests.cs`): keys split and commands stay
   whole, icon tags, links carry their target, automatic dividers (first block, after `---`, level
   1 and 3 untouched). Update the existing tests that used `Code` and the link test. Mutation-check.
3. **Plugin drawing** (`src/Vista.Plugin/Ui/GuideWindow.cs`): icons in the icon font, keycaps as
   bordered rounded boxes placed atomically, command runs muted, links accent and underlined with a
   hand cursor that set the shown page and scroll to the top, faint dividers, more spacing.
4. **`GUIDES.md`**: the new rules and syntax from the spec's Rewrite section.
5. **Rewrite every page** to the new rules, using icons, keycaps and links.
6. **Checklist** `scripts/checks/guide-formatting.json`: what tests can't pin (how icons, caps,
   links, dividers and spacing look and behave in game, and reading the pages).
