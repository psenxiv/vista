# Tracks and points

A point is one camera position you want in your shot. It remembers where the camera was, which way it faced, its roll and its field of view. A track is a line of points. When you play a track, the camera flies from the first point to the last, passing through each one in order.

A scene can hold several tracks. You edit one track at a time, and the points table in the **Vista** window shows that track's points. You change tracks in Edit mode.

## The Hierarchy

The Hierarchy is the column on the left of the **Vista** window. It lists every track in the scene. Click **Hide hierarchy** on the top row to close it, and **Show hierarchy** to bring it back.

- Click **Add track** to make a new, empty track. Vista names it "Track" and a number, and you start editing it.
- Click a track's name to edit it. The camera stays where it is.
- Double-click a track's name to edit it and fly the camera to its first point.
- Drag a track's name up or down to change the order of the list.
- Click the eye beside a track to hide it, and click it again to show it. You can't hide the track you are editing. If you click a hidden track's name, it shows again.

The track you are editing is highlighted in the list. The anchor buttons beside each track are covered in Scenes and anchors.

Right-click a track's name for more:

- **Rename** turns the name into a text box. Type the new name and press `Enter`, or press `Escape` to keep the old one. A track can't have an empty name.
- **Duplicate** makes a copy of the track, puts it below the original, and starts editing the copy. The copy has "copy" at the end of its name.
- **Add to playlist** adds the track to the end of the playlist. The Playlist page explains the rest.
- **Delete** removes the track, and takes it out of the playlist too. A scene always keeps at least one track, so this is greyed out when there is only one.

## Other tracks in the game view

In Edit and View modes, every track that isn't hidden is drawn in the game world. Each point shows as a small wireframe camera with its number, and a line joins the points in order.

The track you are editing is drawn in bright colours. The other tracks are drawn darker, in grey. In Edit mode, click one of their points to start editing that track, with that point selected.

## Clear track

The trash button at the right end of the track row is **Clear track**. It removes every point from the track you are editing, and puts its settings back to how a new track starts. The track keeps its name. If you clear a track by mistake, press `Ctrl + Z` to undo it.
