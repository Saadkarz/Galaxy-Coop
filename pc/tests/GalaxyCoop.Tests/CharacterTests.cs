using GalaxyCoop.Companion;
using GalaxyCoop.Protocol;

namespace GalaxyCoop.Tests;

public class CharacterTests
{
    [Theory]
    [InlineData("mario", CompanionFlags.ForceMario)]
    [InlineData("Luigi", CompanionFlags.ForceLuigi)]
    [InlineData("save", CompanionFlags.None)]
    [InlineData("", CompanionFlags.None)]
    [InlineData(null, CompanionFlags.None)]
    public void CharacterChoiceBecomesAForceFlag(string? choice, CompanionFlags expected) =>
        Assert.Equal(expected, ConnectMode.CharacterFlag(choice));

    [Fact]
    public void UnknownCharacterIsRejected() =>
        Assert.ThrowsAny<ArgumentException>(() => ConnectMode.CharacterFlag("wario"));
}
