using System.Windows;
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

        if (DataContext is MainViewModel vm)
            vm.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(MainViewModel.SelectedLoadout))
                    OnLoadoutChanged(vm.SelectedLoadout);
            };
    }

    private void OnLoadoutChanged(CubeLoadout? loadout)
    {
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
