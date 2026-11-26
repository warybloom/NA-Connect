using System.Data;
using System.Text.Json;
using Microsoft.Data.SqlClient;

namespace APS.WebApp.Services.DisciplineAlias;

/// <summary>
/// SQL Server: SOMENTE objetos do schema normalizador (Migrations/Normalizador). Nenhuma leitura/escrita em tabela legada.
/// Toda mudança grava uma linha em normalizador.SiglaDisciplinaAuditoria (antes/depois em JSON) na mesma transação.
/// </summary>
public sealed class SqlSiglaDisciplinaStore(IConfiguration configuration) : ISiglaDisciplinaStore
{
    private const string Colunas = """
        Id, SiglaOriginal, DisciplinaCanonica, ContextoTipo, ContextoValor, Ativo, Origem, ImportacaoId, Versao,
        CriadoEm, CriadoPor, AlteradoEm, AlteradoPor
        """;
    public const string SqlInstalado = "SELECT CASE WHEN OBJECT_ID(N'normalizador.SiglaDisciplina', N'U') IS NULL OR OBJECT_ID(N'normalizador.SiglaDisciplinaAuditoria', N'U') IS NULL OR OBJECT_ID(N'normalizador.Importacao', N'U') IS NULL THEN 0 ELSE 1 END;";
    public const string SqlListar = $"SELECT {Colunas} FROM normalizador.SiglaDisciplina ORDER BY SiglaChave, ContextoChave;";
    public const string SqlListarBloqueando = $"SELECT {Colunas} FROM normalizador.SiglaDisciplina WITH (UPDLOCK, HOLDLOCK) ORDER BY SiglaChave, ContextoChave;";
    public const string SqlUma = $"SELECT {Colunas} FROM normalizador.SiglaDisciplina WITH (UPDLOCK, HOLDLOCK) WHERE Id = @Id;";
    public const string SqlInserir = """
        INSERT INTO normalizador.SiglaDisciplina
            (SiglaOriginal, SiglaChave, DisciplinaCanonica, ContextoTipo, ContextoValor, ContextoChave, Ativo, Origem, ImportacaoId,
             Versao, CriadoEm, CriadoPor, AlteradoEm, AlteradoPor)
        OUTPUT INSERTED.Id
        VALUES (@SiglaOriginal, @SiglaChave, @DisciplinaCanonica, @ContextoTipo, @ContextoValor, @ContextoChave, @Ativo, @Origem, @ImportacaoId,
             1, SYSUTCDATETIME(), @Usuario, SYSUTCDATETIME(), @Usuario);
        """;
    public const string SqlAtualizar = """
        UPDATE normalizador.SiglaDisciplina
           SET SiglaOriginal = @SiglaOriginal, SiglaChave = @SiglaChave, DisciplinaCanonica = @DisciplinaCanonica,
               ContextoTipo = @ContextoTipo, ContextoValor = @ContextoValor, ContextoChave = @ContextoChave, Ativo = @Ativo,
               ImportacaoId = COALESCE(@ImportacaoId, ImportacaoId), Versao = Versao + 1, AlteradoEm = SYSUTCDATETIME(), AlteradoPor = @Usuario
         WHERE Id = @Id AND Versao = @Versao;
        """;
    public const string SqlExcluir = "DELETE FROM normalizador.SiglaDisciplina WHERE Id = @Id AND Versao = @Versao;";
    public const string SqlAuditar = """
        INSERT INTO normalizador.SiglaDisciplinaAuditoria (RegraId, Acao, AntesJson, DepoisJson, ImportacaoId, Usuario, DataHora)
        VALUES (@RegraId, @Acao, @AntesJson, @DepoisJson, @ImportacaoId, @Usuario, SYSUTCDATETIME());
        """;
    public const string SqlRegistrarImportacao = """
        INSERT INTO normalizador.Importacao (NomeArquivo, HashSha256, Aba, MapeamentoJson, Incluidas, Alteradas, Inalteradas, Rejeitadas,
            Duplicadas, RelatorioJson, ImportadoEm, ImportadoPor)
        OUTPUT INSERTED.Id
        VALUES (@NomeArquivo, @Hash, @Aba, @MapeamentoJson, 0, 0, 0, 0, 0, N'[]', SYSUTCDATETIME(), @Usuario);
        """;
    public const string SqlFecharImportacao = """
        UPDATE normalizador.Importacao SET Incluidas = @Incluidas, Alteradas = @Alteradas, Inalteradas = @Inalteradas, Rejeitadas = @Rejeitadas,
            Duplicadas = @Duplicadas, RelatorioJson = @RelatorioJson WHERE Id = @Id;
        """;
    public const string SqlListarImportacoes = """
        SELECT TOP (@Max) Id, NomeArquivo, HashSha256, Aba, Incluidas, Alteradas, Inalteradas, Rejeitadas, Duplicadas, ImportadoEm, ImportadoPor
        FROM normalizador.Importacao ORDER BY Id DESC;
        """;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

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
        using var cmd = new SqlCommand(SqlInstalado, c);
        return Convert.ToInt32(await cmd.ExecuteScalarAsync(ct)) == 1;
    }

    private async Task GarantirInstaladoAsync(SqlConnection c, SqlTransaction? tx, CancellationToken ct)
    {
        using var cmd = new SqlCommand(SqlInstalado, c, tx);
        if (Convert.ToInt32(await cmd.ExecuteScalarAsync(ct)) != 1) throw new NormalizadorNaoInstaladoException();
    }

    public async Task<IReadOnlyList<RegraSigla>> ListarAsync(CancellationToken ct)
    {
        await using var c = await AbrirAsync(ct);
        await GarantirInstaladoAsync(c, null, ct);
        return await LerAsync(new SqlCommand(SqlListar, c), ct);
    }

    private static async Task<List<RegraSigla>> LerAsync(SqlCommand cmd, CancellationToken ct)
    {
        using (cmd)
        {
            await using var r = await cmd.ExecuteReaderAsync(ct);
            var lista = new List<RegraSigla>();
            while (await r.ReadAsync(ct))
                lista.Add(new RegraSigla(r.GetInt64(0), r.GetString(1), r.GetString(2), r.IsDBNull(3) ? null : r.GetString(3),
                    r.IsDBNull(4) ? null : r.GetString(4), r.GetBoolean(5), r.GetString(6), r.IsDBNull(7) ? null : r.GetInt64(7),
                    r.GetInt32(8), r.GetDateTime(9), r.GetString(10), r.GetDateTime(11), r.GetString(12)));
            return lista;
        }
    }

    private static async Task<RegraSigla?> UmaAsync(SqlConnection c, SqlTransaction tx, long id, CancellationToken ct)
    {
        var cmd = new SqlCommand(SqlUma, c, tx);
        cmd.Parameters.Add("@Id", SqlDbType.BigInt).Value = id;
        return (await LerAsync(cmd, ct)).FirstOrDefault();
    }

    private static void Parametros(SqlCommand cmd, DadosRegra d, string usuario, long? importacaoId)
    {
        cmd.Parameters.Add("@SiglaOriginal", SqlDbType.NVarChar, SiglaNormalizer.MaxSigla).Value = d.SiglaOriginal;
        cmd.Parameters.Add("@SiglaChave", SqlDbType.NVarChar, SiglaNormalizer.MaxSigla).Value = SiglaNormalizer.Chave(d.SiglaOriginal);
        cmd.Parameters.Add("@DisciplinaCanonica", SqlDbType.NVarChar, SiglaNormalizer.MaxDisciplina).Value = d.DisciplinaCanonica;
        cmd.Parameters.Add("@ContextoTipo", SqlDbType.VarChar, 10).Value = (object?)d.ContextoTipo ?? DBNull.Value;
        cmd.Parameters.Add("@ContextoValor", SqlDbType.NVarChar, SiglaNormalizer.MaxContexto).Value = (object?)d.ContextoValor ?? DBNull.Value;
        cmd.Parameters.Add("@ContextoChave", SqlDbType.NVarChar, 220).Value = SiglaNormalizer.ChaveContexto(d.ContextoTipo, d.ContextoValor);
        cmd.Parameters.Add("@Ativo", SqlDbType.Bit).Value = d.Ativo;
        cmd.Parameters.Add("@ImportacaoId", SqlDbType.BigInt).Value = (object?)importacaoId ?? DBNull.Value;
        cmd.Parameters.Add("@Usuario", SqlDbType.NVarChar, 320).Value = usuario;
    }

    private static async Task AuditarAsync(SqlConnection c, SqlTransaction tx, long regraId, string acao, object? antes, object? depois,
        long? importacaoId, string usuario, CancellationToken ct)
    {
        using var cmd = new SqlCommand(SqlAuditar, c, tx);
        cmd.Parameters.Add("@RegraId", SqlDbType.BigInt).Value = regraId;
        cmd.Parameters.Add("@Acao", SqlDbType.VarChar, 12).Value = acao;
        cmd.Parameters.Add("@AntesJson", SqlDbType.NVarChar, -1).Value = antes is null ? DBNull.Value : JsonSerializer.Serialize(antes, Json);
        cmd.Parameters.Add("@DepoisJson", SqlDbType.NVarChar, -1).Value = depois is null ? DBNull.Value : JsonSerializer.Serialize(depois, Json);
        cmd.Parameters.Add("@ImportacaoId", SqlDbType.BigInt).Value = (object?)importacaoId ?? DBNull.Value;
        cmd.Parameters.Add("@Usuario", SqlDbType.NVarChar, 320).Value = usuario;
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static async Task<long> InserirAsync(SqlConnection c, SqlTransaction tx, DadosRegra d, string origem, long? importacaoId,
        string usuario, CancellationToken ct)
    {
        using var cmd = new SqlCommand(SqlInserir, c, tx);
        Parametros(cmd, d, usuario, importacaoId);
        cmd.Parameters.Add("@Origem", SqlDbType.VarChar, 12).Value = origem;
        try { return Convert.ToInt64(await cmd.ExecuteScalarAsync(ct)); }
        catch (SqlException ex) when (ex.Number is 2627 or 2601)
        {
            throw new RegraDuplicadaException(d.SiglaOriginal, SiglaNormalizer.ChaveContexto(d.ContextoTipo, d.ContextoValor));
        }
    }

    private static async Task AtualizarLinhaAsync(SqlConnection c, SqlTransaction tx, long id, int versao, DadosRegra d, long? importacaoId,
        string usuario, CancellationToken ct)
    {
        using var cmd = new SqlCommand(SqlAtualizar, c, tx);
        Parametros(cmd, d, usuario, importacaoId);
        cmd.Parameters.Add("@Id", SqlDbType.BigInt).Value = id;
        cmd.Parameters.Add("@Versao", SqlDbType.Int).Value = versao;
        int n;
        try { n = await cmd.ExecuteNonQueryAsync(ct); }
        catch (SqlException ex) when (ex.Number is 2627 or 2601)
        {
            throw new RegraDuplicadaException(d.SiglaOriginal, SiglaNormalizer.ChaveContexto(d.ContextoTipo, d.ContextoValor));
        }
        if (n != 1) throw new ConcorrenciaException();
    }

    private static string AcaoDe(RegraSigla antes, DadosRegra depois) =>
        antes.SiglaOriginal == depois.SiglaOriginal && antes.DisciplinaCanonica == depois.DisciplinaCanonica
        && antes.ContextoTipo == depois.ContextoTipo && antes.ContextoValor == depois.ContextoValor && antes.Ativo != depois.Ativo
            ? depois.Ativo ? "ATIVAR" : "DESATIVAR"
            : "ALTERAR";

    public async Task<RegraSigla> CriarAsync(DadosRegra dados, string usuario, CancellationToken ct)
    {
        await using var c = await AbrirAsync(ct);
        await using var tx = (SqlTransaction)await c.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        await GarantirInstaladoAsync(c, tx, ct);
        var id = await InserirAsync(c, tx, dados, "MANUAL", null, usuario, ct);
        var nova = (await UmaAsync(c, tx, id, ct))!;
        await AuditarAsync(c, tx, id, "INCLUIR", null, nova, null, usuario, ct);
        await tx.CommitAsync(ct);
        return nova;
    }

    public async Task<RegraSigla> AtualizarAsync(long id, int versao, DadosRegra dados, string usuario, CancellationToken ct)
    {
        await using var c = await AbrirAsync(ct);
        await using var tx = (SqlTransaction)await c.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        await GarantirInstaladoAsync(c, tx, ct);
        var antes = await UmaAsync(c, tx, id, ct) ?? throw new RegraNaoEncontradaException(id);
        if (antes.Versao != versao) throw new ConcorrenciaException();
        await AtualizarLinhaAsync(c, tx, id, versao, dados, null, usuario, ct);
        var depois = (await UmaAsync(c, tx, id, ct))!;
        await AuditarAsync(c, tx, id, AcaoDe(antes, dados), antes, depois, null, usuario, ct);
        await tx.CommitAsync(ct);
        return depois;
    }

    public async Task ExcluirAsync(long id, int versao, string usuario, CancellationToken ct)
    {
        await using var c = await AbrirAsync(ct);
        await using var tx = (SqlTransaction)await c.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        await GarantirInstaladoAsync(c, tx, ct);
        var antes = await UmaAsync(c, tx, id, ct) ?? throw new RegraNaoEncontradaException(id);
        // A auditoria guarda o retrato completo ANTES da exclusão (nenhum dado se perde).
        await AuditarAsync(c, tx, id, "EXCLUIR", antes, null, null, usuario, ct);
        using var cmd = new SqlCommand(SqlExcluir, c, tx);
        cmd.Parameters.Add("@Id", SqlDbType.BigInt).Value = id;
        cmd.Parameters.Add("@Versao", SqlDbType.Int).Value = versao;
        if (await cmd.ExecuteNonQueryAsync(ct) != 1) throw new ConcorrenciaException();
        await tx.CommitAsync(ct);
    }

    public async Task<ResultadoImportacao> AplicarImportacaoAsync(Func<IReadOnlyList<RegraSigla>, PlanoImportacao> planejar, string nomeArquivo,
        string hashSha256, string usuario, CancellationToken ct)
    {
        await using var c = await AbrirAsync(ct);
        await using var tx = (SqlTransaction)await c.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        await GarantirInstaladoAsync(c, tx, ct);
        var atuais = await LerAsync(new SqlCommand(SqlListarBloqueando, c, tx), ct);
        var plano = planejar(atuais);

        long importacaoId;
        using (var cmd = new SqlCommand(SqlRegistrarImportacao, c, tx))
        {
            cmd.Parameters.Add("@NomeArquivo", SqlDbType.NVarChar, 260).Value = Path.GetFileName(nomeArquivo);
            cmd.Parameters.Add("@Hash", SqlDbType.Char, 64).Value = hashSha256;
            cmd.Parameters.Add("@Aba", SqlDbType.NVarChar, 128).Value = plano.Aba;
            cmd.Parameters.Add("@MapeamentoJson", SqlDbType.NVarChar, -1).Value = JsonSerializer.Serialize(plano.Mapeamento, Json);
            cmd.Parameters.Add("@Usuario", SqlDbType.NVarChar, 320).Value = usuario;
            importacaoId = Convert.ToInt64(await cmd.ExecuteScalarAsync(ct));
        }
        var porId = atuais.ToDictionary(r => r.Id);
        foreach (var l in plano.Linhas)
        {
            if (l.Acao is not (AcaoImportacao.Incluir or AcaoImportacao.Alterar)) continue;
            var dados = new DadosRegra(l.SiglaOriginal!, l.DisciplinaCanonica!, l.ContextoTipo, l.ContextoValor, l.Ativo ?? true);
            if (l.Acao == AcaoImportacao.Incluir)
            {
                var id = await InserirAsync(c, tx, dados, "IMPORTACAO", importacaoId, usuario, ct);
                await AuditarAsync(c, tx, id, "INCLUIR", null, await UmaAsync(c, tx, id, ct), importacaoId, usuario, ct);
            }
            else
            {
                var antes = porId[l.RegraExistenteId!.Value];
                await AtualizarLinhaAsync(c, tx, antes.Id, antes.Versao, dados, importacaoId, usuario, ct);
                await AuditarAsync(c, tx, antes.Id, AcaoDe(antes, dados), antes, await UmaAsync(c, tx, antes.Id, ct), importacaoId, usuario, ct);
            }
        }
        var resumo = plano.Resumo;
        using (var cmd = new SqlCommand(SqlFecharImportacao, c, tx))
        {
            cmd.Parameters.Add("@Id", SqlDbType.BigInt).Value = importacaoId;
            cmd.Parameters.Add("@Incluidas", SqlDbType.Int).Value = resumo.Incluidas;
            cmd.Parameters.Add("@Alteradas", SqlDbType.Int).Value = resumo.Alteradas;
            cmd.Parameters.Add("@Inalteradas", SqlDbType.Int).Value = resumo.Inalteradas;
            cmd.Parameters.Add("@Rejeitadas", SqlDbType.Int).Value = resumo.Rejeitadas;
            cmd.Parameters.Add("@Duplicadas", SqlDbType.Int).Value = resumo.Duplicadas;
            cmd.Parameters.Add("@RelatorioJson", SqlDbType.NVarChar, -1).Value = JsonSerializer.Serialize(plano.Linhas, Json);
            await cmd.ExecuteNonQueryAsync(ct);
        }
        await tx.CommitAsync(ct);
        return new ResultadoImportacao(importacaoId, resumo, plano.Linhas);
    }

    public async Task<IReadOnlyList<ImportacaoRegistrada>> ListarImportacoesAsync(int maximo, CancellationToken ct)
    {
        await using var c = await AbrirAsync(ct);
        await GarantirInstaladoAsync(c, null, ct);
        using var cmd = new SqlCommand(SqlListarImportacoes, c);
        cmd.Parameters.Add("@Max", SqlDbType.Int).Value = maximo;
        await using var r = await cmd.ExecuteReaderAsync(ct);
        var lista = new List<ImportacaoRegistrada>();
        while (await r.ReadAsync(ct))
            lista.Add(new ImportacaoRegistrada(r.GetInt64(0), r.GetString(1), r.GetString(2), r.GetString(3), r.GetInt32(4), r.GetInt32(5),
                r.GetInt32(6), r.GetInt32(7), r.GetInt32(8), r.GetDateTime(9), r.GetString(10)));
        return lista;
    }
}
