namespace NA_Connect.OperationalAudit.Cli
{
    /// <summary>Modo de execução do CLI (Checkpoint 4, seção 13) — sempre registrado na metadata da execução.</summary>
    public enum CliMode
    {
        /// <summary>Ajuda/uso — nenhum argumento reconhecido foi passado.</summary>
        Help,

        /// <summary>Só valida presença semântica de configuração — nunca abre conexão nenhuma.</summary>
        ValidateConfig,

        /// <summary>Testa abertura de conexões (SELECT/metadata) e token/GET do ACC — nunca auditoria completa, nunca escrita.</summary>
        ValidateConnections,

        /// <summary>Leitura real permitida, PersistToDatabase sempre false, gera TXT/CSV/JSON, nunca grava audit.*.</summary>
        DryRun,

        /// <summary>Auditoria real, sem persistência (--run --allow-live-read).</summary>
        LiveNoPersist,

        /// <summary>Auditoria real com persistência (--run --allow-live-read --persist).</summary>
        LivePersist
    }
}
