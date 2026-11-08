using PodcastGenerator.Domain.Enums;

namespace PodcastGenerator.UnitTests;

public class CefrLevelTests
{
    [Fact]
    public void Levels_are_ordered_from_beginner_to_mastery()
    {
        Assert.True(CefrLevel.A1 < CefrLevel.B1);
        Assert.True(CefrLevel.B2 < CefrLevel.C2);
    }
}
