using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Media3D;
using CubeProject.App.Rendering;
using CubeProject.App.ViewModels;
using CubeProject.Core.IO;
using CubeProject.Core.Models;

namespace CubeProject.App;

public partial class MainWindow : Window
{
    private readonly CubeRenderer _renderer;
    private readonly AudioReactiveRenderer _audioRenderer;

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
            _audioRenderer.Start(loadout);
        else
            _renderer.Clear();
    }

    protected override void OnClosed(EventArgs e)
    {
        _audioRenderer.Dispose();
        (DataContext as IDisposable)?.Dispose();
        base.OnClosed(e);
    }
}
