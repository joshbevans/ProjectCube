using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CubeProject.App.Rendering;
using CubeProject.App.ViewModels;
using CubeProject.Core.IO;
using CubeProject.Core.Models;

namespace CubeProject.App;

public partial class MainWindow : Window
{
    private readonly SkiaCubeRenderer     _skia  = new();
    private readonly AudioReactiveRenderer _audioRenderer;

    private WriteableBitmap? _wpfBitmap;
    private Point _lastMouse;
    private bool  _isDragging;

    public MainWindow()
    {
        InitializeComponent();
        _audioRenderer = new AudioReactiveRenderer(_skia, Invalidate);
    }

    // ── Rendering ──────────────────────────────────────────────────────────────

    private void Invalidate()
    {
        // Use the viewport Border's size — Image.ActualWidth is 0 until Source is set.
        int w = (int)CubeViewport.ActualWidth;
        int h = (int)CubeViewport.ActualHeight;
        if (w <= 0 || h <= 0) return;

        _skia.Render(w, h);

        if (_wpfBitmap is null || (int)_wpfBitmap.Width != w || (int)_wpfBitmap.Height != h)
        {
            _wpfBitmap = new WriteableBitmap(w, h, 96, 96, PixelFormats.Bgra32, null);
            CubeImage.Source = _wpfBitmap;
        }

        var ptr = _skia.GetPixelPtr();
        if (ptr != 0)
            _wpfBitmap.WritePixels(new Int32Rect(0, 0, w, h), ptr, _skia.RowBytes * h, _skia.RowBytes);
    }

    private void OnViewportSizeChanged(object sender, SizeChangedEventArgs e) => Invalidate();

    // ── Mouse interaction (rotate + zoom) ────────────────────────────────────

    private void OnCubeMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left) return;
        _isDragging = true;
        _lastMouse  = e.GetPosition(CubeImage);
        CubeImage.CaptureMouse();
    }

    private void OnCubeMouseMove(object sender, MouseEventArgs e)
    {
        if (!_isDragging) return;
        var pos   = e.GetPosition(CubeImage);
        var delta = pos - _lastMouse;
        _lastMouse = pos;
        _skia.Rotate((float)delta.X * 0.008f, (float)delta.Y * 0.008f);
        Invalidate();
    }

    private void OnCubeMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left) return;
        _isDragging = false;
        CubeImage.ReleaseMouseCapture();
    }

    private void OnCubeMouseWheel(object sender, MouseWheelEventArgs e)
    {
        _skia.Zoom(e.Delta > 0 ? 1.1f : (1f / 1.1f));
        Invalidate();
    }

    // ── Loadout selection ─────────────────────────────────────────────────────

    private void OnLoadoutSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _audioRenderer.Stop();

        var loadout = e.AddedItems.Count > 0 ? e.AddedItems[0] as CubeLoadout : null;

        if (loadout is null)
        {
            _skia.ClearAll();
            Invalidate();
            return;
        }

        if (loadout.Mode == LoadoutMode.Preset && loadout.Frame is not null)
        {
            _skia.RenderFrame(LoadoutRepository.ToFrame(loadout));
            Invalidate();
        }
        else if (loadout.Mode == LoadoutMode.AudioReactive)
        {
            _audioRenderer.Start(loadout, (DataContext as MainViewModel)?.SelectedAudioDevice);
        }
        else
        {
            _skia.ClearAll();
            Invalidate();
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        _audioRenderer.Dispose();
        _skia.Dispose();
        (DataContext as IDisposable)?.Dispose();
        base.OnClosed(e);
    }
}
