using System;

namespace APS.WebApp.Models
{
    /// <summary>
    /// Um lançamento lido da aba "Timesheet" (oculta) da LDP, ainda em memória — antes de qualquer
    /// cálculo de custo ou persistência. Doc1..Doc5 são preservados apenas para diagnóstico: a tabela
    /// física gestao.TimesheetLancamento (migration 05/17) NÃO tem colunas Doc1..Doc5 hoje (candidato a
    /// uma migration futura, não criada nesta etapa).
    /// CustoInformadoNaPlanilha é o valor da célula CUSTO da própria planilha (coluna O do Timesheet,
    /// "15. CUSTO" nesta LDP de referência) — a regra de negócio vigente (16/09/2026) trata este valor
    /// como o custo REAL do lançamento e exige sua preservação (ver
    /// AUDITORIA_FORGESYNC_NA/PROGRESSO.md, ajuste de 16/09/2026). Isto substitui a decisão anterior desta
    /// sessão (registrada em AUDITORIA_FORGESYNC_NA/MAPA_DADOS_LDP_FINANCEIRO.md seção 1) de nunca ler essa
    /// célula por ela ser, na LDP de referência auditada, uma FÓRMULA (=Trabalhadas × procura em 'Custo
    /// Real' pelo Nome) — ou seja, o mesmo cálculo que o sistema já faz de forma independente em
    /// CustoCalculado. As duas semânticas são preservadas separadas, nunca fundidas: este campo é sempre o
    /// valor congelado que estava na célula no momento do import (evidência), CustoCalculado é sempre o
    /// recálculo do sistema com o histórico de Custo Real conhecido nesta sincronização — podem divergir
    /// legitimamente (correção de taxa aplicada depois, célula sobrescrita manualmente, etc.) e a
    /// divergência nunca deve ser escondida.
    /// </summary>
    public sealed class GestaoVistaTimesheetLancamentoExtraido
    {
        public int LinhaExcel { get; set; }

        public string NomeProfissional { get; set; }

        public decimal? HorasPlanejadas { get; set; }

        public decimal? HorasTrabalhadas { get; set; }

        public string Projeto { get; set; }

        public string Funcao { get; set; }

        public string Disciplina { get; set; }

        public string DocumentoCodigo { get; set; }

        public string Documento { get; set; }

        public string Descricao { get; set; }

        public string Doc1 { get; set; }
        public string Doc2 { get; set; }
        public string Doc3 { get; set; }
        public string Doc4 { get; set; }
        public string Doc5 { get; set; }

        /// <summary>Só preenchido se a coluna Data/Date existir nesta LDP — nunca inventada quando ausente.</summary>
        public DateTime? DataLancamento { get; set; }

        /// <summary>Valor congelado da célula CUSTO (coluna O) desta linha, evidência do que a planilha
        /// tinha no momento do import — nunca usado para calcular nada, nunca confundido com
        /// CustoCalculado. Null quando a coluna CUSTO não existir nesta LDP (comportamento esperado em
        /// LDPs legadas; nunca inventado).</summary>
        public decimal? CustoInformadoNaPlanilha { get; set; }
    }
}
