using System.Data;
using Microsoft.Data.SqlClient;

namespace APS.WebApp.Services.Tv;

/// <summary>
/// Projeto elegível para as Telas de TV: projeto N&A que JÁ FOI SINCRONIZADO pelo robô (tem LDP gravada em
/// dbo.Ldp_Unificada com Data_Sincronizacao). As telas da TV são dashboards SQL sobre esses dados — projeto nunca
/// sincronizado não tem o que exibir. Não usa Autodesk, hubId nem UserProjects (configuração é administrativa).
/// Nomes no mesmo formato de /api/me/projects (o frontend reutiliza o mesmo conversor).
/// </summary>
public sealed record TvProjetoElegivel(int ProjetoId, string ProjectIdAcc, string NomeProjeto, byte? TipoLdp, bool IsAtivo);

public interface ITvProjetosElegiveisRepository
{
    Task<IReadOnlyList<TvProjetoElegivel>> ListarAsync(CancellationToken ct);
}

public sealed class SqlTvProjetosElegiveisRepository(IConfiguration configuration) : ITvProjetosElegiveisRepository
{
    public const string Sql = """
        SELECT p.ProjetoID, p.Project_ID_ACC, COALESCE(p.Nome_Projeto, N''), p.Tipo_LDP, p.Is_Ativo
        FROM dbo.Projetos p
        WHERE p.Project_ID_ACC IS NOT NULL
          AND EXISTS (SELECT 1 FROM dbo.Ldp_Unificada l
                      WHERE l.Project_ID_ACC = p.Project_ID_ACC AND l.Data_Sincronizacao IS NOT NULL)
        ORDER BY p.Nome_Projeto;
        """;

    public async Task<IReadOnlyList<TvProjetoElegivel>> ListarAsync(CancellationToken ct)
    {
        await using var c = new SqlConnection(configuration.GetConnectionString("LocalConnection")
            ?? throw new InvalidOperationException("Conexão indisponível."));
        await c.OpenAsync(ct);
        using var cmd = new SqlCommand(Sql, c) { CommandTimeout = 30 };
        await using var r = await cmd.ExecuteReaderAsync(ct);
        var lista = new List<TvProjetoElegivel>();
        while (await r.ReadAsync(ct))
            lista.Add(new TvProjetoElegivel(r.GetInt32(0), r.GetString(1), r.GetString(2),
                r.IsDBNull(3) ? null : r.GetByte(3), !r.IsDBNull(4) && r.GetBoolean(4)));
        return lista;
    }
}
