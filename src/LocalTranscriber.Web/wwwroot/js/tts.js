/**
 * LocalTranscriber TTS Module
 * Browser-based text-to-speech using Kokoro TTS (82M ONNX model)
 * Lazy-loads from CDN, runs fully client-side via WASM
 */

window.localTranscriberTts = (() => {
  const TTS_SETTINGS_KEY = "localTranscriber_ttsSettings";
  const SAMPLE_RATE = 24000;
  const MAX_CACHE_ENTRIES = 20;

  const defaultSettings = { voice: "af_heart", speed: 1.0 };

  const voices = [
    // American English - Female
    { id: "af_heart", label: "Heart", gender: "female", accent: "American" },
    { id: "af_alloy", label: "Alloy", gender: "female", accent: "American" },
    { id: "af_aoede", label: "Aoede", gender: "female", accent: "American" },
    { id: "af_bella", label: "Bella", gender: "female", accent: "American" },
    { id: "af_jessica", label: "Jessica", gender: "female", accent: "American" },
    { id: "af_kore", label: "Kore", gender: "female", accent: "American" },
    { id: "af_nicole", label: "Nicole", gender: "female", accent: "American" },
    { id: "af_nova", label: "Nova", gender: "female", accent: "American" },
    { id: "af_river", label: "River", gender: "female", accent: "American" },
    { id: "af_sarah", label: "Sarah", gender: "female", accent: "American" },
    { id: "af_sky", label: "Sky", gender: "female", accent: "American" },
    // American English - Male
    { id: "am_adam", label: "Adam", gender: "male", accent: "American" },
    { id: "am_echo", label: "Echo", gender: "male", accent: "American" },
    { id: "am_eric", label: "Eric", gender: "male", accent: "American" },
    { id: "am_liam", label: "Liam", gender: "male", accent: "American" },
    { id: "am_michael", label: "Michael", gender: "male", accent: "American" },
    { id: "am_onyx", label: "Onyx", gender: "male", accent: "American" },
    { id: "am_puck", label: "Puck", gender: "male", accent: "American" },
    { id: "am_santa", label: "Santa", gender: "male", accent: "American" },
    // British English - Female
    { id: "bf_alice", label: "Alice", gender: "female", accent: "British" },
    { id: "bf_emma", label: "Emma", gender: "female", accent: "British" },
    { id: "bf_isabella", label: "Isabella", gender: "female", accent: "British" },
    { id: "bf_lily", label: "Lily", gender: "female", accent: "British" },
    // British English - Male
    { id: "bm_daniel", label: "Daniel", gender: "male", accent: "British" },
    { id: "bm_fable", label: "Fable", gender: "male", accent: "British" },
    { id: "bm_george", label: "George", gender: "male", accent: "British" },
    { id: "bm_lewis", label: "Lewis", gender: "male", accent: "British" },
  ];

  let ttsModulePromise = null;
  let ttsInstance = null;
  let currentSource = null;
  let currentAudioContext = null;
  let speaking = false;

  // Audio cache: key → { audioData, blobUrl, timestamp }
  const audioCache = new Map();

  // ═══════════════════════════════════════════════════════════════
  // Timeout helper (same pattern as browserTranscriber.js)
  // ═══════════════════════════════════════════════════════════════

  function withTimeout(promise, ms, label) {
    return new Promise((resolve, reject) => {
      const timer = setTimeout(() => {
        reject(new Error(`${label} timed out after ${ms}ms. Check your network connection and try again.`));
      }, ms);
      promise.then(
        (val) => { clearTimeout(timer); resolve(val); },
        (err) => { clearTimeout(timer); reject(err); }
      );
    });
  }

  // ═══════════════════════════════════════════════════════════════
  // Lazy Loading
  // ═══════════════════════════════════════════════════════════════

  async function getTtsModule() {
    if (!ttsModulePromise) {
      ttsModulePromise = withTimeout(
        import("https://cdn.jsdelivr.net/npm/kokoro-js@1.1.0/+esm"),
        120000,
        "Kokoro TTS import"
      );
      ttsModulePromise.catch(() => { ttsModulePromise = null; });
    }
    return ttsModulePromise;
  }

  async function ensureTtsInstance(onProgress) {
    if (ttsInstance) return ttsInstance;

    if (onProgress) onProgress(5, "Loading TTS module...");
    const kokoro = await getTtsModule();

    if (onProgress) onProgress(15, "Loading TTS model (~92MB)...");
    ttsInstance = await kokoro.KokoroTTS.from_pretrained(
      "onnx-community/Kokoro-82M-v1.0-ONNX",
      { dtype: "q8" }
    );

    if (onProgress) onProgress(30, "TTS model ready");
    return ttsInstance;
  }

  // ═══════════════════════════════════════════════════════════════
  // Cache Management
  // ═══════════════════════════════════════════════════════════════

  function cacheKey(text, voice, speed) {
    return `${voice}|${speed}|${text}`;
  }

  function getCached(text, voice, speed) {
    const key = cacheKey(text, voice, speed);
    const entry = audioCache.get(key);
    if (entry) {
      entry.timestamp = Date.now();
      return entry;
    }
    return null;
  }

  function putCache(text, voice, speed, audioData, blobUrl) {
    // LRU eviction
    while (audioCache.size >= MAX_CACHE_ENTRIES) {
      let oldest = null;
      let oldestKey = null;
      for (const [k, v] of audioCache) {
        if (!oldest || v.timestamp < oldest.timestamp) {
          oldest = v;
          oldestKey = k;
        }
      }
      if (oldestKey) {
        const evicted = audioCache.get(oldestKey);
        if (evicted?.blobUrl) URL.revokeObjectURL(evicted.blobUrl);
        audioCache.delete(oldestKey);
      }
    }

    audioCache.set(cacheKey(text, voice, speed), {
      audioData,
      blobUrl,
      timestamp: Date.now(),
    });
  }

  // ═══════════════════════════════════════════════════════════════
  // WAV Encoding
  // ═══════════════════════════════════════════════════════════════

  function encodeWav(float32Data, sampleRate) {
    const numChannels = 1;
    const bitsPerSample = 16;
    const byteRate = sampleRate * numChannels * (bitsPerSample / 8);
    const blockAlign = numChannels * (bitsPerSample / 8);
    const dataSize = float32Data.length * (bitsPerSample / 8);
    const headerSize = 44;
    const buffer = new ArrayBuffer(headerSize + dataSize);
    const view = new DataView(buffer);

    // RIFF header
    writeString(view, 0, "RIFF");
    view.setUint32(4, 36 + dataSize, true);
    writeString(view, 8, "WAVE");

    // fmt sub-chunk
    writeString(view, 12, "fmt ");
    view.setUint32(16, 16, true); // sub-chunk size
    view.setUint16(20, 1, true);  // PCM format
    view.setUint16(22, numChannels, true);
    view.setUint32(24, sampleRate, true);
    view.setUint32(28, byteRate, true);
    view.setUint16(32, blockAlign, true);
    view.setUint16(34, bitsPerSample, true);

    // data sub-chunk
    writeString(view, 36, "data");
    view.setUint32(40, dataSize, true);

    // Convert Float32 to Int16
    let offset = headerSize;
    for (let i = 0; i < float32Data.length; i++) {
      const sample = Math.max(-1, Math.min(1, float32Data[i]));
      const int16 = sample < 0 ? sample * 0x8000 : sample * 0x7FFF;
      view.setInt16(offset, int16, true);
      offset += 2;
    }

    return buffer;
  }

  function writeString(view, offset, str) {
    for (let i = 0; i < str.length; i++) {
      view.setUint8(offset + i, str.charCodeAt(i));
    }
  }

  // ═══════════════════════════════════════════════════════════════
  // Synthesis
  // ═══════════════════════════════════════════════════════════════

  async function synthesize(text, options = {}) {
    const settings = getTtsSettings();
    const voice = options.voice || settings.voice;
    const speed = options.speed || settings.speed;

    // Check cache first
    const cached = getCached(text, voice, speed);
    if (cached) return cached.audioData;

    const tts = await ensureTtsInstance(options.onProgress);

    if (options.onProgress) options.onProgress(40, "Synthesizing speech...");
    const result = await tts.generate(text, { voice, speed });

    // result.audio is a Float32Array of PCM samples at 24kHz
    const audioData = result.audio;

    // Create WAV blob URL for caching
    const wavBuffer = encodeWav(audioData, SAMPLE_RATE);
    const blob = new Blob([wavBuffer], { type: "audio/wav" });
    const blobUrl = URL.createObjectURL(blob);

    putCache(text, voice, speed, audioData, blobUrl);

    if (options.onProgress) options.onProgress(100, "Synthesis complete");
    return audioData;
  }

  // ═══════════════════════════════════════════════════════════════
  // Playback
  // ═══════════════════════════════════════════════════════════════

  function stopSpeaking() {
    if (currentSource) {
      try { currentSource.stop(); } catch { /* already stopped */ }
      currentSource = null;
    }
    if (currentAudioContext) {
      try { currentAudioContext.close(); } catch { /* already closed */ }
      currentAudioContext = null;
    }
    speaking = false;
  }

  async function speak(text, options = {}) {
    stopSpeaking();

    const audioData = await synthesize(text, options);

    currentAudioContext = new AudioContext({ sampleRate: SAMPLE_RATE });
    const audioBuffer = currentAudioContext.createBuffer(1, audioData.length, SAMPLE_RATE);
    audioBuffer.getChannelData(0).set(audioData);

    currentSource = currentAudioContext.createBufferSource();
    currentSource.buffer = audioBuffer;
    currentSource.connect(currentAudioContext.destination);

    speaking = true;
    currentSource.onended = () => {
      speaking = false;
      currentSource = null;
      if (currentAudioContext) {
        try { currentAudioContext.close(); } catch { /* ok */ }
        currentAudioContext = null;
      }
    };

    currentSource.start(0);
  }

  function isSpeaking() {
    return speaking;
  }

  // ═══════════════════════════════════════════════════════════════
  // Audio URL Generation (for <audio> elements)
  // ═══════════════════════════════════════════════════════════════

  async function generateAudioUrl(text, options = {}) {
    const settings = getTtsSettings();
    const voice = options.voice || settings.voice;
    const speed = options.speed || settings.speed;

    // Check cache for blob URL
    const cached = getCached(text, voice, speed);
    if (cached?.blobUrl) return cached.blobUrl;

    // Synthesize and cache
    await synthesize(text, options);

    // Now it's cached
    const entry = getCached(text, voice, speed);
    return entry?.blobUrl || null;
  }

  // ═══════════════════════════════════════════════════════════════
  // Settings
  // ═══════════════════════════════════════════════════════════════

  function getTtsSettings() {
    try {
      const stored = localStorage.getItem(TTS_SETTINGS_KEY);
      if (stored) {
        const parsed = JSON.parse(stored);
        return { ...defaultSettings, ...parsed };
      }
    } catch { /* use defaults */ }
    return { ...defaultSettings };
  }

  function setTtsSettings(settings) {
    try {
      const merged = { ...getTtsSettings(), ...settings };
      localStorage.setItem(TTS_SETTINGS_KEY, JSON.stringify(merged));
      return true;
    } catch { return false; }
  }

  function resetTtsSettings() {
    try {
      localStorage.removeItem(TTS_SETTINGS_KEY);
      return true;
    } catch { return false; }
  }

  // ═══════════════════════════════════════════════════════════════
  // Voice List
  // ═══════════════════════════════════════════════════════════════

  function getAvailableVoices() {
    return voices.map(v => ({ ...v }));
  }

  // ═══════════════════════════════════════════════════════════════
  // Public API
  // ═══════════════════════════════════════════════════════════════

  return {
    speak,
    stopSpeaking,
    isSpeaking,
    generateAudioUrl,
    getTtsSettings,
    setTtsSettings,
    resetTtsSettings,
    getAvailableVoices,
  };
})();
