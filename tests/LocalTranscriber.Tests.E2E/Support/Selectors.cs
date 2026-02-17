namespace LocalTranscriber.Tests.E2E.Support;

public static class Selectors
{
    // Home page
    public const string ScreenRoot = "[data-testid='screen-root']";
    public const string RecordButton = "[data-testid='record-button']";
    public const string UploadLabel = "[data-testid='upload-label']";
    public const string TranscribeButton = "[data-testid='transcribe-button']";
    public const string SettingsPanel = "[data-testid='settings-panel']";
    public const string ResultTabRow = "[data-testid='result-tab-row']";
    public const string ResetButton = "[data-testid='reset-button']";
    public const string AdvancedSettings = "[data-testid='advanced-settings']";
    public const string PromptEditor = "[data-testid='prompt-editor']";
    public const string MicSelect = "[data-testid='mic-select']";
    public const string MirrorSelect = "[data-testid='mirror-select']";

    // Client-only
    public const string ServerLlmProviders = "[data-testid='server-llm-providers']";
    public const string SessionHistory = "[data-testid='session-history']";
    public const string Diagnostics = "[data-testid='diagnostics']";
    public const string SpeedPriorityToggle = "[data-testid='speed-priority-toggle']";
    public const string LiveTranscriptionToggle = "[data-testid='live-transcription-toggle']";

    // Workflow editor
    public const string WorkflowEditor = "[data-testid='workflow-editor']";
    public const string WorkflowHeader = "[data-testid='workflow-header']";
    public const string WorkflowSelect = "[data-testid='workflow-select']";
    public const string WorkflowDuplicate = "[data-testid='workflow-duplicate']";
    public const string WorkflowNew = "[data-testid='workflow-new']";
    public const string WorkflowTemplate = "[data-testid='workflow-template']";
    public const string WorkflowDelete = "[data-testid='workflow-delete']";
    public const string AddStepButton = "[data-testid='add-step-btn']";
    public const string ViewSimple = "[data-testid='view-simple']";
    public const string ViewPhase = "[data-testid='view-phase']";

    // Layout
    public const string StudioModeButton = "button[title='Open studio mode']";
    public const string MinimalModeButton = "button[title='Switch to minimal mode']";
    public const string StudioGrid = "section.studio-grid";
    public const string StudioCard = "article.studio-card";

    // Results
    public const string MinimalCapture = "section.minimal-capture";
    public const string MinimalProcessing = "section.minimal-processing";
    public const string MinimalResults = "section.minimal-results";

    // Workflow steps
    public const string WorkflowStep = ".workflow-step";
    public const string StepHeader = ".step-header";
    public const string StepRemoveButton = ".icon-btn.danger";
    public const string StepMoveDownButton = ".icon-btn[title='Move down']";
    public const string StepMoveUpButton = ".icon-btn[title='Move up']";
    public const string StepConfig = ".step-config";

    // Add step menu
    public const string AddStepMenu = ".add-step-menu";
    public const string StepTypeOption = ".step-type-option";

    // Preset picker
    public const string PresetPicker = ".preset-picker";
    public const string PresetOption = ".preset-option";
}
