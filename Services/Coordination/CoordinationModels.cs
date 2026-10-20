namespace NA_Connect.Services.Coordination;

// Cadastro FUNCIONAL de Norma de Coordenação (Sigla -> Disciplina), schema coordenacao.* — ver
// Migrations/Coordenacao/*.sql. Distinto de config.Norma/config.NormaMapeamento (nomenclatura de
// placeholder/pasta destino, conceito diferente, auditado e nunca reutilizado — ver checkpoint).
public sealed class NormaCoordenacaoDto
{
    public int NormaId { get; set; }
    public string Nome { get; set; } = "";
    public string? Descricao { get; set; }
    public string Origem { get; set; } = CoordinationOrigens.NA;
    public bool Ativa { get; set; } = true;
    public DateTime? DataCriacao { get; set; }
    public DateTime? DataAtualizacao { get; set; }
    public List<NormaCoordenacaoMapeamentoDto> Mapeamentos { get; set; } = new();
    public int ProjetosVinculados { get; set; }
}

public sealed class NormaCoordenacaoMapeamentoDto
{
    public int NormaMapeamentoId { get; set; }
    public string Sigla { get; set; } = "";
    public string Disciplina { get; set; } = "";
}

public static class CoordinationOrigens
{
    public const string NA = "N&A";
    public const string Cliente = "CLIENTE";
    public static bool EhValida(string? origem) => origem == NA || origem == Cliente;
}

public sealed class ImportPreviewLinhaDto
{
    public int Linha { get; set; }
    public string? SiglaOriginal { get; set; }
    public string? DisciplinaOriginal { get; set; }
    public string SiglaNormalizada { get; set; } = "";
    public bool Valida { get; set; }
    public List<string> Erros { get; set; } = new();
    public List<string> Avisos { get; set; } = new();
}

public sealed class ImportPreviewResultadoDto
{
    public List<ImportPreviewLinhaDto> Linhas { get; set; } = new();
    public int TotalLinhas { get; set; }
    public int LinhasValidas { get; set; }
    public int LinhasInvalidas { get; set; }
}

public sealed class UnmappedSiglaDiagnosticDto
{
    public string Sigla { get; set; } = "";
    public int Lancamentos { get; set; }
    public decimal? Hh { get; set; }
    public List<string> Projetos { get; set; } = new();
}
