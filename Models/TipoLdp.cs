namespace APS.WebApp.Models
{
    /// <summary>
    /// Tipo de estrutura de LDP usada por um projeto (coluna dbo.Projetos.Tipo_LDP, adicionada
    /// manualmente fora deste código). Tipo1 é o padrão e o único tipo com processamento real
    /// implementado; Tipo2 é a nova estrutura com gates de 30%/60%/90%, cuja infraestrutura está
    /// sendo preparada nesta fase (ver Services/LdpTipo2 e Services/LdpRouting).
    /// </summary>
    public enum TipoLdp : byte
    {
        Tipo1 = 1,
        Tipo2 = 2
    }
}
