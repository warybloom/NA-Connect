using System.Threading;
using System.Threading.Tasks;
using APS.WebApp.Models;

namespace APS.WebApp.Services.Qualidade
{
    /// <summary>
    /// Persiste UMA tabela de Qualidade por vez, em sua PRÓPRIA SqlConnection/SqlTransaction (seção 12 do
    /// pedido — "não utilizar uma única transação global para as três tabelas"). Uma falha estrutural ou
    /// de SQL numa tabela nunca desfaz outra que já teve COMMIT. Quando não há linhas válidas, nunca
    /// executa DELETE — preserva o snapshot anterior.
    /// </summary>
    public interface IQualidadeRepository
    {
        Task<ResultadoTabelaQualidade> PersistirTabelaAsync(
            string projectId,
            ResultadoTabelaQualidade preparado,
            CancellationToken cancellationToken = default);
    }
}
