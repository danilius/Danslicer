using System.Numerics;
using Avalonia.Controls;
using Danslicer.App.Controls;
using Danslicer.Core;
using Danslicer.Core.Geometry;
using Danslicer.Core.Scene;

namespace Danslicer.App.Views;

/// <summary>
/// Minimal two-context host used to de-risk auxiliary OpenGL windows. It deliberately has no
/// application view model or shared document: both controls create and dispose their own
/// renderer in <see cref="ViewportControl"/>'s per-instance OpenGL lifecycle.
/// </summary>
internal sealed class GlContextProbeWindow : Window
{
    private readonly ViewportControl _left = new();
    private readonly ViewportControl _right = new();

    public GlContextProbeWindow()
    {
        Title = "OpenGL context probe";
        Width = 800;
        Height = 420;
        Content = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,*"),
            Children =
            {
                _left,
                _right,
            },
        };
        Grid.SetColumn(_right, 1);
        _left.Document = CreateDocument(-4f);
        _right.Document = CreateDocument(4f);
        Opened += (_, _) =>
        {
            _left.FrameAll();
            _right.FrameAll();
        };
    }

    private static Document CreateDocument(float x)
    {
        var document = new Document();
        document.AddObject(new SceneObject("context probe", Tetrahedron())
        {
            Transform = new Transform { Translation = new Vector3(x, 0, 4) },
        });
        return document;
    }

    private static Mesh Tetrahedron()
    {
        Vector3[] positions =
        [
            new(-3, -2, 0), new(3, -2, 0), new(0, 3, 0), new(0, 0, 6),
        ];
        int[] indices =
        [
            0, 2, 1,
            0, 1, 3,
            1, 2, 3,
            2, 0, 3,
        ];
        return new Mesh(positions, indices);
    }
}
