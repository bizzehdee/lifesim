namespace LifeSim.Core.Brains;

/// <summary>
/// Worked examples of the brain-script language — one seed personality each. They are shipped as
/// <em>editable source text</em>, not hardcoded behaviour: a user is meant to copy one, tweak the
/// rules, and try their own type. Each is only a starting point; once seeded it mutates and competes
/// like any brain, so "which personality wins" is decided by selection, not by these scripts.
/// </summary>
public static class BuiltInBrains
{
    public const string Forager = """
        # Baseline control: seek food and breed, no social behaviour.
        type Forager:
          prefer MoveToward(food)      always
          prefer HarvestToward(food)   always
          prefer HarvestSelf           when hungry
          prefer Reproduce             when ready
          avoid  Share(any)            always
        """;

    public const string Selfish = """
        # Grab food hard, breed eagerly, never give anything away.
        type Selfish:
          prefer HarvestToward(food)   strong
          prefer MoveToward(food)      always
          prefer HarvestSelf           when hungry
          prefer Reproduce             when ready
          avoid  Share(any)            strong
        """;

    public const string Selfless = """
        # Feed itself modestly, but give energy to whoever is nearest — especially when full.
        type Selfless:
          prefer HarvestToward(food)    always
          prefer ShareToward(nearest)   strong
          prefer ShareToward(nearest)   when full
          prefer Reproduce              when ready
          avoid  HarvestToward(nearest) always
        """;

    public const string Cooperator = """
        # Group selection: cluster with kin, share only with kin, never cannibalise kin.
        type Cooperator:
          prefer ShareToward(kin)       strong
          prefer MoveToward(kin)        always
          prefer HarvestToward(food)    always
          prefer Reproduce              when ready
          avoid  HarvestToward(kin)     strong
        """;

    public const string Aggressor = """
        # "Evil": hunt other organisms, preferring smaller prey; take, never give.
        type Aggressor:
          prefer HarvestToward(smaller_neighbour) strong
          prefer MoveToward(nearest)    always
          prefer HarvestToward(nearest) when prey_near
          prefer Reproduce              when ready
          avoid  Share(any)             always
        """;

    public const string Fearless = """
        # Never flees, never idles — closes on the nearest organism and attacks regardless of size.
        type Fearless:
          prefer HarvestToward(nearest) strong
          prefer MoveToward(nearest)    always
          prefer Reproduce              when ready
          avoid  MoveAway(nearest)      always
          avoid  Idle                   always
        """;

    public const string Coward = """
        # Flees anything nearby (more so when threatened), forages the margins, never fights.
        type Coward:
          prefer MoveAway(nearest)      strong
          prefer MoveAway(nearest)      when threatened
          prefer HarvestToward(food)    always
          prefer HarvestSelf            when hungry
          prefer Reproduce              when ready
          avoid  HarvestToward(nearest) always
        """;

    /// <summary>All built-in example scripts, keyed by their type name, in a stable presentation order.</summary>
    public static IReadOnlyList<string> All { get; } =
    [
        Forager, Selfish, Selfless, Cooperator, Aggressor, Fearless, Coward,
    ];
}
