# LocalTranscriber (.NET 10)

A small **local** voice-to-text starter that can:

- Record from a selected Windows capture device (WASAPI)
- Transcribe with Whisper via `Whisper.net` (GPU via CUDA runtime if available)
- Emit a Markdown file with structure
- Optionally polish the Markdown using a local LLM via Ollama

## Prereqs

- Windows 10/11 (recording uses WASAPI)
- .NET 10 SDK installed
- If you want LLM formatting: Ollama running locally
  - Install Ollama and pull a model, e.g.: `ollama pull llama3.2`
- If you want cloud formatting via Hugging Face + Semantic Kernel:
  - Create a Hugging Face token and set `HF_TOKEN`
  - Example (PowerShell): `$env:HF_TOKEN = "hf_xxx"`

## Quick start

From the repo root:

```powershell
cd src\LocalTranscriber.Cli

dotnet restore

dotnet run -- devices

dotnet run -- record --device 0 --out ..\..\output\note.wav

dotnet run -- transcribe --in ..\..\output\note.wav --out ..\..\output\note.md --model SmallEn --format-provider auto

# Optional: use Semantic Kernel + Hugging Face instead of Ollama
dotnet run -- transcribe --in ..\..\output\note.wav --out ..\..\output\note.md --format-provider huggingface --hf-endpoint https://router.huggingface.co --hf-model openai/gpt-oss-20b:groq

# Optional: enable speaker labels quickly
dotnet run -- transcribe --in ..\..\output\note.wav --out ..\..\output\note.md --format-provider local --speakers 2
```

### One-liner: record then transcribe

```powershell
dotnet run -- record-and-transcribe --device 0 --wav ..\..\output\meeting.wav --out ..\..\output\meeting.md --model SmallEn
```

### Blazor UI (record in browser + live progress)

```powershell
cd src\LocalTranscriber.Web
dotnet run
```

Then open the app URL (for example `http://localhost:5078`) and use the recording panel.

### Experimental browser-only mode (WebGPU)

- In the Blazor UI, enable `Run In Browser (Experimental)` when the capability badge reports support.
- Browser mode runs Whisper in-browser (Transformers.js) and can optionally run markdown cleanup via WebLLM.
- Browser mode requires a modern browser with `WebGPU`, `AudioContext`, and `MediaRecorder`.
- WebLLM/Transformers models are fetched by the browser at runtime, so first run can take time and requires internet access.

## Notes

- **Model download**: the first transcription downloads the Whisper GGML model into:
  - `%LOCALAPPDATA%\LocalTranscriber\models`
- **Accuracy vs speed**:
  - `SmallEn` is a very good default.
  - `MediumEn` / `LargeV3` are more accurate but heavier.
- **Segment limit option**:
  - Use `--max-seg-length <int>` to cap segment length (legacy `--max-seg-seconds` is still accepted).
- **Convenience flags**:
  - Use `--format-provider auto|local|ollama|huggingface` instead of juggling multiple boolean switches.
  - Use `--speakers <int>` as a shortcut for speaker labeling.
- **Speaker labeling**:
  - If a speaker count is not supplied by the caller, the app now auto-estimates speaker count and labels speaker swaps heuristically.
  - Speaker labeling is now tunable:
    - `--speaker-sensitivity 0-100` (low = conservative, high = aggressive)
    - `--speaker-min-score-gain`
    - `--speaker-max-switch-rate`
    - `--speaker-min-separation`
    - `--speaker-min-cluster-size`
    - `--speaker-max-auto`
    - `--speaker-global-variance-gate`
    - `--speaker-short-run-merge-seconds`
  - In the Blazor Studio UI, these are exposed in Settings as sliders/advanced numeric controls.
- **Formatting**:
  - `--format-provider auto` tries Ollama first, then Hugging Face (if `HF_TOKEN` / `--hf-api-key` is configured), then local formatting.
  - `--format-provider ollama` or `--format-provider huggingface` force a provider, with local fallback if unavailable.
  - Formatting is tunable:
    - `--format-sensitivity 0-100`
    - `--format-strict-transcript true|false`
    - `--format-overlap-threshold`
    - `--format-summary-min` / `--format-summary-max`
    - `--format-include-action-items true|false`
    - `--format-temperature` / `--format-max-tokens`
    - `--format-local-big-gap` / `--format-local-small-gap`
  - In the Blazor Studio UI, formatter tuning is exposed via slider/toggles and advanced numeric fields.
  - If not, you still get a usable Markdown transcript via a deterministic local formatter.
- **CLI output**:
  - After transcription, the CLI also prints the transcript text to the terminal for quick verification.
- **Web API caching (checksum/signature dedupe)**:
  - The Blazor server endpoint now computes a SHA-256 checksum for uploaded audio.
  - It also computes a request signature from `audio checksum + normalized transcription/format/speaker settings`.
  - Matching submissions are served from persisted cache instead of re-running normalization/transcription/formatting.
  - Cache entries are stored under `src/LocalTranscriber.Web/output/cache`.

## Docker (Cross-Platform)

Run LocalTranscriber anywhere with Docker:

### Quick Start

```bash
# Start web UI with Ollama
docker compose up web

# Or use GPU acceleration (requires NVIDIA Docker runtime)
docker compose --profile cuda up web-cuda
```

### CLI Batch Processing

```bash
# Transcribe a file
docker compose run --rm cli transcribe --in /app/input/meeting.wav --out /app/output/meeting.md

# With GPU
docker compose --profile cli-cuda run --rm cli-cuda transcribe --in /app/input/meeting.wav --out /app/output/meeting.md
```

### Available Images

| Image | Description |
|-------|-------------|
| `local-transcriber:web` | Web UI, CPU-only |
| `local-transcriber:web-cuda` | Web UI with CUDA GPU support |
| `local-transcriber:cli` | CLI, CPU-only |
| `local-transcriber:cli-cuda` | CLI with CUDA GPU support |

### Volumes

- `./models` → `/app/models` — Whisper model cache
- `./output` → `/app/output` — Transcription output
- `./input` → `/app/input` — Input files (CLI only)

### Building Manually

```bash
# CPU version
docker build --target web -t local-transcriber:web .

# CUDA version
docker build --target web --build-arg VARIANT=cuda -t local-transcriber:web-cuda .
```

## Why Whisper.net?

Whisper.net is a .NET wrapper around `whisper.cpp` and supports multiple runtimes (CPU, CUDA, etc.) and a built-in GGML model downloader.

## GitHub Readiness

This repo includes baseline GitHub project automation:

- CI build workflow: `.github/workflows/ci.yml`
- Dependency review on PRs: `.github/workflows/dependency-review.yml`
- PR auto-labeling: `.github/workflows/pr-labeler.yml` + `.github/labeler.yml`
- Label synchronization: `.github/workflows/label-sync.yml` + `.github/labels.yml`
- Release pipeline (tag-driven): `.github/workflows/release.yml`
- Dependabot updates: `.github/dependabot.yml`
- Issue and PR templates under `.github/`

Project governance/docs:

- `CONTRIBUTING.md`
- `CODE_OF_CONDUCT.md`
- `SECURITY.md`
- `SUPPORT.md`
- `CHANGELOG.md`
- `LICENSE`

## First GitHub Setup Checklist

1. Update `.github/ISSUE_TEMPLATE/config.yml` discussion URL (`<owner>/<repo>` placeholder).
2. Update `CODEOWNERS` with real usernames/teams.
3. Push `main`, then run `Label Sync` workflow once to seed labels.
4. Create a tag (for example `v0.1.0`) to validate the release workflow.
