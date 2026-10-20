namespace APS.WebApp.Services.GestaoVista;

/// <summary>
/// Regra OFICIAL definida 2026-09-22 (correção de bug, Etapa 03 — CustoCalculado do Timesheet):
/// CustoCalculado prioriza o valor CONGELADO da própria célula CUSTO da planilha
/// (GestaoVistaTimesheetLancamentoExtraido.CustoInformadoNaPlanilha, coluna "15. CUSTO" — já calculado
/// pelo cliente com o histórico de custo/hora que ele tinha no momento do import, evidência real do
/// lançamento). Corrige o caso em que o recálculo independente do sistema (HorasTrabalhadas × custo/hora
/// vigente em CustoProfissionalRealHistorico) perde o valor inteiro só porque o profissional não tem
/// registro de taxa completo/vigente na nossa tabela — auditoria real BML011.013 2026-09-22: Antonio
/// Clareti (880h, R$220.000), Everton Eustaquio (447,08h, R$44.708) e Tim Chowdhury (56h, R$7.280)
/// tinham custo válido na própria planilha mas CustoCalculado saía NULL/zero pelo recálculo. Fallback:
/// recalcula por HH × custo/hora vigente só quando a planilha não tiver a coluna CUSTO (LDPs legadas,
/// CustoInformadoNaPlanilha sempre null nesse caso) ou a célula da linha estiver vazia — nunca perde o
/// que já funcionava (mesmo comportamento de antes preservado como fallback, nunca substituído).
/// </summary>
public static class Etapa03CustoRules
{
    public static decimal? ResolverCustoCalculado(decimal? custoInformadoNaPlanilha, decimal? horasTrabalhadas, decimal? custoHoraVigente)
        => custoInformadoNaPlanilha
           ?? (horasTrabalhadas.HasValue && custoHoraVigente.HasValue ? horasTrabalhadas.Value * custoHoraVigente.Value : null);
}
