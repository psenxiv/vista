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
        var characters = With(new LoadedCharacter("Guard", null, new Vector3(3f, 0f, 4f)), new LoadedCharacter("Merchant", null, new Vector3(9f, 0f, 9f)));

        Assert.Equal(new Vector3(3f, 0f, 4f), characters.Find("Guard", null, Vector3.Zero));
    }

    [Fact]
    public void AnUnknownOrDifferentlyCasedNameIsNotFound()
    {
        var characters = With(new LoadedCharacter("Guard", null, Vector3.Zero));

        Assert.Null(characters.Find("Merchant", null, Vector3.Zero));
        Assert.Null(characters.Find("guard", null, Vector3.Zero));
        Assert.Null(new NearbyCharacters().Find("Guard", null, Vector3.Zero));
    }

    [Fact]
    public void ADuplicateNameResolvesToTheOneNearestThePlaceGiven()
    {
        var characters = With(new LoadedCharacter("Guard", null, new Vector3(-20f, 0f, 0f)), new LoadedCharacter("Guard", null, new Vector3(20f, 0f, 0f)));

        Assert.Equal(new Vector3(20f, 0f, 0f), characters.Find("Guard", null, new Vector3(15f, 0f, 0f)));
        Assert.Equal(new Vector3(-20f, 0f, 0f), characters.Find("Guard", null, new Vector3(-1f, 0f, 0f)));
    }

    [Fact]
    public void UpdateKeepsItsOwnCopyOfTheCharacters()
    {
        var loaded = new List<LoadedCharacter> { new("Guard", null, new Vector3(3f, 0f, 4f)) };
        var characters = new NearbyCharacters();
        characters.Update(loaded);

        loaded.Clear();

        Assert.Equal(new Vector3(3f, 0f, 4f), characters.Find("Guard", null, Vector3.Zero));
        Assert.Single(characters.All);
    }

    [Fact]
    public void AWorldMatchesOnlyThePlayerFromThatWorld()
    {
        var characters = With(new LoadedCharacter("Aya", "Gilgamesh", new Vector3(1f, 0f, 0f)), new LoadedCharacter("Aya", "Cactuar", new Vector3(30f, 0f, 0f)));

        Assert.Equal(new Vector3(30f, 0f, 0f), characters.Find("Aya", "Cactuar", Vector3.Zero));
        Assert.Equal(new Vector3(1f, 0f, 0f), characters.Find("Aya", "Gilgamesh", new Vector3(30f, 0f, 0f)));
        Assert.Null(characters.Find("Aya", "Sargatanas", Vector3.Zero));
    }

    [Fact]
    public void AWorldNeverMatchesAnNpc()
    {
        var characters = With(new LoadedCharacter("Guard", null, Vector3.Zero));

        Assert.Null(characters.Find("Guard", "Gilgamesh", Vector3.Zero));
    }

    [Fact]
    public void WithNoWorldTheNearestOfTheNameIsFoundWhateverItsWorld()
    {
        var characters = With(new LoadedCharacter("Aya", "Gilgamesh", new Vector3(20f, 0f, 0f)), new LoadedCharacter("Aya", null, new Vector3(-20f, 0f, 0f)));

        Assert.Equal(new Vector3(20f, 0f, 0f), characters.Find("Aya", null, new Vector3(15f, 0f, 0f)));
        Assert.Equal(new Vector3(-20f, 0f, 0f), characters.Find("Aya", null, new Vector3(-15f, 0f, 0f)));
    }
}
