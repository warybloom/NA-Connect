using System.Collections.Generic;

namespace APS.WebApp.Models
{
    /// <summary>
    /// Resultado estrutural do processamento da LDP Tipo 2. Nesta fase, ILdpTipo2Service.ProcessarAsync
    /// devolve este resultado já preenchido com Erros indicando que o mapeamento final das colunas
    /// ainda não foi configurado (ver Services/LdpTipo2/LdpTipo2Service.cs).
    /// </summary>
    public sealed class LdpTipo2Resultado
    {
        public string ProjectIdAcc { get; set; }

        public string NomeArquivo { get; set; }

        public int LinhaCabecalho { get; set; }

        public int LinhasAnalisadas { get; set; }

        public int DocumentosEncontrados { get; set; }

        public int GatesAtivos { get; set; }

        public int GatesInativos { get; set; }

        public List<LdpTipo2Documento> Documentos { get; set; } = new List<LdpTipo2Documento>();

        public List<string> Avisos { get; set; } = new List<string>();

        public List<string> Erros { get; set; } = new List<string>();
    }
}
