using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace NA_Connect.Services.Coordination;

/// <summary>
/// Persistência de Norma de Coordenação — schema coordenacao.* (Migrations/Coordenacao/*.sql). Auditado
/// 2026-09-22: config.Norma/config.NormaMapeamento representam um conceito DIFERENTE (nomenclatura de
/// placeholder/pasta destino do PlaceholderCreationService — Sigla->Pasta_Destino/Tipo_Documento), vazias
/// e não usadas pelo app atual; ldp.ProjetoConfiguracao (Novo Motor de LDP) também é conceito diferente
/// (estrutura/template). Nenhum dos dois foi reaproveitado — schema novo e isolado, por decisão do
/// próprio usuário ("se o conceito existente for diferente, não misturar silenciosamente").
/// </summary>
public sealed class CoordinationStandardRepository(IConfiguration configuration, ILogger<CoordinationStandardRepository> logger)
{
    private readonly string _cs = configuration.GetConnectionString("LocalConnection");

    public List<NormaCoordenacaoDto> ListarNormas()
    {
        using var conn = new SqlConnection(_cs);
        conn.Open();
        var normas = new Dictionary<int, NormaCoordenacaoDto>();
        using (var cmd = new SqlCommand(@"
            SELECT n.NormaId, n.Nome, n.Descricao, n.Origem, n.Ativa, n.Data_Criacao, n.Data_Atualizacao,
                   (SELECT COUNT(*) FROM coordenacao.ProjetoNorma pn WHERE pn.NormaId = n.NormaId) AS ProjetosVinculados
            FROM coordenacao.Norma n ORDER BY n.Nome", conn))
        using (var reader = cmd.ExecuteReader())
        {
            while (reader.Read())
            {
                var dto = new NormaCoordenacaoDto
                {
                    NormaId = reader.GetInt32(0),
                    Nome = reader.GetString(1),
                    Descricao = reader.IsDBNull(2) ? null : reader.GetString(2),
                    Origem = reader.GetString(3),
                    Ativa = reader.GetBoolean(4),
                    DataCriacao = reader.GetDateTime(5),
                    DataAtualizacao = reader.GetDateTime(6),
                    ProjetosVinculados = reader.GetInt32(7),
                };
                normas[dto.NormaId] = dto;
            }
        }
        using (var cmd = new SqlCommand("SELECT NormaMapeamentoId, NormaId, Sigla, Disciplina FROM coordenacao.NormaMapeamento ORDER BY Sigla", conn))
        using (var reader = cmd.ExecuteReader())
        {
            while (reader.Read())
            {
                var normaId = reader.GetInt32(1);
                if (!normas.TryGetValue(normaId, out var norma)) continue;
                norma.Mapeamentos.Add(new() { NormaMapeamentoId = reader.GetInt32(0), Sigla = reader.GetString(2), Disciplina = reader.GetString(3) });
            }
        }
        return normas.Values.ToList();
    }

    public NormaCoordenacaoDto? ObterNorma(int normaId) => ListarNormas().FirstOrDefault(n => n.NormaId == normaId);

    public int CriarNorma(string nome, string? descricao, string origem)
    {
        if (!CoordinationOrigens.EhValida(origem)) throw new ArgumentException("Origem deve ser 'N&A' ou 'CLIENTE'.");
        using var conn = new SqlConnection(_cs);
        conn.Open();
        using var cmd = new SqlCommand(
            "INSERT INTO coordenacao.Norma (Nome, Descricao, Origem) OUTPUT INSERTED.NormaId VALUES (@nome, @desc, @origem)", conn);
        cmd.Parameters.Add("@nome", SqlDbType.NVarChar, 200).Value = nome;
        cmd.Parameters.Add("@desc", SqlDbType.NVarChar, 2000).Value = (object?)descricao ?? DBNull.Value;
        cmd.Parameters.Add("@origem", SqlDbType.NVarChar, 20).Value = origem;
        return (int)cmd.ExecuteScalar();
    }

    public void AtualizarNorma(int normaId, string nome, string? descricao, string origem, bool ativa)
    {
        if (!CoordinationOrigens.EhValida(origem)) throw new ArgumentException("Origem deve ser 'N&A' ou 'CLIENTE'.");
        using var conn = new SqlConnection(_cs);
        conn.Open();
        using var cmd = new SqlCommand(@"UPDATE coordenacao.Norma
            SET Nome=@nome, Descricao=@desc, Origem=@origem, Ativa=@ativa, Data_Atualizacao=SYSUTCDATETIME()
            WHERE NormaId=@id", conn);
        cmd.Parameters.Add("@nome", SqlDbType.NVarChar, 200).Value = nome;
        cmd.Parameters.Add("@desc", SqlDbType.NVarChar, 2000).Value = (object?)descricao ?? DBNull.Value;
        cmd.Parameters.Add("@origem", SqlDbType.NVarChar, 20).Value = origem;
        cmd.Parameters.Add("@ativa", SqlDbType.Bit).Value = ativa;
        cmd.Parameters.Add("@id", SqlDbType.Int).Value = normaId;
        if (cmd.ExecuteNonQuery() == 0) throw new InvalidOperationException($"Norma {normaId} não encontrada.");
    }

    public void ExcluirNorma(int normaId)
    {
        using var conn = new SqlConnection(_cs);
        conn.Open();
        using var check = new SqlCommand("SELECT COUNT(*) FROM coordenacao.ProjetoNorma WHERE NormaId=@id", conn);
        check.Parameters.Add("@id", SqlDbType.Int).Value = normaId;
        var emUso = (int)check.ExecuteScalar();
        if (emUso > 0) throw new InvalidOperationException($"Norma em uso por {emUso} projeto(s) — desvincule antes de excluir.");
        using var cmd = new SqlCommand("DELETE FROM coordenacao.Norma WHERE NormaId=@id", conn);
        cmd.Parameters.Add("@id", SqlDbType.Int).Value = normaId;
        if (cmd.ExecuteNonQuery() == 0) throw new InvalidOperationException($"Norma {normaId} não encontrada.");
    }

    private static string NormalizeSigla(string sigla) => (sigla ?? "").Trim().ToUpperInvariant();

    public int AdicionarMapeamento(int normaId, string sigla, string disciplina)
    {
        var siglaNormalizada = NormalizeSigla(sigla);
        if (siglaNormalizada == "") throw new ArgumentException("Sigla não pode ser vazia.");
        if (string.IsNullOrWhiteSpace(disciplina)) throw new ArgumentException("Disciplina não pode ser vazia.");
        using var conn = new SqlConnection(_cs);
        conn.Open();
        using var check = new SqlCommand("SELECT COUNT(*) FROM coordenacao.NormaMapeamento WHERE NormaId=@normaId AND Sigla=@sigla", conn);
        check.Parameters.Add("@normaId", SqlDbType.Int).Value = normaId;
        check.Parameters.Add("@sigla", SqlDbType.NVarChar, 50).Value = siglaNormalizada;
        if ((int)check.ExecuteScalar() > 0) throw new InvalidOperationException($"Sigla '{siglaNormalizada}' já cadastrada nesta Norma.");
        using var cmd = new SqlCommand(
            "INSERT INTO coordenacao.NormaMapeamento (NormaId, Sigla, Disciplina) OUTPUT INSERTED.NormaMapeamentoId VALUES (@normaId, @sigla, @disc)", conn);
        cmd.Parameters.Add("@normaId", SqlDbType.Int).Value = normaId;
        cmd.Parameters.Add("@sigla", SqlDbType.NVarChar, 50).Value = siglaNormalizada;
        cmd.Parameters.Add("@disc", SqlDbType.NVarChar, 300).Value = disciplina.Trim();
        return (int)cmd.ExecuteScalar();
    }

    public void AtualizarMapeamento(int normaMapeamentoId, string sigla, string disciplina)
    {
        var siglaNormalizada = NormalizeSigla(sigla);
        if (siglaNormalizada == "") throw new ArgumentException("Sigla não pode ser vazia.");
        if (string.IsNullOrWhiteSpace(disciplina)) throw new ArgumentException("Disciplina não pode ser vazia.");
        using var conn = new SqlConnection(_cs);
        conn.Open();
        using var dup = new SqlCommand(@"SELECT COUNT(*) FROM coordenacao.NormaMapeamento
            WHERE Sigla=@sigla AND NormaMapeamentoId<>@id
              AND NormaId = (SELECT NormaId FROM coordenacao.NormaMapeamento WHERE NormaMapeamentoId=@id)", conn);
        dup.Parameters.Add("@sigla", SqlDbType.NVarChar, 50).Value = siglaNormalizada;
        dup.Parameters.Add("@id", SqlDbType.Int).Value = normaMapeamentoId;
        if ((int)dup.ExecuteScalar() > 0) throw new InvalidOperationException($"Sigla '{siglaNormalizada}' já cadastrada nesta Norma.");
        using var cmd = new SqlCommand("UPDATE coordenacao.NormaMapeamento SET Sigla=@sigla, Disciplina=@disc WHERE NormaMapeamentoId=@id", conn);
        cmd.Parameters.Add("@sigla", SqlDbType.NVarChar, 50).Value = siglaNormalizada;
        cmd.Parameters.Add("@disc", SqlDbType.NVarChar, 300).Value = disciplina.Trim();
        cmd.Parameters.Add("@id", SqlDbType.Int).Value = normaMapeamentoId;
        if (cmd.ExecuteNonQuery() == 0) throw new InvalidOperationException($"Mapeamento {normaMapeamentoId} não encontrado.");
    }

    public void RemoverMapeamento(int normaMapeamentoId)
    {
        using var conn = new SqlConnection(_cs);
        conn.Open();
        using var cmd = new SqlCommand("DELETE FROM coordenacao.NormaMapeamento WHERE NormaMapeamentoId=@id", conn);
        cmd.Parameters.Add("@id", SqlDbType.Int).Value = normaMapeamentoId;
        if (cmd.ExecuteNonQuery() == 0) throw new InvalidOperationException($"Mapeamento {normaMapeamentoId} não encontrado.");
    }

    /// <summary>Importação Excel — upsert por Sigla já normalizada (reimportar corrige, nunca duplica).
    /// Chamador (controller) já validou vazio/duplicata-na-mesma-planilha antes de chegar aqui.</summary>
    public void ImportarMapeamentos(int normaId, IReadOnlyList<(string Sigla, string Disciplina)> linhas)
    {
        using var conn = new SqlConnection(_cs);
        conn.Open();
        using var tx = conn.BeginTransaction();
        try
        {
            foreach (var (siglaRaw, disciplinaRaw) in linhas)
            {
                var sigla = NormalizeSigla(siglaRaw);
                var disciplina = disciplinaRaw.Trim();
                using var upd = new SqlCommand("UPDATE coordenacao.NormaMapeamento SET Disciplina=@disc WHERE NormaId=@normaId AND Sigla=@sigla", conn, tx);
                upd.Parameters.Add("@disc", SqlDbType.NVarChar, 300).Value = disciplina;
                upd.Parameters.Add("@normaId", SqlDbType.Int).Value = normaId;
                upd.Parameters.Add("@sigla", SqlDbType.NVarChar, 50).Value = sigla;
                if (upd.ExecuteNonQuery() > 0) continue;
                using var ins = new SqlCommand("INSERT INTO coordenacao.NormaMapeamento (NormaId, Sigla, Disciplina) VALUES (@normaId, @sigla, @disc)", conn, tx);
                ins.Parameters.Add("@normaId", SqlDbType.Int).Value = normaId;
                ins.Parameters.Add("@sigla", SqlDbType.NVarChar, 50).Value = sigla;
                ins.Parameters.Add("@disc", SqlDbType.NVarChar, 300).Value = disciplina;
                ins.ExecuteNonQuery();
            }
            tx.Commit();
        }
        catch { tx.Rollback(); throw; }
    }

    public int? ObterNormaIdDoProjeto(string projectId)
    {
        using var conn = new SqlConnection(_cs);
        conn.Open();
        using var cmd = new SqlCommand("SELECT NormaId FROM coordenacao.ProjetoNorma WHERE Project_ID_ACC=@p", conn);
        cmd.Parameters.Add("@p", SqlDbType.NVarChar, 255).Value = projectId;
        var result = cmd.ExecuteScalar();
        return result == null ? null : (int)result;
    }

    public void DefinirNormaDoProjeto(string projectId, int normaId)
    {
        using var conn = new SqlConnection(_cs);
        conn.Open();
        using var cmd = new SqlCommand(@"
            UPDATE coordenacao.ProjetoNorma SET NormaId=@normaId, Data_Atualizacao=SYSUTCDATETIME() WHERE Project_ID_ACC=@p;
            IF @@ROWCOUNT = 0 INSERT INTO coordenacao.ProjetoNorma (Project_ID_ACC, NormaId) VALUES (@p, @normaId);", conn);
        cmd.Parameters.Add("@normaId", SqlDbType.Int).Value = normaId;
        cmd.Parameters.Add("@p", SqlDbType.NVarChar, 255).Value = projectId;
        cmd.ExecuteNonQuery();
    }

    public void RemoverNormaDoProjeto(string projectId)
    {
        using var conn = new SqlConnection(_cs);
        conn.Open();
        using var cmd = new SqlCommand("DELETE FROM coordenacao.ProjetoNorma WHERE Project_ID_ACC=@p", conn);
        cmd.Parameters.Add("@p", SqlDbType.NVarChar, 255).Value = projectId;
        cmd.ExecuteNonQuery();
    }

    /// <summary>Mapa Sigla(normalizada)->Disciplina da Norma ATIVA vinculada ao projeto. Projeto sem
    /// Norma vinculada (ou Norma inativa) devolve dicionário vazio — todo lançamento cai em SEM
    /// DISCIPLINA, nunca lança exceção (nunca quebra o relatório).</summary>
    public (int? NormaId, Dictionary<string, string> Mapeamentos) ObterMapaDoProjeto(string projectId)
    {
        using var conn = new SqlConnection(_cs);
        conn.Open();
        using var cmd = new SqlCommand(@"
            SELECT n.NormaId, m.Sigla, m.Disciplina
            FROM coordenacao.ProjetoNorma pn
            JOIN coordenacao.Norma n ON n.NormaId = pn.NormaId AND n.Ativa = 1
            LEFT JOIN coordenacao.NormaMapeamento m ON m.NormaId = n.NormaId
            WHERE pn.Project_ID_ACC = @p", conn);
        cmd.Parameters.Add("@p", SqlDbType.NVarChar, 255).Value = projectId;
        using var reader = cmd.ExecuteReader();
        int? normaId = null;
        var mapa = new Dictionary<string, string>();
        while (reader.Read())
        {
            normaId = reader.GetInt32(0);
            if (!reader.IsDBNull(1)) mapa[CoordinationDisciplineResolver.Normalize(reader.GetString(1))] = reader.GetString(2);
        }
        return (normaId, mapa);
    }
}
