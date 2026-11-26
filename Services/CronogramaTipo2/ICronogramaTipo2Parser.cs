using System;
using System.Threading;
using System.Threading.Tasks;
using APS.WebApp.Models;

namespace APS.WebApp.Services.CronogramaTipo2
{
    /// <summary>
    /// Leitor do cronograma Tipo 2 (.mpp). Testável isoladamente: recebe um caminho de arquivo local já
    /// em mãos (mesmo padrão de net.sf.mpxj.reader.UniversalProjectReader já usado pelo Tipo 1 em
    /// AccScheduleSyncService.LoadMppFile) — nunca busca token, nunca acessa o ACC, nunca executa SQL,
    /// nunca faz upload.
    /// </summary>
    public interface ICronogramaTipo2Parser
    {
        Task<CronogramaTipo2Resultado> ProcessarAsync(
            string caminhoArquivoMpp,
            string projectIdAcc,
            byte tipoLdp,
            string nomeArquivo,
            DateTime dataSincronizacao,
            CancellationToken cancellationToken = default);
    }
}
