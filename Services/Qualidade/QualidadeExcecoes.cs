using System;
using System.Collections.Generic;

namespace APS.WebApp.Services.Qualidade
{
    /// <summary>
    /// Erro controlado de layout do arquivo de Qualidade (aba obrigatória ausente, cabeçalho não
    /// encontrado ou ambíguo — seções 7/8 do pedido). <see cref="CodigoResultado"/> carrega o código
    /// técnico estável (ARQUIVO_QUALIDADE_ABA_OBRIGATORIA_AUSENTE / CABECALHO_NAO_ENCONTRADO /
    /// CABECALHO_AMBIGUO) usado tanto no log quanto no ResultadoTabelaQualidade.CodigoResultado
    /// — nunca escolhido silenciosamente, sempre com a lista completa de motivos.
    /// </summary>
    public sealed class QualidadeLayoutException : Exception
    {
        public string CodigoResultado { get; }
        public IReadOnlyList<string> Detalhes { get; }

        public QualidadeLayoutException(string codigoResultado, string mensagem)
            : base(mensagem)
        {
            CodigoResultado = codigoResultado;
            Detalhes = new[] { mensagem };
        }

        public QualidadeLayoutException(string codigoResultado, IReadOnlyList<string> detalhes)
            : base(string.Join(" | ", detalhes))
        {
            CodigoResultado = codigoResultado;
            Detalhes = detalhes;
        }
    }
}
