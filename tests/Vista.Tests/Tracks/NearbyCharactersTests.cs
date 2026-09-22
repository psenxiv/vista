using System.Numerics;
using Vista.Core.Tracks;
using Xunit;

namespace Vista.Tests.Tracks;

public class NearbyCharactersTests
{
    private static NearbyCharacters With(params LoadedCharacter[] loaded)
    {
        var characters = new NearbyCharacters();
        characters.Update(loaded);
        return characters;
    }

    [Fact]
    public void FindGivesTheNamedCharactersFeet()
    {
        var characters = With(new LoadedCharacter("Guard", new Vector3(3f, 0f, 4f)), new LoadedCharacter("Merchant", new Vector3(9f, 0f, 9f)));

        Assert.Equal(new Vector3(3f, 0f, 4f), characters.Find("Guard", Vector3.Zero));
    }

    [Fact]
    public void AnUnknownOrDifferentlyCasedNameIsNotFound()
    {
        var characters = With(new LoadedCharacter("Guard", Vector3.Zero));

        Assert.Null(characters.Find("Merchant", Vector3.Zero));
        Assert.Null(characters.Find("guard", Vector3.Zero));
        Assert.Null(new NearbyCharacters().Find("Guard", Vector3.Zero));
    }

    [Fact]
    public void ADuplicateNameResolvesToTheOneNearestThePlaceGiven()
    {
        var characters = With(new LoadedCharacter("Guard", new Vector3(-20f, 0f, 0f)), new LoadedCharacter("Guard", new Vector3(20f, 0f, 0f)));

        Assert.Equal(new Vector3(20f, 0f, 0f), characters.Find("Guard", new Vector3(15f, 0f, 0f)));
        Assert.Equal(new Vector3(-20f, 0f, 0f), characters.Find("Guard", new Vector3(-1f, 0f, 0f)));
    }

    [Fact]
    public void UpdateKeepsItsOwnCopyOfTheCharacters()
    {
        var loaded = new List<LoadedCharacter> { new("Guard", new Vector3(3f, 0f, 4f)) };
        var characters = new NearbyCharacters();
        characters.Update(loaded);

        loaded.Clear();

        Assert.Equal(new Vector3(3f, 0f, 4f), characters.Find("Guard", Vector3.Zero));
        Assert.Single(characters.All);
    }
}
