using System.Data;
using System.Text.Json;
using Microsoft.Data.SqlClient;

namespace APS.WebApp.Services.BiChs001Schedule;

/// <summary>
/// Persistência e leitura do BI CHS001 — SOMENTE no schema <c>bi_chs001</c>. Uma versão = uma transação: cabeçalho, tarefas e
/// série diária entram juntos (ou nada entra). Nenhum comando aqui escreve em dbo; a única leitura de dbo é o hub do projeto
/// (SELECT em dbo.Projetos/Hubs_Conta, a mesma consulta do robô).
/// </summary>
public sealed class BiChs001ScheduleRepository(string connectionString)
{
    public const string SchemaObrigatorio = "bi_chs001.Versao_Cronograma";

    public async Task<bool> SchemaExisteAsync(CancellationToken ct)
    {
        await using var cn = new SqlConnection(connectionString);
        await cn.OpenAsync(ct);
        await using var cmd = new SqlCommand("SELECT CASE WHEN OBJECT_ID(@o, 'U') IS NULL THEN 0 ELSE 1 END", cn);
        cmd.Parameters.Add("@o", SqlDbType.NVarChar, 256).Value = SchemaObrigatorio;
        return (int)(await cmd.ExecuteScalarAsync(ct))! == 1;
    }

    public async Task<string?> ObterHubAsync(string projectId, CancellationToken ct)
    {
        await using var cn = new SqlConnection(connectionString);
        await cn.OpenAsync(ct);
        await using var cmd = new SqlCommand("""
            SELECT TOP (1) H.Account_ID_ACC FROM dbo.Projetos P JOIN dbo.Hubs_Conta H ON P.HubContaID = H.HubContaID
            WHERE P.Project_ID_ACC = @p AND P.Is_Ativo = 1
            """, cn);
        cmd.Parameters.Add("@p", SqlDbType.NVarChar, 100).Value = projectId;
        return await cmd.ExecuteScalarAsync(ct) as string;
    }

    public sealed record Origem(string Tipo, string? ItemId, string? VersaoUrn, string? ExecutadoPor);

    /// <summary>Grava a leitura como nova versão. Mesmo arquivo (SHA-256) já gravado para o projeto → devolve a versão existente.</summary>
    public async Task<(int VersaoId, bool JaExistia)> PersistirAsync(string projectId, BiLeituraCronograma leitura, Origem origem, CancellationToken ct)
    {
        if (!BiChs001Escopo.ProjetoPermitido(projectId)) throw new InvalidOperationException("Projeto fora do escopo do BI CHS001.");
        await using var cn = new SqlConnection(connectionString);
        await cn.OpenAsync(ct);
        await using var tx = (SqlTransaction)await cn.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
        try
        {
            // Uma sincronização por projeto de cada vez.
            await using (var lk = new SqlCommand("EXEC sp_getapplock @Resource = @r, @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = 30000", cn, tx))
            {
                lk.Parameters.Add("@r", SqlDbType.NVarChar, 255).Value = "bi_chs001_sync_" + projectId;
                await lk.ExecuteNonQueryAsync(ct);
            }
            await using (var ex = new SqlCommand("SELECT Versao_Id FROM bi_chs001.Versao_Cronograma WHERE Project_ID_ACC = @p AND Arquivo_Sha256 = @s", cn, tx))
            {
                ex.Parameters.Add("@p", SqlDbType.NVarChar, 100).Value = projectId;
                ex.Parameters.Add("@s", SqlDbType.Char, 64).Value = leitura.ArquivoSha256;
                if (await ex.ExecuteScalarAsync(ct) is int existente) { await tx.CommitAsync(ct); return (existente, true); }
            }

            int versaoId;
            await using (var ins = new SqlCommand("""
                INSERT INTO bi_chs001.Versao_Cronograma (Project_ID_ACC, Origem, Arquivo_Nome, Arquivo_Item_Id, Arquivo_Versao_Urn, Arquivo_Sha256,
                    Arquivo_Bytes, Data_Status_Projeto, Calendario_Padrao, Minutos_Por_Dia, Leitor, Lido_Em_Utc, Executado_Por,
                    Total_Tarefas, Total_Folhas, Total_Linhas_Diarias, Total_Atribuicoes, Diagnostico_JSON)
                OUTPUT INSERTED.Versao_Id
                VALUES (@p, @origem, @nome, @item, @urn, @sha, @bytes, @status, @cal, @mpd, @leitor, @lido, @por, @nt, @nf, @nd, @na, @diag)
                """, cn, tx))
            {
                var d = leitura.Diagnostico;
                ins.Parameters.Add("@p", SqlDbType.NVarChar, 100).Value = projectId;
                ins.Parameters.Add("@origem", SqlDbType.NVarChar, 20).Value = origem.Tipo;
                ins.Parameters.Add("@nome", SqlDbType.NVarChar, 400).Value = leitura.ArquivoNome;
                ins.Parameters.Add("@item", SqlDbType.NVarChar, 400).Value = (object?)origem.ItemId ?? DBNull.Value;
                ins.Parameters.Add("@urn", SqlDbType.NVarChar, 600).Value = (object?)origem.VersaoUrn ?? DBNull.Value;
                ins.Parameters.Add("@sha", SqlDbType.Char, 64).Value = leitura.ArquivoSha256;
                ins.Parameters.Add("@bytes", SqlDbType.BigInt).Value = leitura.ArquivoBytes;
                ins.Parameters.Add("@status", SqlDbType.DateTime2).Value = (object?)leitura.DataStatus ?? DBNull.Value;
                ins.Parameters.Add("@cal", SqlDbType.NVarChar, 200).Value = (object?)leitura.CalendarioPadrao ?? DBNull.Value;
                ins.Parameters.Add("@mpd", SqlDbType.Decimal).Value = (object?)leitura.MinutosPorDia ?? DBNull.Value;
                ins.Parameters["@mpd"].Precision = 18; ins.Parameters["@mpd"].Scale = 6;
                ins.Parameters.Add("@leitor", SqlDbType.NVarChar, 100).Value = BiChs001Escopo.Leitor;
                ins.Parameters.Add("@lido", SqlDbType.DateTime2).Value = leitura.LidoEmUtc;
                ins.Parameters.Add("@por", SqlDbType.NVarChar, 256).Value = (object?)origem.ExecutadoPor ?? DBNull.Value;
                ins.Parameters.Add("@nt", SqlDbType.Int).Value = d.TotalTarefas;
                ins.Parameters.Add("@nf", SqlDbType.Int).Value = d.TotalFolhas;
                ins.Parameters.Add("@nd", SqlDbType.Int).Value = leitura.TrabalhoDiario.Count;
                ins.Parameters.Add("@na", SqlDbType.Int).Value = d.TotalAtribuicoes;
                ins.Parameters.Add("@diag", SqlDbType.NVarChar, -1).Value = JsonSerializer.Serialize(d);
                versaoId = (int)(await ins.ExecuteScalarAsync(ct))!;
            }

            await CopiarAsync(cn, tx, "bi_chs001.Tarefa_Snapshot", TabelaTarefas(versaoId, projectId, leitura.Tarefas), ct);
            await CopiarAsync(cn, tx, "bi_chs001.Trabalho_Tarefa_Dia", TabelaDiario(versaoId, projectId, leitura.TrabalhoDiario), ct);

            // Conferência dentro da transação: o que entrou é exatamente o que foi lido.
            await using (var chk = new SqlCommand("""
                SELECT (SELECT COUNT(*) FROM bi_chs001.Tarefa_Snapshot WHERE Versao_Id = @v),
                       (SELECT COUNT(*) FROM bi_chs001.Trabalho_Tarefa_Dia WHERE Versao_Id = @v)
                """, cn, tx))
            {
                chk.Parameters.Add("@v", SqlDbType.Int).Value = versaoId;
                await using var r = await chk.ExecuteReaderAsync(ct);
                await r.ReadAsync(ct);
                int nt = r.GetInt32(0), nd = r.GetInt32(1);
                if (nt != leitura.Tarefas.Count || nd != leitura.TrabalhoDiario.Count)
                    throw new InvalidOperationException($"Conferência falhou: tarefas {nt}/{leitura.Tarefas.Count}, dias {nd}/{leitura.TrabalhoDiario.Count}.");
            }
            await tx.CommitAsync(ct);
            return (versaoId, false);
        }
        catch
        {
            try { await tx.RollbackAsync(CancellationToken.None); } catch { /* conexão já perdida: o SQL Server desfaz a transação */ }
            throw;
        }
    }

    private static async Task CopiarAsync(SqlConnection cn, SqlTransaction tx, string destino, DataTable dados, CancellationToken ct)
    {
        using var bulk = new SqlBulkCopy(cn, SqlBulkCopyOptions.CheckConstraints | SqlBulkCopyOptions.FireTriggers, tx)
        { DestinationTableName = destino, BatchSize = 5000, BulkCopyTimeout = 300 };
        foreach (DataColumn c in dados.Columns) bulk.ColumnMappings.Add(c.ColumnName, c.ColumnName);
        await bulk.WriteToServerAsync(dados, ct);
    }

    private static DataTable TabelaTarefas(int versaoId, string projectId, IEnumerable<BiTarefa> tarefas)
    {
        var t = new DataTable();
        void C<T>(string n) => t.Columns.Add(n, Nullable.GetUnderlyingType(typeof(T)) ?? typeof(T));
        C<int>("Versao_Id"); C<string>("Project_ID_ACC"); C<int>("UID_Tarefa"); C<int>("Ordem"); C<string>("GUID_Tarefa"); C<int?>("ID_Tarefa");
        C<int?>("UID_Tarefa_Pai"); C<int?>("Nivel_Estrutura"); C<bool>("Eh_Resumo"); C<bool>("Eh_Marco"); C<string>("Nome"); C<string>("EDT");
        C<DateTime?>("Inicio"); C<DateTime?>("Termino"); C<DateTime?>("Inicio_Linha_Base"); C<DateTime?>("Termino_Linha_Base");
        C<decimal?>("Trabalho_Horas"); C<decimal?>("Trabalho_Real_Horas"); C<decimal?>("Trabalho_Linha_Base_Horas"); C<decimal?>("Trabalho_Restante_Horas");
        C<decimal?>("Percentual_Concluido"); C<int>("Qtde_Atribuicoes"); C<string>("Calendario"); C<string>("Campos_Customizados_JSON");
        static object N(object? v) => v ?? DBNull.Value;
        foreach (var x in tarefas)
            t.Rows.Add(versaoId, projectId, x.UidTarefa, x.Ordem, N(x.GuidTarefa), N(x.IdTarefa), N(x.UidTarefaPai), N(x.NivelEstrutura), x.EhResumo, x.EhMarco,
                x.Nome, N(x.Edt), N(x.Inicio), N(x.Termino), N(x.InicioLinhaBase), N(x.TerminoLinhaBase), N(x.TrabalhoHoras), N(x.TrabalhoRealHoras),
                N(x.TrabalhoLinhaBaseHoras), N(x.TrabalhoRestanteHoras), N(x.PercentualConcluido), x.QtdeAtribuicoes, N(x.Calendario), x.CamposCustomizadosJson);
        return t;
    }

    private static DataTable TabelaDiario(int versaoId, string projectId, IEnumerable<BiTrabalhoDia> dias)
    {
        var t = new DataTable();
        t.Columns.Add("Versao_Id", typeof(int)); t.Columns.Add("Project_ID_ACC", typeof(string)); t.Columns.Add("UID_Tarefa", typeof(int));
        t.Columns.Add("Data", typeof(DateTime)); t.Columns.Add("Trabalho_Real_Horas", typeof(decimal));
        t.Columns.Add("Trabalho_Linha_Base_Horas", typeof(decimal)); t.Columns.Add("Trabalho_Planejado_Horas", typeof(decimal));
        foreach (var d in dias)
            t.Rows.Add(versaoId, projectId, d.UidTarefa, d.Data.Date, (object?)d.TrabalhoRealHoras ?? DBNull.Value,
                (object?)d.TrabalhoLinhaBaseHoras ?? DBNull.Value, (object?)d.TrabalhoPlanejadoHoras ?? DBNull.Value);
        return t;
    }

    // ------------------------------------------------------------------------------------------------ leitura (API)

    /// <summary>Versão pedida (se for do projeto) ou a última COMPLETA. null = nenhuma versão gravada.</summary>
    public async Task<int?> ResolverVersaoAsync(SqlConnection cn, string projectId, int? versao, CancellationToken ct)
    {
        await using var cmd = new SqlCommand("""
            SELECT TOP (1) Versao_Id FROM bi_chs001.Versao_Cronograma
            WHERE Project_ID_ACC = @p AND Status = N'COMPLETA' AND (@v IS NULL OR Versao_Id = @v)
            ORDER BY Versao_Id DESC
            """, cn);
        cmd.Parameters.Add("@p", SqlDbType.NVarChar, 100).Value = projectId;
        cmd.Parameters.Add("@v", SqlDbType.Int).Value = (object?)versao ?? DBNull.Value;
        return await cmd.ExecuteScalarAsync(ct) as int?;
    }

    public const string VersoesSql = """
        SELECT Versao_Id, Status, Origem, Arquivo_Nome, Arquivo_Item_Id, Arquivo_Versao_Urn, Arquivo_Sha256, Arquivo_Bytes, Data_Status_Projeto,
               Calendario_Padrao, Minutos_Por_Dia, Leitor, Lido_Em_Utc, Gravado_Em_Utc, Executado_Por, Total_Tarefas, Total_Folhas,
               Total_Linhas_Diarias, Total_Atribuicoes, Diagnostico_JSON
        FROM bi_chs001.Versao_Cronograma WHERE Project_ID_ACC = @projectId ORDER BY Versao_Id DESC;
        """;

    /// <summary>
    /// Tarefas da versão com os MESMOS nomes de coluna do endpoint legado /api/bi-chs001/cronograma (o frontend troca de fonte sem
    /// remapear) + os campos que o legado não tem. TASK_NAME = rótulo do BI (alias aplicado); TASK_NAME_Original = valor do .mpp.
    /// </summary>
    public const string TarefasSql = """
        SELECT v.Ordem AS ID_Cronograma, v.UID_Tarefa, v.EDT AS EDT_WBS, v.Nome_BI AS TASK_NAME, v.Nome_Original AS TASK_NAME_Original, v.Alias_Id,
               v.Eh_Resumo AS Eh_Tarefa_Resumo, v.Eh_Marco, v.UID_Tarefa_Pai, v.Nivel_Estrutura, v.ID_Tarefa, v.GUID_Tarefa,
               v.Inicio, v.Termino, v.Inicio_Linha_Base, v.Termino_Linha_Base,
               v.Trabalho_Horas, v.Trabalho_Real_Horas, v.Trabalho_Linha_Base_Horas, v.Trabalho_Restante_Horas, v.Percentual_Concluido,
               v.Qtde_Atribuicoes, v.Calendario,
               CASE WHEN ISJSON(v.Campos_Customizados_JSON) = 1 THEN JSON_VALUE(v.Campos_Customizados_JSON, '$.Status1') END AS Status1,
               CASE WHEN ISJSON(v.Campos_Customizados_JSON) = 1 THEN JSON_VALUE(v.Campos_Customizados_JSON, '$.Disciplina') END AS Disciplina,
               CASE WHEN ISJSON(v.Campos_Customizados_JSON) = 1 THEN JSON_VALUE(v.Campos_Customizados_JSON, '$.Responsabilidade') END AS Responsabilidade,
               CASE WHEN ISJSON(v.Campos_Customizados_JSON) = 1 THEN JSON_VALUE(v.Campos_Customizados_JSON, '$.Pacote') END AS Pacote,
               CASE WHEN ISJSON(v.Campos_Customizados_JSON) = 1 THEN JSON_VALUE(v.Campos_Customizados_JSON, '$."NA DOCUMENT NUMBER"') END AS NA_Document_Number_Campo,
               v.Versao_Id
        FROM bi_chs001.VW_Tarefa v
        WHERE v.Project_ID_ACC = @projectId AND v.Versao_Id = @versao
        ORDER BY v.Ordem;
        """;

    public const string TrabalhoDiarioSql = """
        SELECT d.UID_Tarefa, CONVERT(char(10), d.Data, 23) AS Data, d.Trabalho_Real_Horas, d.Trabalho_Linha_Base_Horas, d.Trabalho_Planejado_Horas, d.Versao_Id
        FROM bi_chs001.Trabalho_Tarefa_Dia d
        WHERE d.Project_ID_ACC = @projectId AND d.Versao_Id = @versao
        ORDER BY d.UID_Tarefa, d.Data;
        """;

    public const string TrabalhoSemanalSql = """
        SELECT s.UID_Tarefa, CONVERT(char(10), s.Domingo, 23) AS Domingo, s.Trabalho_Real_Horas, s.Trabalho_Linha_Base_Horas, s.Trabalho_Planejado_Horas, s.Dias, s.Versao_Id
        FROM bi_chs001.VW_Trabalho_Semana s
        WHERE s.Project_ID_ACC = @projectId AND s.Versao_Id = @versao
        ORDER BY s.UID_Tarefa, s.Domingo;
        """;

    public const string AliasSql = """
        SELECT Alias_Id, Campo, Valor_Bruto, Valor_Canonico, Somente_Resumo, Ativo, Motivo, Registrado_Em, Registrado_Por
        FROM bi_chs001.Alias_Rotulo WHERE Project_ID_ACC = @projectId ORDER BY Alias_Id;
        """;

    /// <summary>Comparação somente leitura com o snapshot legado (VW_Cronograma_Tipo2_Ultima_Versao) — informa se os dois leram o mesmo cronograma.</summary>
    public const string ComparacaoLegadoSql = """
        SELECT COUNT(*) AS Tarefas_Legado,
               SUM(CASE WHEN b.UID_Tarefa IS NOT NULL THEN 1 ELSE 0 END) AS Casadas_Por_UID,
               SUM(CASE WHEN b.UID_Tarefa IS NOT NULL AND CONVERT(date, b.Termino) = CONVERT(date, l.Termino)
                         AND CONVERT(date, b.Termino_Linha_Base) = CONVERT(date, l.Termino_Linha_Base) THEN 1 ELSE 0 END) AS Terminos_Iguais,
               MAX(l.Data_Sincronizacao) AS Data_Sincronizacao_Legado
        FROM dbo.VW_Cronograma_Tipo2_Ultima_Versao l
        LEFT JOIN bi_chs001.Tarefa_Snapshot b ON b.Versao_Id = @versao AND CONVERT(nvarchar(100), b.UID_Tarefa) = l.UID_Tarefa
        WHERE l.Project_ID_ACC = @projectId;
        """;
}
