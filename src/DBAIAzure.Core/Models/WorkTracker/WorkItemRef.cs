// Opaque reference to a work item — hides numeric (ADO) vs string-key (Jira) identity from the core.
using System.Globalization;

namespace DBAIAzure.Core.Models.WorkTracker;

/// <summary>
/// A tracker-neutral handle to a work item. Holds the native id in string form (Azure DevOps
/// <c>"4242"</c>, Jira <c>"PROJ-123"</c>); each adapter owns parse/format. Serialises directly into the
/// binding map and cost ledger.
/// </summary>
public readonly record struct WorkItemRef(string Value)
{
    /// <summary>Creates a ref from a numeric (Azure DevOps) id.</summary>
    public static WorkItemRef From(int workItemId) =>
        new(workItemId.ToString(CultureInfo.InvariantCulture));

    /// <summary>True (with the parsed id) when this ref is numeric — used by the ADO adapter boundary.</summary>
    public bool TryAsInt(out int workItemId) =>
        int.TryParse(Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out workItemId);

    public override string ToString() => Value;
}
