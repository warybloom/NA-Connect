namespace APS.WebApp.Services.GestaoVista
{
    /// <summary>
    /// Fórmula compartilhada entre Custo Real e Custo profissionais (a mesma fórmula literal encontrada
    /// nas duas abas — ver AUDITORIA_FORGESYNC_NA/MAPA_DADOS_LDP_FINANCEIRO.md seções 2 e 3): Custo/hora
    /// = (Remuneração + Encargos + Softwares + Computadores) / 176. SEMPRE recalculado em C#, nunca lido
    /// da fórmula da célula (a planilha tolera linhas incompletas/fórmulas referenciando célula vazia).
    /// </summary>
    public static class GestaoVistaCustoCalculo
    {
        private const decimal HorasMesReferencia = 176m;

        /// <summary>
        /// total/custoHora ficam null quando NENHUM dos 4 componentes está preenchido (linha vazia/
        /// incompleta — nunca persistida como custo real/previsto). Quando pelo menos um componente
        /// existe, os ausentes contam como 0 na soma — mesma avaliação que o Excel faz para célula vazia
        /// em uma fórmula de soma (nunca "0 forçado" nos componentes individuais, só no total agregado).
        /// </summary>
        public static void CalcularTotalECustoHora(
            decimal? remuneracao, decimal? encargos, decimal? softwares, decimal? computadores,
            out decimal? total, out decimal? custoHora)
        {
            bool algumPreenchido = remuneracao.HasValue || encargos.HasValue || softwares.HasValue || computadores.HasValue;

            if (!algumPreenchido)
            {
                total = null;
                custoHora = null;
                return;
            }

            total = (remuneracao ?? 0m) + (encargos ?? 0m) + (softwares ?? 0m) + (computadores ?? 0m);
            custoHora = total.Value / HorasMesReferencia;
        }
    }
}
