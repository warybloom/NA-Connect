using System;
using System.Collections.Generic;

namespace APS.WebApp.Models
{
    /// <summary>
    /// Resultado final (diagnóstico completo, seção 13 do pedido) de UMA sincronização de Gestão à Vista
    /// para um projeto. Nunca lança exceção para o chamador (LdpGestaoVistaSyncService captura tudo
    /// internamente) — o Worker consulta este objeto para decidir o que logar, mas mesmo sem consultá-lo
    /// a sincronização de Gestão à Vista nunca derruba a sincronização operacional da LDP.
    /// </summary>
    public sealed class GestaoVistaSyncResultado
    {
        public string ProjectIdAcc { get; set; }

        public string NomeArquivo { get; set; }

        public DateTime DataSincronizacao { get; set; }

        /// <summary>false = schema [gestao] ou alguma das 3 tabelas não existe neste banco — nenhuma
        /// migration foi executada automaticamente; a extração roda, mas nada é persistido. Ver
        /// GESTAO_VISTA_SCHEMA_NAO_DISPONIVEL.</summary>
        public bool SchemaDisponivel { get; set; } = true;

        public List<GestaoVistaWorksheetDiagnostico> Worksheets { get; set; } = new();

        public GestaoVistaFaseResultado CustoReal { get; set; } = new();

        public GestaoVistaFaseResultado CustoPrevisto { get; set; } = new();

        public GestaoVistaFaseResultado Timesheet { get; set; } = new();

        public bool Sucesso { get; set; }

        public List<string> Avisos { get; set; } = new();

        public List<string> Erros { get; set; } = new();
    }
}
