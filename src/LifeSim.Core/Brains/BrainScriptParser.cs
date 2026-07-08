namespace LifeSim.Core.Brains;

/// <summary>
/// Parses the weighted-preference brain-script language into a <see cref="BrainTemplate"/>. The grammar
/// is deliberately tiny and line-oriented:
/// <code>
/// type &lt;Name&gt;:
///   prefer HarvestToward(food)   always
///   prefer Reproduce             when ready
///   prefer HarvestToward(smaller_neighbour) strong
///   avoid  Share(any)            always
///   # blank lines and #-comments are ignored
/// </code>
/// Each rule is "<c>&lt;prefer|avoid&gt; &lt;target&gt; [when &lt;gate&gt;] [weak|strong|always]</c>".
/// Token spellings (targets, gates, directions) are validated against <see cref="BrainVocabulary"/> when
/// the template is compiled; the parser validates shape and reports the offending line.
/// </summary>
public static class BrainScriptParser
{
    public static BrainTemplate ParseTemplate(string script)
    {
        ArgumentNullException.ThrowIfNull(script);

        string? name = null;
        var rules = new List<BrainRule>();
        string[] lines = script.Replace("\r\n", "\n").Split('\n');

        for (int i = 0; i < lines.Length; i++)
        {
            string line = StripComment(lines[i]).Trim();
            if (line.Length == 0)
            {
                continue;
            }

            if (line.StartsWith("type ", StringComparison.OrdinalIgnoreCase))
            {
                if (name is not null)
                {
                    throw new BrainScriptException($"Line {i + 1}: a script defines exactly one 'type'; found a second.");
                }

                name = line[5..].TrimEnd(':').Trim();
                if (name.Length == 0)
                {
                    throw new BrainScriptException($"Line {i + 1}: 'type' needs a name.");
                }

                continue;
            }

            if (name is null)
            {
                throw new BrainScriptException($"Line {i + 1}: rules must come after a 'type <Name>:' header.");
            }

            rules.Add(ParseRule(line, i + 1));
        }

        if (name is null)
        {
            throw new BrainScriptException("Script has no 'type <Name>:' header.");
        }

        if (rules.Count == 0)
        {
            throw new BrainScriptException($"Type '{name}' has no rules.");
        }

        return new BrainTemplate(name, rules);
    }

    private static BrainRule ParseRule(string line, int lineNumber)
    {
        string[] tokens = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length < 2)
        {
            throw new BrainScriptException($"Line {lineNumber}: a rule needs at least '<prefer|avoid> <target>'.");
        }

        RuleVerb verb = tokens[0].ToLowerInvariant() switch
        {
            "prefer" => RuleVerb.Prefer,
            "avoid" => RuleVerb.Avoid,
            _ => throw new BrainScriptException($"Line {lineNumber}: a rule must start with 'prefer' or 'avoid', got '{tokens[0]}'."),
        };

        string target = tokens[1];
        string? gate = null;
        RuleStrength strength = RuleStrength.Normal;

        for (int t = 2; t < tokens.Length; t++)
        {
            string token = tokens[t].ToLowerInvariant();
            switch (token)
            {
                case "when":
                    if (t + 1 >= tokens.Length)
                    {
                        throw new BrainScriptException($"Line {lineNumber}: 'when' must be followed by a gate.");
                    }

                    gate = tokens[++t];
                    break;
                case "weak":
                    strength = RuleStrength.Weak;
                    break;
                case "strong":
                    strength = RuleStrength.Strong;
                    break;
                case "always":
                    // Unconditional marker — normal strength, no gate. Redundant with omitting a gate,
                    // but reads well and matches how authors think ("prefer X always").
                    break;
                default:
                    throw new BrainScriptException($"Line {lineNumber}: unexpected '{tokens[t]}' (expected 'when <gate>', 'weak', 'strong', or 'always').");
            }
        }

        return new BrainRule(verb, target, gate, strength);
    }

    private static string StripComment(string line)
    {
        int hash = line.IndexOf('#');
        return hash < 0 ? line : line[..hash];
    }
}
