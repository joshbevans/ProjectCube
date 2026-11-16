using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Media3D;
using System.Windows.Threading;
using CubeProject.App.Rendering;
using CubeProject.App.ViewModels;
using CubeProject.Core.IO;
using CubeProject.Core.Models;

namespace CubeProject.App;

public partial class MainWindow : Window
{
    private readonly CubeRenderer _renderer;
    private readonly AudioReactiveRenderer _audioRenderer;
    private readonly DispatcherTimer _orbitTimer;

    // Cube centre in world space
    private const double Center = 7.5;

    public MainWindow()
    {
        InitializeComponent();
        _renderer = new CubeRenderer(CubeVisual);
        _audioRenderer = new AudioReactiveRenderer(_renderer);

        // Left drag = rotate, everything else off
        Viewport.RotateGesture  = new MouseGesture(MouseAction.LeftClick);
        Viewport.RotateGesture2 = new MouseGesture(MouseAction.None);
        Viewport.PanGesture     = new MouseGesture(MouseAction.None);
        Viewport.PanGesture2    = new MouseGesture(MouseAction.None);
        Viewport.IsPanEnabled   = false;

        // Slow continuous orbit — 0.25°/frame ≈ 7.5°/sec, one revolution ~48 s
        _orbitTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(33) };
        _orbitTimer.Tick += OrbitTick;
        _orbitTimer.Start();
    }

    private void OrbitTick(object? s, EventArgs e)
    {
        if (Viewport.Camera is not PerspectiveCamera cam) return;

        var pos = cam.Position;
        double dx = pos.X - Center;
        double dz = pos.Z - Center;
        double xzDist = Math.Sqrt(dx * dx + dz * dz);
        if (xzDist < 1) return; // safety guard

        // Increment azimuth, preserve elevation and XZ distance
        double angle = Math.Atan2(dz, dx) + 0.25 * Math.PI / 180.0;
        double nx = Center + xzDist * Math.Cos(angle);
        double nz = Center + xzDist * Math.Sin(angle);

        cam.Position      = new Point3D(nx, pos.Y, nz);
        cam.LookDirection = new Vector3D(Center - nx, Center - pos.Y, Center - nz);
    }

    private void OnLoadoutSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _audioRenderer.Stop();

        var loadout = e.AddedItems.Count > 0 ? e.AddedItems[0] as CubeLoadout : null;

        if (loadout is null)
        {
            _renderer.Clear();
            return;
        }

        if (loadout.Mode == LoadoutMode.Preset && loadout.Frame is not null)
            _renderer.Render(LoadoutRepository.ToFrame(loadout));
        else if (loadout.Mode == LoadoutMode.AudioReactive)
            _audioRenderer.Start(loadout, (DataContext as MainViewModel)?.SelectedAudioDevice);
        else
            _renderer.Clear();
    }

    protected override void OnClosed(EventArgs e)
    {
        _orbitTimer.Stop();
        _audioRenderer.Dispose();
        (DataContext as IDisposable)?.Dispose();
        base.OnClosed(e);
    }
}
