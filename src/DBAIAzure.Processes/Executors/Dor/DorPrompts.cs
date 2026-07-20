// Default AI prompt templates for the DoR workflow (spec-021 §5) and a small placeholder interpolator. The
// templates are overridable via configuration; these defaults are used when the operator has not set one.
using System.Text;

namespace DBAIAzure.Processes.Executors.Dor;

/// <summary>Default DoR prompt templates and the <c>{{placeholder}}</c> interpolation used to fill them.</summary>
public static class DorPrompts
{
    /// <summary>Default DoR review system prompt — evaluates the ticket against the DoR document (contract §5.1).</summary>
    public const string DefaultReviewTemplate = """
        You are a Jira ticket reviewer for an enterprise software delivery team. Your job is to evaluate whether
        a ticket meets the Definition of Ready.

        You will be given the current Definition of Ready document and the ticket field values. Evaluate each DoR
        criterion. A field that is absent or insufficient counts as a failure.

        DoR Document:
        {{dor_document}}

        Ticket Fields (JSON):
        {{ticket_fields}}
        """;

    /// <summary>Default conversation system prompt — interprets a human reply against the outstanding gaps (§5.2).</summary>
    public const string DefaultConversationTemplate = """
        You are helping resolve gaps in a Jira ticket's Definition of Ready. A human has responded to an automated
        request for clarification. Decide whether the reply resolves the outstanding gaps, which remain, and what
        field updates the resolution implies. Compose a concise reply to post back to the channel.

        Outstanding DoR gaps:
        {{failed_criteria}}

        Human response:
        {{human_response}}

        This is iteration {{iteration_count}}.
        """;

    /// <summary>Default field-update system prompt — builds the Jira field body from the resolution (§5.3).</summary>
    public const string DefaultUpdateTemplate = """
        Construct a Jira field-update body from the resolved values. Only include fields that appear in the
        permitted fields list; never include a field outside it, even if it would improve the ticket.

        Permitted fields: {{ai_editable_fields}}
        Resolved values: {{field_updates}}
        Current ticket values (JSON): {{ticket_fields}}
        """;

    /// <summary>Replaces each <c>{{key}}</c> in <paramref name="template"/> with its value (missing keys blanked).</summary>
    public static string Interpolate(string template, IReadOnlyDictionary<string, string> values)
    {
        var builder = new StringBuilder(template);
        foreach (var (key, value) in values)
            builder.Replace("{{" + key + "}}", value ?? string.Empty);
        return builder.ToString();
    }
}
