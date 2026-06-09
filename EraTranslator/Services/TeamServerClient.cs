using System.Net.Http.Headers;
using System.Net.Http.Json;
using EraTranslator.Models;

namespace EraTranslator.Services;

public interface ITeamServerClient
{
    Task<IReadOnlyList<TeamProjectSummary>> GetProjectsAsync(string serverUrl, string bearerToken, CancellationToken cancellationToken);

    Task RegisterClientAsync(string serverUrl, string bearerToken, string clientId, string displayName, CancellationToken cancellationToken);

    Task<TeamSyncResponse> SyncAsync(string serverUrl, string bearerToken, string projectId, CancellationToken cancellationToken);

    Task<Stream> DownloadSourceArchiveAsync(
        string serverUrl,
        string bearerToken,
        string projectId,
        string scanRevisionId,
        CancellationToken cancellationToken);

    Task<TeamScanManifestValidationResponse> UploadScanManifestAsync(
        string serverUrl,
        string bearerToken,
        string projectId,
        TeamScanManifestUploadRequest manifest,
        CancellationToken cancellationToken);

    Task<TeamScanManifestValidationResponse> GetScanManifestValidationAsync(
        string serverUrl,
        string bearerToken,
        string projectId,
        string scanRevisionId,
        CancellationToken cancellationToken);

    Task<TeamSubmitResponse> SubmitAsync(
        string serverUrl,
        string bearerToken,
        string projectId,
        TeamSubmitRequest request,
        CancellationToken cancellationToken);
}

public sealed class TeamServerClient(HttpClient? httpClient = null) : ITeamServerClient
{
    private readonly HttpClient _httpClient = httpClient ?? new HttpClient();

    public async Task<IReadOnlyList<TeamProjectSummary>> GetProjectsAsync(
        string serverUrl,
        string bearerToken,
        CancellationToken cancellationToken)
    {
        using var request = CreateRequest(HttpMethod.Get, serverUrl, "/api/projects", bearerToken);
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<TeamProjectListResponse>(cancellationToken)
            ?? new TeamProjectListResponse();
        return body.Projects;
    }

    public async Task RegisterClientAsync(
        string serverUrl,
        string bearerToken,
        string clientId,
        string displayName,
        CancellationToken cancellationToken)
    {
        using var request = CreateJsonRequest(
            HttpMethod.Post,
            serverUrl,
            "/api/clients/register",
            bearerToken,
            new TeamClientRegisterRequest { ClientId = clientId, DisplayName = displayName });
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    public async Task<TeamSyncResponse> SyncAsync(
        string serverUrl,
        string bearerToken,
        string projectId,
        CancellationToken cancellationToken)
    {
        using var request = CreateRequest(
            HttpMethod.Get,
            serverUrl,
            $"/api/projects/{Uri.EscapeDataString(projectId)}/sync",
            bearerToken);
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<TeamSyncResponse>(cancellationToken)
            ?? throw new InvalidOperationException("Team server returned an empty sync response.");
    }

    public async Task<Stream> DownloadSourceArchiveAsync(
        string serverUrl,
        string bearerToken,
        string projectId,
        string scanRevisionId,
        CancellationToken cancellationToken)
    {
        var path = $"/api/projects/{Uri.EscapeDataString(projectId)}/source/download?scan_revision_id={Uri.EscapeDataString(scanRevisionId)}";
        using var request = CreateRequest(HttpMethod.Get, serverUrl, path, bearerToken);
        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        var buffer = new MemoryStream();
        await response.Content.CopyToAsync(buffer, cancellationToken);
        buffer.Position = 0;
        return buffer;
    }

    public async Task<TeamScanManifestValidationResponse> UploadScanManifestAsync(
        string serverUrl,
        string bearerToken,
        string projectId,
        TeamScanManifestUploadRequest manifest,
        CancellationToken cancellationToken)
    {
        using var request = CreateJsonRequest(
            HttpMethod.Post,
            serverUrl,
            $"/api/projects/{Uri.EscapeDataString(projectId)}/source/{Uri.EscapeDataString(manifest.ScanRevisionId)}/scan-manifest",
            bearerToken,
            manifest);
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<TeamScanManifestValidationResponse>(cancellationToken)
            ?? throw new InvalidOperationException("Team server returned an empty scan manifest validation response.");
    }

    public async Task<TeamScanManifestValidationResponse> GetScanManifestValidationAsync(
        string serverUrl,
        string bearerToken,
        string projectId,
        string scanRevisionId,
        CancellationToken cancellationToken)
    {
        using var request = CreateRequest(
            HttpMethod.Get,
            serverUrl,
            $"/api/projects/{Uri.EscapeDataString(projectId)}/source/{Uri.EscapeDataString(scanRevisionId)}/scan-manifest/validation",
            bearerToken);
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<TeamScanManifestValidationResponse>(cancellationToken)
            ?? throw new InvalidOperationException("Team server returned an empty scan manifest validation response.");
    }

    public async Task<TeamSubmitResponse> SubmitAsync(
        string serverUrl,
        string bearerToken,
        string projectId,
        TeamSubmitRequest request,
        CancellationToken cancellationToken)
    {
        using var httpRequest = CreateJsonRequest(
            HttpMethod.Post,
            serverUrl,
            $"/api/projects/{Uri.EscapeDataString(projectId)}/submit",
            bearerToken,
            request);
        using var response = await _httpClient.SendAsync(httpRequest, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<TeamSubmitResponse>(cancellationToken)
            ?? throw new InvalidOperationException("Team server returned an empty submit response.");
    }

    private static HttpRequestMessage CreateRequest(HttpMethod method, string serverUrl, string path, string bearerToken)
    {
        var request = new HttpRequestMessage(method, BuildUri(serverUrl, path));
        if (!string.IsNullOrWhiteSpace(bearerToken))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);
        }

        return request;
    }

    private static HttpRequestMessage CreateJsonRequest<T>(HttpMethod method, string serverUrl, string path, string bearerToken, T body)
    {
        var request = CreateRequest(method, serverUrl, path, bearerToken);
        request.Content = JsonContent.Create(body);
        return request;
    }

    private static Uri BuildUri(string serverUrl, string path)
    {
        var baseUrl = serverUrl.TrimEnd('/');
        var relativePath = path.StartsWith('/') ? path : "/" + path;
        return new Uri(baseUrl + relativePath, UriKind.Absolute);
    }
}
