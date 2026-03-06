using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LocalAIWriter.Core.Services;
using Microsoft.Extensions.Logging;

namespace LocalAIWriter.ViewModels;

/// <summary>ViewModel for managing installed plugins.</summary>
public sealed partial class PluginManagerViewModel : ObservableObject
{
    private readonly PluginManager _pluginManager;
    private readonly ILogger<PluginManagerViewModel> _logger;

    [ObservableProperty]
    private ObservableCollection<PluginItemViewModel> _plugins = new();

    public PluginManagerViewModel(PluginManager pluginManager, ILogger<PluginManagerViewModel> logger)
    {
        _pluginManager = pluginManager;
        _logger = logger;
        RefreshPlugins();
    }

    [RelayCommand]
    private void RefreshPlugins()
    {
        Plugins.Clear();
        foreach (var plugin in _pluginManager.Plugins)
        {
            Plugins.Add(new PluginItemViewModel
            {
                Id = plugin.Plugin.Id,
                Name = plugin.Plugin.Name,
                Version = plugin.Plugin.Version,
                IsEnabled = plugin.IsEnabled,
                Capabilities = plugin.Plugin.Capabilities.ToString()
            });
        }
    }

    [RelayCommand]
    private void TogglePlugin(string pluginId)
    {
        var plugin = Plugins.FirstOrDefault(p => p.Id == pluginId);
        if (plugin != null)
        {
            plugin.IsEnabled = !plugin.IsEnabled;
            _pluginManager.SetPluginEnabled(pluginId, plugin.IsEnabled);
        }
    }
}

public sealed partial class PluginItemViewModel : ObservableObject
{
    [ObservableProperty] private string _id = "";
    [ObservableProperty] private string _name = "";
    [ObservableProperty] private string _version = "";
    [ObservableProperty] private bool _isEnabled;
    [ObservableProperty] private string _capabilities = "";
}
