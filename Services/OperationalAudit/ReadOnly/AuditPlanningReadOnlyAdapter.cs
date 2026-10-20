using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace APS.WebApp.Services.OperationalAudit.ReadOnly
{
    public sealed class AuditPlanningReadOnlyAdapter : IAuditPlanningReadOnlyAdapter
    {
        private readonly IAuditWorkbookReadOnlyAdapter _workbookAdapter;
        private readonly IAuditDatabaseReadOnlyAdapter _databaseAdapter;

        public AuditPlanningReadOnlyAdapter(IAuditWorkbookReadOnlyAdapter workbookAdapter, IAuditDatabaseReadOnlyAdapter databaseAdapter)
        {
            _workbookAdapter = workbookAdapter;
            _databaseAdapter = databaseAdapter;
        }

        public AuditWorksheetSnapshot LerPrimeiraAba(Stream conteudo, int linhaDeCabecalho)
        {
            var workbook = _workbookAdapter.AbrirSomenteLeitura(conteudo, linhaDeCabecalho);
            foreach (var nome in workbook.NomesDasAbas)
            {
                return workbook.Abas[nome]; // API histórica; a Etapa 15 usa LerAbas.
            }
            return new AuditWorksheetSnapshot();
        }

        public AuditWorksheetSnapshot LerAba(Stream conteudo, string nomeAba, int linhaDeCabecalho)
        {
            var workbook = _workbookAdapter.AbrirSomenteLeitura(conteudo, linhaDeCabecalho);
            return System.Linq.Enumerable.SingleOrDefault(workbook.Abas.Values,
                a => Planning.WeeklyScheduleContract.Normalize(a.Nome) == Planning.WeeklyScheduleContract.Normalize(nomeAba))
                ?? new AuditWorksheetSnapshot();
        }

        // Identificação da Etapa 15 examina as primeiras dez linhas de todas as abas.
        public AuditWorkbookSnapshot LerAbas(Stream conteudo, int linhaDeCabecalho)
        {
            OfficeOpenXml.ExcelPackage.LicenseContext = OfficeOpenXml.LicenseContext.NonCommercial;
            using var package = new OfficeOpenXml.ExcelPackage(conteudo);
            var sheets = new Dictionary<string, AuditWorksheetSnapshot>();
            foreach (var sheet in package.Workbook.Worksheets)
            {
                var rows = new Dictionary<int, IReadOnlyList<string>>();
                for (int r = 1; r <= 10; r++)
                {
                    var values = new List<string>();
                    for (int c = 1; c <= (sheet.Dimension?.End.Column ?? 0); c++) values.Add(sheet.Cells[r, c].Text);
                    rows[r] = values;
                }
                sheets.Add(sheet.Name, new AuditWorksheetSnapshot
                { Nome = sheet.Name, LinhaDeCabecalho = linhaDeCabecalho, Cabecalhos = rows[linhaDeCabecalho], HeaderRows = rows });
            }
            return new AuditWorkbookSnapshot { NomesDasAbas = new List<string>(sheets.Keys), Abas = sheets };
        }

        public async Task<string?> ObterIdiomaProjetoAsync(string projectId, CancellationToken ct = default)
        {
            var table = await _databaseAdapter.ConsultarAsync(
                "SELECT Project_Language FROM dbo.Projetos WHERE Project_ID_ACC=@pid OR Project_ID_ACC=@cleanPid",
                new Dictionary<string, object?> { ["@pid"] = projectId, ["@cleanPid"] = projectId.StartsWith("b.") ? projectId.Substring(2) : projectId }, ct);
            return table.Rows.Count == 0 ? null : table.Rows[0]["Project_Language"]?.ToString();
        }

        public async Task<IReadOnlyDictionary<string, string>> ObterSchemaWeeklyAsync(CancellationToken ct = default)
        {
            var table = await _databaseAdapter.ConsultarAsync(
                "SELECT COLUMN_NAME, DATA_TYPE FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_SCHEMA='dbo' AND TABLE_NAME='tb_WeeklyEngineeringSchedule'", ct: ct);
            var schema = new Dictionary<string, string>(System.StringComparer.OrdinalIgnoreCase);
            foreach (DataRow row in table.Rows) schema.Add(row["COLUMN_NAME"].ToString()!, row["DATA_TYPE"].ToString()!);
            return schema;
        }

        public async Task<IReadOnlyList<string>> ObterColunasDestinoSqlAsync(string nomeTabela, CancellationToken ct = default)
        {
            var parametros = new Dictionary<string, object?> { ["@TableName"] = nomeTabela };
            DataTable tabela = await _databaseAdapter.ConsultarAsync(
                "SELECT COLUMN_NAME FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_SCHEMA = 'dbo' AND TABLE_NAME = @TableName " +
                "AND COLUMN_NAME NOT IN ('Id_Registro','Project_ID_ACC','Data_Sincronizacao') ORDER BY ORDINAL_POSITION",
                parametros, ct);

            var colunas = new List<string>();
            foreach (DataRow linha in tabela.Rows)
            {
                colunas.Add(linha["COLUMN_NAME"]?.ToString() ?? string.Empty);
            }
            return colunas;
        }
    }
}
