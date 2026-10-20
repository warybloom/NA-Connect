using APS.WebApp.Controllers;
using APS.WebApp.Services.DocumentQuality;
using APS.WebApp.Services.UploadGovernado;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace NA_Connect.Planning.Tests;

/// <summary>
/// Rodada corretiva da Fase 2: campos auditados pertencem ao TEMPLATE (coordenada é TEMPLATE + FORMATO + CAMPO), catálogo DINÂMICO das
/// colunas reais da LDP e perfil de folha pelo MAPEAMENTO DO TEMPLATE (nenhum "D = ANSI_D" global), sempre protegido pela dimensão da página.
/// </summary>
public sealed class DocumentQualityTemplateFieldsTests
{
    private const string Projeto = "b.11111111-1111-1111-1111-111111111111";

    /// <summary>Colunas reais de dbo.Ldp_Unificada lidas do SQL Server em 01/10/2026 (sys.columns), na ordem real.</summary>
    public static readonly (string Name, string Type, int Ordinal)[] SchemaReal =
    {
        ("ID_Registro","int",1),("Project_ID_ACC","nvarchar",2),("Item","nvarchar",3),("Schedule_ID","nvarchar",4),("Discipline","nvarchar",5),
        ("NA_Document_Number","nvarchar",6),("Client_Document_Number","nvarchar",7),("Document_Title","nvarchar",8),("Format","nvarchar",9),
        ("No_Pages","int",10),("Planned_Date_Baseline","datetime",11),("Replanned_Date","datetime",12),("Emission_Date","datetime",13),
        ("Transmittal_Number","nvarchar",14),("Issued_Purpose","nvarchar",15),("Revision","nvarchar",16),("Document_Status","nvarchar",17),
        ("WBS_Percent","decimal",18),("Status_In_Flow","nvarchar",19),("Situation","nvarchar",20),("Date_Comments_Received","datetime",21),
        ("Comment_Status","nvarchar",22),("Percent_Planned_Physical","decimal",23),("Percent_Physical_Completed","decimal",24),
        ("Inclusion_Date","datetime",25),("CR","nvarchar",26),("CR_OS","nvarchar",27),("Area","nvarchar",28),("Discipline_Activity","nvarchar",29),
        ("Doc_Type_Code","nvarchar",30),("Seq","nvarchar",31),("Planned_HH","decimal",32),("Actual_HH","decimal",33),("Remaining_HH","decimal",34),
        ("Planned_Cost","decimal",35),("Actual_Cost","decimal",36),("Remaining_Cost","decimal",37),("Data_Sincronizacao","datetime",38),
    };
    private static readonly IReadOnlyList<LdpColumn> Catalogo = LdpColumnCatalogRules.Build(SchemaReal);

    private static readonly LdpDocumentoAtual Doc = new(10, "CHS001-000-PC3-FD-001", "CL-001", "PROCESS", "Process Flow Diagram", "0",
        new Dictionary<string, string?> { ["Format"] = "D", ["Area"] = "000", ["Transmittal_Number"] = "GRD-0042", ["No_Pages"] = "15" });

    // ---------------------------------------------------------------- 10. Catálogo dinâmico

    [Fact]
    public void A10_TodasAsColunasReaisAparecem_ComOuSemRotulo()
    {
        Assert.Equal(38, Catalogo.Count);
        Assert.Equal(SchemaReal.Select(c => c.Name), Catalogo.Select(c => c.Name));
        Assert.All(Catalogo, c => Assert.True(c.Available)); // todas são carregadas pela LDP atual (PmoLdpSnapshot.Tabela)
        Assert.Equal("CR_OS", Catalogo.Single(c => c.Name == "CR_OS").Label);                  // 10B: sem rótulo → nome da coluna
        Assert.Equal("TÍTULO DO DOCUMENTO", Catalogo.Single(c => c.Name == "Document_Title").Label);
        Assert.True(Catalogo.Single(c => c.Name == "Document_Title").Recommended);
        Assert.False(Catalogo.Single(c => c.Name == "Planned_HH").Recommended);              // não recomendado, mas presente
    }

    [Theory]
    [InlineData("document_title", "Document_Title")]
    [InlineData(" Transmittal_Number ", "Transmittal_Number")]
    public void A10_CampoValidadoVoltaComNomeCanonico(string pedido, string esperado) => Assert.Equal(esperado, LdpColumnCatalogRules.Canonical(pedido, Catalogo));

    [Theory]
    [InlineData("Coluna_Inexistente")]
    [InlineData("Document_Title]; DROP TABLE dbo.Projetos;--")]
    [InlineData("")]
    public void A10_SegurancaCampoForaDoSchemaNuncaChegaAoSql(string pedido) =>
        Assert.Throws<ArgumentException>(() => LdpColumnCatalogRules.Canonical(pedido, Catalogo));

    [Fact]
    public void A10_QueryDaLdpSoComIdentificadoresValidadosEEscapados()
    {
        var sql = LdpDocumentosAtuaisRepository.BuildQuery(new[] { "Transmittal_Number" });
        Assert.Contains("[Transmittal_Number]", sql);
        Assert.Contains("[Format]", sql);
        Assert.Equal("[a]]b]", LdpColumnCatalogRules.Quote("a]b"));
    }

    [Fact]
    public void C10_CampoForaDoAntigoCatalogoFixo_ComparaComALdp()
    {
        // Transmittal_Number e No_Pages não existiam no catálogo cravado: agora são colunas da LDP como qualquer outra.
        var r = Compare(new[] { "Transmittal_Number", "No_Pages" }, ("Transmittal_Number", "GRD-0042"), ("No_Pages", "16"));
        Assert.Equal(DocumentFieldStatus.Ok, Campo(r, "Transmittal_Number").Status);
        Assert.Equal(DocumentFieldStatus.Divergente, Campo(r, "No_Pages").Status);
        Assert.Equal("15", Campo(r, "No_Pages").Expected);
    }

    // ---------------------------------------------------------------- 9. Campos do TEMPLATE × coordenadas por formato

    private static OcrTemplateRegion R(string sheet, string field, decimal w, decimal h) => new(1, 1, sheet, field, 10, 10, 50, 10, 1, w, h);

    /// <summary>Coordenadas antigas "perdidas" em outros formatos (CLIENTE, PROJETO, DISCIPLINA, REVISÃO) não podem aparecer no Detalhe.</summary>
    private static readonly OcrTemplateRegion[] Regioes =
    {
        R("ANSI_D", "NA_Document_Number", 1296, 864), R("ANSI_D", "Document_Title", 1296, 864), R("ANSI_D", "Client_Document_Number", 1296, 864),
        R("ANSI_E", "NA_Document_Number", 3168, 2448), R("ANSI_E", "Project_ID_ACC", 3168, 2448), R("ANSI_E", "Revision", 3168, 2448),
        R("A4", "Discipline", 612, 792),
    };

    [Fact]
    public void A9_B9_C9_DetalheMostraExatamenteOsCamposAtivosDoTemplate()
    {
        foreach (var campos in new[]
        {
            new[] { "NA_Document_Number", "Document_Title" },                            // 9A: 2 campos
            new[] { "NA_Document_Number", "Document_Title", "Client_Document_Number" },  // 9B: + 1
            new[] { "NA_Document_Number", "Client_Document_Number" },                    // 9C: título removido
        })
        {
            var reads = Simular(1296, 864, campos, "D", Array.Empty<FormatMapping>());
            var r = DocumentQualityComparisonEngine.Compare("CHS001-000-PC3-FD-001.pdf", "Pasta", Projeto, reads, new[] { Doc });
            Assert.Equal(campos, r.Fields.Select(f => f.Field));
        }
    }

    [Fact]
    public void D9_TrocarDeFormato_MesmosCampos_SoMudaACoordenada()
    {
        var campos = new[] { "NA_Document_Number", "Document_Title" };
        var noD = Simular(1296, 864, campos, null, Array.Empty<FormatMapping>());
        var noE = Simular(3168, 2448, campos, null, Array.Empty<FormatMapping>());
        Assert.Equal(campos, noD.Fields.Select(f => f.TargetField));
        Assert.Equal(campos, noE.Fields.Select(f => f.TargetField));
        Assert.Equal(("ANSI_D", "ANSI_E"), (noD.SheetType, noE.SheetType));
        // No ANSI_E o título não foi calibrado: NÃO CALIBRADO (diferente de NÃO LIDO) — nenhum valor inventado.
        var titulo = noE.Fields.Single(f => f.TargetField == "Document_Title");
        Assert.True(titulo.NotCalibrated);
        var r = DocumentQualityComparisonEngine.Compare("CHS001-000-PC3-FD-001.pdf", "Pasta", Projeto, noE, new[] { Doc });
        Assert.Equal(DocumentFieldStatus.NaoCalibrado, Campo(r, "Document_Title").Status);
        Assert.Equal(DocumentFieldStatus.MotivoNaoCalibrado, Campo(r, "Document_Title").Reason);
        Assert.Null(Campo(r, "Document_Title").Found);
    }

    [Fact]
    public void NaoCalibrado_NaoViraErroDeLeituraNemDivergencia()
    {
        var r = Compare(new[] { "NA_Document_Number" }, ("NA_Document_Number", "CHS001-000-PC3-FD-001"));
        Assert.Equal(DocumentQualityStatus.Conforme, r.Status);
        var reads = new PdfRegionReadResult("ANSI_E", new[]
        {
            new PdfRegionRead("NA_Document_Number", "CHS001-000-PC3-FD-001", true, null, 1, "ANSI_E"),
            new PdfRegionRead("Document_Title", null, false, DocumentFieldStatus.MotivoNaoCalibrado, 0, "ANSI_E") { NotCalibrated = true },
        });
        var parcial = DocumentQualityComparisonEngine.Compare("CHS001-000-PC3-FD-001.pdf", "Pasta", Projeto, reads, new[] { Doc });
        Assert.Equal(DocumentQualityStatus.NaoCalibrado, parcial.Status);
    }

    // ---------------------------------------------------------------- 11. Format pelo MAPEAMENTO DO TEMPLATE

    [Fact]
    public void A11_SemMapeamento_DNaoViraANSI_D_EscolhePelaDimensao()
    {
        var p = DocumentQualityProfiles.Resolve(1296, 864, Regioes, "D", Array.Empty<FormatMapping>());
        Assert.Equal("ANSI_D", p.SheetType);                       // pela DIMENSÃO da página, não por "D"
        Assert.StartsWith("DIMENSÃO DA PÁGINA", p.Origin);
        Assert.Contains("sem mapeamento", p.Origin);
        // Página que não confere com nenhum perfil: nenhum perfil (nada de "o mais próximo" silencioso).
        var nenhum = DocumentQualityProfiles.Resolve(842, 1191, Regioes, "D", Array.Empty<FormatMapping>());
        Assert.Null(nenhum.SheetType);
        Assert.StartsWith("NENHUM PERFIL", nenhum.Origin);
    }

    [Fact]
    public void B11_MapeamentoExplicito_D_para_ANSI_D()
    {
        var p = DocumentQualityProfiles.Resolve(1296, 864, Regioes, "D", new[] { new FormatMapping("D", FormatMappingMode.Perfil, "ANSI_D") });
        Assert.Equal(("ANSI_D", "MAPEAMENTO DO TEMPLATE (D → ANSI_D)"), (p.SheetType, p.Origin));
    }

    [Fact]
    public void C11_OutroTemplate_D_para_ARCH_D()
    {
        var arch = new[] { R("ARCH_D", "NA_Document_Number", 1728, 2592), R("ANSI_D", "NA_Document_Number", 1584, 2448) };
        var p = DocumentQualityProfiles.Resolve(1728, 2592, arch, "d", new[] { new FormatMapping("D", FormatMappingMode.Perfil, "ARCH_D") });
        Assert.Equal("ARCH_D", p.SheetType);
        Assert.StartsWith("MAPEAMENTO DO TEMPLATE", p.Origin);
    }

    [Fact]
    public void D11_DimensaoIncompativel_NuncaUsaACoordenadaDoMapeamento()
    {
        // Mapeado D → ANSI_D, mas a página é do tamanho do ANSI_E: usa o perfil que CONFERE com a página e diz por quê.
        var p = DocumentQualityProfiles.Resolve(3168, 2448, Regioes, "D", new[] { new FormatMapping("D", FormatMappingMode.Perfil, "ANSI_D") });
        Assert.Equal("ANSI_E", p.SheetType);
        Assert.Contains("não tem a dimensão do perfil ANSI_D", p.Origin);
        // Mapeado para perfil sem regiões / formato ignorado / modo dimensão.
        Assert.Contains("não tem regiões", DocumentQualityProfiles.Resolve(1296, 864, Regioes, "D", new[] { new FormatMapping("D", FormatMappingMode.Perfil, "ARCH_D") }).Origin);
        var ignorado = DocumentQualityProfiles.Resolve(1296, 864, Regioes, "3D", new[] { new FormatMapping("3D", FormatMappingMode.Ignorar, null) });
        Assert.Null(ignorado.SheetType);
        Assert.Contains("NÃO SUPORTADO", ignorado.Origin);
        Assert.Equal("ANSI_E", DocumentQualityProfiles.Resolve(3168, 2448, Regioes, "AT", new[] { new FormatMapping("AT", FormatMappingMode.Dimensao, null) }).SheetType);
    }

    [Fact]
    public void Mapeamento_Validado()
    {
        SqlOcrTemplateRepository.ValidateMappings(new[] { new FormatMapping("D", FormatMappingMode.Perfil, "ANSI_D"), new FormatMapping("AT", FormatMappingMode.Dimensao, null) });
        Assert.Throws<ArgumentException>(() => SqlOcrTemplateRepository.ValidateMappings(new[] { new FormatMapping("D", FormatMappingMode.Perfil, null) }));
        Assert.Throws<ArgumentException>(() => SqlOcrTemplateRepository.ValidateMappings(new[] { new FormatMapping("D", "ANSI", null) }));
        Assert.Throws<ArgumentException>(() => SqlOcrTemplateRepository.ValidateMappings(new[] { new FormatMapping("D", "DIMENSAO", null), new FormatMapping("d", "IGNORAR", null) }));
    }

    // ---------------------------------------------------------------- API: campos do template e regiões validados no catálogo

    [Fact]
    public async Task Api_CamposDoTemplateERegioes_SoColunasReais()
    {
        var repo = new MemoryRepository();
        var controller = new OcrTemplateController(repo, new FixedCatalog(Catalogo), NullLogger<OcrTemplateController>.Instance);
        Assert.IsType<OkObjectResult>(await controller.SaveTemplateFields(1, new(new[] { "na_document_number", "Transmittal_Number" }), default));
        Assert.Equal(new[] { "NA_Document_Number", "Transmittal_Number" }, repo.Campos);
        Assert.IsType<BadRequestObjectResult>(await controller.SaveTemplateFields(1, new(new[] { "Nao_Existe" }), default));
        Assert.IsType<OkObjectResult>(await controller.SaveRegions(1, "ANSI_D", new[] { new SaveOcrTemplateRegion("transmittal_number", 10, 20, 30, 12, 1, 1296, 864) }, default));
        Assert.Equal("Transmittal_Number", (await repo.GetRegionsAsync(1, "ANSI_D")).Single().TargetField);
        Assert.IsType<BadRequestObjectResult>(await controller.SaveRegions(1, "ANSI_D", new[] { new SaveOcrTemplateRegion("x]; DROP TABLE y", 10, 20, 30, 12, 1, 1296, 864) }, default));
        Assert.IsType<OkObjectResult>(await controller.Fields(default));
    }

    // ---------------------------------------------------------------- helpers

    private static PdfRegionReadResult Simular(double w, double h, IReadOnlyList<string> campos, string? formato, IReadOnlyList<FormatMapping> mapas)
    {
        // Mesma lógica de PdfPigRegionTextReader (sem abrir PDF): perfil resolvido → campo com/sem coordenada.
        var p = DocumentQualityProfiles.Resolve(w, h, Regioes, formato, mapas);
        var regs = Regioes.Where(r => r.SheetType == p.SheetType).ToArray();
        return new(p.SheetType ?? "—", campos.Select(c => regs.Any(r => r.TargetField == c)
            ? new PdfRegionRead(c, c == "NA_Document_Number" ? "CHS001-000-PC3-FD-001" : "X", true, null, 1, p.SheetType!)
            : new PdfRegionRead(c, null, false, DocumentFieldStatus.MotivoNaoCalibrado, 0, p.SheetType ?? "—") { NotCalibrated = true }).ToArray())
        { SheetSource = p.Origin, LdpFormat = formato };
    }

    private static DocumentQualityDocumentResult Compare(string[] campos, params (string Campo, string Valor)[] lidos) =>
        DocumentQualityComparisonEngine.Compare("CHS001-000-PC3-FD-001.pdf", "Pasta", Projeto,
            new PdfRegionReadResult("ANSI_D", lidos.Select(l => new PdfRegionRead(l.Campo, l.Valor, true, null, 1, "ANSI_D")).ToArray()), new[] { Doc });

    private static DocumentQualityFieldResult Campo(DocumentQualityDocumentResult r, string campo) => r.Fields.Single(f => f.Field == campo);

    private sealed class FixedCatalog(IReadOnlyList<LdpColumn> columns) : ILdpColumnCatalog
    {
        public Task<IReadOnlyList<LdpColumn>> GetColumnsAsync(CancellationToken cancellationToken = default) => Task.FromResult(columns);
    }

    private sealed class MemoryRepository : IOcrTemplateRepository
    {
        private readonly List<OcrTemplateRegion> regions = new();
        public IReadOnlyList<string> Campos { get; private set; } = Array.Empty<string>();
        public Task<IReadOnlyList<OcrPackage>> GetPackagesAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<OcrPackage>>(Array.Empty<OcrPackage>());
        public Task<OcrPackage> CreatePackageAsync(string name, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<OcrPackage> RenamePackageAsync(int packageId, string name, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<IReadOnlyList<OcrTemplateRegion>> GetRegionsAsync(int packageId, string? sheetType = null, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<OcrTemplateRegion>>(regions.Where(x => x.PackageId == packageId && (sheetType == null || x.SheetType == sheetType)).ToArray());
        public Task ReplaceRegionsAsync(int packageId, string sheetType, IReadOnlyList<SaveOcrTemplateRegion> values, CancellationToken cancellationToken = default)
        {
            SqlOcrTemplateRepository.ValidateRegions(values);
            regions.RemoveAll(x => x.PackageId == packageId && x.SheetType == sheetType);
            regions.AddRange(values.Select((x, i) => new OcrTemplateRegion(i + 1, packageId, sheetType, x.CampoDestino, x.X, x.Y, x.Width, x.Height, x.Pagina, x.ReferenceWidth, x.ReferenceHeight)));
            return Task.CompletedTask;
        }
        public Task ClearRegionsAsync(int packageId, string sheetType, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<DocumentQualityProjectConfiguration> GetProjectConfigurationAsync(string projectIdAcc, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<DocumentQualityProjectConfiguration> SaveProjectConfigurationAsync(string projectIdAcc, SaveDocumentQualityProjectConfiguration configuration, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<IReadOnlyList<ProjectAuditTemplate>> GetProjectTemplatesAsync(CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<DocumentQualityProjectConfiguration> SaveProjectTemplateAsync(string projectIdAcc, int? packageId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<IReadOnlyList<TemplateField>?> GetTemplateFieldsAsync(int packageId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<TemplateField>?>(Campos.Select((c, i) => new TemplateField(c, i + 1, true)).ToArray());
        public Task<IReadOnlyList<TemplateField>> SaveTemplateFieldsAsync(int packageId, IReadOnlyList<string> fields, CancellationToken cancellationToken = default)
        {
            Campos = fields.ToArray();
            return Task.FromResult<IReadOnlyList<TemplateField>>(Campos.Select((c, i) => new TemplateField(c, i + 1, true)).ToArray());
        }
        public Task<IReadOnlyList<FormatMapping>> GetFormatMappingsAsync(int packageId, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<FormatMapping>>(Array.Empty<FormatMapping>());
        public Task<IReadOnlyList<FormatMapping>> SaveFormatMappingsAsync(int packageId, IReadOnlyList<FormatMapping> mappings, CancellationToken cancellationToken = default) => Task.FromResult(mappings);
        public Task<IReadOnlyList<string>> GetLdpFormatValuesAsync(int packageId, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());
    }
}
