using DBAIAzure.Core.Models;

namespace DBAIAzure.Core.Interfaces;

public interface IActionConnector
{
    Task<string> CreateJiraIssueAsync(TicketState ticket, CancellationToken cancellationToken = default);
}
