using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace APS.WebApp.Services.DocumentQuality;

public static class DocumentQualityFields
{
    public const string ProjectIdAcc = "Project_ID_ACC";
    public const string NaDocumentNumber = "NA_Document_Number";
    public const string ClientDocumentNumber = "Client_Document_Number";
    public const string DocumentTitle = "Document_Title";
    public const string Revision = "Revision";
    public const string Discipline = "Discipline";
    public const string Format = "Format";
    public const string Area = "Area";
    public const string DocTypeCode = "Doc_Type_Code";
    public const string IssuedPurpose = "Issued_Purpose";

    public static readonly IReadOnlyDictionary<string, string> Labels = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        [NaDocumentNumber] = "NÚMERO N&A",
        [ClientDocumentNumber] = "NÚMERO CLIENTE",
        [DocumentTitle] = "TÍTULO DO DOCUMENTO",
        [Revision] = "REVISÃO",
        [Discipline] = "DISCIPLINA",
        [ProjectIdAcc] = "PROJETO",
        [Format] = "FORMATO",
        [Area] = "ÁREA",
        [DocTypeCode] = "TIPO DE DOCUMENTO",
        [IssuedPurpose] = "FINALIDADE DE EMISSÃO",
        ["No_Pages"] = "Nº DE FOLHAS",
        ["Transmittal_Number"] = "NÚMERO DA GRD",
        ["Discipline_Activity"] = "ATIVIDADE DA DISCIPLINA",
        ["Seq"] = "SEQUENCIAL",
        ["Document_Status"] = "STATUS DO DOCUMENTO",
        ["Emission_Date"] = "DATA DE EMISSÃO",
    };

    // O catálogo de campos auditáveis é DINÂMICO (LdpColumnCatalog: todas as colunas reais de dbo.Ldp_Unificada).
    // Labels acima são só rótulos amigáveis; coluna sem rótulo aparece pelo próprio nome.

    /// <summary>Forma de identificador de coluna (letras, dígitos e _ , até 100) — a EXISTÊNCIA é validada no catálogo da LDP.</summary>
    public static bool IsValidIdentifier(string? field) => field != null && Regex.IsMatch(field, "^[A-Za-z_][A-Za-z0-9_]{0,99}$");

    public static string Label(string field) => Labels.TryGetValue(field, out var value) ? value : field;

    public static string Normalize(string field, string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        var normalized = Regex.Replace(value.Trim(), @"\s+", " ").ToUpperInvariant();
        if (field is NaDocumentNumber or ClientDocumentNumber or ProjectIdAcc or Format or Area or DocTypeCode)
            return Regex.Replace(normalized, @"\s", string.Empty);
        if (field == Revision)
        {
            normalized = Regex.Replace(normalized, @"^(REV(?:ISÃO|ISAO)?|R)\s*[:.\-]?\s*", string.Empty, RegexOptions.IgnoreCase);
            return Regex.Replace(normalized, @"\s", string.Empty);
        }

        var decomposed = normalized.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(decomposed.Length);
        foreach (var character in decomposed)
            if (CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark) sb.Append(character);
        return sb.ToString().Normalize(NormalizationForm.FormC);
    }
}

public static class DocumentQualityStatus
{
    public const string Conforme = "CONFORME";
    public const string Divergencia = "DIVERGÊNCIA";
    public const string ForaDaLdp = "FORA DA LDP";
    public const string NaoIdentificado = "NÃO IDENTIFICADO";
    public const string Duplicado = "DUPLICADO";
    /// <summary>O código (do nome do arquivo ou do carimbo) corresponde a mais de uma linha da LDP — não é "fora da LDP".</summary>
    public const string Ambiguo = "AMBÍGUO";
    /// <summary>Documento identificado, sem divergência, mas com campo(s) ativo(s) sem coordenada no formato usado.</summary>
    public const string NaoCalibrado = "NÃO CALIBRADO";
    public const string ErroLeitura = "ERRO DE LEITURA";
}

public static class DocumentFieldStatus
{
    public const string Ok = "OK";
    public const string Divergente = "DIVERGENTE";
    public const string NaoLido = "NÃO LIDO";
    /// <summary>Campo ativo no template, mas o perfil (formato) usado não tem coordenada para ele — diferente de NÃO LIDO.</summary>
    public const string NaoCalibrado = "NÃO CALIBRADO";
    public const string MotivoNaoCalibrado = "CAMPO NÃO CALIBRADO PARA ESTE FORMATO";
}

public sealed record OcrPackage(int Id, string Name, DateTime CreatedAtUtc);

/// <summary>Coordenadas em pontos PDF, origem no canto superior esquerdo.</summary>
public sealed record OcrTemplateRegion(
    int Id,
    int PackageId,
    string SheetType,
    string TargetField,
    decimal X,
    decimal Y,
    decimal Width,
    decimal Height,
    int Page,
    decimal ReferenceWidth,
    decimal ReferenceHeight);

public sealed record SaveOcrTemplateRegion(
    string CampoDestino,
    decimal X,
    decimal Y,
    decimal Width,
    decimal Height,
    int Pagina,
    decimal ReferenceWidth,
    decimal ReferenceHeight);

public sealed record DocumentQualityProjectConfiguration(
    string ProjectIdAcc,
    string? ProjectName,
    int? PackageId,
    string? PackageName,
    string? AuditFolderId,
    string? AuditFolderPath,
    DateTime? UpdatedAtUtc)
{
    public void EnsureReady()
    {
        if (!PackageId.HasValue) throw new DocumentQualityConfigurationException("O projeto não possui Template de Auditoria Documental configurado.");
        if (string.IsNullOrWhiteSpace(AuditFolderId)) throw new DocumentQualityConfigurationException("O projeto não possui Pasta de Auditoria configurada.");
    }
}

public sealed class DocumentQualityConfigurationException(string message) : InvalidOperationException(message);

public sealed record SaveDocumentQualityProjectConfiguration(int? PackageId, string? AuditFolderId, string? AuditFolderPath);

public sealed record PdfRegionRead(string TargetField, string? Value, bool Available, string? Reason, int Page, string SheetType)
{
    /// <summary>O perfil (formato) usado não tem coordenada para este campo ativo.</summary>
    public bool NotCalibrated { get; init; }
}

public sealed record PdfRegionReadResult(string SheetType, IReadOnlyList<PdfRegionRead> Fields)
{
    /// <summary>Por que este conjunto de regiões (formato de folha) foi usado: Format da LDP ou dimensão da página.</summary>
    public string? SheetSource { get; init; }
    /// <summary>Format da LDP usado na escolha do perfil.</summary>
    public string? LdpFormat { get; init; }
}

public sealed record DocumentQualityFieldResult(string Field, string Label, string? Expected, string? Found, string Status, string? Reason)
{
    /// <summary>Diagnóstico: valores como foram COMPARADOS (após a normalização segura de espaços/caixa/acentos).</summary>
    public string? ExpectedNormalized { get; init; }
    public string? FoundNormalized { get; init; }
}

public sealed class DocumentQualityDocumentResult
{
    public string FileName { get; init; } = string.Empty;
    public string Folder { get; init; } = string.Empty;
    public long? LdpRecordId { get; init; }
    public string? NaDocumentNumber { get; init; }
    public string? ClientDocumentNumber { get; init; }
    public string? Document { get; init; }
    public string? TitleExpected { get; init; }
    public string? TitleFound { get; init; }
    public string? RevisionExpected { get; init; }
    public string? RevisionFound { get; init; }
    public string? Discipline { get; init; }
    public string Status { get; set; } = DocumentQualityStatus.NaoIdentificado;
    public string Reason { get; set; } = string.Empty;
    public string? SheetType { get; init; }
    /// <summary>Como o documento foi identificado: "NOME DO ARQUIVO (N&A Document Number)", "CARIMBO (NÚMERO N&A)"... null = não identificado.</summary>
    public string? IdentifiedBy { get; init; }
    /// <summary>Formato da folha na LDP (Format) do documento identificado.</summary>
    public string? FormatLdp { get; init; }
    /// <summary>Por que o conjunto de regiões (formato) foi escolhido.</summary>
    public string? SheetSource { get; init; }
    public IReadOnlyList<DocumentQualityFieldResult> Fields { get; init; } = Array.Empty<DocumentQualityFieldResult>();
}

public sealed record DocumentQualitySummary(int Total, int Compliant, int Divergent, int OutsideLdp, int NotIdentified, int Duplicates, int ReadErrors)
{
    /// <summary>Código que corresponde a mais de uma linha da LDP (separado de "fora da LDP").</summary>
    public int Ambiguous { get; init; }
    public int NotCalibrated { get; init; }
}

public sealed record DocumentQualityRunResult(
    Guid ExecutionId,
    DateTime StartedAtUtc,
    DateTime FinishedAtUtc,
    string ProjectIdAcc,
    string? ProjectName,
    string Folder,
    int PackageId,
    string? PackageName,
    DocumentQualitySummary Summary,
    IReadOnlyList<DocumentQualityDocumentResult> Documents,
    bool AuditPersistenceSucceeded,
    string? AuditPersistenceError);

public static class FormatMappingMode
{
    public const string Perfil = "PERFIL", Dimensao = "DIMENSAO", Ignorar = "IGNORAR";
    public static bool IsValid(string? m) => m is Perfil or Dimensao or Ignorar;
}

/// <summary>Mapeamento do TEMPLATE: valor da coluna Format da LDP → perfil calibrado (TipoFolha) / detectar pela dimensão / ignorar.</summary>
public sealed record FormatMapping(string ValorFormatoLdp, string Modo, string? TipoFolha);

/// <summary>Campo auditado pelo TEMPLATE (comum a todos os formatos). Coordenadas ficam por formato em config.PacoteOcrCoordenada.</summary>
public sealed record TemplateField(string Field, int Order, bool Active);

/// <summary>Perfil (TipoFolha) escolhido para o PDF e o diagnóstico de por quê. SheetType null = nenhum perfil utilizável.</summary>
public sealed record ProfileResolution(string? SheetType, string Origin, string? LdpFormat);

/// <summary>
/// PERFIL DE FOLHA por documento (01/10/2026): NÃO existe mapeamento global de Format → ANSI ("D" pode ser ANSI D, ARCH D ou código do
/// cliente). Quem traduz é o TEMPLATE (config.PacoteOcrFormato). A dimensão física da página SEMPRE protege: um perfil cuja página de
/// referência não confere (±12% em cada medida) nunca é usado — nem pelo mapeamento, nem pela dimensão.
/// </summary>
public static class DocumentQualityProfiles
{
    public const double Tolerancia = .12;

    public static ProfileResolution Resolve(double pageWidth, double pageHeight, IReadOnlyList<OcrTemplateRegion> regions, string? ldpFormat,
        IReadOnlyList<FormatMapping> mappings)
    {
        var perfis = regions.GroupBy(r => r.SheetType, StringComparer.OrdinalIgnoreCase)
            .Select(g => (Sheet: g.Key, W: (double)g.First().ReferenceWidth, H: (double)g.First().ReferenceHeight)).ToArray();
        var formato = string.IsNullOrWhiteSpace(ldpFormat) ? null : ldpFormat.Trim();
        var mapa = formato == null ? null
            : mappings.FirstOrDefault(m => string.Equals(m.ValorFormatoLdp.Trim(), formato, StringComparison.OrdinalIgnoreCase));

        if (mapa?.Modo == FormatMappingMode.Ignorar)
            return new(null, $"FORMATO {formato} MARCADO COMO NÃO SUPORTADO NO TEMPLATE", formato);

        string? aviso = null;
        if (mapa?.Modo == FormatMappingMode.Perfil)
        {
            var perfil = perfis.FirstOrDefault(p => string.Equals(p.Sheet, mapa.TipoFolha, StringComparison.OrdinalIgnoreCase));
            if (perfil.Sheet == null) aviso = $"o perfil {mapa.TipoFolha} do mapeamento não tem regiões calibradas";
            else if (Confere(pageWidth, pageHeight, perfil.W, perfil.H)) return new(perfil.Sheet, $"MAPEAMENTO DO TEMPLATE ({formato} → {perfil.Sheet})", formato);
            else aviso = $"a página ({pageWidth:0}×{pageHeight:0}) não tem a dimensão do perfil {perfil.Sheet} ({perfil.W:0}×{perfil.H:0}) do mapeamento";
        }

        var porDimensao = perfis.Where(p => Confere(pageWidth, pageHeight, p.W, p.H))
            .OrderBy(p => Math.Abs(Math.Log(pageWidth / Math.Max(1d, p.W))) + Math.Abs(Math.Log(pageHeight / Math.Max(1d, p.H)))).FirstOrDefault();
        var motivo = aviso != null ? $"; {aviso}" : formato != null && mapa == null ? $"; Format {formato} sem mapeamento no template" : "";
        return porDimensao.Sheet != null
            ? new(porDimensao.Sheet, $"DIMENSÃO DA PÁGINA{motivo}", formato)
            : new(null, $"NENHUM PERFIL CALIBRADO PARA A DIMENSÃO DA PÁGINA ({pageWidth:0}×{pageHeight:0}){motivo}", formato);
    }

    /// <summary>Mesma orientação e medidas dentro da tolerância (coordenadas só valem na folha em que foram calibradas).</summary>
    public static bool Confere(double w, double h, double rw, double rh) =>
        rw > 0 && rh > 0 && Math.Abs(w / rw - 1) <= Tolerancia && Math.Abs(h / rh - 1) <= Tolerancia;
}

/// <summary>Plano de leitura: regiões do template (todos os perfis), campos ATIVOS do template, Format da LDP e mapeamentos.</summary>
public sealed record DocumentQualityReadPlan(IReadOnlyList<OcrTemplateRegion> Regions, IReadOnlyList<string> ActiveFields, string? LdpFormat,
    IReadOnlyList<FormatMapping> Mappings);
