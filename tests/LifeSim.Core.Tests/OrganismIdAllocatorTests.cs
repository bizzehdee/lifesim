using LifeSim.Core.Organisms;

namespace LifeSim.Core.Tests;

public class OrganismIdAllocatorTests
{
    [Fact]
    public void Allocate_returnsSequentialIds_startingFromNextId()
    {
        var allocator = new OrganismIdAllocator(10);

        Assert.Equal(10, allocator.Allocate());
        Assert.Equal(11, allocator.Allocate());
        Assert.Equal(12, allocator.Allocate());
        Assert.Equal(13, allocator.NextId);
    }

    [Fact]
    public void Allocate_neverReturnsTheSameIdTwice()
    {
        var allocator = new OrganismIdAllocator(0);
        var seen = new HashSet<long>();

        for (int i = 0; i < 1000; i++)
        {
            Assert.True(seen.Add(allocator.Allocate()));
        }
    }
}
