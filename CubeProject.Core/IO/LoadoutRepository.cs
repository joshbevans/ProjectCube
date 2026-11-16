using System.Text.Json;
using System.Text.Json.Serialization;
using CubeProject.Core.Models;

namespace CubeProject.Core.IO;

public class LoadoutRepository
{
    public static string LoadoutsFolder =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Loadouts");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public static void EnsureFolder() => Directory.CreateDirectory(LoadoutsFolder);

    public static async Task<CubeLoadout?> LoadAsync(string path)
    {
        await using var stream = File.OpenRead(path);
        var loadout = await JsonSerializer.DeserializeAsync<CubeLoadout>(stream, JsonOptions);
        if (loadout is not null)
            loadout.FilePath = path;
        return loadout;
    }

    public static async Task SaveAsync(CubeLoadout loadout, string path)
    {
        EnsureFolder();
        await using var stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, loadout, JsonOptions);
        loadout.FilePath = path;
    }

    public static IEnumerable<string> EnumeratePaths() =>
        Directory.Exists(LoadoutsFolder)
            ? Directory.EnumerateFiles(LoadoutsFolder, "*.loadout")
            : [];

    public static CubeFrame ToFrame(CubeLoadout loadout)
    {
        var frame = new CubeFrame();
        if (loadout.Frame is null) return frame;

        for (var i = 0; i < Math.Min(loadout.Frame.Length, CubeFrame.VoxelCount); i++)
        {
            int x = i % CubeFrame.Size;
            int y = i / CubeFrame.Size % CubeFrame.Size;
            int z = i / (CubeFrame.Size * CubeFrame.Size);
            frame[x, y, z] = loadout.Frame[i];
        }
        return frame;
    }
}
