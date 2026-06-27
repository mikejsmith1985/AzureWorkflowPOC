// JSON Schemas for the forced tool-use calls that realize each node type. Property names are
// snake_case to match the structured-completion service's naming policy, so the model's tool input
// binds straight onto the service's DTOs without any hand parsing (Article VII).

namespace DBAIAzure.Web.Services;

/// <summary>
/// Holds one input schema per realizable node type. Each schema constrains the LLM's forced tool call
/// to exactly the fields the matching configuration needs, keeping the model's output structured and
/// directly bindable. Kept beside <see cref="RealizationPrompts"/> so prompt and schema evolve together.
/// </summary>
internal static class RealizationSchemas
{
    // Reusable fragment: one structured output field (name + data kind + optional enum values).
    private const string OutputFieldItems = """
        {
          "type": "object",
          "properties": {
            "name": { "type": "string", "description": "Field name a downstream step can reference." },
            "kind": { "type": "string", "enum": ["Text", "Number", "Boolean", "Enum"], "description": "The field's data kind." },
            "allowed_values": { "type": "array", "items": { "type": "string" }, "description": "For Enum fields, the permitted values; otherwise omit." }
          },
          "required": ["name", "kind"],
          "additionalProperties": false
        }
        """;

    /// <summary>Schema for a Trigger node's initial-data description and output shape.</summary>
    public static string Trigger => $$"""
        {
          "type": "object",
          "properties": {
            "initial_data_description": { "type": "string", "description": "Plain-language description of the real event or input that starts the workflow." },
            "output_shape": { "type": "array", "description": "Structured fields handed to the first step.", "items": {{OutputFieldItems}} }
          },
          "required": ["initial_data_description"],
          "additionalProperties": false
        }
        """;

    /// <summary>Schema for an AgenticReason node's instruction, output shape, and tool bindings.</summary>
    public static string Agent => $$"""
        {
          "type": "object",
          "properties": {
            "instruction": { "type": "string", "description": "The operating instruction the AI step follows." },
            "output_shape": { "type": "array", "description": "Structured fields the step must produce (required when a downstream step branches/transforms on its result).", "items": {{OutputFieldItems}} },
            "tool_bindings": { "type": "array", "items": { "type": "string", "enum": ["ServiceNow", "AzureDevOps", "LLM", "Messaging"] }, "description": "Connectors the step may call as tools." }
          },
          "required": ["instruction"],
          "additionalProperties": false
        }
        """;

    /// <summary>Schema for a FunctionRoute node's per-port conditions and default path.</summary>
    public const string Route = """
        {
          "type": "object",
          "properties": {
            "conditions": {
              "type": "array",
              "description": "One condition per outgoing path.",
              "items": {
                "type": "object",
                "properties": {
                  "output_port_id": { "type": "string", "description": "The outgoing port id this condition selects." },
                  "expression": { "type": "string", "description": "Plain condition evaluated against upstream output fields." }
                },
                "required": ["output_port_id", "expression"],
                "additionalProperties": false
              }
            },
            "default_port_id": { "type": "string", "description": "The path followed when no condition matches." }
          },
          "required": ["conditions", "default_port_id"],
          "additionalProperties": false
        }
        """;

    /// <summary>Schema for a FunctionTransform node's field mappings.</summary>
    public const string Transform = """
        {
          "type": "object",
          "properties": {
            "field_mappings": {
              "type": "array",
              "description": "Input-to-output field mappings.",
              "items": {
                "type": "object",
                "properties": {
                  "from_field": { "type": "string", "description": "Upstream field read." },
                  "to_field": { "type": "string", "description": "Downstream field written." }
                },
                "required": ["from_field", "to_field"],
                "additionalProperties": false
              }
            }
          },
          "required": ["field_mappings"],
          "additionalProperties": false
        }
        """;

    /// <summary>Schema for a FunctionNotify node's connector, recipient, and message template.</summary>
    public const string Notify = """
        {
          "type": "object",
          "properties": {
            "connector": { "type": "string", "enum": ["Messaging"], "description": "The messaging connector to send through." },
            "recipient_map": { "type": "string", "description": "How the recipient is derived from workflow data. No secrets." },
            "message_template": { "type": "string", "description": "The message body, referencing upstream fields. No secrets." }
          },
          "required": ["connector", "recipient_map", "message_template"],
          "additionalProperties": false
        }
        """;

    /// <summary>Schema for a FunctionData node's connector, operation, and data mappings.</summary>
    public const string Data = """
        {
          "type": "object",
          "properties": {
            "connector": { "type": "string", "enum": ["ServiceNow", "AzureDevOps"], "description": "The data connector backing this step." },
            "operation": { "type": "string", "enum": ["Read", "Write"], "description": "Whether the step reads or writes." },
            "input_map": { "type": "string", "description": "How upstream data binds to the operation inputs. No secrets." },
            "output_map": { "type": "string", "description": "How the result surfaces back into workflow state. No secrets." }
          },
          "required": ["connector", "operation", "input_map", "output_map"],
          "additionalProperties": false
        }
        """;

    /// <summary>Schema for a HumanApproval node's approver, prompt, and decision options.</summary>
    public const string Approval = """
        {
          "type": "object",
          "properties": {
            "approver": { "type": "string", "description": "Who is asked to make the decision." },
            "prompt_shown": { "type": "string", "description": "What the approver is shown when the workflow pauses." },
            "decision_options": { "type": "array", "items": { "type": "string" }, "description": "At least two decision options, e.g. Approve / Reject." }
          },
          "required": ["approver", "prompt_shown", "decision_options"],
          "additionalProperties": false
        }
        """;
}
