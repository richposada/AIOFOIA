using System.IO.Compression;
using System.Text;
using Azure.Identity;
using Azure.Storage;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Sas;
using FoiaProcessor.McpTools.Contracts;
using FoiaProcessor.McpTools.Options;
using Microsoft.Extensions.Options;

namespace FoiaProcessor.McpTools.Servers;

public class BlobStorageServer
{
    private readonly AzureBlobStorageOptions _opts;

    public BlobStorageServer(IOptions<AzureBlobStorageOptions> opts)
    {
        _opts = opts.Value;
    }

    public virtual Task<CreateZipPackageOutput> CreateZipPackageAsync(CreateZipPackageInput input, CancellationToken ct = default)
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var f in input.Files)
            {
                var entry = zip.CreateEntry(f.FileName, CompressionLevel.Optimal);
                using var es = entry.Open();
                var bytes = Encoding.UTF8.GetBytes(f.Content);
                es.Write(bytes, 0, bytes.Length);
            }
        }
        ms.Position = 0;
        return Task.FromResult(new CreateZipPackageOutput(Convert.ToBase64String(ms.ToArray()), input.Files.Count));
    }

    public virtual Task<CreateZipPackageOutput> CreateZipPackageBinaryAsync(CreateZipPackageBinaryInput input, CancellationToken ct = default)
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var f in input.Files)
            {
                var entry = zip.CreateEntry(f.FileName, CompressionLevel.Optimal);
                using var es = entry.Open();
                es.Write(f.Content, 0, f.Content.Length);
            }
        }
        ms.Position = 0;
        return Task.FromResult(new CreateZipPackageOutput(Convert.ToBase64String(ms.ToArray()), input.Files.Count));
    }

    public virtual async Task<UploadToBlobStorageOutput> UploadToBlobStorageAsync(UploadToBlobStorageInput input, CancellationToken ct = default)
    {
        var container = GetContainerClient(input.ContainerName);
        await container.CreateIfNotExistsAsync(PublicAccessType.None, cancellationToken: ct);
        var blob = container.GetBlobClient(input.BlobName);
        var bytes = Convert.FromBase64String(input.ContentBase64);
        using var ms = new MemoryStream(bytes);
        await blob.UploadAsync(ms, new BlobHttpHeaders { ContentType = input.ContentType }, cancellationToken: ct);
        return new UploadToBlobStorageOutput(blob.Uri.ToString());
    }

    public virtual async Task<GenerateSasUrlOutput> GenerateSasUrlAsync(GenerateSasUrlInput input, CancellationToken ct = default)
    {
        var expiresAt = DateTimeOffset.UtcNow.AddDays(input.ExpirationDays);
        var container = GetContainerClient(input.ContainerName);
        var blob = container.GetBlobClient(input.BlobName);

        // Connection string available => account-key SAS (local dev path).
        var conn = NormalizedConnectionString;
        if (!string.IsNullOrEmpty(conn))
        {
            var sasBuilder = new BlobSasBuilder
            {
                BlobContainerName = input.ContainerName,
                BlobName = input.BlobName,
                Resource = "b",
                ExpiresOn = expiresAt,
            };
            sasBuilder.SetPermissions(BlobSasPermissions.Read);
            var key = ExtractAccountKey(conn);
            var accountName = ExtractAccountName(conn);
            var creds = new StorageSharedKeyCredential(accountName, key);
            var sasToken = sasBuilder.ToSasQueryParameters(creds).ToString();
            return new GenerateSasUrlOutput($"{blob.Uri}?{sasToken}", expiresAt.UtcDateTime);
        }

        // Azure path: user-delegation SAS via Managed Identity.
        var serviceClient = GetServiceClient();
        var udk = await serviceClient.GetUserDelegationKeyAsync(
            startsOn: DateTimeOffset.UtcNow.AddMinutes(-5),
            expiresOn: expiresAt,
            cancellationToken: ct);

        var udBuilder = new BlobSasBuilder
        {
            BlobContainerName = input.ContainerName,
            BlobName = input.BlobName,
            Resource = "b",
            ExpiresOn = expiresAt,
        };
        udBuilder.SetPermissions(BlobSasPermissions.Read);
        var udToken = udBuilder.ToSasQueryParameters(udk.Value, serviceClient.AccountName).ToString();
        return new GenerateSasUrlOutput($"{blob.Uri}?{udToken}", expiresAt.UtcDateTime);
    }

    public virtual Task<GetReleasePackageStatusOutput> GetReleasePackageStatusAsync(GetReleasePackageStatusInput input, CancellationToken ct = default)
        => Task.FromResult(new GetReleasePackageStatusOutput(null, null, null, null));

    public virtual async Task<DeleteBlobOutput> DeleteBlobAsync(DeleteBlobInput input, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(input.ContainerName) || string.IsNullOrWhiteSpace(input.BlobName))
        {
            return new DeleteBlobOutput(false);
        }

        var container = GetContainerClient(input.ContainerName);
        var blob = container.GetBlobClient(input.BlobName);
        var response = await blob.DeleteIfExistsAsync(
            DeleteSnapshotsOption.IncludeSnapshots,
            cancellationToken: ct);
        return new DeleteBlobOutput(response.Value);
    }

    /// <summary>
    /// Returns the configured connection string only when it actually looks
    /// like one (i.e. semicolon-delimited name=value pairs). A URL or any
    /// other value is treated as misconfiguration and ignored so the code
    /// falls through to the AccountName + DefaultAzureCredential path.
    /// </summary>
    private string? NormalizedConnectionString
    {
        get
        {
            var raw = _opts.ConnectionString?.Trim();
            if (string.IsNullOrEmpty(raw)) return null;
            if (raw.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                raw.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "AzureBlobStorage:ConnectionString must be a Storage connection string " +
                    "(e.g. 'DefaultEndpointsProtocol=https;AccountName=...;AccountKey=...;EndpointSuffix=core.windows.net'), not a URL. " +
                    "To use Managed Identity, leave ConnectionString empty and set AzureBlobStorage:AccountName instead.");
            }
            if (!raw.Contains('=') || !raw.Contains(';'))
            {
                throw new InvalidOperationException(
                    "AzureBlobStorage:ConnectionString is not a valid Storage connection string. Expected semicolon-delimited name=value pairs.");
            }
            return raw;
        }
    }

    private BlobServiceClient GetServiceClient()
    {
        var conn = NormalizedConnectionString;
        if (!string.IsNullOrEmpty(conn))
            return new BlobServiceClient(conn);
        var accountName = _opts.AccountName?.Trim();
        if (string.IsNullOrEmpty(accountName))
            throw new InvalidOperationException("AzureBlobStorage:AccountName or ConnectionString must be configured.");
        return new BlobServiceClient(new Uri($"https://{accountName}.blob.core.windows.net"), new DefaultAzureCredential());
    }

    private BlobContainerClient GetContainerClient(string containerName)
        => GetServiceClient().GetBlobContainerClient(containerName);

    private static string ExtractAccountKey(string conn)
    {
        var seg = conn.Split(';', StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault(s => s.StartsWith("AccountKey=", StringComparison.OrdinalIgnoreCase));
        return seg?.Substring("AccountKey=".Length) ?? throw new InvalidOperationException("AccountKey missing in connection string.");
    }

    private static string ExtractAccountName(string conn)
    {
        var seg = conn.Split(';', StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault(s => s.StartsWith("AccountName=", StringComparison.OrdinalIgnoreCase));
        return seg?.Substring("AccountName=".Length) ?? throw new InvalidOperationException("AccountName missing in connection string.");
    }
}
