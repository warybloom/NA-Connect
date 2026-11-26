namespace APS.WebApp.Services.Reports;

/// <summary>
/// Diagnóstico de falha dos endpoints de relatório: o corpo enviado ao frontend continua sem detalhes (nunca token, SQL ou connection string), mas o log do
/// servidor passa a registrar mensagem + primeiro frame do código da aplicação, e a resposta leva uma referência (TraceIdentifier) para casar UI e log.
/// </summary>
public static class ReportErrors
{
    public static string Describe(Exception ex)
    {
        var frame = ex.StackTrace?.Split('\n').Select(l => l.Trim()).FirstOrDefault(l => l.Contains("APS.WebApp"));
        return $"{ex.GetType().Name}: {ex.Message.Split('\n')[0]} @ {frame ?? "(sem frame da aplicação)"}";
    }
}
