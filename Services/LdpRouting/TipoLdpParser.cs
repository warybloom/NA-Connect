using System;
using APS.WebApp.Models;

namespace APS.WebApp.Services.LdpRouting
{
    /// <summary>
    /// Interpretação isolada e pura (sem acesso a banco) de um valor bruto de dbo.Projetos.Tipo_LDP
    /// vindo do SQL (DBNull, número, texto, o que vier). Regras:
    ///   - NULL ou 0 -> Tipo1, sem sinalizar como inválido (coluna recém-criada / projeto ainda não
    ///     configurado é o estado esperado da maioria dos projetos existentes, não um erro);
    ///   - valor numérico que não seja 1 nem 2 (ex.: 9), ou valor não numérico -> Tipo1, sinalizado
    ///     como inválido (valorInvalido = true) para o chamador registrar o warning.
    /// Nunca lança exceção — sempre devolve um TipoLdp utilizável.
    /// </summary>
    public static class TipoLdpParser
    {
        public static TipoLdp Interpretar(object valorBruto, out bool valorInvalido)
        {
            valorInvalido = false;

            if (valorBruto == null || valorBruto == DBNull.Value)
            {
                return TipoLdp.Tipo1;
            }

            if (byte.TryParse(valorBruto.ToString(), out byte valor))
            {
                if (valor == 0)
                {
                    return TipoLdp.Tipo1;
                }

                if (Enum.IsDefined(typeof(TipoLdp), valor))
                {
                    return (TipoLdp)valor;
                }
            }

            valorInvalido = true;
            return TipoLdp.Tipo1;
        }
    }
}
