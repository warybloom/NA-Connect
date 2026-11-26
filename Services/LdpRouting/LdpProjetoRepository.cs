using System;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using APS.WebApp.Models;

namespace APS.WebApp.Services.LdpRouting
{
    /// <summary>
    /// Implementação de ILdpProjetoRepository. Deliberadamente NÃO reaproveita nem altera
    /// DatabaseService.GetProjetosAtivos() — usa a mesma connection string ("LocalConnection"), em
    /// classe própria, com sua própria query.
    ///
    /// A coluna dbo.Projetos.Tipo_LDP é adicionada manualmente FORA deste código, em outra etapa; até
    /// que ela exista de fato no banco, a tentativa de lê-la falha com SqlException (Invalid column
    /// name). Essa falha é tratada explicitamente: refaz a consulta sem Tipo_LDP e devolve Tipo1 —
    /// nenhum projeto existente quebra por causa da coluna ainda não existir.
    /// </summary>
    public sealed class LdpProjetoRepository : ILdpProjetoRepository
    {
        private readonly string _connectionString;
        private readonly ILogger<LdpProjetoRepository> _logger;

        public LdpProjetoRepository(IConfiguration configuration, ILogger<LdpProjetoRepository> logger)
        {
            _connectionString = configuration.GetConnectionString("LocalConnection");
            _logger = logger;
        }

        public async Task<Projeto> ObterProjetoAtivoAsync(string projectIdAcc, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(projectIdAcc))
                throw new ArgumentException("projectIdAcc não pode ser vazio.", nameof(projectIdAcc));

            try
            {
                return await ConsultarComTipoLdpAsync(projectIdAcc, cancellationToken);
            }
            catch (SqlException ex) when (ColunaTipoLdpProvavelmenteAusente(ex))
            {
                _logger.LogWarning(
                    $"[LDP ROUTER] Coluna Tipo_LDP ainda não existe em dbo.Projetos (ProjectId: {projectIdAcc}) — assumindo Tipo1 até que ela seja adicionada.");
                return await ConsultarSemTipoLdpAsync(projectIdAcc, cancellationToken);
            }
        }

        private async Task<Projeto> ConsultarComTipoLdpAsync(string projectIdAcc, CancellationToken cancellationToken)
        {
            const string query = @"
                SELECT P.Project_ID_ACC, P.Nome_Projeto, H.Account_ID_ACC, P.Tipo_LDP
                FROM Projetos P
                JOIN Hubs_Conta H ON P.HubContaID = H.HubContaID
                WHERE P.Project_ID_ACC = @pid AND P.Is_Ativo = 1";

            using (var conn = new SqlConnection(_connectionString))
            using (var cmd = new SqlCommand(query, conn))
            {
                cmd.Parameters.Add("@pid", SqlDbType.NVarChar, 200).Value = projectIdAcc;
                await conn.OpenAsync(cancellationToken);

                using (var reader = await cmd.ExecuteReaderAsync(cancellationToken))
                {
                    if (!await reader.ReadAsync(cancellationToken)) return null;

                    var tipoLdp = TipoLdpParser.Interpretar(reader["Tipo_LDP"], out bool valorInvalido);
                    if (valorInvalido)
                    {
                        _logger.LogWarning(
                            $"[LDP ROUTER] Projeto: {reader["Nome_Projeto"]} | ProjectId: {projectIdAcc} | Tipo_LDP com valor inesperado ('{reader["Tipo_LDP"]}') — usando Tipo1 como fallback.");
                    }

                    return new Projeto
                    {
                        ProjectIdAcc = reader["Project_ID_ACC"] as string,
                        NomeProjeto = reader["Nome_Projeto"] as string,
                        AccountIdAcc = reader["Account_ID_ACC"] as string,
                        TipoLdp = tipoLdp
                    };
                }
            }
        }

        private async Task<Projeto> ConsultarSemTipoLdpAsync(string projectIdAcc, CancellationToken cancellationToken)
        {
            const string query = @"
                SELECT P.Project_ID_ACC, P.Nome_Projeto, H.Account_ID_ACC
                FROM Projetos P
                JOIN Hubs_Conta H ON P.HubContaID = H.HubContaID
                WHERE P.Project_ID_ACC = @pid AND P.Is_Ativo = 1";

            using (var conn = new SqlConnection(_connectionString))
            using (var cmd = new SqlCommand(query, conn))
            {
                cmd.Parameters.Add("@pid", SqlDbType.NVarChar, 200).Value = projectIdAcc;
                await conn.OpenAsync(cancellationToken);

                using (var reader = await cmd.ExecuteReaderAsync(cancellationToken))
                {
                    if (!await reader.ReadAsync(cancellationToken)) return null;

                    return new Projeto
                    {
                        ProjectIdAcc = reader["Project_ID_ACC"] as string,
                        NomeProjeto = reader["Nome_Projeto"] as string,
                        AccountIdAcc = reader["Account_ID_ACC"] as string,
                        TipoLdp = TipoLdp.Tipo1
                    };
                }
            }
        }

        private static bool ColunaTipoLdpProvavelmenteAusente(SqlException ex)
        {
            // 207 = Invalid column name (SQL Server) — só engolimos esse erro específico; qualquer
            // outra falha de SQL continua subindo normalmente.
            foreach (SqlError error in ex.Errors)
            {
                if (error.Number == 207) return true;
            }
            return false;
        }
    }
}
