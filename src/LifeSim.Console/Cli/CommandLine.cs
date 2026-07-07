using System.Globalization;

namespace LifeSim.Console.Cli;

/// <summary>
/// A minimal <c>command --key value --flag</c> parser for the <c>sim</c> CLI (lifesim.md §1). The
/// first non-option token is the command; <c>--key value</c> pairs become options; a <c>--key</c>
/// with no following value (or followed by another option) is a boolean flag.
/// </summary>
public sealed class CommandLine
{
    private readonly Dictionary<string, string> _options;
    private readonly HashSet<string> _flags;

    private CommandLine(string command, Dictionary<string, string> options, HashSet<string> flags)
    {
        Command = command;
        _options = options;
        _flags = flags;
    }

    /// <summary>The subcommand (e.g. <c>run</c>), or empty string if none was given.</summary>
    public string Command { get; }

    public static CommandLine Parse(IReadOnlyList<string> args)
    {
        ArgumentNullException.ThrowIfNull(args);

        string command = string.Empty;
        var options = new Dictionary<string, string>(StringComparer.Ordinal);
        var flags = new HashSet<string>(StringComparer.Ordinal);

        for (int i = 0; i < args.Count; i++)
        {
            string token = args[i];
            if (!token.StartsWith("--", StringComparison.Ordinal))
            {
                if (command.Length == 0)
                {
                    command = token;
                }

                continue;
            }

            string key = token[2..];
            if (i + 1 < args.Count && !args[i + 1].StartsWith("--", StringComparison.Ordinal))
            {
                options[key] = args[++i];
            }
            else
            {
                flags.Add(key);
            }
        }

        return new CommandLine(command, options, flags);
    }

    public bool HasFlag(string key) => _flags.Contains(key);

    public string? GetString(string key) => _options.TryGetValue(key, out string? value) ? value : null;

    public string GetRequired(string key) =>
        GetString(key) ?? throw new CommandLineException($"Missing required option --{key}.");

    public long GetLong(string key, long fallback) =>
        _options.TryGetValue(key, out string? value) ? ParseLong(key, value) : fallback;

    public int GetInt(string key, int fallback) =>
        _options.TryGetValue(key, out string? value) ? (int)ParseLong(key, value) : fallback;

    public ulong GetULong(string key, ulong fallback) =>
        _options.TryGetValue(key, out string? value)
            ? (ulong.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out ulong parsed)
                ? parsed
                : throw new CommandLineException($"--{key} must be a non-negative integer."))
            : fallback;

    public double GetDouble(string key, double fallback) =>
        _options.TryGetValue(key, out string? value)
            ? (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed)
                ? parsed
                : throw new CommandLineException($"--{key} must be a number."))
            : fallback;

    private static long ParseLong(string key, string value) =>
        long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out long parsed)
            ? parsed
            : throw new CommandLineException($"--{key} must be an integer.");
}

/// <summary>Thrown for a malformed command line; the CLI catches it and prints a usage error.</summary>
public sealed class CommandLineException(string message) : Exception(message);
