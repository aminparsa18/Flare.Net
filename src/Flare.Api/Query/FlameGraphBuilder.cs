using Flare.Api.Model;

namespace Flare.Api.Query;

/// <summary>
/// Pure assembly of a flame-graph call tree from <c>(stack, value)</c> rows: stacks that share a
/// root-first prefix merge into one path, so a frame's <see cref="FlameGraphNode.Total"/> is the
/// sum of every stack passing through it. Children are sorted heaviest first (ties by name) so
/// the output is deterministic.
/// </summary>
public static class FlameGraphBuilder
{
    public const string RootName = "all";

    public static FlameGraphNode Build(IEnumerable<(IReadOnlyList<string> Stack, long Value)> stacks)
    {
        var root = new MutableNode(RootName);
        foreach (var (stack, value) in stacks)
        {
            if (value <= 0)
            {
                continue;
            }

            var node = root;
            node.Total += value;
            foreach (var frame in stack)
            {
                if (!node.Children.TryGetValue(frame, out var child))
                {
                    child = new MutableNode(frame);
                    node.Children[frame] = child;
                }
                node = child;
                node.Total += value;
            }
            node.Self += value;
        }

        return Freeze(root);
    }

    // Iterative: a pathological stack can be thousands of frames deep.
    private static FlameGraphNode Freeze(MutableNode root)
    {
        var results = new Dictionary<MutableNode, FlameGraphNode>(ReferenceEqualityComparer.Instance);
        var pending = new Stack<(MutableNode Node, bool Visited)>();
        pending.Push((root, false));
        while (pending.Count > 0)
        {
            var (node, visited) = pending.Pop();
            if (!visited)
            {
                pending.Push((node, true));
                foreach (var child in node.Children.Values)
                {
                    pending.Push((child, false));
                }
                continue;
            }

            results[node] = new FlameGraphNode
            {
                Name = node.Name,
                Total = node.Total,
                Self = node.Self,
                Children = [.. node.Children.Values
                    .OrderByDescending(c => c.Total)
                    .ThenBy(c => c.Name, StringComparer.Ordinal)
                    .Select(c => results[c])],
            };
        }
        return results[root];
    }

    private sealed class MutableNode(string name)
    {
        public string Name { get; } = name;

        public long Total { get; set; }

        public long Self { get; set; }

        public Dictionary<string, MutableNode> Children { get; } = new(StringComparer.Ordinal);
    }
}
