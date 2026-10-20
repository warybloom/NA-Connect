using System;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using APS.WebApp.Models;

namespace APS.WebApp.Services.GestaoVista
{
    /// <summary>
    /// Hash estável (SHA-256, 32 bytes — bate com BINARY(32) das 3 tabelas) para idempotência de
    /// importação, sem depender de um Id nativo da planilha (que não existe). Cada chamador decide quais
    /// campos entram no hash (documentado no chamador) — este helper só concatena de forma determinística
    /// e formata cada tipo com cultura fixa (nunca depende da cultura da máquina).
    /// </summary>
    public static class GestaoVistaRowHasher
    {
        /// <summary>Separador de controle (Unit Separator) — praticamente impossível de aparecer em
        /// texto real vindo de planilha, evita colisão entre "A" + "BC" e "AB" + "C".</summary>
        private const char Separador = '';

        public static byte[] Calcular(params string[] campos)
        {
            string chave = string.Join(Separador, campos.Select(c => c ?? string.Empty));
            byte[] bytes = Encoding.UTF8.GetBytes(chave);
            return SHA256.HashData(bytes);
        }

        public static string F(string valor) => valor?.Trim() ?? string.Empty;

        public static string F(decimal? valor) => valor.HasValue
            ? valor.Value.ToString("0.######", CultureInfo.InvariantCulture)
            : string.Empty;

        public static string F(DateTime? valor) => valor.HasValue
            ? valor.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
            : string.Empty;

        /// <summary>Hash dos campos de VALOR de Custo Real (seção 6 do modelo de dados) — deliberadamente
        /// SEM Project_ID_ACC/NomeProfissional (identidade), já que a decisão "mesma pessoa/projeto" é
        /// feita separadamente pelo repositório (WHERE), nunca pelo hash.</summary>
        public static byte[] CustoReal(GestaoVistaCustoRealExtraido r) => Calcular(
            F(r.Remuneracao), F(r.Encargos), F(r.Softwares), F(r.Computadores));

        /// <summary>Hash de Custo Previsto: identidade + valor juntos (sem histórico — dedupe de
        /// snapshot idêntico dentro do mesmo projeto).</summary>
        public static byte[] CustoPrevisto(GestaoVistaCustoPrevistoExtraido r) => Calcular(
            F(r.Funcao), F(r.GrupoFuncao), F(r.Disciplina), F(r.Recurso),
            F(r.Remuneracao), F(r.Encargos), F(r.Softwares), F(r.Computadores));

        /// <summary>Hash de Timesheet (seção 9 do pedido): identidade do lançamento + seus valores —
        /// nunca só Nome+Documento. Inclui CustoInformadoNaPlanilha (evidência congelada da célula CUSTO)
        /// para que uma célula CUSTO corrigida entre sincronizações não colapse silenciosamente no mesmo
        /// lançamento antigo. Nunca inclui CustoCalculado — esse é recalculado pelo sistema a cada
        /// sincronização, não faz parte da identidade do lançamento importado. Limitação documentada:
        /// duas linhas com valores IDÊNTICOS em todos estes campos colapsam em um único lançamento (sem Id
        /// nativo da planilha para diferenciá-las).</summary>
        public static byte[] Timesheet(GestaoVistaTimesheetLancamentoExtraido r) => Calcular(
            F(r.NomeProfissional), F(r.HorasPlanejadas), F(r.HorasTrabalhadas),
            F(r.Projeto), F(r.Funcao), F(r.Disciplina),
            F(r.DocumentoCodigo), F(r.Documento), F(r.Descricao),
            F(r.Doc1), F(r.Doc2), F(r.Doc3), F(r.Doc4), F(r.Doc5),
            F(r.DataLancamento), F(r.CustoInformadoNaPlanilha));
    }
}
