using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Vista.Core.Tracks;

namespace Vista.Core.Scenes;

/// <summary>Reads and writes scene and preset files.</summary>
public static class SceneJson
{
    /// <summary>The file format this version writes and the only one it reads.</summary>
    public const int Format = 1;

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        RespectNullableAnnotations = true,
        RespectRequiredConstructorParameters = true,
        Converters = { new JsonStringEnumConverter(allowIntegerValues: false) },
    };

    /// <summary>The scene as indented JSON.</summary>
    public static string Write(Scene scene) => JsonSerializer.Serialize(
        new SceneFile(
            Format,
            FromAnchor(scene.Anchor),
            scene.AnchorPlaced,
            scene.PlaylistLoops,
            scene.Hidden.Order().ToList(),
            scene.Playlist.Select(e => new EntryDto(e.Id, e.TrackId, e.Loops, e.Transition)).ToList(),
            scene.Tracks.Select(t => FromTrack(t, identity: true)).ToList()),
        Options);

    /// <summary>The scene in <paramref name="json"/>; throws InvalidDataException when it is malformed or another format.</summary>
    public static Scene Read(string json)
    {
        var file = Parse<SceneFile>(json);
        var tracks = Each(file.Tracks, t => ToTrack(t, identity: true));
        if (tracks.Count == 0) throw new InvalidDataException("A scene needs a track.");
        var ids = tracks.Select(t => t.Id).ToHashSet();
        if (ids.Count != tracks.Count) throw new InvalidDataException("Two tracks share an id.");
        if (file.Playlist.Any(e => e is not null && !ids.Contains(e.TrackId))) throw new InvalidDataException("A playlist entry names a missing track.");
        return new Scene(
            tracks,
            file.Hidden.ToHashSet(),
            Each(file.Playlist, e => new PlaylistEntry(e.Id, e.TrackId, e.Loops, e.Transition)),
            ToAnchor(file.Anchor),
            file.AnchorPlaced,
            file.PlaylistLoops);
    }

    /// <summary>The preset as indented JSON, without the track's id, name or anchor.</summary>
    public static string WritePreset(Preset preset) =>
        JsonSerializer.Serialize(new PresetFile(Format, preset.Yaw, FromTrack(preset.Track, identity: false)), Options);

    /// <summary>The preset in <paramref name="json"/>, its track with a new id, an empty name and no anchor; throws InvalidDataException when it is malformed or another format.</summary>
    public static Preset ReadPreset(string json)
    {
        var file = Parse<PresetFile>(json);
        return new Preset(ToTrack(file.Track, identity: false), file.Yaw);
    }

    private static T Parse<T>(string json)
        where T : class
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("format", out var format)
                || format.ValueKind != JsonValueKind.Number
                || !format.TryGetInt32(out var version)
                || version != Format)
            {
                throw new InvalidDataException($"Not a format {Format} file.");
            }

            return root.Deserialize<T>(Options) ?? throw new InvalidDataException("The file is empty.");
        }
        catch (JsonException e)
        {
            throw new InvalidDataException(e.Message, e);
        }
    }

    private static List<T> Each<TDto, T>(IReadOnlyList<TDto?> items, Func<TDto, T> map)
        where TDto : class
        => items.Select(i => i is null ? throw new InvalidDataException("A list holds a null.") : map(i)).ToList();

    private static TrackDto FromTrack(Track t, bool identity) => new(
        t.Speed,
        t.Aim,
        t.Direction,
        t.Loop,
        FromVector(t.LookAt),
        t.LookAtPlaced,
        t.TargetName,
        t.TargetWorld,
        t.AimHeight,
        t.Smoothing,
        t.FollowTurns,
        t.FollowLooks,
        t.Points.Select(p => new PointDto(FromVector(p.Position), p.Yaw, p.Pitch, p.Fov, p.Roll)).ToList(),
        t.Timing.Select(k => new TimingDto(k.LegSpeed, k.Hold, k.InMode, k.OutMode, k.InTangent, k.OutTangent, k.Broken)).ToList(),
        identity ? t.Id : null,
        identity ? t.Name : null,
        identity ? FromAnchor(t.Anchor) : null,
        identity ? t.AnchorPlaced : null);

    private static Track ToTrack(TrackDto t, bool identity)
    {
        var points = Each(t.Points, p => new ControlPoint(ToVector(p.Position), p.Yaw, p.Pitch, p.Fov, p.Roll));
        var timing = Each(t.Timing, k => new PointTiming(k.LegSpeed, k.Hold, k.InMode, k.OutMode, k.InTangent, k.OutTangent, k.Broken));
        if (points.Count != timing.Count) throw new InvalidDataException("A track's points and timing differ in count.");

        var track = new Track(
            Guid.NewGuid(), string.Empty, points, timing, t.Speed, t.Aim, t.Direction, t.Loop,
            LookAt: ToVector(t.LookAt), LookAtPlaced: t.LookAtPlaced, TargetName: t.TargetName, TargetWorld: t.TargetWorld,
            AimHeight: t.AimHeight, Smoothing: t.Smoothing, FollowTurns: t.FollowTurns, FollowLooks: t.FollowLooks);
        if (!identity) return track;

        if (t.Id is not { } id || t.Name is not { } name || t.Anchor is not { } anchor || t.AnchorPlaced is not { } placed)
            throw new InvalidDataException("A scene track needs an id, name and anchor.");
        return track with { Id = id, Name = name, Anchor = ToAnchor(anchor), AnchorPlaced = placed };
    }

    private static VectorDto FromVector(Vector3 v) => new(v.X, v.Y, v.Z);

    private static Vector3 ToVector(VectorDto v) => new(v.X, v.Y, v.Z);

    private static AnchorDto FromAnchor(Anchor a) => new(FromVector(a.Position), a.Yaw);

    private static Anchor ToAnchor(AnchorDto a) => new(ToVector(a.Position), a.Yaw);

    private sealed record SceneFile(int Format, AnchorDto Anchor, bool AnchorPlaced, bool PlaylistLoops, IReadOnlyList<Guid> Hidden, IReadOnlyList<EntryDto?> Playlist, IReadOnlyList<TrackDto?> Tracks);

    private sealed record PresetFile(int Format, float Yaw, TrackDto Track);

    private sealed record TrackDto(
        float Speed,
        AimMode Aim,
        PlaybackDirection Direction,
        bool Loop,
        VectorDto LookAt,
        bool LookAtPlaced,
        string? TargetName,
        string? TargetWorld,
        float AimHeight,
        float Smoothing,
        bool FollowTurns,
        bool FollowLooks,
        IReadOnlyList<PointDto?> Points,
        IReadOnlyList<TimingDto?> Timing,
        [property: JsonPropertyOrder(-1), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] Guid? Id = null,
        [property: JsonPropertyOrder(-1), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Name = null,
        [property: JsonPropertyOrder(-1), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] AnchorDto? Anchor = null,
        [property: JsonPropertyOrder(-1), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] bool? AnchorPlaced = null);

    private sealed record PointDto(VectorDto Position, float Yaw, float Pitch, float Fov, float Roll);

    private sealed record TimingDto(float? LegSpeed, float Hold, TangentMode InMode, TangentMode OutMode, float InTangent, float OutTangent, bool Broken);

    private sealed record EntryDto(Guid Id, Guid TrackId, int? Loops, Transition Transition);

    private sealed record AnchorDto(VectorDto Position, float Yaw);

    private sealed record VectorDto(float X, float Y, float Z);
}
