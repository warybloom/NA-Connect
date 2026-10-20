using System.IO;

namespace APS.WebApp.Services.OperationalAudit.ReadOnly
{
    /// <summary>Leitura somente-leitura de planilhas de Qualidade (Etapa 17) — abre e devolve a projeção imutável das 3 abas esperadas; a regra decide o que fazer com elas.</summary>
    public interface IAuditQualityReadOnlyAdapter
    {
        AuditWorkbookSnapshot LerPastaDeQualidade(Stream conteudo);
    }
}
