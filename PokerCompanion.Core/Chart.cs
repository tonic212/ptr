namespace PokerCompanion.Core;

public sealed class Chart
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public int Page { get; set; }
    public string ImagePath { get; set; } = "";
    public Dictionary<string, string> Actions { get; set; } = new();
}

public sealed record ChartResult(string Action, string Explanation);

public sealed class ChartLookupService
{
    private const string Ranks = "AKQJT98765432";

    public ChartResult Lookup(Chart? chart, string input, decimal stackBb, bool ante, bool icm)
    {
        if (chart is null) return new("Select a chart", "Choose the exact action and position scenario.");
        if (stackBb < 50) return new("Unsupported stack", "This PDF does not cover stacks below 50 BB.");
        if (!ante) return new("Unsupported structure", "These charts assume an ante.");
        if (icm) return new("ICM not covered", "This PDF does not supply a payout-specific ICM strategy.");
        string hand = input.Trim().Replace("10", "T", StringComparison.OrdinalIgnoreCase).ToUpperInvariant();
        if (hand.Length < 2 || hand.Length > 3 || !Ranks.Contains(hand[0]) || !Ranks.Contains(hand[1]))
            return new("Invalid hand", "Enter a hand class such as AA, AKs or AKo.");
        bool pair = hand[0] == hand[1];
        if ((pair && hand.Length != 2) || (!pair && (hand.Length != 3 || (hand[2] != 'S' && hand[2] != 'O'))))
            return new("Invalid hand", "Pairs use two characters; other hands need s or o.");
        char high = Ranks.IndexOf(hand[0]) <= Ranks.IndexOf(hand[1]) ? hand[0] : hand[1];
        char low = high == hand[0] ? hand[1] : hand[0];
        hand = $"{high}{low}" + (pair ? "" : char.ToLowerInvariant(hand[2]).ToString());
        if (!chart.Actions.TryGetValue(hand, out string? action))
            return new("No matching entry", "No chart entry exists for this hand.");
        string explanation = $"{hand} | {chart.Name} | source page {chart.Page}. " +
            "Source assumes 100 BB with an ante; author suggests approximate use from 50 BB. " +
            "This is a chart prescription, not calculated equity or a solver result. " +
            "Select only scenarios matching the prior actions and the source bet-size assumptions.";
        if (action == "Not in prior range")
            explanation = "This hand is outside the opening/limping range leading to this chart. Do not interpret it as a new fold recommendation. " + explanation;
        return new(action, explanation);
    }
}
