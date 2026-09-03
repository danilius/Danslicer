using Danslicer.Core.Supports.Generation;
using Danslicer.Core;
using Danslicer.Core.Config;
using Danslicer.Core.Scene;

namespace Danslicer.Tests;

public sealed class SupportPreviewShapesTests
{
    public static IEnumerable<object[]> ProceduralNames() => SupportPreviewShapes.Names
        .Where(name => name != "Current selection")
        .Select(name => new object[] { name });

    [Fact]
    public void EveryProceduralPreviewShapeIsFiniteAndAboveThePlate()
    {
        foreach (var name in SupportPreviewShapes.Names.Where(name => name != "Current selection"))
        {
            var mesh = SupportPreviewShapes.Create(name);

            Assert.NotEmpty(mesh.Positions);
            Assert.NotEmpty(mesh.Indices);
            Assert.Equal(0, mesh.Indices.Length % 3);
            Assert.All(mesh.Positions, position => Assert.True(
                float.IsFinite(position.X) && float.IsFinite(position.Y) && float.IsFinite(position.Z),
                $"{name} contains a non-finite position."));
            Assert.True(mesh.Bounds.Min.Z >= 0, $"{name} extends below the plate.");
            Assert.True(mesh.Bounds.Max.Z > mesh.Bounds.Min.Z, $"{name} has no height.");
            Assert.All(mesh.Indices, index => Assert.InRange(index, 0, mesh.Positions.Length - 1));
        }
    }

    [Fact]
    public void CurrentSelectionIsNotMistakenForAProceduralShape()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            SupportPreviewShapes.Create("Current selection"));
    }

    [Theory]
    [MemberData(nameof(ProceduralNames))]
    public void ProceduralShapeProducesAnHonestGenerationSummary(string name)
    {
        var document = new Document
        {
            SupportSettings = new SupportConfig
            {
                Spacing = 4,
                IslandSpacingMm = 1,
                UseBaseGrid = false,
            },
        };
        var preview = new SceneObject(name, SupportPreviewShapes.Create(name));
        document.AddObject(preview);

        var prepared = Document.ComputeSupportGeneration(
            document.CaptureSupportGeneration(preview));

        Assert.True(prepared.Summary.CandidateCount > 0,
            $"{name} did not exercise support placement.");
        Assert.Equal(prepared.Summary.UnroutedTipCount,
            prepared.Summary.RefusalReasons.Values.Sum());
    }
}
