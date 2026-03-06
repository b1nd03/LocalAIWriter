using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using LocalAIWriter.Core.Memory;
using LocalAIWriter.Core.Services;
using LocalAIWriter.Services;
using LocalAIWriter.ViewModels;

namespace LocalAIWriter;

public partial class App : Application
{
    private ServiceProvider? _serviceProvider;

    /// <summary>Exposes the DI container globally.</summary>
    public static ServiceProvider? Services { get; private set; }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        try
        {
            var services = new ServiceCollection();
            ConfigureServices(services);
            _serviceProvider = services.BuildServiceProvider();
            Services = _serviceProvider;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Startup error:\n{ex.Message}", "LocalAI Writer",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        // Dispose Ollama service
        try { _serviceProvider?.GetService<OllamaService>()?.Dispose(); } catch { }
        _serviceProvider?.Dispose();
        base.OnExit(e);
    }

    private static void ConfigureServices(IServiceCollection services)
    {
        services.AddLogging(builder => builder.SetMinimumLevel(LogLevel.Debug));

        // Core services
        services.AddSingleton<RuleBasedEngine>();
        services.AddSingleton<OllamaService>();
        services.AddSingleton<TextProcessor>();

        // ML pipeline (ModelRouter now includes OllamaService)
        services.AddSingleton<InferenceMemoryManager>();
        services.AddSingleton<Tokenizer>();
        services.AddSingleton<ModelManager>();
        services.AddSingleton<ModelRouter>();
        services.AddSingleton<NlpPipeline>();
        services.AddSingleton<AdaptiveLearningEngine>();
        services.AddSingleton<PluginManager>();

        services.AddSingleton<GlobalHookService>();
        services.AddSingleton<TextInterceptor>();
        services.AddSingleton<CaretPositionTracker>();
        services.AddSingleton<ModelInferenceService>();
        services.AddSingleton<NotificationService>();
        services.AddSingleton<ThemeService>();
        services.AddSingleton<ContextAwarenessService>();
        services.AddSingleton<ResilienceGuardian>();
        services.AddSingleton<AccessibilityService>();

        services.AddSingleton<MainViewModel>();
        services.AddTransient<SettingsViewModel>();
        services.AddTransient<OnboardingViewModel>();
        services.AddTransient<PluginManagerViewModel>();
    }
}
