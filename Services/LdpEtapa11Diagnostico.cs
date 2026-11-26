using System.Globalization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace NA_Connect.Services;

// Temporário: filtro configurável, sem exceções de negócio por documento.
internal static class LdpEtapa11Diagnostico
{
    internal static void Log(ILogger logger, IConfiguration configuration, string stage,
        string projectId, Dictionary<int, object> row)
    {
        var document = Convert.ToString(row.GetValueOrDefault(4), CultureInfo.InvariantCulture)?.Trim();
        if (!configuration.GetSection("LdpEtapa11Diagnostico:Documentos").GetChildren()
            .Any(c => string.Equals(c.Value, document, StringComparison.OrdinalIgnoreCase))) return;
        string Value(int column)
        {
            var value = row.GetValueOrDefault(column);
            return value == null || value == DBNull.Value ? "NULL" :
                $"{Convert.ToString(value, CultureInfo.InvariantCulture)} [{value.GetType().Name}]";
        }
        logger.LogInformation("[LDP_ETAPA11_DIAG] Estagio={Estagio} ProjectId={ProjectId} Documento={Documento} Planned_HH={PlannedHH} Actual_HH={ActualHH} Remaining_HH={RemainingHH} Planned_Cost={PlannedCost} Actual_Cost={ActualCost} Remaining_Cost={RemainingCost}",
            stage, projectId, document, Value(30), Value(31), Value(32), Value(33), Value(34), Value(35));
    }
}
