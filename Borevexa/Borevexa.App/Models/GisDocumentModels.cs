using System.Text;

namespace Borevexa.App.Models;

public sealed record ProjectDocumentEntry(
    string Name,
    string Type,
    long SizeKb,
    string LocalPath,
    string? ZipEntryName)
{
    public string Id => Convert
        .ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes($"{LocalPath}|{ZipEntryName}|{Name}")))
        .Substring(0, 24);
}
