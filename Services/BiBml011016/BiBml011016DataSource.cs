using System.Data;
using APS.WebApp.Controllers;
using APS.WebApp.Services.Planning;
using Microsoft.Data.SqlClient;

namespace APS.WebApp.Services.BiBml011016;

public sealed record BiBml011016Coluna(string Nome, string Tipo);

public sealed record BiBml011016Leitura(string Estado, string? Motivo, IReadOnlyList<BiBml011016Coluna> Colunas,
    IReadOnlyList<Dictionary<string, object?>> Linhas)
{
    public static BiBml011016Leitura Indisponivel(string motivo) => new(BiBml011016Estado.Indisponivel, motivo, [], []);
}

public interface IBiBml011016DataSource
{
    Task<IReadOnlyList<BiBml011016CandidatoProjeto>> ListarCandidatosProjetoAsync(CancellationToken ct);
    /// <param name="projectIdAcc">SEMPRE o Project_ID_ACC resolvido no servidor (BiBml011016ProjetoResolver), nunca do request.</param>
    Task<BiBml011016Leitura> LerAsync(BiBml011016Fonte fonte, string projectIdAcc, CancellationToken ct);
}

/// <summary>
/// Leituras SOMENTE SELECT das fontes reais já usadas por outras telas (mesmas views/snapshots). Nenhuma escrita, nenhum DDL.
/// Objeto/coluna inexistente (SQL 208/207) torna só AQUELA fonte INDISPONIVEL; outros erros SQL sobem para o controller (503).
/// </summary>
public sealed class BiBml011016SqlDataSource(string connectionString) : IBiBml011016DataSource
{
    public const string CandidatosProjetoSql = """
        SELECT ProjetoID, Project_ID_ACC, Nome_Projeto, CAST(ISNULL(Is_Ativo, 0) AS bit) AS Ativo
        FROM dbo.Projetos
        WHERE Nome_Projeto LIKE N'%BML011.016%';
        """;

    /// <summary>LDP atual completa (todas as colunas do snapshot PmoLdpSnapshot) + gates 30/60/90, como no BI CHS001.</summary>
    public static readonly string LdpSql = $"""
        SELECT LdpAtual.*,
               G30.Fase AS Fase_30, G30.Gate_Ativo AS Ativo_30, G30.Document_Status AS Document_Status_30, G30.Situation AS Situation_30,
               G60.Fase AS Fase_60, G60.Gate_Ativo AS Ativo_60, G60.Document_Status AS Document_Status_60, G60.Situation AS Situation_60,
               G90.Fase AS Fase_90, G90.Gate_Ativo AS Ativo_90, G90.Document_Status AS Document_Status_90, G90.Situation AS Situation_90
        FROM {PmoLdpSnapshot.Tabela}
        LEFT JOIN dbo.VW_Ldp_Documento_Gates_Ultima_Versao G30
          ON G30.Ldp_Registro_Id = LdpAtual.ID_Registro AND G30.Project_ID_ACC = @projectId AND G30.Gate_Percentual = 30
        LEFT JOIN dbo.VW_Ldp_Documento_Gates_Ultima_Versao G60
          ON G60.Ldp_Registro_Id = LdpAtual.ID_Registro AND G60.Project_ID_ACC = @projectId AND G60.Gate_Percentual = 60
        LEFT JOIN dbo.VW_Ldp_Documento_Gates_Ultima_Versao G90
          ON G90.Ldp_Registro_Id = LdpAtual.ID_Registro AND G90.Project_ID_ACC = @projectId AND G90.Gate_Percentual = 90
        ORDER BY LdpAtual.ID_Registro
        {PmoLdpSnapshot.OpcaoPlano};
        """;

    // BML011.016 e LDP Tipo 1. A view de Tipo 2 nao tem registros para ele,
    // mas o cronograma oficial (snapshot de 28/09/2026) possui 377 tarefas.
    // Mantemos campos e versao ORIGINAIS; isto nao cria AssignmentByDay,
    // nem reproduz medidas DAX nao comprovadas.
    public const string CronogramaTipo1Sql = """
        SELECT ID_Cronograma, Project_ID_ACC, UID_Tarefa, EDT_WBS, NA_DOCUMENT_NUMBER,
               TASK_NAME, PLANNED_DATE_START, PLANNED_DATA_BASELINE, REPLANNED_DATE,
               Termino_Linha_Base, Termino, Trabalho, PERCENT_PHYSICAL_COMPLETED,
               PERCENT_PREDICTED_PHYSICAL, Percent_Previsto_LB, Percent_Previsto_Reprogramado,
               Percent_Realizado, Disciplina_Cronograma, Data_Sincronizacao, Data_Leitura_Cronograma
        FROM dbo.Cronograma_Projetos
        WHERE Project_ID_ACC = @projectId AND Tipo_LDP = 1
        ORDER BY EDT_WBS, UID_Tarefa;
        """;

    public const string CapExternoSql = """
        SELECT Id_Registro, ID, [Date], Name, Company, CYCLE, FRONT_OF_SERVICES, AFE, Description, Name2, Company3,
               Schedule, Reschedule, Concl, Critical, Status, COMMENTS, Data_Sincronizacao
        FROM dbo.vw_Latest_ExternalAction
        WHERE Project_ID_ACC = @projectId
        ORDER BY Id_Registro;
        """;

    public const string DesviosSql = """
        SELECT Id_Registro, ITEM, DESCRICAO, DISCIPLINA, N_DO_DESVIO, DATA, QUANT_HH, STATUS, DESVIO_INTERNO, CHANGE_ORDER, Data_Sincronizacao
        FROM dbo.vw_Latest_ScopeDeviation
        WHERE Project_ID_ACC = @projectId
        ORDER BY Id_Registro;
        """;

    public const string HoldsSql = """
        SELECT Id_Registro, ITEM, N_HOLD, N_NA_DOCUMENTO, DISCIPLINA, TITULO_DOCUMENTO, REV, MOTIVO_HOLD, STATUS, RESPONSAVEL, Data_Sincronizacao
        FROM dbo.vw_Latest_HoldControl
        WHERE Project_ID_ACC = @projectId
        ORDER BY Id_Registro;
        """;

    /// <summary>Todo SQL fixo deste módulo (o do cronograma é montado só com nomes da lista fixa do BI CHS001).</summary>
    public static IReadOnlyDictionary<string, string> Consultas => new Dictionary<string, string>
    {
        ["projeto"] = CandidatosProjetoSql,
        ["ldp"] = LdpSql,
        ["cronograma"] = BiChs001Controller.MontarCronogramaSql(BiChs001Controller.ColunasCronogramaOpcionais),
        ["cronograma-diario"] = BiChs001Controller.CronogramaDiarioSql,
        ["programacao-semanal"] = BiChs001Controller.ProgramacaoSql,
        ["cap-externo"] = CapExternoSql,
        ["desvios"] = DesviosSql,
        ["holds"] = HoldsSql,
    };

    public async Task<IReadOnlyList<BiBml011016CandidatoProjeto>> ListarCandidatosProjetoAsync(CancellationToken ct)
    {
        await using var cn = new SqlConnection(connectionString);
        await cn.OpenAsync(ct);
        using var cmd = new SqlCommand(CandidatosProjetoSql, cn) { CommandTimeout = 30 };
        await using var r = await cmd.ExecuteReaderAsync(ct);
        var lista = new List<BiBml011016CandidatoProjeto>();
        while (await r.ReadAsync(ct))
            lista.Add(new(Convert.ToInt32(r.GetValue(0)), r.IsDBNull(1) ? null : r.GetString(1), r.IsDBNull(2) ? null : r.GetString(2),
                !r.IsDBNull(3) && r.GetBoolean(3)));
        return lista;
    }

    public async Task<BiBml011016Leitura> LerAsync(BiBml011016Fonte fonte, string projectIdAcc, CancellationToken ct)
    {
        await using var cn = new SqlConnection(connectionString);
        await cn.OpenAsync(ct);
        string sql;
        switch (fonte.Id)
        {
            case "ldp": sql = LdpSql; break;
            case "cronograma":
                // Projeto Tipo 1: NAO ler view Tipo 2 vazia nem inventar gates.
                // Para Tipo 1 servimos as tarefas realmente registradas em Cronograma_Projetos.
                // Os dados timephased AssignmentByDay continuam uma fonte distinta.
                using (var tipo = new SqlCommand("SELECT TOP(1) Tipo_LDP FROM dbo.Projetos WHERE Project_ID_ACC = @projectId AND Is_Ativo = 1", cn))
                {
                    tipo.Parameters.Add("@projectId", SqlDbType.NVarChar, 100).Value = projectIdAcc;
                    var tipoLdp = await tipo.ExecuteScalarAsync(ct);
                    if (tipoLdp is not null and not DBNull && Convert.ToInt32(tipoLdp) == 1)
                        sql = CronogramaTipo1Sql;
                    else
                        sql = BiChs001Controller.MontarCronogramaSql(await ColunasExistentesAsync(cn, "dbo.VW_Cronograma_Tipo2_Ultima_Versao",
                            BiChs001Controller.ColunasCronogramaOpcionais, ct));
                }
                break;
            case "cronograma-diario":
                using (var existe = new SqlCommand("SELECT OBJECT_ID(@nome, 'V')", cn) { CommandTimeout = 30 })
                {
                    existe.Parameters.Add("@nome", SqlDbType.NVarChar, 256).Value = BiChs001Controller.ViewCronogramaDiario;
                    if (await existe.ExecuteScalarAsync(ct) is DBNull or null)
                        return BiBml011016Leitura.Indisponivel($"{BiChs001Controller.ViewCronogramaDiario} não existe: o trabalho por tarefa × dia " +
                            "(AssignmentByDay) do .mpp não é persistido no banco principal.");
                }
                sql = BiChs001Controller.CronogramaDiarioSql;
                break;
            case "programacao-semanal": sql = BiChs001Controller.ProgramacaoSql; break;
            case "cap-externo": sql = CapExternoSql; break;
            case "desvios": sql = DesviosSql; break;
            case "holds": sql = HoldsSql; break;
            default: return new(BiBml011016Estado.Pendente, fonte.MotivoPendente ?? "Fonte sem consulta mapeada.", [], []);
        }

        try
        {
            using var cmd = new SqlCommand(sql, cn) { CommandTimeout = 90 };
            cmd.Parameters.Add("@projectId", SqlDbType.NVarChar, 100).Value = projectIdAcc;
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            var colunas = Enumerable.Range(0, reader.FieldCount)
                .Select(i => new BiBml011016Coluna(reader.GetName(i), Tipo(reader.GetFieldType(i)))).ToArray();
            var linhas = new List<Dictionary<string, object?>>();
            while (await reader.ReadAsync(ct))
            {
                var linha = new Dictionary<string, object?>(colunas.Length, StringComparer.Ordinal);
                for (int i = 0; i < colunas.Length; i++) linha[colunas[i].Nome] = reader.IsDBNull(i) ? null : reader.GetValue(i);
                linhas.Add(linha);
            }
            return new(linhas.Count == 0 ? BiBml011016Estado.Vazio : BiBml011016Estado.Ok,
                linhas.Count == 0 ? $"{fonte.Origem} não tem linhas para o projeto {BiBml011016Catalogo.CodigoProjeto}." : null, colunas, linhas);
        }
        catch (SqlException ex) when (ex.Number is 207 or 208)
        {
            return BiBml011016Leitura.Indisponivel($"Objeto ou coluna ausente no banco para {fonte.Origem} (SQL {ex.Number}).");
        }
    }

    public static string Tipo(Type t) => Type.GetTypeCode(t) switch
    {
        TypeCode.String or TypeCode.Char => "texto",
        TypeCode.Byte or TypeCode.Int16 or TypeCode.Int32 or TypeCode.Int64 => "inteiro",
        TypeCode.Decimal or TypeCode.Double or TypeCode.Single => "decimal",
        TypeCode.DateTime => "dataHora",
        TypeCode.Boolean => "booleano",
        _ => t == typeof(DateTimeOffset) ? "dataHora" : "outro",
    };

    private static async Task<HashSet<string>> ColunasExistentesAsync(SqlConnection cn, string objeto, string[] candidatas, CancellationToken ct)
    {
        using var cmd = new SqlCommand("SELECT name FROM sys.columns WHERE object_id = OBJECT_ID(@objeto)", cn) { CommandTimeout = 30 };
        cmd.Parameters.Add("@objeto", SqlDbType.NVarChar, 256).Value = objeto;
        var existentes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct)) { var nome = r.GetString(0); if (candidatas.Contains(nome, StringComparer.OrdinalIgnoreCase)) existentes.Add(nome); }
        return existentes;
    }
}
