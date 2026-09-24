using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Vista.Core.Tracks;
using Vista.Core.Tracks.Aiming;

namespace Vista.Plugin.Game;

/// <summary>Reads the players and NPCs loaded nearby from the object table.</summary>
internal static class CharacterTable
{
    /// <summary>Every loaded player and NPC with a name, a player's home world, where its feet are and which way it faces. Main thread only.</summary>
    public static IReadOnlyList<LoadedCharacter> Read()
    {
        var found = new List<LoadedCharacter>();
        foreach (var obj in Plugin.ObjectTable)
        {
            if (obj.ObjectKind is not (ObjectKind.Pc or ObjectKind.BattleNpc or ObjectKind.EventNpc)) continue;
            var name = obj.Name.TextValue;
            if (string.IsNullOrEmpty(name)) continue;
            found.Add(new LoadedCharacter(name, WorldOf(obj as IPlayerCharacter), obj.Position, obj.Rotation + MathF.PI));
        }

        return found;
    }

    private static string? WorldOf(IPlayerCharacter? player)
    {
        if (player?.HomeWorld.ValueNullable is not { } world) return null;
        var name = world.Name.ExtractText();
        return string.IsNullOrEmpty(name) ? null : name;
    }
}
