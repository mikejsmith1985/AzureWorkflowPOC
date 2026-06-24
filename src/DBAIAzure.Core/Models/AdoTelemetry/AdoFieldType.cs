// Maps logical field types to their ADO REST API type strings.
namespace DBAIAzure.Core.Models.AdoTelemetry;

/// <summary>
/// The data type of an ADO custom telemetry field.
/// Used when calling the ADO fields creation REST API to specify the correct type string.
/// </summary>
public enum AdoFieldType
{
    /// <summary>ADO type string: "string". General text fields.</summary>
    String,

    /// <summary>ADO type string: "integer". Whole-number count fields (tokens, calls, errors).</summary>
    Integer,

    /// <summary>ADO type string: "double". Decimal fields (cost in USD, rates). Maps to ADO "Decimal".</summary>
    Double,

    /// <summary>
    /// ADO type string: "picklistString".
    /// Requires a separate picklist creation step before the field can be created.
    /// </summary>
    PicklistString,
}
