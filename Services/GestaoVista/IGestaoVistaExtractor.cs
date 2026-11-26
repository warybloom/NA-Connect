using System.IO;
using APS.WebApp.Models;

namespace APS.WebApp.Services.GestaoVista
{
    public interface IGestaoVistaExtractor
    {
        /// <summary>Leitura pura do workbook (nunca toca banco). Nunca lança exceção para o chamador —
        /// falhas de leitura (workbook ilegível) viram GestaoVistaExtracaoResultado.Erros.</summary>
        GestaoVistaExtracaoResultado Processar(Stream workbook, string projectIdAcc, string nomeArquivo);
    }
}
