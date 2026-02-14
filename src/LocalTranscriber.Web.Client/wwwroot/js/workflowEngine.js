/**
 * LocalTranscriber Workflow Engine
 * Executes customizable transcription/processing pipelines
 */

window.localTranscriberWorkflow = (() => {
  const WORKFLOW_STORAGE_KEY = "localTranscriber_workflows";
  const ACTIVE_WORKFLOW_KEY = "localTranscriber_activeWorkflow";

  // ═══════════════════════════════════════════════════════════════
  // Step Type Definitions
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
  };

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
  // Workflow Management
  // ═══════════════════════════════════════════════════════════════

  function getWorkflows() {
    try {
      const stored = localStorage.getItem(WORKFLOW_STORAGE_KEY);
      if (stored) {
        const workflows = JSON.parse(stored);
        // Ensure default workflow exists
        if (!workflows.find(w => w.id === "default")) {
          workflows.unshift({ ...defaultWorkflow });
        }
        return workflows;
      }
    } catch (e) {
      console.warn("[Workflow] Failed to load workflows:", e);
    }
    return [{ ...defaultWorkflow }];
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
    newWorkflow.steps = newWorkflow.steps.map((s, i) => ({
      ...s,
      id: `step-${Date.now()}-${i}`,
    }));

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
  // Step Management
  // ═══════════════════════════════════════════════════════════════

  function addStep(workflowId, stepType, config = {}, insertIndex = -1) {
    const workflows = getWorkflows();
    const workflow = workflows.find(w => w.id === workflowId);
    if (!workflow) return null;

    const typeDef = stepTypes[stepType];
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
    };

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

    const step = workflow.steps.find(s => s.id === stepId);
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

    workflow.steps = workflow.steps.filter(s => s.id !== stepId);
    saveWorkflows(workflows);
    return true;
  }

  function reorderSteps(workflowId, stepIds) {
    const workflows = getWorkflows();
    const workflow = workflows.find(w => w.id === workflowId);
    if (!workflow) return false;

    const stepMap = new Map(workflow.steps.map(s => [s.id, s]));
    workflow.steps = stepIds.map(id => stepMap.get(id)).filter(Boolean);
    saveWorkflows(workflows);
    return true;
  }

  // ═══════════════════════════════════════════════════════════════
  // Workflow Execution
  // ═══════════════════════════════════════════════════════════════

  async function executeWorkflow(workflow, audioInput, dotNetRef, jobId) {
    const context = {
      audio: audioInput,
      rawText: null,
      segments: null,
      labeledText: null,
      speakerCount: null,
      processedText: null,
      outputs: {},
    };

    const enabledSteps = workflow.steps.filter(s => s.enabled);
    const totalSteps = enabledSteps.length;
    let currentStep = 0;

    for (const step of enabledSteps) {
      currentStep++;
      const basePercent = Math.round((currentStep - 1) / totalSteps * 100);
      const nextPercent = Math.round(currentStep / totalSteps * 100);

      await emitProgress(dotNetRef, jobId, basePercent, step.type, `Running: ${step.name}...`);

      try {
        const result = await executeStep(step, context, (pct, msg) => {
          const scaledPct = basePercent + Math.round((pct / 100) * (nextPercent - basePercent));
          emitProgress(dotNetRef, jobId, scaledPct, step.type, msg);
        });

        // Store outputs in context
        context.outputs[step.id] = result;
        
        // Update context based on step outputs
        if (result.rawText !== undefined) context.rawText = result.rawText;
        if (result.segments !== undefined) context.segments = result.segments;
        if (result.labeledText !== undefined) context.labeledText = result.labeledText;
        if (result.speakerCount !== undefined) context.speakerCount = result.speakerCount;
        if (result.processedText !== undefined) context.processedText = result.processedText;

        await emitProgress(dotNetRef, jobId, nextPercent, step.type, `Completed: ${step.name}`, {
          stepId: step.id,
          stepOutput: result,
        });
      } catch (err) {
        await emitProgress(dotNetRef, jobId, nextPercent, step.type, `Failed: ${step.name} - ${err.message}`, {
          isError: true,
          stepId: step.id,
        });
        throw err;
      }
    }

    // Build final output
    const finalText = context.processedText || context.labeledText || context.rawText || "";
    
    await emitProgress(dotNetRef, jobId, 100, "done", "Workflow complete.", {
      isCompleted: true,
      rawWhisperText: context.rawText,
      speakerLabeledText: context.labeledText,
      markdown: finalText,
      detectedSpeakerCount: context.speakerCount,
      workflowOutputs: context.outputs,
    });

    return {
      rawWhisperText: context.rawText,
      speakerLabeledText: context.labeledText,
      markdown: finalText,
      detectedSpeakerCount: context.speakerCount,
      outputs: context.outputs,
    };
  }

  async function executeStep(step, context, onProgress) {
    const handler = stepHandlers[step.type];
    if (!handler) {
      throw new Error(`Unknown step type: ${step.type}`);
    }
    return handler(step, context, onProgress);
  }

  // ═══════════════════════════════════════════════════════════════
  // Step Handlers
  // ═══════════════════════════════════════════════════════════════

  const stepHandlers = {
    async transcribe(step, context, onProgress) {
      onProgress(10, "Loading Whisper model...");
      
      // Use existing transcription infrastructure
      const browser = window.localTranscriberBrowser;
      if (!browser) throw new Error("Browser transcriber not available");

      // This would integrate with existing transcribe logic
      // For now, we call the existing pipeline
      const request = {
        jobId: `step-${step.id}`,
        model: step.config.model,
        language: step.config.language,
      };

      onProgress(30, "Transcribing audio...");
      
      // Note: This is a simplified version. Full implementation would
      // directly call the Whisper pipeline with proper audio handling
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

      // Use existing speaker labeling
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
      
      // Get inputs from previous step outputs
      const input1 = context.processedText || context.labeledText || context.rawText || "";
      const input2 = ""; // Would come from parallel branches in future

      let result = step.config.template || "{input1}\n\n{input2}";
      result = result.replace("{input1}", input1);
      result = result.replace("{input2}", input2);

      onProgress(100, "Merge complete");

      return { processedText: result };
    },
  };

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
  // Public API
  // ═══════════════════════════════════════════════════════════════

  return {
    // Step types
    getStepTypes: () => ({ ...stepTypes }),
    getStepType: (id) => stepTypes[id] ? { ...stepTypes[id] } : null,

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

    // Step management
    addStep,
    updateStep,
    removeStep,
    reorderSteps,

    // Execution
    executeWorkflow,
  };
})();

console.log("[LocalTranscriber] Workflow engine loaded");
