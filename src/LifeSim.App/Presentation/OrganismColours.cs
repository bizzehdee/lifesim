using Avalonia.Media;
using LifeSim.Core.Configuration;
using LifeSim.Core.Organisms;
using LifeSim.Core.Snapshot;

namespace LifeSim.App.Presentation;

/// <summary>
/// Turns an organism's snapshot record into its two visual channels (lifesim.md §18): the
/// mode-dependent <b>fill</b> and the always-on last-action <b>outline</b>, plus marker radius and
/// overlay flags. Pure functions of the snapshot fields, so live and loaded frames render identically.
/// </summary>
public static class OrganismColours
{
    private const double MinRadius = 0.18; // tile units
    private const double MaxRadius = 0.48;

    /// <summary>
    /// The fill colour for the active <paramref name="mode"/>. <paramref name="lineageId"/> is the
    /// organism's resolved lineage (from the lineage records — not stored on the organism record itself).
    /// </summary>
    public static Color Fill(
        ColourMode mode, OrganismSnapshot organism, long lineageId, double energyCeiling, double tileTemperatureCelsius)
    {
        GenomeSnapshot g = organism.Genome;
        return mode switch
        {
            ColourMode.Action => SimulationPalette.ActionColour(organism.LastAction ?? OrganismAction.Idle),
            ColourMode.Energy => SimulationPalette.EnergyColour(organism.Energy, energyCeiling),
            ColourMode.DietTendency => DietColour(organism),
            ColourMode.StressFit => SimulationPalette.StressFitColour(tileTemperatureCelsius, g.ThermalCenter, g.ThermalWidth),
            ColourMode.Lineage => LineageColour.ForLineage(lineageId),
            ColourMode.Cooperation => IsShare(organism.LastAction) ? SimulationPalette.Share : SimulationPalette.Neutral,
            _ => SimulationPalette.Neutral,
        };
    }

    private static bool IsShare(OrganismAction? action) => action is OrganismAction.ShareNorth
        or OrganismAction.ShareSouth or OrganismAction.ShareEast or OrganismAction.ShareWest;

    /// <summary>The outline colour, from the last action + its result (lifesim.md §18).</summary>
    public static Color Outline(OrganismAction? lastAction, ActionResult lastResult)
    {
        if (lastAction is null)
        {
            return SimulationPalette.Idle;
        }

        return lastAction.Value switch
        {
            OrganismAction.MoveNorth or OrganismAction.MoveSouth or OrganismAction.MoveEast or OrganismAction.MoveWest
                => SimulationPalette.Move,
            OrganismAction.HarvestSelf or OrganismAction.HarvestNorth or OrganismAction.HarvestSouth
                or OrganismAction.HarvestEast or OrganismAction.HarvestWest
                => lastResult == ActionResult.Killed ? SimulationPalette.Predation : SimulationPalette.Graze,
            OrganismAction.Reproduce => SimulationPalette.Reproduce,
            OrganismAction.ShareNorth or OrganismAction.ShareSouth
                or OrganismAction.ShareEast or OrganismAction.ShareWest => SimulationPalette.Share,
            _ => SimulationPalette.Idle,
        };
    }

    /// <summary>Marker radius in tile units, scaling <c>Size</c> across its bounds into [0.18, 0.48].</summary>
    public static double Radius(double size, TraitBounds.Range sizeBounds)
    {
        double span = sizeBounds.Max - sizeBounds.Min;
        double t = span <= 0.0 ? 0.5 : Math.Clamp((size - sizeBounds.Min) / span, 0.0, 1.0);
        return MinRadius + ((MaxRadius - MinRadius) * t);
    }

    /// <summary>Diet tendency from the current action (lifesim.md §18; see notes on history-vs-snapshot).</summary>
    private static Color DietColour(OrganismSnapshot organism)
    {
        if (organism.LastAction is not { } action)
        {
            return SimulationPalette.DietMixed;
        }

        bool isHarvest = action is OrganismAction.HarvestSelf or OrganismAction.HarvestNorth
            or OrganismAction.HarvestSouth or OrganismAction.HarvestEast or OrganismAction.HarvestWest;
        if (!isHarvest)
        {
            return SimulationPalette.DietMixed;
        }

        return organism.LastActionResult == ActionResult.Killed ? SimulationPalette.DietPredator : SimulationPalette.DietGrazer;
    }
}
