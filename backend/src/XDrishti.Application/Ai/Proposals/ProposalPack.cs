namespace XDrishti.Application.Ai.Proposals;

/// <summary>The pack the model reads (spec P9-08 AC 1): data only, the same inputs give the same bytes and hash. <paramref name="Unavailable"/> names the inputs that could not be read (the pack says so in words, never with a number).</summary>
internal sealed record ProposalPack(string Text, string Hash, int Cells, int Baskets, int Candidates, IReadOnlyList<string> Unavailable)
{
    /// <summary>Whether there is nothing the model could propose from (no model call is made then).</summary>
    public bool Empty => Cells == 0 && Candidates == 0;
}
