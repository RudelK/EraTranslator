using System.IO.Compression;
using System.Security.Cryptography;
using EraTranslator.Models;
using EraTranslator.Services;

namespace EraTranslator.Tests;

public sealed class TeamSourceSyncServiceTests : IDisposable
{
    private readonly string _rootPath = Path.Combine(Path.GetTempPath(), "EraTranslatorTests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_rootPath))
        {
            Directory.Delete(_rootPath, recursive: true);
        }
    }

    [Fact]
    public async Task EnsureSourceAsync_DownloadsVerifiedArchiveAndExtractsSource()
    {
        var archiveBytes = CreateZip(("ERB/test.erb", "PRINTL 테스트"));
        var fakeClient = new FakeTeamServerClient("scan-1", ComputeSha256Hex(archiveBytes), archiveBytes);
        var context = BuildContext();
        var service = new TeamSourceSyncService(fakeClient);

        var result = await service.EnsureSourceAsync(context, "token");

        Assert.True(result.Downloaded);
        Assert.Equal("scan-1", result.ScanRevisionId);
        Assert.Equal(1, fakeClient.DownloadCount);
        Assert.Equal("PRINTL 테스트", await File.ReadAllTextAsync(Path.Combine(context.SourceDirectory, "ERB", "test.erb")));

        var state = new TeamProjectStateService().Load(context);
        Assert.Equal("scan-1", state.LocalSourceScanRevisionId);
    }

    [Fact]
    public async Task EnsureSourceAsync_ReusesExistingSourceWhenRevisionMatches()
    {
        var archiveBytes = CreateZip(("ERB/test.erb", "PRINTL 테스트"));
        var fakeClient = new FakeTeamServerClient("scan-1", ComputeSha256Hex(archiveBytes), archiveBytes);
        var context = BuildContext();
        var stateService = new TeamProjectStateService();
        var service = new TeamSourceSyncService(fakeClient, stateService);

        await service.EnsureSourceAsync(context, "token");
        var result = await service.EnsureSourceAsync(context, "token");

        Assert.False(result.Downloaded);
        Assert.Equal(1, fakeClient.DownloadCount);
    }

    [Fact]
    public async Task EnsureSourceAsync_RejectsHashMismatch()
    {
        var archiveBytes = CreateZip(("ERB/test.erb", "PRINTL 테스트"));
        var fakeClient = new FakeTeamServerClient("scan-1", new string('0', 64), archiveBytes);
        var service = new TeamSourceSyncService(fakeClient);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => service.EnsureSourceAsync(BuildContext(), "token"));

        Assert.Contains("SHA-256", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EnsureSourceAsync_RejectsZipSlipEntries()
    {
        var archiveBytes = CreateZip(("../evil.txt", "bad"));
        var fakeClient = new FakeTeamServerClient("scan-1", ComputeSha256Hex(archiveBytes), archiveBytes);
        var service = new TeamSourceSyncService(fakeClient);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => service.EnsureSourceAsync(BuildContext(), "token"));

        Assert.Contains("안전하지 않은 경로", ex.Message, StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(_rootPath, "evil.txt")));
    }

    private TeamProjectContext BuildContext()
    {
        return new TeamProjectContext(
            "http://localhost:8000",
            "project-1",
            "tester",
            "client-1",
            _rootPath,
            Path.Combine(_rootPath, "source"),
            Path.Combine(_rootPath, "output"),
            Path.Combine(_rootPath, ".era-translator"),
            Path.Combine(_rootPath, ".era-translator", "dictionaries"));
    }

    private static byte[] CreateZip(params (string path, string content)[] entries)
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (path, content) in entries)
            {
                var entry = archive.CreateEntry(path);
                using var writer = new StreamWriter(entry.Open());
                writer.Write(content);
            }
        }

        return stream.ToArray();
    }

    private static string ComputeSha256Hex(byte[] bytes)
    {
        return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    }

    private sealed class FakeTeamServerClient(string scanRevisionId, string sourceArchiveSha256, byte[] archiveBytes) : ITeamServerClient
    {
        public int DownloadCount { get; private set; }

        public Task<IReadOnlyList<TeamProjectSummary>> GetProjectsAsync(string serverUrl, string bearerToken, CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyList<TeamProjectSummary>>(Array.Empty<TeamProjectSummary>());
        }

        public Task RegisterClientAsync(string serverUrl, string bearerToken, string clientId, string displayName, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        public Task<TeamSyncResponse> SyncAsync(string serverUrl, string bearerToken, string projectId, CancellationToken cancellationToken)
        {
            return Task.FromResult(new TeamSyncResponse
            {
                ProjectId = projectId,
                ScanRevisionId = scanRevisionId,
                SourceArchiveSha256 = sourceArchiveSha256,
            });
        }

        public Task<Stream> DownloadSourceArchiveAsync(
            string serverUrl,
            string bearerToken,
            string projectId,
            string requestedScanRevisionId,
            CancellationToken cancellationToken)
        {
            Assert.Equal(scanRevisionId, requestedScanRevisionId);
            DownloadCount++;
            return Task.FromResult<Stream>(new MemoryStream(archiveBytes, writable: false));
        }

        public Task<TeamScanManifestValidationResponse> UploadScanManifestAsync(
            string serverUrl,
            string bearerToken,
            string projectId,
            TeamScanManifestUploadRequest manifest,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(new TeamScanManifestValidationResponse());
        }

        public Task<TeamScanManifestValidationResponse> GetScanManifestValidationAsync(
            string serverUrl,
            string bearerToken,
            string projectId,
            string requestedScanRevisionId,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(new TeamScanManifestValidationResponse
            {
                ScanRevisionId = requestedScanRevisionId,
                ValidationStatus = "valid",
            });
        }

        public Task<TeamSubmitResponse> SubmitAsync(
            string serverUrl,
            string bearerToken,
            string projectId,
            TeamSubmitRequest request,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(new TeamSubmitResponse
            {
                SubmissionId = request.SubmissionId,
                Status = "accepted",
            });
        }
    }
}
