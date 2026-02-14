using System.Reflection;

namespace LocalTranscriber.Web.Plugins;

public sealed class PluginLoader
{
    private readonly Dictionary<string, IWorkflowStepPlugin> _plugins = new(StringComparer.OrdinalIgnoreCase);
    private readonly ILogger<PluginLoader> _logger;

    public PluginLoader(ILogger<PluginLoader> logger)
    {
        _logger = logger;
    }

    public IReadOnlyDictionary<string, IWorkflowStepPlugin> Plugins => _plugins;

    public void LoadFromDirectory(string path)
    {
        if (!Directory.Exists(path))
        {
            _logger.LogInformation("Plugin directory {Path} does not exist, skipping", path);
            return;
        }

        foreach (var dll in Directory.EnumerateFiles(path, "*.dll"))
        {
            try
            {
                var assembly = Assembly.LoadFrom(dll);
                var pluginTypes = assembly.GetTypes()
                    .Where(t => typeof(IWorkflowStepPlugin).IsAssignableFrom(t) && !t.IsInterface && !t.IsAbstract);

                foreach (var type in pluginTypes)
                {
                    if (Activator.CreateInstance(type) is IWorkflowStepPlugin plugin)
                    {
                        _plugins[plugin.StepTypeId] = plugin;
                        _logger.LogInformation("Loaded plugin step type: {StepTypeId} ({Name})", plugin.StepTypeId, plugin.Name);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to load plugin assembly: {Path}", dll);
            }
        }
    }

    public async Task<StepResult> ExecuteStepAsync(string stepTypeId, StepInput input, Dictionary<string, object> config, CancellationToken ct = default)
    {
        if (!_plugins.TryGetValue(stepTypeId, out var plugin))
            return new StepResult(null, null, null, false, $"Unknown plugin step type: {stepTypeId}");

        return await plugin.ExecuteAsync(input, config, ct);
    }
}
