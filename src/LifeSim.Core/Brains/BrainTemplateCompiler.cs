using LifeSim.Core.Neat;
using LifeSim.Core.Organisms;
using LifeSim.Core.Sensing;

namespace LifeSim.Core.Brains;

/// <summary>Thrown when a brain script references a word or shape the vocabulary doesn't know.</summary>
public sealed class BrainScriptException(string message) : Exception(message);

/// <summary>
/// Compiles a <see cref="BrainTemplate"/> into a <em>seed</em> <see cref="NeatGenome"/>: the exact same
/// input→output topology and innovation ids as a generic genesis brain
/// (<see cref="NeatGenomeFactory.CreateMinimalFullyConnected"/>), but with author-chosen weights in
/// place of random ones. Because the topology is identical, the seed drops straight into the existing
/// evolution, serialization, and determinism machinery — from tick 0 it mutates and competes like any
/// other brain. Unspecified connections are left at weight 0 (present and evolvable, just neutral).
/// </summary>
public static class BrainTemplateCompiler
{
    // A rule of unit strength contributes this weight; strengths scale it. Kept a few times the
    // genesis [-1,1] range so an authored leaning is clearly expressed at birth without saturating.
    private const double BaseWeight = 3.0;

    // A gate that also modulates a directional macro contributes a gentler flat bias than the
    // direction wiring itself, so it colours the behaviour without overriding the aim.
    private const double GateModulationFactor = 0.5;

    public static NeatGenome Compile(BrainTemplate template)
    {
        ArgumentNullException.ThrowIfNull(template);

        var weights = new Dictionary<(int Input, int Output), double>();
        foreach (BrainRule rule in template.Rules)
        {
            ApplyRule(rule, weights);
        }

        var nodes = new List<NodeGene>(NeatTopology.InputCount + NeatTopology.OutputCount);
        foreach (long id in NeatTopology.InputNodeIds)
        {
            nodes.Add(new NodeGene { Id = id, Type = NodeType.Input });
        }

        foreach (long id in NeatTopology.OutputNodeIds)
        {
            nodes.Add(new NodeGene { Id = id, Type = NodeType.Output });
        }

        var connections = new List<ConnectionGene>(NeatTopology.InputCount * NeatTopology.OutputCount);
        for (int i = 0; i < NeatTopology.InputCount; i++)
        {
            for (int j = 0; j < NeatTopology.OutputCount; j++)
            {
                connections.Add(new ConnectionGene
                {
                    InnovationId = NeatTopology.ConnectionInnovationId(i, j),
                    From = NeatTopology.InputNodeIds[i],
                    To = NeatTopology.OutputNodeIds[j],
                    Weight = weights.GetValueOrDefault((i, j), 0.0),
                    Enabled = true,
                });
            }
        }

        return new NeatGenome { Nodes = nodes, Connections = connections };
    }

    private static void ApplyRule(BrainRule rule, Dictionary<(int, int), double> weights)
    {
        double magnitude = (rule.Verb == RuleVerb.Avoid ? -1.0 : 1.0) * StrengthScale(rule.Strength) * BaseWeight;

        (bool isDirectional, BrainVocabulary.ActionFamily family, int polarity, string? directionArg, OrganismAction plainAction, bool isGroup)
            = ResolveTarget(rule.Target);

        if (!isDirectional)
        {
            // Plain action (Reproduce/Idle/HarvestSelf): the gate is its only driver (or the pseudo-bias).
            BrainVocabulary.Gate gate = ResolveGate(rule.Gate ?? "always");
            Add(weights, (int)gate.Field, (int)plainAction, magnitude * gate.Sign);
            return;
        }

        int[] familyActions = [(int)family.North, (int)family.South, (int)family.East, (int)family.West];

        if (isGroup)
        {
            // A whole family driven flatly by the gate — e.g. "avoid Share(any) always".
            BrainVocabulary.Gate gate = ResolveGate(rule.Gate ?? "always");
            foreach (int action in familyActions)
            {
                Add(weights, (int)gate.Field, action, magnitude * gate.Sign);
            }

            return;
        }

        // Directional macro (…Toward/…Away(<dir>)): wire the direction vector into the family so the
        // action pointing the right way gets boosted, then apply any neighbour-kind skew.
        BrainVocabulary.DirectionSource dir = ResolveDirection(directionArg!);
        Add(weights, (int)dir.X, (int)family.East, magnitude * polarity);
        Add(weights, (int)dir.X, (int)family.West, -magnitude * polarity);
        Add(weights, (int)dir.Y, (int)family.South, magnitude * polarity);
        Add(weights, (int)dir.Y, (int)family.North, -magnitude * polarity);

        if (dir.Extra is SensoryField extra)
        {
            foreach (int action in familyActions)
            {
                Add(weights, (int)extra, action, magnitude * polarity * dir.ExtraSign);
            }
        }

        // An explicit gate on a directional macro colours it (e.g. "…Away(nearest) when threatened").
        if (rule.Gate is not null)
        {
            BrainVocabulary.Gate gate = ResolveGate(rule.Gate);
            foreach (int action in familyActions)
            {
                Add(weights, (int)gate.Field, action, magnitude * gate.Sign * GateModulationFactor);
            }
        }
    }

    private static (bool IsDirectional, BrainVocabulary.ActionFamily Family, int Polarity, string? DirectionArg, OrganismAction PlainAction, bool IsGroup)
        ResolveTarget(string target)
    {
        string token = target.Trim();
        string lower = token.ToLowerInvariant();

        if (BrainVocabulary.PlainActions.TryGetValue(lower, out OrganismAction plain))
        {
            return (false, default!, 0, null, plain, false);
        }

        int open = lower.IndexOf('(');
        if (open < 0 || !lower.EndsWith(')'))
        {
            throw new BrainScriptException($"Unknown target '{token}'.");
        }

        string macro = lower[..open];
        string arg = token[(open + 1)..^1].Trim();

        (BrainVocabulary.ActionFamily family, int polarity, bool isGroup, bool needsDir) = macro switch
        {
            "movetoward" => (BrainVocabulary.MoveFamily, 1, false, true),
            "moveaway" => (BrainVocabulary.MoveFamily, -1, false, true),
            "harvesttoward" => (BrainVocabulary.HarvestFamily, 1, false, true),
            "sharetoward" => (BrainVocabulary.ShareFamily, 1, false, true),
            "move" => (BrainVocabulary.MoveFamily, 1, true, false),
            "harvest" => (BrainVocabulary.HarvestFamily, 1, true, false),
            "share" => (BrainVocabulary.ShareFamily, 1, true, false),
            _ => throw new BrainScriptException($"Unknown target macro '{macro}'."),
        };

        if (isGroup && arg.ToLowerInvariant() != "any")
        {
            throw new BrainScriptException($"Group target '{macro}' takes only '(any)', got '({arg})'.");
        }

        return (true, family, polarity, needsDir ? arg : null, default, isGroup);
    }

    private static BrainVocabulary.Gate ResolveGate(string gate) =>
        BrainVocabulary.Gates.TryGetValue(gate.Trim().ToLowerInvariant(), out BrainVocabulary.Gate? g)
            ? g
            : throw new BrainScriptException($"Unknown gate '{gate}'.");

    private static BrainVocabulary.DirectionSource ResolveDirection(string arg) =>
        BrainVocabulary.Directions.TryGetValue(arg.Trim().ToLowerInvariant(), out BrainVocabulary.DirectionSource? d)
            ? d
            : throw new BrainScriptException($"Unknown direction '{arg}'.");

    private static double StrengthScale(RuleStrength strength) => strength switch
    {
        RuleStrength.Weak => 0.5,
        RuleStrength.Strong => 2.0,
        _ => 1.0,
    };

    private static void Add(Dictionary<(int, int), double> weights, int input, int output, double delta) =>
        weights[(input, output)] = weights.GetValueOrDefault((input, output)) + delta;
}
