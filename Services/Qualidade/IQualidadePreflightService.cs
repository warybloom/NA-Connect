using APS.WebApp.Models;
using OfficeOpenXml;

namespace APS.WebApp.Services.Qualidade
{
    /// <summary>
    /// Prepara UMA tabela de Qualidade por vez (seção 11 do pedido — cada aba é um pipeline
    /// independente): localiza a aba, resolve o cabeçalho, mapeia colunas por nome e monta o DataTable
    /// já com o preenchimento automático de campos textuais obrigatórios aplicado. Nunca abre conexão
    /// SQL — só lê o Excel. O chamador só persiste quando <see cref="ResultadoTabelaQualidade.Sucesso"/>
    /// for true E <see cref="ResultadoTabelaQualidade.Tabela"/> tiver linhas.
    /// </summary>
    public interface IQualidadePreflightService
    {
        ResultadoTabelaQualidade PrepararTabela(ExcelPackage package, QualidadeTabelaDefinicao definicao, string projectId);
    }
}
