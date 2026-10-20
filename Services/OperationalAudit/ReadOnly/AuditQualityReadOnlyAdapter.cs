using System.IO;

namespace APS.WebApp.Services.OperationalAudit.ReadOnly
{
    public sealed class AuditQualityReadOnlyAdapter : IAuditQualityReadOnlyAdapter
    {
        private readonly IAuditWorkbookReadOnlyAdapter _workbookAdapter;

        public AuditQualityReadOnlyAdapter(IAuditWorkbookReadOnlyAdapter workbookAdapter)
        {
            _workbookAdapter = workbookAdapter;
        }

        public AuditWorkbookSnapshot LerPastaDeQualidade(Stream conteudo) =>
            _workbookAdapter.AbrirSomenteLeitura(conteudo, linhaDeCabecalhoPadrao: 1);
    }
}
