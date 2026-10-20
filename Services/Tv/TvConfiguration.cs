using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace APS.WebApp.Services.Tv;

/// <summary>Projeto de um item da TV (multiprojeto).</summary>
public sealed record TvSlideProjeto(string HubId, string ProjectId, string? ProjectName);

/// <summary>
/// Um item da apresentação: uma tela e o(s) seu(s) projeto(s). HubId/ProjectId = projeto PRINCIPAL (compatível com arquivos
/// antigos, que só têm um projeto). Projetos (opcional, 30/09/2026) = seleção multiprojeto do item, só aceita em telas que já
/// consolidam vários projetos (TvTelas → MultiProjeto); o primeiro é sempre o principal.
/// DataReferencia (opcional, 19/10/2026) = dia "yyyy-MM-dd" do item: OBRIGATÓRIA nas telas TvTela.DataReferenciaObrigatoria
/// (Programação Diária) e descartada nas demais — nunca muda os dados de outra tela; arquivos antigos continuam válidos.
/// </summary>
public sealed record TvSlide(string ScreenId, string HubId, string ProjectId, string? ProjectName, IReadOnlyList<TvSlideProjeto>? Projetos = null,
    string? DataReferencia = null);

/// <summary>
/// Configuração de UMA TV (Gestão à Vista): Name = "Nome da Tela / TV" (ex.: "TV SALA DE ENGENHARIA"), sequência de telas.
/// Ativa/AtualizadoEm (30/09/2026) são opcionais: arquivos antigos sem esses campos continuam válidos (null = ativa / sem data).
/// </summary>
public sealed record TvPresentation(string Id, string Name, int SecondsPerScreen, IReadOnlyList<TvSlide> Slides,
    bool? Ativa = null, DateTimeOffset? AtualizadoEm = null)
{
    public bool EstaAtiva => Ativa ?? true;
}

/// <summary>Configuração das Telas para TV (arquivo JSON, mesmo padrão do RobotConfigurationStore — sem tabela nova).</summary>
public sealed record TvConfiguration(int Version, long Revision, DateTimeOffset? SavedAt, string? SavedBy, IReadOnlyList<TvPresentation> Presentations)
{
    public static TvConfiguration Empty => new(1, 0, null, null, Array.Empty<TvPresentation>());
}

public sealed class TvConfigurationException(string message) : Exception(message);
public sealed class TvConfigurationConflictException() : Exception("A configuração foi alterada por outra sessão. Recarregue antes de salvar.");

public static class TvConfigurationRules
{
    public const int MaxApresentacoes = 20, MaxTelas = 50, MinSegundos = 10, MaxSegundos = 600;
    private static readonly Regex IdValido = new("^[a-z0-9][a-z0-9-]{0,39}$", RegexOptions.CultureInvariant);

    /// <summary>
    /// Telas permitidas na TV = telas implementadas do Controle de Produção + painel PMO antigo (catálogo TvTelas, com
    /// contrato verificado contra o frontend). Qualquer outra tela é recusada.
    /// </summary>
    public static IReadOnlyCollection<string> TelasPermitidas => TvTelas.Todas.Select(t => t.Id).ToArray();

    /// <summary>Curva S existente (rota /controle-producao/curva-s, endpoint /api/reports/curva-s com as próprias permissões).</summary>
    public const string CurvaS = "curva-s";

    /// <summary>Limite de projetos por item (só telas TvTela.MultiProjeto aceitam mais de um).</summary>
    public const int MaxProjetosPorTela = 200;

    /// <summary>
    /// "Última atualização" por TV: só a configuração que MUDOU recebe a data do salvamento; as demais mantêm a sua.
    /// </summary>
    public static IReadOnlyList<TvPresentation> CarimbarAtualizacao(IReadOnlyList<TvPresentation> anteriores, IReadOnlyList<TvPresentation> novas, DateTimeOffset agora)
    {
        string Conteudo(TvPresentation a) => System.Text.Json.JsonSerializer.Serialize(a with { AtualizadoEm = null, Ativa = a.EstaAtiva, Name = a.Name?.Trim() ?? "" });
        return novas.Select(n =>
        {
            var antes = anteriores.FirstOrDefault(a => a.Id == n.Id);
            bool igual = antes != null && Conteudo(antes) == Conteudo(n);
            return n with { AtualizadoEm = igual ? antes!.AtualizadoEm : agora };
        }).ToArray();
    }

    /// <summary>hubId vazio é aceito (projeto SQL identificado pelo Project_ID_ACC); preenchido, precisa ser um id ACC.</summary>
    private static bool HubOpcionalValido(string? hub) => string.IsNullOrEmpty(hub) || EhAccId(hub);
    private static bool EhAccId(string? v) => v is { Length: 38 } && v.StartsWith("b.", StringComparison.Ordinal) && Guid.TryParseExact(v[2..], "D", out _);

    /// <summary>Data ISO real ("yyyy-MM-dd", existe no calendário). Sem fallback para hoje.</summary>
    public static bool DataReferenciaValida(string? v) =>
        v is { Length: 10 } && DateOnly.TryParseExact(v, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _);

    public static TvConfiguration Validate(TvConfiguration c)
    {
        if (c is null) throw new ArgumentException("Configuração vazia.");
        var apresentacoes = c.Presentations ?? Array.Empty<TvPresentation>();
        if (apresentacoes.Count > MaxApresentacoes) throw new ArgumentException($"Máximo de {MaxApresentacoes} apresentações.");
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var a in apresentacoes)
        {
            if (a is null || !IdValido.IsMatch(a.Id ?? "")) throw new ArgumentException("Identificador de apresentação inválido (use letras minúsculas, números e hífen).");
            if (!ids.Add(a.Id)) throw new ArgumentException($"Apresentação duplicada: {a.Id}.");
            if (string.IsNullOrWhiteSpace(a.Name) || a.Name.Trim().Length > 80) throw new ArgumentException("Nome da Tela / TV obrigatório (até 80 caracteres).");
            if (a.SecondsPerScreen is < MinSegundos or > MaxSegundos) throw new ArgumentException($"Tempo por tela deve ficar entre {MinSegundos} e {MaxSegundos} segundos.");
            var telas = a.Slides ?? Array.Empty<TvSlide>();
            if (telas.Count == 0) throw new ArgumentException($"A apresentação \"{a.Name}\" precisa de ao menos uma tela.");
            if (telas.Count > MaxTelas) throw new ArgumentException($"Máximo de {MaxTelas} telas por apresentação.");
            foreach (var t in telas)
            {
                var tela = TvTelas.Obter(t?.ScreenId);
                if (t is null || tela is null) throw new ArgumentException($"Tela não permitida na TV: {t?.ScreenId}.");
                // hubId é opcional (telas SQL identificam o projeto pelo Project_ID_ACC; usuário N&A sem Autodesk não tem hub).
                // Se informado (configurações antigas), continua validado.
                if (!HubOpcionalValido(t.HubId) || !EhAccId(t.ProjectId)) throw new ArgumentException("Conta/projeto inválidos em uma das telas.");
                if (t.ProjectName is { Length: > 200 }) throw new ArgumentException("Nome de projeto longo demais.");
                if (t.Projetos is { Count: > 0 } ps)
                {
                    if (ps.Count > MaxProjetosPorTela) throw new ArgumentException($"Máximo de {MaxProjetosPorTela} projetos por tela.");
                    if (ps.Any(p => p is null || !HubOpcionalValido(p.HubId) || !EhAccId(p.ProjectId) || p.ProjectName is { Length: > 200 }))
                        throw new ArgumentException("Projeto inválido em uma das telas.");
                    if (ps.Select(p => p.ProjectId).Distinct(StringComparer.Ordinal).Count() != ps.Count) throw new ArgumentException("Projeto repetido em uma das telas.");
                    if (ps[0].ProjectId != t.ProjectId || (ps[0].HubId ?? "") != (t.HubId ?? "")) throw new ArgumentException("O primeiro projeto da tela deve ser o projeto principal.");
                    // Tela que exibe um projeto por vez nunca combina projetos (um item por projeto).
                    if (!tela.MultiProjeto && ps.Count > 1) throw new ArgumentException($"A tela {t.ScreenId} exibe um projeto por vez: use um item por projeto.");
                }
                // Tela exclusiva de um projeto (Gestão à Vista Tipo 2 = CHS001): outro projeto é recusado mesmo chamado por API.
                if (tela.ProjetoExclusivo is { } exclusivo
                    && (!string.Equals(t.ProjectId, exclusivo, StringComparison.Ordinal)
                        || (t.Projetos ?? Array.Empty<TvSlideProjeto>()).Any(p => !string.Equals(p.ProjectId, exclusivo, StringComparison.Ordinal))))
                    throw new ArgumentException($"A tela {t.ScreenId} é exclusiva do projeto CHS001 ({exclusivo}).");
                // Programação Diária: cada item tem a SUA data (obrigatória, ISO real); hoje nunca é assumido.
                if (tela.DataReferenciaObrigatoria && !DataReferenciaValida(t.DataReferencia))
                    throw new ArgumentException($"A tela {t.ScreenId} exige data de referência válida (AAAA-MM-DD) em cada item.");
            }
        }
        return c with
        {
            Presentations = apresentacoes.Select(a => a with
            {
                Name = a.Name.Trim(),
                Slides = a.Slides.Select(t => t with
                {
                    HubId = t.HubId ?? "",
                    ProjectName = string.IsNullOrWhiteSpace(t.ProjectName) ? null : t.ProjectName.Trim(),
                    // Um único projeto = formato antigo (sem lista) — arquivo idêntico ao de antes para quem não usa multiprojeto.
                    Projetos = t.Projetos is { Count: > 1 } ps
                        ? ps.Select(p => p with { ProjectName = string.IsNullOrWhiteSpace(p.ProjectName) ? null : p.ProjectName.Trim() }).ToArray()
                        : null,
                    // Só as telas com data a guardam; nas demais o campo é descartado (não altera dados nem o arquivo antigo).
                    DataReferencia = TvTelas.Obter(t.ScreenId)!.DataReferenciaObrigatoria ? t.DataReferencia : null,
                }).ToArray(),
            }).ToArray(),
        };
    }
}

/// <summary>
/// Persistência da configuração das Telas para TV em JSON (App_Data), com escrita atômica, backup (.bak) e controle
/// de revisão (duas sessões editando ao mesmo tempo → a segunda recebe conflito, nunca sobrescreve em silêncio).
/// Mesmo padrão comprovado de RobotConfigurationStore.
/// </summary>
public sealed class TvConfigurationStore(string filePath)
{
    private readonly SemaphoreSlim mutex = new(1, 1);
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    public string FilePath { get; } = Path.GetFullPath(filePath);

    public async Task<TvConfiguration> ReadAsync(CancellationToken ct = default)
    {
        await mutex.WaitAsync(ct);
        try { return await ReadCore(ct); }
        finally { mutex.Release(); }
    }

    private async Task<TvConfiguration> ReadCore(CancellationToken ct)
    {
        if (!File.Exists(FilePath)) return TvConfiguration.Empty;
        try
        {
            if (new FileInfo(FilePath).Length > 2_000_000) throw new ArgumentException("Arquivo excessivo.");
            var config = JsonSerializer.Deserialize<TvConfiguration>(await File.ReadAllTextAsync(FilePath, ct), Json);
            return TvConfigurationRules.Validate(config!);
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException or NullReferenceException or IOException)
        { throw new TvConfigurationException("Configuração das Telas para TV corrompida. Restaure o arquivo .bak com o administrador."); }
    }

    public async Task<TvConfiguration> SaveAsync(IReadOnlyList<TvPresentation> presentations, long expectedRevision, string? savedBy, CancellationToken ct = default)
    {
        TvConfigurationRules.Validate(new(1, 0, null, null, presentations));
        await mutex.WaitAsync(ct);
        string? temp = null;
        try
        {
            var current = await ReadCore(ct); // arquivo corrompido nunca é sobrescrito
            if (current.Revision != expectedRevision) throw new TvConfigurationConflictException();
            var agora = DateTimeOffset.UtcNow;
            var next = TvConfigurationRules.Validate(new(1, checked(current.Revision + 1), agora, savedBy,
                TvConfigurationRules.CarimbarAtualizacao(current.Presentations, presentations, agora)));
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            temp = FilePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            var bytes = JsonSerializer.SerializeToUtf8Bytes(next, Json);
            await using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            {
                await stream.WriteAsync(bytes, ct);
                stream.Flush(flushToDisk: true);
            }
            TvConfigurationRules.Validate(JsonSerializer.Deserialize<TvConfiguration>(await File.ReadAllTextAsync(temp, ct), Json)!);
            ct.ThrowIfCancellationRequested();
            if (File.Exists(FilePath)) File.Replace(temp, FilePath, FilePath + ".bak");
            else File.Move(temp, FilePath);
            return next;
        }
        finally { if (temp != null && File.Exists(temp)) File.Delete(temp); mutex.Release(); }
    }
}
