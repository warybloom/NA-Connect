using System;
using System.Collections.Generic;

namespace APS.WebApp.Models
{
    /// <summary>
    /// Resultado estrutural da leitura do cronograma Tipo 2 (MPP). Toda tarefa lida vira exatamente um
    /// item em Registros — TarefasAnalisadas deve sempre ser igual a Registros.Count.
    /// </summary>
    public sealed class CronogramaTipo2Resultado
    {
        public string ProjectIdAcc { get; set; }
        public string NomeArquivo { get; set; }
        public DateTime DataSincronizacao { get; set; }

        public int TarefasAnalisadas { get; set; }
        public int TarefasPersistiveis { get; set; }
        public int TarefasResumo { get; set; }
        public int TarefasSemNumeroNa { get; set; }
        public int TarefasSemFase { get; set; }
        public int TarefasComFaseInvalida { get; set; }
        public int RegistrosValidos { get; set; }
        public int RegistrosDuplicados { get; set; }

        public int MatchesEncontrados { get; set; }
        public int MatchesNaoEncontrados { get; set; }
        public int GatesInativos { get; set; }
        public int MatchesAmbiguos { get; set; }

        public List<CronogramaTipo2Registro> Registros { get; set; } = new List<CronogramaTipo2Registro>();

        public List<string> Avisos { get; set; } = new List<string>();
        public List<string> Erros { get; set; } = new List<string>();
    }
}
