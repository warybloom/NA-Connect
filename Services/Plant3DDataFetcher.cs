using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Dapper;
using APS.WebApp.Models;
using Microsoft.Extensions.Logging;

namespace APS.WebApp.Services
{
    public class Plant3DDataFetcher
    {
        private readonly ILogger<Plant3DDataFetcher> _logger;

        public Plant3DDataFetcher(ILogger<Plant3DDataFetcher> logger)
        {
            _logger = logger;
        }

        // ======================================================================
        // EXTRAÇÃO DE P&ID (Requer o ProcessPower.dcf baixado localmente)
        // ======================================================================
        
        public async Task<List<InstrumentoDTO>> ExtrairInstrumentosPIDAsync(string processPowerDcfPath)
        {
            _logger.LogInformation($"Extraindo Instrumentos do P&ID: {processPowerDcfPath}");
            var connectionString = $"Data Source={processPowerDcfPath};";

            using (var connection = new SqliteConnection(connectionString))
            {
                await connection.OpenAsync();

                string sql = @"
                    SELECT 
                        Instrumentation.Tag, 
                        Instrumentation.Type, 
                        Instrumentation.SubArea, 
                        Instrumentation.PnPID, 
                        PnPDrawings.Title, 
                        PnPDrawings.[Dwg Name] as DwgName
                    FROM PnPDataLinks 
                    INNER JOIN (Instrumentation LEFT JOIN PipeLineGroup ON Instrumentation.PnPID = PipeLineGroup.PnPID) as TieIn 
                        ON PnPDataLinks.RowId = Instrumentation.PnPID
                    INNER JOIN PnPDrawings 
                        ON PnPDataLinks.DwgId = PnPDrawings.PnPID
                    WHERE Instrumentation.Status <> 'Deleted' OR Instrumentation.Status IS NULL
                    ORDER BY Instrumentation.Tag";

                var result = await connection.QueryAsync<InstrumentoDTO>(sql);
                return result.ToList();
            }
        }

        public async Task<List<ValveDTO>> ExtrairValvulasPIDAsync(string processPowerDcfPath)
        {
            _logger.LogInformation($"Extraindo Válvulas do P&ID: {processPowerDcfPath}");
            var connectionString = $"Data Source={processPowerDcfPath};";

            using (var connection = new SqliteConnection(connectionString))
            {
                await connection.OpenAsync();

                string sql = @"
                    SELECT 
                        HandValves.Tag, 
                        HandValves.Code, 
                        HandValves.SubArea, 
                        HandValves.PnPID, 
                        HandValves.Number, 
                        PnPDrawings.Title, 
                        EngineeringItems.ClassName, 
                        PnPDrawings.[Dwg Name] as DwgName
                    FROM PnPDataLinks 
                    INNER JOIN (HandValves INNER JOIN EngineeringItems ON HandValves.PnPID = EngineeringItems.PnPID) 
                        ON PnPDataLinks.RowId = HandValves.PnPID
                    INNER JOIN PnPDrawings 
                        ON PnPDataLinks.DwgId = PnPDrawings.PnPID
                    WHERE HandValves.Status <> 'Deleted' OR HandValves.Status IS NULL
                    ORDER BY HandValves.Tag";

                var result = await connection.QueryAsync<ValveDTO>(sql);
                return result.ToList();
            }
        }

        public async Task<List<LinhaDTO>> ExtrairLinhasPIDAsync(string processPowerDcfPath)
        {
            _logger.LogInformation($"Extraindo Linhas do P&ID: {processPowerDcfPath}");
            var connectionString = $"Data Source={processPowerDcfPath};";

            using (var connection = new SqliteConnection(connectionString))
            {
                await connection.OpenAsync();

                string sql = @"
                    SELECT 
                        PipeLineGroup.Tag,
                        PipeLineGroup.NominalSize,
                        PipeLineGroup.ServiceColor,
                        PipeLineGroup.SubArea,
                        PipeLineGroup.LineNumber,
                        PipeLineGroup.NominalSpec,
                        PnPDrawings.[Dwg Name] as DwgName
                    FROM PnPDataLinks 
                    INNER JOIN PipeLineGroup ON PnPDataLinks.RowId = PipeLineGroup.PnPID
                    INNER JOIN PnPDrawings ON PnPDataLinks.DwgId = PnPDrawings.PnPID
                    WHERE PipeLineGroup.Status <> 'Deleted' OR PipeLineGroup.Status IS NULL
                    ORDER BY PipeLineGroup.LineNumber";

                var result = await connection.QueryAsync<LinhaDTO>(sql);
                return result.ToList();
            }
        }

        public async Task<List<EquipamentoDTO>> ExtrairEquipamentosPIDAsync(string processPowerDcfPath)
        {
            _logger.LogInformation($"Extraindo Equipamentos do P&ID: {processPowerDcfPath}");
            var connectionString = $"Data Source={processPowerDcfPath};";

            using (var connection = new SqliteConnection(connectionString))
            {
                await connection.OpenAsync();

                string sql = @"
                    SELECT 
                        Equipment.Tag,
                        Equipment.Area,
                        Equipment.EquipmentType,
                        PnPDrawings.[Dwg Name] as DwgName
                    FROM PnPDataLinks 
                    INNER JOIN Equipment ON PnPDataLinks.RowId = Equipment.PnPID
                    INNER JOIN PnPDrawings ON PnPDataLinks.DwgId = PnPDrawings.PnPID
                    WHERE Equipment.Status <> 'Deleted' OR Equipment.Status IS NULL
                    ORDER BY Equipment.Tag";

                var result = await connection.QueryAsync<EquipamentoDTO>(sql);
                return result.ToList();
            }
        }

        // ======================================================================
        // EXTRAÇÃO DO MODELO 3D (Requer o Piping.dcf baixado localmente)
        // ======================================================================

        public async Task<List<InstrumentoDTO>> ExtrairInstrumentos3DAsync(string pipingDcfPath)
        {
            _logger.LogInformation($"Extraindo Instrumentos do 3D: {pipingDcfPath}");
            var connectionString = $"Data Source={pipingDcfPath};";

            using (var connection = new SqliteConnection(connectionString))
            {
                await connection.OpenAsync();

                // Lógica mapeada do seu arquivo txt (Query instrumentos 3D)
                string sql = @"
                    SELECT 
                        PipeRunComponent.Tag, 
                        PipeRunComponent.NumberComp, 
                        PnPDrawings.Title, 
                        PnPDrawings.[Dwg Name] as DwgName,
                        PipeRunComponent.TypeComp as Type
                    FROM PnPDataLinks 
                    INNER JOIN PipeRunComponent ON PnPDataLinks.RowId = PipeRunComponent.PnPID
                    INNER JOIN PnPDrawings ON PnPDataLinks.DwgId = PnPDrawings.PnPID 
                    WHERE (PipeRunComponent.TypeComp <> '' AND PipeRunComponent.TypeComp IS NOT NULL) 
                      AND (PipeRunComponent.NumberComp <>'' AND PipeRunComponent.NumberComp IS NOT NULL)
                    ORDER BY PipeRunComponent.NumberComp";

                var result = await connection.QueryAsync<InstrumentoDTO>(sql);
                return result.ToList();
            }
        }

        public async Task<List<ValveDTO>> ExtrairValvulas3DAsync(string pipingDcfPath)
        {
            _logger.LogInformation($"Extraindo Válvulas do 3D: {pipingDcfPath}");
            var connectionString = $"Data Source={pipingDcfPath};";

            using (var connection = new SqliteConnection(connectionString))
            {
                await connection.OpenAsync();

                // Lógica mapeada do seu arquivo txt (Query VALVES 3D)
                string sql = @"
                    SELECT 
                        Valve.ValveBodyType as ClassName, 
                        EngineeringItems.Spec, 
                        EngineeringItems.NominalDiameter as Size, 
                        PnPDrawings.[Dwg Name] as DwgName, 
                        P3dLineGroup.Tag
                    FROM PnPDataLinks 
                    INNER JOIN (Valve LEFT JOIN (PipeRunComponent INNER JOIN EngineeringItems ON PipeRunComponent.PnPID = EngineeringItems.PnPID) as Comps ON Valve.PnPID = Comps.PnPID) as Valves ON PnPDataLinks.RowId = Valve.PnPID
                    INNER JOIN PnPDrawings ON PnPDataLinks.DwgId = PnPDrawings.PnPID
                    INNER JOIN (P3dLineGroupPartRelationship INNER JOIN P3dLineGroup ON P3dLineGroupPartRelationship.LineGroup = P3dLineGroup.PnPID) ON Valve.PnPID  = P3dLineGroupPartRelationship.Part
                    WHERE PipeRunComponent.TypeComp IS NULL OR PipeRunComponent.TypeComp = ''";

                var result = await connection.QueryAsync<ValveDTO>(sql);
                return result.ToList();
            }
        }
        
        public async Task<List<EquipamentoDTO>> ExtrairEquipamentos3DAsync(string pipingDcfPath)
        {
            _logger.LogInformation($"Extraindo Equipamentos do 3D: {pipingDcfPath}");
            var connectionString = $"Data Source={pipingDcfPath};";

            using (var connection = new SqliteConnection(connectionString))
            {
                await connection.OpenAsync();

                string sql = @"
                    SELECT 
                        Equipment.Tag,
                        Equipment.Area,
                        Equipment.EquipmentType,
                        PnPDrawings.[Dwg Name] as DwgName
                    FROM PnPDataLinks 
                    INNER JOIN Equipment ON PnPDataLinks.RowId = Equipment.PnPID
                    INNER JOIN PnPDrawings ON PnPDataLinks.DwgId = PnPDrawings.PnPID
                    ORDER BY Equipment.Tag";

                var result = await connection.QueryAsync<EquipamentoDTO>(sql);
                return result.ToList();
            }
        }
    }
}