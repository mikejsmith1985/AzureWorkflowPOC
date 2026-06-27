// Small value records shared across the per-node-type realized configuration shapes.

namespace DBAIAzure.Core.Models.NodeConfig;

/// <summary>The data kind of a single field a node produces, so downstream steps can act on it.</summary>
public enum OutputFieldKind
{
    /// <summary>Free text.</summary>
    Text,

    /// <summary>A numeric value.</summary>
    Number,

    /// <summary>A true/false value.</summary>
    Boolean,

    /// <summary>One of a fixed set of allowed values (see <see cref="OutputField.AllowedValues"/>).</summary>
    Enum,
}

/// <summary>
/// One field in a node's structured output. A non-text, enumerated shape lets a Route or Transform
/// node act on the value deterministically rather than parsing free text.
/// </summary>
/// <param name="Name">The field name downstream steps reference.</param>
/// <param name="Kind">The data kind of the field.</param>
/// <param name="AllowedValues">For <see cref="OutputFieldKind.Enum"/>, the permitted values; otherwise null.</param>
public sealed record OutputField(string Name, OutputFieldKind Kind, IReadOnlyList<string>? AllowedValues = null);

/// <summary>
/// A single branching rule on a Route node: when <see cref="Expression"/> holds against the upstream
/// structured output, the run follows the edge leaving <see cref="OutputPortId"/>.
/// </summary>
/// <param name="OutputPortId">The outgoing port selected when the expression is satisfied.</param>
/// <param name="Expression">A plain condition evaluated against upstream output fields.</param>
public sealed record RouteCondition(string OutputPortId, string Expression);

/// <summary>A single input→output field mapping applied by a Transform node.</summary>
/// <param name="FromField">The upstream field read.</param>
/// <param name="ToField">The downstream field written.</param>
public sealed record FieldMapping(string FromField, string ToField);
