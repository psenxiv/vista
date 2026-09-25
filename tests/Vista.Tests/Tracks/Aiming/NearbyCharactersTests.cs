using System.Numerics;
using Vista.Core.Tracks.Aiming;
using Xunit;

namespace Vista.Tests.Tracks.Aiming;

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
        var characters = With(
            new LoadedCharacter("Guard", null, new Vector3(3f, 0f, 4f)),
            new LoadedCharacter("Merchant", null, new Vector3(9f, 0f, 9f))
        );

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
        var characters = With(
            new LoadedCharacter("Guard", null, new Vector3(-20f, 0f, 0f)),
            new LoadedCharacter("Guard", null, new Vector3(20f, 0f, 0f))
        );

        Assert.Equal(
            new Vector3(20f, 0f, 0f),
            characters.FindCharacter("Guard", null, new Vector3(15f, 0f, 0f))?.Position
        );
        Assert.Equal(
            new Vector3(-20f, 0f, 0f),
            characters.FindCharacter("Guard", null, new Vector3(-1f, 0f, 0f))?.Position
        );
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
        var characters = With(
            new LoadedCharacter("Aya", "Gilgamesh", new Vector3(1f, 0f, 0f)),
            new LoadedCharacter("Aya", "Cactuar", new Vector3(30f, 0f, 0f))
        );

        Assert.Equal(new Vector3(30f, 0f, 0f), characters.FindCharacter("Aya", "Cactuar", Vector3.Zero)?.Position);
        Assert.Equal(
            new Vector3(1f, 0f, 0f),
            characters.FindCharacter("Aya", "Gilgamesh", new Vector3(30f, 0f, 0f))?.Position
        );
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
        var characters = With(
            new LoadedCharacter("Aya", "Gilgamesh", new Vector3(20f, 0f, 0f)),
            new LoadedCharacter("Aya", null, new Vector3(-20f, 0f, 0f))
        );

        Assert.Equal(
            new Vector3(20f, 0f, 0f),
            characters.FindCharacter("Aya", null, new Vector3(15f, 0f, 0f))?.Position
        );
        Assert.Equal(
            new Vector3(-20f, 0f, 0f),
            characters.FindCharacter("Aya", null, new Vector3(-15f, 0f, 0f))?.Position
        );
    }

    // Loaded Bob of B before bob of A, so a sort that kept input order would list Bob first.
    private static NearbyCharacters Crowd() =>
        With(
            new LoadedCharacter("Bob", "B", Vector3.Zero),
            new LoadedCharacter("bob", "A", Vector3.Zero),
            new LoadedCharacter("Alice", null, Vector3.Zero),
            new LoadedCharacter("bob", "A", Vector3.UnitX),
            new LoadedCharacter("Carl", "A", Vector3.Zero)
        );

    // " b " trims to "b": Bob and bob contain it ignoring case, Alice and Carl don't; the second bob of A goes;
    // Bob and bob tie on name ignoring case, so their worlds order them, A then B.

    [Fact]
    public void TheListMatchesTheSearchOncePerNameAndWorldInNameThenWorldOrder()
    {
        var listed = Crowd().Listed(" b ");

        Assert.Equal([("bob", "A"), ("Bob", "B")], listed.Select(c => (c.Name, c.World)));
    }

    // With no search every name and world is listed once: Alice, then bob (A) and Bob (B), then Carl.

    [Fact]
    public void AnEmptySearchListsEveryone()
    {
        var listed = Crowd().Listed("");

        Assert.Equal(
            [("Alice", null), ("bob", "A"), ("Bob", "B"), ("Carl", "A")],
            listed.Select(c => (c.Name, c.World))
        );
    }

    [Fact]
    public void ALabelNamesTheWorldOrNpc()
    {
        Assert.Equal("Guard · NPC", NearbyCharacters.Label("Guard", null));
        Assert.Equal("Ann · Ultros", NearbyCharacters.Label("Ann", "Ultros"));
    }
}
