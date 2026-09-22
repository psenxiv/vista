using Dalamud.Game.ClientState.Objects.Enums;
using Vista.Core.Tracks;

namespace Vista.Plugin.Game;

/// <summary>Reads the players and NPCs loaded nearby from the object table.</summary>
internal static class CharacterTable
{
    /// <summary>Every loaded player and NPC with a name, and where its feet are. Main thread only.</summary>
    public static IReadOnlyList<LoadedCharacter> Read()
    {
        var found = new List<LoadedCharacter>();
        foreach (var obj in Plugin.ObjectTable)
        {
            if (obj.ObjectKind is not (ObjectKind.Pc or ObjectKind.BattleNpc or ObjectKind.EventNpc)) continue;
            var name = obj.Name.TextValue;
            if (string.IsNullOrEmpty(name)) continue;
            found.Add(new LoadedCharacter(name, obj.Position));
        }

        return found;
    }
}
