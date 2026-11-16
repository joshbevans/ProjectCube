using SkiaSharp;
using CubeProject.Core.Models;

namespace CubeProject.App.Rendering;

/// <summary>
/// Renders the 16³ cube as glowing LED dots using SkiaSharp software rasterisation.
/// Caller owns a WriteableBitmap; call Render(w,h), then copy pixels via GetPixelPtr().
/// Two-pass bloom: halo circles → Gaussian blur → additive composite → sharp cores.
/// Zero per-frame heap allocation (scratch buffers pre-allocated and reused).
/// </summary>
public sealed class SkiaCubeRenderer : IDisposable
{
    private const int LedCount = 16 * 16 * 16;

    private readonly SKColor[] _colors = new SKColor[LedCount];

    private float _yaw   = MathF.PI / 4f;  // 45° corner view
    private float _pitch = 0.49f;           // ~28° elevated
    private float _zoom  = 1.0f;

    // Main render surface (Bgra8888 matches WPF PixelFormats.Bgra32)
    private SKBitmap? _renderBmp;
    private SKCanvas? _renderCvs;

    // Offscreen glow surface
    private SKBitmap? _glowBmp;
    private SKCanvas? _glowCvs;

    private int _renderW, _renderH;

    // Per-frame projection scratch — zero alloc per frame
    private readonly (float sx, float sy, float rz, int idx)[] _buf =
        new (float, float, float, int)[LedCount];

    // ── Public API ────────────────────────────────────────────────────────────

    public void SetLed(int x, int y, int z, RgbColor c)
        => _colors[x + y * 16 + z * 256] = new SKColor(c.R, c.G, c.B);

    public void ClearAll() => Array.Fill(_colors, SKColors.Black);

    public void RenderFrame(CubeFrame frame)
    {
        for (int x = 0; x < 16; x++)
        for (int y = 0; y < 16; y++)
        for (int z = 0; z < 16; z++)
        {
            var c = frame[x, y, z];
            _colors[x + y * 16 + z * 256] = new SKColor(c.R, c.G, c.B);
        }
    }

    public void Rotate(float dYaw, float dPitch)
    {
        _yaw   = (_yaw + dYaw) % (2f * MathF.PI);
        _pitch = Math.Clamp(_pitch + dPitch, -1.52f, 1.52f);
    }

    public void Zoom(float factor) =>
        _zoom = Math.Clamp(_zoom * factor, 0.3f, 5f);

    /// <summary>
    /// Renders the frame into an internal Bgra8888 bitmap. Call GetPixelPtr()
    /// after this to copy pixels into a WPF WriteableBitmap.
    /// </summary>
    public void Render(int width, int height)
    {
        if (width <= 0 || height <= 0) return;
        if (_renderBmp is null || _renderW != width || _renderH != height)
            AllocateBuffers(width, height);
        Draw(_renderCvs!, width, height);
    }

    /// <summary>Pointer to rendered pixel data (valid until next Render call).</summary>
    public nint GetPixelPtr() => _renderBmp?.GetPixels() ?? 0;

    /// <summary>Row stride in bytes of the rendered bitmap.</summary>
    public int RowBytes => _renderBmp?.RowBytes ?? 0;

    // ── Internal rendering ────────────────────────────────────────────────────

    private void AllocateBuffers(int w, int h)
    {
        _renderCvs?.Dispose(); _renderBmp?.Dispose();
        _glowCvs?.Dispose();   _glowBmp?.Dispose();

        _renderBmp = new SKBitmap(w, h, SKColorType.Bgra8888, SKAlphaType.Premul);
        _renderCvs = new SKCanvas(_renderBmp);
        _glowBmp   = new SKBitmap(w, h, SKColorType.Rgba8888, SKAlphaType.Premul);
        _glowCvs   = new SKCanvas(_glowBmp);
        _renderW   = w;
        _renderH   = h;
    }

    private void Draw(SKCanvas canvas, int width, int height)
    {
        canvas.Clear(new SKColor(5, 5, 8));
        _glowCvs!.Clear(SKColors.Transparent);

        float cosy = MathF.Cos(_yaw),   siny = MathF.Sin(_yaw);
        float cosp = MathF.Cos(_pitch), sinp = MathF.Sin(_pitch);
        float camD   = 22f / _zoom;
        float scale0 = MathF.Min(width, height) * 0.55f / 15f;
        float cx     = width  * 0.5f;
        float cy     = height * 0.5f;

        // Project all lit LEDs
        int count = 0;
        for (int x = 0; x < 16; x++)
        for (int y = 0; y < 16; y++)
        for (int z = 0; z < 16; z++)
        {
            int i = x + y * 16 + z * 256;
            if (_colors[i] == SKColors.Black) continue;

            float wx = x - 7.5f, wy = y - 7.5f, wz = z - 7.5f;
            float rx  =  wx * cosy + wz * siny; // yaw
            float ry  =  wy;
            float rz  = -wx * siny + wz * cosy;
            float rx2 = rx;                      // pitch
            float ry2 = ry * cosp - rz * sinp;
            float rz2 = ry * sinp + rz * cosp;

            // Positive rz2 = closer to camera; perspective: scale = camD/(camD-rz2)
            float persp = camD / (camD - rz2);
            _buf[count++] = (
                cx + rx2 * persp * scale0,
                cy - ry2 * persp * scale0,
                rz2, i);
        }

        // Sort back-to-front (ascending rz2 = farthest first)
        _buf.AsSpan(0, count).Sort((a, b) => a.rz.CompareTo(b.rz));

        // ── Pass 1: soft halos → glow buffer (no AA — blurred anyway) ─────────
        using var haloPaint = new SKPaint { IsAntialias = false };
        for (int k = 0; k < count; k++)
        {
            var (sx, sy, rz2, idx) = _buf[k];
            float persp = camD / (camD - rz2);
            float haloR = persp * scale0 * 0.30f;
            var   col   = _colors[idx];
            haloPaint.Color = new SKColor(col.Red, col.Green, col.Blue, 60);
            _glowCvs!.DrawCircle(sx, sy, haloR, haloPaint);
        }

        // Composite blurred glow (additive — colours mix and amplify)
        using var glowPaint = new SKPaint
        {
            ImageFilter = SKImageFilter.CreateBlur(9, 9),
            BlendMode   = SKBlendMode.Plus,
        };
        canvas.DrawBitmap(_glowBmp, SKPoint.Empty, glowPaint);

        // ── Pass 2: sharp antialiased LED cores ───────────────────────────────
        using var dotPaint = new SKPaint { IsAntialias = true };
        for (int k = 0; k < count; k++)
        {
            var (sx, sy, rz2, idx) = _buf[k];
            float persp = camD / (camD - rz2);
            float dotR  = persp * scale0 * 0.13f;
            var   col   = _colors[idx];

            dotPaint.Color = col;
            canvas.DrawCircle(sx, sy, dotR, dotPaint);
            dotPaint.Color = SKColors.White.WithAlpha(110);
            canvas.DrawCircle(sx, sy, dotR * 0.38f, dotPaint);
        }
    }

    public void Dispose()
    {
        _renderCvs?.Dispose(); _renderBmp?.Dispose();
        _glowCvs?.Dispose();   _glowBmp?.Dispose();
    }
}
