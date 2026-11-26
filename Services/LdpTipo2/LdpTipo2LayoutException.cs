using System;
using System.Collections.Generic;
using System.Linq;

namespace APS.WebApp.Services.LdpTipo2
{
    /// <summary>
    /// Layout da aba LDP (Tipo 2) inválido/ambíguo — aba não encontrada, cabeçalho obrigatório
    /// ausente ou duplicado, bloco manual de gate não encontrado (ou ambíguo), ou conjunto de
    /// indicadores de aplicabilidade não encontrado (ou ambíguo). Sempre agrega TODOS os problemas
    /// encontrados numa única exceção — nunca interrompe na primeira falha isoladamente, para que o
    /// chamador registre todos os cabeçalhos obrigatórios ausentes numa única mensagem.
    /// </summary>
    public sealed class LdpTipo2LayoutException : Exception
    {
        public IReadOnlyList<string> Problemas { get; }

        public LdpTipo2LayoutException(IReadOnlyList<string> problemas)
            : base("[LDP TIPO 2] Layout inválido: " + string.Join(" | ", problemas ?? Array.Empty<string>()))
        {
            Problemas = problemas ?? Array.Empty<string>();
        }

        public LdpTipo2LayoutException(string problemaUnico)
            : this(new[] { problemaUnico })
        {
        }
    }
}
