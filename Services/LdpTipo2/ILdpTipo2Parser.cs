using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using APS.WebApp.Models;

namespace APS.WebApp.Services.LdpTipo2
{
    /// <summary>
    /// Leitor da aba "LDP" de um arquivo de LDP Tipo 2. Testável isoladamente, sem Autodesk: recebe um
    /// Stream já em mãos, nunca busca token, nunca acessa o ACC, nunca executa SQL, nunca faz upload e
    /// nunca conhece VersionUrn — essas responsabilidades são do LdpTipo2Service (orquestração).
    /// </summary>
    public interface ILdpTipo2Parser
    {
        Task<LdpTipo2Resultado> ProcessarAsync(
            Stream arquivoExcel,
            string projectIdAcc,
            string nomeArquivo,
            DateTime dataSincronizacao,
            CancellationToken cancellationToken = default);
    }
}
