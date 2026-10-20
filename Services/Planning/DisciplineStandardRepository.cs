using System.Data;
using Microsoft.Data.SqlClient;

namespace APS.WebApp.Services.Planning;

public sealed record DisciplineStandardDto(
    int DisciplineStandardId,
    string StandardName,
    string? PortugueseName,
    string? EnglishName,
    bool Active,
    IReadOnlyList<string> Aliases);

public sealed class DisciplineStandardRepository(IConfiguration configuration)
{
    private readonly string _cs = configuration.GetConnectionString("LocalConnection")
        ?? throw new InvalidOperationException("Conexão indisponível.");

    /// <summary>
    /// Nome (normalizado) -&gt; Nome_Padrao ativo. Prioridade por nível, para um cadastro global com nomes repetidos não
    /// apagar a disciplina inteira: 1) Nome_Padrao; 2) alias explícito; 3) Nome_PT/Nome_EN. Dentro do mesmo nível, nome
    /// que aponta para mais de um padrão é descartado (nunca escolhe o primeiro). Ex. auditado 15/10/2026: ID 19
    /// "3D MODELING" tem Nome_PT/Nome_EN "DIGITAL SYSTEMS", que é o Nome_Padrao do ID 8 — "DIGITAL SYSTEMS" resolve para o ID 8.
    /// </summary>
    public static IReadOnlyDictionary<string,string> CanonicalNameMap(IEnumerable<DisciplineStandardDto> standards)
    {
        var ativos = standards.Where(x=>x.Active).ToArray();
        var niveis = new[]
        {
            ativos.Select(x=>(Nomes:(IEnumerable<string?>)new[]{x.StandardName},x.StandardName)),
            ativos.Select(x=>(Nomes:(IEnumerable<string?>)x.Aliases,x.StandardName)),
            ativos.Select(x=>(Nomes:(IEnumerable<string?>)new[]{x.PortugueseName,x.EnglishName},x.StandardName)),
        };
        var map = new Dictionary<string,string>(StringComparer.Ordinal);
        foreach (var nivel in niveis)
        {
            var grupos = nivel.SelectMany(x=>x.Nomes.Where(a=>!string.IsNullOrWhiteSpace(a))
                    .Select(a=>(Key:DisciplineCanonicalRules.Normalize(a),Name:x.StandardName)))
                .GroupBy(x=>x.Key,StringComparer.Ordinal).ToArray();
            foreach (var g in grupos)
            {
                if (map.ContainsKey(g.Key)) continue;
                var nomes = g.Select(x=>x.Name).Distinct(StringComparer.Ordinal).ToArray();
                if (nomes.Length==1) map[g.Key]=nomes[0];
            }
            // Nome ambíguo num nível também bloqueia os níveis seguintes (não vira "primeiro que aparecer").
            foreach (var g in grupos) map.TryAdd(g.Key, null!);
        }
        foreach (var k in map.Where(kv=>kv.Value is null).Select(kv=>kv.Key).ToArray()) map.Remove(k);
        return map;
    }

    public async Task<IReadOnlyList<DisciplineStandardDto>> ListAsync(CancellationToken ct)
    {
        await using var con = new SqlConnection(_cs);
        await con.OpenAsync(ct);

        var map = new Dictionary<int, (string Standard, string? Pt, string? En, bool Active, List<string> Aliases)>();
        await using (var cmd = new SqlCommand("""
            SELECT DisciplinaPadraoId, Nome_Padrao, Nome_PT, Nome_EN, Ativa
            FROM coordenacao.DisciplinaPadrao
            ORDER BY Nome_Padrao;
            """, con))
        await using (var r = await cmd.ExecuteReaderAsync(ct))
            while (await r.ReadAsync(ct))
                map[r.GetInt32(0)] = (
                    r.GetString(1),
                    r.IsDBNull(2) ? null : r.GetString(2),
                    r.IsDBNull(3) ? null : r.GetString(3),
                    r.GetBoolean(4),
                    []);

        await using (var cmd = new SqlCommand("""
            SELECT DisciplinaPadraoId, Alias
            FROM coordenacao.DisciplinaAlias
            ORDER BY Alias;
            """, con))
        await using (var r = await cmd.ExecuteReaderAsync(ct))
            while (await r.ReadAsync(ct))
                if (map.TryGetValue(r.GetInt32(0), out var item))
                    item.Aliases.Add(r.GetString(1));

        return map.Select(x => new DisciplineStandardDto(
                x.Key, x.Value.Standard, x.Value.Pt, x.Value.En, x.Value.Active, x.Value.Aliases))
            .ToArray();
    }

    public async Task<int> CreateAsync(
        string standardName,
        string? portugueseName,
        string? englishName,
        IEnumerable<string>? aliases,
        CancellationToken ct)
    {
        standardName = CleanRequired(standardName, "Nome padrão");
        portugueseName = CleanOptional(portugueseName);
        englishName = CleanOptional(englishName);

        await using var con = new SqlConnection(_cs);
        await con.OpenAsync(ct);
        await using var tx = (SqlTransaction)await con.BeginTransactionAsync(ct);

        await EnsureStandardNameAvailableAsync(con, tx, 0, standardName, ct);

        int id;
        await using (var cmd = new SqlCommand("""
            INSERT INTO coordenacao.DisciplinaPadrao(Nome_Padrao, Nome_PT, Nome_EN)
            OUTPUT INSERTED.DisciplinaPadraoId
            VALUES(@padrao,@pt,@en);
            """, con, tx))
        {
            cmd.Parameters.Add("@padrao", SqlDbType.NVarChar, 200).Value = standardName;
            cmd.Parameters.Add("@pt", SqlDbType.NVarChar, 200).Value = (object?)portugueseName ?? DBNull.Value;
            cmd.Parameters.Add("@en", SqlDbType.NVarChar, 200).Value = (object?)englishName ?? DBNull.Value;
            id = Convert.ToInt32(await cmd.ExecuteScalarAsync(ct));
        }

        foreach (var alias in new[] { standardName, portugueseName, englishName }.Concat(aliases ?? []))
            await EnsureAliasAsync(con, tx, id, alias, ct, rejectOtherOwner: true);

        await tx.CommitAsync(ct);
        return id;
    }

    public async Task RenameAndCascadeAsync(
        int id,
        string standardName,
        string? portugueseName,
        string? englishName,
        CancellationToken ct)
    {
        standardName = CleanRequired(standardName, "Nome padrão");
        portugueseName = CleanOptional(portugueseName);
        englishName = CleanOptional(englishName);

        await using var con = new SqlConnection(_cs);
        await con.OpenAsync(ct);
        await using var tx = (SqlTransaction)await con.BeginTransactionAsync(ct);

        string oldStandard;
        string? oldPt;
        string? oldEn;
        await using (var read = new SqlCommand("""
            SELECT Nome_Padrao, Nome_PT, Nome_EN
            FROM coordenacao.DisciplinaPadrao WITH (UPDLOCK, HOLDLOCK)
            WHERE DisciplinaPadraoId=@id;
            """, con, tx))
        {
            read.Parameters.Add("@id", SqlDbType.Int).Value = id;
            await using var r = await read.ExecuteReaderAsync(ct);
            if (!await r.ReadAsync(ct)) throw new KeyNotFoundException("Disciplina padrão não encontrada.");
            oldStandard = r.GetString(0);
            oldPt = r.IsDBNull(1) ? null : r.GetString(1);
            oldEn = r.IsDBNull(2) ? null : r.GetString(2);
        }

        // Se o nome escolhido já existe como outro padrão, a intenção é consolidar/mesclar,
        // não criar duas disciplinas oficiais com o mesmo significado.
        int? mergeTargetId = null;
        string? mergeTargetName = null;
        await using (var findTarget = new SqlCommand("""
            SELECT TOP 1 DisciplinaPadraoId, Nome_Padrao
            FROM coordenacao.DisciplinaPadrao
            WHERE DisciplinaPadraoId<>@id
              AND UPPER(LTRIM(RTRIM(Nome_Padrao))) COLLATE Latin1_General_100_CI_AI =
                  UPPER(LTRIM(RTRIM(@v))) COLLATE Latin1_General_100_CI_AI;
            """, con, tx))
        {
            findTarget.Parameters.Add("@id", SqlDbType.Int).Value = id;
            findTarget.Parameters.Add("@v", SqlDbType.NVarChar, 200).Value = standardName;
            await using var targetReader = await findTarget.ExecuteReaderAsync(ct);
            if (await targetReader.ReadAsync(ct))
            {
                mergeTargetId = targetReader.GetInt32(0);
                mergeTargetName = targetReader.GetString(1);
            }
        }

        if (mergeTargetId.HasValue)
        {
            await using (var cascadeMerge = new SqlCommand("""
                UPDATE M
                   SET M.Disciplina=@targetName
                FROM coordenacao.NormaMapeamento M
                WHERE EXISTS (
                    SELECT 1 FROM coordenacao.DisciplinaAlias A
                    WHERE A.DisciplinaPadraoId=@sourceId
                      AND UPPER(LTRIM(RTRIM(A.Alias))) COLLATE Latin1_General_100_CI_AI =
                          UPPER(LTRIM(RTRIM(M.Disciplina))) COLLATE Latin1_General_100_CI_AI
                )
                OR UPPER(LTRIM(RTRIM(M.Disciplina))) COLLATE Latin1_General_100_CI_AI IN (
                    UPPER(LTRIM(RTRIM(@oldStandard))) COLLATE Latin1_General_100_CI_AI,
                    UPPER(LTRIM(RTRIM(ISNULL(@oldPt,'')))) COLLATE Latin1_General_100_CI_AI,
                    UPPER(LTRIM(RTRIM(ISNULL(@oldEn,'')))) COLLATE Latin1_General_100_CI_AI
                );
                """, con, tx))
            {
                cascadeMerge.Parameters.Add("@targetName", SqlDbType.NVarChar, 300).Value = mergeTargetName!;
                cascadeMerge.Parameters.Add("@sourceId", SqlDbType.Int).Value = id;
                cascadeMerge.Parameters.Add("@oldStandard", SqlDbType.NVarChar, 200).Value = oldStandard;
                cascadeMerge.Parameters.Add("@oldPt", SqlDbType.NVarChar, 200).Value = (object?)oldPt ?? DBNull.Value;
                cascadeMerge.Parameters.Add("@oldEn", SqlDbType.NVarChar, 200).Value = (object?)oldEn ?? DBNull.Value;
                await cascadeMerge.ExecuteNonQueryAsync(ct);
            }

            // Remove aliases duplicados e move todos os demais para o padrão de destino.
            await using (var move = new SqlCommand("""
                DELETE S
                FROM coordenacao.DisciplinaAlias S
                WHERE S.DisciplinaPadraoId=@sourceId
                  AND EXISTS (
                      SELECT 1 FROM coordenacao.DisciplinaAlias T
                      WHERE T.DisciplinaPadraoId=@targetId
                        AND UPPER(LTRIM(RTRIM(T.Alias))) COLLATE Latin1_General_100_CI_AI =
                            UPPER(LTRIM(RTRIM(S.Alias))) COLLATE Latin1_General_100_CI_AI
                  );
                UPDATE coordenacao.DisciplinaAlias
                   SET DisciplinaPadraoId=@targetId
                 WHERE DisciplinaPadraoId=@sourceId;
                """, con, tx))
            {
                move.Parameters.Add("@sourceId", SqlDbType.Int).Value = id;
                move.Parameters.Add("@targetId", SqlDbType.Int).Value = mergeTargetId.Value;
                await move.ExecuteNonQueryAsync(ct);
            }

            foreach (var alias in new[] { oldStandard, oldPt, oldEn, portugueseName, englishName })
                await EnsureAliasAsync(con, tx, mergeTargetId.Value, alias, ct);

            await using (var updateTarget = new SqlCommand("""
                UPDATE coordenacao.DisciplinaPadrao
                   SET Nome_PT=COALESCE(@pt,Nome_PT),
                       Nome_EN=COALESCE(@en,Nome_EN),
                       Data_Atualizacao=SYSUTCDATETIME()
                 WHERE DisciplinaPadraoId=@targetId;
                DELETE FROM coordenacao.DisciplinaPadrao WHERE DisciplinaPadraoId=@sourceId;
                """, con, tx))
            {
                updateTarget.Parameters.Add("@pt", SqlDbType.NVarChar, 200).Value = (object?)portugueseName ?? DBNull.Value;
                updateTarget.Parameters.Add("@en", SqlDbType.NVarChar, 200).Value = (object?)englishName ?? DBNull.Value;
                updateTarget.Parameters.Add("@targetId", SqlDbType.Int).Value = mergeTargetId.Value;
                updateTarget.Parameters.Add("@sourceId", SqlDbType.Int).Value = id;
                await updateTarget.ExecuteNonQueryAsync(ct);
            }

            await tx.CommitAsync(ct);
            return;
        }

        await EnsureStandardNameAvailableAsync(con, tx, id, standardName, ct);

        await using (var update = new SqlCommand("""
            UPDATE coordenacao.DisciplinaPadrao
            SET Nome_Padrao=@padrao, Nome_PT=@pt, Nome_EN=@en, Data_Atualizacao=SYSUTCDATETIME()
            WHERE DisciplinaPadraoId=@id;
            """, con, tx))
        {
            update.Parameters.Add("@padrao", SqlDbType.NVarChar, 200).Value = standardName;
            update.Parameters.Add("@pt", SqlDbType.NVarChar, 200).Value = (object?)portugueseName ?? DBNull.Value;
            update.Parameters.Add("@en", SqlDbType.NVarChar, 200).Value = (object?)englishName ?? DBNull.Value;
            update.Parameters.Add("@id", SqlDbType.Int).Value = id;
            await update.ExecuteNonQueryAsync(ct);
        }

        foreach (var alias in new[] { oldStandard, oldPt, oldEn, standardName, portugueseName, englishName })
            await EnsureAliasAsync(con, tx, id, alias, ct);

        // Toda Norma de Coordenação que já apontava para qualquer nome/alias deste grupo
        // passa a apontar imediatamente para o novo nome padrão.
        await using (var cascade = new SqlCommand("""
            UPDATE M
               SET M.Disciplina=@padrao
            FROM coordenacao.NormaMapeamento M
            WHERE EXISTS (
                SELECT 1
                FROM coordenacao.DisciplinaAlias A
                WHERE A.DisciplinaPadraoId=@id
                  AND UPPER(LTRIM(RTRIM(A.Alias))) COLLATE Latin1_General_100_CI_AI =
                      UPPER(LTRIM(RTRIM(M.Disciplina))) COLLATE Latin1_General_100_CI_AI
            );
            """, con, tx))
        {
            cascade.Parameters.Add("@padrao", SqlDbType.NVarChar, 300).Value = standardName;
            cascade.Parameters.Add("@id", SqlDbType.Int).Value = id;
            await cascade.ExecuteNonQueryAsync(ct);
        }

        await tx.CommitAsync(ct);
    }

    public async Task AssignAliasAsync(string alias, int disciplineStandardId, CancellationToken ct)
    {
        alias = CleanRequired(alias, "Nome encontrado");

        await using var con = new SqlConnection(_cs);
        await con.OpenAsync(ct);
        await using var tx = (SqlTransaction)await con.BeginTransactionAsync(ct);

        string standardName;
        await using (var cmd = new SqlCommand("""
            SELECT Nome_Padrao FROM coordenacao.DisciplinaPadrao
            WHERE DisciplinaPadraoId=@id AND Ativa=1;
            """, con, tx))
        {
            cmd.Parameters.Add("@id", SqlDbType.Int).Value = disciplineStandardId;
            var value = await cmd.ExecuteScalarAsync(ct);
            standardName = value?.ToString() ?? throw new KeyNotFoundException("Disciplina padrão não encontrada.");
        }

        // Reatribuição é permitida: esta tela é a autoridade para corrigir um nome que tenha sido
        // padronizado no grupo errado. O alias continua único globalmente.
        await using (var moveAlias = new SqlCommand("""
            UPDATE coordenacao.DisciplinaAlias
               SET DisciplinaPadraoId=@id
             WHERE UPPER(LTRIM(RTRIM(Alias))) COLLATE Latin1_General_100_CI_AI =
                   UPPER(LTRIM(RTRIM(@alias))) COLLATE Latin1_General_100_CI_AI;
            IF @@ROWCOUNT = 0
                INSERT INTO coordenacao.DisciplinaAlias(DisciplinaPadraoId, Alias) VALUES(@id,@alias);
            """, con, tx))
        {
            moveAlias.Parameters.Add("@id", SqlDbType.Int).Value = disciplineStandardId;
            moveAlias.Parameters.Add("@alias", SqlDbType.NVarChar, 200).Value = alias;
            await moveAlias.ExecuteNonQueryAsync(ct);
        }

        // Se alguma Norma de Coordenação usa exatamente esse nome bruto, ela também é alinhada.
        await using (var cascade = new SqlCommand("""
            UPDATE coordenacao.NormaMapeamento
            SET Disciplina=@padrao
            WHERE UPPER(LTRIM(RTRIM(Disciplina))) COLLATE Latin1_General_100_CI_AI =
                  UPPER(LTRIM(RTRIM(@alias))) COLLATE Latin1_General_100_CI_AI;
            """, con, tx))
        {
            cascade.Parameters.Add("@padrao", SqlDbType.NVarChar, 300).Value = standardName;
            cascade.Parameters.Add("@alias", SqlDbType.NVarChar, 200).Value = alias;
            await cascade.ExecuteNonQueryAsync(ct);
        }

        await tx.CommitAsync(ct);
    }

    private static async Task EnsureStandardNameAvailableAsync(
        SqlConnection con, SqlTransaction tx, int id, string value, CancellationToken ct)
    {
        await using var cmd = new SqlCommand("""
            SELECT COUNT(*) FROM coordenacao.DisciplinaPadrao
            WHERE DisciplinaPadraoId<>@id
              AND UPPER(LTRIM(RTRIM(Nome_Padrao))) COLLATE Latin1_General_100_CI_AI =
                  UPPER(LTRIM(RTRIM(@v))) COLLATE Latin1_General_100_CI_AI;
            """, con, tx);
        cmd.Parameters.Add("@id", SqlDbType.Int).Value = id;
        cmd.Parameters.Add("@v", SqlDbType.NVarChar, 200).Value = value;
        if (Convert.ToInt32(await cmd.ExecuteScalarAsync(ct)) > 0)
            throw new InvalidOperationException($"Já existe uma disciplina padrão chamada '{value}'.");
    }

    private static async Task EnsureAliasAsync(
        SqlConnection con,
        SqlTransaction tx,
        int id,
        string? alias,
        CancellationToken ct,
        bool rejectOtherOwner = false)
    {
        alias = CleanOptional(alias);
        if (alias is null) return;

        await using var owner = new SqlCommand("""
            SELECT TOP 1 DisciplinaPadraoId
            FROM coordenacao.DisciplinaAlias
            WHERE UPPER(LTRIM(RTRIM(Alias))) COLLATE Latin1_General_100_CI_AI =
                  UPPER(LTRIM(RTRIM(@alias))) COLLATE Latin1_General_100_CI_AI;
            """, con, tx);
        owner.Parameters.Add("@alias", SqlDbType.NVarChar, 200).Value = alias;
        var existing = await owner.ExecuteScalarAsync(ct);
        if (existing is not null && existing is not DBNull)
        {
            var ownerId = Convert.ToInt32(existing);
            if (ownerId == id) return;
            if (rejectOtherOwner)
                throw new InvalidOperationException($"O nome '{alias}' já pertence a outra disciplina padrão.");
            return;
        }

        await using var insert = new SqlCommand("""
            INSERT INTO coordenacao.DisciplinaAlias(DisciplinaPadraoId, Alias)
            VALUES(@id,@alias);
            """, con, tx);
        insert.Parameters.Add("@id", SqlDbType.Int).Value = id;
        insert.Parameters.Add("@alias", SqlDbType.NVarChar, 200).Value = alias;
        await insert.ExecuteNonQueryAsync(ct);
    }

    private static string CleanRequired(string? value, string field) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException($"{field} não pode ser vazio.")
            : value.Trim();

    private static string? CleanOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
