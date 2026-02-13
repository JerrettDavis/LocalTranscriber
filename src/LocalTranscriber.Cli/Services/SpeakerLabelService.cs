using LocalTranscriber.Cli.Models;
using NAudio.Wave;

namespace LocalTranscriber.Cli.Services;

internal sealed class SpeakerLabelService
{
    public Transcript LabelSpeakers(
        Transcript transcript,
        string wav16kMonoPath,
        int speakerCount = 0,
        SpeakerLabelingOptions? options = null)
    {
        if (transcript.Segments.Count == 0)
            return transcript;

        if (!File.Exists(wav16kMonoPath))
            throw new FileNotFoundException("WAV file not found for speaker labeling", wav16kMonoPath);

        var tuned = (options ?? new SpeakerLabelingOptions()).Normalized();

        var features = ExtractFeatures(wav16kMonoPath, transcript.Segments);
        var smoothedFeatures = SmoothFeatures(features);
        var maxCandidateSpeakers = Math.Min(
            tuned.EffectiveMaxAutoSpeakers,
            Math.Max(1, transcript.Segments.Count / 2));

        var normalizedCount = speakerCount > 0
            ? Math.Clamp(speakerCount, 1, maxCandidateSpeakers)
            : EstimateSpeakerCount(smoothedFeatures, transcript.Segments, maxCandidateSpeakers, tuned);

        var normalized = Normalize(smoothedFeatures);
        var labels = ClusterNormalized(normalized, normalizedCount);
        labels = PostProcessLabels(
            labels,
            normalized,
            transcript.Segments,
            tuned,
            preserveSpeakerCount: speakerCount > 0);

        if (speakerCount <= 0 && ShouldCollapseToSingleSpeaker(labels, normalized, tuned))
            labels = Enumerable.Repeat(0, labels.Length).ToArray();

        RemapByFirstAppearance(labels);

        var labeled = transcript.Segments
            .Select((s, i) => s with { Speaker = $"Speaker {labels[i] + 1}" })
            .ToList();

        return transcript with { Segments = labeled };
    }

    private static int EstimateSpeakerCount(
        IReadOnlyList<double[]> features,
        IReadOnlyList<TranscriptSegment> segments,
        int maxCandidateSpeakers,
        SpeakerLabelingOptions options)
    {
        if (features.Count <= 1 || maxCandidateSpeakers <= 1)
            return 1;

        var normalized = Normalize(features);
        var globalVariance = EstimateGlobalVariance(normalized);
        if (features.Count <= 6 || globalVariance < options.EffectiveGlobalVarianceGate)
            return 1;

        var kOneScore = ScoreClustering(normalized, segments, 1, options);
        var best = kOneScore;

        for (var k = 2; k <= maxCandidateSpeakers; k++)
        {
            var candidate = ScoreClustering(normalized, segments, k, options);
            if (candidate.Score > best.Score)
                best = candidate;
        }

        // Require meaningful confidence uplift before splitting from one speaker.
        if (best.K > 1 && best.Score - kOneScore.Score < options.EffectiveMinScoreGainForSplit)
            return 1;

        // High switching frequency on short clips is usually false multi-speaker detection.
        if (best.K > 1 && best.SwitchRate > options.EffectiveMaxSwitchRateForSplit)
            return 1;

        return best.K;
    }

    private static ScoreResult ScoreClustering(
        IReadOnlyList<double[]> normalized,
        IReadOnlyList<TranscriptSegment> segments,
        int k,
        SpeakerLabelingOptions options)
    {
        var labels = ClusterNormalized(normalized, k);
        var silhouette = k == 1 ? -0.25 : CalculateSilhouetteScore(normalized, labels, k);
        var clusterCounts = BuildClusterCounts(labels, k);
        var singletonClusters = clusterCounts.Count(c => c < 2);
        var minNonZeroClusterSize = clusterCounts.Where(c => c > 0).DefaultIfEmpty(1).Min();
        var imbalance = clusterCounts.Max() / (double)Math.Max(1, minNonZeroClusterSize);
        var switchRate = CalculateSwitchRate(labels);
        var avgDuration = AverageSegmentSeconds(segments);

        var complexityPenalty = (k - 1) * options.EffectiveComplexityPenaltyPerSpeaker;
        var singletonPenalty = singletonClusters * options.EffectiveSingletonClusterPenalty;
        var imbalancePenalty = Math.Max(0, imbalance - 2.2) * options.EffectiveImbalancePenaltyFactor;
        var switchPenalty = switchRate * (avgDuration < 2.6
            ? options.EffectiveSwitchPenaltyShort
            : options.EffectiveSwitchPenaltyLong);

        var score = silhouette - complexityPenalty - singletonPenalty - imbalancePenalty - switchPenalty;
        return new ScoreResult(k, score, switchRate);
    }

    private static List<double[]> ExtractFeatures(string wavPath, IReadOnlyList<TranscriptSegment> segments)
    {
        using var reader = new WaveFileReader(wavPath);
        var sampleProvider = reader.ToSampleProvider();
        var sampleRate = sampleProvider.WaveFormat.SampleRate;
        var samples = ReadAllSamples(sampleProvider);

        return segments
            .Select(s => BuildFeatureVector(samples, sampleRate, s.Start, s.End))
            .ToList();
    }

    private static float[] ReadAllSamples(ISampleProvider provider)
    {
        var chunk = new float[provider.WaveFormat.SampleRate * provider.WaveFormat.Channels];
        var all = new List<float>(chunk.Length * 4);

        int read;
        while ((read = provider.Read(chunk, 0, chunk.Length)) > 0)
            all.AddRange(chunk.Take(read));

        return all.ToArray();
    }

    private static double[] BuildFeatureVector(float[] samples, int sampleRate, TimeSpan start, TimeSpan end)
    {
        var startIdx = Math.Clamp((int)(start.TotalSeconds * sampleRate), 0, samples.Length);
        var endIdx = Math.Clamp((int)(end.TotalSeconds * sampleRate), 0, samples.Length);

        if (endIdx <= startIdx)
            endIdx = Math.Min(samples.Length, startIdx + (sampleRate / 2));

        // Expand tiny windows so features stay stable.
        if (endIdx - startIdx < sampleRate / 4)
        {
            var pad = sampleRate / 8;
            startIdx = Math.Max(0, startIdx - pad);
            endIdx = Math.Min(samples.Length, endIdx + pad);
        }

        var length = endIdx - startIdx;
        if (length <= 16)
            return [0, 0, 0, 0];

        double energy = 0;
        double derivative = 0;
        var zeroCrossings = 0;
        var prev = samples[startIdx];

        for (var i = startIdx; i < endIdx; i++)
        {
            var v = samples[i];
            energy += v * v;

            if (i > startIdx)
            {
                derivative += Math.Abs(v - prev);
                if ((prev >= 0 && v < 0) || (prev < 0 && v >= 0))
                    zeroCrossings++;
            }

            prev = v;
        }

        var rms = Math.Sqrt(energy / length);
        var zcr = zeroCrossings / (double)Math.Max(1, length - 1);
        var flux = derivative / Math.Max(1, length - 1);
        var pitch = EstimatePitch(samples, startIdx, endIdx, sampleRate);

        return [Math.Log10(rms + 1e-6), zcr, pitch, flux];
    }

    private static double EstimatePitch(float[] samples, int startIdx, int endIdx, int sampleRate)
    {
        var minLag = sampleRate / 350;
        var maxLag = sampleRate / 70;
        var size = endIdx - startIdx;
        if (size <= maxLag + 8)
            return 0;

        var bestCorr = 0d;
        var bestLag = 0;

        for (var lag = minLag; lag <= maxLag; lag++)
        {
            double corr = 0;
            double normA = 0;
            double normB = 0;

            for (var i = startIdx; i < endIdx - lag; i++)
            {
                var a = samples[i];
                var b = samples[i + lag];
                corr += a * b;
                normA += a * a;
                normB += b * b;
            }

            var denom = Math.Sqrt(normA * normB) + 1e-9;
            var score = corr / denom;
            if (score > bestCorr)
            {
                bestCorr = score;
                bestLag = lag;
            }
        }

        if (bestCorr < 0.15 || bestLag == 0)
            return 0;

        return sampleRate / (double)bestLag;
    }

    private static int[] ClusterNormalized(IReadOnlyList<double[]> normalized, int k)
    {
        var pointCount = normalized.Count;
        var dim = normalized[0].Length;

        var centroids = new double[k][];
        for (var c = 0; c < k; c++)
        {
            var idx = k == 1
                ? 0
                : (int)Math.Round(c * (pointCount - 1) / (double)(k - 1));
            centroids[c] = (double[])normalized[idx].Clone();
        }

        var labels = Enumerable.Repeat(-1, pointCount).ToArray();

        for (var iteration = 0; iteration < 25; iteration++)
        {
            var changed = false;

            for (var i = 0; i < pointCount; i++)
            {
                var bestCluster = 0;
                var bestDistance = DistanceSquared(normalized[i], centroids[0]);
                for (var c = 1; c < k; c++)
                {
                    var d = DistanceSquared(normalized[i], centroids[c]);
                    if (d < bestDistance)
                    {
                        bestDistance = d;
                        bestCluster = c;
                    }
                }

                if (labels[i] != bestCluster)
                {
                    labels[i] = bestCluster;
                    changed = true;
                }
            }

            var sums = new double[k][];
            var counts = new int[k];
            for (var c = 0; c < k; c++)
                sums[c] = new double[dim];

            for (var i = 0; i < pointCount; i++)
            {
                var label = labels[i];
                counts[label]++;
                for (var d = 0; d < dim; d++)
                    sums[label][d] += normalized[i][d];
            }

            for (var c = 0; c < k; c++)
            {
                if (counts[c] == 0)
                {
                    centroids[c] = (double[])normalized[(c * pointCount) / k].Clone();
                    continue;
                }

                for (var d = 0; d < dim; d++)
                    centroids[c][d] = sums[c][d] / counts[c];
            }

            if (!changed)
                break;
        }

        return labels;
    }

    private static List<double[]> Normalize(IReadOnlyList<double[]> features)
    {
        var count = features.Count;
        var dim = features[0].Length;
        var means = new double[dim];
        var stdDevs = new double[dim];

        foreach (var f in features)
        {
            for (var d = 0; d < dim; d++)
                means[d] += f[d];
        }

        for (var d = 0; d < dim; d++)
            means[d] /= count;

        foreach (var f in features)
        {
            for (var d = 0; d < dim; d++)
            {
                var diff = f[d] - means[d];
                stdDevs[d] += diff * diff;
            }
        }

        for (var d = 0; d < dim; d++)
            stdDevs[d] = Math.Sqrt(stdDevs[d] / Math.Max(1, count - 1)) + 1e-9;

        return features
            .Select(f =>
            {
                var v = new double[dim];
                for (var d = 0; d < dim; d++)
                    v[d] = (f[d] - means[d]) / stdDevs[d];
                return v;
            })
            .ToList();
    }

    private static List<double[]> SmoothFeatures(IReadOnlyList<double[]> features)
    {
        if (features.Count <= 2)
            return features.Select(v => (double[])v.Clone()).ToList();

        var dim = features[0].Length;
        var smoothed = new List<double[]>(features.Count);

        for (var i = 0; i < features.Count; i++)
        {
            var from = Math.Max(0, i - 1);
            var to = Math.Min(features.Count - 1, i + 1);
            var span = (to - from) + 1;
            var avg = new double[dim];

            for (var idx = from; idx <= to; idx++)
            {
                for (var d = 0; d < dim; d++)
                    avg[d] += features[idx][d];
            }

            for (var d = 0; d < dim; d++)
                avg[d] /= span;

            smoothed.Add(avg);
        }

        return smoothed;
    }

    private static double DistanceSquared(double[] a, double[] b)
    {
        var sum = 0d;
        for (var i = 0; i < a.Length; i++)
        {
            var diff = a[i] - b[i];
            sum += diff * diff;
        }
        return sum;
    }

    private static double CalculateSilhouetteScore(IReadOnlyList<double[]> normalized, int[] labels, int k)
    {
        var clusters = Enumerable.Range(0, k)
            .Select(_ => new List<int>())
            .ToArray();

        for (var i = 0; i < labels.Length; i++)
            clusters[labels[i]].Add(i);

        double total = 0;
        var count = 0;

        for (var i = 0; i < normalized.Count; i++)
        {
            var label = labels[i];
            var ownCluster = clusters[label];

            if (ownCluster.Count <= 1)
                continue;

            var a = AverageDistanceToCluster(normalized, i, ownCluster);
            var b = double.PositiveInfinity;

            for (var c = 0; c < clusters.Length; c++)
            {
                if (c == label || clusters[c].Count == 0)
                    continue;

                var candidate = AverageDistanceToCluster(normalized, i, clusters[c]);
                if (candidate < b)
                    b = candidate;
            }

            if (!double.IsFinite(b))
                continue;

            var denominator = Math.Max(a, b);
            var s = denominator <= 1e-9 ? 0 : (b - a) / denominator;

            total += s;
            count++;
        }

        return count == 0 ? -0.25 : total / count;
    }

    private static double AverageDistanceToCluster(IReadOnlyList<double[]> normalized, int pointIndex, IReadOnlyList<int> clusterPoints)
    {
        double sum = 0;
        var count = 0;

        for (var i = 0; i < clusterPoints.Count; i++)
        {
            var other = clusterPoints[i];
            if (other == pointIndex)
                continue;

            sum += Math.Sqrt(DistanceSquared(normalized[pointIndex], normalized[other]));
            count++;
        }

        return count == 0 ? 0 : sum / count;
    }

    private static void RemapByFirstAppearance(int[] labels)
    {
        var remap = new Dictionary<int, int>();
        var next = 0;

        for (var i = 0; i < labels.Length; i++)
        {
            if (!remap.TryGetValue(labels[i], out var mapped))
            {
                mapped = next++;
                remap[labels[i]] = mapped;
            }

            labels[i] = mapped;
        }
    }

    private static int[] PostProcessLabels(
        int[] labels,
        IReadOnlyList<double[]> normalized,
        IReadOnlyList<TranscriptSegment> segments,
        SpeakerLabelingOptions options,
        bool preserveSpeakerCount)
    {
        if (labels.Length <= 2)
            return labels;

        var result = (int[])labels.Clone();

        SmoothSingleSegmentIslands(result);
        MergeTinyClusters(
            result,
            normalized,
            preserveSpeakerCount
                ? 1
                : Math.Max(1, options.EffectiveMinClusterSize));

        if (!preserveSpeakerCount)
            MergeShortRuns(result, segments, normalized, options.EffectiveShortRunMergeSeconds);

        SmoothSingleSegmentIslands(result);

        return result;
    }

    private static void SmoothSingleSegmentIslands(int[] labels)
    {
        for (var i = 1; i < labels.Length - 1; i++)
        {
            if (labels[i - 1] == labels[i + 1] && labels[i] != labels[i - 1])
                labels[i] = labels[i - 1];
        }
    }

    private static void MergeTinyClusters(int[] labels, IReadOnlyList<double[]> normalized, int minCount)
    {
        var distinct = labels.Distinct().ToList();
        if (distinct.Count <= 1)
            return;

        var counts = distinct.ToDictionary(x => x, x => labels.Count(v => v == x));
        var valid = counts.Where(kv => kv.Value >= minCount).Select(kv => kv.Key).ToHashSet();
        if (valid.Count == 0)
            return;

        var centroids = BuildCentroids(labels, normalized);

        for (var i = 0; i < labels.Length; i++)
        {
            if (counts[labels[i]] >= minCount)
                continue;

            var replacement = valid
                .OrderBy(cluster => DistanceSquared(normalized[i], centroids[cluster]))
                .First();
            labels[i] = replacement;
        }
    }

    private static void MergeShortRuns(
        int[] labels,
        IReadOnlyList<TranscriptSegment> segments,
        IReadOnlyList<double[]> normalized,
        double shortRunMergeSeconds)
    {
        if (labels.Length <= 2)
            return;

        var centroids = BuildCentroids(labels, normalized);
        var i = 0;
        while (i < labels.Length)
        {
            var j = i;
            while (j + 1 < labels.Length && labels[j + 1] == labels[i])
                j++;

            var runLength = (j - i) + 1;
            var duration = TimeSpan.Zero;
            for (var idx = i; idx <= j && idx < segments.Count; idx++)
                duration += segments[idx].End - segments[idx].Start;

            if (runLength == 1 && duration.TotalSeconds < shortRunMergeSeconds)
            {
                var left = i > 0 ? labels[i - 1] : (int?)null;
                var right = j < labels.Length - 1 ? labels[j + 1] : (int?)null;

                if (left.HasValue && right.HasValue && left.Value == right.Value)
                {
                    labels[i] = left.Value;
                }
                else if (left.HasValue || right.HasValue)
                {
                    var candidates = new[] { left, right }
                        .Where(x => x.HasValue)
                        .Select(x => x!.Value)
                        .Distinct()
                        .ToList();

                    var nearest = candidates
                        .OrderBy(cluster => DistanceSquared(normalized[i], centroids[cluster]))
                        .First();
                    labels[i] = nearest;
                }
            }

            i = j + 1;
        }
    }

    private static Dictionary<int, double[]> BuildCentroids(int[] labels, IReadOnlyList<double[]> normalized)
    {
        var dim = normalized[0].Length;
        var groups = new Dictionary<int, (double[] Sum, int Count)>();

        for (var i = 0; i < labels.Length; i++)
        {
            var label = labels[i];
            if (!groups.TryGetValue(label, out var item))
                item = (new double[dim], 0);

            for (var d = 0; d < dim; d++)
                item.Sum[d] += normalized[i][d];
            item.Count++;
            groups[label] = item;
        }

        var centroids = new Dictionary<int, double[]>(groups.Count);
        foreach (var (cluster, data) in groups)
        {
            var mean = new double[dim];
            for (var d = 0; d < dim; d++)
                mean[d] = data.Sum[d] / Math.Max(1, data.Count);
            centroids[cluster] = mean;
        }

        return centroids;
    }

    private static int[] BuildClusterCounts(int[] labels, int k)
    {
        var counts = new int[k];
        for (var i = 0; i < labels.Length; i++)
        {
            if (labels[i] >= 0 && labels[i] < k)
                counts[labels[i]]++;
        }

        return counts;
    }

    private static double CalculateSwitchRate(int[] labels)
    {
        if (labels.Length <= 1)
            return 0;

        var switches = 0;
        for (var i = 1; i < labels.Length; i++)
        {
            if (labels[i] != labels[i - 1])
                switches++;
        }

        return switches / (double)(labels.Length - 1);
    }

    private static double AverageSegmentSeconds(IReadOnlyList<TranscriptSegment> segments)
    {
        if (segments.Count == 0)
            return 0;

        var total = 0d;
        for (var i = 0; i < segments.Count; i++)
        {
            total += Math.Max(0, (segments[i].End - segments[i].Start).TotalSeconds);
        }

        return total / segments.Count;
    }

    private static double EstimateGlobalVariance(IReadOnlyList<double[]> normalized)
    {
        if (normalized.Count <= 1)
            return 0;

        var sum = 0d;
        var comparisons = 0;
        for (var i = 0; i < normalized.Count; i++)
        {
            for (var j = i + 1; j < normalized.Count; j++)
            {
                sum += Math.Sqrt(DistanceSquared(normalized[i], normalized[j]));
                comparisons++;
            }
        }

        return comparisons == 0 ? 0 : sum / comparisons;
    }

    private static bool ShouldCollapseToSingleSpeaker(
        int[] labels,
        IReadOnlyList<double[]> normalized,
        SpeakerLabelingOptions options)
    {
        var distinctCount = labels.Distinct().Count();
        if (distinctCount <= 1)
            return true;

        var switchRate = CalculateSwitchRate(labels);
        if (switchRate > options.EffectiveMaxSwitchRateForSplit)
            return true;

        var centroids = BuildCentroids(labels, normalized);
        var keys = centroids.Keys.ToList();
        if (keys.Count <= 1)
            return true;

        double minSeparation = double.PositiveInfinity;
        for (var i = 0; i < keys.Count; i++)
        {
            for (var j = i + 1; j < keys.Count; j++)
            {
                var sep = Math.Sqrt(DistanceSquared(centroids[keys[i]], centroids[keys[j]]));
                if (sep < minSeparation)
                    minSeparation = sep;
            }
        }

        // If clusters are close in feature space, multi-speaker labeling is likely noise.
        return minSeparation < options.EffectiveMinClusterSeparation;
    }

    private sealed record ScoreResult(int K, double Score, double SwitchRate);
}
