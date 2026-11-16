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

    public MainWindow()
    {
        InitializeComponent();
        _renderer = new CubeRenderer(CubeVisual);

        // Left drag = rotate, everything else off
        Viewport.RotateGesture  = new MouseGesture(MouseAction.LeftClick);
        Viewport.RotateGesture2 = new MouseGesture(MouseAction.None);
        Viewport.PanGesture     = new MouseGesture(MouseAction.None);
        Viewport.PanGesture2    = new MouseGesture(MouseAction.None);
        Viewport.IsPanEnabled   = false;
    }

    private void OnLoadoutSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // Read from event args — ViewModel binding hasn't propagated yet when this fires
        var loadout = e.AddedItems.Count > 0 ? e.AddedItems[0] as CubeLoadout : null;

        if (loadout?.Mode == LoadoutMode.Preset && loadout.Frame is not null)
            _renderer.Render(LoadoutRepository.ToFrame(loadout));
        else
            _renderer.Clear();
    }

    protected override void OnClosed(EventArgs e)
    {
        (DataContext as IDisposable)?.Dispose();
        base.OnClosed(e);
    }
}
