namespace APS.WebApp.Models
{
    /// <summary>
    /// Tipo de dado esperado de um campo lógico de Planning Control (seção 18 do pedido). Controla qual
    /// parser é usado (data/inteiro nunca convertem valor inválido em um valor "seguro" silencioso — ver
    /// PlanningPreflightService) e o tipo da coluna criada no DataTable preparado para o SqlBulkCopy.
    /// </summary>
    public enum PlanningColumnDataType
    {
        Texto,
        Data,
        Decimal,
        Inteiro
    }

    /// <summary>
    /// Um campo lógico de uma tabela de Planning Control (Etapas 12/13/14/16 — seção 6 do pedido).
    /// Separa explicitamente: nome interno (<see cref="LogicalName"/>), cabeçalhos aceitos em PT/EN
    /// (<see cref="AcceptedHeaders"/>, sempre comparados por igualdade exata após normalização, nunca
    /// Contains), coluna SQL de destino (<see cref="SqlColumnName"/>) e a posição física — que nunca é
    /// fixada aqui, é sempre descoberta em tempo de execução pelo PlanningHeaderResolver.
    /// </summary>
    public sealed class PlanningColumnDefinition
    {
        public string LogicalName { get; set; }
        public string[] AcceptedHeaders { get; set; }
        public string SqlColumnName { get; set; }
        public PlanningColumnDataType DataType { get; set; } = PlanningColumnDataType.Texto;

        /// <summary>
        /// Obrigatório no cabeçalho (entra na assinatura mínima de detecção da linha — seção 4) E
        /// obrigatório com conteúdo em cada linha de dado (seção 9/19). As duas obrigatoriedades andam
        /// juntas nas quatro tabelas desta entrega (External/Internal/Scope/Hold) — nenhuma delas tem um
        /// campo obrigatório-no-cabeçalho-mas-opcional-na-linha ou vice-versa.
        /// </summary>
        public bool Required { get; set; }
        public bool? RequiredValue { get; set; }
        public bool StoreAsText { get; set; }

        /// <summary>
        /// Participa da checagem de "linha real" (seção 11): uma linha só é importada se pelo menos um
        /// campo identificador tiver conteúdo operacional (não vazio, não "-", não N/A, não fórmula vazia).
        /// </summary>
        public bool IsRowIdentifier { get; set; }
    }
}
