namespace APS.WebApp.Services.OperationalAudit
{
    /// <summary>
    /// Classificação específica de um <see cref="OperationalFinding"/> — seção 7 da correção da Fase 2.
    /// Sempre acompanhada de um <see cref="OperationalAuditErrorDomain"/>; nunca decide sozinha se o
    /// problema é operacional ou da automação (isso é o domínio, um campo separado). Substitui a lista de
    /// classificação da Fase 1 — <c>Automacao</c> e <c>Operacional</c> saíram daqui porque agora são
    /// domínio, não classificação.
    /// </summary>
    public enum OperationalAuditClassification
    {
        ConfiguracaoProjeto,
        EstruturaAcc,
        EstruturaPlanilha,

        /// <summary>Configuração de atributos personalizados do ACC fora do esperado (seção 8) — nunca confundir com ausência simples do dado.</summary>
        QualidadeConfiguracaoAcc,

        DadoAusente,
        DadoInvalido,

        /// <summary>Documento existe na LDP, mas nenhum arquivo correspondente foi encontrado no ACC.</summary>
        DocumentoLdpSemAcc,

        /// <summary>Documento existe (ativo) no ACC, mas nenhuma linha correspondente foi encontrada na LDP.</summary>
        DocumentoAccSemLdp,

        /// <summary>Checkpoint 3 (Stage04/06): nenhum documento/arquivo candidato válido foi encontrado onde a automação real esperaria um (ex.: GRD sem candidato .xlsx/.xlsm válido) — distinto de DocumentoLdpSemAcc, que pressupõe um documento LDP específico já identificado.</summary>
        DocumentoAusenteAcc,

        /// <summary>Checkpoint 3 (Stage04): documento presente numa fonte real (ex.: GRD) sem linha correspondente na LDP — distinto de DocumentoAccSemLdp (que é sobre arquivo ACC ativo, não sobre cobertura de GRD).</summary>
        DocumentoAusenteLdp,

        /// <summary>Checkpoint 3 (Stage01 TS-005): registros logicamente duplicados pela mesma chave operacional real.</summary>
        DadoDuplicado,

        ReviewInconsistente,

        /// <summary>Atributo personalizado obrigatório não está cadastrado em nenhuma definição visível para a pasta.</summary>
        AtributoAccAusente,

        /// <summary>Atributo cadastrado, mas oculto na pasta/configuração onde deveria estar disponível.</summary>
        AtributoAccOculto,

        /// <summary>Duas ou mais definições diferentes normalizam para o mesmo nome — nunca escolhida arbitrariamente.</summary>
        AtributoAccAmbiguo,

        IntegracaoExterna,
        BancoDados,

        /// <summary>Risco técnico identificado no código (ex.: achados AUT-CODE-001/AUT-DB-001/AUT-DB-002/AUT-WRK-001) — registrado, não corrigido nesta fase.</summary>
        RiscoTecnico,

        Aviso,
        ComportamentoEsperado,

        /// <summary>Evidência insuficiente para classificar — sempre combinado com <see cref="OperationalAuditErrorDomain.Inconclusivo"/>.</summary>
        NaoClassificado
    }
}
