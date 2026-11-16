using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CubeProject.Core.IO;
using CubeProject.Core.Models;
using NAudio.CoreAudioApi;

namespace CubeProject.App.ViewModels;

public partial class MainViewModel : ObservableObject, IDisposable
{
    private FileSystemWatcher? _watcher;

    [ObservableProperty] private ObservableCollection<CubeLoadout> _loadouts = [];
    [ObservableProperty] private CubeLoadout? _selectedLoadout;
    [ObservableProperty] private bool _loadoutPanelOpen = true;
    [ObservableProperty] private string _connectionStatus = "Not connected";
    [ObservableProperty] private bool _settingsVisible = false;
    [ObservableProperty] private ObservableCollection<MMDevice> _audioDevices = [];
    [ObservableProperty] private MMDevice? _selectedAudioDevice;

    public GridLength LoadoutPanelWidth => LoadoutPanelOpen ? new GridLength(220) : new GridLength(0);
    public Visibility LoadoutPanelVisible => LoadoutPanelOpen ? Visibility.Visible : Visibility.Collapsed;
    public Visibility SettingsOverlayVisible => SettingsVisible ? Visibility.Visible : Visibility.Collapsed;

    public string ActiveLoadoutLabel => SelectedLoadout is null
        ? "No loadout selected"
        : $"{SelectedLoadout.Name}  [{SelectedLoadout.Mode}]";

    public MainViewModel()
    {
        LoadoutsFromDisk();
        StartWatcher();
        LoadAudioDevices();
    }

    [RelayCommand]
    private async Task Refresh() => await RefreshAsync();

    [RelayCommand]
    private void ToggleLoadoutPanel()
    {
        LoadoutPanelOpen = !LoadoutPanelOpen;
        OnPropertyChanged(nameof(LoadoutPanelWidth));
        OnPropertyChanged(nameof(LoadoutPanelVisible));
    }

    [RelayCommand]
    private void OpenSettings() => SettingsVisible = true;

    [RelayCommand]
    private void CloseSettings() => SettingsVisible = false;

    [RelayCommand]
    private void SaveSettings()
    {
        AppSettings.Current.AudioDeviceId = SelectedAudioDevice?.ID;
        AppSettings.Save();
        SettingsVisible = false;
    }

    partial void OnSelectedLoadoutChanged(CubeLoadout? value) =>
        OnPropertyChanged(nameof(ActiveLoadoutLabel));

    partial void OnLoadoutPanelOpenChanged(bool value)
    {
        OnPropertyChanged(nameof(LoadoutPanelWidth));
        OnPropertyChanged(nameof(LoadoutPanelVisible));
    }

    partial void OnSettingsVisibleChanged(bool value) =>
        OnPropertyChanged(nameof(SettingsOverlayVisible));

    private void LoadAudioDevices()
    {
        using var enumerator = new MMDeviceEnumerator();
        var devices = enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active);
        AudioDevices = new ObservableCollection<MMDevice>(devices);

        var savedId = AppSettings.Current.AudioDeviceId;
        if (savedId is not null)
            SelectedAudioDevice = AudioDevices.FirstOrDefault(d => d.ID == savedId);
    }

    private void LoadoutsFromDisk()
    {
        Loadouts.Clear();
        foreach (var path in LoadoutRepository.EnumeratePaths())
        {
            var loadout = LoadoutRepository.LoadAsync(path).GetAwaiter().GetResult();
            if (loadout is not null)
                Loadouts.Add(loadout);
        }
    }

    private async Task RefreshAsync()
    {
        var previousName = SelectedLoadout?.Name;
        LoadoutsFromDisk();
        if (previousName is not null)
            SelectedLoadout = Loadouts.FirstOrDefault(l => l.Name == previousName);
    }

    private void StartWatcher()
    {
        LoadoutRepository.EnsureFolder();
        _watcher = new FileSystemWatcher(LoadoutRepository.LoadoutsFolder, "*.loadout")
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName,
            EnableRaisingEvents = true
        };
        _watcher.Changed += OnLoadoutFileChanged;
        _watcher.Created += OnLoadoutFileChanged;
        _watcher.Deleted += OnLoadoutFileChanged;
        _watcher.Renamed += OnLoadoutFileChanged;
    }

    private void OnLoadoutFileChanged(object sender, FileSystemEventArgs e) =>
        Application.Current.Dispatcher.InvokeAsync(async () => await RefreshAsync());

    public void Dispose()
    {
        _watcher?.Dispose();
        foreach (var device in AudioDevices)
            device.Dispose();
    }
}
