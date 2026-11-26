using System;
using System.Collections.Generic;

namespace APS.WebApp.Models
{
    /// <summary>
    /// Resultado da persistência transacional de um LdpTipo2Resultado (Fase 3): uma linha principal em
    /// dbo.Ldp_Unificada por documento + exatamente três linhas em dbo.Ldp_Documento_Gates por documento,
    /// tudo dentro de uma única transação por arquivo/projeto. Sucesso só é true depois do COMMIT.
    /// </summary>
    public sealed class LdpTipo2PersistenceResult
    {
        public bool Sucesso { get; set; }

        public DateTime DataSincronizacao { get; set; }

        public int DocumentosRecebidos { get; set; }

        public int DocumentosInseridos { get; set; }

        public int GatesInseridos { get; set; }

        public int GatesAtivosInseridos { get; set; }

        public int GatesInativosInseridos { get; set; }

        public List<string> Avisos { get; set; } = new List<string>();

        public List<string> Erros { get; set; } = new List<string>();
    }
}
