using System.Text.Json;
using System.Windows.Threading;
using NAudio.CoreAudioApi;
using NAudio.Dsp;
using NAudio.Wave;
using CubeProject.Core.Models;

namespace CubeProject.App.Rendering;

public class AudioReactiveRenderer : IDisposable
{
    private readonly SkiaCubeRenderer _skia;
    private readonly Action           _invalidate; // triggers SKElement redraw

    private DispatcherTimer? _timer;
    private CubeLoadout?     _loadout;
    private double           _t;
    private int              _tick;

    // Bass tracking: fast-attack envelope + slowly decaying peak for auto-gain,
    // so heavy bass (dubstep) is measured relative to itself rather than clipping.
    private float            _bassEnv;
    private float            _bassPeak = 1e-4f;

    // Audio capture
#pragma warning disable CS0618
    private WasapiLoopbackCapture? _capture;
#pragma warning restore CS0618
    private float[]?               _sampleBuffer;
    private int                    _bufferPos;
    private const int              FftSize = 2048;
    private readonly float[]       _spectrum    = new float[16];
    private readonly object        _spectrumLock = new();
    private bool                   _hasRealAudio;

    public AudioReactiveRenderer(SkiaCubeRenderer skia, Action invalidate)
    {
        _skia       = skia;
        _invalidate = invalidate;
    }

    public void Start(CubeLoadout loadout, MMDevice? device = null)
    {
        Stop();
        _loadout      = loadout;
        _t            = 0;
        _tick         = 0;
        _bassEnv      = 0;
        _bassPeak     = 1e-4f;
        _hasRealAudio = false;

        if (device is not null) TryStartCapture(device);

        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(33) };
        _timer.Tick += Tick;
        _timer.Start();
    }

    public void Stop()
    {
        _timer?.Stop();
        _timer = null;
        _capture?.StopRecording();
        _capture?.Dispose();
        _capture       = null;
        _hasRealAudio  = false;
    }

    private void Tick(object? s, EventArgs e)
    {
        _t += 0.04;
        _tick++;
        switch (_loadout?.Effect?.ToLowerInvariant())
        {
            case "rainbowcycle": TickRainbowCycle(); break;
            case "aurorawaves":  TickAuroraWaves();  break;
            case "bassorb":      TickBassOrb();      break;
            default:             TickVerticalBars();  break;
        }
        _invalidate(); // ask SKElement to repaint
    }

    // ── RainbowCycle ──────────────────────────────────────────────────────────

    private void TickRainbowCycle()
    {
        for (int x = 0; x < 16; x++)
        for (int y = 0; y < 16; y++)
        for (int z = 0; z < 16; z++)
        {
            float hue = ((x + y + z) / 45f + (float)(_t * 0.5)) % 1f;
            _skia.SetLed(x, y, z, HueToRgb(hue));
        }
    }

    // ── AuroraWaves ───────────────────────────────────────────────────────────

    // (hue, amplitude, base height, x freq, z freq, phase, drift speed)
    private static readonly (float H, float Amp, float Base, float Fx, float Fz, float Ph, float Spd)[] AuroraLayers =
    [
        (0.38f, 2.5f,  5.0f, 0.45f, 0.30f, 0.0f, 1.00f), // green
        (0.50f, 2.0f,  9.0f, 0.35f, 0.50f, 1.7f, 0.75f), // teal
        (0.80f, 1.8f, 12.5f, 0.55f, 0.25f, 3.1f, 1.30f), // violet
    ];

    private void TickAuroraWaves()
    {
        float t         = (float)_t * GetFloatParam("speed", 1f);
        float thickness = GetFloatParam("thickness", 1.6f);

        Span<float> surf = stackalloc float[AuroraLayers.Length];
        Span<float> hue  = stackalloc float[AuroraLayers.Length];

        for (int x = 0; x < 16; x++)
        for (int z = 0; z < 16; z++)
        {
            // Surface height of each ribbon at this column, rippling over time
            for (int i = 0; i < AuroraLayers.Length; i++)
            {
                var l = AuroraLayers[i];
                surf[i] = l.Base
                        + l.Amp  * MathF.Sin(x * l.Fx + l.Ph + t * l.Spd)
                                 * MathF.Cos(z * l.Fz + l.Ph * 0.5f - t * l.Spd * 0.6f)
                        + 0.6f   * MathF.Sin(t * 0.4f + l.Ph);               // slow vertical bob
                hue[i]  = l.H + 0.04f * MathF.Sin(x * 0.3f + z * 0.3f + t * 0.5f);
            }

            for (int y = 0; y < 16; y++)
            {
                float r = 0, g = 0, b = 0;
                for (int i = 0; i < AuroraLayers.Length; i++)
                {
                    float dist = MathF.Abs(y - surf[i]);
                    if (dist >= thickness) continue;
                    var (cr, cg, cb) = Hsv(hue[i], 0.85f, (thickness - dist) / thickness);
                    r = MathF.Max(r, cr); g = MathF.Max(g, cg); b = MathF.Max(b, cb);
                }
                if (y == 0) { g = MathF.Max(g, 10f / 255f); b = MathF.Max(b, 30f / 255f); } // faint ground glow

                _skia.SetLed(x, y, z, new RgbColor((byte)(r * 255), (byte)(g * 255), (byte)(b * 255)));
            }
        }
    }

    private static (float R, float G, float B) Hsv(float h, float s, float v)
    {
        h -= MathF.Floor(h);
        float r = Math.Clamp(MathF.Abs(h * 6 - 3) - 1, 0f, 1f);
        float g = Math.Clamp(2 - MathF.Abs(h * 6 - 2), 0f, 1f);
        float b = Math.Clamp(2 - MathF.Abs(h * 6 - 4), 0f, 1f);
        return (((r - 1) * s + 1) * v, ((g - 1) * s + 1) * v, ((b - 1) * s + 1) * v);
    }

    // ── VerticalBars ──────────────────────────────────────────────────────────

    private void TickVerticalBars()
    {
        var colorLow    = GetColorParam("colorLow",    new RgbColor(0, 0, 255));
        var colorHigh   = GetColorParam("colorHigh",   new RgbColor(255, 0, 0));
        var sensitivity = GetFloatParam("sensitivity", 0.8f);

        // Bass handling (all optional; defaults leave behaviour unchanged):
        //   bassBands      – how many of the lowest bands count as bass (4 ≈ 40–180 Hz)
        //   bassDamping    – 0..1, how much to shrink the bass bars' height
        //   bassColor      – [r,g,b] every bar is tinted toward this on bass hits
        //   bassColorAmount– 0..1, strength of that tint
        int   bassBands   = Math.Clamp((int)GetFloatParam("bassBands", 4f), 1, 16);
        float bassDamping = Math.Clamp(GetFloatParam("bassDamping", 0f), 0f, 1f);
        var   bassColor   = TryGetColorParam("bassColor");
        float bassAmount  = Math.Clamp(GetFloatParam("bassColorAmount", 0.8f), 0f, 1f);
        float bassPulse   = UpdateBassPulse(bassBands, GetFloatParam("bassFloor", 0.35f));

        _skia.ClearAll();

        for (int x = 0; x < 16; x++)
        {
            float gain      = x < bassBands ? 1f - bassDamping * (bassBands - x) / (float)bassBands : 1f;
            float level     = ComputeLevel(x, sensitivity) * gain;
            int   barHeight = (int)(level * 16);

            for (int y = 0; y < barHeight; y++)
            {
                var color = Lerp(colorLow, colorHigh, y / 15f);
                if (bassColor is { } bc) color = Lerp(color, bc, bassPulse * bassAmount);
                for (int z = 0; z < 16; z++)
                    _skia.SetLed(x, y, z, color);
            }
        }
    }

    // ── BassOrb ───────────────────────────────────────────────────────────────

    private void TickBassOrb()
    {
        float minR  = GetFloatParam("minRadius", 2.5f);
        float maxR  = GetFloatParam("maxRadius", 7.0f);
        float fuzz  = Math.Max(0.1f, GetFloatParam("fuzz", 2.2f));
        float hueA  = GetFloatParam("hueCore", 0.72f);   // violet
        float hueB  = GetFloatParam("hueEdge", 0.17f);   // warm orange (wraps through pink)
        float pulse = UpdateBassPulse(Math.Clamp((int)GetFloatParam("bassBands", 4f), 1, 16),
                                      GetFloatParam("bassFloor", 0.35f));
        float t     = (float)_t;

        float radius = minR + (maxR - minR) * pulse;
        const float c = 7.5f;

        for (int x = 0; x < 16; x++)
        for (int y = 0; y < 16; y++)
        for (int z = 0; z < 16; z++)
        {
            float dx = x - c, dy = y - c, dz = z - c;
            float d  = MathF.Sqrt(dx * dx + dy * dy + dz * dz);

            // Fuzz the edge: smooth shimmering noise + per-frame sparkle,
            // so the boundary flickers and breaks up as it fades out.
            float smooth  = MathF.Sin(x * 1.7f + t * 2.3f) * MathF.Sin(y * 2.1f - t * 1.9f) * MathF.Sin(z * 1.3f + t * 2.7f);
            float sparkle = Hash01(x, y, z, _tick) - 0.5f;
            float dj      = d + fuzz * (0.45f * smooth + 0.5f * sparkle);

            // Soft falloff across a band 'fuzz' wide centred on the radius
            float v = 1f - SmoothStep(radius - fuzz * 0.5f, radius + fuzz * 0.5f, dj);
            if (v <= 0.01f) { _skia.SetLed(x, y, z, RgbColor.Black); continue; }
            v *= v; // gentler glow tail

            float rel = Math.Clamp(d / Math.Max(radius, 0.5f), 0f, 1.2f);
            float hue = hueA + (hueB - hueA + (hueB < hueA ? 1f : 0f)) * Math.Min(rel, 1f)
                      + 0.03f * MathF.Sin(t * 0.3f);
            float sat = 0.9f - 0.35f * pulse * Math.Max(0f, 1f - rel); // core flashes whiter on hits
            var (r, g, b) = Hsv(hue, sat, v);
            _skia.SetLed(x, y, z, new RgbColor((byte)(r * 255), (byte)(g * 255), (byte)(b * 255)));
        }
    }

    // ── Bass helpers ──────────────────────────────────────────────────────────

    /// Returns 0..1 bass pulse: instant attack, quick decay, auto-gained against
    /// recent bass peaks so heavy-bass music doesn't sit pinned at max.
    /// bassFloor: fraction of the recent peak treated as "silence".
    private float UpdateBassPulse(int bassBands, float bassFloor)
    {
        float raw;
        if (_hasRealAudio)
        {
            float sum = 0;
            lock (_spectrumLock)
                for (int i = 0; i < bassBands; i++) sum += _spectrum[i];
            raw = sum / bassBands;
        }
        else
        {
            // No audio: fake a ~140 BPM kick so the preview still moves
            raw = MathF.Pow(Math.Max(0f, MathF.Sin((float)_t * 3.6f)), 6f) + 0.05f;
        }

        _bassPeak = Math.Max(raw, _bassPeak * 0.997f);       // ~10 s memory
        _bassPeak = Math.Max(_bassPeak, 1e-4f);
        float norm = Math.Clamp((raw / _bassPeak - bassFloor) / Math.Max(1e-3f, 1f - bassFloor), 0f, 1f);

        _bassEnv = Math.Max(norm, _bassEnv * 0.82f);          // punchy attack, ~0.2 s release
        return _bassEnv;
    }

    private static float SmoothStep(float e0, float e1, float x)
    {
        float k = Math.Clamp((x - e0) / (e1 - e0), 0f, 1f);
        return k * k * (3 - 2 * k);
    }

    private static float Hash01(int x, int y, int z, int seed)
    {
        unchecked
        {
            uint h = (uint)(x * 73856093 ^ y * 19349663 ^ z * 83492791 ^ seed * 2654435761u);
            h ^= h >> 13; h *= 0x5bd1e995; h ^= h >> 15;
            return (h & 0xFFFFFF) / (float)0x1000000;
        }
    }

    private float ComputeLevel(int x, float sensitivity)
    {
        if (_hasRealAudio)
        {
            float band;
            lock (_spectrumLock) band = _spectrum[x];
            return Math.Clamp(band * sensitivity * 50f, 0f, 1f);
        }
        return (float)Math.Clamp(
            (0.40 * Math.Sin(_t * 2.1 + x * 0.45) +
             0.28 * Math.Sin(_t * 3.7 + x * 0.90) +
             0.20 * Math.Sin(_t * 1.5 - x * 0.35) +
             0.12 * Math.Sin(_t * 5.3 + x * 1.20) + 1.0) / 2.0 * sensitivity,
            0, 1);
    }

    // ── Audio capture ─────────────────────────────────────────────────────────

    private void TryStartCapture(MMDevice device)
    {
        try
        {
            _sampleBuffer = new float[FftSize];
            _bufferPos    = 0;
#pragma warning disable CS0618
            _capture = new WasapiLoopbackCapture(device);
#pragma warning restore CS0618
            _capture.DataAvailable += OnCaptureData;
            _capture.StartRecording();
            _hasRealAudio = true;
        }
        catch
        {
            _capture?.Dispose();
            _capture      = null;
            _hasRealAudio = false;
        }
    }

    private void OnCaptureData(object? sender, WaveInEventArgs e)
    {
        if (_capture is null || _sampleBuffer is null) return;
        var fmt            = _capture.WaveFormat;
        int bytesPerSample = fmt.BitsPerSample / 8;
        int stride         = bytesPerSample * fmt.Channels;

        for (int i = 0; i + stride <= e.BytesRecorded; i += stride)
        {
            float sample = 0f;
            for (int ch = 0; ch < fmt.Channels; ch++)
            {
                int offset = i + ch * bytesPerSample;
                sample += fmt.Encoding == WaveFormatEncoding.IeeeFloat
                    ? BitConverter.ToSingle(e.Buffer, offset)
                    : BitConverter.ToInt16(e.Buffer, offset) / 32768f;
            }
            _sampleBuffer[_bufferPos++] = sample / fmt.Channels;
            if (_bufferPos >= FftSize) { RunFft(fmt.SampleRate); _bufferPos = 0; }
        }
    }

    private void RunFft(int sampleRate)
    {
        var fft = new Complex[FftSize];
        for (int i = 0; i < FftSize; i++)
        {
            float hann = 0.5f * (1f - MathF.Cos(2f * MathF.PI * i / (FftSize - 1)));
            fft[i] = new Complex { X = _sampleBuffer![i] * hann, Y = 0f };
        }
        FastFourierTransform.FFT(true, (int)Math.Log2(FftSize), fft);

        int      nyquist = FftSize / 2;
        float[]  raw     = new float[16];
        for (int band = 0; band < 16; band++)
        {
            float freqLo  = 40f * MathF.Pow(16000f / 40f,  band      / 16f);
            float freqHi  = 40f * MathF.Pow(16000f / 40f, (band + 1) / 16f);
            int   binLo   = Math.Clamp((int)(freqLo * FftSize / sampleRate), 1, nyquist - 1);
            int   binHi   = Math.Clamp((int)(freqHi * FftSize / sampleRate), binLo + 1, nyquist);
            float sum     = 0f;
            for (int b = binLo; b < binHi; b++)
                sum += MathF.Sqrt(fft[b].X * fft[b].X + fft[b].Y * fft[b].Y);
            raw[band] = sum / (binHi - binLo);
        }

        const float Attack = 0.6f, Decay = 0.10f;
        lock (_spectrumLock)
        {
            for (int b = 0; b < 16; b++)
            {
                float alpha = raw[b] > _spectrum[b] ? Attack : Decay;
                _spectrum[b] += alpha * (raw[b] - _spectrum[b]);
            }
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static RgbColor HueToRgb(float h)
    {
        float r = Math.Abs(h * 6 - 3) - 1;
        float g = 2 - Math.Abs(h * 6 - 2);
        float b = 2 - Math.Abs(h * 6 - 4);
        return new RgbColor(
            (byte)(Math.Clamp(r, 0f, 1f) * 255),
            (byte)(Math.Clamp(g, 0f, 1f) * 255),
            (byte)(Math.Clamp(b, 0f, 1f) * 255));
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

    private RgbColor? TryGetColorParam(string key) =>
        _loadout?.Params?.ContainsKey(key) == true ? GetColorParam(key, RgbColor.Black) : null;

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
