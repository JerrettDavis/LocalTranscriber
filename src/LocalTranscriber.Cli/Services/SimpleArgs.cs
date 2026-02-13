namespace LocalTranscriber.Cli.Services;

/// <summary>
/// Tiny args parser: --key value OR --flag true/false.
///
/// Examples:
///   record --device 0 --out file.wav --loopback true
/// </summary>
internal sealed class SimpleArgs
{
    private readonly Dictionary<string, string?> _values;

    private SimpleArgs(Dictionary<string, string?> values)
        => _values = values;

    public static SimpleArgs Parse(string[] args)
    {
        var dict = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

        for (var i = 0; i < args.Length; i++)
        {
            var token = args[i];
            if (!token.StartsWith("--", StringComparison.Ordinal))
                continue;

            var key = token[2..];
            string? value = null;

            if (i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal))
            {
                value = args[i + 1];
                i++;
            }
            else
            {
                // allow --flag without a value => true
                value = "true";
            }

            dict[key] = value;
        }

        return new SimpleArgs(dict);
    }

    public static SimpleArgs FromDictionary(Dictionary<string, string?> dict) => new(dict);

    public bool Has(string key) => _values.ContainsKey(key);

    public string? GetString(string key) => _values.TryGetValue(key, out var v) ? v : null;

    public int? GetInt(string key)
        => int.TryParse(GetString(key), out var v) ? v : null;

    public double? GetDouble(string key)
        => double.TryParse(GetString(key), out var v) ? v : null;

    public bool? GetBool(string key)
        => bool.TryParse(GetString(key), out var v) ? v : null;
}
