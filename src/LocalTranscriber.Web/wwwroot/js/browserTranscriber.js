window.localTranscriberBrowser = (() => {
  const whisperModelMap = {
    tiny: "Xenova/whisper-tiny",
    tinyen: "Xenova/whisper-tiny.en",
    base: "Xenova/whisper-base",
    baseen: "Xenova/whisper-base.en",
    small: "Xenova/whisper-small",
    smallen: "Xenova/whisper-small.en",
    medium: "Xenova/whisper-medium",
    mediumen: "Xenova/whisper-medium.en",
    largev1: "Xenova/whisper-large-v1",
    largev2: "Xenova/whisper-large-v2",
    largev3: "Xenova/whisper-large-v3",
    largev3turbo: "Xenova/whisper-large-v3-turbo",
  };

  // Mirror configuration for model downloads
  // Set via: setMirrorPreference('jsdelivr') or localStorage
  // jsDelivr CDN serves from the browser-models branch with CORS headers
  const JSDELIVR_BASE = "https://cdn.jsdelivr.net/gh/JerrettDavis/LocalTranscriber@browser-models";
  
  const defaultMirrors = [
    { url: "https://huggingface.co", name: "HuggingFace", region: "Global", type: "hf" },
    { url: "jsdelivr", name: "jsDelivr CDN", region: "Enterprise-friendly", type: "jsdelivr" },
    { url: "https://hf-mirror.com", name: "HF-Mirror", region: "China-friendly", type: "hf" },
  ];

  // === Prompt Templates ===
  const PROMPT_STORAGE_KEY = "localTranscriber_promptTemplates";
  
  const defaultPromptTemplates = {
    systemMessage: "You are a transcription editor.",
    cleanupPrompt: `Convert the following raw transcript into clean Markdown with structure and correct punctuation.

Rules:
- Do NOT add facts that are not present.
- Keep speaker intent the same.
- Fix obvious ASR errors when you can infer the intended word from context.
- Keep code-ish content as inline code or fenced blocks.
- {strictness}
- If uncertain, preserve the original wording instead of summarizing.
- Summary should contain between {summaryMinBullets} and {summaryMaxBullets} bullets.
- {actionItemRule}`,
    outputTemplate: `# Transcription

- **Model:** \`{model}\`
- **Language:** \`{language}\`

## Summary
- (3-8 bullet points)

## Action Items
- (use checkboxes like "- [ ]")

## Transcript
(Use paragraphs. Use headings only if the transcript strongly suggests it.)`
  };

  function getPromptTemplates() {
    try {
      const stored = localStorage.getItem(PROMPT_STORAGE_KEY);
      if (stored) {
        const parsed = JSON.parse(stored);
        return {
          systemMessage: parsed.systemMessage || defaultPromptTemplates.systemMessage,
          cleanupPrompt: parsed.cleanupPrompt || defaultPromptTemplates.cleanupPrompt,
          outputTemplate: parsed.outputTemplate || defaultPromptTemplates.outputTemplate,
        };
      }
    } catch (e) {
      console.warn("[LocalTranscriber] Failed to load prompt templates:", e);
    }
    return { ...defaultPromptTemplates };
  }

  function setPromptTemplates(templates) {
    try {
      const toSave = {
        systemMessage: templates.systemMessage || defaultPromptTemplates.systemMessage,
        cleanupPrompt: templates.cleanupPrompt || defaultPromptTemplates.cleanupPrompt,
        outputTemplate: templates.outputTemplate || defaultPromptTemplates.outputTemplate,
      };
      localStorage.setItem(PROMPT_STORAGE_KEY, JSON.stringify(toSave));
      console.log("[LocalTranscriber] Prompt templates saved");
      return true;
    } catch (e) {
      console.error("[LocalTranscriber] Failed to save prompt templates:", e);
      return false;
    }
  }

  function resetPromptTemplates() {
    localStorage.removeItem(PROMPT_STORAGE_KEY);
    console.log("[LocalTranscriber] Prompt templates reset to defaults");
    return { ...defaultPromptTemplates };
  }

  function getDefaultPromptTemplates() {
    return { ...defaultPromptTemplates };
  }

  function buildFormattingPrompt(transcript, model, language, options = {}) {
    const templates = getPromptTemplates();
    const strictness = options.strictTranscript
      ? "Preserve transcript wording very strictly."
      : "You may lightly smooth wording while preserving meaning.";
    const actionItemRule = options.includeActionItems !== false
      ? "Include actionable checkbox items if present."
      : "Keep Action Items minimal (use - [] when unclear).";
    const summaryMin = options.summaryMinBullets || 3;
    const summaryMax = options.summaryMaxBullets || 8;

    const cleanupWithPlaceholders = templates.cleanupPrompt
      .replace("{strictness}", strictness)
      .replace("{actionItemRule}", actionItemRule)
      .replace("{summaryMinBullets}", summaryMin)
      .replace("{summaryMaxBullets}", summaryMax);

    const outputWithPlaceholders = templates.outputTemplate
      .replace("{model}", model || "unknown")
      .replace("{language}", language || "auto");

    return [
      cleanupWithPlaceholders,
      "",
      "Output template (exact sections, in this order):",
      outputWithPlaceholders,
      "",
      "Raw transcript:",
      transcript || "(no speech detected)"
    ].join("\n");
  }

  // === Tuning Options ===
  const TUNING_STORAGE_KEY = "localTranscriber_tuningOptions";
  
  const defaultTuningOptions = {
    strictTranscript: true,
    includeActionItems: true,
    summaryMinBullets: 3,
    summaryMaxBullets: 8,
    temperature: 0.2,
    sensitivity: 50,
  };

  function getTuningOptions() {
    try {
      const stored = localStorage.getItem(TUNING_STORAGE_KEY);
      if (stored) {
        const parsed = JSON.parse(stored);
        return {
          strictTranscript: parsed.strictTranscript ?? defaultTuningOptions.strictTranscript,
          includeActionItems: parsed.includeActionItems ?? defaultTuningOptions.includeActionItems,
          summaryMinBullets: parsed.summaryMinBullets ?? defaultTuningOptions.summaryMinBullets,
          summaryMaxBullets: parsed.summaryMaxBullets ?? defaultTuningOptions.summaryMaxBullets,
          temperature: parsed.temperature ?? defaultTuningOptions.temperature,
          sensitivity: parsed.sensitivity ?? defaultTuningOptions.sensitivity,
        };
      }
    } catch (e) {
      console.warn("[LocalTranscriber] Failed to load tuning options:", e);
    }
    return { ...defaultTuningOptions };
  }

  function setTuningOptions(options) {
    try {
      const toSave = {
        strictTranscript: options.strictTranscript ?? defaultTuningOptions.strictTranscript,
        includeActionItems: options.includeActionItems ?? defaultTuningOptions.includeActionItems,
        summaryMinBullets: options.summaryMinBullets ?? defaultTuningOptions.summaryMinBullets,
        summaryMaxBullets: options.summaryMaxBullets ?? defaultTuningOptions.summaryMaxBullets,
        temperature: options.temperature ?? defaultTuningOptions.temperature,
        sensitivity: options.sensitivity ?? defaultTuningOptions.sensitivity,
      };
      localStorage.setItem(TUNING_STORAGE_KEY, JSON.stringify(toSave));
      console.log("[LocalTranscriber] Tuning options saved");
      return true;
    } catch (e) {
      console.error("[LocalTranscriber] Failed to save tuning options:", e);
      return false;
    }
  }

  function resetTuningOptions() {
    localStorage.removeItem(TUNING_STORAGE_KEY);
    console.log("[LocalTranscriber] Tuning options reset to defaults");
    return { ...defaultTuningOptions };
  }

  let transformersModulePromise = null;
  let webLlmModulePromise = null;
  const asrPipelineCache = new Map();
  const webLlmEngineCache = new Map();
  let preferredMirror = null; // Set by user or auto-detected
  let originalFetch = null; // For GitHub Releases URL rewriting

  function getCapabilities() {
    const hasWebGpu = typeof navigator !== "undefined" && !!navigator.gpu;
    const hasAudioContext =
      typeof window !== "undefined" &&
      (typeof window.AudioContext !== "undefined" ||
        typeof window.webkitAudioContext !== "undefined");
    const hasMediaRecorder = typeof MediaRecorder !== "undefined";
    const supported = hasWebGpu && hasAudioContext && hasMediaRecorder;

    // Mobile detection
    const isMobile = /Android|webOS|iPhone|iPad|iPod|BlackBerry|IEMobile|Opera Mini/i.test(
      navigator.userAgent
    ) || (navigator.maxTouchPoints > 0 && window.innerWidth < 768);
    
    // Memory estimation (in MB)
    const deviceMemory = navigator.deviceMemory || null; // GB, Chrome only
    const estimatedMemoryMB = deviceMemory ? deviceMemory * 1024 : (isMobile ? 200 : 2000);
    
    // Model recommendations based on device
    const recommendedModels = getRecommendedModels(isMobile, estimatedMemoryMB);

    let reason = "Browser-only mode unavailable.";
    if (supported) {
      if (isMobile) {
        reason = `Browser mode available (mobile). Recommended: ${recommendedModels[0] || "Tiny"} model for best performance.`;
      } else {
        reason = "Browser-only mode available. WebGPU detected, transcription can run fully in-browser.";
      }
    } else {
      const missing = [];
      if (!hasWebGpu) missing.push("WebGPU");
      if (!hasAudioContext) missing.push("AudioContext");
      if (!hasMediaRecorder) missing.push("MediaRecorder");
      reason = `Browser-only mode unavailable. Missing: ${missing.join(", ")}.`;
    }

    return {
      supported,
      hasWebGpu,
      hasAudioContext,
      hasMediaRecorder,
      isMobile,
      estimatedMemoryMB,
      recommendedModels,
      reason,
    };
  }

  function getRecommendedModels(isMobile, memoryMB) {
    // Model approximate memory requirements (MB):
    // tiny: ~150MB, base: ~250MB, small: ~500MB, medium: ~1500MB
    if (isMobile || memoryMB < 300) {
      return ["Tiny", "TinyEn"];
    } else if (memoryMB < 600) {
      return ["Tiny", "TinyEn", "Base", "BaseEn"];
    } else if (memoryMB < 1500) {
      return ["Base", "BaseEn", "Small", "SmallEn"];
    } else {
      return ["Small", "SmallEn", "Base", "BaseEn"];
    }
  }

  function checkMemoryForModel(modelName) {
    const caps = getCapabilities();
    const modelMemory = {
      tiny: 150, tinyen: 150,
      base: 250, baseen: 250,
      small: 500, smallen: 500,
      medium: 1500, mediumen: 1500,
    };
    const normalized = modelName.toLowerCase().replace(/[^a-z]/g, "");
    const required = modelMemory[normalized] || 500;
    const available = caps.estimatedMemoryMB;
    
    return {
      modelName,
      requiredMB: required,
      availableMB: available,
      isSafe: available >= required * 1.2, // 20% headroom
      warning: available < required * 1.2 
        ? `⚠️ ${modelName} needs ~${required}MB but device may only have ~${available}MB available. Consider using a smaller model.`
        : null,
    };
  }

  // PWA / Service Worker helpers
  async function registerServiceWorker() {
    if ('serviceWorker' in navigator) {
      try {
        const registration = await navigator.serviceWorker.register('/LocalTranscriber/sw.js');
        console.log('[LocalTranscriber] Service Worker registered:', registration.scope);
        return registration;
      } catch (err) {
        console.warn('[LocalTranscriber] Service Worker registration failed:', err);
        return null;
      }
    }
    return null;
  }

  async function clearModelCache() {
    if (!navigator.serviceWorker?.controller) {
      console.warn('[LocalTranscriber] No active Service Worker');
      return false;
    }
    return new Promise((resolve) => {
      const channel = new MessageChannel();
      channel.port1.onmessage = (event) => resolve(event.data?.success || false);
      navigator.serviceWorker.controller.postMessage(
        { type: 'CLEAR_MODEL_CACHE' },
        [channel.port2]
      );
    });
  }

  async function getModelCacheSize() {
    if (!navigator.serviceWorker?.controller) {
      return 0;
    }
    return new Promise((resolve) => {
      const channel = new MessageChannel();
      channel.port1.onmessage = (event) => resolve(event.data?.size || 0);
      navigator.serviceWorker.controller.postMessage(
        { type: 'GET_CACHE_SIZE' },
        [channel.port2]
      );
    });
  }

  function isPWAInstalled() {
    return window.matchMedia('(display-mode: standalone)').matches ||
           window.navigator.standalone === true;
  }

  async function transcribeInBrowser(dotNetRef, request) {
    const capabilities = getCapabilities();
    if (!capabilities.supported) {
      throw new Error(capabilities.reason);
    }

    // Check memory before loading model
    const memCheck = checkMemoryForModel(request.model);
    if (memCheck.warning && capabilities.isMobile) {
      console.warn(memCheck.warning);
      await emitProgress(dotNetRef, request, 2, "prepare", memCheck.warning);
      // Give user a moment to see the warning
      await new Promise(r => setTimeout(r, 1500));
    }

    await emitProgress(dotNetRef, request, 5, "prepare", "Browser mode enabled.");
    await emitProgress(dotNetRef, request, 12, "prepare", "Decoding audio in browser...");

    const audioData = await decodeToMono16kFloat32(request.base64);
    await emitProgress(
      dotNetRef,
      request,
      28,
      "transcribe",
      "Loading in-browser Whisper model..."
    );

    const asr = await getWhisperPipeline(request.model, (pct) =>
      emitProgress(
        dotNetRef,
        request,
        Math.min(43, Math.max(30, pct)),
        "transcribe",
        "Preparing in-browser Whisper runtime..."
      )
    );

    await emitProgress(
      dotNetRef,
      request,
      45,
      "transcribe",
      "Running in-browser transcription..."
    );

    const language =
      request.language && request.language.toLowerCase() !== "auto"
        ? request.language
        : undefined;

    let asrResult;
    try {
      asrResult = await asr(audioData, {
        chunk_length_s: 30,
        stride_length_s: 5,
        return_timestamps: "word",
        ...(language ? { language } : {}),
      });
    } catch {
      asrResult = await asr(audioData, {
        chunk_length_s: 30,
        stride_length_s: 5,
        return_timestamps: true,
        ...(language ? { language } : {}),
      });
    }

    const rawText = normalizeText(asrResult?.text ?? "");
    const subtitleSegments = buildSubtitleSegments(asrResult?.chunks, rawText);
    await emitProgress(
      dotNetRef,
      request,
      62,
      "transcribe",
      "In-browser transcription complete.",
      { rawWhisperText: rawText, subtitleSegments }
    );

    let speakerLabeledText = rawText;
    let detectedSpeakerCount = null;

    if (request.enableSpeakerLabels) {
      await emitProgress(
        dotNetRef,
        request,
        68,
        "speakers",
        "Applying browser speaker labels..."
      );

      const speakerResult = buildSpeakerLabeledTranscript(subtitleSegments, rawText);
      speakerLabeledText = speakerResult.text;
      detectedSpeakerCount = speakerResult.detectedSpeakerCount;

      await emitProgress(
        dotNetRef,
        request,
        74,
        "speakers",
        "Speaker labeling complete (browser heuristic).",
        {
          rawWhisperText: rawText,
          speakerLabeledText,
          detectedSpeakerCount,
          subtitleSegments,
        }
      );
    }

    const transcriptForFormatting = request.enableSpeakerLabels
      ? speakerLabeledText
      : rawText;

    await emitProgress(
      dotNetRef,
      request,
      82,
      "format",
      "Formatting transcript..."
    );

    let formatterOutput = "";
    let formatterUsed = "local-browser";

    if (request.enableWebLlmCleanup) {
      try {
        await emitProgress(
          dotNetRef,
          request,
          86,
          "format",
          `Loading WebLLM model (${request.webLlmModel})...`
        );

        formatterOutput = await formatWithWebLlm(
          request.webLlmModel,
          request.model,
          request.language,
          transcriptForFormatting,
          (pct, msg) =>
            emitProgress(dotNetRef, request, pct, "format", msg)
        );
        formatterUsed = "webllm";

        await emitProgress(
          dotNetRef,
          request,
          92,
          "format",
          "Formatter output received from WebLLM.",
          { formatterOutput, formatterUsed }
        );
      } catch (err) {
        const reason = err instanceof Error ? err.message : `${err}`;
        await emitProgress(
          dotNetRef,
          request,
          90,
          "format",
          `WebLLM unavailable (${reason}). Falling back to local browser formatter.`
        );
      }
    }

    if (!formatterOutput) {
      formatterOutput = formatMarkdownLocally({
        model: request.model,
        language: request.language,
        transcript: transcriptForFormatting,
        detectedSpeakerCount,
      });
      formatterUsed = "local-browser";
    }

    const finalMarkdown = enforceTranscriptSection(
      formatterOutput,
      transcriptForFormatting
    );

    await emitProgress(dotNetRef, request, 100, "done", "Browser transcription complete.", {
      isCompleted: true,
      rawWhisperText: rawText,
      speakerLabeledText,
      formatterOutput,
      formatterUsed,
      markdown: finalMarkdown,
      detectedSpeakerCount,
      subtitleSegments,
    });

    return {
      rawWhisperText: rawText,
      speakerLabeledText,
      formatterOutput,
      formatterUsed,
      markdown: finalMarkdown,
      detectedSpeakerCount,
      subtitleSegments,
    };
  }

  async function emitProgress(dotNetRef, request, percent, stage, message, extras = {}) {
    if (!dotNetRef || typeof dotNetRef.invokeMethodAsync !== "function") {
      return;
    }

    const payload = {
      jobId: request.jobId,
      percent,
      stage,
      message,
      isCompleted: false,
      isError: false,
      ...extras,
    };

    await dotNetRef.invokeMethodAsync("OnBrowserProgress", payload);
  }

  async function getWhisperPipeline(modelName, onProgress) {
    const modelId = resolveWhisperModel(modelName);
    const cacheKey = `webgpu:${modelId}`;
    if (asrPipelineCache.has(cacheKey)) {
      return asrPipelineCache.get(cacheKey);
    }

    const transformers = await getTransformersModule();
    const instance = await createAsrPipeline(transformers, modelId, onProgress);
    asrPipelineCache.set(cacheKey, instance);
    return instance;
  }

  async function getTransformersModule() {
    if (!transformersModulePromise) {
      transformersModulePromise = import(
        "https://cdn.jsdelivr.net/npm/@xenova/transformers@2.17.2"
      );
    }

    return transformersModulePromise;
  }

  // === Mirror Management ===
  
  function listMirrors() {
    // Return available mirrors with status
    return defaultMirrors.map(m => ({ url: m.url, name: m.name, region: m.region }));
  }

  function getMirrorPreference() {
    // Check for user preference (localStorage > window global)
    return localStorage.getItem("transformers_mirror") || 
           window.TRANSFORMERS_MIRROR || 
           preferredMirror;
  }

  function setMirrorPreference(mirrorUrl) {
    // Set preferred mirror (persists to localStorage)
    const cleaned = mirrorUrl?.replace(/\/$/, "") || null;
    preferredMirror = cleaned;
    if (cleaned) {
      localStorage.setItem("transformers_mirror", cleaned);
      console.log(`[LocalTranscriber] Mirror preference set: ${cleaned}`);
    } else {
      localStorage.removeItem("transformers_mirror");
      console.log("[LocalTranscriber] Mirror preference cleared, using defaults");
    }
    // Clear pipeline cache so next load uses new mirror
    asrPipelineCache.clear();
    return cleaned;
  }

  function getOrderedMirrors() {
    // Build ordered list: user preference first, then defaults
    const pref = getMirrorPreference();
    const urls = [];
    
    if (pref) {
      urls.push(pref);
    }
    
    for (const m of defaultMirrors) {
      if (!urls.includes(m.url)) {
        urls.push(m.url);
      }
    }
    
    return urls;
  }

  // === jsDelivr CDN Mirror Support ===
  
  function isJsDelivrMirror(mirrorUrl) {
    return mirrorUrl === "jsdelivr";
  }

  function rewriteUrlForJsDelivr(url) {
    // Convert HuggingFace-style URLs to jsDelivr CDN format
    // HF: https://huggingface.co/Xenova/whisper-small/resolve/main/onnx/model_quantized.onnx
    // jsDelivr: https://cdn.jsdelivr.net/gh/JerrettDavis/LocalTranscriber@browser-models/whisper-small/onnx/model_quantized.onnx
    
    try {
      const urlObj = new URL(url);
      
      // Check if this is a HuggingFace model URL
      if (!urlObj.hostname.includes("huggingface")) {
        return url; // Not a HF URL, don't rewrite
      }
      
      // Parse HF URL: /Xenova/whisper-small/resolve/main/path/to/file.ext
      const pathMatch = urlObj.pathname.match(/^\/([^/]+)\/([^/]+)\/resolve\/[^/]+\/(.+)$/);
      if (!pathMatch) {
        return url; // Doesn't match expected format
      }
      
      const [, org, model, filePath] = pathMatch;
      
      // Only rewrite Xenova whisper models
      if (org !== "Xenova" || !model.startsWith("whisper-")) {
        return url;
      }
      
      // jsDelivr uses directory structure: /model/path/file.ext
      const newUrl = `${JSDELIVR_BASE}/${model}/${filePath}`;
      
      console.log(`[LocalTranscriber] Rewriting URL for jsDelivr CDN:`);
      console.log(`  From: ${url}`);
      console.log(`  To: ${newUrl}`);
      
      return newUrl;
    } catch {
      return url;
    }
  }

  function enableJsDelivrProxy() {
    if (originalFetch) return; // Already enabled
    
    originalFetch = window.fetch;
    window.fetch = async function(input, init) {
      let url = typeof input === "string" ? input : input?.url;
      
      // Log all fetch requests for debugging
      const isHfUrl = url && url.includes("huggingface.co");
      if (isHfUrl) {
        console.log(`[LocalTranscriber] Intercepted fetch: ${url?.substring(0, 100)}...`);
      }
      
      if (url && url.includes("huggingface.co") && url.includes("Xenova/whisper-")) {
        const newUrl = rewriteUrlForJsDelivr(url);
        if (newUrl !== url) {
          console.log(`[LocalTranscriber] Rewriting to: ${newUrl}`);
          if (typeof input === "string") {
            input = newUrl;
          } else if (input?.url) {
            input = new Request(newUrl, input);
          }
        }
      }
      
      try {
        return await originalFetch.call(this, input, init);
      } catch (err) {
        console.error(`[LocalTranscriber] Fetch failed for ${typeof input === "string" ? input : input?.url}:`, err.message);
        throw err;
      }
    };
    
    console.log("[LocalTranscriber] jsDelivr CDN fetch proxy enabled");
  }

  function disableJsDelivrProxy() {
    if (originalFetch) {
      window.fetch = originalFetch;
      originalFetch = null;
      console.log("[LocalTranscriber] jsDelivr CDN fetch proxy disabled");
    }
  }

  async function probeMirrors(timeoutMs = 5000) {
    // Test which mirrors are reachable (CORS check via small file)
    const results = [];
    
    for (const mirror of defaultMirrors) {
      // Different test URL for jsDelivr CDN
      let testUrl;
      if (mirror.type === "jsdelivr") {
        testUrl = `${JSDELIVR_BASE}/whisper-tiny/config.json`;
      } else {
        testUrl = `${mirror.url}/Xenova/whisper-tiny/resolve/main/config.json`;
      }
      
      const start = performance.now();
      
      try {
        const controller = new AbortController();
        const timeoutId = setTimeout(() => controller.abort(), timeoutMs);
        
        const response = await fetch(testUrl, { 
          method: "HEAD",
          mode: "cors",
          signal: controller.signal 
        });
        
        clearTimeout(timeoutId);
        const latency = Math.round(performance.now() - start);
        
        results.push({
          url: mirror.url,
          name: mirror.name,
          region: mirror.region,
          reachable: response.ok || response.status === 302,
          latency,
          status: response.status
        });
      } catch (err) {
        results.push({
          url: mirror.url,
          name: mirror.name,
          region: mirror.region,
          reachable: false,
          latency: null,
          error: err.name === "AbortError" ? "Timeout" : (err.message || "Network error")
        });
      }
    }
    
    // Sort by reachable first, then by latency
    results.sort((a, b) => {
      if (a.reachable !== b.reachable) return b.reachable - a.reachable;
      return (a.latency || 9999) - (b.latency || 9999);
    });
    
    return results;
  }

  async function autoSelectMirror(timeoutMs = 5000) {
    // Probe mirrors and select the best one
    console.log("[LocalTranscriber] Auto-selecting best mirror...");
    const results = await probeMirrors(timeoutMs);
    
    const best = results.find(r => r.reachable);
    if (best) {
      console.log(`[LocalTranscriber] Best mirror: ${best.name} (${best.url}) - ${best.latency}ms`);
      setMirrorPreference(best.url);
      return best;
    }
    
    console.warn("[LocalTranscriber] No reachable mirrors found");
    return null;
  }

  function applyMirrorToEnv(transformers, mirrorUrl) {
    if (transformers?.env) {
      // Only set remoteHost - don't override remotePathTemplate
      // transformers.js already has the correct HF path format built-in
      transformers.env.remoteHost = mirrorUrl;
    }
  }

  async function createAsrPipeline(transformers, modelId, onProgress) {
    if (transformers?.env) {
      transformers.env.allowRemoteModels = true;
      transformers.env.allowLocalModels = false;
      transformers.env.useBrowserCache = true;
    }

    const mirrors = getOrderedMirrors();
    let lastError = null;
    let successMirror = null;

    // Try each mirror in order
    for (const mirrorUrl of mirrors) {
      const mirrorInfo = defaultMirrors.find(m => m.url === mirrorUrl);
      const mirrorName = mirrorInfo?.name || mirrorUrl;
      const isJsDelivr = mirrorInfo?.type === "jsdelivr" || mirrorUrl === "jsdelivr";
      
      // Enable/disable jsDelivr CDN fetch proxy
      if (isJsDelivr) {
        enableJsDelivrProxy();
        // For jsDelivr, we still use HF as the "host" but intercept fetch calls
        applyMirrorToEnv(transformers, "https://huggingface.co");
      } else {
        disableJsDelivrProxy();
        applyMirrorToEnv(transformers, mirrorUrl);
      }
      
      console.log(`[LocalTranscriber] Trying mirror: ${mirrorName}${isJsDelivr ? " (via fetch proxy)" : ""}`);

      try {
        // Try WebGPU first
        const pipeline = await transformers.pipeline("automatic-speech-recognition", modelId, {
          device: "webgpu",
          quantized: true,
          progress_callback: (update) => {
            if (!onProgress || typeof update?.progress !== "number") return;
            const pct = Math.round(30 + update.progress * 13);
            onProgress(pct);
          },
        });
        console.log(`[LocalTranscriber] ✓ Model loaded from ${mirrorName} (WebGPU)`);
        successMirror = mirrorUrl;
        // Remember working mirror for next time
        if (!getMirrorPreference()) {
          preferredMirror = mirrorUrl;
        }
        return pipeline;
      } catch (webGpuError) {
        // If WebGPU failed, try without it
        try {
          const pipeline = await transformers.pipeline("automatic-speech-recognition", modelId, {
            quantized: true,
            progress_callback: (update) => {
              if (!onProgress || typeof update?.progress !== "number") return;
              const pct = Math.round(30 + update.progress * 13);
              onProgress(pct);
            },
          });
          console.log(`[LocalTranscriber] ✓ Model loaded from ${mirrorName} (CPU fallback)`);
          successMirror = mirrorUrl;
          if (!getMirrorPreference()) {
            preferredMirror = mirrorUrl;
          }
          return pipeline;
        } catch (cpuError) {
          lastError = cpuError;
          const errorMsg = cpuError?.message || String(cpuError);
          
          // Check for network/CORS errors - try next mirror
          if (errorMsg.includes("CORS") || errorMsg.includes("NetworkError") || 
              errorMsg.includes("Failed to fetch") || errorMsg.includes("blocked") ||
              errorMsg.includes("TypeError") || errorMsg.includes("404")) {
            console.warn(`[LocalTranscriber] ✗ ${mirrorName} failed (${errorMsg.slice(0, 50)}), trying next...`);
            disableJsDelivrProxy(); // Clean up before trying next
            continue;
          }
          
          // For other errors, might not be mirror-related
          console.error(`[LocalTranscriber] ✗ ${mirrorName} failed:`, cpuError);
          disableJsDelivrProxy();
        }
      }
    }

    // Clean up proxy on failure
    disableJsDelivrProxy();

    // All mirrors failed
    const mirrorList = mirrors.map(u => defaultMirrors.find(m => m.url === u)?.name || u).join(", ");
    const helpMessage = 
      `Model download failed. Tried mirrors: ${mirrorList}\n\n` +
      "This is likely due to network policies blocking model CDNs.\n\n" +
      "Options:\n" +
      "1. Run: localTranscriberBrowser.autoSelectMirror() to find a working mirror\n" +
      "2. Set manually: localTranscriberBrowser.setMirrorPreference('jsdelivr')\n" +
      "3. If using jsDelivr CDN, ensure models are synced (run workflow)\n" +
      "4. Use server-side transcription mode instead";
    
    throw new Error(helpMessage + "\n\nLast error: " + (lastError?.message || "Unknown"));
  }

  async function formatWithWebLlm(webLlmModel, whisperModel, language, transcript, onProgress) {
    const cleanedModel = normalizeText(webLlmModel);
    if (!cleanedModel) {
      throw new Error("No WebLLM model was specified.");
    }

    onProgress?.(88, "Starting WebLLM cleanup...");

    const engine = await getWebLlmEngine(cleanedModel, (report) => {
      const progress = typeof report?.progress === "number" ? report.progress : 0;
      const pct = Math.round(86 + progress * 6);
      const text = normalizeText(report?.text) || "Initializing WebLLM model cache...";
      onProgress?.(Math.min(94, Math.max(86, pct)), text);
    });

    // Use customizable prompt templates and tuning options
    const templates = getPromptTemplates();
    const tuning = getTuningOptions();
    const prompt = buildFormattingPrompt(transcript, whisperModel, language, {
      strictTranscript: tuning.strictTranscript,
      includeActionItems: tuning.includeActionItems,
      summaryMinBullets: tuning.summaryMinBullets,
      summaryMaxBullets: tuning.summaryMaxBullets
    });

    const completion = await engine.chat.completions.create({
      messages: [
        { role: "system", content: templates.systemMessage },
        { role: "user", content: prompt }
      ],
      temperature: tuning.temperature,
    });

    const text = normalizeCompletionText(completion);
    if (!text) {
      throw new Error("WebLLM returned an empty response.");
    }

    return text;
  }

  async function getWebLlmEngine(model, progressCallback) {
    if (webLlmEngineCache.has(model)) {
      return webLlmEngineCache.get(model);
    }

    const webllm = await getWebLlmModule();
    const createEngine = webllm.CreateMLCEngine || webllm.CreateWebWorkerMLCEngine;
    if (typeof createEngine !== "function") {
      throw new Error("WebLLM runtime does not expose a supported engine factory.");
    }

    const engine = await createEngine(model, {
      initProgressCallback: (report) => progressCallback?.(report),
    });

    webLlmEngineCache.set(model, engine);
    return engine;
  }

  async function getWebLlmModule() {
    if (!webLlmModulePromise) {
      webLlmModulePromise = import("https://esm.run/@mlc-ai/web-llm");
    }

    return webLlmModulePromise;
  }

  async function listWebLlmModels() {
    try {
      const webllm = await getWebLlmModule();
      const list = webllm?.prebuiltAppConfig?.model_list;
      if (!Array.isArray(list)) {
        return [];
      }

      return list
        .map((item) => normalizeText(item?.model_id))
        .filter((id) => !!id);
    } catch {
      return [];
    }
  }

  function buildSpeakerLabeledTranscript(subtitleSegments, fallbackText) {
    const lines = [];
    if (Array.isArray(subtitleSegments)) {
      for (const segment of subtitleSegments) {
        const text = normalizeText(segment?.text);
        if (!text) continue;
        lines.push(`[Speaker 1] ${text}`);
      }
    }

    if (lines.length === 0) {
      const text = normalizeText(fallbackText);
      if (!text) {
        return { text: "", detectedSpeakerCount: 0 };
      }

      return {
        text: `[Speaker 1] ${text}`,
        detectedSpeakerCount: 1,
      };
    }

    return {
      text: lines.join("\n"),
      detectedSpeakerCount: 1,
    };
  }

  function buildSubtitleSegments(chunks, fallbackText) {
    const timedWords = [];
    let cursor = 0;

    if (Array.isArray(chunks)) {
      for (const chunk of chunks) {
        const text = normalizeText(chunk?.text);
        if (!text) continue;

        let start = cursor;
        let end = cursor + estimateDurationSeconds(text);

        const timestamp = normalizeTimestamp(chunk?.timestamp ?? chunk?.timestamps);
        if (timestamp) {
          if (Number.isFinite(timestamp.start) && timestamp.start >= 0) {
            start = timestamp.start;
          }

          if (Number.isFinite(timestamp.end) && timestamp.end > start) {
            end = timestamp.end;
          }
        }

        end = Math.max(end, start + estimateDurationSeconds(text));

        if (!Number.isFinite(end) || end <= start) {
          end = start + estimateDurationSeconds(text);
        }

        cursor = end;
        const words = buildTimedWords(text, start, end);
        timedWords.push(...words);
      }
    }

    if (timedWords.length > 0) {
      return buildSegmentsFromWords(timedWords);
    }

    const text = normalizeText(fallbackText);
    if (!text) {
      return [];
    }

    const fallbackWords = buildTimedWords(text, 0, Math.max(2, estimateDurationSeconds(text)));
    return [
      {
        startSeconds: 0,
        endSeconds: Number(Math.max(2, estimateDurationSeconds(text)).toFixed(3)),
        text,
        speaker: null,
        words: fallbackWords,
      },
    ];
  }

  function buildSegmentsFromWords(rawWords) {
    const words = [...rawWords]
      .map((word) => ({
        text: normalizeText(word?.text),
        startSeconds: toFiniteNumber(word?.startSeconds),
        endSeconds: toFiniteNumber(word?.endSeconds),
      }))
      .filter(
        (word) =>
          word.text &&
          Number.isFinite(word.startSeconds) &&
          Number.isFinite(word.endSeconds) &&
          word.endSeconds > word.startSeconds
      )
      .sort((a, b) => a.startSeconds - b.startSeconds);

    if (words.length === 0) {
      return [];
    }

    const segments = [];
    let current = [];

    const flush = () => {
      if (current.length === 0) {
        return;
      }

      const segmentText = composeSegmentText(current.map((x) => x.text));
      if (!segmentText) {
        current = [];
        return;
      }

      segments.push({
        startSeconds: Number(current[0].startSeconds.toFixed(3)),
        endSeconds: Number(current[current.length - 1].endSeconds.toFixed(3)),
        text: segmentText,
        speaker: null,
        words: current.map((x) => ({
          text: x.text,
          startSeconds: Number(x.startSeconds.toFixed(3)),
          endSeconds: Number(x.endSeconds.toFixed(3)),
        })),
      });
      current = [];
    };

    for (const word of words) {
      if (current.length === 0) {
        current.push(word);
        continue;
      }

      const previous = current[current.length - 1];
      const gap = Math.max(0, word.startSeconds - previous.endSeconds);
      const sentenceEnd = /[.!?]$/.test(previous.text);
      const shouldBreak =
        gap > 0.6 ||
        current.length >= 18 ||
        (sentenceEnd && (gap >= 0.16 || current.length >= 10));

      if (shouldBreak) {
        flush();
      }

      current.push(word);
    }

    flush();
    return segments;
  }

  function buildTimedWords(text, startSeconds, endSeconds) {
    const tokens = normalizeText(text)
      .split(/\s+/)
      .map((token) => normalizeText(token))
      .filter(Boolean);

    if (tokens.length === 0) {
      return [];
    }

    let start = Number.isFinite(startSeconds) ? Math.max(0, startSeconds) : 0;
    let end = Number.isFinite(endSeconds) ? endSeconds : start + estimateDurationSeconds(text);
    if (!Number.isFinite(end) || end <= start) {
      end = start + Math.max(0.08, estimateDurationSeconds(text));
    }

    if (tokens.length === 1) {
      return [
        {
          text: tokens[0],
          startSeconds: Number(start.toFixed(3)),
          endSeconds: Number(end.toFixed(3)),
        },
      ];
    }

    const duration = Math.max(0.08, end - start);
    const weights = tokens.map(estimateWordWeight);
    const totalWeight = Math.max(1, weights.reduce((a, b) => a + b, 0));
    const words = [];
    let cursor = start;

    for (let i = 0; i < tokens.length; i++) {
      const isLast = i === tokens.length - 1;
      const slice = isLast ? end - cursor : duration * (weights[i] / totalWeight);
      let wordEnd = Math.min(end, cursor + Math.max(0.03, slice));
      if (wordEnd <= cursor) {
        wordEnd = cursor + 0.03;
      }
      if (isLast || wordEnd > end) {
        wordEnd = end;
      }

      words.push({
        text: tokens[i],
        startSeconds: Number(cursor.toFixed(3)),
        endSeconds: Number(wordEnd.toFixed(3)),
      });
      cursor = wordEnd;
    }

    return words;
  }

  function composeSegmentText(tokens) {
    const parts = [];
    for (const token of tokens) {
      const normalized = normalizeText(token);
      if (!normalized) {
        continue;
      }

      if (parts.length === 0) {
        parts.push(normalized);
        continue;
      }

      if (shouldAttachToPrevious(normalized)) {
        parts[parts.length - 1] = `${parts[parts.length - 1]}${normalized}`;
      } else {
        parts.push(normalized);
      }
    }

    return parts.join(" ");
  }

  function shouldAttachToPrevious(token) {
    if (!token) {
      return false;
    }

    if (/^['’\-–—]/.test(token)) {
      return true;
    }

    return /^[.,!?;:%)\]}]+$/.test(token);
  }

  function normalizeTimestamp(value) {
    if (!Array.isArray(value) || value.length < 2) {
      return null;
    }

    const start = toFiniteNumber(value[0]);
    const end = toFiniteNumber(value[1]);
    return {
      start,
      end,
    };
  }

  function estimateDurationSeconds(text) {
    const normalized = normalizeText(text);
    if (!normalized) {
      return 0.8;
    }

    const chars = normalized.length;
    const words = normalized.split(/\s+/).filter(Boolean).length;
    const byWords = words / 2.8;
    const byChars = chars * 0.052;
    return Math.max(0.8, Math.max(byWords, byChars));
  }

  function estimateWordWeight(token) {
    const normalized = normalizeText(token);
    if (!normalized) {
      return 1;
    }

    const alphaNumeric = (normalized.match(/[A-Za-z0-9]/g) || []).length;
    return Math.min(14, Math.max(1, alphaNumeric));
  }

  function toFiniteNumber(value) {
    const n = Number(value);
    return Number.isFinite(n) ? n : NaN;
  }

  function formatMarkdownLocally(args) {
    const transcript = normalizeText(args.transcript) || "(no speech detected)";
    const model = normalizeText(args.model) || "unknown";
    const language = normalizeText(args.language) || "auto";
    const detected = args.detectedSpeakerCount;

    const lines = [];
    lines.push("# Transcription");
    lines.push("");
    lines.push(`- **Model:** \`${model}\``);
    lines.push(`- **Language:** \`${language}\``);
    if (typeof detected === "number" && detected > 0) {
      lines.push(`- **Detected Speakers:** ${detected}`);
    }
    lines.push("");
    lines.push("## Summary");
    lines.push("- Browser-mode transcript produced locally.");
    lines.push("");
    lines.push("## Action Items");
    lines.push("- []");
    lines.push("");
    lines.push("## Transcript");
    lines.push("");
    lines.push(transcript);

    return lines.join("\n");
  }

  function enforceTranscriptSection(markdown, transcript) {
    const normalizedMarkdown = normalizeText(markdown);
    const normalizedTranscript = normalizeText(transcript) || "(no speech detected)";
    const section = `## Transcript\n\n${normalizedTranscript}`;

    if (!normalizedMarkdown) {
      return section;
    }

    const pattern = /^##\s*Transcript\b[\s\S]*$/im;
    if (pattern.test(normalizedMarkdown)) {
      return normalizedMarkdown.replace(pattern, section);
    }

    return `${normalizedMarkdown}\n\n${section}`;
  }

  function resolveWhisperModel(modelName) {
    const key = normalizeText(modelName).toLowerCase().replace(/\s+/g, "");
    return whisperModelMap[key] || whisperModelMap.smallen;
  }

  async function decodeToMono16kFloat32(base64) {
    const arrayBuffer = base64ToArrayBuffer(base64);
    const AudioContextCtor = window.AudioContext || window.webkitAudioContext;
    if (!AudioContextCtor) {
      throw new Error("AudioContext is not available in this browser.");
    }

    const decodeContext = new AudioContextCtor();
    let decoded;
    try {
      decoded = await decodeContext.decodeAudioData(arrayBuffer.slice(0));
    } finally {
      if (typeof decodeContext.close === "function") {
        await decodeContext.close();
      }
    }

    const targetSampleRate = 16000;
    if (
      decoded.sampleRate === targetSampleRate &&
      decoded.numberOfChannels === 1
    ) {
      return decoded.getChannelData(0);
    }

    const frameCount = Math.ceil(decoded.duration * targetSampleRate);
    const offline = new OfflineAudioContext(1, frameCount, targetSampleRate);
    const source = offline.createBufferSource();
    source.buffer = decoded;
    source.connect(offline.destination);
    source.start(0);
    const rendered = await offline.startRendering();
    return rendered.getChannelData(0);
  }

  function base64ToArrayBuffer(base64) {
    const binary = atob(base64);
    const bytes = new Uint8Array(binary.length);
    for (let i = 0; i < binary.length; i++) {
      bytes[i] = binary.charCodeAt(i);
    }
    return bytes.buffer;
  }

  function normalizeCompletionText(completion) {
    const choices = completion?.choices;
    if (!Array.isArray(choices) || choices.length === 0) {
      return "";
    }

    const message = choices[0]?.message;
    if (!message) return "";

    if (typeof message.content === "string") {
      return normalizeText(message.content);
    }

    if (Array.isArray(message.content)) {
      const text = message.content
        .map((part) => (typeof part?.text === "string" ? part.text : ""))
        .join("\n");
      return normalizeText(text);
    }

    return "";
  }

  function normalizeText(value) {
    return typeof value === "string" ? value.trim().replace(/\r\n/g, "\n") : "";
  }

  function loadSessions() {
    const raw = localStorage.getItem("localtranscriber.sessions.v1");
    if (!raw) {
      return [];
    }

    try {
      const parsed = JSON.parse(raw);
      if (!Array.isArray(parsed)) {
        return [];
      }

      return parsed.sort((a, b) => {
        const aTime = Date.parse(a?.createdAt || "") || 0;
        const bTime = Date.parse(b?.createdAt || "") || 0;
        return bTime - aTime;
      });
    } catch {
      return [];
    }
  }

  function saveSession(session) {
    if (!session || !session.id) {
      return;
    }

    const sessions = loadSessions();
    const next = sessions.filter((x) => x?.id !== session.id);
    next.unshift(session);
    localStorage.setItem("localtranscriber.sessions.v1", JSON.stringify(next.slice(0, 80)));
  }

  function deleteSession(id) {
    if (!id) {
      return;
    }

    const sessions = loadSessions();
    const next = sessions.filter((x) => x?.id !== id);
    localStorage.setItem("localtranscriber.sessions.v1", JSON.stringify(next));
  }

  function downloadText(fileName, content, mimeType) {
    const text = typeof content === "string" ? content : `${content ?? ""}`;
    const safeName = normalizeText(fileName) || "localtranscriber-export.md";
    const type = normalizeText(mimeType) || "text/plain;charset=utf-8";

    const blob = new Blob([text], { type });
    const url = URL.createObjectURL(blob);

    const anchor = document.createElement("a");
    anchor.href = url;
    anchor.download = safeName;
    anchor.style.display = "none";
    document.body.appendChild(anchor);
    anchor.click();
    anchor.remove();

    setTimeout(() => URL.revokeObjectURL(url), 0);
  }

  // Standalone transcription function for workflow engine step handlers.
  // audioInput: { base64, fileName, mimeType }
  async function transcribeAudio(audioInput, model, language, onProgress) {
    const resolvedModel = model || "SmallEn";
    const resolvedLang = language && language.toLowerCase() !== "auto" ? language : undefined;

    if (onProgress) onProgress(5, "Decoding audio...");
    const audioData = await decodeToMono16kFloat32(audioInput.base64);

    if (onProgress) onProgress(20, "Loading Whisper model...");
    const asr = await getWhisperPipeline(resolvedModel, (pct) => {
      if (onProgress) onProgress(20 + pct * 0.3, "Preparing Whisper runtime...");
    });

    if (onProgress) onProgress(50, "Transcribing...");

    let asrResult;
    try {
      asrResult = await asr(audioData, {
        chunk_length_s: 30,
        stride_length_s: 5,
        return_timestamps: "word",
        ...(resolvedLang ? { language: resolvedLang } : {}),
      });
    } catch {
      asrResult = await asr(audioData, {
        chunk_length_s: 30,
        stride_length_s: 5,
        return_timestamps: true,
        ...(resolvedLang ? { language: resolvedLang } : {}),
      });
    }

    const text = normalizeText(asrResult?.text ?? "");
    const segments = buildSubtitleSegments(asrResult?.chunks, text);

    if (onProgress) onProgress(100, "Transcription complete.");
    return { text, segments };
  }

  return {
    // Core functionality
    getCapabilities,
    transcribeInBrowser,

    // Workflow step APIs (used by workflowEngine.js step handlers)
    transcribeAudio,
    buildSpeakerLabeledTranscript,
    formatWithWebLlm,
    
    // Mobile / Memory management
    checkMemoryForModel,
    getRecommendedModels: () => getCapabilities().recommendedModels,
    
    // PWA / Service Worker
    registerServiceWorker,
    clearModelCache,
    getModelCacheSize,
    isPWAInstalled,
    
    // Mirror management (for network issues)
    listMirrors,
    probeMirrors,
    autoSelectMirror,
    getMirrorPreference,
    setMirrorPreference,
    
    // Prompt templates (for tuning output)
    getPromptTemplates,
    setPromptTemplates,
    resetPromptTemplates,
    getDefaultPromptTemplates,
    buildFormattingPrompt,
    
    // Tuning options (formatting behavior)
    getTuningOptions,
    setTuningOptions,
    resetTuningOptions,
    
    // WebLLM
    listWebLlmModels,
    
    // Session storage
    loadSessions,
    saveSession,
    deleteSession,
    downloadText,
    
    // Diagnostics
    async diagnose() {
      console.log("=== LocalTranscriber Diagnostics ===\n");
      
      // 1. Check capabilities
      const caps = getCapabilities();
      console.log("Browser Capabilities:", caps);
      
      // 2. Test direct fetch to each CDN
      const testUrls = [
        { name: "HuggingFace", url: "https://huggingface.co/Xenova/whisper-tiny/resolve/main/config.json" },
        { name: "jsDelivr CDN", url: `${JSDELIVR_BASE}/whisper-tiny/config.json` },
        { name: "HF-Mirror", url: "https://hf-mirror.com/Xenova/whisper-tiny/resolve/main/config.json" },
      ];
      
      console.log("\nTesting direct fetch to CDNs:");
      for (const { name, url } of testUrls) {
        try {
          const start = performance.now();
          const resp = await fetch(url, { mode: "cors" });
          const ms = Math.round(performance.now() - start);
          if (resp.ok) {
            const data = await resp.json();
            console.log(`  ✓ ${name}: ${ms}ms (model: ${data._name_or_path || "ok"})`);
          } else {
            console.log(`  ✗ ${name}: HTTP ${resp.status}`);
          }
        } catch (err) {
          console.log(`  ✗ ${name}: ${err.name} - ${err.message}`);
        }
      }
      
      // 3. Check transformers.js import
      console.log("\nTesting transformers.js import:");
      try {
        const start = performance.now();
        const transformers = await getTransformersModule();
        const ms = Math.round(performance.now() - start);
        console.log(`  ✓ Loaded in ${ms}ms`);
        console.log(`  env.remoteHost: ${transformers.env?.remoteHost || "(default)"}`);
      } catch (err) {
        console.log(`  ✗ Failed: ${err.message}`);
      }
      
      // 4. Mirror probe
      console.log("\nMirror probe results:");
      const probeResults = await probeMirrors(5000);
      for (const r of probeResults) {
        if (r.reachable) {
          console.log(`  ✓ ${r.name}: ${r.latency}ms`);
        } else {
          console.log(`  ✗ ${r.name}: ${r.error || `HTTP ${r.status}`}`);
        }
      }
      
      console.log("\n=== End Diagnostics ===");
      return { capabilities: caps, mirrors: probeResults };
    },

    // Comprehensive stats for UI
    async getStats() {
      const stats = {
        timestamp: Date.now(),
        browser: {},
        storage: {},
        cache: {},
        sessions: {},
        models: {},
        workflows: {},
      };

      // Browser info
      stats.browser = {
        userAgent: navigator.userAgent,
        platform: navigator.platform,
        language: navigator.language,
        cookiesEnabled: navigator.cookieEnabled,
        onLine: navigator.onLine,
        deviceMemory: navigator.deviceMemory || null,
        hardwareConcurrency: navigator.hardwareConcurrency || null,
        maxTouchPoints: navigator.maxTouchPoints || 0,
      };

      // Capabilities
      const caps = getCapabilities();
      stats.browser.webGpu = caps.hasWebGpu;
      stats.browser.audioContext = caps.hasAudioContext;
      stats.browser.mediaRecorder = caps.hasMediaRecorder;
      stats.browser.isMobile = caps.isMobile;
      stats.browser.estimatedMemoryMB = caps.estimatedMemoryMB;

      // Storage estimates
      try {
        if (navigator.storage && navigator.storage.estimate) {
          const estimate = await navigator.storage.estimate();
          stats.storage.quota = estimate.quota || 0;
          stats.storage.usage = estimate.usage || 0;
          stats.storage.usagePercent = estimate.quota ? Math.round((estimate.usage / estimate.quota) * 100) : 0;
        }
      } catch (e) {
        stats.storage.error = e.message;
      }

      // localStorage size
      try {
        let localStorageSize = 0;
        for (let i = 0; i < localStorage.length; i++) {
          const key = localStorage.key(i);
          const value = localStorage.getItem(key);
          localStorageSize += (key.length + value.length) * 2; // UTF-16
        }
        stats.storage.localStorage = localStorageSize;
        stats.storage.localStorageItems = localStorage.length;
      } catch (e) {
        stats.storage.localStorageError = e.message;
      }

      // Model cache (via service worker)
      try {
        stats.cache.modelCacheSize = await getModelCacheSize();
      } catch (e) {
        stats.cache.modelCacheError = e.message;
      }

      // Cached models list
      try {
        if ('caches' in window) {
          const cache = await caches.open('localtranscriber-models-v1');
          const keys = await cache.keys();
          stats.cache.cachedModels = keys.map(req => {
            const url = new URL(req.url);
            // Extract model name from URL
            const match = url.pathname.match(/whisper-([^/]+)/);
            return {
              url: req.url,
              model: match ? `whisper-${match[1]}` : url.pathname.split('/').pop(),
              host: url.host,
            };
          });
          stats.cache.cachedModelCount = keys.length;
        }
      } catch (e) {
        stats.cache.cachedModelsError = e.message;
      }

      // Sessions/history
      try {
        const sessions = loadSessions();
        stats.sessions.count = sessions.length;
        stats.sessions.totalDuration = sessions.reduce((sum, s) => sum + (s.durationMs || 0), 0);
        stats.sessions.recent = sessions.slice(0, 5).map(s => ({
          id: s.id,
          date: s.startedAt,
          model: s.model,
          durationMs: s.durationMs,
          wordCount: s.wordCount,
        }));
        
        // Calculate total words transcribed
        stats.sessions.totalWords = sessions.reduce((sum, s) => sum + (s.wordCount || 0), 0);
      } catch (e) {
        stats.sessions.error = e.message;
      }

      // Workflows
      try {
        if (window.localTranscriberWorkflow) {
          const workflows = window.localTranscriberWorkflow.getWorkflows();
          stats.workflows.count = workflows.length;
          stats.workflows.activeId = window.localTranscriberWorkflow.getActiveWorkflowId();
          stats.workflows.list = workflows.map(w => ({
            id: w.id,
            name: w.name,
            stepCount: w.steps?.length || 0,
          }));
        }
      } catch (e) {
        stats.workflows.error = e.message;
      }

      // Service Worker status
      try {
        if ('serviceWorker' in navigator) {
          const reg = await navigator.serviceWorker.getRegistration();
          stats.serviceWorker = {
            registered: !!reg,
            scope: reg?.scope,
            state: reg?.active?.state,
          };
        }
      } catch (e) {
        stats.serviceWorker = { error: e.message };
      }

      // Preferred mirror
      stats.models.preferredMirror = getMirrorPreference() || 'default';

      return stats;
    },

    // Clear all data
    async clearAllData() {
      const results = { cleared: [], errors: [] };

      // Clear localStorage
      try {
        const keys = [];
        for (let i = 0; i < localStorage.length; i++) {
          const key = localStorage.key(i);
          if (key.startsWith('localtranscriber') || key.startsWith('localTranscriber') || key.startsWith('transformers')) {
            keys.push(key);
          }
        }
        keys.forEach(k => localStorage.removeItem(k));
        results.cleared.push(`localStorage (${keys.length} items)`);
      } catch (e) {
        results.errors.push(`localStorage: ${e.message}`);
      }

      // Clear model cache
      try {
        await clearModelCache();
        results.cleared.push('Model cache');
      } catch (e) {
        results.errors.push(`Model cache: ${e.message}`);
      }

      // Clear app cache
      try {
        if ('caches' in window) {
          const keys = await caches.keys();
          for (const key of keys) {
            if (key.startsWith('localtranscriber')) {
              await caches.delete(key);
              results.cleared.push(`Cache: ${key}`);
            }
          }
        }
      } catch (e) {
        results.errors.push(`Caches: ${e.message}`);
      }

      return results;
    },
  };
})();
