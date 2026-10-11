namespace PokerCompanion.Core;

public static class PositionChartSelector
{
    public static readonly string[] Scenarios = ["RFI", "Facing raise", "Facing 3-bet", "SB limp vs BB raise"];
    public static readonly string[] Opponents = ["", "UTG", "UTG+1", "UTG+2", "LJ", "HJ", "CO", "BTN", "SB", "BB"];

    public static Chart? Select(IEnumerable<Chart> charts, string hero, string scenario, string opponent)
    {
        if (!Opponents.Contains(hero) || hero == "" || !Scenarios.Contains(scenario)) return null;
        string category = scenario == "SB limp vs BB raise" ? "Facing 3-bet" : scenario;
        var matches = charts.Where(chart =>
        {
            var parts = chart.Name.Split(" | ", StringSplitOptions.None);
            if (parts.Length != 2 || parts[0] != category) return false;
            if (scenario == "SB limp vs BB raise") return hero == "SB" && parts[1] == "SB limp vs BB raise";
            var matchup = parts[1].Split(" vs ", StringSplitOptions.None);
            if (matchup[0] != hero) return false;
            return scenario == "RFI" ? matchup.Length == 1
                : matchup.Length == 2 && opponent != "" && Opponents.Contains(opponent) && matchup[1].Split('/').Contains(opponent);
        }).ToArray();
        return matches.Length == 1 ? matches[0] : null;
    }
}
