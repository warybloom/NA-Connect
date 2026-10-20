using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace APS.WebApp.Services.Reports;

/// <summary>Verifica acesso usando exclusivamente o token da sessão, sem token de robô.</summary>
public sealed class AccProjectReportAccess(IHttpClientFactory clients) : IProjectReportAccess
{
    public async Task<ProjectReportAccess> CheckAsync(string hubId, string projectId, string accessToken, CancellationToken cancellationToken)
    {
        using var client = clients.CreateClient("ReportAccess");
        using var request = new HttpRequestMessage(HttpMethod.Get,
            $"https://developer.api.autodesk.com/project/v1/hubs/{Uri.EscapeDataString(hubId)}/projects/{Uri.EscapeDataString(projectId)}/topFolders");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using var response = await client.SendAsync(request, cancellationToken);
        if (response.StatusCode == HttpStatusCode.Unauthorized) return ProjectReportAccess.Unauthenticated;
        if (response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.NotFound) return ProjectReportAccess.Denied;
        if (!response.IsSuccessStatusCode) return ProjectReportAccess.Unavailable;
        using var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
        return json.RootElement.ValueKind == JsonValueKind.Object &&
            json.RootElement.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array
            ? ProjectReportAccess.Allowed : ProjectReportAccess.Unavailable;
    }
}
