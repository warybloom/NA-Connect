using System.Data;
using System.Text.Json;
using APS.WebApp.Services.Planning;
using Microsoft.Data.SqlClient;
using NA_Connect.Services;

namespace APS.WebApp.Services.UploadGovernado;

// =============================================================================================
// ENVIO DE DOCUMENTOS GOVERNADO PELA LDP (01/10/2026) — núcleo:
//   1) identificar o documento pelo nome com a regra OFICIAL do projeto (DocumentMatchingService.NomeCorrespondePorPrefixoDeTokens:
//      tokens do código = prefixo dos tokens do arquivo; sufixos de revisão "-R02", "_01", "-REV02" ficam indiferentes);
//   2) procurar na LDP ATUAL (PmoLdpSnapshot.Tabela — mesma carga das telas; Tipo 1 e Tipo 2 preservados), N&A Document Number
//      primeiro e Client Document Number como alternativa;
//   3) a regra só vale em ÁRVORES PROTEGIDAS: pastas-raiz configuradas por projeto + TODAS as subpastas, em qualquer
//      profundidade (ancestralidade resolvida por ID/parent do ACC, nunca por nome). Disciplina × pasta NÃO é validada;
//   4) na árvore protegida: existe na LDP → permitido; não existe → bloqueado. Fora das árvores: regra não aplicada.
//      Nada é enviado antes de validar.
// =============================================================================================

public static class UploadGovernadoStatus
{
    public const string Pronto = "PRONTO", ForaDaLdp = "FORA_DA_LDP", Ambiguo = "DOCUMENTO_AMBIGUO", NomeInvalido = "NOME_INVALIDO",
        // Pasta de destino fora de qualquer árvore protegida: a regra da LDP não se aplica (envio permitido).
        NaoProtegida = "PASTA_NAO_PROTEGIDA";

    /// <summary>Status que permitem o envio.</summary>
    public static bool PermiteEnvio(string status) => status is Pronto or NaoProtegida;
}

/// <summary>
/// Documento da LDP atual. Extras = demais colunas documentais da LDP usadas pela Auditoria de Qualidade (chave = nome da coluna em
/// dbo.Ldp_Unificada: Format, Area, Doc_Type_Code, Issued_Purpose).
/// </summary>
public sealed record LdpDocumentoAtual(long IdRegistro, string? NaDocumentNumber, string? ClientDocumentNumber, string? Disciplina, string? Titulo, string? Revisao,
    IReadOnlyDictionary<string, string?>? Extras = null)
{
    /// <summary>Formato da folha na LDP (coluna Format: A, B, D, A1, A3...).</summary>
    public string? Formato => Extra("Format");
    public string? Extra(string coluna) => Extras != null && Extras.TryGetValue(coluna, out var v) ? v : null;
}

/// <summary>Pasta-RAIZ protegida pela LDP (vale para ela e todas as subpastas). Só o ID da raiz é persistido.</summary>
public sealed record PastaProtegida(string FolderId, string Caminho);

public sealed record ConfiguracaoPastasProjeto(IReadOnlyList<PastaProtegida> Raizes, DateTimeOffset? AtualizadoEm = null, string? AtualizadoPor = null)
{
    public static ConfiguracaoPastasProjeto Vazia => new(Array.Empty<PastaProtegida>());
}

/// <summary>Resultado da pré-validação de UM arquivo.</summary>
public sealed record UploadValidacao(string Arquivo, string Status, string Mensagem, string? Documento, string? CampoIdentificador,
    long? IdRegistroLdp, string? Disciplina, string? Revisao, string? FolderId, string? Pasta);

public enum IdentificacaoResultado { Encontrado, NaoEncontrado, Ambiguo, NomeInvalido }

/// <summary>Resultado da identificação pelo nome do arquivo. Documento só é preenchido quando Encontrado (inequívoco).</summary>
public sealed record IdentificacaoPorNome(IdentificacaoResultado Resultado, LdpDocumentoAtual? Documento, string? Campo, string? Codigo,
    IReadOnlyList<string> Codigos, string? RevisaoNoNome);

public static class UploadGovernadoRules
{
    private static readonly HashSet<string> Extensoes = new(StringComparer.OrdinalIgnoreCase)
    { ".pdf", ".dwg", ".dxf", ".doc", ".docx", ".xls", ".xlsx", ".xlsm", ".rvt", ".rfa", ".nwd", ".nwc", ".ifc", ".zip", ".txt", ".csv", ".xml" };

    public static string ChaveDisciplina(string? d) => ProjectPlanningRules.Key(d);

    /// <summary>
    /// Pré-valida um arquivo para a pasta de destino (puro, testável). raizProtegida = raiz da árvore protegida que contém o
    /// destino (null = destino fora das árvores → regra da LDP não aplicada). Disciplina não é validada.
    /// </summary>
    public static UploadValidacao Validar(string nomeArquivo, IReadOnlyList<LdpDocumentoAtual> ldp, PastaProtegida? raizProtegida, string destinoFolderId, string? destinoCaminho)
    {
        var nome = Path.GetFileName(nomeArquivo ?? "").Trim();
        if (raizProtegida is null)
            return new(nome, UploadGovernadoStatus.NaoProtegida, "Pasta fora das árvores protegidas: a regra da LDP não se aplica.",
                null, null, null, null, null, destinoFolderId, destinoCaminho);
        var id = IdentificarPorNomeArquivo(nome, ldp);
        switch (id.Resultado)
        {
            case IdentificacaoResultado.NomeInvalido:
                return Falha(nome, UploadGovernadoStatus.NomeInvalido, "Nome de arquivo inválido: use o número do documento seguido da extensão (ex.: PDF, DWG).");
            case IdentificacaoResultado.NaoEncontrado:
                return Falha(nome, UploadGovernadoStatus.ForaDaLdp, "Documento não encontrado na LDP atual do projeto.");
            case IdentificacaoResultado.Ambiguo:
                return Falha(nome, UploadGovernadoStatus.Ambiguo,
                    $"O nome corresponde a mais de um documento da LDP ({string.Join(", ", id.Codigos)}). Corrija o nome do arquivo ou a LDP.");
        }
        var doc = id.Documento!;
        return new(nome, UploadGovernadoStatus.Pronto, $"Documento na LDP — pasta protegida por \"{raizProtegida.Caminho}\".",
            id.Codigo, id.Campo, doc.IdRegistro, doc.Disciplina, id.RevisaoNoNome ?? doc.Revisao, destinoFolderId, destinoCaminho);
    }

    public const string CampoNa = "N&A Document Number", CampoCliente = "Client Document Number";

    /// <summary>
    /// IDENTIFICAÇÃO DO DOCUMENTO PELO NOME DO ARQUIVO — regra ÚNICA do N&A Connect (envio governado e Auditoria de Qualidade):
    /// tokens do código = prefixo dos tokens do nome (DocumentMatchingService.NomeCorrespondePorPrefixoDeTokens; sufixos de revisão
    /// ficam indiferentes); N&A Document Number primeiro e Client Document Number só se nenhum N&A corresponder.
    /// </summary>
    public static IdentificacaoPorNome IdentificarPorNomeArquivo(string nomeArquivo, IReadOnlyList<LdpDocumentoAtual> ldp)
    {
        var nome = Path.GetFileName(nomeArquivo ?? "").Trim();
        var tokens = DocumentMatchingService.TokenizarCodigoDocumento(nome);
        if (nome.Length == 0 || !Extensoes.Contains(Path.GetExtension(nome)) || tokens.Length < 2)
            return new(IdentificacaoResultado.NomeInvalido, null, null, null, Array.Empty<string>(), null);

        var porNa = ldp.Where(d => !string.IsNullOrWhiteSpace(d.NaDocumentNumber) && DocumentMatchingService.NomeCorrespondePorPrefixoDeTokens(nome, d.NaDocumentNumber!)).ToList();
        var (candidatos, campo) = porNa.Count > 0 ? (porNa, CampoNa)
            : (ldp.Where(d => !string.IsNullOrWhiteSpace(d.ClientDocumentNumber) && DocumentMatchingService.NomeCorrespondePorPrefixoDeTokens(nome, d.ClientDocumentNumber!)).ToList(), CampoCliente);
        string Codigo(LdpDocumentoAtual d) => campo == CampoNa ? d.NaDocumentNumber! : d.ClientDocumentNumber!;
        if (candidatos.Count == 0) return new(IdentificacaoResultado.NaoEncontrado, null, null, null, Array.Empty<string>(), null);
        // Mesmo código casando com mais de um documento (código repetido ou um código prefixo de outro) = ambíguo.
        if (candidatos.Count > 1) return new(IdentificacaoResultado.Ambiguo, null, campo, null, candidatos.Select(Codigo).Distinct().ToArray(), null);

        var doc = candidatos[0];
        var codigo = Codigo(doc);
        var resto = tokens.Skip(DocumentMatchingService.TokenizarCodigoDocumento(codigo).Length).ToArray();
        return new(IdentificacaoResultado.Encontrado, doc, campo, codigo, new[] { codigo }, resto.Length > 0 ? string.Join("-", resto) : null);
    }

    private static UploadValidacao Falha(string arquivo, string status, string mensagem) =>
        new(arquivo, status, mensagem, null, null, null, null, null, null, null);

    public static ConfiguracaoPastasProjeto ValidarConfiguracao(IReadOnlyList<PastaProtegida>? raizes)
    {
        var lista = (raizes ?? Array.Empty<PastaProtegida>()).Where(r => r != null && !string.IsNullOrWhiteSpace(r.FolderId)).ToArray();
        if (lista.Length > 200) throw new ArgumentException("Pastas protegidas demais.");
        if (lista.Any(r => r.FolderId.Length > 300 || (r.Caminho?.Length ?? 0) > 1000)) throw new ArgumentException("Pasta protegida inválida.");
        if (lista.GroupBy(r => r.FolderId, StringComparer.Ordinal).Any(g => g.Count() > 1)) throw new ArgumentException("Pasta protegida repetida.");
        return new(lista.Select(r => r with { Caminho = r.Caminho?.Trim() ?? "" }).ToArray());
    }

    /// <summary>
    /// ÁRVORE PROTEGIDA: sobe pelos pais (ID/parent do ACC) a partir da pasta de destino até achar uma raiz configurada (a própria
    /// pasta conta) ou chegar ao topo. Profundidade limitada e ciclo detectado — nunca laço infinito. null = fora das árvores.
    /// </summary>
    public static async Task<PastaProtegida?> RaizQueContemAsync(string destinoFolderId, IReadOnlyList<PastaProtegida> raizes,
        Func<string, Task<string?>> paiDe, int profundidadeMaxima = 64)
    {
        if (raizes.Count == 0 || string.IsNullOrWhiteSpace(destinoFolderId)) return null;
        var porId = raizes.ToDictionary(r => r.FolderId, StringComparer.Ordinal);
        var visitadas = new HashSet<string>(StringComparer.Ordinal);
        string? atual = destinoFolderId;
        for (int i = 0; atual != null && i <= profundidadeMaxima && visitadas.Add(atual); i++)
        {
            if (porId.TryGetValue(atual, out var raiz)) return raiz;
            atual = await paiDe(atual);
        }
        return null;
    }
}

/// <summary>
/// Configuração de pastas por projeto (JSON em App_Data, escrita atômica com .bak — mesmo padrão de TV/Logo; sem tabela nova).
/// </summary>
public sealed class PastasDocumentosStore(string filePath)
{
    private readonly SemaphoreSlim mutex = new(1, 1);
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    public string FilePath { get; } = Path.GetFullPath(filePath);

    public async Task<ConfiguracaoPastasProjeto> ReadAsync(string projectId, CancellationToken ct = default)
    {
        await mutex.WaitAsync(ct);
        try { return (await LerTudo(ct)).TryGetValue(projectId, out var c) ? c : ConfiguracaoPastasProjeto.Vazia; }
        finally { mutex.Release(); }
    }

    public async Task<ConfiguracaoPastasProjeto> SaveAsync(string projectId, IReadOnlyList<PastaProtegida> raizes, string? usuario, CancellationToken ct = default)
    {
        var validada = UploadGovernadoRules.ValidarConfiguracao(raizes) with { AtualizadoEm = DateTimeOffset.UtcNow, AtualizadoPor = usuario };
        await mutex.WaitAsync(ct);
        try
        {
            var tudo = await LerTudo(ct);
            tudo[projectId] = validada;
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            var temp = FilePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            await File.WriteAllBytesAsync(temp, JsonSerializer.SerializeToUtf8Bytes(tudo, Json), ct);
            if (File.Exists(FilePath)) File.Replace(temp, FilePath, FilePath + ".bak"); else File.Move(temp, FilePath);
            return validada;
        }
        finally { mutex.Release(); }
    }

    private async Task<Dictionary<string, ConfiguracaoPastasProjeto>> LerTudo(CancellationToken ct)
    {
        if (!File.Exists(FilePath)) return new(StringComparer.Ordinal);
        try
        {
            var lido = JsonSerializer.Deserialize<Dictionary<string, ConfiguracaoPastasProjeto>>(await File.ReadAllTextAsync(FilePath, ct), Json) ?? new(StringComparer.Ordinal);
            // Formato anterior (regras por disciplina) não tem "raizes": vira configuração vazia, nunca erro.
            return lido.ToDictionary(kv => kv.Key, kv => kv.Value with { Raizes = kv.Value.Raizes ?? Array.Empty<PastaProtegida>() }, StringComparer.Ordinal);
        }
        catch (JsonException) { throw new InvalidOperationException("Configuração de pastas corrompida. Restaure o arquivo .bak."); }
    }
}

/// <summary>Registro de auditoria do envio (JSON Lines em App_Data + ILogger). Nunca grava token nem conteúdo do arquivo.</summary>
public sealed record UploadLogEntrada(DateTimeOffset DataHora, string ProjectId, string? Usuario, string Arquivo, string? Documento,
    string? Pasta, string Resultado, string? Motivo, string? ItemId, string? VersionId);

public sealed class UploadGovernadoLog(string filePath, ILogger<UploadGovernadoLog> logger)
{
    private readonly SemaphoreSlim mutex = new(1, 1);
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task RegistrarAsync(UploadLogEntrada e, CancellationToken ct = default)
    {
        logger.LogInformation("[UPLOAD_LDP] Projeto={Projeto} Usuario={Usuario} Arquivo={Arquivo} Documento={Documento} Pasta={Pasta} Resultado={Resultado} Motivo={Motivo} Item={Item}",
            e.ProjectId, e.Usuario, e.Arquivo, e.Documento, e.Pasta, e.Resultado, e.Motivo, e.ItemId);
        await mutex.WaitAsync(ct);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(filePath))!);
            await File.AppendAllTextAsync(filePath, JsonSerializer.Serialize(e, Json) + Environment.NewLine, ct);
        }
        catch (IOException ex) { logger.LogWarning("[UPLOAD_LDP] Falha ao gravar log em arquivo ({Tipo}).", ex.GetType().Name); }
        finally { mutex.Release(); }
    }
}

public interface ILdpDocumentosAtuaisRepository
{
    Task<IReadOnlyList<LdpDocumentoAtual>> ReadAsync(string projectId, CancellationToken ct);

    /// <summary>
    /// LDP atual + colunas EXTRAS pedidas (ex.: campos auditados pelo template). As colunas são validadas contra o schema real da LDP
    /// antes de entrar na query (nada vindo do frontend entra direto no SQL). Format é sempre carregado.
    /// </summary>
    Task<IReadOnlyList<LdpDocumentoAtual>> ReadAsync(string projectId, IReadOnlyCollection<string> extraColumns, CancellationToken ct) => ReadAsync(projectId, ct);
}

/// <summary>LDP ATUAL do projeto: a MESMA carga (PmoLdpSnapshot.Tabela) usada por todas as telas — Tipo 1 e Tipo 2.</summary>
public sealed class LdpDocumentosAtuaisRepository(IConfiguration configuration, APS.WebApp.Services.DocumentQuality.ILdpColumnCatalog catalog)
    : ILdpDocumentosAtuaisRepository
{
    // Identidade/roteamento (sempre) + Format (perfil de folha da auditoria).
    private static readonly string[] Base = { "ID_Registro", "NA_Document_Number", "Client_Document_Number", "Discipline", "Document_Title", "Revision", "Format" };

    public static string BuildQuery(IEnumerable<string> extrasValidadas) =>
        $"SELECT {string.Join(", ", Base.Concat(extrasValidadas).Select(APS.WebApp.Services.DocumentQuality.LdpColumnCatalogRules.Quote))}\n" +
        $"FROM {PmoLdpSnapshot.Tabela} WHERE {LdpItemRules.SqlIsDocument("LdpAtual")} {PmoLdpSnapshot.OpcaoPlano};";

    /// <summary>Consulta padrão (envio governado): identidade + Format.</summary>
    public static readonly string Query = BuildQuery(Array.Empty<string>());

    public Task<IReadOnlyList<LdpDocumentoAtual>> ReadAsync(string projectId, CancellationToken ct) => ReadAsync(projectId, Array.Empty<string>(), ct);

    public async Task<IReadOnlyList<LdpDocumentoAtual>> ReadAsync(string projectId, IReadOnlyCollection<string> extraColumns, CancellationToken ct)
    {
        var extras = Array.Empty<string>();
        if (extraColumns.Count > 0)
        {
            var colunas = await catalog.GetColumnsAsync(ct);
            extras = extraColumns.Select(c => APS.WebApp.Services.DocumentQuality.LdpColumnCatalogRules.Canonical(c, colunas))
                .Where(c => !Base.Contains(c, StringComparer.OrdinalIgnoreCase)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        }
        await using var connection = new SqlConnection(configuration.GetConnectionString("LocalConnection")
            ?? throw new InvalidOperationException("Conexão indisponível."));
        await connection.OpenAsync(ct);
        await using var command = new SqlCommand(BuildQuery(extras), connection) { CommandTimeout = 60 };
        command.Parameters.Add("@projectId", SqlDbType.NVarChar, 100).Value = projectId;
        await using var reader = await command.ExecuteReaderAsync(ct);
        string? T(string c) => Texto(reader[c]);
        var lista = new List<LdpDocumentoAtual>();
        while (await reader.ReadAsync(ct))
            lista.Add(new(Convert.ToInt64(reader["ID_Registro"]), T("NA_Document_Number"), T("Client_Document_Number"), T("Discipline"), T("Document_Title"), T("Revision"),
                extras.Append("Format").ToDictionary(c => c, T, StringComparer.OrdinalIgnoreCase)));
        return lista;
    }

    /// <summary>Valor da LDP como texto comparável (datas ISO, números invariantes).</summary>
    public static string? Texto(object valor) => valor switch
    {
        DBNull or null => null,
        DateTime d => d.TimeOfDay == TimeSpan.Zero ? d.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture) : d.ToString("yyyy-MM-dd HH:mm", System.Globalization.CultureInfo.InvariantCulture),
        IFormattable f => f.ToString(null, System.Globalization.CultureInfo.InvariantCulture),
        _ => Convert.ToString(valor, System.Globalization.CultureInfo.InvariantCulture),
    };
}
