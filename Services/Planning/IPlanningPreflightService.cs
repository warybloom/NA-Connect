using APS.WebApp.Models;
using OfficeOpenXml;

namespace APS.WebApp.Services.Planning
{
    /// <summary>
    /// Preflight completo (seções 11/19 do pedido) de UMA etapa de Planning Control. Nunca abre conexão
    /// SQL — só lê o Excel e monta o DataTable em memória. O chamador só pode abrir conexão/transação
    /// quando <see cref="ResultadoPreflightPlanejamento.Sucesso"/> for true.
    /// </summary>
    public interface IPlanningPreflightService
    {
        ResultadoPreflightPlanejamento ExecutarPreflight(
            ExcelPackage package,
            PlanningSheetDefinition definicao,
            string projectId,
            string pastaEncontrada,
            string arquivoSelecionado);
    }
}
