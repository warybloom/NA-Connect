namespace APS.WebApp.Models
{
    /// <summary>
    /// Representa uma linha de dbo.Projetos para uso pela infraestrutura de roteamento Tipo 1/Tipo 2
    /// (Services/LdpRouting). Não existia, antes desta fase, um modelo fortemente tipado para
    /// dbo.Projetos no projeto — o fluxo atual (GrdAutomationWorker.ProcessarCicloCompleto) itera
    /// diretamente sobre o DataTable retornado por DatabaseService.GetProjetosAtivos(), lendo cada
    /// coluna por nome (DataRow["Nome_Projeto"], DataRow["Project_ID_ACC"], DataRow["Account_ID_ACC"]).
    /// Esta classe é nova e isolada, criada especificamente para a nova infraestrutura de roteamento —
    /// GetProjetosAtivos() e o loop do Worker continuam exatamente como estão, sem nenhuma alteração.
    /// </summary>
    public sealed class Projeto
    {
        public string ProjectIdAcc { get; set; }
        public string NomeProjeto { get; set; }
        public string AccountIdAcc { get; set; }

        public TipoLdp TipoLdp { get; set; } = TipoLdp.Tipo1;
    }
}
