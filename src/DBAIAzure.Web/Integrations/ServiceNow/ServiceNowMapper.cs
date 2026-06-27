using DBAIAzure.Core.Models;

namespace DBAIAzure.Web.Integrations.ServiceNow;

/// <summary>
/// Maps a raw ServiceNow webhook payload to the TicketState domain model.
/// Centralising the mapping here means the pipeline steps are insulated from
/// SNow field name changes — update only this file if the payload schema changes.
/// </summary>
public static class ServiceNowMapper
{
    public static TicketState ToTicketState(ServiceNowWebhookPayload payload)
    {
        // Use the SNow number as the ticket ID so it's traceable back to the source
        var ticketId = string.IsNullOrWhiteSpace(payload.Number)
            ? $"SNow-{payload.SysId[..Math.Min(8, payload.SysId.Length)]}"
            : payload.Number;

        // Combine short + long description; SNow often has content in both
        var description = string.IsNullOrWhiteSpace(payload.Description)
            ? payload.ShortDescription
            : $"{payload.ShortDescription}\n\n{payload.Description}".Trim();

        return new TicketState
        {
            TicketId     = ticketId,
            Title        = payload.ShortDescription,
            Description  = description,
            Source       = "servicenow",
            SnowSysId    = payload.SysId,
            SnowNumber   = payload.Number,
            SnowPriority = string.IsNullOrWhiteSpace(payload.Priority) ? null : payload.Priority,
            SnowCategory = string.IsNullOrWhiteSpace(payload.Category) ? null : payload.Category,
        };
    }
}
