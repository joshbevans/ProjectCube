using System.Windows.Media;
using System.Windows.Media.Media3D;
using CubeProject.Core.Models;

namespace CubeProject.App.Rendering;

/// <summary>
/// Pre-builds one frozen MeshGeometry3D per band. Per-frame updates are
/// just brush Color writes — no allocation or geometry rebuild.
/// </summary>
public sealed class BandedCubeRenderer : IDisposable
{
    private const double V = 0.12;
    private const double Gap = 1.0;

    private readonly ModelVisual3D _host;
    private readonly SolidColorBrush?[] _brushes;
    private bool _disposed;

    public BandedCubeRenderer(ModelVisual3D host, int bandCount, Func<int, int, int, int> bandOf)
    {
        _host = host;
        _brushes = new SolidColorBrush?[bandCount];

        var pts  = new List<Point3D>[bandCount];
        var tris = new List<int>[bandCount];
        for (int i = 0; i < bandCount; i++) { pts[i] = []; tris[i] = []; }

        for (int x = 0; x < 16; x++)
        for (int y = 0; y < 16; y++)
        for (int z = 0; z < 16; z++)
        {
            int b = bandOf(x, y, z);
            if ((uint)b < (uint)bandCount)
                AddBox(pts[b], tris[b], x * Gap, y * Gap, z * Gap);
        }

        var group = new Model3DGroup();
        for (int i = 0; i < bandCount; i++)
        {
            if (pts[i].Count == 0) continue;

            var mesh = new MeshGeometry3D();
            foreach (var p in pts[i])  mesh.Positions.Add(p);
            foreach (var t in tris[i]) mesh.TriangleIndices.Add(t);
            mesh.Freeze(); // positions never change

            var brush = new SolidColorBrush(Colors.Black);
            _brushes[i] = brush;
            group.Children.Add(new GeometryModel3D(mesh, new DiffuseMaterial(brush)));
        }
        // group NOT frozen — brush colors update in place each frame
        _host.Content = group;
    }

    public void SetBand(int band, RgbColor color)
    {
        if ((uint)band < (uint)_brushes.Length && _brushes[band] is { } brush)
            brush.Color = Color.FromRgb(color.R, color.G, color.B);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _host.Content = null;
    }

    private static void AddBox(List<Point3D> pts, List<int> tris, double cx, double cy, double cz)
    {
        int b = pts.Count;
        pts.Add(new Point3D(cx - V, cy - V, cz - V)); // 0
        pts.Add(new Point3D(cx + V, cy - V, cz - V)); // 1
        pts.Add(new Point3D(cx + V, cy + V, cz - V)); // 2
        pts.Add(new Point3D(cx - V, cy + V, cz - V)); // 3
        pts.Add(new Point3D(cx - V, cy - V, cz + V)); // 4
        pts.Add(new Point3D(cx + V, cy - V, cz + V)); // 5
        pts.Add(new Point3D(cx + V, cy + V, cz + V)); // 6
        pts.Add(new Point3D(cx - V, cy + V, cz + V)); // 7

        AddFace(tris, b, 0, 3, 2, 1); // -Z
        AddFace(tris, b, 4, 5, 6, 7); // +Z
        AddFace(tris, b, 0, 4, 7, 3); // -X
        AddFace(tris, b, 1, 2, 6, 5); // +X
        AddFace(tris, b, 0, 1, 5, 4); // -Y
        AddFace(tris, b, 3, 7, 6, 2); // +Y
    }

    private static void AddFace(List<int> tris, int b, int a, int c, int d, int e)
    {
        tris.Add(b + a); tris.Add(b + c); tris.Add(b + d);
        tris.Add(b + a); tris.Add(b + d); tris.Add(b + e);
    }
}
