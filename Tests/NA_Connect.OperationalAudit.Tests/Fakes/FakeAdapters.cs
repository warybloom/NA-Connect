using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using APS.WebApp.Services.OperationalAudit.Persistence;
using APS.WebApp.Services.OperationalAudit.ReadOnly;
using APS.WebApp.Services.OperationalAudit.Reporting;
using Newtonsoft.Json.Linq;

namespace NA_Connect.OperationalAudit.Tests.Fakes
{
    /// <summary>
    /// Fakes usados por TODOS os testes de regra/runner do Checkpoint 2 (seção 6: "todos os testes devem
    /// utilizar adaptadores fake" — nunca ACC/SQL/Excel/MPP reais). Cada fake é deliberadamente simples
    /// (dicionários/filas configurados pelo teste) — nenhuma lógica de negócio mora aqui, só resposta
    /// canônica ao que o teste pediu.
    /// </summary>
    public sealed class FakeAuditAccReadOnlyAdapter : IAuditAccReadOnlyAdapter
    {
        private readonly List<(Func<IReadOnlyList<string>, bool> Casa, AccFolderRef? Resultado)> _pastasPorAlias = new();
        private readonly Dictionary<string, AccFolderListingResult> _listagensPorFolderId = new();
        private readonly Dictionary<string, IReadOnlyList<AccCustomAttributeDefinitionRef>> _definicoesPorFolderId = new();
        private readonly Dictionary<string, IReadOnlyList<AccCustomAttributeValueRef>> _valoresPorVersionUrn = new();
        private readonly Dictionary<string, byte[]> _conteudoPorVersionUrn = new();

        public int ChamadasDeListagem { get; private set; }

        public void ConfigurarPasta(string trechoDoAlias, AccFolderRef? resultado) =>
            _pastasPorAlias.Add((aliases => aliases.Any(a => a.Contains(trechoDoAlias, StringComparison.OrdinalIgnoreCase)), resultado));

        public void ConfigurarListagem(string folderId, AccFolderListingResult resultado) => _listagensPorFolderId[folderId] = resultado;
        public void ConfigurarDefinicoes(string folderId, IReadOnlyList<AccCustomAttributeDefinitionRef> definicoes) => _definicoesPorFolderId[folderId] = definicoes;
        public void ConfigurarValores(string versionUrn, IReadOnlyList<AccCustomAttributeValueRef> valores) => _valoresPorVersionUrn[versionUrn] = valores;
        public void ConfigurarConteudo(string versionUrn, byte[] bytes) => _conteudoPorVersionUrn[versionUrn] = bytes;

        public Task<AccFolderRef?> LocalizarPastaPorAliasAsync(string projectId, IReadOnlyList<string> caminhosAlternativos, bool navegarRecursivamente, CancellationToken ct = default)
        {
            // Percorre do registro MAIS RECENTE para o mais antigo — uma segunda chamada a
            // ConfigurarPasta(mesmoTrecho, ...) dentro de um teste tem que SOBRESCREVER a primeira (ex.:
            // testes que reconfiguram "09.14" depois de um helper compartilhado já ter configurado "null").
            for (int i = _pastasPorAlias.Count - 1; i >= 0; i--)
            {
                if (_pastasPorAlias[i].Casa(caminhosAlternativos)) return Task.FromResult(_pastasPorAlias[i].Resultado);
            }
            return Task.FromResult<AccFolderRef?>(null);
        }

        public Task<AccFolderListingResult> ListarConteudoCompletoAsync(string projectId, string folderId, bool recursivo, CancellationToken ct = default)
        {
            ChamadasDeListagem++;
            return Task.FromResult(_listagensPorFolderId.TryGetValue(folderId, out var r) ? r : new AccFolderListingResult());
        }

        public Task<IReadOnlyList<AccCustomAttributeDefinitionRef>> ObterDefinicoesDeAtributoAsync(string projectId, string folderId, CancellationToken ct = default) =>
            Task.FromResult(_definicoesPorFolderId.TryGetValue(folderId, out var r) ? r : (IReadOnlyList<AccCustomAttributeDefinitionRef>)Array.Empty<AccCustomAttributeDefinitionRef>());

        public Task<IReadOnlyList<AccCustomAttributeValueRef>> ObterValoresDeAtributoDaVersaoAsync(string projectId, string versionUrn, CancellationToken ct = default) =>
            Task.FromResult(_valoresPorVersionUrn.TryGetValue(versionUrn, out var r) ? r : (IReadOnlyList<AccCustomAttributeValueRef>)Array.Empty<AccCustomAttributeValueRef>());

        public Task<Stream> BaixarConteudoDaVersaoAsync(string projectId, string versionUrn, CancellationToken ct = default) =>
            Task.FromResult<Stream>(new MemoryStream(_conteudoPorVersionUrn.TryGetValue(versionUrn, out var bytes) ? bytes : Array.Empty<byte>()));
    }

    public sealed class FakeAuditDatabaseReadOnlyAdapter : IAuditDatabaseReadOnlyAdapter
    {
        private readonly List<(Func<string, bool> Casa, DataTable Resultado)> _respostas = new();
        private readonly List<(Func<string, bool> Casa, Exception Excecao)> _excecoes = new();

        public void ConfigurarResposta(string trechoDoSql, DataTable resultado) =>
            _respostas.Add((sql => sql.Contains(trechoDoSql, StringComparison.OrdinalIgnoreCase), resultado));

        /// <summary>Checkpoint 3 (TS-001) — simula falha de leitura (timeout/schema indisponível) para um trecho de SQL específico.</summary>
        public void ConfigurarExcecao(string trechoDoSql, Exception excecao) =>
            _excecoes.Add((sql => sql.Contains(trechoDoSql, StringComparison.OrdinalIgnoreCase), excecao));

        public Task<DataTable> ConsultarAsync(string sql, IReadOnlyDictionary<string, object?>? parametros = null, CancellationToken ct = default)
        {
            for (int i = _excecoes.Count - 1; i >= 0; i--)
            {
                if (_excecoes[i].Casa(sql)) throw _excecoes[i].Excecao;
            }
            for (int i = _respostas.Count - 1; i >= 0; i--)
            {
                if (_respostas[i].Casa(sql)) return Task.FromResult(_respostas[i].Resultado);
            }
            return Task.FromResult(new DataTable());
        }
    }

    public sealed class FakeAuditWorkbookReadOnlyAdapter : IAuditWorkbookReadOnlyAdapter
    {
        private readonly Queue<AuditWorkbookSnapshot> _fila = new();
        public AuditWorkbookSnapshot? ProximoPadrao { get; set; }

        public void Enfileirar(AuditWorkbookSnapshot snapshot) => _fila.Enqueue(snapshot);

        public AuditWorkbookSnapshot AbrirSomenteLeitura(Stream conteudo, int linhaDeCabecalhoPadrao = 1) =>
            _fila.Count > 0 ? _fila.Dequeue() : (ProximoPadrao ?? new AuditWorkbookSnapshot());
    }

    public sealed class FakeAuditScheduleReadOnlyAdapter : IAuditScheduleReadOnlyAdapter
    {
        public AuditScheduleSnapshot Resultado { get; set; } = new();
        public AuditScheduleSnapshot LerSomenteLeitura(Stream conteudoMpp) => Resultado;
    }

    public sealed class FakeAuditWorkflowReadOnlyAdapter : IAuditWorkflowReadOnlyAdapter
    {
        private readonly Dictionary<string, IReadOnlyList<AuditReviewRef>> _reviewsPorItemId = new();
        private readonly Dictionary<string, JObject?> _definicoesPorWorkflowId = new();
        public IReadOnlyList<AuditReviewRef> RespostaPadrao { get; set; } = Array.Empty<AuditReviewRef>();

        public void ConfigurarReviews(string itemId, IReadOnlyList<AuditReviewRef> reviews) => _reviewsPorItemId[itemId] = reviews;

        /// <summary>Correção pós-Checkpoint 3 (WF-007) — configura a definição BRUTA (Newtonsoft JObject) que o resolvedor real de 4 níveis vai processar; null = definição indisponível.</summary>
        public void ConfigurarDefinicaoDoWorkflow(string workflowId, JObject? definicao) => _definicoesPorWorkflowId[workflowId] = definicao;

        public Task<IReadOnlyList<AuditReviewRef>> ObterHistoricoDeReviewsAsync(string projectId, string itemId, CancellationToken ct = default) =>
            Task.FromResult(_reviewsPorItemId.TryGetValue(itemId, out var r) ? r : RespostaPadrao);

        public Task<JObject?> ObterDefinicaoDoWorkflowAsync(string projectId, string workflowId, CancellationToken ct = default) =>
            Task.FromResult(_definicoesPorWorkflowId.TryGetValue(workflowId, out var r) ? r : null);
    }

    public sealed class FakeAuditPlanningReadOnlyAdapter : IAuditPlanningReadOnlyAdapter
    {
        public AuditWorksheetSnapshot Resultado { get; set; } = new();
        public IReadOnlyList<string> ColunasDestinoSql { get; set; } = Array.Empty<string>();

        public AuditWorksheetSnapshot LerPrimeiraAba(Stream conteudo, int linhaDeCabecalho) => Resultado;
        public AuditWorksheetSnapshot LerAba(Stream conteudo, string nomeAba, int linhaDeCabecalho) => Resultado;
        public AuditWorkbookSnapshot? WorkbookResultado { get; set; }
        public AuditWorkbookSnapshot LerAbas(Stream conteudo, int linhaDeCabecalho) => WorkbookResultado ?? new AuditWorkbookSnapshot
        { NomesDasAbas = new[] { Resultado.Nome }, Abas = new Dictionary<string, AuditWorksheetSnapshot> { [Resultado.Nome] = Resultado } };

        public Task<string?> ObterIdiomaProjetoAsync(string projectId, CancellationToken ct = default) => Task.FromResult<string?>(null);
        public IReadOnlyDictionary<string, string> SchemaWeekly { get; set; } = new Dictionary<string, string>();
        public Task<IReadOnlyDictionary<string, string>> ObterSchemaWeeklyAsync(CancellationToken ct = default) => Task.FromResult(SchemaWeekly);

        public Task<IReadOnlyList<string>> ObterColunasDestinoSqlAsync(string nomeTabela, CancellationToken ct = default) => Task.FromResult(ColunasDestinoSql);
    }

    public sealed class FakeAuditQualityReadOnlyAdapter : IAuditQualityReadOnlyAdapter
    {
        public AuditWorkbookSnapshot Resultado { get; set; } = new();
        public AuditWorkbookSnapshot LerPastaDeQualidade(Stream conteudo) => Resultado;
    }

    public sealed class FakeAuditRepository : IAuditRepository
    {
        public bool IniciarChamado { get; private set; }
        public bool FinalizarChamado { get; private set; }
        public List<OperationalAuditProjectData> ProjetosPersistidos { get; } = new();
        public bool FalharAoIniciar { get; set; }
        public bool FalharAoPersistirProjeto { get; set; }
        public bool LancarAoPersistirProjeto { get; set; }

        public Task<long> IniciarAuditRunAsync(Guid executionId, string triggeredBy, IReadOnlyList<string> stagesRequested, CancellationToken ct = default)
        {
            IniciarChamado = true;
            if (FalharAoIniciar) throw new InvalidOperationException("Falha simulada ao iniciar AuditRun.");
            return Task.FromResult(1L);
        }

        public Task<AuditProjectPersistResult> PersistirProjetoAsync(long auditRunId, OperationalAuditProjectData dados, CancellationToken ct = default)
        {
            if (LancarAoPersistirProjeto) throw new InvalidOperationException("Falha simulada ao persistir projeto.");
            ProjetosPersistidos.Add(dados);
            return Task.FromResult(FalharAoPersistirProjeto ? AuditProjectPersistResult.Falha("Falha simulada.") : AuditProjectPersistResult.Ok(1));
        }

        public Task FinalizarAuditRunAsync(long auditRunId, APS.WebApp.Services.OperationalAudit.OperationalAuditRunStatus status, int totalProjetos, int totalFindings, int totalRequisitos, string? erro, CancellationToken ct = default)
        {
            FinalizarChamado = true;
            return Task.CompletedTask;
        }
    }

    public sealed class FakeReportWriter : IOperationalAuditReportWriter
    {
        public int VezesChamado { get; private set; }
        public bool LancarExcecao { get; set; }
        public string ExtensaoFake { get; set; } = "fake";

        public Task<string> EscreverAsync(APS.WebApp.Services.OperationalAudit.OperationalAuditResult resultado, string outputDirectory, CancellationToken cancellationToken = default)
        {
            VezesChamado++;
            if (LancarExcecao) throw new InvalidOperationException("Falha simulada no writer.");
            return Task.FromResult(Path.Combine(outputDirectory, $"fake.{ExtensaoFake}"));
        }
    }

    public sealed class FakeOperationalAuditRule : APS.WebApp.Services.OperationalAudit.IOperationalAuditRule
    {
        public string Etapa { get; }
        public string NomeEtapa { get; }
        public Func<APS.WebApp.Services.OperationalAudit.OperationalAuditContext, APS.WebApp.Services.OperationalAudit.OperationalRuleExecutionResult>? Executar { get; set; }
        public bool LancarExcecao { get; set; }
        public int VezesExecutada { get; private set; }

        public FakeOperationalAuditRule(string etapa, string nomeEtapa = "Fake")
        {
            Etapa = etapa;
            NomeEtapa = nomeEtapa;
        }

        public Task<APS.WebApp.Services.OperationalAudit.OperationalRuleExecutionResult> ExecutarAsync(APS.WebApp.Services.OperationalAudit.OperationalAuditContext contexto)
        {
            VezesExecutada++;
            if (LancarExcecao) throw new InvalidOperationException($"Falha simulada na regra da Etapa {Etapa}.");
            return Task.FromResult(Executar?.Invoke(contexto) ?? new APS.WebApp.Services.OperationalAudit.OperationalRuleExecutionResult());
        }
    }
}
