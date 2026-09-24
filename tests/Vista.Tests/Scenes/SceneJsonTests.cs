using System.Numerics;
using Vista.Core.Scenes;
using Vista.Core.Tracks;
using Vista.Core.Tracks.Aiming;
using Vista.Core.Tracks.Playback;
using Vista.Core.Tracks.Timing;
using Xunit;
using static Vista.Tests.Fixtures;

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

    private static Scene FullScene()
    {
        var full = FullTrack();
        var plain = TrackEditing.Empty();
        return new Scene(
            [full, plain],
            new HashSet<Guid> { plain.Id },
            [new PlaylistEntry(Guid.NewGuid(), full.Id, 3), new PlaylistEntry(Guid.NewGuid(), plain.Id)],
            new Anchor(new Vector3(-100f, 50f, 25f), -2f),
            true,
            true
        );
    }

    // Record equality compares lists by reference, so the lists are compared by element and then swapped in.
    private static void SameTrack(Track expected, Track actual)
    {
        Assert.Equal(expected.Points, actual.Points);
        Assert.Equal(expected.Timing, actual.Timing);
        Assert.Equal(expected, actual with { Points = expected.Points, Timing = expected.Timing });
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
        Assert.Equal(scene.Playlist, read.Playlist);
        Assert.Equal(scene, read with { Tracks = scene.Tracks, Hidden = scene.Hidden, Playlist = scene.Playlist });
    }

    [Fact]
    public void ASceneFileIsIndentedWithTheFormatFirstAndEnumsByName()
    {
        var json = SceneJson.Write(FullScene());
        var nl = Environment.NewLine;

        Assert.StartsWith($"{{{nl}  \"format\": 1,{nl}", json);
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
        var json = SceneJson.Write(FullScene()).Replace("\"format\": 1", "\"format\": 2");
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

        Assert.Throws<InvalidDataException>(() => SceneJson.Read(json.Replace("\"playlistLoops\": true,", "")));
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
        var json = SceneJson.Write(new Scene([], new HashSet<Guid>(), []));

        Assert.Throws<InvalidDataException>(() => SceneJson.Read(json));
    }

    [Fact]
    public void ATrackWhosePointsAndTimingDifferInCountIsRefused()
    {
        var track = FullTrack();
        var json = SceneJson.Write(new Scene([track with { Timing = [track.Timing[0]] }], new HashSet<Guid>(), []));

        Assert.Throws<InvalidDataException>(() => SceneJson.Read(json));
    }

    [Fact]
    public void APlaylistEntryForAMissingTrackIsRefused()
    {
        var track = TrackEditing.Empty();
        var json = SceneJson.Write(
            new Scene([track], new HashSet<Guid>(), [new PlaylistEntry(Guid.NewGuid(), Guid.NewGuid())])
        );

        Assert.Throws<InvalidDataException>(() => SceneJson.Read(json));
    }

    [Fact]
    public void TwoTracksWithOneIdAreRefused()
    {
        var track = TrackEditing.Empty();
        var json = SceneJson.Write(new Scene([track, track with { Name = "Twin" }], new HashSet<Guid>(), []));

        Assert.Throws<InvalidDataException>(() => SceneJson.Read(json));
    }

    // Two points 10 yalms apart, every value in range.
    private static Track Plain() =>
        TrackEditing.Append(TrackEditing.Append(TrackEditing.Empty(), Point(0f)), Point(10f));

    private static string SceneOf(Track track, int? loops = null) =>
        SceneJson.Write(new Scene([track], new HashSet<Guid>(), [new PlaylistEntry(Guid.NewGuid(), track.Id, loops)]));

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
            // Look ahead runs 0 to 2 seconds.
            { "look ahead -0.1", t => t with { LookAhead = -0.1f } },
            { "look ahead 2.5", t => t with { LookAhead = 2.5f } },
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
        // The range ends themselves, and a pitch and FoV past the editor's own limits (89°, 120°) that a camera can still record.
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
        var old = System.Text.Json.Nodes.JsonNode.Parse(SceneOf(track))!;
        old["tracks"]![0]!.AsObject().Remove("lookAhead");
        Assert.Equal(TrackEditing.DefaultLookAhead, SceneJson.Read(old.ToJsonString()).Tracks[0].LookAhead);
    }
}
