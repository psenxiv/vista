# Vista — the welcome screen

Date: 2026-09-23
Status: Approved

A short window that greets a tester the first time Vista loads: thanks them, says Vista is a private
beta and a work in progress, points them at the User Guide, and asks for feedback on Discord.

## Behaviour

- Opens on its own when the plugin loads, if it has never been dismissed on this install.
- Dismissed by **Ok** or its close button. Either records it as seen, and it never opens again.
- It can't be reopened: no guide page, no command.

## The window

Titled **Welcome to Vista**, centred on first appearance, fixed width, sized to its text, not
collapsible. The text:

> Thank you for helping test Vista.
>
> Vista is in private beta and still a work in progress. Expect things to change, and some things
> to break.
>
> To learn how it works, open the User Guide with the **?** at the top right of the Vista window.
>
> If you find a bug or have an idea, let me know on Discord.

Then a full-width **Ok** button.

The Discord line names no server or handle: testers already know how to reach the author.

## Saved setting

This is Vista's first saved setting. A `Configuration : IPluginConfiguration` in `Vista.Plugin`
holds `Version` (1) and `WelcomeSeen`, read with `GetPluginConfig()` at load and written with
`SavePluginConfig()` when the welcome is dismissed. 3.f will add its save folder to the same class.

## Tests

None in Core: the one decision, show when not seen, lives on a Dalamud type. It goes on the in-game
checklist.
