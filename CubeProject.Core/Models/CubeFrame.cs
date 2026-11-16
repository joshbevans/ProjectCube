namespace CubeProject.Core.Models;

public class CubeFrame
{
    public const int Size = 16;
    public const int VoxelCount = Size * Size * Size;

    // voxels[x, y, z]
    public RgbColor[,,] Voxels { get; } = new RgbColor[Size, Size, Size];

    public RgbColor this[int x, int y, int z]
    {
        get => Voxels[x, y, z];
        set => Voxels[x, y, z] = value;
    }

    public static int Index(int x, int y, int z) => x + y * Size + z * Size * Size;
}
