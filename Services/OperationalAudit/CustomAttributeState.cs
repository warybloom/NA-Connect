namespace APS.WebApp.Services.OperationalAudit
{
    /// <summary>
    /// Estado observado de um atributo personalizado do ACC — seção 8 da correção da Fase 2. Usado pela
    /// futura <c>Stage07AttributesAuditRule</c> (Checkpoint 2). Nunca inventar
    /// <see cref="PresentHidden"/> quando a API simplesmente deixou de retornar a definição — nesse caso o
    /// estado correto é <see cref="ApiVisibilityInconclusive"/>.
    /// </summary>
    public enum CustomAttributeState
    {
        /// <summary>Não cadastrado em nenhuma definição visível (raiz ou pasta específica).</summary>
        Missing,

        /// <summary>Cadastrado e confirmadamente visível na pasta avaliada.</summary>
        PresentVisible,

        /// <summary>Cadastrado, mas oculto na pasta/configuração onde deveria ser utilizado.</summary>
        PresentHidden,

        /// <summary>Duas ou mais definições diferentes normalizam para o mesmo nome — não resolvido.</summary>
        Ambiguous,

        /// <summary>A API retornou a definição, mas não trouxe evidência suficiente para confirmar visibilidade — nunca presumir oculto.</summary>
        ApiVisibilityInconclusive
    }
}
