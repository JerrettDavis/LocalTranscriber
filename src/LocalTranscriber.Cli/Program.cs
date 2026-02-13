using LocalTranscriber.Cli.Services;
using System.Globalization;

namespace LocalTranscriber.Cli;

internal static class Program
{
    private static readonly string AppName = "localtranscriber";

    private enum FormatProvider
    {
        Auto,
        Local,
        Ollama,
        HuggingFace
    }

    public static async Task<int> Main(string[] args)
    {
        if (args.Length == 0 || args[0] is "-h" or "--help" or "help")
        {
            PrintHelp();
            return 0;
        }

        var command = args[0].ToLowerInvariant();
        var opts = SimpleArgs.Parse(args.Skip(1).ToArray());

        try
        {
            return command switch
            {
                "devices" => await DevicesAsync(),
                "mirrors" => MirrorsCommand(opts),
                "record" => await RecordAsync(opts),
                "transcribe" => await TranscribeAsync(opts),
                "record-and-transcribe" => await RecordAndTranscribeAsync(opts),
                _ => Unknown()
            };
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[error] {ex.Message}");
            Console.Error.WriteLine(ex);
            return 1;
        }
    }

    private static int Unknown()
    {
        Console.Error.WriteLine("Unknown command. Run 'localtranscriber help'.");
        return 2;
    }

    private static void PrintHelp()
    {
        Console.WriteLine($"{AppName} - local recording + Whisper transcription + optional LLM markdown formatting\n");
        Console.WriteLine("Commands:");
        Console.WriteLine("  devices");
        Console.WriteLine("      List available audio capture devices (Windows).");
        Console.WriteLine("\n  mirrors [--mirror-url <url>]");
        Console.WriteLine("      List available model download mirrors and their status.");
        Console.WriteLine("\n  record --device <index> --out <path.wav> [--loopback true|false]");
        Console.WriteLine("      Record audio until you press ENTER.");
        Console.WriteLine("\n  transcribe --in <path.(wav|mp3|m4a|...)> --out <path.md> [options]");
        Console.WriteLine("      Transcribe an existing audio file to a formatted markdown file.");
        Console.WriteLine("\n  record-and-transcribe --device <index> --wav <path.wav> --out <path.md> [options]");
        Console.WriteLine("      Convenience: record to a wav and then transcribe it.");
        Console.WriteLine("\nTranscribe options:");
        Console.WriteLine("  --model <Tiny|TinyEn|Base|BaseEn|Small|SmallEn|Medium|MediumEn|LargeV1|LargeV2|LargeV3|LargeV3Turbo> (default: SmallEn)");
        Console.WriteLine("  --language <auto|en|...>                                                      (default: auto)");
        Console.WriteLine("  --max-seg-length <int>                                                        (default: 0, means whisper default)");
        Console.WriteLine("  --format-provider <auto|local|ollama|huggingface>                            (default: auto)");
        Console.WriteLine("  --format-sensitivity <0-100>                                                  (default: 50)");
        Console.WriteLine("  --format-strict-transcript true|false                                         (default: true)");
        Console.WriteLine("  --format-overlap-threshold <double>                                           (default: derived from sensitivity)");
        Console.WriteLine("  --format-summary-min <int>                                                    (default: 3)");
        Console.WriteLine("  --format-summary-max <int>                                                    (default: 8)");
        Console.WriteLine("  --format-include-action-items true|false                                      (default: true)");
        Console.WriteLine("  --format-temperature <double>                                                 (default: derived from sensitivity)");
        Console.WriteLine("  --format-max-tokens <int>                                                     (default: 1200)");
        Console.WriteLine("  --format-local-big-gap <double>                                               (default: derived from sensitivity)");
        Console.WriteLine("  --format-local-small-gap <double>                                             (default: derived from sensitivity)");
        Console.WriteLine("  --speakers <int>                                                              (implies --label-speakers true)");
        Console.WriteLine("  --label-speakers true|false                                                   (default: false)");
        Console.WriteLine("  --speaker-count <int>                                                         (default: auto-detect)");
        Console.WriteLine("  --speaker-sensitivity <0-100>                                                 (default: 25; low=conservative)");
        Console.WriteLine("  --speaker-min-score-gain <double>                                             (default: derived from sensitivity)");
        Console.WriteLine("  --speaker-max-switch-rate <double>                                            (default: derived from sensitivity)");
        Console.WriteLine("  --speaker-min-separation <double>                                             (default: derived from sensitivity)");
        Console.WriteLine("  --speaker-min-cluster-size <int>                                              (default: derived from sensitivity)");
        Console.WriteLine("  --speaker-max-auto <int>                                                      (default: derived from sensitivity)");
        Console.WriteLine("  --speaker-global-variance-gate <double>                                       (default: derived from sensitivity)");
        Console.WriteLine("  --speaker-short-run-merge-seconds <double>                                    (default: derived from sensitivity)");
        Console.WriteLine("  --format-with-ollama true|false                                               (legacy)");
        Console.WriteLine("  --format-with-huggingface true|false                                          (legacy)");
        Console.WriteLine("  --ollama-uri http://localhost:11434                                           (default: http://localhost:11434)");
        Console.WriteLine("  --ollama-model mistral-nemo:12b                                               (default: mistral-nemo:12b)");
        Console.WriteLine("  --hf-endpoint https://router.huggingface.co                                   (default: same)");
        Console.WriteLine("  --hf-model Qwen/Qwen2.5-14B-Instruct                                          (default: same)");
        Console.WriteLine("  --hf-api-key <token>                                                          (default: HF_TOKEN env var)");
        Console.WriteLine("\nModel mirror options:");
        Console.WriteLine("  --mirror <name>                                                               (use specific mirror: HuggingFace, HF-Mirror, ModelScope, GitHub)");
        Console.WriteLine("  --mirror-url <url>                                                            (use custom mirror URL, e.g., internal server)");
        Console.WriteLine("\nNetwork options:");
        Console.WriteLine("  --trust-all-certs true|false                                                  (default: false; use for enterprise SSL inspection)");
        Console.WriteLine("\nExamples:");
        Console.WriteLine("  localtranscriber devices");
        Console.WriteLine("  localtranscriber record --device 0 --out output\\mic.wav");
        Console.WriteLine("  localtranscriber transcribe --in output\\mic.wav --out output\\mic.md --model SmallEn");
        Console.WriteLine("  localtranscriber transcribe --in output\\mic.wav --out output\\mic.md --format-provider auto --speakers 2");
        Console.WriteLine("  localtranscriber record-and-transcribe --device 0 --wav output\\note.wav --out output\\note.md");
    }

    private static Task<int> DevicesAsync()
    {
        var audio = new AudioRecordingService();
        var devices = audio.ListCaptureDevices();

        if (devices.Count == 0)
        {
            Console.WriteLine("No capture devices found.");
            return Task.FromResult(0);
        }

        Console.WriteLine("Capture devices:");
        foreach (var d in devices)
            Console.WriteLine($"  [{d.Index}] {d.FriendlyName}");

        Console.WriteLine();
        Console.WriteLine("Tip: Use --loopback true to record 'what you hear' (system output) instead of a mic.");
        return Task.FromResult(0);
    }

    private static int MirrorsCommand(SimpleArgs opts)
    {
        var customUrl = opts.GetString("mirror-url");
        
        Console.WriteLine("Available model mirrors:\n");
        ResilientModelDownloader.PrintMirrorStatus(customUrl);
        
        Console.WriteLine();
        Console.WriteLine("Usage:");
        Console.WriteLine("  --mirror <name>       Use a specific mirror by name");
        Console.WriteLine("  --mirror-url <url>    Use a custom URL (takes highest priority)");
        Console.WriteLine("  WHISPER_MODEL_MIRROR  Environment variable for custom URL");
        Console.WriteLine();
        Console.WriteLine("Mirrors are tried in priority order. Custom URL (if set) is always tried first.");
        
        return 0;
    }

    private static async Task<int> RecordAsync(SimpleArgs opts)
    {
        var deviceIndex = opts.GetInt("device") ?? throw new ArgumentException("Missing --device <index>");
        var outPath = opts.GetString("out") ?? throw new ArgumentException("Missing --out <path.wav>");
        var loopback = opts.GetBool("loopback") ?? false;

        var audio = new AudioRecordingService();
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outPath))!);

        Console.WriteLine($"Recording to: {outPath}");
        Console.WriteLine("Press ENTER to stop...");

        using var cts = new CancellationTokenSource();
        var recordTask = audio.RecordToWavAsync(deviceIndex, outPath, loopback, cts.Token);
        Console.ReadLine();
        cts.Cancel();

        await recordTask;

        Console.WriteLine("Done.");
        return 0;
    }

    private static async Task<int> TranscribeAsync(SimpleArgs opts)
    {
        var input = opts.GetString("in") ?? throw new ArgumentException("Missing --in <path>");
        var output = opts.GetString("out") ?? throw new ArgumentException("Missing --out <path.md>");

        var model = opts.GetString("model") ?? "SmallEn";
        var language = opts.GetString("language") ?? "auto";
        var maxSegLength = opts.GetInt("max-seg-length")
            ?? opts.GetInt("max-seg-seconds") // legacy alias
            ?? 0;

        var speakersOverride = opts.GetInt("speakers");
        var labelSpeakers = (opts.GetBool("label-speakers") ?? false) || speakersOverride.HasValue;
        var speakerCount = speakersOverride ?? opts.GetInt("speaker-count") ?? 0;
        if (speakerCount < 0)
            throw new ArgumentException("Speaker count must be >= 0");
        var speakerOptions = ParseSpeakerOptions(opts);

        var trustAllCerts = opts.GetBool("trust-all-certs") ?? false;
        var mirrorName = opts.GetString("mirror");
        var mirrorUrl = opts.GetString("mirror-url");
        var downloadOptions = new ResilientModelDownloader.DownloadOptions(
            MirrorName: mirrorName,
            MirrorUrl: mirrorUrl,
            TrustAllCerts: trustAllCerts);

        var formatProvider = ResolveFormatProvider(opts);
        var ollamaUri = opts.GetString("ollama-uri") ?? "http://localhost:11434";
        var ollamaModel = opts.GetString("ollama-model") ?? "mistral-nemo:12b";
        var hfEndpoint = opts.GetString("hf-endpoint") ?? "https://router.huggingface.co";
        var hfModel = opts.GetString("hf-model") ?? "Qwen/Qwen2.5-14B-Instruct";
        var hfApiKey = opts.GetString("hf-api-key") ?? Environment.GetEnvironmentVariable("HF_TOKEN");
        var formatterOptions = ParseFormatterOptions(opts);

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);

        var normalizer = new AudioNormalizeService();
        var whisper = new WhisperTranscriptionService();
        var speakers = new SpeakerLabelService();
        var formatter = new MarkdownFormatterService();
        var ollama = new OllamaFormattingService();
        var huggingFace = new HuggingFaceFormattingService();

        var normalized = await normalizer.Ensure16kMonoWavAsync(input);
        Console.WriteLine($"Normalized audio: {normalized}");

        var transcript = await whisper.TranscribeAsync(normalized, model, language, maxSegLength, downloadOptions);

        if (labelSpeakers)
        {
            var labelMode = speakerCount == 0 ? "auto speaker count" : $"target speakers: {speakerCount}";
            Console.WriteLine(
                $"Applying speaker labels ({labelMode}, sensitivity: {speakerOptions.Sensitivity})...");
            transcript = speakers.LabelSpeakers(transcript, normalized, speakerCount, speakerOptions);
        }

        var markdown = formatter.FormatBasicMarkdown(transcript, formatterOptions);
        var rendered = false;

        if ((formatProvider is FormatProvider.Auto or FormatProvider.Ollama) && await ollama.IsHealthyAsync(new Uri(ollamaUri)))
        {
            Console.WriteLine($"Formatting with Ollama ({ollamaModel})...");
            markdown = await ollama.FormatToMarkdownAsync(new Uri(ollamaUri), ollamaModel, transcript, formatterOptions);
            rendered = true;
        }
        else if (formatProvider == FormatProvider.Ollama)
        {
            Console.WriteLine("Ollama formatting requested but Ollama is unavailable.");
        }

        if (!rendered && (formatProvider is FormatProvider.Auto or FormatProvider.HuggingFace))
        {
            if (huggingFace.IsConfigured(hfApiKey))
            {
                Console.WriteLine($"Formatting with Hugging Face ({hfModel}) via Semantic Kernel...");
                try
                {
                    markdown = await huggingFace.FormatToMarkdownAsync(
                        new Uri(hfEndpoint),
                        hfModel,
                        hfApiKey!,
                        transcript,
                        formatterOptions);
                    rendered = true;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Hugging Face formatting unavailable ({ex.Message}).");
                }
            }
            else if (formatProvider == FormatProvider.HuggingFace)
            {
                Console.WriteLine("Hugging Face formatting requested but no API key is configured (use --hf-api-key or HF_TOKEN).");
            }
        }

        if (!rendered)
            Console.WriteLine("Formatting locally...");

        await File.WriteAllTextAsync(output, markdown);
        Console.WriteLine($"Wrote: {output}");
        Console.WriteLine();
        Console.WriteLine("Transcript:");
        if (transcript.Segments.Count == 0)
        {
            Console.WriteLine("(no speech segments detected)");
        }
        else
        {
            Console.WriteLine(transcript.PromptText);
        }

        return 0;
    }

    private static async Task<int> RecordAndTranscribeAsync(SimpleArgs opts)
    {
        var deviceIndex = opts.GetInt("device") ?? throw new ArgumentException("Missing --device <index>");
        var wav = opts.GetString("wav") ?? throw new ArgumentException("Missing --wav <path.wav>");
        var output = opts.GetString("out") ?? throw new ArgumentException("Missing --out <path.md>");
        var loopback = opts.GetBool("loopback") ?? false;

        var audio = new AudioRecordingService();
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(wav))!);

        Console.WriteLine($"Recording to: {wav}");
        Console.WriteLine("Press ENTER to stop...");

        using var cts = new CancellationTokenSource();
        var recordTask = audio.RecordToWavAsync(deviceIndex, wav, loopback, cts.Token);
        Console.ReadLine();
        cts.Cancel();
        await recordTask;

        var transcribeArgs = new Dictionary<string, string?>
        {
            ["in"] = wav,
            ["out"] = output,
            ["model"] = opts.GetString("model") ?? "SmallEn",
            ["language"] = opts.GetString("language") ?? "auto",
            ["max-seg-length"] = (opts.GetInt("max-seg-length") ?? opts.GetInt("max-seg-seconds") ?? 0)
                .ToString(CultureInfo.InvariantCulture),
            ["ollama-uri"] = opts.GetString("ollama-uri") ?? "http://localhost:11434",
            ["ollama-model"] = opts.GetString("ollama-model") ?? "mistral-nemo:12b",
            ["hf-endpoint"] = opts.GetString("hf-endpoint") ?? "https://router.huggingface.co",
            ["hf-model"] = opts.GetString("hf-model") ?? "Qwen/Qwen2.5-14B-Instruct",
            ["hf-api-key"] = opts.GetString("hf-api-key") ?? Environment.GetEnvironmentVariable("HF_TOKEN"),
        };

        CopyOptionalArg(opts, transcribeArgs, "label-speakers");
        CopyOptionalArg(opts, transcribeArgs, "speaker-count");
        CopyOptionalArg(opts, transcribeArgs, "speakers");
        CopyOptionalArg(opts, transcribeArgs, "speaker-sensitivity");
        CopyOptionalArg(opts, transcribeArgs, "speaker-min-score-gain");
        CopyOptionalArg(opts, transcribeArgs, "speaker-max-switch-rate");
        CopyOptionalArg(opts, transcribeArgs, "speaker-min-separation");
        CopyOptionalArg(opts, transcribeArgs, "speaker-min-cluster-size");
        CopyOptionalArg(opts, transcribeArgs, "speaker-max-auto");
        CopyOptionalArg(opts, transcribeArgs, "speaker-global-variance-gate");
        CopyOptionalArg(opts, transcribeArgs, "speaker-short-run-merge-seconds");
        CopyOptionalArg(opts, transcribeArgs, "format-provider");
        CopyOptionalArg(opts, transcribeArgs, "format-with-ollama");
        CopyOptionalArg(opts, transcribeArgs, "format-with-huggingface");
        CopyOptionalArg(opts, transcribeArgs, "format-sensitivity");
        CopyOptionalArg(opts, transcribeArgs, "format-strict-transcript");
        CopyOptionalArg(opts, transcribeArgs, "format-overlap-threshold");
        CopyOptionalArg(opts, transcribeArgs, "format-summary-min");
        CopyOptionalArg(opts, transcribeArgs, "format-summary-max");
        CopyOptionalArg(opts, transcribeArgs, "format-include-action-items");
        CopyOptionalArg(opts, transcribeArgs, "format-temperature");
        CopyOptionalArg(opts, transcribeArgs, "format-max-tokens");
        CopyOptionalArg(opts, transcribeArgs, "format-local-big-gap");
        CopyOptionalArg(opts, transcribeArgs, "format-local-small-gap");
        CopyOptionalArg(opts, transcribeArgs, "trust-all-certs");
        CopyOptionalArg(opts, transcribeArgs, "mirror");
        CopyOptionalArg(opts, transcribeArgs, "mirror-url");

        return await TranscribeAsync(SimpleArgs.FromDictionary(transcribeArgs));
    }

    private static void CopyOptionalArg(SimpleArgs source, Dictionary<string, string?> destination, string key)
    {
        if (source.Has(key))
            destination[key] = source.GetString(key);
    }

    private static FormatProvider ResolveFormatProvider(SimpleArgs opts)
    {
        var provider = opts.GetString("format-provider");
        if (!string.IsNullOrWhiteSpace(provider))
            return ParseFormatProvider(provider);

        var usesLegacyFlags = opts.Has("format-with-ollama") || opts.Has("format-with-huggingface");
        if (!usesLegacyFlags)
            return FormatProvider.Auto;

        var formatWithOllama = opts.GetBool("format-with-ollama") ?? false;
        var formatWithHuggingFace = opts.GetBool("format-with-huggingface") ?? false;

        if (formatWithOllama && formatWithHuggingFace)
            return FormatProvider.Auto;
        if (formatWithOllama)
            return FormatProvider.Ollama;
        if (formatWithHuggingFace)
            return FormatProvider.HuggingFace;

        return FormatProvider.Local;
    }

    private static FormatProvider ParseFormatProvider(string value)
        => value.Trim().ToLowerInvariant() switch
        {
            "auto" => FormatProvider.Auto,
            "local" or "none" => FormatProvider.Local,
            "ollama" => FormatProvider.Ollama,
            "huggingface" or "hf" => FormatProvider.HuggingFace,
            _ => throw new ArgumentException(
                $"Unknown --format-provider '{value}'. Expected one of: auto, local, ollama, huggingface.")
        };

    private static SpeakerLabelingOptions ParseSpeakerOptions(SimpleArgs opts)
    {
        var sensitivity = Math.Clamp(opts.GetInt("speaker-sensitivity") ?? 25, 0, 100);
        var minClusterSize = opts.GetInt("speaker-min-cluster-size");
        if (minClusterSize is < 1)
            throw new ArgumentException("--speaker-min-cluster-size must be >= 1");

        var maxAuto = opts.GetInt("speaker-max-auto");
        if (maxAuto is < 1)
            throw new ArgumentException("--speaker-max-auto must be >= 1");

        return new SpeakerLabelingOptions(
            Sensitivity: sensitivity,
            MinScoreGainForSplit: opts.GetDouble("speaker-min-score-gain"),
            MaxSwitchRateForSplit: opts.GetDouble("speaker-max-switch-rate"),
            MinClusterSeparation: opts.GetDouble("speaker-min-separation"),
            MinClusterSize: minClusterSize,
            MaxAutoSpeakers: maxAuto,
            GlobalVarianceGate: opts.GetDouble("speaker-global-variance-gate"),
            ShortRunMergeSeconds: opts.GetDouble("speaker-short-run-merge-seconds"));
    }

    private static FormatterTuningOptions ParseFormatterOptions(SimpleArgs opts)
    {
        var sensitivity = Math.Clamp(opts.GetInt("format-sensitivity") ?? 50, 0, 100);
        var strictTranscript = opts.GetBool("format-strict-transcript") ?? true;
        var summaryMin = opts.GetInt("format-summary-min");
        var summaryMax = opts.GetInt("format-summary-max");
        var includeActionItems = opts.GetBool("format-include-action-items") ?? true;
        var maxTokens = opts.GetInt("format-max-tokens");

        if (summaryMin is < 1)
            throw new ArgumentException("--format-summary-min must be >= 1");
        if (summaryMax is < 1)
            throw new ArgumentException("--format-summary-max must be >= 1");
        if (summaryMin.HasValue && summaryMax.HasValue && summaryMin.Value > summaryMax.Value)
            throw new ArgumentException("--format-summary-min must be <= --format-summary-max");
        if (maxTokens is < 1)
            throw new ArgumentException("--format-max-tokens must be >= 1");

        return new FormatterTuningOptions(
            Sensitivity: sensitivity,
            StrictTranscript: strictTranscript,
            OverlapThreshold: opts.GetDouble("format-overlap-threshold"),
            SummaryMinBullets: summaryMin,
            SummaryMaxBullets: summaryMax,
            IncludeActionItems: includeActionItems,
            Temperature: opts.GetDouble("format-temperature"),
            MaxTokens: maxTokens,
            LocalBigGapSeconds: opts.GetDouble("format-local-big-gap"),
            LocalSmallGapSeconds: opts.GetDouble("format-local-small-gap"));
    }
}
