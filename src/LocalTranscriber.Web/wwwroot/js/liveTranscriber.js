window.localTranscriberLive = (() => {
  let recognition = null;
  let dotNetRef = null;
  let finalText = "";
  let interimText = "";
  let shouldRestart = false;

  // Server streaming state
  let serverMode = false;
  let signalRConnection = null;
  let chunkBuffer = [];
  let flushIntervalId = null;
  const FLUSH_INTERVAL_MS = 5000;

  function isSupported() {
    return !!(window.SpeechRecognition || window.webkitSpeechRecognition);
  }

  function start(ref, options) {
    if (recognition) {
      stop();
    }

    const SpeechRecognition = window.SpeechRecognition || window.webkitSpeechRecognition;
    if (!SpeechRecognition) {
      throw new Error("Web Speech API is not supported in this browser.");
    }

    dotNetRef = ref;
    finalText = "";
    interimText = "";
    shouldRestart = true;
    serverMode = false;

    recognition = new SpeechRecognition();
    recognition.continuous = true;
    recognition.interimResults = true;
    recognition.lang = (options && options.language) || "en-US";

    recognition.onresult = (event) => {
      let currentInterim = "";
      for (let i = event.resultIndex; i < event.results.length; i++) {
        const transcript = event.results[i][0].transcript;
        if (event.results[i].isFinal) {
          finalText += transcript + " ";
        } else {
          currentInterim += transcript;
        }
      }
      interimText = currentInterim;
      notifyDotNet(false);
    };

    recognition.onerror = (event) => {
      if (event.error === "no-speech" || event.error === "aborted") {
        return;
      }
      notifyDotNet(false, event.error);
    };

    recognition.onend = () => {
      if (shouldRestart) {
        try {
          recognition.start();
        } catch {
          // Already started or disposed
        }
      }
    };

    recognition.start();
  }

  function stop() {
    shouldRestart = false;

    if (flushIntervalId) {
      clearInterval(flushIntervalId);
      flushIntervalId = null;
    }

    if (serverMode && chunkBuffer.length > 0) {
      _flushChunks();
    }

    if (recognition) {
      try {
        recognition.stop();
      } catch {
        // Already stopped
      }
      recognition = null;
    }

    const result = finalText.trim();
    finalText = "";
    interimText = "";
    dotNetRef = null;
    serverMode = false;
    signalRConnection = null;
    chunkBuffer = [];

    return result;
  }

  function startServer(ref, connection, options) {
    dotNetRef = ref;
    signalRConnection = connection;
    serverMode = true;
    finalText = "";
    interimText = "";
    chunkBuffer = [];
    shouldRestart = false;

    flushIntervalId = setInterval(() => {
      _flushChunks();
    }, FLUSH_INTERVAL_MS);
  }

  function onRecorderChunk(blobChunk) {
    if (!serverMode) {
      return;
    }
    chunkBuffer.push(blobChunk);
  }

  async function _flushChunks() {
    if (chunkBuffer.length === 0 || !signalRConnection) {
      return;
    }

    const chunksToSend = chunkBuffer.splice(0);
    const combined = new Blob(chunksToSend, { type: chunksToSend[0].type || "audio/webm" });

    try {
      const base64 = await blobToBase64(combined);
      await signalRConnection.invoke("SendAudioChunk", base64);
    } catch (err) {
      console.warn("[LiveTranscriber] Failed to flush audio chunks:", err);
    }
  }

  function blobToBase64(blob) {
    return new Promise((resolve, reject) => {
      const reader = new FileReader();
      reader.onloadend = () => {
        const result = reader.result;
        if (typeof result !== "string") {
          reject(new Error("Unable to read blob."));
          return;
        }
        const comma = result.indexOf(",");
        resolve(comma >= 0 ? result.substring(comma + 1) : result);
      };
      reader.onerror = () => reject(reader.error || new Error("Unable to read blob."));
      reader.readAsDataURL(blob);
    });
  }

  function notifyDotNet(isFinal, error) {
    if (!dotNetRef) {
      return;
    }
    try {
      dotNetRef.invokeMethodAsync("OnLiveTranscriptUpdate", {
        finalText: finalText.trim(),
        interimText,
        isFinal: isFinal || false,
        error: error || null,
      });
    } catch {
      // DotNet ref may have been disposed
    }
  }

  function scrollToBottom(el) {
    if (el) el.scrollTop = el.scrollHeight;
  }

  return {
    isSupported,
    start,
    stop,
    startServer,
    onRecorderChunk,
    scrollToBottom,
  };
})();
