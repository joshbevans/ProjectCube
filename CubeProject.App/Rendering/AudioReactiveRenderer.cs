using System.Text.Json;
using System.Windows.Threading;
using CubeProject.Core.Models;

namespace CubeProject.App.Rendering;

public class AudioReactiveRenderer : IDisposable
{
    private readonly CubeRenderer _renderer;
    private DispatcherTimer? _timer;
    private CubeLoadout? _loadout;
    private double _t = 0;

    public AudioReactiveRenderer(CubeRenderer renderer) => _renderer = renderer;

    public void Start(CubeLoadout loadout)
    {
        Stop();
        _loadout = loadout;
        _t = 0;
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(33) }; // ~30fps
        _timer.Tick += Tick;
        _timer.Start();
    }

    public void Stop()
    {
        _timer?.Stop();
        _timer = null;
    }

    private void Tick(object? s, EventArgs e)
    {
        _t += 0.04;
        var frame = _loadout?.Effect?.ToLowerInvariant() switch
        {
            "verticalbars" => RenderVerticalBars(),
            _ => RenderVerticalBars()
        };
        _renderer.Render(frame);
    }

    private CubeFrame RenderVerticalBars()
    {
        var colorLow  = GetColorParam("colorLow",  new RgbColor(0, 0, 255));
        var colorHigh = GetColorParam("colorHigh", new RgbColor(255, 0, 0));
        var sensitivity = GetFloatParam("sensitivity", 0.8f);

        var frame = new CubeFrame();

        for (var x = 0; x < 16; x++)
        {
            // Layered sines per column to fake a spectrum
            float level = (float)(
                0.40 * Math.Sin(_t * 2.1 + x * 0.45) +
                0.28 * Math.Sin(_t * 3.7 + x * 0.90) +
                0.20 * Math.Sin(_t * 1.5 - x * 0.35) +
                0.12 * Math.Sin(_t * 5.3 + x * 1.20));
            level = Math.Clamp((level + 1f) / 2f * sensitivity, 0f, 1f);

            var barHeight = (int)(level * 16);
            for (var y = 0; y < barHeight; y++)
            {
                var color = Lerp(colorLow, colorHigh, y / 15f);
                for (var z = 0; z < 16; z++)
                    frame[x, y, z] = color;
            }
        }

        return frame;
    }

    private RgbColor GetColorParam(string key, RgbColor fallback)
    {
        if (_loadout?.Params?.TryGetValue(key, out var el) != true) return fallback;
        try
        {
            if (el.ValueKind == JsonValueKind.Array)
            {
                var arr = el.EnumerateArray().Select(e => e.GetByte()).ToArray();
                if (arr.Length >= 3) return new RgbColor(arr[0], arr[1], arr[2]);
            }
        }
        catch { }
        return fallback;
    }

    private float GetFloatParam(string key, float fallback)
    {
        if (_loadout?.Params?.TryGetValue(key, out var el) != true) return fallback;
        try { return (float)el.GetDouble(); } catch { return fallback; }
    }

    private static RgbColor Lerp(RgbColor a, RgbColor b, float t) => new(
        (byte)(a.R + (b.R - a.R) * t),
        (byte)(a.G + (b.G - a.G) * t),
        (byte)(a.B + (b.B - a.B) * t));

    public void Dispose() => Stop();
}
