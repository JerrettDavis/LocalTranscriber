namespace LocalTranscriber.Web.Plugins;

public interface IWorkflowStepPlugin
{
    string StepTypeId { get; }
    string Name { get; }
    string Description { get; }
    string Icon { get; }
    string Category { get; }
    Dictionary<string, object> ConfigSchema { get; }
    Task<StepResult> ExecuteAsync(StepInput input, Dictionary<string, object> config, CancellationToken ct = default);
}
