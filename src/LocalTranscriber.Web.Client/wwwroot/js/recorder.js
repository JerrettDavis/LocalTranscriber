window.localTranscriberRecorder = (() => {
  let mediaStream = null;
  let mediaRecorder = null;
  let chunks = [];
  let activePreviewUrl = null;

  async function ensureMicPermission() {
    const stream = await navigator.mediaDevices.getUserMedia({ audio: true });
    stream.getTracks().forEach((t) => t.stop());
  }

  async function listInputDevices() {
    try {
      // Try to get permission first for proper device labels
      await ensureMicPermission();
    } catch {
      // Permission denied or unavailable - continue with limited labels
    }
    const devices = await navigator.mediaDevices.enumerateDevices();
    return devices
      .filter((d) => d.kind === "audioinput")
      .map((d, i) => ({
        deviceId: d.deviceId,
        label: d.label || `Microphone ${i + 1}`,
      }));
  }

  async function startRecording(deviceId) {
    if (mediaRecorder && mediaRecorder.state !== "inactive") {
      throw new Error("Recording is already in progress.");
    }

    const constraints = deviceId
      ? { audio: { deviceId: { exact: deviceId } } }
      : { audio: true };

    try {
      mediaStream = await navigator.mediaDevices.getUserMedia(constraints);
      chunks = [];
      mediaRecorder = new MediaRecorder(mediaStream);
      mediaRecorder.ondataavailable = (event) => {
        if (event.data && event.data.size > 0) {
          chunks.push(event.data);
        }
      };
      mediaRecorder.start(250);
    } catch (error) {
      cleanup();
      throw error;
    }
  }

  async function stopRecording() {
    if (!mediaRecorder) {
      cleanup();
      return null;
    }

    if (mediaRecorder.state === "inactive") {
      if (chunks.length > 0) {
        const sourceBlob = new Blob(chunks, { type: mediaRecorder.mimeType || "audio/webm" });
        const wavBlob = await convertToWav(sourceBlob);
        const payload = await blobToPayload(wavBlob, "recording.wav");

        if (activePreviewUrl) {
          URL.revokeObjectURL(activePreviewUrl);
        }

        activePreviewUrl = URL.createObjectURL(wavBlob);
        payload.previewUrl = activePreviewUrl;
        cleanup();
        return payload;
      }

      cleanup();
      return null;
    }

    return await new Promise((resolve, reject) => {
      const recorder = mediaRecorder;
      let settled = false;
      const timeoutId = setTimeout(() => {
        if (settled) {
          return;
        }

        settled = true;
        cleanup();
        resolve(null);
      }, 6000);

      const finishResolve = (value) => {
        if (settled) {
          return;
        }

        settled = true;
        clearTimeout(timeoutId);
        resolve(value);
      };

      const finishReject = (error) => {
        if (settled) {
          return;
        }

        settled = true;
        clearTimeout(timeoutId);
        reject(error);
      };

      recorder.onerror = (event) => finishReject(event.error || new Error("Recorder error"));
      recorder.onstop = async () => {
        try {
          const sourceBlob = new Blob(chunks, { type: recorder.mimeType || "audio/webm" });
          const payload = await blobToPayload(
            sourceBlob,
            fileNameForMime(sourceBlob.type || recorder.mimeType || "audio/webm"));

          if (activePreviewUrl) {
            URL.revokeObjectURL(activePreviewUrl);
          }

          activePreviewUrl = URL.createObjectURL(sourceBlob);
          payload.previewUrl = activePreviewUrl;

          cleanup();
          finishResolve(payload);
        } catch (error) {
          cleanup();
          finishReject(error);
        }
      };

      try {
        recorder.stop();
      } catch (error) {
        cleanup();
        finishReject(error);
      }
    });
  }

  function clearPreview() {
    if (activePreviewUrl) {
      URL.revokeObjectURL(activePreviewUrl);
      activePreviewUrl = null;
    }
  }

  function isRecording() {
    return !!mediaRecorder && mediaRecorder.state !== "inactive";
  }

  function cleanup() {
    if (mediaStream) {
      mediaStream.getTracks().forEach((t) => t.stop());
      mediaStream = null;
    }
    mediaRecorder = null;
    chunks = [];
  }

  function resetRecorderState() {
    cleanup();
  }

  async function blobToPayload(blob, fileName) {
    const base64 = await blobToBase64(blob);
    return {
      fileName,
      mimeType: blob.type || "audio/wav",
      size: blob.size,
      base64,
      previewUrl: null,
    };
  }

  function fileNameForMime(mimeType) {
    const type = `${mimeType || ""}`.toLowerCase();
    if (type.includes("wav")) return "recording.wav";
    if (type.includes("mp4")) return "recording.m4a";
    if (type.includes("ogg")) return "recording.ogg";
    if (type.includes("mpeg") || type.includes("mp3")) return "recording.mp3";
    return "recording.webm";
  }

  async function convertToWav(blob) {
    try {
      const data = await blob.arrayBuffer();
      const audioContext = new AudioContext();
      const audioBuffer = await audioContext.decodeAudioData(data);
      const wavBytes = encodeWav(audioBuffer);
      await audioContext.close();
      return new Blob([wavBytes], { type: "audio/wav" });
    } catch {
      // Fall back to original blob if browser decode/encode isn't available.
      return blob;
    }
  }

  function encodeWav(audioBuffer) {
    const numChannels = Math.min(audioBuffer.numberOfChannels, 2);
    const sampleRate = audioBuffer.sampleRate;
    const samplesPerChannel = audioBuffer.length;
    const bytesPerSample = 2;
    const blockAlign = numChannels * bytesPerSample;
    const byteRate = sampleRate * blockAlign;
    const dataLength = samplesPerChannel * blockAlign;

    const buffer = new ArrayBuffer(44 + dataLength);
    const view = new DataView(buffer);

    writeAscii(view, 0, "RIFF");
    view.setUint32(4, 36 + dataLength, true);
    writeAscii(view, 8, "WAVE");
    writeAscii(view, 12, "fmt ");
    view.setUint32(16, 16, true); // PCM chunk size
    view.setUint16(20, 1, true); // PCM
    view.setUint16(22, numChannels, true);
    view.setUint32(24, sampleRate, true);
    view.setUint32(28, byteRate, true);
    view.setUint16(32, blockAlign, true);
    view.setUint16(34, 16, true); // bits per sample
    writeAscii(view, 36, "data");
    view.setUint32(40, dataLength, true);

    let offset = 44;
    const channels = [];
    for (let c = 0; c < numChannels; c++) {
      channels.push(audioBuffer.getChannelData(c));
    }

    for (let i = 0; i < samplesPerChannel; i++) {
      for (let c = 0; c < numChannels; c++) {
        const sample = Math.max(-1, Math.min(1, channels[c][i]));
        const value = sample < 0 ? sample * 0x8000 : sample * 0x7fff;
        view.setInt16(offset, value, true);
        offset += 2;
      }
    }

    return buffer;
  }

  function writeAscii(view, offset, text) {
    for (let i = 0; i < text.length; i++) {
      view.setUint8(offset + i, text.charCodeAt(i));
    }
  }

  function blobToBase64(blob) {
    return new Promise((resolve, reject) => {
      const reader = new FileReader();
      reader.onloadend = () => {
        const result = reader.result;
        if (typeof result !== "string") {
          reject(new Error("Unable to read recording blob."));
          return;
        }

        const comma = result.indexOf(",");
        resolve(comma >= 0 ? result.substring(comma + 1) : result);
      };
      reader.onerror = () => reject(reader.error || new Error("Unable to read recording blob."));
      reader.readAsDataURL(blob);
    });
  }

  return {
    listInputDevices,
    startRecording,
    stopRecording,
    clearPreview,
    isRecording,
    resetRecorderState,
  };
})();
