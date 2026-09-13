using Clipper2Lib;

namespace Danslicer.Core.Slicing;

internal static class PolygonComponents
{
    // Tree ownership matters: a hole belongs only to its immediate solid parent.
    internal static List<Paths64> Split(Paths64 paths)
    {
        var clipper = new Clipper64();
        clipper.AddSubject(paths);
        var tree = new PolyTree64();
        clipper.Execute(ClipType.Union, FillRule.NonZero, tree);
        var result = new List<Paths64>();
        Visit(tree);
        return result;

        void Visit(PolyPath64 parent)
        {
            for (int i = 0; i < parent.Count; i++)
            {
                var child = parent[i];
                if (!child.IsHole && child.Polygon is { } outer)
                {
                    var component = new Paths64 { outer };
                    for (int j = 0; j < child.Count; j++)
                        if (child[j].Polygon is { } hole) component.Add(hole);
                    result.Add(component);
                }
                Visit(child);
            }
        }
    }
}
