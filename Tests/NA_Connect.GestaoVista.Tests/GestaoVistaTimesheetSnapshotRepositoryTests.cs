using APS.WebApp.Services.GestaoVista;

namespace NA_Connect.GestaoVista.Tests
{
    /// <summary>Regra pura de validação da ETAPA 03 (seção "VALIDAÇÃO OBRIGATÓRIA" do pedido):
    /// ABS(hhExcel - hhBanco) &lt;= 0.01 — tolerância só de arredondamento decimal.</summary>
    public class GestaoVistaTimesheetSnapshotRepositoryTests
    {
        [Theory]
        [InlineData(5447.68, 5447.68, true)]
        [InlineData(5447.68, 5447.681, true)]   // 0.001 de diferença — dentro da tolerância
        [InlineData(5447.68, 5447.69, true)]    // exatamente 0.01 — na borda, ainda aceito
        [InlineData(5447.68, 5447.70, false)]   // 0.02 — acima da tolerância
        [InlineData(4737.52, 5447.68, false)]   // divergência real observada em produção
        public void DentroDaTolerancia_ComparaComTolerancia001(decimal hhExcel, decimal hhBanco, bool esperado)
        {
            Assert.Equal(esperado, GestaoVistaTimesheetSnapshotRepository.DentroDaTolerancia(hhExcel, hhBanco));
        }
    }
}
