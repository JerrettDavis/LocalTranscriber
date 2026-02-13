using LocalTranscriber.Cli.Models;
using System.Text;
using Whisper.net;
using Whisper.net.Ggml;

namespace LocalTranscriber.Cli.Services;

internal sealed class WhisperTranscriptionService
{
    public async Task<Transcript> TranscribeAsync(
        string wav16kMonoPath,
        string modelName,
        string language,
        int maxSegmentLength,
        bool trustAllCerts = false)
    {
        if (!File.Exists(wav16kMonoPath))
            throw new FileNotFoundException("WAV file not found", wav16kMonoPath);

        var parsedModelName = modelName.Trim();
        if (parsedModelName.Equals("Large", StringComparison.OrdinalIgnoreCase))
            parsedModelName = nameof(GgmlType.LargeV3);

        if (!Enum.TryParse<GgmlType>(parsedModelName, ignoreCase: true, out var ggmlType))
        {
            var valid = string.Join(", ", Enum.GetNames<GgmlType>());
            throw new ArgumentException($"Unknown model '{modelName}'. Valid values: {valid}");
        }

        var modelPath = await ResilientModelDownloader.EnsureModelAsync(ggmlType, trustAllCerts);

        using var whisperFactory = WhisperFactory.FromPath(modelPath);

        var builder = whisperFactory.CreateBuilder()
            .WithLanguage(language)
            .WithTokenTimestamps()
            .SplitOnWord();

        if (maxSegmentLength > 0)
            builder = builder.WithMaxSegmentLength(maxSegmentLength);

        using var processor = builder.Build();

        var fallbackSegments = new List<TranscriptSegment>();
        var words = new List<TranscriptWordTiming>();
        using var fileStream = File.OpenRead(wav16kMonoPath);

        await foreach (var result in processor.ProcessAsync(fileStream))
        {
            var text = NormalizeTokenText(result.Text);
            if (string.IsNullOrWhiteSpace(text))
                continue;

            var start = ClampStart(result.Start);
            var end = ClampEnd(start, result.End);

            fallbackSegments.Add(new TranscriptSegment(start, end, text));

            foreach (var word in ExpandWordSlices(text, start, end))
                AppendWord(words, word);
        }

        var segments = BuildSegmentsFromWords(words);
        if (segments.Count == 0)
            segments = fallbackSegments;

        return new Transcript(modelName, language, segments);
    }

    private static List<TranscriptSegment> BuildSegmentsFromWords(IReadOnlyList<TranscriptWordTiming> words)
    {
        if (words.Count == 0)
            return [];

        var ordered = words
            .Where(w => !string.IsNullOrWhiteSpace(w.Text))
            .OrderBy(w => w.Start)
            .ToArray();
        if (ordered.Length == 0)
            return [];

        var segments = new List<TranscriptSegment>();
        var currentWords = new List<TranscriptWordTiming>(18);
        var currentCharCount = 0;

        for (var i = 0; i < ordered.Length; i++)
        {
            var word = ordered[i];
            if (currentWords.Count > 0)
            {
                var prev = currentWords[^1];
                var gapSeconds = (word.Start - prev.End).TotalSeconds;
                var shouldBreak =
                    gapSeconds > 0.6 ||
                    currentWords.Count >= 18 ||
                    currentCharCount >= 140 ||
                    (EndsSentence(prev.Text) && (gapSeconds >= 0.16 || currentWords.Count >= 10));

                if (shouldBreak)
                {
                    AddSegment(segments, currentWords);
                    currentWords.Clear();
                    currentCharCount = 0;
                }
            }

            currentWords.Add(word);
            currentCharCount += word.Text.Length + 1;
        }

        AddSegment(segments, currentWords);
        return segments;
    }

    private static void AddSegment(List<TranscriptSegment> segments, IReadOnlyList<TranscriptWordTiming> words)
    {
        if (words.Count == 0)
            return;

        var text = BuildSegmentText(words);
        if (string.IsNullOrWhiteSpace(text))
            return;

        var start = words[0].Start;
        var end = ClampEnd(start, words[^1].End);

        segments.Add(new TranscriptSegment(start, end, text, Words: words.ToArray()));
    }

    private static string BuildSegmentText(IReadOnlyList<TranscriptWordTiming> words)
    {
        var sb = new StringBuilder();
        for (var i = 0; i < words.Count; i++)
        {
            var token = NormalizeTokenText(words[i].Text);
            if (token.Length == 0)
                continue;

            if (sb.Length == 0)
            {
                sb.Append(token);
                continue;
            }

            if (ShouldAttachToPrevious(token))
                sb.Append(token);
            else
                sb.Append(' ').Append(token);
        }

        return sb.ToString().Trim();
    }

    private static IEnumerable<TranscriptWordTiming> ExpandWordSlices(string text, TimeSpan start, TimeSpan end)
    {
        var tokens = text
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(NormalizeTokenText)
            .Where(t => t.Length > 0)
            .ToArray();

        if (tokens.Length == 0)
            yield break;

        var boundedStart = ClampStart(start);
        var boundedEnd = ClampEnd(boundedStart, end);
        if (tokens.Length == 1)
        {
            yield return new TranscriptWordTiming(tokens[0], boundedStart, boundedEnd);
            yield break;
        }

        var durationSeconds = Math.Max(0.08, (boundedEnd - boundedStart).TotalSeconds);
        var weights = tokens.Select(EstimateWordWeight).ToArray();
        var totalWeight = Math.Max(1, weights.Sum());
        var cursor = boundedStart;

        for (var i = 0; i < tokens.Length; i++)
        {
            var isLast = i == tokens.Length - 1;
            var sliceSeconds = isLast
                ? Math.Max(0.03, (boundedEnd - cursor).TotalSeconds)
                : durationSeconds * (weights[i] / (double)totalWeight);

            var wordEnd = cursor + TimeSpan.FromSeconds(Math.Max(0.03, sliceSeconds));
            if (wordEnd > boundedEnd || isLast)
                wordEnd = boundedEnd;

            if (wordEnd <= cursor)
                wordEnd = cursor + TimeSpan.FromSeconds(0.03);

            yield return new TranscriptWordTiming(tokens[i], cursor, wordEnd);
            cursor = wordEnd;
        }
    }

    private static void AppendWord(List<TranscriptWordTiming> words, TranscriptWordTiming candidate)
    {
        var text = NormalizeTokenText(candidate.Text);
        if (text.Length == 0)
            return;

        var start = ClampStart(candidate.Start);
        var end = ClampEnd(start, candidate.End);

        if (words.Count > 0)
        {
            var previous = words[^1];
            if (ShouldAttachToPrevious(text))
            {
                words[^1] = previous with
                {
                    Text = $"{previous.Text}{text}",
                    End = end > previous.End ? end : previous.End
                };
                return;
            }

            if (start < previous.End)
                start = previous.End;

            end = ClampEnd(start, end);
        }

        words.Add(new TranscriptWordTiming(text, start, end));
    }

    private static int EstimateWordWeight(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
            return 1;

        var alphaNumeric = token.Count(char.IsLetterOrDigit);
        return Math.Clamp(alphaNumeric, 1, 14);
    }

    private static bool ShouldAttachToPrevious(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
            return false;

        var first = token[0];
        if (first is '\'' or '’' or '-' or '–' or '—')
            return true;

        return token.All(c => char.IsPunctuation(c)) &&
               token.Any(c => c is '.' or ',' or '!' or '?' or ';' or ':' or ')' or ']' or '}' or '%');
    }

    private static bool EndsSentence(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
            return false;

        for (var i = token.Length - 1; i >= 0; i--)
        {
            var c = token[i];
            if (char.IsWhiteSpace(c))
                continue;

            return c is '.' or '!' or '?';
        }

        return false;
    }

    private static TimeSpan ClampStart(TimeSpan value)
        => value < TimeSpan.Zero ? TimeSpan.Zero : value;

    private static TimeSpan ClampEnd(TimeSpan start, TimeSpan value)
    {
        if (value <= start)
            return start + TimeSpan.FromSeconds(0.05);
        return value;
    }

    private static string NormalizeTokenText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        return value.Trim()
            .Replace('\u00A0', ' ')
            .Replace("\r\n", " ")
            .Replace('\n', ' ');
    }

}
