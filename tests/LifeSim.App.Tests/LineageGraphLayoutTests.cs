using LifeSim.App.Presentation;
using LifeSim.Core.Snapshot;

namespace LifeSim.App.Tests;

public class LineageGraphLayoutTests
{
    // Founder 1 → children 2, 3; grandchild 4 under 2. All one lineage (asexual).
    private static WorldSnapshot FamilySnapshot() => new()
    {
        Lineages =
        [
            new LineageSnapshot { OrganismId = 1, ParentId = null, LineageId = 1, GenerationDepth = 0 },
            new LineageSnapshot { OrganismId = 2, ParentId = 1, LineageId = 1, GenerationDepth = 1 },
            new LineageSnapshot { OrganismId = 3, ParentId = 1, LineageId = 1, GenerationDepth = 1 },
            new LineageSnapshot { OrganismId = 4, ParentId = 2, LineageId = 1, GenerationDepth = 2 },
        ],
    };

    [Fact]
    public void Build_showsTheAncestorLineAndDescendants_excludingSiblings()
    {
        // Focus 2: ancestor 1 (up) + descendant 4 (down); sibling 3 is neither, so excluded.
        LineageGraph graph = LineageGraphLayout.Build(FamilySnapshot(), focusId: 2);

        long[] expected = [1L, 2L, 4L];
        Assert.Equal(expected, graph.Nodes.Select(n => n.OrganismId).OrderBy(x => x));
        Assert.DoesNotContain(graph.Nodes, n => n.OrganismId == 3);
        Assert.Equal(2, graph.Edges.Count); // 1→2, 2→4

        Assert.Equal(0.0, graph.Nodes.Single(n => n.OrganismId == 1).Y, precision: 6); // topmost ancestor
        Assert.Equal(0.5, graph.Nodes.Single(n => n.OrganismId == 2).Y, precision: 6);
        Assert.Equal(1.0, graph.Nodes.Single(n => n.OrganismId == 4).Y, precision: 6);
        Assert.True(graph.Nodes.Single(n => n.OrganismId == 2).IsFocus);
    }

    [Fact]
    public void Build_respectsMaxParentGenerations()
    {
        // Focus 4 (grandchild). One ancestor generation → parent 2 only, not grandparent 1.
        LineageGraph graph = LineageGraphLayout.Build(FamilySnapshot(), focusId: 4, maxParentGenerations: 1, maxChildGenerations: 0);

        Assert.Contains(graph.Nodes, n => n.OrganismId == 2);
        Assert.DoesNotContain(graph.Nodes, n => n.OrganismId == 1);
        Assert.DoesNotContain(graph.Nodes, n => n.OrganismId == 3);
    }

    [Fact]
    public void Build_respectsMaxChildGenerations()
    {
        // Focus 1 (founder). One descendant generation → children 2, 3, but not grandchild 4.
        LineageGraph graph = LineageGraphLayout.Build(FamilySnapshot(), focusId: 1, maxParentGenerations: 0, maxChildGenerations: 1);

        long[] expected = [1L, 2L, 3L];
        Assert.Equal(expected, graph.Nodes.Select(n => n.OrganismId).OrderBy(x => x));
        Assert.DoesNotContain(graph.Nodes, n => n.OrganismId == 4);
    }

    [Fact]
    public void Build_isScopedToTheFocusLineage()
    {
        var snapshot = new WorldSnapshot
        {
            Lineages =
            [
                new LineageSnapshot { OrganismId = 1, ParentId = null, LineageId = 1, GenerationDepth = 0 },
                new LineageSnapshot { OrganismId = 2, ParentId = 1, LineageId = 1, GenerationDepth = 1 },
                new LineageSnapshot { OrganismId = 10, ParentId = null, LineageId = 10, GenerationDepth = 0 },
            ],
        };

        LineageGraph graph = LineageGraphLayout.Build(snapshot, focusId: 1);
        Assert.Equal(2, graph.Nodes.Count); // lineage 1 only (focus + its descendant)
        Assert.DoesNotContain(graph.Nodes, n => n.OrganismId == 10);
    }

    [Fact]
    public void Build_returnsEmptyForAnUnknownOrganism() =>
        Assert.Empty(LineageGraphLayout.Build(FamilySnapshot(), focusId: 999).Nodes);
}
