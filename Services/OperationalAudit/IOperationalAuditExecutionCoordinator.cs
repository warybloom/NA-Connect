using System;
using System.Threading;
using System.Threading.Tasks;

namespace APS.WebApp.Services.OperationalAudit
{
    /// <summary>
    /// Integração Auditoria Operacional x ciclo produtivo — único ponto que <c>GrdAutomationWorker</c>
    /// conhece (seção 6/14 do checkpoint de integração de produção). Nunca duplica DI nem o runner:
    /// monta, por chamada, um container isolado só com <see cref="OperationalAuditServiceCollectionExtensions.AddOperationalAudit"/>
    /// e delega tudo a <see cref="IOperationalAuditRunner"/>.
    ///
    /// GARANTIA CENTRAL (seção 2): esta interface NUNCA lança para quem chama — qualquer exceção,
    /// inclusive falha ao montar o container de DI da auditoria, é capturada internamente e devolvida
    /// como <see cref="OperationalAuditCoordinatorResult.ErroFatal"/>. A automação do N&amp;A Connect
    /// nunca precisa de try/catch ao redor da chamada a este coordinator — mas o chamador (seção 5) ainda
    /// deve envolver a chamada em try/catch por defesa em profundidade, nunca confiando cegamente numa
    /// única camada de proteção.
    /// </summary>
    public interface IOperationalAuditExecutionCoordinator
    {
        Task<OperationalAuditCoordinatorResult> ExecutarAuditoriaDoProjetoAsync(
            string projectIdAcc, string nomeProjeto, CancellationToken cancellationToken = default);
    }

    /// <summary>Resultado resumido para o log do Worker (seção 12) — nunca confundido com o status operacional (seção 11).</summary>
    public sealed class OperationalAuditCoordinatorResult
    {
        /// <summary>false quando <c>OperationalAudit:Enabled=false</c> — a auditoria nem chegou a ser tentada.</summary>
        public bool Executada { get; init; }

        /// <summary>
        /// true sempre que o coordinator concluiu sem exceção NÃO TRATADA — mesmo que o próprio
        /// <see cref="OperationalAuditResult"/> tenha findings, falhas de persistência ou de regras
        /// individuais (essas já são reportadas dentro de <see cref="Resultado"/>, nunca aqui).
        /// false só quando o coordinator capturou uma exceção fatal e inesperada (seção 2/6/21 teste 3/4/5).
        /// </summary>
        public bool Sucesso { get; init; }

        /// <summary>Preenchido só quando <see cref="Sucesso"/>=false — nunca a connection string/segredo bruto, só <c>ex.Message</c>.</summary>
        public string? ErroFatal { get; init; }

        /// <summary>Resultado completo do runner — null só quando <see cref="Executada"/>=false ou quando o coordinator falhou antes de conseguir chamar o runner.</summary>
        public OperationalAuditResult? Resultado { get; init; }

        public TimeSpan Duracao { get; init; }

        public static OperationalAuditCoordinatorResult NaoExecutada(TimeSpan duracao) =>
            new() { Executada = false, Sucesso = true, Duracao = duracao };

        public static OperationalAuditCoordinatorResult ExecutadaComSucesso(OperationalAuditResult resultado, TimeSpan duracao) =>
            new() { Executada = true, Sucesso = true, Resultado = resultado, Duracao = duracao };

        public static OperationalAuditCoordinatorResult Falhou(string erroFatal, TimeSpan duracao) =>
            new() { Executada = true, Sucesso = false, ErroFatal = erroFatal, Duracao = duracao };
    }
}
