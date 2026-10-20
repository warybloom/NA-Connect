using System.Linq;
using APS.WebApp.Services.Planning;

namespace NA_Connect.Planning.Tests
{
    /// <summary>
    /// Confirma que PlanningDefinicoes usa exatamente os nomes de coluna SQL reais das quatro tabelas
    /// (schema informado pelo usuário após o teste com BML011.013 — nunca inventado) e que Id_Registro
    /// nunca é declarado como coluna (é IDENTITY nas quatro tabelas, igual ao código legado e à
    /// QualidadeRepository: nunca mapeado, nunca inserido, gerado pelo SQL).
    /// </summary>
    public class PlanningDefinicoesSchemaTests
    {
        [Fact]
        public void ExternalEInternal_UsamOMesmoSchemaRealDeActionItemList()
        {
            string[] esperado = { "ID", "Date", "Name", "Company", "CYCLE", "FRONT_OF_SERVICES", "AFE", "Description", "Name2", "Company3", "Schedule", "Reschedule", "Concl", "Critical", "Status", "COMMENTS" };

            var external = PlanningDefinicoes.External().Columns.Select(c => c.SqlColumnName).ToArray();
            var interno = PlanningDefinicoes.Internal().Columns.Select(c => c.SqlColumnName).ToArray();

            Assert.Equal(esperado, external);
            Assert.Equal(esperado, interno);
            Assert.Equal("[dbo].[tb_ExternalActionLog]", PlanningDefinicoes.External().DestinationTable);
            Assert.Equal("[dbo].[tb_InternalActionLog]", PlanningDefinicoes.Internal().DestinationTable);
        }

        [Fact]
        public void Scope_UsaOSchemaRealDeTbScopeDeviation()
        {
            string[] esperado = { "ITEM", "DESCRICAO", "DISCIPLINA", "N_DO_DESVIO", "DATA", "QUANT_HH", "STATUS", "DESVIO_INTERNO", "CHANGE_ORDER" };

            var colunas = PlanningDefinicoes.ScopeDeviation().Columns.Select(c => c.SqlColumnName).ToArray();

            Assert.Equal(esperado, colunas);
            Assert.Equal("[dbo].[tb_ScopeDeviation]", PlanningDefinicoes.ScopeDeviation().DestinationTable);
        }

        [Fact]
        public void Hold_UsaOSchemaRealDeTbHoldControl()
        {
            string[] esperado = { "ITEM", "N_HOLD", "N_NA_DOCUMENTO", "DISCIPLINA", "TITULO_DOCUMENTO", "REV", "MOTIVO_HOLD", "STATUS", "RESPONSAVEL" };

            var colunas = PlanningDefinicoes.HoldControl().Columns.Select(c => c.SqlColumnName).ToArray();

            Assert.Equal(esperado, colunas);
            Assert.Equal("[dbo].[tb_HoldControl]", PlanningDefinicoes.HoldControl().DestinationTable);
        }

        [Theory]
        [MemberData(nameof(TodasAsDefinicoes))]
        public void NenhumaDefinicao_DeclaraIdRegistroComoColuna(APS.WebApp.Models.PlanningSheetDefinition definicao)
        {
            Assert.DoesNotContain(definicao.Columns, c => c.SqlColumnName.Equals("Id_Registro", System.StringComparison.OrdinalIgnoreCase));
        }

        [Theory]
        [MemberData(nameof(TodasAsDefinicoes))]
        public void DestinosLegadosContinuamTextoComValidacaoTipada(APS.WebApp.Models.PlanningSheetDefinition definicao)
        {
            Assert.All(definicao.Columns, c => Assert.True(c.DataType == APS.WebApp.Models.PlanningColumnDataType.Texto || c.StoreAsText));
        }

        public static System.Collections.Generic.IEnumerable<object[]> TodasAsDefinicoes()
        {
            yield return new object[] { PlanningDefinicoes.External() };
            yield return new object[] { PlanningDefinicoes.Internal() };
            yield return new object[] { PlanningDefinicoes.ScopeDeviation() };
            yield return new object[] { PlanningDefinicoes.HoldControl() };
        }
    }
}
