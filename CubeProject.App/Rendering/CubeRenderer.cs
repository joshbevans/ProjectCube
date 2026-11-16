using System.Windows.Media;
using System.Windows.Media.Media3D;
using CubeProject.Core.Models;

namespace CubeProject.App.Rendering;

public class CubeRenderer
{
    private readonly ModelVisual3D _host;
    private const double V = 0.45; // half voxel size
    private const double Gap = 1.0;

    public CubeRenderer(ModelVisual3D host) => _host = host;

    public void Render(CubeFrame frame)
    {
        // Group voxels by color so each unique color gets one GeometryModel3D
        var byColor = new Dictionary<RgbColor, (List<Point3D> pts, List<int> tris)>();

        for (var x = 0; x < CubeFrame.Size; x++)
        for (var y = 0; y < CubeFrame.Size; y++)
        for (var z = 0; z < CubeFrame.Size; z++)
        {
            var color = frame[x, y, z];
            if (color == RgbColor.Black) continue;

            if (!byColor.TryGetValue(color, out var buffers))
                byColor[color] = buffers = (new List<Point3D>(), new List<int>());

            AddBox(buffers.pts, buffers.tris, x * Gap, y * Gap, z * Gap);
        }

        if (byColor.Count == 0)
        {
            _host.Content = null;
            return;
        }

        var group = new Model3DGroup();
        foreach (var (color, (pts, tris)) in byColor)
        {
            var geom = new MeshGeometry3D();
            foreach (var p in pts) geom.Positions.Add(p);
            foreach (var i in tris) geom.TriangleIndices.Add(i);
            geom.Freeze();

            var brush = new SolidColorBrush(Color.FromRgb(color.R, color.G, color.B));
            brush.Freeze();
            var mat = new DiffuseMaterial(brush);
            mat.Freeze();

            group.Children.Add(new GeometryModel3D(geom, mat));
        }

        group.Freeze();
        _host.Content = group;
    }

    public void Clear() => _host.Content = null;

    private void AddBox(List<Point3D> pts, List<int> tris, double cx, double cy, double cz)
    {
        // 8 corners
        int b = pts.Count;
        pts.Add(new Point3D(cx - V, cy - V, cz - V)); // 0
        pts.Add(new Point3D(cx + V, cy - V, cz - V)); // 1
        pts.Add(new Point3D(cx + V, cy + V, cz - V)); // 2
        pts.Add(new Point3D(cx - V, cy + V, cz - V)); // 3
        pts.Add(new Point3D(cx - V, cy - V, cz + V)); // 4
        pts.Add(new Point3D(cx + V, cy - V, cz + V)); // 5
        pts.Add(new Point3D(cx + V, cy + V, cz + V)); // 6
        pts.Add(new Point3D(cx - V, cy + V, cz + V)); // 7

        // 6 faces × 2 triangles each
        AddFace(tris, b, 0, 1, 2, 3); // -Z
        AddFace(tris, b, 5, 4, 7, 6); // +Z
        AddFace(tris, b, 4, 0, 3, 7); // -X
        AddFace(tris, b, 1, 5, 6, 2); // +X
        AddFace(tris, b, 0, 4, 5, 1); // -Y
        AddFace(tris, b, 3, 2, 6, 7); // +Y
    }

    private static void AddFace(List<int> tris, int b, int a, int c, int d, int e)
    {
        tris.Add(b + a); tris.Add(b + c); tris.Add(b + d);
        tris.Add(b + a); tris.Add(b + d); tris.Add(b + e);
    }
}
