using System.Numerics;
using Danslicer.Cli;
using Danslicer.Core;
using Danslicer.Core.Geometry;
using Danslicer.Core.IO;
using Danslicer.Core.Printers;
using Danslicer.Core.Scene;

namespace Danslicer.Tests;

public sealed class SliceCommandTests
{
    [Fact]
    public void AllowOutOfBoundsControlsCliExitCodeAndWarning()
    {
        using var files = new TemporarySliceFiles();
        var document = new Document
        {
            Printer = PrinterDefinition.PhotonMonoX with
            {
                Id = "test-printer",
                IsBuiltIn = false,
                DisplayWidthMm = 20,
                DisplayHeightMm = 10,
                ZTravelMm = 1,
                ResolutionX = 20,
                ResolutionY = 10,
            },
        };
        var obj = new SceneObject("outside", Box(30, 5, 0.1f));
        obj.Transform = Transform.Identity with { Translation = new Vector3(-15, -2.5f, 0) };
        document.AddObject(obj);
        ProjectFile.Save(files.Project, document, new ProjectViewState());

        var refusedError = new StringWriter();
        var refused = SliceCommand.Run([files.Project, "-o", files.RefusedOutput],
            standardOutput: new StringWriter(), standardError: refusedError, reportProgress: false);

        Assert.Equal(2, refused);
        Assert.Contains("error:", refusedError.ToString());
        Assert.False(File.Exists(files.RefusedOutput));

        var allowedError = new StringWriter();
        var allowed = SliceCommand.Run(
            [files.Project, "-o", files.AllowedOutput, "--allow-out-of-bounds"],
            standardOutput: new StringWriter(), standardError: allowedError, reportProgress: false);

        Assert.Equal(0, allowed);
        Assert.Contains("warning: content outside the build area on X was cropped.",
            allowedError.ToString());
        Assert.True(File.Exists(files.AllowedOutput));
    }

    private static Mesh Box(float sx, float sy, float sz)
    {
        var positions = new Vector3[8];
        for (var i = 0; i < positions.Length; i++)
            positions[i] = new Vector3((i & 1) * sx, ((i >> 1) & 1) * sy, ((i >> 2) & 1) * sz);
        int[] indices =
        [
            0, 2, 3, 0, 3, 1, 4, 5, 7, 4, 7, 6, 0, 1, 5, 0, 5, 4,
            2, 6, 7, 2, 7, 3, 0, 4, 6, 0, 6, 2, 1, 3, 7, 1, 7, 5,
        ];
        return new Mesh(positions, indices);
    }

    private sealed class TemporarySliceFiles : IDisposable
    {
        private readonly string _directory = Path.Combine(Path.GetTempPath(), $"danslicer-{Guid.NewGuid():N}");
        public string Project => Path.Combine(_directory, "outside.danslicer");
        public string RefusedOutput => Path.Combine(_directory, "refused.pwmx");
        public string AllowedOutput => Path.Combine(_directory, "allowed.pwmx");

        public TemporarySliceFiles() => Directory.CreateDirectory(_directory);

        public void Dispose() => Directory.Delete(_directory, recursive: true);
    }
}
