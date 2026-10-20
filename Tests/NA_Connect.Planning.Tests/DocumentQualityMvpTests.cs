using System.Text;
using APS.WebApp.Controllers;
using APS.WebApp.Services.DocumentQuality;
using APS.WebApp.Services.OperationalAudit;
using APS.WebApp.Services.OperationalAudit.Persistence;
using APS.WebApp.Services.UploadGovernado;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;

namespace NA_Connect.Planning.Tests;

public sealed class DocumentQualityMvpTests
{
    private static readonly LdpDocumentoAtual[] Ldp =
    {
        new(1, "CHS001-000-PC1-RE-001", "CLI-001", "PROCESSO", "Relatório de Processo", "02"),
    };

    [Fact]
    public async Task A_SalvarECarregarCoordenadas_PreservaContrato()
    {
        var repository = new MemoryTemplateRepository();
        var controller = new OcrTemplateController(repository, new CatalogoReal(), NullLogger<OcrTemplateController>.Instance);
        var request = new[] { new SaveOcrTemplateRegion(DocumentQualityFields.Revision, 10, 20, 30, 12, 2, 612, 792) };
        var saved = await controller.SaveRegions(1, "A4", request, default);
        Assert.IsType<OkObjectResult>(saved);
        var loaded = await repository.GetRegionsAsync(1, "A4");
        var region = Assert.Single(loaded);
        Assert.Equal((DocumentQualityFields.Revision, 2, 612m, 792m), (region.TargetField, region.Page, region.ReferenceWidth, region.ReferenceHeight));
    }

    [Fact]
    public async Task B_C_TextoDentroDaRegiaoELetrasForaSaoIgnoradas()
    {
        await using var pdf = new MemoryStream(CreatePdf((50, 700, "DENTRO"), (350, 700, "FORA")));
        var region = Region(DocumentQualityFields.NaDocumentNumber, 40, 70, 130, 35);
        var result = await new PdfPigRegionTextReader().ReadAsync(pdf, new[] { region });
        var field = Assert.Single(result.Fields);
        Assert.True(field.Available);
        Assert.Contains("DENTRO", field.Value);
        Assert.DoesNotContain("FORA", field.Value);
    }

    [Fact]
    public void D_DocumentoIdentificadoPrimeiroPorNumeroNa()
    {
        var result = Compare(Read(DocumentQualityFields.NaDocumentNumber, "CHS001-000-PC1-RE-001"));
        Assert.Equal(1, result.LdpRecordId);
        Assert.NotEqual(DocumentQualityStatus.ForaDaLdp, result.Status);
    }

    [Fact]
    public void E_FallbackUsaNumeroClienteQuandoNaNaoCasa()
    {
        var result = Compare(Read(DocumentQualityFields.NaDocumentNumber, "NAO-EXISTE"), Read(DocumentQualityFields.ClientDocumentNumber, "CLI-001"));
        Assert.Equal(1, result.LdpRecordId);
    }

    [Fact]
    public void F_RevisaoIgual_FicaOk()
    {
        var result = Compare(Read(DocumentQualityFields.NaDocumentNumber, "CHS001-000-PC1-RE-001"), Read(DocumentQualityFields.Revision, "REV 02"));
        Assert.Equal(DocumentFieldStatus.Ok, result.Fields.Single(x => x.Field == DocumentQualityFields.Revision).Status);
    }

    [Fact]
    public void G_RevisaoDivergente_GeraDivergencia()
    {
        var result = Compare(Read(DocumentQualityFields.NaDocumentNumber, "CHS001-000-PC1-RE-001"), Read(DocumentQualityFields.Revision, "01"));
        Assert.Equal(DocumentQualityStatus.Divergencia, result.Status);
    }

    [Fact]
    public void H_TituloDivergente_GeraDetalheDoCampo()
    {
        var result = Compare(Read(DocumentQualityFields.NaDocumentNumber, "CHS001-000-PC1-RE-001"), Read(DocumentQualityFields.DocumentTitle, "Outro título"));
        var field = result.Fields.Single(x => x.Field == DocumentQualityFields.DocumentTitle);
        Assert.Equal(DocumentFieldStatus.Divergente, field.Status);
        Assert.Equal("Relatório de Processo", field.Expected);
    }

    [Fact]
    public void I_DocumentoAusenteDaLdp_FicaForaDaLdp()
    {
        var result = Compare(Read(DocumentQualityFields.NaDocumentNumber, "CHS001-000-PC1-RE-999"));
        Assert.Equal(DocumentQualityStatus.ForaDaLdp, result.Status);
    }

    [Fact]
    public async Task J_RegiaoSemTexto_RetornaNecessitaOcrDeImagem()
    {
        await using var pdf = new MemoryStream(CreatePdf((50, 700, "TEXTO")));
        var result = await new PdfPigRegionTextReader().ReadAsync(pdf, new[] { Region(DocumentQualityFields.Revision, 400, 400, 80, 30) });
        var field = Assert.Single(result.Fields);
        Assert.False(field.Available);
        Assert.Contains("OCR DE IMAGEM", field.Reason);
    }

    [Fact]
    public void K_ProjetoSemTemplate_ErroExplicito()
    {
        var config = new DocumentQualityProjectConfiguration("b.1", "Teste", null, null, "folder", "Project Files/Audit", null);
        Assert.Contains("Template", Assert.Throws<DocumentQualityConfigurationException>(config.EnsureReady).Message);
    }

    [Fact]
    public void L_ProjetoSemPasta_ErroExplicito()
    {
        var config = new DocumentQualityProjectConfiguration("b.1", "Teste", 1, "Template", null, null, null);
        Assert.Contains("Pasta", Assert.Throws<DocumentQualityConfigurationException>(config.EnsureReady).Message);
    }

    [Fact]
    public async Task M_ResultadosSaoPersistidosEmOperationalAudit()
    {
        var repository = new CapturingAuditRepository();
        var persistence = new DocumentQualityOperationalAuditPersistence(() => repository, NullLogger<DocumentQualityOperationalAuditPersistence>.Instance);
        var document = Compare(Read(DocumentQualityFields.NaDocumentNumber, "CHS001-000-PC1-RE-001"), Read(DocumentQualityFields.Revision, "01"));
        var result = await persistence.PersistAsync(Guid.NewGuid(), DateTime.UtcNow, "b.11111111-1111-1111-1111-111111111111", "Projeto", new[] { document }, "teste");
        Assert.True(result.Succeeded);
        Assert.True(repository.Started && repository.Finished);
        Assert.NotNull(repository.ProjectData);
        Assert.Contains(repository.ProjectData!.Findings, finding => finding.Campo == DocumentQualityFields.Revision && finding.ValorEsperado == "02" && finding.ValorEncontrado == "01");
    }

    [Fact]
    public void PersistenciaNaoMaterializaAuditRepositoryAoConstruir()
    {
        var persistence = new DocumentQualityOperationalAuditPersistence(
            () => throw new InvalidOperationException("AuditRepositoryConnection ausente."),
            NullLogger<DocumentQualityOperationalAuditPersistence>.Instance);

        Assert.NotNull(persistence);
    }

    [Fact]
    public void ResultadoDuplicado_MarcaTodosOsArquivosDoMesmoDocumento()
    {
        var first = Compare(Read(DocumentQualityFields.NaDocumentNumber, "CHS001-000-PC1-RE-001"));
        var second = Compare(Read(DocumentQualityFields.ClientDocumentNumber, "CLI-001"));
        DocumentQualityComparisonEngine.MarkDuplicates(new[] { first, second });
        Assert.All(new[] { first, second }, item => Assert.Equal(DocumentQualityStatus.Duplicado, item.Status));
    }

    [Fact]
    public async Task RelatorioCsv_ContemLayoutDeDocumentControl()
    {
        var directory = Path.Combine(Path.GetTempPath(), "doc-quality-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            var document = Compare(Read(DocumentQualityFields.NaDocumentNumber, "CHS001-000-PC1-RE-001"), Read(DocumentQualityFields.Revision, "02"));
            var execution = Guid.NewGuid();
            var result = new DocumentQualityRunResult(execution, DateTime.UtcNow, DateTime.UtcNow, "b.11111111-1111-1111-1111-111111111111", "Projeto",
                "Project Files/Audit", 1, "Template", new(1, 1, 0, 0, 0, 0, 0), new[] { document }, true, null);
            var store = new DocumentQualityReportStore(directory);
            await store.SaveAsync(result);
            var csv = await File.ReadAllTextAsync(Assert.IsType<string>(store.GetPath(result.ProjectIdAcc, execution)));
            Assert.Contains("Título esperado", csv);
            Assert.Contains("Revisão encontrada", csv);
            Assert.Contains("CHS001-000-PC1-RE-001", csv);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task N_O_P_DuasExecucoes_SegundaViraAAtual_PrimeiraContinuaPersistida()
    {
        var directory = Path.Combine(Path.GetTempPath(), "doc-quality-hist-" + Guid.NewGuid().ToString("N"));
        const string projeto = "b.11111111-1111-1111-1111-111111111111";
        try
        {
            var repository = new CapturingAuditRepository();
            var persistence = new DocumentQualityOperationalAuditPersistence(() => repository, NullLogger<DocumentQualityOperationalAuditPersistence>.Instance);
            var store = new DocumentQualityReportStore(directory);
            var documento = Compare(Read(DocumentQualityFields.NaDocumentNumber, "CHS001-000-PC1-RE-001"), Read(DocumentQualityFields.Revision, "01"));
            var execucoes = new List<DocumentQualityRunResult>();
            for (var i = 0; i < 2; i++)
            {
                var id = Guid.NewGuid();
                await persistence.PersistAsync(id, DateTime.UtcNow, projeto, "Projeto", new[] { documento }, "teste");
                var resultado = new DocumentQualityRunResult(id, DateTime.UtcNow, DateTime.UtcNow.AddSeconds(i), projeto, "Projeto", "Pasta", 1, "Template",
                    new(1, 0, 1, 0, 0, 0, 0), new[] { documento }, true, null);
                await store.SaveAsync(resultado);
                execucoes.Add(resultado);
            }

            Assert.Equal(execucoes.Select(e => e.ExecutionId), repository.Executions); // um AuditRun por execução (OperationalAudit)
            Assert.Equal(execucoes[1].ExecutionId, (await store.GetLatestAsync(projeto))!.ExecutionId); // a 2ª vira a atual
            Assert.All(execucoes, e => Assert.NotNull(store.GetPath(projeto, e.ExecutionId))); // a 1ª continua lá (CSV + JSON)
            Assert.Equal(2, Directory.GetFiles(Path.GetDirectoryName(store.GetPath(projeto, execucoes[0].ExecutionId))!, "*.json").Length);
            Assert.Null(await store.GetLatestAsync("b.22222222-2222-2222-2222-222222222222"));
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    private static DocumentQualityDocumentResult Compare(params PdfRegionRead[] fields) =>
        DocumentQualityComparisonEngine.Compare("arquivo.pdf", "Project Files/Audit", "b.11111111-1111-1111-1111-111111111111", new("A4", fields), Ldp);

    private static PdfRegionRead Read(string field, string value) => new(field, value, true, null, 1, "A4");
    private static OcrTemplateRegion Region(string field, decimal x, decimal y, decimal width, decimal height) =>
        new(1, 1, "A4", field, x, y, width, height, 1, 612, 792);

    private static byte[] CreatePdf(params (int X, int Y, string Text)[] texts)
    {
        var content = string.Join("\n", texts.Select(t => $"BT /F1 12 Tf {t.X} {t.Y} Td ({t.Text.Replace("(", "\\(").Replace(")", "\\)")}) Tj ET"));
        var objects = new[]
        {
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Resources << /Font << /F1 5 0 R >> >> /Contents 4 0 R >>",
            $"<< /Length {Encoding.ASCII.GetByteCount(content)} >>\nstream\n{content}\nendstream",
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>",
        };
        var builder = new StringBuilder("%PDF-1.4\n");
        var offsets = new List<int> { 0 };
        for (var index = 0; index < objects.Length; index++)
        {
            offsets.Add(Encoding.ASCII.GetByteCount(builder.ToString()));
            builder.Append(index + 1).Append(" 0 obj\n").Append(objects[index]).Append("\nendobj\n");
        }
        var xref = Encoding.ASCII.GetByteCount(builder.ToString());
        builder.Append("xref\n0 6\n0000000000 65535 f \n");
        foreach (var offset in offsets.Skip(1)) builder.Append(offset.ToString("D10")).Append(" 00000 n \n");
        builder.Append("trailer\n<< /Size 6 /Root 1 0 R >>\nstartxref\n").Append(xref).Append("\n%%EOF\n");
        return Encoding.ASCII.GetBytes(builder.ToString());
    }

    private sealed class MemoryTemplateRepository : IOcrTemplateRepository
    {
        private readonly List<OcrTemplateRegion> regions = new();
        public Task<IReadOnlyList<OcrPackage>> GetPackagesAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<OcrPackage>>(new[] { new OcrPackage(1, "Teste", DateTime.UtcNow) });
        public Task<OcrPackage> CreatePackageAsync(string name, CancellationToken cancellationToken = default) => Task.FromResult(new OcrPackage(1, name, DateTime.UtcNow));
        public Task<OcrPackage> RenamePackageAsync(int packageId, string name, CancellationToken cancellationToken = default) => Task.FromResult(new OcrPackage(packageId, name, DateTime.UtcNow));
        public Task<IReadOnlyList<OcrTemplateRegion>> GetRegionsAsync(int packageId, string? sheetType = null, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<OcrTemplateRegion>>(regions.Where(x => x.PackageId == packageId && (sheetType == null || x.SheetType == sheetType)).ToArray());
        public Task ReplaceRegionsAsync(int packageId, string sheetType, IReadOnlyList<SaveOcrTemplateRegion> values, CancellationToken cancellationToken = default)
        {
            regions.RemoveAll(x => x.PackageId == packageId && x.SheetType == sheetType);
            regions.AddRange(values.Select((x, i) => new OcrTemplateRegion(i + 1, packageId, sheetType, x.CampoDestino, x.X, x.Y, x.Width, x.Height, x.Pagina, x.ReferenceWidth, x.ReferenceHeight)));
            return Task.CompletedTask;
        }
        public Task ClearRegionsAsync(int packageId, string sheetType, CancellationToken cancellationToken = default) { regions.RemoveAll(x => x.PackageId == packageId && x.SheetType == sheetType); return Task.CompletedTask; }
        public Task<DocumentQualityProjectConfiguration> GetProjectConfigurationAsync(string projectIdAcc, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<DocumentQualityProjectConfiguration> SaveProjectConfigurationAsync(string projectIdAcc, SaveDocumentQualityProjectConfiguration configuration, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<IReadOnlyList<ProjectAuditTemplate>> GetProjectTemplatesAsync(CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<DocumentQualityProjectConfiguration> SaveProjectTemplateAsync(string projectIdAcc, int? packageId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<IReadOnlyList<TemplateField>?> GetTemplateFieldsAsync(int packageId, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<TemplateField>?>(null);
        public Task<IReadOnlyList<TemplateField>> SaveTemplateFieldsAsync(int packageId, IReadOnlyList<string> fields, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<IReadOnlyList<FormatMapping>> GetFormatMappingsAsync(int packageId, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<FormatMapping>>(Array.Empty<FormatMapping>());
        public Task<IReadOnlyList<FormatMapping>> SaveFormatMappingsAsync(int packageId, IReadOnlyList<FormatMapping> mappings, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<IReadOnlyList<string>> GetLdpFormatValuesAsync(int packageId, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());
    }

    private sealed class CatalogoReal : ILdpColumnCatalog
    {
        public Task<IReadOnlyList<LdpColumn>> GetColumnsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(LdpColumnCatalogRules.Build(DocumentQualityTemplateFieldsTests.SchemaReal));
    }

    private sealed class CapturingAuditRepository : IAuditRepository
    {
        public bool Started { get; private set; }
        public List<Guid> Executions { get; } = new();
        public bool Finished { get; private set; }
        public OperationalAuditProjectData? ProjectData { get; private set; }
        public Task<long> IniciarAuditRunAsync(Guid executionId, string triggeredBy, IReadOnlyList<string> stagesRequested, CancellationToken cancellationToken = default) { Started = true; Executions.Add(executionId); return Task.FromResult(10L + Executions.Count); }
        public Task<AuditProjectPersistResult> PersistirProjetoAsync(long auditRunId, OperationalAuditProjectData dados, CancellationToken cancellationToken = default) { ProjectData = dados; return Task.FromResult(AuditProjectPersistResult.Ok(20)); }
        public Task FinalizarAuditRunAsync(long auditRunId, OperationalAuditRunStatus status, int totalProjetos, int totalFindings, int totalRequisitos, string? erro, CancellationToken cancellationToken = default) { Finished = true; return Task.CompletedTask; }
    }
}
