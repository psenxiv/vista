using System.Numerics;
using System.Text.Json.Nodes;
using CsCheck;
using Vista.Core.Scenes;
using Vista.Core.Tracks;
using Vista.Core.Tracks.Aiming;
using Vista.Core.Tracks.Playback;
using Vista.Core.Tracks.Timing;
using Xunit;
using static Vista.Tests.Fixtures;
using static Vista.Tests.Scenes.SceneFixtures;

namespace Vista.Tests.Scenes;

public class SceneJsonTests
{
    // Every field away from its default; the second timing keeps a null leg speed so the null path is covered.
    private static Track FullTrack() =>
        new(
            Guid.NewGuid(),
            "Dolly in",
            [
                Point(1.5f, 2.25f, -3f, yaw: 0.5f, pitch: -0.25f, fov: 0.9f, roll: 0.1f),
                Point(-7f, 0.125f, 4f, yaw: -1f, pitch: 0.3f, fov: 1.2f, roll: -0.2f),
            ],
            [
                new PointTiming(3.5f, 2f, TangentMode.Linear, TangentMode.Flat, 0.4f, -0.6f, true),
                new PointTiming(null, 0.75f, TangentMode.Manual, TangentMode.Auto, -1.5f, 2.5f, false),
            ],
            7.5f,
            AimMode.FollowTarget,
            PlaybackDirection.PingPong,
            true,
            new Anchor(new Vector3(10f, 20f, 30f), 1.25f),
            true,
            new Vector3(4f, 5f, 6f),
            true,
            "Wol",
            "Ultros",
            1.8f,
            0.6f,
            false,
            false
        );

    // Two playlists, the second selected and looping; slot 0 on the full track and following its name, slot 4 on Intro under its own name, every toggle on, slot 4 on Program at 2.5 s, slot 0 Next and resuming at 1.25 s.
    private static Scene FullScene()
    {
        var full = FullTrack();
        var plain = TrackEditing.Empty();
        var intro = new Playlist(
            Guid.NewGuid(),
            "Intro",
            [new PlaylistEntry(Guid.NewGuid(), full.Id, 3), new PlaylistEntry(Guid.NewGuid(), plain.Id)]
        );
        var main = new Playlist(Guid.NewGuid(), "Main", [new PlaylistEntry(Guid.NewGuid(), plain.Id, 2)], true);
        var slots = new Slot?[SwitchboardEditing.SlotCount];
        slots[0] = new Slot(null, full.Id, null);
        slots[4] = new Slot("Opening", null, intro.Id);
        return new Scene(
            [full, plain],
            new HashSet<Guid> { plain.Id },
            [intro, main],
            main.Id,
            new Switchboard(slots, true, true, true, new OnAir(4, 0, 2.5, Resume((0, 1.25)))),
            new Anchor(new Vector3(-100f, 50f, 25f), -2f),
            true
        );
    }

    // The switchboard a file without one reads with: ten empty slots, every toggle off and nothing on air.
    private static readonly Switchboard NoSwitchboard = new(
        new Slot?[10],
        false,
        false,
        false,
        new OnAir(null, null, 0.0, new double?[10])
    );

    // Record equality compares lists by reference, so the lists are compared by element and then swapped in.
    private static void SameTrack(Track expected, Track actual)
    {
        Assert.Equal(expected.Points, actual.Points);
        Assert.Equal(expected.Timing, actual.Timing);
        Assert.Equal(expected, actual with { Points = expected.Points, Timing = expected.Timing });
    }

    // As SameTrack, for each playlist's entries.
    private static void SamePlaylists(IReadOnlyList<Playlist> expected, IReadOnlyList<Playlist> actual)
    {
        Assert.Equal(expected.Count, actual.Count);
        for (var i = 0; i < expected.Count; i++)
        {
            Assert.Equal(expected[i].Entries, actual[i].Entries);
            Assert.Equal(expected[i], actual[i] with { Entries = expected[i].Entries });
        }
    }

    // A format 2 scene file, edited as JSON.
    private static string Edited(Scene scene, Action<JsonNode> edit)
    {
        var node = JsonNode.Parse(SceneJson.Write(scene))!;
        edit(node);
        return node.ToJsonString();
    }

    [Fact]
    public void ASceneRoundTripsEveryField()
    {
        var scene = FullScene();
        var read = SceneJson.Read(SceneJson.Write(scene));

        Assert.Equal(scene.Tracks.Count, read.Tracks.Count);
        for (var i = 0; i < scene.Tracks.Count; i++)
            SameTrack(scene.Tracks[i], read.Tracks[i]);
        Assert.True(scene.Hidden.SetEquals(read.Hidden));
        SamePlaylists(scene.Playlists, read.Playlists);
        SameBoard(scene.Switchboard, read.Switchboard);
        Assert.Equal(
            scene,
            read with
            {
                Tracks = scene.Tracks,
                Hidden = scene.Hidden,
                Playlists = scene.Playlists,
                Switchboard = scene.Switchboard,
            }
        );
    }

    [Fact]
    public void AFormatTwoFileWithoutASwitchboardReadsWithAnEmptyOne()
    {
        var json = Edited(FullScene(), n => n.AsObject().Remove("switchboard"));

        SameBoard(NoSwitchboard, SceneJson.Read(json).Switchboard);
    }

    [Theory]
    [InlineData(0, "trackId")]
    [InlineData(4, "playlistId")]
    public void ASlotNamingAMissingTrackOrPlaylistIsRefused(int slot, string id)
    {
        var json = Edited(FullScene(), n => n["switchboard"]!["slots"]![slot]![id] = Guid.NewGuid().ToString());

        Assert.Throws<InvalidDataException>(() => SceneJson.Read(json));
    }

    [Fact]
    public void ASlotsOwnNameReadsTrimmed()
    {
        var json = Edited(FullScene(), n => n["switchboard"]!["slots"]![4]!["name"] = "  Opening night  ");

        Assert.Equal("Opening night", SceneJson.Read(json).Switchboard.Slots[4]!.Name);
    }

    [Fact]
    public void AFollowingSlotIsWrittenWithANullName()
    {
        var slots = JsonNode.Parse(SceneJson.Write(FullScene()))!["switchboard"]!["slots"]!;

        // Slot 0 follows its track; slot 4 has its own name.
        Assert.True(slots[0]!.AsObject().ContainsKey("name"));
        Assert.Null(slots[0]!["name"]);
        Assert.Equal("Opening", slots[4]!["name"]!.GetValue<string>());
    }

    [Fact]
    public void ASlotWithNoNameInTheFileReadsAsFollowing()
    {
        var json = Edited(FullScene(), n => n["switchboard"]!["slots"]![4]!.AsObject().Remove("name"));

        Assert.Null(SceneJson.Read(json).Switchboard.Slots[4]!.Name);
    }

    // FullScene's slot 0 holds the track "Dolly in" and its slot 4 the playlist "Intro".
    [Theory]
    [InlineData(0, "Dolly in")]
    [InlineData(0, "  Dolly in  ")]
    [InlineData(4, "Intro")]
    public void ASlotNamedAsItsTrackOrPlaylistReadsAsFollowing(int slot, string name)
    {
        var json = Edited(FullScene(), n => n["switchboard"]!["slots"]![slot]!["name"] = name);

        Assert.Null(SceneJson.Read(json).Switchboard.Slots[slot]!.Name);
    }

    // FullScene has slot 4 on Program at 2.5 s and slot 0 Next; slot 3 is empty.
    [Theory]
    [InlineData(-1)]
    [InlineData(10)]
    [InlineData(3)]
    public void AProgramOffTheSlotsOrOnAnEmptyOneReadsAsNoProgram(int program)
    {
        var json = Edited(FullScene(), n => n["switchboard"]!["live"]!["program"] = program);

        // With nothing on Program its time reads as 0; Next and the resume stay.
        SameAir(new OnAir(null, 0, 0.0, Resume((0, 1.25))), SceneJson.Read(json).Switchboard.Live);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(10)]
    [InlineData(3)]
    public void ANextOffTheSlotsOrOnAnEmptyOneReadsAsNoNext(int next)
    {
        var json = Edited(FullScene(), n => n["switchboard"]!["live"]!["next"] = next);

        SameAir(new OnAir(4, null, 2.5, Resume((0, 1.25))), SceneJson.Read(json).Switchboard.Live);
    }

    [Fact]
    public void AResumeOnAnEmptySlotReadsAsNone()
    {
        var json = Edited(FullScene(), n => n["switchboard"]!["live"]!["resume"]![3] = 7.5);

        SameAir(new OnAir(4, 0, 2.5, Resume((0, 1.25))), SceneJson.Read(json).Switchboard.Live);
    }

    [Fact]
    public void AResumeOfZeroOnAFilledSlotReadsAsZero()
    {
        // Cutting away from a shot at its very start records 0 s, which is a valid resume point and not none.
        var json = Edited(FullScene(), n => n["switchboard"]!["live"]!["resume"]![0] = 0.0);

        SameAir(new OnAir(4, 0, 2.5, Resume((0, 0.0))), SceneJson.Read(json).Switchboard.Live);
    }

    public static TheoryData<string, Action<JsonNode>> MalformedSwitchboards =>
        new()
        {
            { "nine slots", n => n["switchboard"]!["slots"]!.AsArray().RemoveAt(9) },
            { "eleven slots", n => n["switchboard"]!["slots"]!.AsArray().Add(null) },
            {
                "both ids",
                n => n["switchboard"]!["slots"]![0]!["playlistId"] = n["playlists"]![0]!["id"]!.GetValue<string>()
            },
            { "neither id", n => n["switchboard"]!["slots"]![0]!["trackId"] = null },
            { "a blank name", n => n["switchboard"]!["slots"]![0]!["name"] = "   " },
            { "a name too long", n => n["switchboard"]!["slots"]![0]!["name"] = new string('a', 101) },
            { "a negative program time", n => n["switchboard"]!["live"]!["programTime"] = -0.5 },
            { "nine resumes", n => n["switchboard"]!["live"]!["resume"]!.AsArray().RemoveAt(9) },
            { "eleven resumes", n => n["switchboard"]!["live"]!["resume"]!.AsArray().Add(null) },
            { "a negative resume", n => n["switchboard"]!["live"]!["resume"]![0] = -1.0 },
        };

    [Theory]
    [MemberData(nameof(MalformedSwitchboards))]
    public void AMalformedSwitchboardMakesTheSceneUnreadable(string _, Action<JsonNode> spoil) =>
        Assert.Throws<InvalidDataException>(() => SceneJson.Read(Edited(FullScene(), spoil)));

    [Fact]
    public void AFormatOneSceneReadsAsOneSelectedLoopingPlaylist()
    {
        var read = SceneJson.Read(FormatOneSceneJson());

        // The fixture's playlist, in file order: five entries, the second repeating twice, and playlistLoops true.
        SameBoard(NoSwitchboard, read.Switchboard);
        var playlist = Assert.Single(read.Playlists);
        Assert.Equal("Playlist 1", playlist.Name);
        Assert.True(playlist.Loops);
        Assert.Equal(playlist.Id, read.SelectedPlaylistId);
        Assert.Equal(
            new PlaylistEntry[]
            {
                new(
                    Guid.Parse("3b7fa88d-8888-4124-b207-29893e56e637"),
                    Guid.Parse("f8688d0d-14c7-47af-8df5-edb2bb6d0553")
                ),
                new(
                    Guid.Parse("207bbd93-54f1-4772-84bf-970f541ef70c"),
                    Guid.Parse("643e8e6b-e089-4664-bed2-04826979c289"),
                    2
                ),
                new(
                    Guid.Parse("af961abd-b459-43b8-b68b-46d05926a199"),
                    Guid.Parse("e30aece0-efd0-4c9b-8f01-65017a4b66c0")
                ),
                new(
                    Guid.Parse("e0a09bda-658a-4a57-ad61-4ee0982f5913"),
                    Guid.Parse("c4fd65bd-4eca-49d5-aa11-f67d1cbb7594")
                ),
                new(
                    Guid.Parse("7855e698-f816-43ca-8265-14f7e8194582"),
                    Guid.Parse("d776513b-f0dd-4367-b1ac-ae58cc1dc132")
                ),
            },
            playlist.Entries
        );
    }

    [Fact]
    public void FormatOfReadsTheFormatNumber()
    {
        Assert.Equal(1, SceneJson.FormatOf(FormatOneSceneJson()));
        Assert.Equal(2, SceneJson.FormatOf(SceneJson.Write(FullScene())));
        Assert.Throws<InvalidDataException>(() => SceneJson.FormatOf("{ \"tracks\": [] }"));
        Assert.Throws<InvalidDataException>(() => SceneJson.FormatOf("not json"));
    }

    [Fact]
    public void ASelectedIdNamingNoPlaylistSelectsTheFirst()
    {
        var scene = FullScene();
        var json = Edited(scene, n => n["selectedPlaylist"] = Guid.NewGuid().ToString());

        Assert.Equal(scene.Playlists[0].Id, SceneJson.Read(json).SelectedPlaylistId);
    }

    [Fact]
    public void TwoPlaylistsWithOneIdAreRefused()
    {
        var scene = FullScene();
        var json = Edited(scene, n => n["playlists"]![1]!["id"] = scene.Playlists[0].Id.ToString());

        Assert.Throws<InvalidDataException>(() => SceneJson.Read(json));
    }

    [Fact]
    public void TwoPlaylistsWithOneNameLoadAsTheyAre()
    {
        var json = Edited(FullScene(), n => n["playlists"]![1]!["name"] = "intro");

        Assert.Equal(new[] { "Intro", "intro" }, SceneJson.Read(json).Playlists.Select(p => p.Name));
    }

    [Fact]
    public void ASceneWithNoPlaylistsIsRefused()
    {
        var json = Edited(FullScene(), n => n["playlists"] = new JsonArray());

        Assert.Throws<InvalidDataException>(() => SceneJson.Read(json));
    }

    [Fact]
    public void AnEntryInAnyPlaylistForAMissingTrackIsRefused()
    {
        var json = Edited(FullScene(), n => n["playlists"]![1]!["entries"]![0]!["trackId"] = Guid.NewGuid().ToString());

        Assert.Throws<InvalidDataException>(() => SceneJson.Read(json));
    }

    [Fact]
    public void ANewerSceneFormatAsksForANewerVista()
    {
        var json = SceneJson.Write(FullScene()).Replace("\"format\": 2", "\"format\": 3");

        var refused = Assert.Throws<NewerFormatException>(() => SceneJson.Read(json));
        Assert.Equal("This scene needs a newer version of Vista. Update Vista to open it.", refused.Message);
    }

    [Fact]
    public void APresetFileInFormatOneStillReadsAndIsWrittenAsFormatOne()
    {
        // The fixture: yaw 0.5, and a track at 5 yalms per second through points at x = 0 and x = 10.
        var preset = SceneJson.ReadPreset(FormatOnePresetJson());

        Assert.Equal(0.5f, preset.Yaw);
        Assert.Equal(5f, preset.Track.Speed);
        Assert.Equal(new[] { 0f, 10f }, preset.Track.Points.Select(p => p.Position.X));
        Assert.Equal(1, SceneJson.FormatOf(SceneJson.WritePreset(preset)));
    }

    [Fact]
    public void ASceneFileIsIndentedWithTheFormatFirstAndEnumsByName()
    {
        var json = SceneJson.Write(FullScene());
        var nl = Environment.NewLine;

        Assert.StartsWith($"{{{nl}  \"format\": 2,{nl}", json);
        Assert.Contains("\"aim\": \"FollowTarget\"", json);
        Assert.Contains("\"direction\": \"PingPong\"", json);
    }

    [Fact]
    public void APresetRoundTripsWithoutItsIdNameOrAnchor()
    {
        var track = FullTrack();
        var json = SceneJson.WritePreset(new Preset(track, 2.5f));
        var read = SceneJson.ReadPreset(json);

        Assert.DoesNotContain("Dolly in", json);
        Assert.DoesNotContain(track.Id.ToString(), json);
        Assert.DoesNotContain("\"anchor\"", json);
        Assert.DoesNotContain("\"anchorPlaced\"", json);
        Assert.Equal(2.5f, read.Yaw);
        Assert.NotEqual(track.Id, read.Track.Id);
        SameTrack(track with { Id = read.Track.Id, Name = "", Anchor = default, AnchorPlaced = false }, read.Track);
    }

    [Fact]
    public void EachPresetReadGetsANewId()
    {
        var json = SceneJson.WritePreset(new Preset(FullTrack(), 0f));

        Assert.NotEqual(SceneJson.ReadPreset(json).Track.Id, SceneJson.ReadPreset(json).Track.Id);
    }

    [Fact]
    public void AnotherFormatIsRefused()
    {
        var json = SceneJson.Write(FullScene()).Replace("\"format\": 2", "\"format\": 0");
        var preset = SceneJson.WritePreset(new Preset(FullTrack(), 0f)).Replace("\"format\": 1", "\"format\": 2");

        Assert.Throws<InvalidDataException>(() => SceneJson.Read(json));
        Assert.Throws<InvalidDataException>(() => SceneJson.ReadPreset(preset));
        Assert.Throws<InvalidDataException>(() => SceneJson.Read("{ \"tracks\": [] }"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("[1, 2]")]
    [InlineData("{ \"format\": 1, \"tracks\": [ }")]
    public void GarbageIsRefused(string json)
    {
        Assert.Throws<InvalidDataException>(() => SceneJson.Read(json));
        Assert.Throws<InvalidDataException>(() => SceneJson.ReadPreset(json));
    }

    [Fact]
    public void MissingOrNullFieldsAreRefused()
    {
        var json = SceneJson.Write(FullScene());

        Assert.Throws<InvalidDataException>(() => SceneJson.Read(json.Replace("\"loops\": true,", "")));
        Assert.Throws<InvalidDataException>(() =>
            SceneJson.Read(json.Replace("\"name\": \"Dolly in\"", "\"name\": null"))
        );
        Assert.Throws<InvalidDataException>(() =>
            SceneJson.Read(json.Replace("\"lookAt\": {", "\"lookAt\": null, \"x\": {"))
        );
    }

    [Fact]
    public void ASceneWithNoTracksIsRefused()
    {
        var json = SceneJson.Write(OnePlaylist([]));

        Assert.Throws<InvalidDataException>(() => SceneJson.Read(json));
    }

    [Fact]
    public void ATrackWhosePointsAndTimingDifferInCountIsRefused()
    {
        var track = FullTrack();
        var json = SceneJson.Write(OnePlaylist([track with { Timing = [track.Timing[0]] }]));

        Assert.Throws<InvalidDataException>(() => SceneJson.Read(json));
    }

    [Fact]
    public void APlaylistEntryForAMissingTrackIsRefused()
    {
        var track = TrackEditing.Empty();
        var json = SceneJson.Write(OnePlaylist([track], [new PlaylistEntry(Guid.NewGuid(), Guid.NewGuid())]));

        Assert.Throws<InvalidDataException>(() => SceneJson.Read(json));
    }

    [Fact]
    public void TwoTracksWithOneIdAreRefused()
    {
        var track = TrackEditing.Empty();
        var json = SceneJson.Write(OnePlaylist([track, track with { Name = "Twin" }]));

        Assert.Throws<InvalidDataException>(() => SceneJson.Read(json));
    }

    // Two points 10 yalms apart, every value in range.
    private static Track Plain() => WithTwoPoints(TrackEditing.Empty());

    private static string SceneOf(Track track, int? loops = null) =>
        SceneJson.Write(OnePlaylist([track], [new PlaylistEntry(Guid.NewGuid(), track.Id, loops)]));

    private static Track WithPoint(Track t, ControlPoint p) => t with { Points = [p, t.Points[1]] };

    private static Track WithTiming(Track t, PointTiming k) => t with { Timing = [t.Timing[0], k] };

    public static TheoryData<string, Func<Track, Track>> OutOfRange =>
        new()
        {
            // TrackEditing's speed range is 0.01 to 100 yalms per second, for the track and a pinned leg.
            { "track speed 0", t => t with { Speed = 0f } },
            { "track speed 101", t => t with { Speed = 101f } },
            { "leg speed 0", t => WithTiming(t, new PointTiming(LegSpeed: 0f)) },
            // A hold runs 0 to 600 seconds.
            { "hold -1", t => WithTiming(t, new PointTiming(Hold: -1f)) },
            { "hold 601", t => WithTiming(t, new PointTiming(Hold: 601f)) },
            // Aim height runs 0 to 3 yalms, smoothing 0 to 1.
            { "aim height 3.5", t => t with { AimHeight = 3.5f } },
            { "smoothing 1.5", t => t with { Smoothing = 1.5f } },
            // Look ahead runs 0 to 10 yalms.
            { "look ahead -0.1", t => t with { LookAhead = -0.1f } },
            { "look ahead 10.5", t => t with { LookAhead = 10.5f } },
            // A camera can't look past straight up (π/2 ≈ 1.5708) or see with no field of view, or all round (π).
            { "pitch 1.6", t => WithPoint(t, Point(0f, pitch: 1.6f)) },
            { "fov 0", t => WithPoint(t, Point(0f, fov: 0f)) },
            { "fov π", t => WithPoint(t, Point(0f, fov: MathF.PI)) },
        };

    [Theory]
    [MemberData(nameof(OutOfRange))]
    public void AValueOutOfRangeIsRefused(string _, Func<Track, Track> spoil)
    {
        Assert.Throws<InvalidDataException>(() => SceneJson.Read(SceneOf(spoil(Plain()))));
        Assert.Throws<InvalidDataException>(() =>
            SceneJson.ReadPreset(SceneJson.WritePreset(new Preset(spoil(Plain()), 0f)))
        );
    }

    [Fact]
    public void ValuesAtTheEdgesOfTheirRangesLoad()
    {
        // The range ends themselves, and a FoV past the editor's own limit (120°) that a camera can still record.
        var track = WithTiming(
            Plain() with
            {
                Speed = 100f,
                AimHeight = 3f,
                Smoothing = 1f,
            },
            new PointTiming(LegSpeed: 0.01f, Hold: 600f)
        );
        track = WithPoint(track, Point(0f, pitch: 1.56f, fov: 2.5f));

        Assert.Single(SceneJson.Read(SceneOf(track, 99)).Tracks);
        Assert.Single(SceneJson.Read(SceneOf(track, 1)).Tracks);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(100)]
    public void RepeatsOutOfRangeAreRefused(int loops)
    {
        // An entry repeats 1 to PlaylistEditing.MaxLoops (99) times, or follows its track with none.
        Assert.Throws<InvalidDataException>(() => SceneJson.Read(SceneOf(Plain(), loops)));
    }

    [Fact]
    public void LookAheadRoundTripsAndDefaultsWhenMissing()
    {
        var track = TrackEditing.SetLookAhead(Plain(), 1.25f);
        Assert.Equal(1.25f, SceneJson.Read(SceneOf(track)).Tracks[0].LookAhead, 1e-6f);

        // A file saved before look-ahead existed has no lookAhead line.
        var old = JsonNode.Parse(SceneOf(track))!;
        old["tracks"]![0]!.AsObject().Remove("lookAhead");
        Assert.Equal(TrackEditing.DefaultLookAhead, SceneJson.Read(old.ToJsonString()).Tracks[0].LookAhead);
    }

    // 1e39 is past a float's largest value, about 3.4e38, so it reads as infinity: JSON has no literal for one.
    private static string Infinite(string json, Func<JsonNode, JsonNode> parent, string key)
    {
        var node = JsonNode.Parse(json)!;
        parent(node)[key] = JsonNode.Parse("1e39");
        return node.ToJsonString();
    }

    [Fact]
    public void ASceneAnchorThatIsntFiniteIsRefused()
    {
        var json = SceneOf(Plain());

        Assert.Throws<InvalidDataException>(() => SceneJson.Read(Infinite(json, n => n["anchor"]!["position"]!, "x")));
        Assert.Throws<InvalidDataException>(() => SceneJson.Read(Infinite(json, n => n["anchor"]!, "yaw")));
    }

    [Fact]
    public void APresetYawThatIsntFiniteIsRefused() =>
        Assert.Throws<InvalidDataException>(() =>
            SceneJson.ReadPreset(Infinite(SceneJson.WritePreset(new Preset(Plain(), 0f)), n => n, "yaw"))
        );

    [Fact]
    public void APointAtAFinitePlaceWhoseYawIsntFiniteIsRefused() =>
        Assert.Throws<InvalidDataException>(() =>
            SceneJson.Read(Infinite(SceneOf(Plain()), n => n["tracks"]![0]!["points"]![0]!, "yaw"))
        );

    private static readonly Gen<string> AnyName = Gen.String[Gen.Char[' ', '\uD7FF'], 1, 20]
        .Where(s => !string.IsNullOrWhiteSpace(s));

    /// <summary>A path track with every other setting random too, as a scene file holds it; id, name and anchor are set directly, as <c>SceneJson.Read</c> sets them, not by an edit sequence, and a Follow Target track keeps only its first point, as the editor requires.</summary>
    private static readonly Gen<Track> AnySavedTrack = Gen.Select(
        AnyPathTrack,
        Gen.Select(Gen.Guid, AnyName, Gen.Enum<AimMode>(), Gen.Enum<PlaybackDirection>(), Gen.Bool),
        Gen.Select(AnyPosition, Gen.Float[-MathF.PI, MathF.PI], Gen.Bool, AnyPosition, Gen.Bool),
        Gen.Select(AnyName.Null(), AnyName.Null(), AnyTargetSettings),
        Gen.Select(Gen.Bool, Gen.Bool),
        (track, identity, places, target, follow) =>
        {
            var (id, name, aim, direction, loop) = identity;
            var (anchor, yaw, anchorPlaced, lookAt, lookAtPlaced) = places;
            if (aim == AimMode.FollowTarget)
                track = TrackEditing.Delete(track, Enumerable.Range(1, track.Points.Count - 1).ToArray());
            track = TrackEditing.SetAim(track, aim, track.Points[0]);
            if (lookAtPlaced)
                track = TrackEditing.SetLookAt(track, lookAt);
            track = TrackEditing.SetDirection(track, direction);
            track = TrackEditing.SetLoop(track, loop);
            track = WithTarget(track, target.Item1, target.Item2, target.Item3);
            track = TrackEditing.SetFollowTurns(track, follow.Item1);
            track = TrackEditing.SetFollowLooks(track, follow.Item2);
            return track with { Id = id, Name = name, Anchor = new Anchor(anchor, yaw), AnchorPlaced = anchorPlaced };
        }
    );

    /// <summary>One to four tracks, some hidden, one to three named playlists of them with random repeats and loop flags, one selected, a random anchor, and a switchboard whose slots are empty or on any of them, following or under trimmed names of their own, with any toggles, and Program, Next and resume times only on filled slots; built directly as <c>SceneJson.Read</c> builds a scene, not by an edit sequence.</summary>
    private static readonly Gen<Scene> AnyScene = Gen.Select(
        AnySavedTrack.Array[1, 4],
        Gen.Select(
            Gen.Guid,
            AnyName,
            Gen.Bool,
            Gen.Select(Gen.Int[0, 3], Gen.Int[0, PlaylistEditing.MaxLoops], Gen.Guid).Array[0, 6]
        ).Array[1, 3],
        Gen.Select(Gen.Bool.Array[4], Gen.Int[0, 2]),
        Gen.Select(AnyPosition, Gen.Float[-MathF.PI, MathF.PI], Gen.Bool),
        Gen.Select(
            Gen.Select(
                Gen.Int[0, 2],
                Gen.Int[0, 3],
                AnyName.Select(n => n.Trim()).Null(),
                Gen.Bool,
                Gen.Double[0.0, 600.0]
            ).Array[SwitchboardEditing.SlotCount],
            Gen.Bool.Array[3],
            Gen.Select(
                Gen.Int[-1, SwitchboardEditing.SlotCount - 1],
                Gen.Int[-1, SwitchboardEditing.SlotCount - 1],
                Gen.Double[0.0, 600.0]
            )
        ),
        (tracks, playlists, picks, scene, board) =>
        {
            var (hidden, selected) = picks;
            var built = playlists
                .Select(p => new Playlist(
                    p.Item1,
                    p.Item2,
                    p.Item4.Select(e => new PlaylistEntry(
                            e.Item3,
                            tracks[e.Item1 % tracks.Length].Id,
                            e.Item2 == 0 ? null : e.Item2
                        ))
                        .ToList(),
                    p.Item3
                ))
                .ToList();
            var (slotPicks, toggles, air) = board;
            // A name the same as its track or playlist's isn't the slot's own.
            static string? Own(string? name, string target) => name == target ? null : name;
            var slots = slotPicks
                .Select(s =>
                {
                    var track = tracks[s.Item2 % tracks.Length];
                    var playlist = built[s.Item2 % built.Count];
                    return s.Item1 switch
                    {
                        1 => new Slot(Own(s.Item3, track.Name), track.Id, null),
                        2 => new Slot(Own(s.Item3, playlist.Name), null, playlist.Id),
                        _ => null,
                    };
                })
                .ToArray();
            int? OnSlot(int i) => i >= 0 && slots[i] is not null ? i : null;
            var program = OnSlot(air.Item1);
            var live = new OnAir(
                program,
                OnSlot(air.Item2),
                program is null ? 0.0 : air.Item3,
                slotPicks.Select((s, i) => slots[i] is not null && s.Item4 ? s.Item5 : (double?)null).ToArray()
            );
            return new Scene(
                tracks,
                tracks.Where((_, i) => hidden[i]).Select(t => t.Id).ToHashSet(),
                built,
                built[selected % built.Count].Id,
                new Switchboard(slots, toggles[0], toggles[1], toggles[2], live),
                new Anchor(scene.Item1, scene.Item2),
                scene.Item3
            );
        }
    );

    [Fact]
    [Trait("Category", "Property")]
    public void AnyValidSceneSurvivesSavingAndLoading() =>
        AnyScene.Sample(
            scene =>
            {
                var read = SceneJson.Read(SceneJson.Write(scene));

                Assert.Equal(scene.Tracks.Count, read.Tracks.Count);
                for (var i = 0; i < scene.Tracks.Count; i++)
                {
                    var (want, got) = (scene.Tracks[i], read.Tracks[i]);
                    Assert.Equal(want.Points, got.Points);
                    Assert.Equal(want.Timing, got.Timing);
                    // Records compare lists by reference, so compare the rest with the lists swapped in.
                    Assert.Equal(want, got with { Points = want.Points, Timing = want.Timing });
                }

                Assert.True(scene.Hidden.SetEquals(read.Hidden));
                SamePlaylists(scene.Playlists, read.Playlists);
                SameBoard(scene.Switchboard, read.Switchboard);
                Assert.Equal(
                    scene,
                    read with
                    {
                        Tracks = scene.Tracks,
                        Hidden = scene.Hidden,
                        Playlists = scene.Playlists,
                        Switchboard = scene.Switchboard,
                    }
                );
            },
            iter: 3000,
            print: Kept<Scene>(SceneJson.Write)
        );
}
