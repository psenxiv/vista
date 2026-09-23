# Vista — world overlay readability (Phase 4.2)

Date: 2026-09-23
Status: Approved

Track names and non-edited tracks are hard to read in bright zones. Both are drawn in
`Overlay.cs` with colours from `EditorColours.cs`; nothing else changes.

## Track name plate

Today the name is drawn with `ImGui.CalcTextSize` at the default font size and a bare
`AddText`, tinted the *anchor's* colour — gold when edited, blue when selected, grey otherwise.
Coloured text with no backing is what washes out.

- The name gets a filled, slightly rounded plate behind it: semi-opaque black, a few pixels of
  padding, drawn before the text.
- The text is white at **1.5x** the base font size, using the same explicit font-size overload
  the point-marker labels already use.
- The name no longer carries the edited / selected / other tint. That signal stays on the anchor
  ring and arrow directly beneath it, which keep their colours untouched.
- The plate is centred on the name as the name is centred today, and the whole block still sits
  `NameGap` above the highest point of the anchor ring.

## Non-edited tracks

The `Other*` colours are a flat light grey at roughly 47% alpha, which disappears against bright
sand and stone. They become **more opaque and darker**, so they read as a dark line against a
bright world:

| | from | to |
|---|---|---|
| `OtherPath`, `OtherGlyph`, `OtherUpLine`, `OtherAnchor` | `0x78A0A0A0` | `0xB0404040` |
| `OtherMarker` | `0xA0303030` | `0xC0202020` |
| `OtherMarkerRing` | `0x90B0B0B0` | `0xC0808080` |
| `OtherAnchorLink` | `0x40A0A0A0` | `0x70404040` |
| `OtherMarkerText` | `0xB0C8C8C8` | `0xE0E0E0E0` |

`OtherMarkerText` is the point number sitting on the dark `OtherMarker` disc, so it gets *lighter*
rather than darker — only its alpha follows the rest.

Non-edited tracks stay below the edited track's alpha (`0xC8`) so the two remain distinguishable;
the distinction now rests on lightness — near-white edited, dark grey other — rather than on
opacity alone.

## Known trade-off

Darkening trades a failure case rather than removing it: a dark line reads well against bright
sand and less well in a dark interior. This was chosen knowingly over drawing a contrasting
outline behind every line, which reads on both but costs a second draw pass per segment. If dark
interiors become the complaint, the outline is the follow-up.

## Not doing

- No per-track colours. Non-edited tracks stay a single grey.
- No change to the edited track's colours, the axis colours, or the scene anchor.

## Testing

`Vista.Plugin` has no automated cover and these are draw calls, so both go on the Phase 4 in-game
checklist: a track name read against bright ground and against a dark interior, and a non-edited
track read in a bright outdoor zone.
