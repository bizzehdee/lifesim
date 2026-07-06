using LifeSim.Core.Naming;

namespace LifeSim.Core.Tests;

public class WordListTests
{
    [Theory]
    [InlineData("adjectives-1")]
    [InlineData("nouns-1")]
    public void Load_hasNoBlankOrDuplicateEntries(string version)
    {
        WordList list = WordList.Load(version);

        Assert.True(list.Count > 0);

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < list.Count; i++)
        {
            Assert.False(string.IsNullOrWhiteSpace(list[i]));
            Assert.True(seen.Add(list[i]), $"Duplicate word '{list[i]}' in '{version}'.");
        }
    }

    [Fact]
    public void Load_isCached_andReturnsSameCount()
    {
        WordList a = WordList.Load("adjectives-1");
        WordList b = WordList.Load("adjectives-1");

        Assert.Equal(a.Count, b.Count);
    }

    [Fact]
    public void Load_throwsForUnknownVersion()
    {
        Assert.Throws<InvalidOperationException>(() => WordList.Load("no-such-list"));
    }
}
