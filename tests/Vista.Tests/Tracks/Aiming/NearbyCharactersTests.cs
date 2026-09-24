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
    public void FindCharacterGivesTheNamedCharactersFeet()
    {
        var characters = With(new LoadedCharacter("Guard", null, new Vector3(3f, 0f, 4f)), new LoadedCharacter("Merchant", null, new Vector3(9f, 0f, 9f)));

        Assert.Equal(new Vector3(3f, 0f, 4f), characters.FindCharacter("Guard", null, Vector3.Zero)?.Position);
    }

    [Fact]
    public void AnUnknownOrDifferentlyCasedNameIsNotFound()
    {
        var characters = With(new LoadedCharacter("Guard", null, Vector3.Zero));

        Assert.Null(characters.FindCharacter("Merchant", null, Vector3.Zero));
        Assert.Null(characters.FindCharacter("guard", null, Vector3.Zero));
        Assert.Null(new NearbyCharacters().FindCharacter("Guard", null, Vector3.Zero));
    }

    [Fact]
    public void ADuplicateNameResolvesToTheOneNearestThePlaceGiven()
    {
        var characters = With(new LoadedCharacter("Guard", null, new Vector3(-20f, 0f, 0f)), new LoadedCharacter("Guard", null, new Vector3(20f, 0f, 0f)));

        Assert.Equal(new Vector3(20f, 0f, 0f), characters.FindCharacter("Guard", null, new Vector3(15f, 0f, 0f))?.Position);
        Assert.Equal(new Vector3(-20f, 0f, 0f), characters.FindCharacter("Guard", null, new Vector3(-1f, 0f, 0f))?.Position);
    }

    [Fact]
    public void UpdateKeepsItsOwnCopyOfTheCharacters()
    {
        var loaded = new List<LoadedCharacter> { new("Guard", null, new Vector3(3f, 0f, 4f)) };
        var characters = new NearbyCharacters();
        characters.Update(loaded);

        loaded.Clear();

        Assert.Equal(new Vector3(3f, 0f, 4f), characters.FindCharacter("Guard", null, Vector3.Zero)?.Position);
        Assert.Single(characters.All);
    }

    [Fact]
    public void AWorldMatchesOnlyThePlayerFromThatWorld()
    {
        var characters = With(new LoadedCharacter("Aya", "Gilgamesh", new Vector3(1f, 0f, 0f)), new LoadedCharacter("Aya", "Cactuar", new Vector3(30f, 0f, 0f)));

        Assert.Equal(new Vector3(30f, 0f, 0f), characters.FindCharacter("Aya", "Cactuar", Vector3.Zero)?.Position);
        Assert.Equal(new Vector3(1f, 0f, 0f), characters.FindCharacter("Aya", "Gilgamesh", new Vector3(30f, 0f, 0f))?.Position);
        Assert.Null(characters.FindCharacter("Aya", "Sargatanas", Vector3.Zero));
    }

    [Fact]
    public void AWorldNeverMatchesAnNpc()
    {
        var characters = With(new LoadedCharacter("Guard", null, Vector3.Zero));

        Assert.Null(characters.FindCharacter("Guard", "Gilgamesh", Vector3.Zero));
    }

    [Fact]
    public void WithNoWorldTheNearestOfTheNameIsFoundWhateverItsWorld()
    {
        var characters = With(new LoadedCharacter("Aya", "Gilgamesh", new Vector3(20f, 0f, 0f)), new LoadedCharacter("Aya", null, new Vector3(-20f, 0f, 0f)));

        Assert.Equal(new Vector3(20f, 0f, 0f), characters.FindCharacter("Aya", null, new Vector3(15f, 0f, 0f))?.Position);
        Assert.Equal(new Vector3(-20f, 0f, 0f), characters.FindCharacter("Aya", null, new Vector3(-15f, 0f, 0f))?.Position);
    }
}
