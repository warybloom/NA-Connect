using System;
using System.Collections.Generic;

namespace APS.WebApp.Models
{
    /// <summary>
    /// Resultado da persistência transacional dos registros do cronograma Tipo 2 em
    /// dbo.Cronograma_Projetos. Sucesso só é true depois do COMMIT — qualquer falha reverte a
    /// transação inteira (nenhum registro parcial desta execução permanece).
    /// </summary>
    public sealed class CronogramaTipo2PersistenceResult
    {
        public bool Sucesso { get; set; }

        public DateTime DataSincronizacao { get; set; }

        public int RegistrosRecebidos { get; set; }

        public int RegistrosInseridos { get; set; }

        public List<string> Avisos { get; set; } = new List<string>();

        public List<string> Erros { get; set; } = new List<string>();
    }
}
