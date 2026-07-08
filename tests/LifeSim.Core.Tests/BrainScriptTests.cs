using LifeSim.Core.Brains;
using LifeSim.Core.Determinism;
using LifeSim.Core.Neat;
using LifeSim.Core.Organisms;
using LifeSim.Core.Sensing;

namespace LifeSim.Core.Tests;

public class BrainScriptTests
{
    [Fact]
    public void Parser_readsNameAndRules()
    {
        BrainTemplate template = BrainScriptParser.ParseTemplate(BuiltInBrains.Selfish);

        Assert.Equal("Selfish", template.Name);
        Assert.NotEmpty(template.Rules);
        Assert.Contains(template.Rules, r => r.Verb == RuleVerb.Avoid); // "avoid Share(any)"
    }

    [Theory]
    [InlineData("prefer HarvestToward(food)\n  more", "must come after")] // rule before a type header
    [InlineData("type X:\n  poke Reproduce", "'prefer' or 'avoid'")]     // bad verb
    [InlineData("type X:\n  prefer Reproduce when", "must be followed")] // dangling when
    [InlineData("type X:", "no rules")]                                    // header, no rules
    public void Parser_reportsMalformedScripts(string script, string expectedFragment)
    {
        BrainScriptException ex = Assert.Throws<BrainScriptException>(() => BrainScriptParser.ParseTemplate(script));
        Assert.Contains(expectedFragment, ex.Message);
    }

    [Fact]
    public void Compiler_rejectsUnknownVocabulary()
    {
        var template = new BrainTemplate("Bad", [new BrainRule(RuleVerb.Prefer, "HarvestToward(unicorns)", null, RuleStrength.Normal)]);
        Assert.Throws<BrainScriptException>(() => BrainTemplateCompiler.Compile(template));
    }

    [Fact]
    public void CompiledSeed_hasTheSameTopologyAsAGenericBrain()
    {
        // Evolution/serialization compatibility: a compiled seed must be a plain genesis-topology genome
        // (same nodes and same connection innovation ids), differing only in its weights.
        NeatGenome generic = NeatGenomeFactory.CreateMinimalFullyConnected(new Prng(1));
        NeatGenome seed = BrainTemplateCompiler.Compile(BrainScriptParser.ParseTemplate(BuiltInBrains.Aggressor));

        Assert.Equal(generic.Nodes.Count, seed.Nodes.Count);
        Assert.Equal(generic.Connections.Count, seed.Connections.Count);
        Assert.Equal(
            generic.Connections.Select(c => c.InnovationId).OrderBy(x => x),
            seed.Connections.Select(c => c.InnovationId).OrderBy(x => x));
    }

    [Fact]
    public void EveryBuiltInExample_compiles()
    {
        foreach (string script in BuiltInBrains.All)
        {
            NeatGenome seed = BrainTemplateCompiler.Compile(BrainScriptParser.ParseTemplate(script));
            Assert.Equal(NeatTopology.InputCount * NeatTopology.OutputCount, seed.Connections.Count);
            Assert.Contains(seed.Connections, c => c.Weight != 0.0); // it actually authored something
        }
    }

    [Fact]
    public void SelfishSeed_chasesFoodAndWontShare()
    {
        NeatGenome selfish = BrainTemplateCompiler.Compile(BrainScriptParser.ParseTemplate(BuiltInBrains.Selfish));

        // Food lies due east (direction vector +x); nothing else salient. Two steps: the recurrent net
        // loads inputs on step 1 and they reach the outputs on step 2.
        double[] inputs = FoodToTheEast();
        double[] p = NeatBrain.Propagate(selfish, inputs, 2).Probabilities;

        // Harvests toward the food (east), not away from it, and won't share that direction.
        Assert.True(p[(int)OrganismAction.HarvestEast] > p[(int)OrganismAction.HarvestWest]);
        Assert.True(p[(int)OrganismAction.HarvestEast] > p[(int)OrganismAction.ShareEast]);
    }

    [Fact]
    public void CowardAndFearless_reactOppositelyToANeighbour()
    {
        NeatGenome coward = BrainTemplateCompiler.Compile(BrainScriptParser.ParseTemplate(BuiltInBrains.Coward));
        NeatGenome fearless = BrainTemplateCompiler.Compile(BrainScriptParser.ParseTemplate(BuiltInBrains.Fearless));

        // A neighbour lies due east (two steps so the reading reaches the outputs).
        double[] inputs = OrganismToTheEast();
        double[] cowardP = NeatBrain.Propagate(coward, inputs, 2).Probabilities;
        double[] fearlessP = NeatBrain.Propagate(fearless, inputs, 2).Probabilities;

        // Fearless closes in and attacks east; the coward flees west and does not attack east.
        Assert.True(fearlessP[(int)OrganismAction.HarvestEast] > cowardP[(int)OrganismAction.HarvestEast]);
        Assert.True(cowardP[(int)OrganismAction.MoveWest] > cowardP[(int)OrganismAction.MoveEast]);
        Assert.True(fearlessP[(int)OrganismAction.MoveEast] > fearlessP[(int)OrganismAction.MoveWest]);
    }

    private static double[] FoodToTheEast()
    {
        var inputs = new double[NeatTopology.InputCount];
        inputs[(int)SensoryField.Energy] = 0.5;
        inputs[(int)SensoryField.RichestTileDistance] = 0.5;
        inputs[(int)SensoryField.RichestTileDirectionX] = 1.0;
        return inputs;
    }

    private static double[] OrganismToTheEast()
    {
        var inputs = new double[NeatTopology.InputCount];
        inputs[(int)SensoryField.Energy] = 0.5;
        inputs[(int)SensoryField.ClosestOrganismDistance] = 0.5;
        inputs[(int)SensoryField.ClosestOrganismDirectionX] = 1.0;
        return inputs;
    }
}
