using System;

namespace APS.WebApp.Services.Planning
{
    /// <summary>
    /// Erro controlado de layout de uma planilha de Planning Control (aba obrigatória ausente/ambígua,
    /// cabeçalho não encontrado/ambíguo, arquivo com assinatura esperada não encontrada — seções 4/7/12/13
    /// do pedido). <see cref="CodigoResultado"/> carrega o código técnico estável usado tanto no log
    /// quanto em ResultadoPreflightPlanejamento.CodigoResultado — nunca escolhido silenciosamente.
    /// </summary>
    public sealed class PlanningLayoutException : Exception
    {
        public string CodigoResultado { get; }

        public PlanningLayoutException(string codigoResultado, string mensagem)
            : base(mensagem)
        {
            CodigoResultado = codigoResultado;
        }
    }
}
