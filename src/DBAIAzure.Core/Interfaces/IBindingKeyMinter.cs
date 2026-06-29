// Mints the canonical, source-neutral binding key that joins a ticket to its AI-cost records.
namespace DBAIAzure.Core.Interfaces;

/// <summary>
/// Mints a source-neutral, branch-safe binding key for a ticket entering the pipeline (e.g.
/// <c>BIND-7K3QF2AB</c>). The key is the universal join across systems, branches, runs, and sessions —
/// it does not embed any single system's native id.
/// </summary>
public interface IBindingKeyMinter
{
    /// <summary>Returns a new unique, branch-safe binding key.</summary>
    string Mint();

    /// <summary>True when <paramref name="candidate"/> is a well-formed binding key (branch-safe, non-blank).</summary>
    bool IsValid(string? candidate);
}
