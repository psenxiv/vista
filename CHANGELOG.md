# Changelog

## X.Y.Z.N

- Recorded aim turns smoothly through each point instead of changing direction slightly at it.
- Look At turns the picture round gradually from point to point when the camera passes under or over its point, instead of quickly right beneath it.

## 0.11.0.1

- Reworked the points table's rows, with flatter value fields and a handle for dragging a row.
- The plugin installer now says Vista works in GPose or out in the world.

## 0.10.0.1

- If something goes wrong inside Vista, or a game update breaks part of it, Vista gives the camera back and stops until you reload it, instead of risking a crash.
- Fixed dragging a key up to the end of its hold sometimes removing the hold.
- New tracks, copies and tracks placed from a preset no longer share a name with an existing track.
- Error messages say what failed, such as deleting a scene, instead of always saying it couldn't save.
- Fixed Direction of travel facing the wrong way for a moment as a shot starts, when its path comes back to where it began within the look ahead.
- Times show to a hundredth of a second everywhere, and the Follow Target angle to a tenth of a degree, so a number reads the same in every window.
- When Vista can't do something you asked, it now tells you why.
- A point recorded with a field of view under 5° or over 120° now plays at 5° or 120°, the range the Point window allows.

## 0.9.0.1

- Hide game UI when Live stays on or off between sessions.
- Fixed a shimmer with Direction of travel while the camera holds still at a sharp turn.
- The camera can look straight up, straight down or upside down, in Edit and in your shots.
- Direction of travel turns upside down over a loop, like a rollercoaster.
- The picture no longer flips when a shot goes straight up or down, or passes over or under what it's looking at. It turns round smoothly instead.
- Fixed the turn colours from G sometimes showing the very end of a path as hot.
- Dragging rows near the top or bottom of a list scrolls it.

## 0.8.0.1

- A demo scene, Demo - Limsa, is added to your save folder once, to show what Vista can do. It's set in Limsa Lominsa. Change it or delete it as you like.
- Drag the inner edge of the Hierarchy or the Playlist to make it wider or narrower. Vista remembers the widths, and the Hierarchy starts a little wider.
- Closing the welcome screen goes straight on to choosing a save folder, then opens the Vista window.
- You can close Vista Setup without choosing a folder. It comes back the next time you open Vista.
- Long track and playlist names end in ... and scroll when you hover over them.
- Typing a number into a playlist entry's Repeats cell now works when it shows a dash or ∞, and clearing it makes the entry follow its track again.
- Fixed a small shake with Direction of travel as the camera comes to a stop at a hold or the end of a track.

## 0.7.0.1

- The camera turns, rolls and zooms smoothly through each point, without a sudden change in speed.
- Direction of travel looks ahead along the path, turning into corners before it reaches them. Set how far with Look ahead in the aim menu. Saved tracks look ahead too; set it to 0 to face straight along the path.
- Press G in Edit or View to colour the path by how fast the camera turns.

## 0.6.0.1

- Select several tracks, points or playlist entries with Ctrl + click and Shift + click, then drag them together or right-click for a menu.
- Move points to another track: drag them onto a track in the Hierarchy or below the tracks, or right-click them and choose Move to.
- Pressing Delete deletes every selected point.
- The buttons on a Hierarchy row show when you hover over it.
- A selected track or playlist entry is highlighted across its whole row.
- The User Guide's tables have more room around each cell.

## 0.5.1.1

- Fixed the camera shaking slightly while holding at the start or end of a Direction of travel track.
- Number fields can now be dragged left or right to change them. Double-click to type.
- The Camera window can move the camera along your view: choose Move (local), then drag Right, Up or Forward.
- Move (local) now moves anchors along the way they face.
- Fly speed no longer has a feather icon.

## 0.5.0.1

- A test build, the same as 0.5.0, to try out testing builds.

## 0.5.0

- Scenes save as you work, and you can keep as many as you like. Vista asks for a save folder the first time you open it.
- Save any track as a preset and add it to any scene.
- A User Guide: click the ? at the top right of the Vista window.
- Zoom and pan the timing graph.
- Clearer menus, a welcome screen for testers, a new icon, and many small fixes.

## 0.4.0

- New keys: E and Q fly up and down, Ctrl + Q and Ctrl + E roll, Space plays and pauses.
- A Camera window, with buttons to level the roll and reset the field of view.
- Choose whether the gizmo moves along the world's axes or the point's own.
- Hold Ctrl while dragging a timing key to move every later key with it.
- Track names show above their anchors, and tracks you aren't editing are darker.

## 0.3.0

- Vista starts in Off, with View beside it to see your tracks without editing them.

## 0.2.0

- First closed beta: scenes with several tracks on movable anchors, a playlist to play in Live, and Look At, Watch Target and Follow Target aims.
