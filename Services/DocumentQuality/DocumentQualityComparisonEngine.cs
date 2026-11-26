using APS.WebApp.Services.UploadGovernado;

namespace APS.WebApp.Services.DocumentQuality;

public static class DocumentQualityComparisonEngine
{
    public static DocumentQualityDocumentResult Compare(
        string fileName,
        string folder,
        string projectIdAcc,
        PdfRegionReadResult reads,
        IReadOnlyList<LdpDocumentoAtual> ldp)
    {
        var byField = reads.Fields.ToDictionary(r => r.TargetField, StringComparer.OrdinalIgnoreCase);
        var na = ReadValue(byField, DocumentQualityFields.NaDocumentNumber);
        var client = ReadValue(byField, DocumentQualityFields.ClientDocumentNumber);

        // 1) NOME DO ARQUIVO — mesma regra do envio governado (DocumentMatchingService; N&A primeiro, Cliente como alternativa).
        //    O carimbo NÃO é necessário para saber se o documento existe na LDP quando o nome já identifica o documento.
        var byName = UploadGovernadoRules.IdentificarPorNomeArquivo(fileName, ldp);
        LdpDocumentoAtual document;
        string identifiedBy;
        if (byName.Resultado == IdentificacaoResultado.Encontrado)
        {
            document = byName.Documento!;
            identifiedBy = $"NOME DO ARQUIVO ({byName.Campo})";
        }
        else
        {
            // 2) Só quando o nome não identifica de forma inequívoca: números lidos no carimbo (N&A, depois Cliente).
            var candidates = Match(ldp, d => d.NaDocumentNumber, DocumentQualityFields.NaDocumentNumber, na);
            var identificationField = DocumentQualityFields.NaDocumentNumber;
            if (candidates.Count == 0)
            {
                candidates = Match(ldp, d => d.ClientDocumentNumber, DocumentQualityFields.ClientDocumentNumber, client);
                identificationField = DocumentQualityFields.ClientDocumentNumber;
            }
            if (candidates.Count > 1)
                return Failure(fileName, folder, reads, DocumentQualityStatus.Ambiguo,
                    $"O valor lido em {DocumentQualityFields.Label(identificationField)} corresponde a mais de uma linha da LDP.");
            if (candidates.Count == 0) return NotIdentified(fileName, folder, reads, byName, !string.IsNullOrWhiteSpace(na) || !string.IsNullOrWhiteSpace(client));
            document = candidates[0];
            identifiedBy = $"CARIMBO ({DocumentQualityFields.Label(identificationField)})";
        }

        // 3) Documento identificado: as regiões do PDF servem para AUDITAR os dados (inclusive o próprio número).
        var fields = reads.Fields.Select(read => CompareField(read, Expected(document, projectIdAcc, read.TargetField))).ToArray();
        var unread = fields.Any(field => field.Status == DocumentFieldStatus.NaoLido);
        var divergent = fields.Any(field => field.Status == DocumentFieldStatus.Divergente);
        var notCalibrated = fields.Where(field => field.Status == DocumentFieldStatus.NaoCalibrado).Select(field => field.Label).ToArray();
        var statusFinal = divergent ? DocumentQualityStatus.Divergencia : unread ? DocumentQualityStatus.ErroLeitura
            : notCalibrated.Length > 0 ? DocumentQualityStatus.NaoCalibrado : DocumentQualityStatus.Conforme;
        var reasonFinal = statusFinal switch
        {
            DocumentQualityStatus.Divergencia => "Um ou mais campos do PDF divergem da LDP atual.",
            DocumentQualityStatus.ErroLeitura => "Um ou mais campos configurados não possuem texto incorporado na região.",
            DocumentQualityStatus.NaoCalibrado => $"Campo(s) não calibrado(s) para este formato: {string.Join(", ", notCalibrated)}.",
            _ => "Todos os campos configurados conferem com a LDP atual.",
        };

        return new DocumentQualityDocumentResult
        {
            FileName = fileName, Folder = folder, LdpRecordId = document.IdRegistro,
            NaDocumentNumber = document.NaDocumentNumber, ClientDocumentNumber = document.ClientDocumentNumber,
            Document = document.NaDocumentNumber ?? document.ClientDocumentNumber,
            TitleExpected = document.Titulo, TitleFound = Find(fields, DocumentQualityFields.DocumentTitle)?.Found,
            RevisionExpected = document.Revisao, RevisionFound = Find(fields, DocumentQualityFields.Revision)?.Found,
            Discipline = document.Disciplina, Status = statusFinal, Reason = reasonFinal, SheetType = reads.SheetType, Fields = fields,
            IdentifiedBy = identifiedBy, FormatLdp = document.Formato ?? reads.LdpFormat, SheetSource = reads.SheetSource,
        };
    }

    /// <summary>
    /// Nem o nome nem o carimbo identificaram o documento. Situações DISTINTAS: nome com código fora da LDP → FORA DA LDP; código ambíguo →
    /// AMBÍGUO; nome sem código e carimbo com número fora da LDP → FORA DA LDP; nome sem código e carimbo ilegível → NÃO IDENTIFICADO /
    /// ERRO DE LEITURA (nunca FORA DA LDP só por falha de leitura).
    /// </summary>
    private static DocumentQualityDocumentResult NotIdentified(string fileName, string folder, PdfRegionReadResult reads, IdentificacaoPorNome byName, bool anyIdentifierRead)
    {
        var anyAvailable = reads.Fields.Any(x => x.Available);
        return byName.Resultado switch
        {
            IdentificacaoResultado.Ambiguo => Failure(fileName, folder, reads, DocumentQualityStatus.Ambiguo,
                $"O nome do arquivo corresponde a mais de um documento da LDP ({string.Join(", ", byName.Codigos)}) e o carimbo não desfez a ambiguidade."),
            IdentificacaoResultado.NaoEncontrado => Failure(fileName, folder, reads, DocumentQualityStatus.ForaDaLdp,
                anyIdentifierRead ? "O nome do arquivo e os números lidos no carimbo não correspondem a nenhuma linha da LDP atual."
                    : "O nome do arquivo não corresponde a nenhuma linha da LDP atual."),
            _ when anyIdentifierRead => Failure(fileName, folder, reads, DocumentQualityStatus.ForaDaLdp,
                "O nome do arquivo não contém um número de documento e os números lidos no carimbo não foram encontrados na LDP atual."),
            _ when anyAvailable => Failure(fileName, folder, reads, DocumentQualityStatus.NaoIdentificado,
                "O nome do arquivo não contém um número de documento e o carimbo não trouxe NÚMERO N&A nem NÚMERO CLIENTE legíveis."),
            _ => Failure(fileName, folder, reads, DocumentQualityStatus.ErroLeitura,
                "O nome do arquivo não contém um número de documento e nenhuma região configurada contém texto incorporado; necessita OCR de imagem."),
        };
    }

    public static void MarkDuplicates(IReadOnlyList<DocumentQualityDocumentResult> documents)
    {
        foreach (var group in documents.Where(d => d.LdpRecordId.HasValue).GroupBy(d => d.LdpRecordId!.Value).Where(g => g.Count() > 1))
            foreach (var document in group)
            {
                document.Status = DocumentQualityStatus.Duplicado;
                document.Reason = "Mais de um PDF da pasta foi identificado como o mesmo documento da LDP.";
            }
    }

    private static List<LdpDocumentoAtual> Match(IReadOnlyList<LdpDocumentoAtual> ldp, Func<LdpDocumentoAtual, string?> selector, string field, string? found)
    {
        if (string.IsNullOrWhiteSpace(found)) return new();
        var normalized = DocumentQualityFields.Normalize(field, found);
        return ldp.Where(d => DocumentQualityFields.Normalize(field, selector(d)) == normalized).ToList();
    }

    private static string? ReadValue(IReadOnlyDictionary<string, PdfRegionRead> reads, string field) =>
        reads.TryGetValue(field, out var read) && read.Available ? read.Value : null;

    private static string? Expected(LdpDocumentoAtual document, string projectIdAcc, string field) => field switch
    {
        DocumentQualityFields.ProjectIdAcc => projectIdAcc,
        DocumentQualityFields.NaDocumentNumber => document.NaDocumentNumber,
        DocumentQualityFields.ClientDocumentNumber => document.ClientDocumentNumber,
        DocumentQualityFields.DocumentTitle => document.Titulo,
        DocumentQualityFields.Revision => document.Revisao,
        DocumentQualityFields.Discipline => document.Disciplina,
        _ => document.Extra(field), // demais campos do catálogo: coluna homônima da LDP (Format, Area, Doc_Type_Code, Issued_Purpose)
    };

    private static DocumentQualityFieldResult CompareField(PdfRegionRead read, string? expected)
    {
        var expectedNormalized = DocumentQualityFields.Normalize(read.TargetField, expected);
        if (read.NotCalibrated)
            return new(read.TargetField, DocumentQualityFields.Label(read.TargetField), expected, null, DocumentFieldStatus.NaoCalibrado, DocumentFieldStatus.MotivoNaoCalibrado)
            { ExpectedNormalized = expectedNormalized };
        if (!read.Available)
            return new(read.TargetField, DocumentQualityFields.Label(read.TargetField), expected, null, DocumentFieldStatus.NaoLido, read.Reason)
            { ExpectedNormalized = expectedNormalized };
        var foundNormalized = DocumentQualityFields.Normalize(read.TargetField, read.Value);
        var equal = expectedNormalized == foundNormalized;
        return new(read.TargetField, DocumentQualityFields.Label(read.TargetField), expected, read.Value,
            equal ? DocumentFieldStatus.Ok : DocumentFieldStatus.Divergente, equal ? null : "Valor diferente da LDP atual.")
            { ExpectedNormalized = expectedNormalized, FoundNormalized = foundNormalized };
    }

    private static DocumentQualityDocumentResult Failure(string fileName, string folder, PdfRegionReadResult reads, string status, string reason)
    {
        string? Value(string field) => reads.Fields.FirstOrDefault(r => r.TargetField == field)?.Value;
        var na = Value(DocumentQualityFields.NaDocumentNumber);
        var client = Value(DocumentQualityFields.ClientDocumentNumber);
        return new()
        {
            FileName = fileName, Folder = folder, Status = status, Reason = reason, SheetType = reads.SheetType,
            NaDocumentNumber = na, ClientDocumentNumber = client, Document = na ?? client,
            TitleFound = Value(DocumentQualityFields.DocumentTitle), RevisionFound = Value(DocumentQualityFields.Revision),
            // Disciplina vem SÓ da LDP do documento identificado (nunca do carimbo nem da pasta): não identificado = sem disciplina.
            Discipline = null, SheetSource = reads.SheetSource,
            Fields = reads.Fields.Select(read => new DocumentQualityFieldResult(read.TargetField, DocumentQualityFields.Label(read.TargetField), null,
                read.Value, read.NotCalibrated ? DocumentFieldStatus.NaoCalibrado : read.Available ? DocumentFieldStatus.Divergente : DocumentFieldStatus.NaoLido, read.Reason)
                { FoundNormalized = read.Available ? DocumentQualityFields.Normalize(read.TargetField, read.Value) : null }).ToArray(),
        };
    }

    private static DocumentQualityFieldResult? Find(IEnumerable<DocumentQualityFieldResult> fields, string name) => fields.FirstOrDefault(f => f.Field == name);
}
