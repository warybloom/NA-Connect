namespace APS.WebApp.Models
{
    /// <summary>
    /// Resultado estruturado da persistência do Cronograma Tipo 1 no SQL (AccScheduleSyncService.PersistToSql).
    /// Existe para impedir que uma tarefa inválida seja simplesmente pulada e o método ainda assim seja
    /// tratado como sucesso pelo chamador — ver seção 4 da manutenção técnica: nenhuma persistência
    /// parcial pode ser chamada de sucesso.
    /// </summary>
    public sealed class CronogramaTipo1SyncResult
    {
        public int TarefasLidas { get; set; }
        public int TarefasComDocumento { get; set; }
        public int TarefasValidas { get; set; }
        public int TarefasInvalidas { get; set; }
        public int TarefasPersistidas { get; set; }
        public int TarefasAtualizadasNaLdp { get; set; }
        public int ErrosConversao { get; set; }
        public int ErrosSql { get; set; }
        public bool AliasAmbiguo { get; set; }
        public bool Sucesso { get; set; }
        public string CodigoResultado { get; set; } = "OK";
        public string MensagemResumo { get; set; } = string.Empty;

        public override string ToString() => MensagemResumo;
    }
}
