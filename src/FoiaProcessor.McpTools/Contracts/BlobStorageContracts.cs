namespace FoiaProcessor.McpTools.Contracts;

public record ZipFileEntry(string FileName, string Content);
public record CreateZipPackageInput(Guid CaseId, IReadOnlyList<ZipFileEntry> Files);
public record CreateZipPackageOutput(string ZipBytesBase64, int FileCount);

public record UploadToBlobStorageInput(
    string ContainerName,
    string BlobName,
    string ContentBase64,
    string ContentType);

public record UploadToBlobStorageOutput(string BlobUrl);

public record GenerateSasUrlInput(string ContainerName, string BlobName, int ExpirationDays);
public record GenerateSasUrlOutput(string SasUrl, DateTime ExpiresAt);

public record GetReleasePackageStatusInput(Guid CaseId);
public record GetReleasePackageStatusOutput(
    Guid? PackageId,
    string? ZipBlobName,
    string? SasUrl,
    DateTime? SasExpiresAt);

public record DeleteBlobInput(string ContainerName, string BlobName);
public record DeleteBlobOutput(bool Deleted);
