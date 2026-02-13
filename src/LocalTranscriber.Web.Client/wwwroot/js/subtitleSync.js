window.localTranscriberSubtitleSync = (() => {
  const handlers = new WeakMap();

  function isAudioElement(audio) {
    return (
      !!audio &&
      typeof audio.addEventListener === "function" &&
      typeof audio.removeEventListener === "function" &&
      typeof audio.currentTime !== "undefined"
    );
  }

  async function notify(dotNetRef, audio) {
    if (!dotNetRef || typeof dotNetRef.invokeMethodAsync !== "function") {
      return;
    }

    const currentTime =
      Number.isFinite(audio?.currentTime) && audio.currentTime >= 0
        ? audio.currentTime
        : 0;
    const paused = !!audio?.paused;
    const ended = !!audio?.ended;

    await dotNetRef.invokeMethodAsync("OnSubtitleClock", currentTime, paused, ended);
  }

  function bind(audio, dotNetRef) {
    if (!isAudioElement(audio)) {
      return false;
    }

    dispose(audio);

    const state = {
      disposed: false,
      inFlight: false,
      lastEmitMs: 0,
      rafId: 0,
    };

    const emit = () => {
      if (state.inFlight || state.disposed) {
        return;
      }

      state.inFlight = true;
      notify(dotNetRef, audio)
        .catch(() => {})
        .finally(() => {
          state.inFlight = false;
        });
    };

    const stopRaf = () => {
      if (state.rafId) {
        cancelAnimationFrame(state.rafId);
        state.rafId = 0;
      }
    };

    const tick = (now) => {
      if (state.disposed || audio.paused || audio.ended) {
        stopRaf();
        return;
      }

      if (now - state.lastEmitMs >= 33) {
        state.lastEmitMs = now;
        emit();
      }

      state.rafId = requestAnimationFrame(tick);
    };

    const startRaf = () => {
      if (state.rafId || state.disposed || audio.paused || audio.ended) {
        return;
      }

      state.lastEmitMs = performance.now();
      state.rafId = requestAnimationFrame(tick);
    };

    const onPlay = () => {
      emit();
      startRaf();
    };

    const onPause = () => {
      stopRaf();
      emit();
    };

    const onEnded = () => {
      stopRaf();
      emit();
    };

    audio.addEventListener("play", onPlay);
    audio.addEventListener("pause", onPause);
    audio.addEventListener("timeupdate", emit);
    audio.addEventListener("seeking", emit);
    audio.addEventListener("seeked", emit);
    audio.addEventListener("ended", onEnded);
    audio.addEventListener("ratechange", emit);
    audio.addEventListener("loadedmetadata", emit);

    handlers.set(audio, {
      emit,
      onPlay,
      onPause,
      onEnded,
      stopRaf,
      state,
    });
    emit();
    if (!audio.paused && !audio.ended) {
      startRaf();
    }

    return true;
  }

  function dispose(audio) {
    if (!isAudioElement(audio)) {
      return false;
    }

    const registered = handlers.get(audio);
    if (!registered) {
      return false;
    }

    registered.state.disposed = true;
    registered.stopRaf();

    audio.removeEventListener("play", registered.onPlay);
    audio.removeEventListener("pause", registered.onPause);
    audio.removeEventListener("timeupdate", registered.emit);
    audio.removeEventListener("seeking", registered.emit);
    audio.removeEventListener("seeked", registered.emit);
    audio.removeEventListener("ended", registered.onEnded);
    audio.removeEventListener("ratechange", registered.emit);
    audio.removeEventListener("loadedmetadata", registered.emit);
    handlers.delete(audio);
    return true;
  }

  function seek(audio, seconds) {
    if (!isAudioElement(audio)) {
      return false;
    }

    const target = Number.isFinite(seconds) ? Math.max(0, seconds) : 0;
    audio.currentTime = target;
    return true;
  }

  function scrollToIndex(container, index) {
    if (!container || !Number.isInteger(index) || index < 0) {
      return;
    }

    const line = container.querySelector(`[data-subtitle-index="${index}"]`);
    if (!line) {
      return;
    }

    const prefersReducedMotion =
      typeof window !== "undefined" &&
      typeof window.matchMedia === "function" &&
      window.matchMedia("(prefers-reduced-motion: reduce)").matches;

    line.scrollIntoView({
      block: "center",
      inline: "nearest",
      behavior: prefersReducedMotion ? "auto" : "smooth",
    });
  }

  return {
    bind,
    dispose,
    seek,
    scrollToIndex,
  };
})();
