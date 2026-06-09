using System.IO.Compression;
using System.Security.Cryptography;
using EraTranslator.Models;

namespace EraTranslator.Services;

public sealed record TeamSourceSyncResult(
    bool Downloaded,
    string ScanRevisionId,
    string SourceDirectory,
    string SourceArchiveSha256);

public sealed class TeamSourceSyncService(
    ITeamServerClient? teamServerClient = null,
    TeamProjectStateService? stateService = null)
{
    private const int MaxArchiveFileCount = 200_000;
    private const long MaxArchiveUncompressedBytes = 2L * 1024 * 1024 * 1024;
    private readonly ITeamServerClient _teamServerClient = teamServerClient ?? new TeamServerClient();
    private readonly TeamProjectStateService _stateService = stateService ?? new TeamProjectStateService();

    public async Task<TeamSourceSyncResult> EnsureSourceAsync(
        TeamProjectContext context,
        string bearerToken,
        CancellationToken cancellationToken = default)
    {
        new ProjectContextFactory().EnsureWorkspace(context);

        var sync = await _teamServerClient.SyncAsync(
            context.ServerUrl,
            bearerToken,
            context.ProjectId,
            cancellationToken);
        var state = _stateService.Load(context);

        if (IsCurrentSourceAvailable(context, state, sync.ScanRevisionId))
        {
            return new TeamSourceSyncResult(
                Downloaded: false,
                ScanRevisionId: sync.ScanRevisionId,
                SourceDirectory: context.SourceDirectory,
                SourceArchiveSha256: sync.SourceArchiveSha256);
        }

        await using var archiveStream = await _teamServerClient.DownloadSourceArchiveAsync(
            context.ServerUrl,
            bearerToken,
            context.ProjectId,
            sync.ScanRevisionId,
            cancellationToken);
        using var archiveBuffer = new MemoryStream();
        await archiveStream.CopyToAsync(archiveBuffer, cancellationToken);
        archiveBuffer.Position = 0;

        var actualSha256 = ComputeSha256Hex(archiveBuffer);
        if (!string.Equals(actualSha256, sync.SourceArchiveSha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("서버 원본 압축 파일의 SHA-256 값이 sync metadata와 일치하지 않습니다.");
        }

        archiveBuffer.Position = 0;
        ReplaceSourceDirectory(context.SourceDirectory);
        ExtractZipSafely(archiveBuffer, context.SourceDirectory);

        _stateService.Save(context, new TeamProjectState
        {
            LastSyncedScanRevisionId = sync.ScanRevisionId,
            LocalSourceScanRevisionId = sync.ScanRevisionId,
            TeamProjectDictionaryPath = context.TeamProjectDictionaryDirectory,
            SourceArchiveSha256 = sync.SourceArchiveSha256,
            WorkItemsBySegmentId = state.WorkItemsBySegmentId,
            SharedKeysByLookupKey = state.SharedKeysByLookupKey,
            ConflictIdsByTargetId = state.ConflictIdsByTargetId,
            OfflineSubmissionQueue = state.OfflineSubmissionQueue,
        });

        return new TeamSourceSyncResult(
            Downloaded: true,
            ScanRevisionId: sync.ScanRevisionId,
            SourceDirectory: context.SourceDirectory,
            SourceArchiveSha256: sync.SourceArchiveSha256);
    }

    private static bool IsCurrentSourceAvailable(TeamProjectContext context, TeamProjectState state, string scanRevisionId)
    {
        return string.Equals(state.LocalSourceScanRevisionId, scanRevisionId, StringComparison.Ordinal)
            && Directory.Exists(context.SourceDirectory)
            && Directory.EnumerateFileSystemEntries(context.SourceDirectory).Any();
    }

    private static string ComputeSha256Hex(Stream stream)
    {
        var originalPosition = stream.CanSeek ? stream.Position : 0;
        using var sha256 = SHA256.Create();
        var hash = sha256.ComputeHash(stream);
        if (stream.CanSeek)
        {
            stream.Position = originalPosition;
        }

        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static void ReplaceSourceDirectory(string sourceDirectory)
    {
        if (Directory.Exists(sourceDirectory))
        {
            Directory.Delete(sourceDirectory, recursive: true);
        }

        Directory.CreateDirectory(sourceDirectory);
    }

    private static void ExtractZipSafely(Stream zipStream, string destinationDirectory)
    {
        var destinationRoot = Path.GetFullPath(destinationDirectory);
        Directory.CreateDirectory(destinationRoot);

        using var archive = new ZipArchive(zipStream, ZipArchiveMode.Read, leaveOpen: true);
        var fileCount = 0;
        long uncompressedBytes = 0;
        foreach (var entry in archive.Entries)
        {
            if (string.IsNullOrWhiteSpace(entry.FullName))
            {
                continue;
            }

            var normalizedEntryName = entry.FullName.Replace('\\', '/');
            if (Path.IsPathRooted(normalizedEntryName)
                || normalizedEntryName.Split('/').Any(part => part is ".."))
            {
                throw new InvalidOperationException($"원본 압축 파일에 안전하지 않은 경로가 포함되어 있습니다: {entry.FullName}");
            }

            var destinationPath = Path.GetFullPath(Path.Combine(destinationRoot, normalizedEntryName));
            if (!IsPathInside(destinationPath, destinationRoot))
            {
                throw new InvalidOperationException($"원본 압축 파일에 워크스페이스 밖 경로가 포함되어 있습니다: {entry.FullName}");
            }

            if (string.IsNullOrEmpty(entry.Name))
            {
                Directory.CreateDirectory(destinationPath);
                continue;
            }

            fileCount++;
            if (fileCount > MaxArchiveFileCount)
            {
                throw new InvalidOperationException("원본 압축 파일의 파일 수가 클라이언트 제한을 초과했습니다.");
            }

            uncompressedBytes += entry.Length;
            if (uncompressedBytes > MaxArchiveUncompressedBytes)
            {
                throw new InvalidOperationException("원본 압축 파일의 압축 해제 크기가 클라이언트 제한을 초과했습니다.");
            }

            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath) ?? destinationRoot);
            entry.ExtractToFile(destinationPath, overwrite: true);
        }
    }

    private static bool IsPathInside(string path, string root)
    {
        var normalizedRoot = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        return path.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase)
            || string.Equals(path, root, StringComparison.OrdinalIgnoreCase);
    }
}
