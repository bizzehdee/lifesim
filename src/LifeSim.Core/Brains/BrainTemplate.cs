namespace LifeSim.Core.Brains;

/// <summary>
/// A parsed brain "type": a named list of weighted-preference rules an author writes to bias a
/// seed brain toward a behavioural personality (selfish, fearless, …). It is <em>only a starting
/// point</em> — <see cref="BrainTemplateCompiler"/> turns it into a seed NEAT network with the same
/// topology as a generic genesis brain (author-chosen weights instead of random ones), which then
/// mutates and competes under normal selection. The template itself is never evolved or stored in the
/// live sim; the compiled network is.
/// </summary>
public sealed record BrainTemplate(string Name, IReadOnlyList<BrainRule> Rules);

/// <summary>Whether a rule pushes the organism toward (<see cref="Prefer"/>) or away from (<see cref="Avoid"/>) an action.</summary>
public enum RuleVerb
{
    Prefer,
    Avoid,
}

/// <summary>The magnitude of a rule's bias, scaled to a seed-weight in the compiler.</summary>
public enum RuleStrength
{
    Weak,
    Normal,
    Strong,
}

/// <summary>
/// One authored leaning: "<c>&lt;verb&gt; &lt;target&gt; [when &lt;gate&gt;] [&lt;strength&gt;]</c>".
/// <see cref="Target"/> and <see cref="Gate"/> are vocabulary tokens resolved by
/// <see cref="BrainVocabulary"/> (e.g. <c>HarvestToward(food)</c>, <c>ready</c>). <see cref="Gate"/> is
/// null for an unconditional rule.
/// </summary>
public sealed record BrainRule(RuleVerb Verb, string Target, string? Gate, RuleStrength Strength);
