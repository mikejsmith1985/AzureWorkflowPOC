using DBAIAzure.Core.Interfaces;
using DBAIAzure.Core.Models;

namespace DBAIAzure.Connectors;

/// <summary>Hardcoded demo tickets — simulates a ServiceNow pull without real credentials.</summary>
public class FakeTicketConnector : ITicketConnector
{
    private static readonly TicketState[] Tickets =
    [
        new()
        {
            TicketId = "INC0001001",
            Title = "Add dark mode toggle to admin console",
            Description = """
                Users have requested a dark mode option in the admin console.
                It should persist the preference in localStorage and apply a CSS class
                to the root element. The toggle should appear in the top-right nav bar.
                Acceptance criteria: preference survives page reload; all existing pages
                render without contrast issues in dark mode.
                """,
        },
        new()
        {
            TicketId = "INC0001002",
            Title = "Fix the thing with the login",
            Description = "Sometimes login doesn't work. Please fix.",
        },
    ];

    public Task<TicketState> GetTicketAsync(string ticketId, CancellationToken cancellationToken = default)
    {
        var ticket = Tickets.FirstOrDefault(t => t.TicketId == ticketId)
            ?? throw new KeyNotFoundException($"Ticket {ticketId} not found");
        return Task.FromResult(ticket);
    }

    public async IAsyncEnumerable<TicketState> ListPendingTicketsAsync(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        foreach (var ticket in Tickets)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return ticket;
            await Task.Yield();
        }
    }
}
