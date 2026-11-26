namespace APS.WebApp.Models
{
    /// <summary>
    /// Resultado da persistência transacional de UMA tabela de Planning Control (seção 19/20 do pedido):
    /// contar registros anteriores, DELETE por Project_ID_ACC, SqlBulkCopy nomeado, tudo dentro da MESMA
    /// SqlTransaction. Em caso de falha, <see cref="RollbackExecutado"/> confirma que nada ficou
    /// parcialmente aplicado.
    /// </summary>
    public sealed class ResultadoPersistenciaPlanejamento
    {
        public bool Sucesso { get; set; }
        public int RegistrosAnteriores { get; set; }
        public int LinhasInseridas { get; set; }
        public bool CommitExecutado { get; set; }
        public bool RollbackExecutado { get; set; }
        public string Erro { get; set; }

        public override string ToString() =>
            $"Sucesso={Sucesso} | RegistrosAnteriores={RegistrosAnteriores} | LinhasInseridas={LinhasInseridas} | " +
            $"CommitExecutado={CommitExecutado} | RollbackExecutado={RollbackExecutado} | Erro={Erro}";
    }
}
