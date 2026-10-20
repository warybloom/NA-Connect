using System;

namespace APS.WebApp.Services.LdpTipo2
{
    /// <summary>
    /// Interpretação isolada da marcação de um gate na planilha Tipo 2. A regra oficial (confirmada)
    /// é: célula com "X" (ignorando caixa e espaços nas pontas) -> gate ativo/obrigatório; célula
    /// vazia -> gate não aplicável. Nenhuma outra grafia ("SIM", "YES", "1", "TRUE") é aceita — nunca
    /// ampliar essa lista sem confirmação, e nunca ler a planilha de fato nesta fase.
    /// </summary>
    public static class LdpTipo2GateMarkingHelper
    {
        public static bool InterpretarMarcacaoGate(string valor)
        {
            return string.Equals(valor?.Trim(), "X", StringComparison.OrdinalIgnoreCase);
        }
    }
}
