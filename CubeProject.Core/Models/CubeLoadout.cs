using System.Text.Json;
using System.Text.Json.Serialization;

namespace CubeProject.Core.Models;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum LoadoutMode { Preset, AudioReactive }

public class CubeLoadout
{
    public string Name { get; set; } = string.Empty;
    public LoadoutMode Mode { get; set; } = LoadoutMode.Preset;
    public string Description { get; set; } = string.Empty;
    public DateOnly Created { get; set; } = DateOnly.FromDateTime(DateTime.Today);

    // Preset mode
    public RgbColor[]? Frame { get; set; }

    // AudioReactive mode
    public string? Effect { get; set; }
    public Dictionary<string, JsonElement>? Params { get; set; }

    [JsonIgnore]
    public string? FilePath { get; set; }
}
