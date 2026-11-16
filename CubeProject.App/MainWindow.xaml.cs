using System.Windows;
using System.Windows.Controls;
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
    }

    private void OnLoadoutSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DataContext is not MainViewModel vm || vm.SelectedLoadout is not CubeLoadout loadout)
        {
            _renderer.Clear();
            return;
        }

        if (loadout.Mode == LoadoutMode.Preset && loadout.Frame is not null)
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
