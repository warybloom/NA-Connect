namespace APS.WebApp.Services.Planning;

/// <summary>
/// BML011.017: a versão do MPP contém valores do campo PREVISTO em Text2 como
/// "6000%", "300000%" etc. O REALIZADO de Text3 permanece válido e independente.
/// O campo previsto inválido fica NULO; nunca é dividido/normalizado por heurística.
/// Uma origem de REALIZADO inválida nunca é tolerada.
/// Outros projetos continuam com o bloqueio rígido preexistente.
/// </summary>
public static class CronogramaTipo1PlannedFieldPolicy
{
    public const string ProjetoIncidente = "b.ec04bee8-48ba-4640-9242-04a4df90d732";

    public static bool PodeIsolarPrevistoInvalido(string? projectIdAcc, bool realizadoValido,
        bool realizadoInvalido, bool previstoInvalido, string? motivoErroPrevisto)
        => string.Equals(projectIdAcc, ProjetoIncidente, StringComparison.OrdinalIgnoreCase)
           && realizadoValido
           && !realizadoInvalido
           && previstoInvalido
           && !string.IsNullOrWhiteSpace(motivoErroPrevisto)
           && motivoErroPrevisto.Contains("Percentual fora da faixa esperada", StringComparison.OrdinalIgnoreCase);
}
