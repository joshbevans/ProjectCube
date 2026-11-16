using System.Text.Json;
using System.Windows.Threading;
using NAudio.CoreAudioApi;
using NAudio.Dsp;
using NAudio.Wave;
using CubeProject.Core.Models;

namespace CubeProject.App.Rendering;

public class AudioReactiveRenderer : IDisposable
{
    private readonly CubeRenderer _renderer;
    private BandedCubeRenderer? _banded;
    private DispatcherTimer? _timer;
    private CubeLoadout? _loadout;
    private double _t = 0;

    // Audio capture
    private WasapiLoopbackCapture? _capture;
    private float[]? _sampleBuffer;
    private int _bufferPos;
    private const int FftSize = 2048;
    private readonly float[] _spectrum = new float[16];
    private readonly object _spectrumLock = new();
    private bool _hasRealAudio;

    public AudioReactiveRenderer(CubeRenderer renderer) => _renderer = renderer;

    public void Start(CubeLoadout loadout, MMDevice? device = null)
    {
        Stop();
        _loadout = loadout;
        _t = 0;
        _hasRealAudio = false;

        if (device is not null) TryStartCapture(device);

        // Build banded geometry once; only colour updates happen per frame
        _banded = loadout.Effect?.ToLowerInvariant() switch
        {
            "rainbowcycle" => new BandedCubeRenderer(_renderer.Host, 46, (x, y, z) => x + y + z),
            "verticalbars" => new BandedCubeRenderer(_renderer.Host, 256, (x, y, z) => x * 16 + y),
            _              => new BandedCubeRenderer(_renderer.Host, 256, (x, y, z) => x * 16 + y),
        };

        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(33) };
        _timer.Tick += Tick;
        _timer.Start();
    }

    public void Stop()
    {
        _timer?.Stop();
        _timer = null;
        _banded?.Dispose();
        _banded = null;
        _capture?.StopRecording();
        _capture?.Dispose();
        _capture = null;
        _hasRealAudio = false;
    }

    private void Tick(object? s, EventArgs e)
    {
        _t += 0.04;
        switch (_loadout?.Effect?.ToLowerInvariant())
        {
            case "rainbowcycle": TickRainbowCycle(); break;
            default:             TickVerticalBars();  break;
        }
    }

    // ── RainbowCycle ────────────────────────────────────────────────────────
    // 46 bands keyed by x+y+z sum. Each tick: compute 46 colours, write brushes.

    private void TickRainbowCycle()
    {
        if (_banded is null) return;
        for (int sum = 0; sum <= 45; sum++)
        {
            float hue = (sum / 45f + (float)(_t * 0.5)) % 1f;
            _banded.SetBand(sum, HueToRgb(hue));
        }
    }

    // ── VerticalBars ────────────────────────────────────────────────────────
    // 256 bands keyed by x*16+y. Each is a full Z-row of 16 LEDs.

    private void TickVerticalBars()
    {
        if (_banded is null) return;

        var colorLow    = GetColorParam("colorLow",    new RgbColor(0, 0, 255));
        var colorHigh   = GetColorParam("colorHigh",   new RgbColor(255, 0, 0));
        var sensitivity = GetFloatParam("sensitivity", 0.8f);

        for (int x = 0; x < 16; x++)
        {
            float level;
            if (_hasRealAudio)
            {
                float band;
                lock (_spectrumLock) band = _spectrum[x];
                level = Math.Clamp(band * sensitivity * 50f, 0f, 1f);
            }
            else
            {
                level = (float)(
                    0.40 * Math.Sin(_t * 2.1 + x * 0.45) +
                    0.28 * Math.Sin(_t * 3.7 + x * 0.90) +
                    0.20 * Math.Sin(_t * 1.5 - x * 0.35) +
                    0.12 * Math.Sin(_t * 5.3 + x * 1.20));
                level = Math.Clamp((level + 1f) / 2f * sensitivity, 0f, 1f);
            }

            int barHeight = (int)(level * 16);
            for (int y = 0; y < 16; y++)
                _banded.SetBand(x * 16 + y, y < barHeight
                    ? Lerp(colorLow, colorHigh, y / 15f)
                    : RgbColor.Black);
        }
    }

    // ── Audio capture ────────────────────────────────────────────────────────

    private void TryStartCapture(MMDevice device)
    {
        try
        {
            _sampleBuffer = new float[FftSize];
            _bufferPos = 0;
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
            _capture = null;
            _hasRealAudio = false;
        }
    }

    private void OnCaptureData(object? sender, WaveInEventArgs e)
    {
        if (_capture is null || _sampleBuffer is null) return;
        var fmt = _capture.WaveFormat;
        int bytesPerSample = fmt.BitsPerSample / 8;
        int stride = bytesPerSample * fmt.Channels;

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
            sample /= fmt.Channels;

            _sampleBuffer[_bufferPos++] = sample;
            if (_bufferPos >= FftSize)
            {
                RunFft(fmt.SampleRate);
                _bufferPos = 0;
            }
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

        int nyquist = FftSize / 2;
        var raw = new float[16];
        for (int band = 0; band < 16; band++)
        {
            float freqLow  = 40f * MathF.Pow(16000f / 40f,  band      / 16f);
            float freqHigh = 40f * MathF.Pow(16000f / 40f, (band + 1) / 16f);
            int binLow  = Math.Clamp((int)(freqLow  * FftSize / sampleRate), 1, nyquist - 1);
            int binHigh = Math.Clamp((int)(freqHigh * FftSize / sampleRate), binLow + 1, nyquist);

            float sum = 0f;
            for (int b = binLow; b < binHigh; b++)
                sum += MathF.Sqrt(fft[b].X * fft[b].X + fft[b].Y * fft[b].Y);
            raw[band] = sum / (binHigh - binLow);
        }

        const float Attack = 0.6f, Decay = 0.10f;
        lock (_spectrumLock)
        {
            for (int b = 0; b < 16; b++)
            {
                float alpha = raw[b] > _spectrum[b] ? Attack : Decay;
                _spectrum[b] = _spectrum[b] + alpha * (raw[b] - _spectrum[b]);
            }
        }
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

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
