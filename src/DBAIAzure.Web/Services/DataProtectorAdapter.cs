// Bridges ASP.NET Core Data Protection to the ISecretProtector interface (FR-019, Article IX).
using DBAIAzure.Core.Interfaces;
using Microsoft.AspNetCore.DataProtection;

namespace DBAIAzure.Web.Services;

/// <summary>
/// Adapts <see cref="IDataProtectionProvider"/> to the project-internal <see cref="ISecretProtector"/>
/// seam, keeping the Storage layer free of ASP.NET Core dependencies. All connector secrets
/// flow through the <c>ConnectorSecrets</c> key-ring purpose string — rotating to a different
/// purpose string invalidates all stored secrets (operators would need to re-enter them).
/// </summary>
internal sealed class DataProtectorAdapter : ISecretProtector
{
    private readonly IDataProtector _protector;

    public DataProtectorAdapter(IDataProtectionProvider provider)
        => _protector = provider.CreateProtector("ConnectorSecrets");

    /// <inheritdoc/>
    public string Protect(string plaintext) => _protector.Protect(plaintext);

    /// <inheritdoc/>
    public string Unprotect(string ciphertext) => _protector.Unprotect(ciphertext);
}
