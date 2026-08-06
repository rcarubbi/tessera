using Tessera.Domain.Merkle;

namespace Tessera.Domain.Tests;

public class SemanticHasherTests
{
    [Fact]
    public void SameContent_SameHash()
    {
        var a = SemanticHasher.Compute("# PaymentService\nresponsibilities", Array.Empty<ChildHash>());
        var b = SemanticHasher.Compute("# PaymentService\nresponsibilities", Array.Empty<ChildHash>());
        Assert.Equal(a, b);
    }

    [Fact]
    public void DifferentContent_DifferentHash()
    {
        var a = SemanticHasher.Compute("content a", Array.Empty<ChildHash>());
        var b = SemanticHasher.Compute("content b", Array.Empty<ChildHash>());
        Assert.NotEqual(a, b);
    }

    [Fact]
    public void ChildrenOrderDoesNotMatter()
    {
        var a = SemanticHasher.Compute("content", new[] { new ChildHash("B", "calls", "bbb"), new ChildHash("A", "uses", "aaa") });
        var b = SemanticHasher.Compute("content", new[] { new ChildHash("A", "uses", "aaa"), new ChildHash("B", "calls", "bbb") });
        Assert.Equal(a, b);
    }

    [Fact]
    public void ChildChange_ChangesParentHash()
    {
        var a = SemanticHasher.Compute("content", new[] { new ChildHash("A", "uses", "aaa") });
        var b = SemanticHasher.Compute("content", new[] { new ChildHash("A", "uses", "bbb") });
        Assert.NotEqual(a, b);
    }

    [Fact]
    public void SnapshotRoot_IgnoresNodeOrder()
    {
        var a = SemanticHasher.ComputeSnapshotRoot(new[] { "aaa", "bbb" });
        var b = SemanticHasher.ComputeSnapshotRoot(new[] { "bbb", "aaa" });
        Assert.Equal(a, b);
    }

    [Fact]
    public void SnapshotRoot_ChangesWhenNodeChanges()
    {
        var a = SemanticHasher.ComputeSnapshotRoot(new[] { "aaa", "bbb" });
        var b = SemanticHasher.ComputeSnapshotRoot(new[] { "aaa", "ccc" });
        Assert.NotEqual(a, b);
    }
}

public class MerkleDagTests
{
    [Fact]
    public void Acyclic_ParentHashDiffersFromChild()
    {
        var nodes = new[]
        {
            new DagNode { Key = "A", Content = "a", Children = Array.Empty<ChildHash>() },
            new DagNode { Key = "B", Content = "b", Children = new[] { new ChildHash("A", "calls", "") } },
        };
        var hashes = MerkleDag.ComputeHashes(nodes);
        Assert.NotEqual(hashes["A"], hashes["B"]);
    }

    [Fact]
    public void ChildChange_PropagatesToParent()
    {
        var child = new DagNode { Key = "A", Content = "a", Children = Array.Empty<ChildHash>() };
        var parent = new DagNode { Key = "B", Content = "b", Children = new[] { new ChildHash("A", "calls", "") } };

        var before = MerkleDag.ComputeHashes(new[] { child, parent });
        var after = MerkleDag.ComputeHashes(new[] { child with { Content = "a2" }, parent });

        Assert.NotEqual(before["A"], after["A"]);
        Assert.NotEqual(before["B"], after["B"]);
    }

    [Fact]
    public void UnchangedChild_KeepsParentHashStable()
    {
        var child = new DagNode { Key = "A", Content = "a", Children = Array.Empty<ChildHash>() };
        var parent = new DagNode { Key = "B", Content = "b", Children = new[] { new ChildHash("A", "calls", "") } };

        var first = MerkleDag.ComputeHashes(new[] { child, parent });
        var second = MerkleDag.ComputeHashes(new[] { child, parent });

        Assert.Equal(first["A"], second["A"]);
        Assert.Equal(first["B"], second["B"]);
    }

    [Fact]
    public void Cycle_StabilizesWithoutHanging()
    {
        var a = new DagNode { Key = "A", Content = "a", Children = new[] { new ChildHash("B", "calls", "") } };
        var b = new DagNode { Key = "B", Content = "b", Children = new[] { new ChildHash("A", "calls", "") } };
        var hashes = MerkleDag.ComputeHashes(new[] { a, b });
        Assert.True(hashes.ContainsKey("A"));
        Assert.True(hashes.ContainsKey("B"));
    }

    [Fact]
    public void OverrideHash_PreservesUnchangedNodeHash()
    {
        var child = new DagNode { Key = "A", Content = "a", Children = Array.Empty<ChildHash>() };
        var parent = new DagNode { Key = "B", Content = "b", Children = new[] { new ChildHash("A", "calls", "") } };

        var previous = MerkleDag.ComputeHashes(new[] { child, parent });
        var overrides = new Dictionary<string, string> { ["A"] = previous["A"] };
        var recomputed = MerkleDag.ComputeHashes(new[] { child, parent }, overrides);

        Assert.Equal(previous["A"], recomputed["A"]);
    }
}
