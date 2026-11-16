namespace CubeProject.Core.Models;

public readonly record struct RgbColor(byte R, byte G, byte B)
{
    public static readonly RgbColor Black = new(0, 0, 0);

    public static RgbColor FromHex(string hex)
    {
        hex = hex.TrimStart('#');
        return new RgbColor(
            Convert.ToByte(hex[..2], 16),
            Convert.ToByte(hex[2..4], 16),
            Convert.ToByte(hex[4..6], 16));
    }

    public string ToHex() => $"#{R:X2}{G:X2}{B:X2}";
}
