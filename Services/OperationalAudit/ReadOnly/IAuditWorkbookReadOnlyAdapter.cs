using System.Collections.Generic;
using System.IO;

namespace APS.WebApp.Services.OperationalAudit.ReadOnly
{
    /// <summary>Um workbook aberto somente-leitura — nunca expõe o objeto EPPlus mutável diretamente, só uma projeção imutável.</summary>
    public sealed class AuditWorkbookSnapshot
    {
        public IReadOnlyList<string> NomesDasAbas { get; init; } = System.Array.Empty<string>();
        public IReadOnlyDictionary<string, AuditWorksheetSnapshot> Abas { get; init; } = new Dictionary<string, AuditWorksheetSnapshot>();
    }

    public sealed class AuditWorksheetSnapshot
    {
        public string Nome { get; init; } = string.Empty;
        public bool Oculta { get; init; }
        public int LinhaDeCabecalho { get; init; }
        public IReadOnlyList<string> Cabecalhos { get; init; } = System.Array.Empty<string>();
        public IReadOnlyDictionary<int, IReadOnlyList<string>> HeaderRows { get; init; } = new Dictionary<int, IReadOnlyList<string>>();
        public int TotalDeLinhasFisicas { get; init; }

        /// <summary>Uma linha por posição real na planilha (1-based, igual ao Excel) — nunca reindexado, para o achado sempre apontar para a linha real.</summary>
        public IReadOnlyDictionary<int, IReadOnlyDictionary<string, string?>> Linhas { get; init; } =
            new Dictionary<int, IReadOnlyDictionary<string, string?>>();

        /// <summary>Endereços de célula (ex.: "D12") cujo conteúdo original era uma FÓRMULA, não um valor literal — usado por MIR-006.</summary>
        public IReadOnlySet<string> CelulasComFormula { get; init; } = new HashSet<string>();
    }

    /// <summary>
    /// Abre um workbook Excel SOMENTE LEITURA (seção 5 do Checkpoint 2) — nunca chama Save/SaveAs, nunca
    /// escreve no arquivo de origem. Aceita um Stream (nunca um caminho de arquivo de produção diretamente)
    /// para deixar explícito que a auditoria não abre o arquivo original em modo compartilhado de escrita.
    /// </summary>
    public interface IAuditWorkbookReadOnlyAdapter
    {
        AuditWorkbookSnapshot AbrirSomenteLeitura(Stream conteudo, int linhaDeCabecalhoPadrao = 1);
    }
}
