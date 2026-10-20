namespace APS.WebApp.Services.OperationalAudit
{
    /// <summary>
    /// Domínio do problema de um <see cref="OperationalFinding"/> — separado de
    /// <see cref="OperationalAuditClassification"/> (seção 7 da correção da Fase 2): nunca inferido
    /// automaticamente de um nível de log (um Warning não vira OPERACIONAL só por ser Warning) e nunca
    /// atribuído por padrão — regras que não têm evidência suficiente devem usar
    /// <see cref="Inconclusivo"/>/<see cref="OperationalAuditClassification.NaoClassificado"/> juntos,
    /// nunca "chutar" um dos outros três.
    /// </summary>
    public enum OperationalAuditErrorDomain
    {
        /// <summary>Falha causada por dado, arquivo, planilha, pasta ou cadastro fora do padrão — nunca pela automação.</summary>
        Operacional,

        /// <summary>Falha comprovadamente causada pela automação (bug, lógica incorreta, código de teste esquecido em produção etc.).</summary>
        Automacao,

        /// <summary>Evidência insuficiente para decidir entre Operacional e Automacao — nunca escolher arbitrariamente.</summary>
        Inconclusivo,

        /// <summary>Nenhum problema — o resultado observado é exatamente o esperado pelo desenho atual do sistema.</summary>
        ComportamentoEsperado
    }
}
