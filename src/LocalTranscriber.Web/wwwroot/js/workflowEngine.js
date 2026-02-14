/**
 * LocalTranscriber Workflow Engine v2
 * Executes customizable transcription/processing pipelines
 * Supports phases, branching, looping, structured data flow, plugins, and presets
 */

window.localTranscriberWorkflow = (() => {
  const WORKFLOW_STORAGE_KEY = "localTranscriber_workflows";
  const ACTIVE_WORKFLOW_KEY = "localTranscriber_activeWorkflow";
  const PLUGIN_STORAGE_KEY = "localTranscriber_plugins";
  const MAX_PHASE_EXECUTIONS = 50;

  // ═══════════════════════════════════════════════════════════════
  // Utility: Nested Value Access
  // ═══════════════════════════════════════════════════════════════

  function getNestedValue(obj, path) {
    if (!path || !obj) return undefined;
    const parts = path.split(".");
    let current = obj;
    for (const part of parts) {
      if (current == null) return undefined;
      current = current[part];
    }
    return current;
  }

  function setNestedValue(obj, path, value) {
    if (!path || !obj) return;
    const parts = path.split(".");
    let current = obj;
    for (let i = 0; i < parts.length - 1; i++) {
      if (current[parts[i]] == null) current[parts[i]] = {};
      current = current[parts[i]];
    }
    current[parts[parts.length - 1]] = value;
  }

  // ═══════════════════════════════════════════════════════════════
  // Template Resolution
  // ═══════════════════════════════════════════════════════════════

  function resolveTemplate(template, context) {
    if (!template || typeof template !== "string") return template;
    return template.replace(/\{([^}]+)\}/g, (match, path) => {
      // Try context paths: context.text.processed, variables.topic, etc.
      const value = getNestedValue(context, path);
      if (value !== undefined) return String(value);
      // Try top-level context shorthand
      const shorthand = getNestedValue(context, `variables.${path}`);
      if (shorthand !== undefined) return String(shorthand);
      return match; // Leave unresolved placeholders as-is
    });
  }

  // ═══════════════════════════════════════════════════════════════
  // Step Type Definitions (Built-in)
  // ═══════════════════════════════════════════════════════════════

  const stepTypes = {
    transcribe: {
      id: "transcribe",
      name: "Transcribe Audio",
      description: "Convert audio to text using Whisper",
      icon: "🎤",
      category: "input",
      configSchema: {
        model: { type: "select", label: "Model", options: ["TinyEn", "SmallEn", "MediumEn", "LargeV3", "LargeV3Turbo"], default: "SmallEn" },
        language: { type: "select", label: "Language", options: ["auto", "en", "es", "fr", "de", "ja", "zh"], default: "auto" },
      },
      inputs: ["audio"],
      outputs: ["rawText", "segments"],
    },

    speakerLabels: {
      id: "speakerLabels",
      name: "Speaker Labels",
      description: "Detect and label different speakers",
      icon: "👥",
      category: "process",
      configSchema: {
        sensitivity: { type: "range", label: "Sensitivity", min: 0, max: 100, default: 50 },
        maxSpeakers: { type: "number", label: "Max Speakers", min: 1, max: 10, default: 6 },
      },
      inputs: ["rawText", "segments"],
      outputs: ["labeledText", "speakerCount"],
    },

    llmFormat: {
      id: "llmFormat",
      name: "LLM Formatter",
      description: "Clean and format transcript with LLM",
      icon: "✨",
      category: "process",
      configSchema: {
        model: { type: "select", label: "Model", options: ["Llama-3.1-8B-Instruct-q4f16_1-MLC", "Qwen2.5-7B-Instruct-q4f16_1-MLC", "Phi-3.5-mini-instruct-q4f16_1-MLC"], default: "Llama-3.1-8B-Instruct-q4f16_1-MLC" },
        systemPrompt: { type: "textarea", label: "System Prompt", default: "You are a transcription editor." },
        userPrompt: { type: "textarea", label: "Processing Prompt", default: "Clean and format this transcript into well-structured Markdown." },
        temperature: { type: "range", label: "Temperature", min: 0, max: 1, step: 0.1, default: 0.2 },
      },
      inputs: ["text"],
      outputs: ["processedText"],
    },

    llmTransform: {
      id: "llmTransform",
      name: "LLM Transform",
      description: "Custom LLM processing step",
      icon: "🔄",
      category: "process",
      configSchema: {
        model: { type: "select", label: "Model", options: ["Llama-3.1-8B-Instruct-q4f16_1-MLC", "Qwen2.5-7B-Instruct-q4f16_1-MLC", "Phi-3.5-mini-instruct-q4f16_1-MLC"], default: "Llama-3.1-8B-Instruct-q4f16_1-MLC" },
        systemPrompt: { type: "textarea", label: "System Prompt", default: "You are a helpful assistant." },
        userPrompt: { type: "textarea", label: "Prompt Template", default: "Process the following text:\n\n{input}" },
        temperature: { type: "range", label: "Temperature", min: 0, max: 1, step: 0.1, default: 0.3 },
      },
      inputs: ["text"],
      outputs: ["processedText"],
    },

    summarize: {
      id: "summarize",
      name: "Summarize",
      description: "Generate a summary of the content",
      icon: "📝",
      category: "process",
      configSchema: {
        model: { type: "select", label: "Model", options: ["Llama-3.1-8B-Instruct-q4f16_1-MLC", "Qwen2.5-7B-Instruct-q4f16_1-MLC"], default: "Llama-3.1-8B-Instruct-q4f16_1-MLC" },
        style: { type: "select", label: "Style", options: ["bullets", "paragraph", "executive"], default: "bullets" },
        maxLength: { type: "number", label: "Max Length (words)", min: 50, max: 1000, default: 200 },
      },
      inputs: ["text"],
      outputs: ["summary"],
    },

    extractActions: {
      id: "extractActions",
      name: "Extract Action Items",
      description: "Pull out actionable tasks and to-dos",
      icon: "✅",
      category: "process",
      configSchema: {
        model: { type: "select", label: "Model", options: ["Llama-3.1-8B-Instruct-q4f16_1-MLC", "Qwen2.5-7B-Instruct-q4f16_1-MLC"], default: "Llama-3.1-8B-Instruct-q4f16_1-MLC" },
        format: { type: "select", label: "Output Format", options: ["markdown", "json", "checklist"], default: "checklist" },
      },
      inputs: ["text"],
      outputs: ["actionItems"],
    },

    convertFormat: {
      id: "convertFormat",
      name: "Convert to Format",
      description: "Transform content into a specific format",
      icon: "📄",
      category: "output",
      configSchema: {
        model: { type: "select", label: "Model", options: ["Llama-3.1-8B-Instruct-q4f16_1-MLC", "Qwen2.5-7B-Instruct-q4f16_1-MLC"], default: "Llama-3.1-8B-Instruct-q4f16_1-MLC" },
        targetFormat: { type: "select", label: "Target Format", options: ["business-report", "meeting-notes", "blog-post", "email", "documentation", "custom"], default: "meeting-notes" },
        customTemplate: { type: "textarea", label: "Custom Template (if custom)", default: "" },
      },
      inputs: ["text"],
      outputs: ["formattedOutput"],
    },

    merge: {
      id: "merge",
      name: "Merge Outputs",
      description: "Combine multiple text outputs into one",
      icon: "🔗",
      category: "utility",
      configSchema: {
        separator: { type: "text", label: "Separator", default: "\n\n---\n\n" },
        template: { type: "textarea", label: "Merge Template", default: "## Section 1\n{input1}\n\n## Section 2\n{input2}" },
      },
      inputs: ["text1", "text2"],
      outputs: ["mergedText"],
    },

    userReview: {
      id: "userReview",
      name: "User Review & Edit",
      description: "Pause for human review with playback sync and live editing",
      icon: "👁️",
      category: "interactive",
      configSchema: {
        title: { type: "text", label: "Review Title", default: "Review Transcript" },
        instructions: { type: "textarea", label: "Instructions for Reviewer", default: "Review the transcript below. Use the playback tool to verify accuracy, then make any needed corrections." },
        showPlayback: { type: "select", label: "Show Playback Controls", options: ["yes", "no"], default: "yes" },
        showHighlighting: { type: "select", label: "Highlight Sync", options: ["yes", "no"], default: "yes" },
        requireApproval: { type: "select", label: "Require Approval", options: ["yes", "no"], default: "yes" },
      },
      inputs: ["text", "segments", "audio"],
      outputs: ["editedText", "approved"],
      isInteractive: true,
    },

    checkpoint: {
      id: "checkpoint",
      name: "Checkpoint",
      description: "Save intermediate output for comparison or rollback",
      icon: "💾",
      category: "utility",
      configSchema: {
        label: { type: "text", label: "Checkpoint Label", default: "Checkpoint" },
        includeInOutput: { type: "select", label: "Include in Final Output", options: ["yes", "no"], default: "no" },
      },
      inputs: ["text"],
      outputs: ["text"],
    },

    compare: {
      id: "compare",
      name: "Compare Versions",
      description: "Show diff between two text versions for review",
      icon: "🔀",
      category: "interactive",
      configSchema: {
        label1: { type: "text", label: "Version 1 Label", default: "Original" },
        label2: { type: "text", label: "Version 2 Label", default: "Processed" },
        showDiff: { type: "select", label: "Show Diff View", options: ["side-by-side", "inline", "unified"], default: "side-by-side" },
      },
      inputs: ["text1", "text2"],
      outputs: ["selectedText"],
      isInteractive: true,
    },

    // Phase 2: New step types

    agentGenerate: {
      id: "agentGenerate",
      name: "Agent Generate",
      description: "LLM generates structured output (questions, outlines, etc.)",
      icon: "🤖",
      category: "agent",
      configSchema: {
        model: { type: "select", label: "Model", options: ["Llama-3.1-8B-Instruct-q4f16_1-MLC", "Qwen2.5-7B-Instruct-q4f16_1-MLC", "Phi-3.5-mini-instruct-q4f16_1-MLC"], default: "Llama-3.1-8B-Instruct-q4f16_1-MLC" },
        systemPrompt: { type: "textarea", label: "System Prompt", default: "You are a helpful assistant that outputs structured data." },
        userPromptTemplate: { type: "textarea", label: "User Prompt (use {variable} placeholders)", default: "Generate a list of interview questions about: {variables.topic}" },
        outputFormat: { type: "select", label: "Output Format", options: ["text", "json", "json-array"], default: "text" },
        outputVariable: { type: "text", label: "Store Result In Variable", default: "generatedOutput" },
        temperature: { type: "range", label: "Temperature", min: 0, max: 1, step: 0.1, default: 0.4 },
      },
      inputs: ["text"],
      outputs: ["processedText"],
    },

    regexTransform: {
      id: "regexTransform",
      name: "Regex Transform",
      description: "Apply regex find/replace patterns to text",
      icon: "🔧",
      category: "process",
      configSchema: {
        patterns: { type: "textarea", label: "Patterns (one per line: /pattern/flags => replacement)", default: "/\\s+/g => \n/^\\s*$/gm =>" },
        inputFrom: { type: "text", label: "Read Input From (context path)", default: "" },
        outputTo: { type: "text", label: "Write Output To (context path)", default: "" },
      },
      inputs: ["text"],
      outputs: ["processedText"],
    },

    jsonExtract: {
      id: "jsonExtract",
      name: "JSON Extract",
      description: "Parse JSON from text and extract specific fields",
      icon: "📦",
      category: "utility",
      configSchema: {
        extractionMode: { type: "select", label: "Extraction Mode", options: ["auto-detect", "full-input"], default: "auto-detect" },
        jsonPath: { type: "text", label: "JSON Path (e.g. data.items)", default: "" },
        outputVariable: { type: "text", label: "Store In Variable", default: "extractedData" },
        fallbackValue: { type: "text", label: "Fallback Value", default: "" },
      },
      inputs: ["text"],
      outputs: ["processedText"],
    },

    templateFormat: {
      id: "templateFormat",
      name: "Template Format",
      description: "Format data using variable-substituted templates",
      icon: "📋",
      category: "utility",
      configSchema: {
        template: { type: "textarea", label: "Template (use {variable} placeholders)", default: "# {variables.title}\n\n{variables.content}" },
        outputTo: { type: "text", label: "Write Output To (context path)", default: "" },
      },
      inputs: ["text"],
      outputs: ["processedText"],
    },

    // Phase 3: Interactive step types

    multiRecord: {
      id: "multiRecord",
      name: "Multi-Record",
      description: "Record multiple audio answers to a series of prompts",
      icon: "🎙️",
      category: "interactive",
      configSchema: {
        promptsVariable: { type: "text", label: "Prompts Variable (context path to array)", default: "variables.questions" },
        instructions: { type: "textarea", label: "Instructions", default: "Record your answer for each question below." },
        transcribeModel: { type: "select", label: "Transcribe Model", options: ["TinyEn", "SmallEn", "MediumEn"], default: "SmallEn" },
        outputVariable: { type: "text", label: "Store Answers In Variable", default: "answers" },
        outputFormat: { type: "select", label: "Output Format", options: ["text-array", "qa-pairs"], default: "qa-pairs" },
      },
      inputs: ["text"],
      outputs: ["processedText"],
      isInteractive: true,
    },

    userChoice: {
      id: "userChoice",
      name: "User Choice",
      description: "Present branching options to the user within a phase",
      icon: "🔀",
      category: "interactive",
      configSchema: {
        title: { type: "text", label: "Choice Title", default: "What would you like to do next?" },
        choicesJson: { type: "textarea", label: "Choices (JSON array: [{label, nextPhase}])", default: '[{"label":"Continue","nextPhase":"next"},{"label":"Go Back","nextPhase":"previous"}]' },
      },
      inputs: [],
      outputs: ["selectedChoice"],
      isInteractive: true,
    },
  };

  // Plugin step types registry
  const pluginStepTypes = {};

  // ═══════════════════════════════════════════════════════════════
  // Default Workflow (matches current behavior)
  // ═══════════════════════════════════════════════════════════════

  const defaultWorkflow = {
    id: "default",
    name: "Standard Transcription",
    description: "Transcribe → Speaker Labels → LLM Format",
    steps: [
      {
        id: "step-1",
        type: "transcribe",
        name: "Transcribe",
        config: { model: "SmallEn", language: "auto" },
        enabled: true,
      },
      {
        id: "step-2",
        type: "speakerLabels",
        name: "Speaker Labels",
        config: { sensitivity: 50, maxSpeakers: 6 },
        enabled: true,
      },
      {
        id: "step-3",
        type: "llmFormat",
        name: "Format with LLM",
        config: {
          model: "Llama-3.1-8B-Instruct-q4f16_1-MLC",
          systemPrompt: "You are a transcription editor.",
          userPrompt: "Clean and format this transcript into well-structured Markdown with a summary and action items.",
          temperature: 0.2,
        },
        enabled: true,
      },
    ],
  };

  // ═══════════════════════════════════════════════════════════════
  // V2 Schema Migration
  // ═══════════════════════════════════════════════════════════════

  function migrateV1ToV2(workflow) {
    if (workflow.version === 2) return workflow;

    return {
      ...workflow,
      version: 2,
      variables: workflow.variables || {},
      phases: [
        {
          id: `phase-main`,
          name: "Main",
          description: "All workflow steps",
          steps: (workflow.steps || []).map(s => ({ ...s, target: s.target || "browser" })),
          transitions: [],
        },
      ],
      // Keep steps for backward compat with v1 consumers
      steps: workflow.steps || [],
    };
  }

  function ensureV2(workflow) {
    if (!workflow) return workflow;
    if (workflow.version === 2) {
      // Ensure target field on all steps
      for (const phase of (workflow.phases || [])) {
        for (const step of (phase.steps || [])) {
          if (!step.target) step.target = "browser";
        }
      }
      return workflow;
    }
    return migrateV1ToV2(workflow);
  }

  // ═══════════════════════════════════════════════════════════════
  // Workflow Management
  // ═══════════════════════════════════════════════════════════════

  function getWorkflows() {
    let workflows = [];
    try {
      const stored = localStorage.getItem(WORKFLOW_STORAGE_KEY);
      if (stored) {
        workflows = JSON.parse(stored);
      }
    } catch (e) {
      console.warn("[Workflow] Failed to load workflows:", e);
    }

    // Ensure default workflow exists
    if (!workflows.find(w => w.id === "default")) {
      workflows.unshift({ ...defaultWorkflow });
    }

    // Ensure all preset workflows are available
    for (const preset of presetWorkflows) {
      if (!workflows.find(w => w.id === preset.id)) {
        workflows.push(JSON.parse(JSON.stringify(preset)));
      }
    }

    return workflows;
  }

  function saveWorkflows(workflows) {
    try {
      localStorage.setItem(WORKFLOW_STORAGE_KEY, JSON.stringify(workflows));
      return true;
    } catch (e) {
      console.error("[Workflow] Failed to save workflows:", e);
      return false;
    }
  }

  function getActiveWorkflowId() {
    return localStorage.getItem(ACTIVE_WORKFLOW_KEY) || "default";
  }

  function setActiveWorkflow(workflowId) {
    localStorage.setItem(ACTIVE_WORKFLOW_KEY, workflowId);
  }

  function getActiveWorkflow() {
    const workflows = getWorkflows();
    const activeId = getActiveWorkflowId();
    return workflows.find(w => w.id === activeId) || workflows[0] || { ...defaultWorkflow };
  }

  function createWorkflow(name, description = "") {
    const workflows = getWorkflows();
    const newWorkflow = {
      id: `workflow-${Date.now()}`,
      name,
      description,
      steps: [],
    };
    workflows.push(newWorkflow);
    saveWorkflows(workflows);
    return newWorkflow;
  }

  function duplicateWorkflow(workflowId, newName) {
    const workflows = getWorkflows();
    const source = workflows.find(w => w.id === workflowId);
    if (!source) return null;

    const newWorkflow = {
      ...JSON.parse(JSON.stringify(source)),
      id: `workflow-${Date.now()}`,
      name: newName || `${source.name} (Copy)`,
    };
    // Regenerate step IDs
    if (newWorkflow.steps) {
      newWorkflow.steps = newWorkflow.steps.map((s, i) => ({
        ...s,
        id: `step-${Date.now()}-${i}`,
      }));
    }
    // Regenerate phase/step IDs for v2
    if (newWorkflow.phases) {
      newWorkflow.phases = newWorkflow.phases.map((p, pi) => ({
        ...p,
        id: `phase-${Date.now()}-${pi}`,
        steps: (p.steps || []).map((s, si) => ({
          ...s,
          id: `step-${Date.now()}-${pi}-${si}`,
        })),
      }));
    }

    workflows.push(newWorkflow);
    saveWorkflows(workflows);
    return newWorkflow;
  }

  function updateWorkflow(workflowId, updates) {
    const workflows = getWorkflows();
    const idx = workflows.findIndex(w => w.id === workflowId);
    if (idx === -1) return null;

    workflows[idx] = { ...workflows[idx], ...updates };
    saveWorkflows(workflows);
    return workflows[idx];
  }

  function deleteWorkflow(workflowId) {
    if (workflowId === "default") return false; // Can't delete default
    const workflows = getWorkflows();
    const filtered = workflows.filter(w => w.id !== workflowId);
    saveWorkflows(filtered);
    if (getActiveWorkflowId() === workflowId) {
      setActiveWorkflow("default");
    }
    return true;
  }

  // ═══════════════════════════════════════════════════════════════
  // Step Management (v1 compat -- operates on flat steps array)
  // ═══════════════════════════════════════════════════════════════

  function addStep(workflowId, stepType, config = {}, insertIndex = -1) {
    const workflows = getWorkflows();
    const workflow = workflows.find(w => w.id === workflowId);
    if (!workflow) return null;

    const allTypes = getAllStepTypes();
    const typeDef = allTypes[stepType];
    if (!typeDef) return null;

    // Build default config from schema
    const defaultConfig = {};
    for (const [key, schema] of Object.entries(typeDef.configSchema || {})) {
      defaultConfig[key] = schema.default;
    }

    const newStep = {
      id: `step-${Date.now()}`,
      type: stepType,
      name: typeDef.name,
      config: { ...defaultConfig, ...config },
      enabled: true,
      target: "browser",
    };

    // Ensure steps array exists
    if (!workflow.steps) workflow.steps = [];

    if (insertIndex >= 0 && insertIndex < workflow.steps.length) {
      workflow.steps.splice(insertIndex, 0, newStep);
    } else {
      workflow.steps.push(newStep);
    }

    saveWorkflows(workflows);
    return newStep;
  }

  function updateStep(workflowId, stepId, updates) {
    const workflows = getWorkflows();
    const workflow = workflows.find(w => w.id === workflowId);
    if (!workflow) return null;

    // Search in flat steps
    let step = workflow.steps?.find(s => s.id === stepId);

    // Also search in phases
    if (!step && workflow.phases) {
      for (const phase of workflow.phases) {
        step = phase.steps?.find(s => s.id === stepId);
        if (step) break;
      }
    }

    if (!step) return null;

    Object.assign(step, updates);
    if (updates.config) {
      step.config = { ...step.config, ...updates.config };
    }
    saveWorkflows(workflows);
    return step;
  }

  function removeStep(workflowId, stepId) {
    const workflows = getWorkflows();
    const workflow = workflows.find(w => w.id === workflowId);
    if (!workflow) return false;

    if (workflow.steps) {
      workflow.steps = workflow.steps.filter(s => s.id !== stepId);
    }
    if (workflow.phases) {
      for (const phase of workflow.phases) {
        if (phase.steps) {
          phase.steps = phase.steps.filter(s => s.id !== stepId);
        }
      }
    }
    saveWorkflows(workflows);
    return true;
  }

  function reorderSteps(workflowId, stepIds) {
    const workflows = getWorkflows();
    const workflow = workflows.find(w => w.id === workflowId);
    if (!workflow) return false;

    if (workflow.steps) {
      const stepMap = new Map(workflow.steps.map(s => [s.id, s]));
      workflow.steps = stepIds.map(id => stepMap.get(id)).filter(Boolean);
    }
    saveWorkflows(workflows);
    return true;
  }

  // ═══════════════════════════════════════════════════════════════
  // Phase Management (v2)
  // ═══════════════════════════════════════════════════════════════

  function addPhase(workflowId, name = "New Phase", description = "") {
    const workflows = getWorkflows();
    const workflow = workflows.find(w => w.id === workflowId);
    if (!workflow) return null;

    const v2 = ensureV2(workflow);
    Object.assign(workflow, v2);

    const newPhase = {
      id: `phase-${Date.now()}`,
      name,
      description,
      steps: [],
      transitions: [],
    };
    workflow.phases.push(newPhase);
    saveWorkflows(workflows);
    return newPhase;
  }

  function updatePhase(workflowId, phaseId, updates) {
    const workflows = getWorkflows();
    const workflow = workflows.find(w => w.id === workflowId);
    if (!workflow?.phases) return null;

    const phase = workflow.phases.find(p => p.id === phaseId);
    if (!phase) return null;

    Object.assign(phase, updates);
    saveWorkflows(workflows);
    return phase;
  }

  function removePhase(workflowId, phaseId) {
    const workflows = getWorkflows();
    const workflow = workflows.find(w => w.id === workflowId);
    if (!workflow?.phases) return false;

    // Check referential integrity -- don't delete if transitions point to it
    const hasRefs = workflow.phases.some(p =>
      p.transitions?.some(t => t.target === phaseId)
    );
    if (hasRefs) return false;

    workflow.phases = workflow.phases.filter(p => p.id !== phaseId);
    saveWorkflows(workflows);
    return true;
  }

  function reorderPhases(workflowId, phaseIds) {
    const workflows = getWorkflows();
    const workflow = workflows.find(w => w.id === workflowId);
    if (!workflow?.phases) return false;

    const phaseMap = new Map(workflow.phases.map(p => [p.id, p]));
    workflow.phases = phaseIds.map(id => phaseMap.get(id)).filter(Boolean);
    saveWorkflows(workflows);
    return true;
  }

  function addStepToPhase(workflowId, phaseId, stepType, config = {}) {
    const workflows = getWorkflows();
    const workflow = workflows.find(w => w.id === workflowId);
    if (!workflow?.phases) return null;

    const phase = workflow.phases.find(p => p.id === phaseId);
    if (!phase) return null;

    const allTypes = getAllStepTypes();
    const typeDef = allTypes[stepType];
    if (!typeDef) return null;

    const defaultConfig = {};
    for (const [key, schema] of Object.entries(typeDef.configSchema || {})) {
      defaultConfig[key] = schema.default;
    }

    const newStep = {
      id: `step-${Date.now()}`,
      type: stepType,
      name: typeDef.name,
      config: { ...defaultConfig, ...config },
      enabled: true,
      target: "browser",
    };

    if (!phase.steps) phase.steps = [];
    phase.steps.push(newStep);
    saveWorkflows(workflows);
    return newStep;
  }

  // ═══════════════════════════════════════════════════════════════
  // Workflow Execution (v2 Phase Walker)
  // ═══════════════════════════════════════════════════════════════

  function resolveVariableDefaults(vars) {
    if (!vars || typeof vars !== "object") return {};
    const resolved = {};
    for (const [key, val] of Object.entries(vars)) {
      // Support both { type, default } definitions and plain values
      if (val && typeof val === "object" && "type" in val) {
        resolved[key] = val.default ?? "";
      } else {
        resolved[key] = val;
      }
    }
    return resolved;
  }

  function normalizePrompt(prompt) {
    return (prompt || "").trim().toLowerCase();
  }

  // Active execution tracking for navigation
  let activeExecution = null;

  function buildV2Context(audioInput, workflowVariables) {
    return {
      audio: audioInput,
      // Legacy flat fields
      rawText: null,
      segments: null,
      labeledText: null,
      speakerCount: null,
      processedText: null,
      outputs: {},
      // V2 enhanced context
      text: { raw: null, labeled: null, processed: null },
      structured: {},
      variables: resolveVariableDefaults(workflowVariables),
      checkpoints: {},
      phaseOutputs: {},
      currentPhase: null,
      phaseHistory: [],
      // Recording persistence across navigation
      recordingStore: {},
    };
  }

  async function executeWorkflow(workflow, audioInput, dotNetRef, jobId) {
    const v2 = ensureV2(workflow);

    // If it's a simple v1 workflow (single phase, no transitions), use the v2 engine
    return executeWorkflowV2(v2, audioInput, dotNetRef, jobId);
  }

  async function executeActiveWorkflow(audioInput, dotNetRef, jobId) {
    const workflow = getActiveWorkflow();
    return executeWorkflow(workflow, audioInput, dotNetRef, jobId);
  }

  async function executeWorkflowV2(workflow, audioInput, dotNetRef, jobId, startPhaseIndex = 0, existingContext = null) {
    const context = existingContext || buildV2Context(audioInput, workflow.variables);
    const phases = workflow.phases || [];
    if (phases.length === 0) {
      await emitProgress(dotNetRef, jobId, 100, "done", "No phases to execute.", { isCompleted: true });
      return { rawWhisperText: null, speakerLabeledText: null, markdown: "", outputs: {} };
    }

    // Set up active execution tracking
    let aborted = false;
    activeExecution = {
      abort() { aborted = true; },
      context,
      dotNetRef,
      jobId,
      workflow,
    };

    // Emit initial phase list for UI
    const phaseNames = phases.map(p => p.name);
    if (dotNetRef?.invokeMethodAsync) {
      try {
        await dotNetRef.invokeMethodAsync("OnWorkflowPhaseProgress", { phaseNames, currentIndex: startPhaseIndex });
      } catch (e) { /* callback not available */ }
    }

    let currentPhaseIndex = startPhaseIndex;
    let phaseExecutionCount = 0;

    while (currentPhaseIndex < phases.length && phaseExecutionCount < MAX_PHASE_EXECUTIONS) {
      if (aborted) break;
      phaseExecutionCount++;
      const phase = phases[currentPhaseIndex];
      context.currentPhase = phase.id;
      context.phaseHistory.push({ phaseId: phase.id, timestamp: Date.now(), index: currentPhaseIndex });

      // Update phase progress for UI
      if (dotNetRef?.invokeMethodAsync) {
        try {
          await dotNetRef.invokeMethodAsync("OnWorkflowPhaseProgress", { phaseNames, currentIndex: currentPhaseIndex });
        } catch (e) { /* callback not available */ }
      }

      await emitProgress(dotNetRef, jobId, 0, "phase", `Phase: ${phase.name}`, {
        currentPhase: phase.id,
        phaseName: phase.name,
        phaseIndex: currentPhaseIndex,
        totalPhases: phases.length,
        phaseHistory: context.phaseHistory,
      });

      // Execute all steps in this phase linearly
      const enabledSteps = (phase.steps || []).filter(s => s.enabled);
      const totalSteps = enabledSteps.length;
      let stepIndex = 0;

      for (const step of enabledSteps) {
        if (aborted) break;
        stepIndex++;
        const phaseProgress = phases.length > 0
          ? (currentPhaseIndex / phases.length) * 100
          : 0;
        const stepProgress = totalSteps > 0
          ? (stepIndex / totalSteps) * (100 / phases.length)
          : 0;
        const overallPercent = Math.round(phaseProgress + stepProgress);

        await emitProgress(dotNetRef, jobId, overallPercent, step.type, `Running: ${step.name}...`, {
          currentPhase: phase.id,
        });

        try {
          const result = await executeStep(step, context, (pct, msg, extras) => {
            emitProgress(dotNetRef, jobId, overallPercent, step.type, msg, {
              currentPhase: phase.id,
              ...(extras || {}),
            });
          });

          // Check if aborted during interactive step
          if (aborted || result?.navigated) break;

          // Store outputs
          context.outputs[step.id] = result;

          // Update legacy flat context fields
          if (result.rawText !== undefined) {
            context.rawText = result.rawText;
            context.text.raw = result.rawText;
          }
          if (result.segments !== undefined) context.segments = result.segments;
          if (result.labeledText !== undefined) {
            context.labeledText = result.labeledText;
            context.text.labeled = result.labeledText;
          }
          if (result.speakerCount !== undefined) context.speakerCount = result.speakerCount;
          if (result.processedText !== undefined) {
            context.processedText = result.processedText;
            context.text.processed = result.processedText;
          }

          // Handle outputTo for data flow
          if (step.config?.outputTo) {
            setNestedValue(context, step.config.outputTo, result.processedText || result);
          }

          await emitProgress(dotNetRef, jobId, overallPercent, step.type, `Completed: ${step.name}`, {
            stepId: step.id,
            stepOutput: result,
            currentPhase: phase.id,
          });
        } catch (err) {
          if (aborted) break;
          await emitProgress(dotNetRef, jobId, overallPercent, step.type, `Failed: ${step.name} - ${err.message}`, {
            isError: true,
            stepId: step.id,
            currentPhase: phase.id,
          });
          throw err;
        }
      }

      if (aborted) break;

      // Store phase output
      context.phaseOutputs[phase.id] = {
        processedText: context.processedText,
        variables: { ...context.variables },
        timestamp: Date.now(),
      };

      // Resolve transition to next phase
      const nextPhaseIndex = await resolveTransition(phase, phases, context, dotNetRef, jobId);
      if (nextPhaseIndex === -1) {
        // Terminal -- no more phases
        break;
      }
      currentPhaseIndex = nextPhaseIndex;
    }

    // If aborted by navigation, return navigated result
    if (aborted) {
      return { navigated: true };
    }

    if (phaseExecutionCount >= MAX_PHASE_EXECUTIONS) {
      console.warn("[Workflow] Loop guard triggered: exceeded max phase executions");
      await emitProgress(dotNetRef, jobId, 100, "warning", "Loop guard: workflow exceeded maximum phase executions.", {
        isError: true,
      });
    }

    // Build final output
    const finalText = context.processedText || context.labeledText || context.rawText || "";

    activeExecution = null;

    await emitProgress(dotNetRef, jobId, 100, "done", "Workflow complete.", {
      isCompleted: true,
      rawWhisperText: context.rawText,
      speakerLabeledText: context.labeledText,
      markdown: finalText,
      detectedSpeakerCount: context.speakerCount,
      workflowOutputs: context.outputs,
      phaseOutputs: context.phaseOutputs,
      variables: context.variables,
    });

    return {
      rawWhisperText: context.rawText,
      speakerLabeledText: context.labeledText,
      markdown: finalText,
      detectedSpeakerCount: context.speakerCount,
      outputs: context.outputs,
      phaseOutputs: context.phaseOutputs,
      variables: context.variables,
    };
  }

  async function resolveTransition(phase, allPhases, context, dotNetRef, jobId) {
    const transitions = phase.transitions || [];

    if (transitions.length === 0) {
      // Auto-advance to next sequential phase
      const currentIndex = allPhases.findIndex(p => p.id === phase.id);
      return currentIndex + 1 < allPhases.length ? currentIndex + 1 : -1;
    }

    for (const transition of transitions) {
      switch (transition.condition) {
        case "auto":
          return findPhaseIndex(allPhases, transition.target);

        case "expression": {
          const result = evaluateExpression(transition.expression, context);
          if (result) {
            return findPhaseIndex(allPhases, transition.target);
          }
          break;
        }

        case "userChoice": {
          // Emit choice event and wait for user response
          const choiceId = `choice-${phase.id}-${Date.now()}`;
          const choiceTransitions = transitions.filter(t => t.condition === "userChoice");

          const selectedTarget = await new Promise((resolve) => {
            pendingChoices.set(choiceId, { resolve, transitions: choiceTransitions });

            emitProgress(dotNetRef, jobId, 0, "choice", "Waiting for user decision...", {
              isInteractive: true,
              interactiveType: "choice",
              choiceId,
              choices: choiceTransitions.map(t => ({
                label: t.label || t.target,
                target: t.target,
              })),
              currentPhase: phase.id,
            });
          });

          return findPhaseIndex(allPhases, selectedTarget);
        }
      }
    }

    // Fallback: advance to next phase
    const currentIndex = allPhases.findIndex(p => p.id === phase.id);
    return currentIndex + 1 < allPhases.length ? currentIndex + 1 : -1;
  }

  function findPhaseIndex(phases, targetId) {
    if (!targetId || targetId === "end" || targetId === "terminal") return -1;
    const idx = phases.findIndex(p => p.id === targetId);
    return idx >= 0 ? idx : -1;
  }

  function evaluateExpression(expression, context) {
    if (!expression) return false;
    try {
      // Simple expression evaluator -- supports basic comparisons
      // e.g. "variables.moreQuestions === true", "variables.count > 3"
      const fn = new Function("ctx", `with(ctx) { return ${expression}; }`);
      return !!fn(context);
    } catch (e) {
      console.warn("[Workflow] Expression evaluation failed:", expression, e);
      return false;
    }
  }

  // ═══════════════════════════════════════════════════════════════
  // Step Execution with Target Dispatch
  // ═══════════════════════════════════════════════════════════════

  async function executeStep(step, context, onProgress) {
    const target = step.target || step.config?.target || "browser";

    switch (target) {
      case "browser":
        return executeStepBrowser(step, context, onProgress);
      case "server":
      case "ollama":
      case "openai":
      case "anthropic":
        // Future: dispatch to server/API targets
        console.warn(`[Workflow] Execution target "${target}" not yet implemented, falling back to browser`);
        return executeStepBrowser(step, context, onProgress);
      default:
        return executeStepBrowser(step, context, onProgress);
    }
  }

  async function executeStepBrowser(step, context, onProgress) {
    // Check built-in handlers first, then plugin handlers
    const handler = stepHandlers[step.type] || pluginStepHandlers[step.type];
    if (!handler) {
      throw new Error(`Unknown step type: ${step.type}`);
    }

    // Handle inputFrom: read from arbitrary context path
    if (step.config?.inputFrom) {
      const inputValue = getNestedValue(context, step.config.inputFrom);
      if (inputValue !== undefined) {
        if (typeof inputValue === "string") {
          context.processedText = inputValue;
        }
      }
    }

    return handler(step, context, onProgress);
  }

  // ═══════════════════════════════════════════════════════════════
  // Step Handlers (Built-in)
  // ═══════════════════════════════════════════════════════════════

  const stepHandlers = {
    async transcribe(step, context, onProgress) {
      onProgress(10, "Loading Whisper model...");

      // Use existing transcription infrastructure
      const browser = window.localTranscriberBrowser;
      if (!browser) throw new Error("Browser transcriber not available");

      onProgress(30, "Transcribing audio...");

      const result = await browser.transcribeAudio?.(context.audio, step.config.model, step.config.language, (p, m) => {
        onProgress(30 + p * 0.6, m);
      });

      return {
        rawText: result?.text || context.rawText,
        segments: result?.segments || context.segments,
      };
    },

    async speakerLabels(step, context, onProgress) {
      onProgress(20, "Analyzing speakers...");

      const browser = window.localTranscriberBrowser;
      if (!browser) throw new Error("Browser transcriber not available");

      const result = browser.buildSpeakerLabeledTranscript?.(
        context.segments,
        context.rawText
      );

      onProgress(100, "Speaker labeling complete");

      return {
        labeledText: result?.text || context.rawText,
        speakerCount: result?.detectedSpeakerCount,
      };
    },

    async llmFormat(step, context, onProgress) {
      return runLlmStep(step, context, onProgress, (config, inputText) => {
        return `${config.userPrompt}\n\n${inputText}`;
      });
    },

    async llmTransform(step, context, onProgress) {
      return runLlmStep(step, context, onProgress, (config, inputText) => {
        return config.userPrompt.replace("{input}", inputText);
      });
    },

    async summarize(step, context, onProgress) {
      const prompts = {
        bullets: `Summarize the following text as ${step.config.maxLength} words or fewer using bullet points:\n\n`,
        paragraph: `Write a ${step.config.maxLength}-word paragraph summarizing:\n\n`,
        executive: `Write an executive summary (max ${step.config.maxLength} words) for:\n\n`,
      };

      return runLlmStep(step, context, onProgress, (config, inputText) => {
        return (prompts[config.style] || prompts.bullets) + inputText;
      });
    },

    async extractActions(step, context, onProgress) {
      const formatInstructions = {
        markdown: "Format as a Markdown list.",
        json: "Format as a JSON array of objects with 'task' and 'assignee' fields.",
        checklist: "Format as Markdown checkboxes (- [ ] task).",
      };

      return runLlmStep(step, context, onProgress, (config, inputText) => {
        return `Extract all action items, tasks, and to-dos from the following text. ${formatInstructions[config.format] || ""}\n\n${inputText}`;
      });
    },

    async convertFormat(step, context, onProgress) {
      const templates = {
        "business-report": "Convert this into a formal business report with sections: Executive Summary, Key Findings, Recommendations, Next Steps.",
        "meeting-notes": "Format as professional meeting notes with: Attendees (if mentioned), Agenda Items, Discussion Points, Decisions Made, Action Items.",
        "blog-post": "Transform into an engaging blog post with a catchy intro, clear sections, and a conclusion.",
        "email": "Convert into a professional email format with subject line suggestion, greeting, body, and sign-off.",
        "documentation": "Format as technical documentation with clear headings, bullet points, and code blocks where appropriate.",
        "custom": step.config.customTemplate || "Process this text:",
      };

      return runLlmStep(step, context, onProgress, (config, inputText) => {
        return `${templates[config.targetFormat] || templates.custom}\n\n${inputText}`;
      });
    },

    async merge(step, context, onProgress) {
      onProgress(50, "Merging outputs...");

      const input1 = context.processedText || context.labeledText || context.rawText || "";
      const input2 = "";

      let result = step.config.template || "{input1}\n\n{input2}";
      result = result.replace("{input1}", input1);
      result = result.replace("{input2}", input2);

      onProgress(100, "Merge complete");

      return { processedText: result };
    },

    async userReview(step, context, onProgress) {
      onProgress(0, "Waiting for user review...");

      const inputText = context.processedText || context.labeledText || context.rawText || "";

      return new Promise((resolve) => {
        const reviewId = `review-${step.id}-${Date.now()}`;

        pendingReviews.set(reviewId, {
          step,
          context,
          resolve,
          inputText,
          segments: context.segments,
          audio: context.audio,
        });

        onProgress(50, `Review required: ${step.config.title || "Review Transcript"}`, {
          isInteractive: true,
          interactiveType: "userReview",
          reviewId,
          inputText,
          segments: context.segments,
          showPlayback: step.config.showPlayback === "yes",
          showHighlighting: step.config.showHighlighting === "yes",
          requireApproval: step.config.requireApproval === "yes",
          instructions: step.config.instructions,
        });
      });
    },

    async checkpoint(step, context, onProgress) {
      onProgress(50, `Saving checkpoint: ${step.config.label}...`);

      const text = context.processedText || context.labeledText || context.rawText || "";

      context.checkpoints = context.checkpoints || {};
      context.checkpoints[step.config.label] = {
        text,
        timestamp: Date.now(),
        stepId: step.id,
      };

      onProgress(100, "Checkpoint saved");

      return {
        processedText: text,
        checkpointLabel: step.config.label,
      };
    },

    async compare(step, context, onProgress) {
      onProgress(0, "Waiting for version comparison...");

      const text1 = context.checkpoints?.[step.config.label1]?.text || context.rawText || "";
      const text2 = context.processedText || context.labeledText || "";

      return new Promise((resolve) => {
        const compareId = `compare-${step.id}-${Date.now()}`;

        pendingReviews.set(compareId, {
          step,
          context,
          resolve,
          text1,
          text2,
        });

        onProgress(50, "Compare versions", {
          isInteractive: true,
          interactiveType: "compare",
          compareId,
          text1,
          text2,
          label1: step.config.label1,
          label2: step.config.label2,
          diffMode: step.config.showDiff,
        });
      });
    },

    // Phase 2: New step handlers

    async agentGenerate(step, context, onProgress) {
      onProgress(10, "Preparing agent prompt...");

      const userPrompt = resolveTemplate(step.config.userPromptTemplate, context);

      const result = await runLlmStep(step, context, onProgress, (config) => {
        return userPrompt;
      });

      let outputValue = result.processedText;

      // Parse JSON output if requested
      if (step.config.outputFormat === "json" || step.config.outputFormat === "json-array") {
        try {
          outputValue = extractJsonFromText(result.processedText);
        } catch (e) {
          console.warn("[Workflow] Failed to parse JSON output from agentGenerate:", e);
        }
      }

      // Store in variables
      if (step.config.outputVariable) {
        context.variables[step.config.outputVariable] = outputValue;
      }

      return { processedText: typeof outputValue === "string" ? outputValue : JSON.stringify(outputValue, null, 2) };
    },

    async regexTransform(step, context, onProgress) {
      onProgress(20, "Applying regex transformations...");

      let text = context.processedText || context.labeledText || context.rawText || "";

      // Read from inputFrom if specified
      if (step.config.inputFrom) {
        const inputValue = getNestedValue(context, step.config.inputFrom);
        if (typeof inputValue === "string") text = inputValue;
      }

      const lines = (step.config.patterns || "").split("\n").filter(l => l.trim());
      for (const line of lines) {
        const match = line.match(/^\/(.+?)\/([gimsuvy]*)\s*=>\s*(.*)$/);
        if (match) {
          try {
            const regex = new RegExp(match[1], match[2]);
            text = text.replace(regex, match[3]);
          } catch (e) {
            console.warn(`[Workflow] Invalid regex pattern: ${line}`, e);
          }
        }
      }

      // Write to outputTo if specified
      if (step.config.outputTo) {
        setNestedValue(context, step.config.outputTo, text);
      }

      onProgress(100, "Regex transformations complete");
      return { processedText: text };
    },

    async jsonExtract(step, context, onProgress) {
      onProgress(30, "Extracting JSON data...");

      let text = context.processedText || context.labeledText || context.rawText || "";
      let parsed;

      if (step.config.extractionMode === "full-input") {
        try {
          parsed = JSON.parse(text);
        } catch (e) {
          parsed = step.config.fallbackValue || null;
        }
      } else {
        // Auto-detect: find JSON in text (handles markdown code fences)
        parsed = extractJsonFromText(text);
        if (parsed === null || parsed === undefined) {
          parsed = step.config.fallbackValue || null;
        }
      }

      // Extract via jsonPath
      if (step.config.jsonPath && parsed != null) {
        parsed = getNestedValue(parsed, step.config.jsonPath);
      }

      // Store in variable
      if (step.config.outputVariable) {
        context.variables[step.config.outputVariable] = parsed;
      }

      onProgress(100, "JSON extraction complete");
      return {
        processedText: typeof parsed === "string" ? parsed : JSON.stringify(parsed, null, 2),
      };
    },

    async templateFormat(step, context, onProgress) {
      onProgress(50, "Formatting template...");

      const result = resolveTemplate(step.config.template, context);

      // Write to outputTo if specified
      if (step.config.outputTo) {
        setNestedValue(context, step.config.outputTo, result);
      }

      onProgress(100, "Template formatting complete");
      return { processedText: result };
    },

    // Phase 3: Interactive step handlers

    async multiRecord(step, context, onProgress) {
      onProgress(0, "Preparing multi-record session...");

      // Read prompts from context variable
      let prompts = [];
      if (step.config.promptsVariable) {
        const promptsValue = getNestedValue(context, step.config.promptsVariable);
        if (Array.isArray(promptsValue)) {
          prompts = promptsValue;
        } else if (typeof promptsValue === "string") {
          try {
            prompts = JSON.parse(promptsValue);
          } catch {
            prompts = promptsValue.split("\n").filter(l => l.trim());
          }
        }
      }

      if (prompts.length === 0) {
        onProgress(100, "No prompts found for multi-record");
        return { processedText: "" };
      }

      // Match current prompts against recordingStore for previous recordings
      const store = context.recordingStore || {};
      const previousRecordings = {};
      const matchedKeys = new Set();

      prompts.forEach((prompt, i) => {
        const key = normalizePrompt(prompt);
        if (store[key]) {
          previousRecordings[i] = store[key];
          matchedKeys.add(key);
        }
      });

      // Find orphaned recordings (in store but not matching any current prompt)
      const orphanedRecordings = {};
      for (const [key, value] of Object.entries(store)) {
        if (!matchedKeys.has(key)) {
          orphanedRecordings[key] = value;
        }
      }

      return new Promise((resolve) => {
        const multiRecordId = `multiRecord-${step.id}-${Date.now()}`;

        pendingMultiRecords.set(multiRecordId, {
          step,
          context,
          resolve,
          prompts,
        });

        onProgress(50, `Record answers to ${prompts.length} questions`, {
          isInteractive: true,
          interactiveType: "multiRecord",
          multiRecordId,
          prompts,
          instructions: step.config.instructions,
          transcribeModel: step.config.transcribeModel,
          previousRecordings,
          orphanedRecordings,
        });
      });
    },

    async userChoice(step, context, onProgress) {
      onProgress(0, "Waiting for user choice...");

      let choices = [];
      try {
        choices = JSON.parse(step.config.choicesJson || "[]");
      } catch {
        choices = [];
      }

      if (choices.length === 0) {
        onProgress(100, "No choices configured");
        return { processedText: "", selectedChoice: null };
      }

      return new Promise((resolve) => {
        const choiceId = `userChoice-${step.id}-${Date.now()}`;

        pendingChoices.set(choiceId, {
          resolve: (selectedTarget) => {
            const selected = choices.find(c => c.nextPhase === selectedTarget) || choices[0];
            resolve({
              processedText: selected.label,
              selectedChoice: selected,
            });
          },
          transitions: choices.map(c => ({ label: c.label, target: c.nextPhase })),
        });

        onProgress(50, step.config.title || "Make a choice", {
          isInteractive: true,
          interactiveType: "choice",
          choiceId,
          choices: choices.map(c => ({
            label: c.label,
            target: c.nextPhase,
          })),
          title: step.config.title,
        });
      });
    },
  };

  // Plugin step handlers registered at runtime
  const pluginStepHandlers = {};

  // ═══════════════════════════════════════════════════════════════
  // Interactive Resolution
  // ═══════════════════════════════════════════════════════════════

  const pendingReviews = new Map();
  const pendingChoices = new Map();
  const pendingMultiRecords = new Map();

  function completeReview(reviewId, editedText, approved = true) {
    const review = pendingReviews.get(reviewId);
    if (!review) {
      console.warn(`[Workflow] Review not found: ${reviewId}`);
      return false;
    }

    pendingReviews.delete(reviewId);
    review.resolve({
      processedText: editedText,
      editedText,
      approved,
      reviewedAt: Date.now(),
    });

    return true;
  }

  function completeCompare(compareId, selectedText, selectedVersion) {
    const compare = pendingReviews.get(compareId);
    if (!compare) {
      console.warn(`[Workflow] Compare not found: ${compareId}`);
      return false;
    }

    pendingReviews.delete(compareId);
    compare.resolve({
      processedText: selectedText,
      selectedText,
      selectedVersion,
      comparedAt: Date.now(),
    });

    return true;
  }

  function completeChoice(choiceId, selectedTarget) {
    const choice = pendingChoices.get(choiceId);
    if (!choice) {
      console.warn(`[Workflow] Choice not found: ${choiceId}`);
      return false;
    }

    pendingChoices.delete(choiceId);
    choice.resolve(selectedTarget);
    return true;
  }

  function completeMultiRecord(multiRecordId, recordings, deletedOrphanKeys) {
    const mr = pendingMultiRecords.get(multiRecordId);
    if (!mr) {
      console.warn(`[Workflow] MultiRecord not found: ${multiRecordId}`);
      return false;
    }

    pendingMultiRecords.delete(multiRecordId);
    const { step, context, prompts } = mr;

    // Build Q&A pairs — handle both { text: ... } and { answer: ... } shapes
    const pairs = prompts.map((prompt, i) => ({
      question: prompt,
      answer: recordings[i]?.text ?? recordings[i]?.answer ?? (typeof recordings[i] === "string" ? recordings[i] : ""),
    }));

    // Update recordingStore with new recordings
    const store = context.recordingStore || (context.recordingStore = {});
    pairs.forEach(pair => {
      const key = normalizePrompt(pair.question);
      store[key] = { question: pair.question, answer: pair.answer };
    });

    // Remove explicitly deleted orphans from recordingStore
    const deletedKeys = deletedOrphanKeys || [];
    deletedKeys.forEach(key => {
      delete store[key];
    });

    // Collect non-deleted orphans to include in output
    const currentPromptKeys = new Set(prompts.map(p => normalizePrompt(p)));
    const orphanPairs = [];
    for (const [key, value] of Object.entries(store)) {
      if (!currentPromptKeys.has(key)) {
        orphanPairs.push(value);
      }
    }

    const allPairs = [...pairs, ...orphanPairs];

    // Store in variables
    const outputVar = step.config.outputVariable || "answers";
    if (step.config.outputFormat === "qa-pairs") {
      context.variables[outputVar] = allPairs;
    } else {
      context.variables[outputVar] = allPairs.map(p => p.answer);
    }

    // Build combined text
    const combinedText = allPairs.map(p =>
      `**Q: ${p.question}**\n${p.answer}`
    ).join("\n\n");

    mr.resolve({
      processedText: combinedText,
      pairs: allPairs,
    });

    return true;
  }

  // ═══════════════════════════════════════════════════════════════
  // LLM + JSON Helpers
  // ═══════════════════════════════════════════════════════════════

  async function runLlmStep(step, context, onProgress, buildPrompt) {
    onProgress(10, `Loading model: ${step.config.model}...`);

    const browser = window.localTranscriberBrowser;
    if (!browser?.formatWithWebLlm) {
      throw new Error("WebLLM not available");
    }

    const inputText = context.processedText || context.labeledText || context.rawText || "";
    const prompt = buildPrompt(step.config, inputText);

    onProgress(30, "Running LLM...");

    const result = await browser.formatWithWebLlm(
      step.config.model,
      "custom",
      "en",
      prompt,
      (pct, msg) => onProgress(30 + pct * 0.6, msg),
      {
        systemPrompt: step.config.systemPrompt,
        temperature: step.config.temperature || 0.3,
      }
    );

    onProgress(100, "LLM processing complete");

    return { processedText: result };
  }

  function extractJsonFromText(text) {
    if (!text) return null;

    // Try direct parse first
    try {
      return JSON.parse(text);
    } catch {
      // Continue to extraction
    }

    // Try to find JSON in markdown code fences
    const fenceMatch = text.match(/```(?:json)?\s*\n?([\s\S]*?)\n?\s*```/);
    if (fenceMatch) {
      try {
        return JSON.parse(fenceMatch[1].trim());
      } catch {
        // Continue
      }
    }

    // Try to find JSON array or object in text
    const jsonMatch = text.match(/(\[[\s\S]*\]|\{[\s\S]*\})/);
    if (jsonMatch) {
      try {
        return JSON.parse(jsonMatch[1]);
      } catch {
        // Give up
      }
    }

    return null;
  }

  async function emitProgress(dotNetRef, jobId, percent, stage, message, extras = {}) {
    if (!dotNetRef?.invokeMethodAsync) return;

    await dotNetRef.invokeMethodAsync("OnWorkflowProgress", {
      jobId,
      percent,
      stage,
      message,
      isCompleted: false,
      isError: false,
      ...extras,
    });
  }

  // ═══════════════════════════════════════════════════════════════
  // Plugin System
  // ═══════════════════════════════════════════════════════════════

  function registerStepType(definition, handler) {
    if (!definition?.id || typeof handler !== "function") {
      console.error("[Workflow] Invalid plugin step type registration");
      return false;
    }

    if (stepTypes[definition.id]) {
      console.warn(`[Workflow] Cannot override built-in step type: ${definition.id}`);
      return false;
    }

    pluginStepTypes[definition.id] = {
      ...definition,
      isPlugin: true,
    };
    pluginStepHandlers[definition.id] = handler;
    console.log(`[Workflow] Registered plugin step type: ${definition.id}`);
    return true;
  }

  function unregisterStepType(id) {
    if (stepTypes[id]) {
      console.warn(`[Workflow] Cannot unregister built-in step type: ${id}`);
      return false;
    }
    delete pluginStepTypes[id];
    delete pluginStepHandlers[id];
    return true;
  }

  async function loadPlugin(url) {
    try {
      const module = await import(url);
      if (typeof module.register === "function") {
        module.register({
          registerStepType,
          resolveTemplate,
          getNestedValue,
          setNestedValue,
          runLlmStep,
        });
        console.log(`[Workflow] Plugin loaded from: ${url}`);
        return true;
      } else {
        console.warn(`[Workflow] Plugin module has no register() export: ${url}`);
        return false;
      }
    } catch (e) {
      console.error(`[Workflow] Failed to load plugin: ${url}`, e);
      return false;
    }
  }

  async function loadAllPlugins() {
    const plugins = getPluginRegistry();
    for (const plugin of plugins) {
      if (plugin.enabled) {
        await loadPlugin(plugin.url);
      }
    }
  }

  function getPluginRegistry() {
    try {
      const stored = localStorage.getItem(PLUGIN_STORAGE_KEY);
      return stored ? JSON.parse(stored) : [];
    } catch {
      return [];
    }
  }

  function savePluginRegistry(plugins) {
    localStorage.setItem(PLUGIN_STORAGE_KEY, JSON.stringify(plugins));
  }

  function addPlugin(url, name = "") {
    const plugins = getPluginRegistry();
    if (plugins.some(p => p.url === url)) return false;
    plugins.push({ url, name: name || url, enabled: true, addedAt: Date.now() });
    savePluginRegistry(plugins);
    return true;
  }

  function removePlugin(url) {
    const plugins = getPluginRegistry().filter(p => p.url !== url);
    savePluginRegistry(plugins);
    const id = Object.keys(pluginStepTypes).find(k => pluginStepTypes[k]._pluginUrl === url);
    if (id) unregisterStepType(id);
    return true;
  }

  function togglePlugin(url, enabled) {
    const plugins = getPluginRegistry();
    const plugin = plugins.find(p => p.url === url);
    if (plugin) {
      plugin.enabled = enabled;
      savePluginRegistry(plugins);
    }
    return true;
  }

  function getAllStepTypes() {
    return { ...stepTypes, ...pluginStepTypes };
  }

  // ═══════════════════════════════════════════════════════════════
  // Preset Workflows
  // ═══════════════════════════════════════════════════════════════

  const presetWorkflows = [
    {
      id: "preset-blog-interview",
      name: "Blog Writing (Interview Style)",
      description: "Record topic intro, generate interview questions, record answers, review, then generate blog post",
      version: 2,
      variables: { topic: { type: "string", default: "" }, blogTitle: { type: "string", default: "" } },
      phases: [
        {
          id: "phase-topic",
          name: "Topic Introduction",
          description: "Record your topic intro and transcribe it",
          steps: [
            { id: "s-topic-1", type: "transcribe", name: "Transcribe Topic", config: { model: "SmallEn", language: "auto" }, enabled: true, target: "browser" },
            { id: "s-topic-2", type: "llmFormat", name: "Format Intro", config: { model: "Llama-3.1-8B-Instruct-q4f16_1-MLC", systemPrompt: "You are a transcription editor.", userPrompt: "Clean up this transcript into clear sentences.", temperature: 0.2 }, enabled: true, target: "browser" },
            { id: "s-topic-3", type: "userReview", name: "Review Topic", config: { title: "Review Your Topic", instructions: "Review and edit your topic introduction.", showPlayback: "yes", showHighlighting: "no", requireApproval: "yes" }, enabled: true, target: "browser" },
          ],
          transitions: [{ id: "t-topic-1", target: "phase-questions", condition: "auto" }],
        },
        {
          id: "phase-questions",
          name: "Generate Questions",
          description: "AI generates interview questions, then you review them before recording",
          steps: [
            { id: "s-q-1", type: "agentGenerate", name: "Generate Questions", config: { model: "Llama-3.1-8B-Instruct-q4f16_1-MLC", systemPrompt: "You are an expert interviewer. Generate insightful, open-ended questions that will help the user elaborate on their topic for a blog post. Output as a JSON array of strings.", userPromptTemplate: "Generate 5 interview questions about the following topic:\n\n{text.processed}", outputFormat: "json-array", outputVariable: "questions", temperature: 0.5 }, enabled: true, target: "browser" },
            { id: "s-q-2", type: "userReview", name: "Review Questions", config: { title: "Review Interview Questions", instructions: "Review the generated questions. Edit or remove any that don't fit before recording your answers.", showPlayback: "no", showHighlighting: "no", requireApproval: "yes" }, enabled: true, target: "browser" },
          ],
          transitions: [{ id: "t-q-1", target: "phase-answers", condition: "auto" }],
        },
        {
          id: "phase-answers",
          name: "Record Answers",
          description: "Record your answers, then transcribe and correlate with questions",
          steps: [
            { id: "s-a-1", type: "multiRecord", name: "Answer Questions", config: { promptsVariable: "variables.questions", instructions: "Record your answer for each question. Take your time and speak naturally.", transcribeModel: "SmallEn", outputVariable: "answers", outputFormat: "qa-pairs" }, enabled: true, target: "browser" },
            { id: "s-a-2", type: "agentGenerate", name: "Format Q&A", config: { model: "Llama-3.1-8B-Instruct-q4f16_1-MLC", systemPrompt: "You are a transcription editor. Take raw interview Q&A pairs and format them into clean, well-structured text. Preserve the question-answer format. Clean up speech artifacts, fix grammar, and ensure readability while keeping the speaker's original intent and meaning.", userPromptTemplate: "Format these interview Q&A pairs into clean, readable text:\n\n{text.processed}", outputFormat: "text", outputVariable: "formattedQA", temperature: 0.2 }, enabled: true, target: "browser" },
          ],
          transitions: [{ id: "t-a-1", target: "phase-review-qa", condition: "auto" }],
        },
        {
          id: "phase-review-qa",
          name: "Review Q&A",
          description: "Review your Q&A pairs and decide what to do next",
          steps: [
            { id: "s-rqa-1", type: "userReview", name: "Review Answers", config: { title: "Review Q&A Pairs", instructions: "Review your answers. Edit any responses that need correction.", showPlayback: "no", showHighlighting: "no", requireApproval: "yes" }, enabled: true, target: "browser" },
          ],
          transitions: [
            { id: "t-rqa-1", target: "phase-questions", condition: "userChoice", label: "More Questions" },
            { id: "t-rqa-2", target: "phase-write", condition: "userChoice", label: "Start Writing" },
          ],
        },
        {
          id: "phase-write",
          name: "Write Blog Post",
          description: "AI writes a blog post from your accumulated Q&A content",
          steps: [
            { id: "s-w-1", type: "agentGenerate", name: "Write Blog Post", config: { model: "Llama-3.1-8B-Instruct-q4f16_1-MLC", systemPrompt: "You are an expert blog writer. Write engaging, well-structured blog posts based on interview-style Q&A content. Use a conversational but informative tone.", userPromptTemplate: "Write a comprehensive blog post based on the following interview Q&A content. Create a compelling title, introduction, well-organized sections, and conclusion.\n\nTopic Introduction:\n{text.processed}\n\nQ&A Content:\n{variables.formattedQA}", outputFormat: "text", outputVariable: "blogPost", temperature: 0.6 }, enabled: true, target: "browser" },
          ],
          transitions: [{ id: "t-w-1", target: "phase-final-review", condition: "auto" }],
        },
        {
          id: "phase-final-review",
          name: "Final Review",
          description: "Review and edit the final blog post",
          steps: [
            { id: "s-fr-1", type: "userReview", name: "Final Edit", config: { title: "Final Blog Review", instructions: "Review the generated blog post. Make any final edits before exporting.", showPlayback: "no", showHighlighting: "no", requireApproval: "yes" }, enabled: true, target: "browser" },
          ],
          transitions: [],
        },
      ],
    },
    {
      id: "preset-meeting-notes",
      name: "Meeting Notes",
      description: "Transcribe, label speakers, format as meeting notes, then review",
      version: 2,
      variables: {},
      phases: [
        {
          id: "phase-capture",
          name: "Capture & Process",
          description: "Transcribe and identify speakers",
          steps: [
            { id: "s-mn-1", type: "transcribe", name: "Transcribe Meeting", config: { model: "SmallEn", language: "auto" }, enabled: true, target: "browser" },
            { id: "s-mn-2", type: "speakerLabels", name: "Label Speakers", config: { sensitivity: 50, maxSpeakers: 6 }, enabled: true, target: "browser" },
            { id: "s-mn-3", type: "convertFormat", name: "Format as Meeting Notes", config: { model: "Llama-3.1-8B-Instruct-q4f16_1-MLC", targetFormat: "meeting-notes", customTemplate: "" }, enabled: true, target: "browser" },
          ],
          transitions: [{ id: "t-mn-1", target: "phase-mn-review", condition: "auto" }],
        },
        {
          id: "phase-mn-review",
          name: "Review",
          description: "Review the formatted meeting notes",
          steps: [
            { id: "s-mn-4", type: "userReview", name: "Review Notes", config: { title: "Review Meeting Notes", instructions: "Review the formatted meeting notes. Correct any speaker names or errors.", showPlayback: "yes", showHighlighting: "yes", requireApproval: "yes" }, enabled: true, target: "browser" },
          ],
          transitions: [],
        },
      ],
    },
    {
      id: "preset-podcast-transcript",
      name: "Podcast Transcript",
      description: "Transcribe, label speakers, summarize, format, and review",
      version: 2,
      variables: {},
      phases: [
        {
          id: "phase-pc-process",
          name: "Process",
          description: "Transcribe, label, and summarize",
          steps: [
            { id: "s-pc-1", type: "transcribe", name: "Transcribe Podcast", config: { model: "SmallEn", language: "auto" }, enabled: true, target: "browser" },
            { id: "s-pc-2", type: "speakerLabels", name: "Label Speakers", config: { sensitivity: 40, maxSpeakers: 4 }, enabled: true, target: "browser" },
            { id: "s-pc-3", type: "summarize", name: "Generate Summary", config: { model: "Llama-3.1-8B-Instruct-q4f16_1-MLC", style: "executive", maxLength: 300 }, enabled: true, target: "browser" },
            { id: "s-pc-4", type: "llmFormat", name: "Format Transcript", config: { model: "Llama-3.1-8B-Instruct-q4f16_1-MLC", systemPrompt: "You are a transcription editor.", userPrompt: "Format this podcast transcript with clear speaker labels, timestamps, and paragraph breaks.", temperature: 0.2 }, enabled: true, target: "browser" },
          ],
          transitions: [{ id: "t-pc-1", target: "phase-pc-review", condition: "auto" }],
        },
        {
          id: "phase-pc-review",
          name: "Review",
          description: "Review the final podcast transcript",
          steps: [
            { id: "s-pc-5", type: "userReview", name: "Review Transcript", config: { title: "Review Podcast Transcript", instructions: "Review the formatted podcast transcript and summary.", showPlayback: "yes", showHighlighting: "yes", requireApproval: "yes" }, enabled: true, target: "browser" },
          ],
          transitions: [],
        },
      ],
    },
  ];

  function getPresetWorkflows() {
    return presetWorkflows.map(p => ({
      id: p.id,
      name: p.name,
      description: p.description,
      phaseCount: p.phases?.length || 0,
    }));
  }

  function createFromPreset(presetId) {
    const preset = presetWorkflows.find(p => p.id === presetId);
    if (!preset) return null;

    const now = Date.now();
    const newWorkflow = JSON.parse(JSON.stringify(preset));
    newWorkflow.id = `workflow-${now}`;
    newWorkflow.name = `${preset.name}`;

    // Regenerate all IDs
    if (newWorkflow.phases) {
      newWorkflow.phases = newWorkflow.phases.map((p, pi) => ({
        ...p,
        id: `phase-${now}-${pi}`,
        steps: (p.steps || []).map((s, si) => ({
          ...s,
          id: `step-${now}-${pi}-${si}`,
        })),
        transitions: (p.transitions || []).map((t, ti) => ({
          ...t,
          id: `trans-${now}-${pi}-${ti}`,
          // Remap targets to new phase IDs
          target: t.target ? `phase-${now}-${preset.phases.findIndex(pp => pp.id === t.target)}` : null,
        })),
      }));
    }

    // Build flat steps for v1 compat
    newWorkflow.steps = newWorkflow.phases.flatMap(p => p.steps || []);

    const workflows = getWorkflows();
    workflows.push(newWorkflow);
    saveWorkflows(workflows);
    return newWorkflow;
  }

  // ═══════════════════════════════════════════════════════════════
  // Phase Navigation
  // ═══════════════════════════════════════════════════════════════

  async function navigateToPhase(targetPhaseIndex) {
    if (!activeExecution) {
      console.warn("[Workflow] No active execution to navigate");
      return false;
    }

    const { context, dotNetRef, jobId, workflow } = activeExecution;
    const phases = workflow.phases || [];

    if (targetPhaseIndex < 0 || targetPhaseIndex >= phases.length) {
      console.warn(`[Workflow] Invalid phase index: ${targetPhaseIndex}`);
      return false;
    }

    // Abort current execution
    activeExecution.abort();

    // Resolve all pending interactive promises so the execution loop unblocks
    for (const [, review] of pendingReviews) {
      review.resolve({ processedText: "", navigated: true });
    }
    pendingReviews.clear();

    for (const [, choice] of pendingChoices) {
      choice.resolve(null);
    }
    pendingChoices.clear();

    for (const [, mr] of pendingMultiRecords) {
      mr.resolve({ processedText: "", navigated: true });
    }
    pendingMultiRecords.clear();

    // Build a new context, preserving recordingStore and replaying phase outputs up to target
    const newContext = buildV2Context(context.audio, workflow.variables);
    newContext.recordingStore = context.recordingStore;

    // Replay step outputs and phase outputs for phases before the target
    for (let i = 0; i < targetPhaseIndex; i++) {
      const phase = phases[i];
      const savedOutput = context.phaseOutputs[phase.id];
      if (savedOutput) {
        newContext.phaseOutputs[phase.id] = savedOutput;
        Object.assign(newContext.variables, savedOutput.variables);
        if (savedOutput.processedText) {
          newContext.processedText = savedOutput.processedText;
        }
      }
      // Restore individual step outputs
      for (const step of (phase.steps || [])) {
        if (context.outputs[step.id]) {
          newContext.outputs[step.id] = context.outputs[step.id];
          const result = context.outputs[step.id];
          if (result.rawText !== undefined) {
            newContext.rawText = result.rawText;
            newContext.text.raw = result.rawText;
          }
          if (result.segments !== undefined) newContext.segments = result.segments;
          if (result.labeledText !== undefined) {
            newContext.labeledText = result.labeledText;
            newContext.text.labeled = result.labeledText;
          }
          if (result.speakerCount !== undefined) newContext.speakerCount = result.speakerCount;
          if (result.processedText !== undefined) {
            newContext.processedText = result.processedText;
            newContext.text.processed = result.processedText;
          }
        }
      }
    }

    // Notify UI about navigation
    if (dotNetRef?.invokeMethodAsync) {
      try {
        await dotNetRef.invokeMethodAsync("OnWorkflowPhaseNavigated", targetPhaseIndex);
      } catch (e) { /* callback not available */ }
    }

    // Re-invoke workflow from target phase after current execution unwinds
    setTimeout(() => {
      executeWorkflowV2(workflow, newContext.audio, dotNetRef, jobId, targetPhaseIndex, newContext);
    }, 0);

    return true;
  }

  // ═══════════════════════════════════════════════════════════════
  // Public API
  // ═══════════════════════════════════════════════════════════════

  return {
    // Step types (merged built-in + plugins)
    getStepTypes: () => getAllStepTypes(),
    getAllStepTypes,
    getStepType: (id) => {
      const all = getAllStepTypes();
      return all[id] ? { ...all[id] } : null;
    },

    // Workflow CRUD
    getWorkflows,
    getActiveWorkflow,
    getActiveWorkflowId,
    setActiveWorkflow,
    createWorkflow,
    duplicateWorkflow,
    updateWorkflow,
    deleteWorkflow,
    getDefaultWorkflow: () => ({ ...defaultWorkflow }),

    // Step management (v1 compat)
    addStep,
    updateStep,
    removeStep,
    reorderSteps,

    // Phase management (v2)
    addPhase,
    updatePhase,
    removePhase,
    reorderPhases,
    addStepToPhase,
    migrateV1ToV2,
    ensureV2,

    // Execution
    executeWorkflow,
    executeWorkflowV2,
    executeActiveWorkflow,

    // Interactive steps
    completeReview,
    completeCompare,
    completeChoice,
    completeMultiRecord,
    navigateToPhase,
    getPendingReviews: () => Array.from(pendingReviews.keys()),
    getPendingChoices: () => Array.from(pendingChoices.keys()),
    getPendingMultiRecords: () => Array.from(pendingMultiRecords.keys()),

    // Plugin system
    registerStepType,
    unregisterStepType,
    loadPlugin,
    loadAllPlugins,
    getPluginRegistry,
    addPlugin,
    removePlugin,
    togglePlugin,

    // Presets
    getPresetWorkflows,
    createFromPreset,

    // Utilities (exposed for plugins)
    resolveTemplate,
    getNestedValue,
    setNestedValue,
  };
})();

console.log("[LocalTranscriber] Workflow engine v2 loaded");
