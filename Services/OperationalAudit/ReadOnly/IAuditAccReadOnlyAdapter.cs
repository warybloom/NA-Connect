using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace APS.WebApp.Services.OperationalAudit.ReadOnly
{
    /// <summary>Uma pasta do ACC (Data Management API), resolvida por alias — nunca por criação.</summary>
    public sealed class AccFolderRef
    {
        public string FolderId { get; init; } = string.Empty;
        public string Name { get; init; } = string.Empty;
        public string ResolvedPath { get; init; } = string.Empty;
    }

    /// <summary>Um item (arquivo) do ACC, já com metadados suficientes para classificação estrutural (temporário/backup/oculto) sem nenhuma chamada adicional.</summary>
    public sealed class AccItemRef
    {
        public string ItemId { get; init; } = string.Empty;
        public string Name { get; init; } = string.Empty;
        public string? Extension { get; init; }
        public string ParentFolderId { get; init; } = string.Empty;
        public string ParentFolderName { get; init; } = string.Empty;
        public bool IsHiddenFolder { get; init; }
        public string? LatestVersionUrn { get; init; }
        public DateTime? LastModifiedUtc { get; init; }

        /// <summary>
        /// Cadeia de nomes de pasta da raiz pesquisada até o pai imediato deste item (ex.:
        /// "07.01.01 - AUTODESK REVIT" ou "A/B/C" quando aninhado), separada por "/". Vazio quando o item
        /// está diretamente na pasta raiz pesquisada (caso comum das configurações não-recursivas). Usado
        /// por ATR-ARQUIVO_EM_PASTA_NAO_PERMITIDA — nunca por ATR-ARQUIVO_EM_BACKUP/RECYCLE_BIN, que são
        /// checks distintos e já cobertos separadamente.
        /// </summary>
        public string CaminhoRelativoAPastaRaiz { get; init; } = string.Empty;
    }

    /// <summary>
    /// Resultado de uma listagem paginada. <see cref="PaginacaoCompleta"/>=false só quando há evidência
    /// OBJETIVA de que a coleta não representa a população inteira (seção 2 da correção pós-Checkpoint 2:
    /// limite de segurança de páginas atingido, falha de rede no meio da paginação, ou contagem total
    /// declarada pela própria API divergente da quantidade de itens coletados) — nunca inferido só por
    /// "poucos arquivos". Falha na PRIMEIRA página (nada foi coletado) continua lançando, como antes —
    /// não há nada "parcial" a reportar quando zero páginas foram lidas com sucesso.
    /// </summary>
    public sealed class AccFolderListingResult
    {
        public IReadOnlyList<AccItemRef> Itens { get; init; } = Array.Empty<AccItemRef>();
        public int QuantidadeDePaginas { get; init; }
        public IReadOnlyList<int> QuantidadePorPagina { get; init; } = Array.Empty<int>();

        public bool PaginacaoCompleta { get; init; } = true;

        /// <summary>Nunca nulo quando <see cref="PaginacaoCompleta"/> é false — descreve a evidência objetiva encontrada.</summary>
        public string? MotivoPaginacaoIncompleta { get; init; }

        /// <summary>Contagem total declarada pela própria API (quando o endpoint expõe isso), para comparar contra <see cref="Itens"/>.Count — null quando a API não declara total.</summary>
        public int? TotalDeclaradoPelaApi { get; init; }
    }

    /// <summary>Definição de um atributo customizado — espelha exatamente o que a API de definições devolve (id + nome), sem inventar um campo de visibilidade que a API real não fornece.</summary>
    public sealed class AccCustomAttributeDefinitionRef
    {
        public int DefinitionId { get; init; }
        public string Name { get; init; } = string.Empty;
    }

    /// <summary>
    /// O valor (ou ausência) de UM atributo customizado para UMA versão. <see cref="ValorPresente"/>=false com
    /// <see cref="EvidenciaVisibilidadeExplicita"/>=null é o caso comum e real hoje (a API não distingue
    /// "ausente" de "oculto" — ver seção 9 do Checkpoint 2). PRESENT_HIDDEN só pode ser afirmado quando
    /// <see cref="EvidenciaVisibilidadeExplicita"/> vier preenchido por uma fonte de evidência explícita
    /// (nunca inferido só da API real, que não tem esse campo).
    /// </summary>
    public sealed class AccCustomAttributeValueRef
    {
        public int DefinitionId { get; init; }
        public string Name { get; init; } = string.Empty;
        public bool ValorPresente { get; init; }
        public string? Valor { get; init; }

        /// <summary>Nunca preenchido pelo adaptador real contra a API ACC hoje (ela não expõe esse dado) — só por fontes de evidência explícitas (ex.: fixtures de teste, comparação futura documentada).</summary>
        public bool? EvidenciaVisibilidadeExplicita { get; init; }
    }

    /// <summary>
    /// Acesso somente-leitura ao ACC (Data Management API) — nunca cria pasta, nunca sobe versão, nunca
    /// altera atributo. Todo método aqui corresponde a um GET real; nenhum aqui pode, mesmo transitivamente,
    /// alcançar um endpoint de escrita. Implementação real usa <see cref="Safety.AuditHttpWriteGuardHandler"/>
    /// como handler mais externo de qualquer <c>HttpClient</c> usado.
    /// </summary>
    public interface IAuditAccReadOnlyAdapter
    {
        Task<AccFolderRef?> LocalizarPastaPorAliasAsync(string projectId, IReadOnlyList<string> caminhosAlternativos, bool navegarRecursivamente, CancellationToken ct = default);

        Task<AccFolderListingResult> ListarConteudoCompletoAsync(string projectId, string folderId, bool recursivo, CancellationToken ct = default);

        Task<IReadOnlyList<AccCustomAttributeDefinitionRef>> ObterDefinicoesDeAtributoAsync(string projectId, string folderId, CancellationToken ct = default);

        Task<IReadOnlyList<AccCustomAttributeValueRef>> ObterValoresDeAtributoDaVersaoAsync(string projectId, string versionUrn, CancellationToken ct = default);

        /// <summary>Baixa o conteúdo da versão para um MemoryStream — nunca grava no arquivo de origem, nunca cria uma nova versão.</summary>
        Task<Stream> BaixarConteudoDaVersaoAsync(string projectId, string versionUrn, CancellationToken ct = default);
    }
}
