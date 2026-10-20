using System.Collections.Generic;
using System.Data;

namespace APS.WebApp.Models
{
    /// <summary>
    /// Códigos de resultado estáveis de UMA tabela de Qualidade (seção 14 do pedido). Usados tanto no log
    /// quanto em <see cref="ResultadoTabelaQualidade.CodigoResultado"/> — nunca inventados/formatados
    /// livremente.
    /// </summary>
    public static class QualidadeCodigosResultado
    {
        public const string Sucesso = "QUALIDADE_SUCESSO";
        public const string SucessoComAlertas = "QUALIDADE_SUCESSO_COM_ALERTAS";
        public const string SemLinhasValidas = "QUALIDADE_SEM_LINHAS_VALIDAS";
        public const string ErroEstrutural = "QUALIDADE_ERRO_ESTRUTURAL";
        public const string ErroSql = "QUALIDADE_ERRO_SQL";
    }

    /// <summary>
    /// Resultado completo e independente de UMA das três tabelas de Qualidade (BASE LANÇAMENTO/RNC/
    /// DESVIO DE QUALIDADE — seções 11/12/14 do pedido). Cada tabela tem seu próprio ciclo completo:
    /// localizar aba, resolver cabeçalho, preparar linhas (com preenchimento automático quando
    /// aplicável), validar schema e persistir em transação PRÓPRIA — uma falha aqui nunca bloqueia as
    /// outras duas tabelas.
    /// </summary>
    public sealed class ResultadoTabelaQualidade
    {
        public bool Sucesso { get; set; }
        public bool SucessoComAlertas { get; set; }
        public string CodigoResultado { get; set; }
        public string Mensagem { get; set; }

        public string Aba { get; set; }
        public string TabelaDestino { get; set; }
        public int HeaderRow { get; set; }

        public int LinhasFisicas { get; set; }
        public int LinhasVazias { get; set; }
        public int LinhasPlaceholder { get; set; }
        public int LinhasValidas { get; set; }
        public int LinhasComPreenchimentoAutomatico { get; set; }
        public int CamposPreenchidosAutomaticamente { get; set; }
        public int LinhasRejeitadas { get; set; }
        public int LinhasInseridas { get; set; }
        public int RegistrosAnteriores { get; set; }

        public bool CommitExecutado { get; set; }
        public bool RollbackExecutado { get; set; }

        public List<string> CamposMapeados { get; set; } = new List<string>();
        public List<string> CamposAusentes { get; set; } = new List<string>();
        public List<string> Erros { get; set; } = new List<string>();
        public List<QualidadeCampoPreenchidoAutomaticamente> CamposPreenchidosDetalhe { get; set; } = new List<QualidadeCampoPreenchidoAutomaticamente>();

        /// <summary>DataTable já preparado (com preenchimento automático aplicado), pronto para BulkCopy. Null quando Sucesso é false ou quando não há linhas válidas.</summary>
        public DataTable Tabela { get; set; }

        public override string ToString() =>
            $"Aba={Aba} | TabelaDestino={TabelaDestino} | Sucesso={Sucesso} | SucessoComAlertas={SucessoComAlertas} | CodigoResultado={CodigoResultado} | " +
            $"HeaderRow={HeaderRow} | LinhasFisicas={LinhasFisicas} | LinhasVazias={LinhasVazias} | LinhasPlaceholder={LinhasPlaceholder} | " +
            $"LinhasValidas={LinhasValidas} | LinhasComPreenchimentoAutomatico={LinhasComPreenchimentoAutomatico} | " +
            $"CamposPreenchidosAutomaticamente={CamposPreenchidosAutomaticamente} | LinhasRejeitadas={LinhasRejeitadas} | LinhasInseridas={LinhasInseridas} | " +
            $"RegistrosAnteriores={RegistrosAnteriores} | CommitExecutado={CommitExecutado} | RollbackExecutado={RollbackExecutado} | Mensagem={Mensagem}";
    }
}
