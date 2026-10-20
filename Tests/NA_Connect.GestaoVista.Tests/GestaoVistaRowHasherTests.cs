using APS.WebApp.Models;
using APS.WebApp.Services.GestaoVista;

namespace NA_Connect.GestaoVista.Tests
{
    /// <summary>
    /// RowHash determinístico e sensível a mudança de valor — a base de toda a idempotência/histórico
    /// SCD2 da Gestão à Vista (seção 9/16 do pedido). Nunca toca banco.
    /// </summary>
    public class GestaoVistaRowHasherTests
    {
        [Fact]
        public void CustoReal_MesmosValores_GeraHashIdentico()
        {
            var r1 = new GestaoVistaCustoRealExtraido { Remuneracao = 1000m, Encargos = 60m, Softwares = 50m, Computadores = 20m };
            var r2 = new GestaoVistaCustoRealExtraido { Remuneracao = 1000m, Encargos = 60m, Softwares = 50m, Computadores = 20m };

            Assert.Equal(GestaoVistaRowHasher.CustoReal(r1), GestaoVistaRowHasher.CustoReal(r2));
        }

        [Fact]
        public void CustoReal_MudancaDeRemuneracao_GeraHashDiferente()
        {
            var r1 = new GestaoVistaCustoRealExtraido { Remuneracao = 1000m, Encargos = 60m, Softwares = 50m, Computadores = 20m };
            var r2 = new GestaoVistaCustoRealExtraido { Remuneracao = 1500m, Encargos = 60m, Softwares = 50m, Computadores = 20m };

            Assert.NotEqual(GestaoVistaRowHasher.CustoReal(r1), GestaoVistaRowHasher.CustoReal(r2));
        }

        [Fact]
        public void CustoReal_Hash_NaoDependeDeNomeOuProjeto_IdentidadeEhResponsabilidadeDoRepositorio()
        {
            // Duas pessoas diferentes, mesmo custo: o hash sozinho NÃO as diferencia — a identidade
            // (Project_ID_ACC + NomeProfissional) é sempre resolvida pela query do repositório (WHERE),
            // nunca pelo hash. Documentado explicitamente para não ser "corrigido" por engano depois.
            var pessoaA = new GestaoVistaCustoRealExtraido { NomeProfissional = "Ana", Remuneracao = 1000m, Encargos = 60m };
            var pessoaB = new GestaoVistaCustoRealExtraido { NomeProfissional = "Bruno", Remuneracao = 1000m, Encargos = 60m };

            Assert.Equal(GestaoVistaRowHasher.CustoReal(pessoaA), GestaoVistaRowHasher.CustoReal(pessoaB));
        }

        [Fact]
        public void CustoPrevisto_IdentidadeEValorEntramNoHash()
        {
            var r1 = new GestaoVistaCustoPrevistoExtraido { Funcao = "Engenheiro", Disciplina = "Elétrica", CustoHora = 10m, Remuneracao = 1000m };
            var r2 = new GestaoVistaCustoPrevistoExtraido { Funcao = "Engenheiro", Disciplina = "Civil", CustoHora = 10m, Remuneracao = 1000m };

            Assert.NotEqual(GestaoVistaRowHasher.CustoPrevisto(r1), GestaoVistaRowHasher.CustoPrevisto(r2));
        }

        [Fact]
        public void Timesheet_NomeEDocumentoIguaisComHorasDiferentes_GeraHashDiferente()
        {
            // Prova a exigência da seção 9: hash NUNCA pode ser só Nome+Documento, porque dois
            // lançamentos legítimos diferentes (horas diferentes) para a mesma pessoa/documento existem.
            var r1 = new GestaoVistaTimesheetLancamentoExtraido { NomeProfissional = "Ana", DocumentoCodigo = "DOC-1", HorasTrabalhadas = 4m };
            var r2 = new GestaoVistaTimesheetLancamentoExtraido { NomeProfissional = "Ana", DocumentoCodigo = "DOC-1", HorasTrabalhadas = 8m };

            Assert.NotEqual(GestaoVistaRowHasher.Timesheet(r1), GestaoVistaRowHasher.Timesheet(r2));
        }

        [Fact]
        public void Timesheet_DataLancamentoDiferente_GeraHashDiferente()
        {
            var r1 = new GestaoVistaTimesheetLancamentoExtraido { NomeProfissional = "Ana", DocumentoCodigo = "DOC-1", HorasTrabalhadas = 4m, DataLancamento = new System.DateTime(2026, 1, 10) };
            var r2 = new GestaoVistaTimesheetLancamentoExtraido { NomeProfissional = "Ana", DocumentoCodigo = "DOC-1", HorasTrabalhadas = 4m, DataLancamento = new System.DateTime(2026, 1, 11) };

            Assert.NotEqual(GestaoVistaRowHasher.Timesheet(r1), GestaoVistaRowHasher.Timesheet(r2));
        }

        [Fact]
        public void Timesheet_CustoInformadoNaPlanilhaDiferente_GeraHashDiferente()
        {
            // Uma célula CUSTO corrigida na LDP entre sincronizações não pode colapsar no lançamento antigo.
            var r1 = new GestaoVistaTimesheetLancamentoExtraido { NomeProfissional = "Ana", DocumentoCodigo = "DOC-1", HorasTrabalhadas = 4m, CustoInformadoNaPlanilha = 69.14772727m };
            var r2 = new GestaoVistaTimesheetLancamentoExtraido { NomeProfissional = "Ana", DocumentoCodigo = "DOC-1", HorasTrabalhadas = 4m, CustoInformadoNaPlanilha = 70m };

            Assert.NotEqual(GestaoVistaRowHasher.Timesheet(r1), GestaoVistaRowHasher.Timesheet(r2));
        }

        [Fact]
        public void Timesheet_TodosOsCamposIguais_GeraHashIdentico_MesmaInstanciaOuNao()
        {
            var r1 = new GestaoVistaTimesheetLancamentoExtraido
            {
                NomeProfissional = "Ana", HorasPlanejadas = 10m, HorasTrabalhadas = 8m, Projeto = "Proj X",
                Funcao = "Engenheiro", Disciplina = "Elétrica", DocumentoCodigo = "DOC-1", Documento = "Doc 1",
                Descricao = "Desc", Doc1 = "A", Doc2 = "B", DataLancamento = new System.DateTime(2026, 1, 10)
            };
            var r2 = new GestaoVistaTimesheetLancamentoExtraido
            {
                NomeProfissional = "Ana", HorasPlanejadas = 10m, HorasTrabalhadas = 8m, Projeto = "Proj X",
                Funcao = "Engenheiro", Disciplina = "Elétrica", DocumentoCodigo = "DOC-1", Documento = "Doc 1",
                Descricao = "Desc", Doc1 = "A", Doc2 = "B", DataLancamento = new System.DateTime(2026, 1, 10)
            };

            Assert.Equal(GestaoVistaRowHasher.Timesheet(r1), GestaoVistaRowHasher.Timesheet(r2));
        }

        [Fact]
        public void Calcular_Hash_TemSempre32Bytes_CompativelComBinary32DasTabelas()
        {
            byte[] hash = GestaoVistaRowHasher.Calcular("a", "b", "c");
            Assert.Equal(32, hash.Length);
        }
    }
}
