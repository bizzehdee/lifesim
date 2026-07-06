using System.Collections.Concurrent;
using System.Reflection;

namespace LifeSim.Core.Naming;

/// <summary>
/// An immutable, embedded word list referenced by its version string (lifesim.md §19). A run
/// pins its word-list version in <c>configuration</c>, so a fixed run always replays to
/// identical names even if the shipped default lists are later expanded.
/// </summary>
public sealed class WordList
{
    private static readonly ConcurrentDictionary<string, WordList> Cache = new();

    private readonly string[] _words;

    private WordList(string[] words) => _words = words;

    public int Count => _words.Length;

    public string this[int index] => _words[index];

    /// <summary>Loads (and caches) the embedded word list for the given version, e.g. <c>"adjectives-1"</c>.</summary>
    public static WordList Load(string version) => Cache.GetOrAdd(version, LoadFromResource);

    private static WordList LoadFromResource(string version)
    {
        string resourceName = $"LifeSim.Core.Naming.{version}.txt";
        Assembly assembly = typeof(WordList).Assembly;
        using Stream stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Embedded word list '{resourceName}' not found.");
        using var reader = new StreamReader(stream);

        string[] words = reader.ReadToEnd()
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (words.Length == 0)
        {
            throw new InvalidOperationException($"Embedded word list '{resourceName}' is empty.");
        }

        return new WordList(words);
    }
}
