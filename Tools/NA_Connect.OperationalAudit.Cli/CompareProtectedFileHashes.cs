using System;
using System.Collections.Generic;
using System.Linq;

namespace NA_Connect.OperationalAudit.Cli
{
    /// <summary>Checkpoint 4.1, seção 1 — nunca confundir "sem baseline" com "sem alteração".</summary>
    public enum ProtectedFileHashStatus
    {
        Match,
        Mismatch,
        MissingBaseline
    }

    public sealed class ProtectedFileHashCheck
    {
        public string Nome { get; init; } = string.Empty;
        public string? HashBaseline { get; init; }
        public string? HashAtual { get; init; }
        public ProtectedFileHashStatus Status { get; init; }
    }

    public sealed class ProtectedFileHashReport
    {
        public IReadOnlyList<ProtectedFileHashCheck> Checagens { get; init; } = Array.Empty<ProtectedFileHashCheck>();

        /// <summary>true SOMENTE quando há pelo menos 1 checagem e TODAS são Match — MissingBaseline nunca conta como aprovação.</summary>
        public bool TudoIgual => Checagens.Count > 0 && Checagens.All(c => c.Status == ProtectedFileHashStatus.Match);
    }

    /// <summary>
    /// Checkpoint 4.1, seção 1: compara o baseline (gravado ANTES de qualquer alteração, em local
    /// persistente fora de <c>build_verify_output</c>/<c>bin</c>/<c>obj</c>) contra o hash atual dos mesmos
    /// arquivos. <c>baseline == null</c> (arquivo de baseline ausente) produz <see cref="ProtectedFileHashStatus.MissingBaseline"/>
    /// para TODOS os arquivos — nunca <see cref="ProtectedFileHashStatus.Match"/>.
    /// </summary>
    public static class CompareProtectedFileHashes
    {
        public static ProtectedFileHashReport Compare(IReadOnlyDictionary<string, string>? baseline, IReadOnlyDictionary<string, string> atual)
        {
            var checagens = new List<ProtectedFileHashCheck>();

            foreach (var (nome, hashAtual) in atual.OrderBy(kv => kv.Key, StringComparer.Ordinal))
            {
                if (baseline == null || !baseline.TryGetValue(nome, out var hashBaseline))
                {
                    checagens.Add(new ProtectedFileHashCheck { Nome = nome, HashBaseline = null, HashAtual = hashAtual, Status = ProtectedFileHashStatus.MissingBaseline });
                    continue;
                }

                checagens.Add(new ProtectedFileHashCheck
                {
                    Nome = nome,
                    HashBaseline = hashBaseline,
                    HashAtual = hashAtual,
                    Status = string.Equals(hashBaseline, hashAtual, StringComparison.OrdinalIgnoreCase) ? ProtectedFileHashStatus.Match : ProtectedFileHashStatus.Mismatch
                });
            }

            return new ProtectedFileHashReport { Checagens = checagens };
        }
    }
}
