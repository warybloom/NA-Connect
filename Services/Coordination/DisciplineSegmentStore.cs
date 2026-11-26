using System.Data;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;

namespace NA_Connect.Services.Coordination;

public sealed record ExcecaoPosicaoRegistro(long Id, string ProjectId, string Criterio, string? CodigoDocumento, string? SegmentoDoc3,
    string SiglaEsperada, int Posicao, string DisciplinaNoCadastro, string? Motivo, bool Ativo, int Versao,
    DateTime CriadoEm, string CriadoPor, DateTime AlteradoEm, string AlteradoPor)
{
    public ExcecaoPosicao Regra => new(Id, Criterio, CodigoDocumento, SegmentoDoc3, SiglaEsperada, Posicao, Ativo);
}

/// <summary>Nova exceção já validada contra o inventário e a Norma do projeto.</summary>
public sealed record NovaExcecaoPosicao(string Criterio, string? CodigoDocumento, string? SegmentoDoc3, string SiglaEsperada, int Posicao,
    string DisciplinaNoCadastro, string? Motivo);

public sealed record ExcecaoPosicaoAuditoria(long Id, long ExcecaoId, string ProjectId, string Acao, string? AntesJson, string? DepoisJson,
    string Usuario, DateTime DataHora);

public sealed class PosicaoDisciplinaNaoInstaladaException()
    : Exception("Exceções de posição da disciplina não instaladas: a migração Migrations/Normalizador/20261026_002_ExcecaoPosicaoDisciplina_UP.sql ainda não foi aplicada.");

public sealed class PosicaoDisciplinaInvalidaException(IReadOnlyList<string> erros) : Exception(string.Join(" ", erros))
{
    public IReadOnlyList<string> Erros { get; } = erros;
}

public sealed class PosicaoDisciplinaConcorrenciaException()
    : Exception("A exceção foi alterada por outra pessoa desde que a tela foi carregada. Recarregue e tente novamente.");

/// <summary>
/// Exceções pontuais de posição da sigla (schema normalizador, migração 20261026_002). Sem a tabela ou sem exceção ativa,
/// vale o DOC3 — o comportamento anterior. Reverter = desativar (nunca apaga). Toda mudança grava auditoria na mesma transação.
/// </summary>
public sealed class DisciplineSegmentStore(IConfiguration configuration)
{
    private const string Colunas = """
        Id, Project_ID_ACC, Criterio, CodigoDocumento, SegmentoDoc3, SiglaEsperada, Posicao, DisciplinaNoCadastro, Motivo, Ativo, Versao,
        CriadoEm, CriadoPor, AlteradoEm, AlteradoPor
        """;
    public const string SqlObterAtivas = """
        IF OBJECT_ID(N'normalizador.ExcecaoPosicaoDisciplina', N'U') IS NOT NULL
            SELECT Id, Criterio, CodigoDocumento, SegmentoDoc3, SiglaEsperada, Posicao
              FROM normalizador.ExcecaoPosicaoDisciplina WHERE Project_ID_ACC = @projectId AND Ativo = 1;
        """;
    public const string SqlInstalado = "SELECT CASE WHEN OBJECT_ID(N'normalizador.ExcecaoPosicaoDisciplina', N'U') IS NULL OR OBJECT_ID(N'normalizador.ExcecaoPosicaoDisciplinaAuditoria', N'U') IS NULL THEN 0 ELSE 1 END;";
    public const string SqlListar = $"SELECT {Colunas} FROM normalizador.ExcecaoPosicaoDisciplina WHERE (@projectId IS NULL OR Project_ID_ACC = @projectId) ORDER BY Project_ID_ACC, Id;";
    public const string SqlUma = $"SELECT {Colunas} FROM normalizador.ExcecaoPosicaoDisciplina WITH (UPDLOCK, HOLDLOCK) WHERE Id = @id;";
    public const string SqlInserir = $"""
        INSERT INTO normalizador.ExcecaoPosicaoDisciplina
            (Project_ID_ACC, Criterio, CodigoDocumento, CodigoChave, SegmentoDoc3, SegmentoChave, SiglaEsperada, SiglaChave, Posicao,
             DisciplinaNoCadastro, Motivo, Ativo, Versao, CriadoEm, CriadoPor, AlteradoEm, AlteradoPor)
        OUTPUT INSERTED.Id
        VALUES (@projectId, @criterio, @codigo, @codigoChave, @segmento, @segmentoChave, @sigla, @siglaChave, @posicao,
             @disciplina, @motivo, 1, 1, SYSUTCDATETIME(), @usuario, SYSUTCDATETIME(), @usuario);
        """;
    public const string SqlDefinirAtivo = """
        UPDATE normalizador.ExcecaoPosicaoDisciplina
           SET Ativo = @ativo, Versao = Versao + 1, AlteradoEm = SYSUTCDATETIME(), AlteradoPor = @usuario
         WHERE Id = @id AND Versao = @versao;
        """;
    public const string SqlAuditar = """
        INSERT INTO normalizador.ExcecaoPosicaoDisciplinaAuditoria (ExcecaoId, Project_ID_ACC, Acao, AntesJson, DepoisJson, Usuario, DataHora)
        VALUES (@excecaoId, @projectId, @acao, @antes, @depois, @usuario, SYSUTCDATETIME());
        """;
    public const string SqlHistorico = """
        SELECT TOP (300) Id, ExcecaoId, Project_ID_ACC, Acao, AntesJson, DepoisJson, Usuario, DataHora
          FROM normalizador.ExcecaoPosicaoDisciplinaAuditoria WHERE (@projectId IS NULL OR Project_ID_ACC = @projectId) ORDER BY Id DESC;
        """;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>Exceções ATIVAS do projeto na conexão do chamador (somente leitura). Sem tabela: nenhuma (DOC3).</summary>
    public static async Task<DisciplineSegmentRules> ObterAsync(SqlConnection conn, string projectId, CancellationToken ct)
    {
        await using var cmd = new SqlCommand(SqlObterAtivas, conn);
        cmd.Parameters.Add("@projectId", SqlDbType.NVarChar, 255).Value = projectId;
        await using var r = await cmd.ExecuteReaderAsync(ct);
        var lista = new List<ExcecaoPosicao>();
        while (await r.ReadAsync(ct))
        {
            var e = new ExcecaoPosicao(r.GetInt64(0), r.GetString(1), r.IsDBNull(2) ? null : r.GetString(2), r.IsDBNull(3) ? null : r.GetString(3),
                r.GetString(4), r.GetByte(5), true);
            // Linha fora do contrato (não deveria passar pelos CHECKs) é ignorada: nunca arrisca reclassificar.
            if (ExcecaoPosicao.Validar(e.Criterio, e.CodigoDocumento, e.SegmentoDoc3, e.SiglaEsperada, e.Posicao).Count == 0) lista.Add(e);
        }
        return lista.Count == 0 ? DisciplineSegmentRules.Padrao : new DisciplineSegmentRules(lista);
    }

    public static async Task<DisciplineSegmentRules> ObterAsync(IConfiguration configuration, string projectId, CancellationToken ct)
    {
        await using var conn = new SqlConnection(configuration.GetConnectionString("LocalConnection"));
        await conn.OpenAsync(ct);
        return await ObterAsync(conn, projectId, ct);
    }

    private async Task<SqlConnection> AbrirAsync(CancellationToken ct)
    {
        var c = new SqlConnection(configuration.GetConnectionString("LocalConnection")
            ?? throw new InvalidOperationException("Conexão indisponível."));
        await c.OpenAsync(ct);
        return c;
    }

    public async Task<bool> InstaladoAsync(CancellationToken ct)
    {
        await using var c = await AbrirAsync(ct);
        await using var cmd = new SqlCommand(SqlInstalado, c);
        return Convert.ToInt32(await cmd.ExecuteScalarAsync(ct)) == 1;
    }

    public async Task<IReadOnlyList<ExcecaoPosicaoRegistro>> ListarAsync(string? projectId, CancellationToken ct)
    {
        await using var c = await AbrirAsync(ct);
        await ExigirInstaladoAsync(c, null, ct);
        await using var cmd = new SqlCommand(SqlListar, c);
        cmd.Parameters.Add("@projectId", SqlDbType.NVarChar, 255).Value = (object?)projectId ?? DBNull.Value;
        await using var r = await cmd.ExecuteReaderAsync(ct);
        var lista = new List<ExcecaoPosicaoRegistro>();
        while (await r.ReadAsync(ct)) lista.Add(Ler(r));
        return lista;
    }

    public async Task<IReadOnlyList<ExcecaoPosicaoAuditoria>> HistoricoAsync(string? projectId, CancellationToken ct)
    {
        await using var c = await AbrirAsync(ct);
        await ExigirInstaladoAsync(c, null, ct);
        await using var cmd = new SqlCommand(SqlHistorico, c);
        cmd.Parameters.Add("@projectId", SqlDbType.NVarChar, 255).Value = (object?)projectId ?? DBNull.Value;
        await using var r = await cmd.ExecuteReaderAsync(ct);
        var lista = new List<ExcecaoPosicaoAuditoria>();
        while (await r.ReadAsync(ct))
            lista.Add(new(r.GetInt64(0), r.GetInt64(1), r.GetString(2), r.GetString(3), r.IsDBNull(4) ? null : r.GetString(4),
                r.IsDBNull(5) ? null : r.GetString(5), r.GetString(6), r.GetDateTime(7)));
        return lista;
    }

    /// <summary>Inclui um lote de exceções numa ÚNICA transação (tudo ou nada). Duplicata viola a chave única → nada é gravado.</summary>
    public async Task<IReadOnlyList<ExcecaoPosicaoRegistro>> IncluirAsync(string projectId, IReadOnlyList<NovaExcecaoPosicao> novas, string usuario,
        CancellationToken ct)
    {
        await using var c = await AbrirAsync(ct);
        await using var tx = (SqlTransaction)await c.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        await ExigirInstaladoAsync(c, tx, ct);
        var gravadas = new List<ExcecaoPosicaoRegistro>();
        foreach (var n in novas)
        {
            long id;
            await using (var cmd = new SqlCommand(SqlInserir, c, tx))
            {
                var codigo = n.Criterio == CriteriosExcecaoPosicao.Codigo ? DisciplineSegments.Chave(n.CodigoDocumento) : null;
                var segmento = n.Criterio == CriteriosExcecaoPosicao.Padrao ? DisciplineSegments.Chave(n.SegmentoDoc3) : null;
                cmd.Parameters.Add("@projectId", SqlDbType.NVarChar, 255).Value = projectId;
                cmd.Parameters.Add("@criterio", SqlDbType.VarChar, 10).Value = n.Criterio;
                cmd.Parameters.Add("@codigo", SqlDbType.NVarChar, 255).Value = (object?)codigo ?? DBNull.Value;
                cmd.Parameters.Add("@codigoChave", SqlDbType.NVarChar, 255).Value = codigo ?? "";
                cmd.Parameters.Add("@segmento", SqlDbType.NVarChar, 50).Value = (object?)segmento ?? DBNull.Value;
                cmd.Parameters.Add("@segmentoChave", SqlDbType.NVarChar, 50).Value = segmento ?? "";
                cmd.Parameters.Add("@sigla", SqlDbType.NVarChar, 50).Value = n.SiglaEsperada.Trim();
                cmd.Parameters.Add("@siglaChave", SqlDbType.NVarChar, 50).Value = DisciplineSegments.Chave(n.SiglaEsperada);
                cmd.Parameters.Add("@posicao", SqlDbType.TinyInt).Value = (byte)n.Posicao;
                cmd.Parameters.Add("@disciplina", SqlDbType.NVarChar, 200).Value = n.DisciplinaNoCadastro;
                cmd.Parameters.Add("@motivo", SqlDbType.NVarChar, 500).Value = string.IsNullOrWhiteSpace(n.Motivo) ? DBNull.Value : n.Motivo.Trim();
                cmd.Parameters.Add("@usuario", SqlDbType.NVarChar, 320).Value = usuario;
                id = Convert.ToInt64(await cmd.ExecuteScalarAsync(ct));
            }
            var depois = (await UmaAsync(c, tx, id, ct))!;
            await AuditarAsync(c, tx, depois, "INCLUIR", null, depois, usuario, ct);
            gravadas.Add(depois);
        }
        await tx.CommitAsync(ct);
        return gravadas;
    }

    /// <summary>Ativa/desativa (reverter = desativar; o registro e a auditoria são preservados).</summary>
    public async Task<ExcecaoPosicaoRegistro> DefinirAtivoAsync(string projectId, long id, int versao, bool ativo, string usuario, CancellationToken ct)
    {
        await using var c = await AbrirAsync(ct);
        await using var tx = (SqlTransaction)await c.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        await ExigirInstaladoAsync(c, tx, ct);
        var antes = await UmaAsync(c, tx, id, ct);
        if (antes is null || antes.ProjectId != projectId) throw new PosicaoDisciplinaInvalidaException(["Exceção não encontrada neste projeto."]);
        if (antes.Versao != versao) throw new PosicaoDisciplinaConcorrenciaException();
        await using (var cmd = new SqlCommand(SqlDefinirAtivo, c, tx))
        {
            cmd.Parameters.Add("@id", SqlDbType.BigInt).Value = id;
            cmd.Parameters.Add("@versao", SqlDbType.Int).Value = versao;
            cmd.Parameters.Add("@ativo", SqlDbType.Bit).Value = ativo;
            cmd.Parameters.Add("@usuario", SqlDbType.NVarChar, 320).Value = usuario;
            if (await cmd.ExecuteNonQueryAsync(ct) != 1) throw new PosicaoDisciplinaConcorrenciaException();
        }
        var depois = (await UmaAsync(c, tx, id, ct))!;
        await AuditarAsync(c, tx, depois, ativo ? "ATIVAR" : "DESATIVAR", antes, depois, usuario, ct);
        await tx.CommitAsync(ct);
        return depois;
    }

    private static async Task ExigirInstaladoAsync(SqlConnection c, SqlTransaction? tx, CancellationToken ct)
    {
        await using var cmd = new SqlCommand(SqlInstalado, c, tx);
        if (Convert.ToInt32(await cmd.ExecuteScalarAsync(ct)) != 1) throw new PosicaoDisciplinaNaoInstaladaException();
    }

    private static async Task<ExcecaoPosicaoRegistro?> UmaAsync(SqlConnection c, SqlTransaction tx, long id, CancellationToken ct)
    {
        await using var cmd = new SqlCommand(SqlUma, c, tx);
        cmd.Parameters.Add("@id", SqlDbType.BigInt).Value = id;
        await using var r = await cmd.ExecuteReaderAsync(ct);
        return await r.ReadAsync(ct) ? Ler(r) : null;
    }

    private static ExcecaoPosicaoRegistro Ler(SqlDataReader r) =>
        new(r.GetInt64(0), r.GetString(1), r.GetString(2), r.IsDBNull(3) ? null : r.GetString(3), r.IsDBNull(4) ? null : r.GetString(4),
            r.GetString(5), r.GetByte(6), r.GetString(7), r.IsDBNull(8) ? null : r.GetString(8), r.GetBoolean(9), r.GetInt32(10),
            r.GetDateTime(11), r.GetString(12), r.GetDateTime(13), r.GetString(14));

    private static async Task AuditarAsync(SqlConnection c, SqlTransaction tx, ExcecaoPosicaoRegistro alvo, string acao,
        ExcecaoPosicaoRegistro? antes, ExcecaoPosicaoRegistro? depois, string usuario, CancellationToken ct)
    {
        await using var cmd = new SqlCommand(SqlAuditar, c, tx);
        cmd.Parameters.Add("@excecaoId", SqlDbType.BigInt).Value = alvo.Id;
        cmd.Parameters.Add("@projectId", SqlDbType.NVarChar, 255).Value = alvo.ProjectId;
        cmd.Parameters.Add("@acao", SqlDbType.VarChar, 12).Value = acao;
        cmd.Parameters.Add("@antes", SqlDbType.NVarChar, -1).Value = antes is null ? DBNull.Value : JsonSerializer.Serialize(antes, Json);
        cmd.Parameters.Add("@depois", SqlDbType.NVarChar, -1).Value = depois is null ? DBNull.Value : JsonSerializer.Serialize(depois, Json);
        cmd.Parameters.Add("@usuario", SqlDbType.NVarChar, 320).Value = usuario;
        await cmd.ExecuteNonQueryAsync(ct);
    }
}
