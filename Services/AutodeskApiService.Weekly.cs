using System.Net.Http.Headers;
using System.Text.Json;
using APS.WebApp.Services.Planning;

namespace APS.WebApp.Services;

public partial class AutodeskApiService
{
    /// <summary>API histórica da Etapa 15; mantém a listagem completa compartilhada com os cinco templates.</summary>
    public Task<List<ArquivoCandidato>> ListarArquivosWeeklyAsync(string projectId, string folderId, string token)
        => ListarArquivosPlanejamentoAsync(projectId, folderId, token);

    public async Task<List<ArquivoCandidato>> ListarArquivosPlanejamentoAsync(string projectId, string folderId, string token, bool includeLegacyXls = false)
    {
        var candidates = new List<ArquivoCandidato>();
        var seenPages = new HashSet<string>();
        var seenItems = new HashSet<string>();
        string? next = $"https://developer.api.autodesk.com/data/v1/projects/{projectId}/folders/{folderId}/contents";
        int? declaredTotal = null;
        while (next != null)
        {
            if (!seenPages.Add(next)) throw new InvalidDataException("LISTAGEM_WEEKLY_SCHEDULE_INCOMPLETA: pagina repetida");
            var uri = new Uri(new Uri("https://developer.api.autodesk.com"), next);
            if (uri.Scheme != "https" || uri.Host != "developer.api.autodesk.com")
                throw new InvalidDataException("LISTAGEM_WEEKLY_SCHEDULE_INCOMPLETA: link de pagina invalido");
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using var response = await _httpClient.SendAsync(request);
            response.EnsureSuccessStatusCode();
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var root = json.RootElement;
            if (root.TryGetProperty("meta", out var meta) && meta.TryGetProperty("totalResults", out var total)) declaredTotal = total.GetInt32();
            foreach (var item in root.GetProperty("data").EnumerateArray())
            {
                string id = item.GetProperty("id").GetString()!;
                if (!seenItems.Add(id)) continue;
                if (item.GetProperty("type").GetString() != "items") continue;
                string name = item.GetProperty("attributes").GetProperty("displayName").GetString() ?? "";
                if (name.StartsWith("~$", StringComparison.Ordinal)) continue;
                bool excel = WeeklyScheduleContract.IsExcel(Path.GetExtension(name));
                bool xls = string.Equals(Path.GetExtension(name), ".xls", StringComparison.OrdinalIgnoreCase);
                if (!excel && !(includeLegacyXls && xls)) continue;
                candidates.Add(new ArquivoCandidato { FileId = id, FileName = name, EhXlsxOuXlsm = excel, EhXls = xls });
            }
            next = null;
            if (root.TryGetProperty("links", out var links) && links.TryGetProperty("next", out var link))
            {
                if (link.ValueKind == JsonValueKind.String) next = link.GetString();
                else if (link.ValueKind == JsonValueKind.Object && link.TryGetProperty("href", out var href)) next = href.GetString();
            }
            if (string.IsNullOrWhiteSpace(next)) next = null;
        }
        if (declaredTotal.HasValue && seenItems.Count != declaredTotal.Value)
            throw new InvalidDataException("LISTAGEM_WEEKLY_SCHEDULE_INCOMPLETA: total divergente");
        return candidates;
    }
}
