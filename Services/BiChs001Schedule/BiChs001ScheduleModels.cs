namespace APS.WebApp.Services.BiChs001Schedule;

/// <summary>
/// Módulo ISOLADO do BI CHS001 (Original). Não substitui nem altera o importador CronogramaTipo2: lê uma CÓPIA do .mpp
/// oficial e grava SOMENTE no schema SQL <c>bi_chs001</c>. As medidas DAX do PBIX são calculadas no frontend
/// (ClientApp/src/features/production/biChs001/dados) sobre estas linhas brutas.
/// </summary>
public static class BiChs001Escopo
{
    /// <summary>Único projeto atendido por este módulo (CHS001 - ETHANOL PLANT). Outros projetos são recusados.</summary>
    public const string ProjectIdChs001 = "b.fcdb7ebd-1b23-440d-a04b-90391098522e";

    public const string Leitor = "MPXJ.Net 13.5.0 / BiChs001MppReader v1";

    public static bool ProjetoPermitido(string? projectId) =>
        string.Equals(projectId, ProjectIdChs001, StringComparison.OrdinalIgnoreCase);
}

/// <summary>Uma tarefa do .mpp com os campos nativos que o PBIX usa (WBX / Task) — valores exatamente como o MPXJ devolve.</summary>
public sealed class BiTarefa
{
    public int Ordem { get; set; }
    public int UidTarefa { get; set; }
    public string? GuidTarefa { get; set; }
    public int? IdTarefa { get; set; }
    public int? UidTarefaPai { get; set; }
    public int? NivelEstrutura { get; set; }
    public bool EhResumo { get; set; }
    public bool EhMarco { get; set; }
    public string Nome { get; set; } = "";
    public string? Edt { get; set; }
    public DateTime? Inicio { get; set; }
    public DateTime? Termino { get; set; }
    public DateTime? InicioLinhaBase { get; set; }
    public DateTime? TerminoLinhaBase { get; set; }
    public decimal? TrabalhoHoras { get; set; }
    public decimal? TrabalhoRealHoras { get; set; }
    public decimal? TrabalhoLinhaBaseHoras { get; set; }
    public decimal? TrabalhoRestanteHoras { get; set; }
    public decimal? PercentualConcluido { get; set; }
    public int QtdeAtribuicoes { get; set; }
    public string? Calendario { get; set; }
    /// <summary>Campos customizados com alias (texto de exibição), mesmo formato do JSON do Tipo 2 (chaves = alias original).</summary>
    public string CamposCustomizadosJson { get; set; } = "{}";
}

/// <summary>Trabalho de uma tarefa num dia (soma das atribuições da tarefa nesse dia), em horas.</summary>
public sealed class BiTrabalhoDia
{
    public int UidTarefa { get; set; }
    public DateTime Data { get; set; }
    public decimal? TrabalhoRealHoras { get; set; }
    public decimal? TrabalhoLinhaBaseHoras { get; set; }
    public decimal? TrabalhoPlanejadoHoras { get; set; }
}

/// <summary>Resultado da leitura de UMA cópia do .mpp (pura, sem SQL/ACC).</summary>
public sealed class BiLeituraCronograma
{
    public string ArquivoNome { get; set; } = "";
    public string ArquivoSha256 { get; set; } = "";
    public long ArquivoBytes { get; set; }
    public DateTime LidoEmUtc { get; set; }
    public DateTime? DataStatus { get; set; }
    public DateTime? DataAtualProjeto { get; set; }
    public string? CalendarioPadrao { get; set; }
    public decimal? MinutosPorDia { get; set; }
    public List<BiTarefa> Tarefas { get; } = new();
    public List<BiTrabalhoDia> TrabalhoDiario { get; } = new();
    public BiDiagnosticoLeitura Diagnostico { get; } = new();
}

/// <summary>Fatos verificáveis sobre a leitura — gravados junto com a versão; nada é estimado.</summary>
public sealed class BiDiagnosticoLeitura
{
    public int TotalTarefas { get; set; }
    public int TotalFolhas { get; set; }
    public int TotalResumo { get; set; }
    public int TotalAtribuicoes { get; set; }
    public int FolhasSemInicio { get; set; }
    public int FolhasSemInicioLinhaBase { get; set; }
    public int FolhasSemTrabalho { get; set; }
    public int FolhasSemTrabalhoLinhaBase { get; set; }
    public int FolhasComTrabalhoRealSemSerie { get; set; }
    public int FolhasComSerieDivergenteDoTotalReal { get; set; }
    public int FolhasComSerieDivergenteDoTotalLinhaBase { get; set; }
    /// <summary>Blocos timephased com horas &gt; 0 cujo intervalo não tem hora útil no calendário efetivo (lançados no dia de início).</summary>
    public int BlocosSemHoraUtil { get; set; }
    public int TarefasSemPaiForaDaRaiz { get; set; }
    public decimal SomaTrabalhoFolhas { get; set; }
    public decimal SomaTrabalhoRealFolhas { get; set; }
    public decimal SomaTrabalhoLinhaBaseFolhas { get; set; }
    public decimal SomaSerieReal { get; set; }
    public decimal SomaSerieLinhaBase { get; set; }
    public decimal SomaSeriePlanejado { get; set; }
    public DateTime? PrimeiraDataSerie { get; set; }
    public DateTime? UltimaDataSerie { get; set; }
    public List<string> Calendarios { get; set; } = new();
    public List<string> Avisos { get; set; } = new();
}

public sealed class BiResultadoSincronizacao
{
    public bool Sucesso { get; set; }
    public bool JaExistia { get; set; }
    public int? VersaoId { get; set; }
    public string? ArquivoNome { get; set; }
    public string? ArquivoVersaoUrn { get; set; }
    public string? ArquivoSha256 { get; set; }
    public int Tarefas { get; set; }
    public int LinhasDiarias { get; set; }
    public BiDiagnosticoLeitura? Diagnostico { get; set; }
    public List<string> Erros { get; } = new();
}
