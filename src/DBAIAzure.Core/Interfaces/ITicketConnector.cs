using DBAIAzure.Core.Models;

namespace DBAIAzure.Core.Interfaces;

public interface ITicketConnector
{
    Task<TicketState> GetTicketAsync(string ticketId, CancellationToken cancellationToken = default);
    IAsyncEnumerable<TicketState> ListPendingTicketsAsync(CancellationToken cancellationToken = default);
}
