using System.Data;
using Microsoft.Data.SqlClient;

namespace APS.WebApp.Services.DocumentQuality;

public interface IOcrTemplateRepository
{
    Task<IReadOnlyList<OcrPackage>> GetPackagesAsync(CancellationToken cancellationToken = default);
    Task<OcrPackage> CreatePackageAsync(string name, CancellationToken cancellationToken = default);
    Task<OcrPackage> RenamePackageAsync(int packageId, string name, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<OcrTemplateRegion>> GetRegionsAsync(int packageId, string? sheetType = null, CancellationToken cancellationToken = default);
    Task ReplaceRegionsAsync(int packageId, string sheetType, IReadOnlyList<SaveOcrTemplateRegion> regions, CancellationToken cancellationToken = default);
    Task ClearRegionsAsync(int packageId, string sheetType, CancellationToken cancellationToken = default);
    Task<DocumentQualityProjectConfiguration> GetProjectConfigurationAsync(string projectIdAcc, CancellationToken cancellationToken = default);
    Task<DocumentQualityProjectConfiguration> SaveProjectConfigurationAsync(string projectIdAcc, SaveDocumentQualityProjectConfiguration configuration, CancellationToken cancellationToken = default);
    /// <summary>Template de auditoria (config.ProjetoConfiguracao.PacoteOcrId) de todos os projetos — coluna da Configuração do Projeto.</summary>
    Task<IReadOnlyList<ProjectAuditTemplate>> GetProjectTemplatesAsync(CancellationToken cancellationToken = default);
    /// <summary>Altera SÓ o template do projeto (a pasta de auditoria já configurada é preservada).</summary>
    Task<DocumentQualityProjectConfiguration> SaveProjectTemplateAsync(string projectIdAcc, int? packageId, CancellationToken cancellationToken = default);

    /// <summary>Campos auditados do TEMPLATE (config.PacoteOcrCampo). null = tabela ainda não criada (migration 07 pendente).</summary>
    Task<IReadOnlyList<TemplateField>?> GetTemplateFieldsAsync(int packageId, CancellationToken cancellationToken = default);
    /// <summary>Grava o conjunto de campos (ordem = posição; removidos ficam inativos, nunca apagados). Campos já validados no catálogo.</summary>
    Task<IReadOnlyList<TemplateField>> SaveTemplateFieldsAsync(int packageId, IReadOnlyList<string> fields, CancellationToken cancellationToken = default);
    /// <summary>Mapeamento de formatos do template (config.PacoteOcrFormato); vazio se a tabela não existir.</summary>
    Task<IReadOnlyList<FormatMapping>> GetFormatMappingsAsync(int packageId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<FormatMapping>> SaveFormatMappingsAsync(int packageId, IReadOnlyList<FormatMapping> mappings, CancellationToken cancellationToken = default);
    /// <summary>Valores reais da coluna Format nas LDPs dos projetos que usam o template (todos os projetos se nenhum usar).</summary>
    Task<IReadOnlyList<string>> GetLdpFormatValuesAsync(int packageId, CancellationToken cancellationToken = default);
}

/// <summary>Migration 07 (config.PacoteOcrCampo / config.PacoteOcrFormato) ainda não aplicada.</summary>
public sealed class DocumentQualitySchemaPendingException(string message) : InvalidOperationException(message);

public sealed record ProjectAuditTemplate(string ProjectIdAcc, int PackageId, string? PackageName);

public sealed class SqlOcrTemplateRepository(IConfiguration configuration) : IOcrTemplateRepository
{
    private string ConnectionString => configuration.GetConnectionString("LocalConnection")
        ?? throw new InvalidOperationException("ConnectionStrings:LocalConnection não configurada.");

    public async Task<IReadOnlyList<OcrPackage>> GetPackagesAsync(CancellationToken cancellationToken = default)
    {
        const string sql = "SELECT PacoteOcrId, Nome_Pacote, Data_Criacao FROM config.PacoteOcr ORDER BY Nome_Pacote;";
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var result = new List<OcrPackage>();
        while (await reader.ReadAsync(cancellationToken))
            result.Add(new(reader.GetInt32(0), reader.GetString(1), reader.GetDateTime(2)));
        return result;
    }

    public async Task<OcrPackage> CreatePackageAsync(string name, CancellationToken cancellationToken = default)
    {
        name = NormalizePackageName(name);
        const string sql = "INSERT INTO config.PacoteOcr (Nome_Pacote) OUTPUT INSERTED.PacoteOcrId, INSERTED.Nome_Pacote, INSERTED.Data_Criacao VALUES (@name);";
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.Add("@name", SqlDbType.NVarChar, 200).Value = name;
        try
        {
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            await reader.ReadAsync(cancellationToken);
            return new(reader.GetInt32(0), reader.GetString(1), reader.GetDateTime(2));
        }
        catch (SqlException ex) when (ex.Number is 2601 or 2627)
        {
            throw new InvalidOperationException("Já existe um template com esse nome.", ex);
        }
    }

    public async Task<OcrPackage> RenamePackageAsync(int packageId, string name, CancellationToken cancellationToken = default)
    {
        if (packageId <= 0) throw new ArgumentException("Template invÃ¡lido.", nameof(packageId));
        name = NormalizePackageName(name);
        const string sql = "UPDATE config.PacoteOcr SET Nome_Pacote=@name OUTPUT INSERTED.PacoteOcrId, INSERTED.Nome_Pacote, INSERTED.Data_Criacao WHERE PacoteOcrId=@packageId;";
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.Add("@packageId", SqlDbType.Int).Value = packageId;
        command.Parameters.Add("@name", SqlDbType.NVarChar, 200).Value = name;
        try
        {
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken)) throw new KeyNotFoundException("Template nÃ£o encontrado.");
            return new(reader.GetInt32(0), reader.GetString(1), reader.GetDateTime(2));
        }
        catch (SqlException ex) when (ex.Number is 2601 or 2627)
        {
            throw new InvalidOperationException("JÃ¡ existe um template com esse nome.", ex);
        }
    }

    public async Task<IReadOnlyList<OcrTemplateRegion>> GetRegionsAsync(int packageId, string? sheetType = null, CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT PacoteOcrCoordenadaId, PacoteOcrId, TipoFolha, CampoDestino, X, Y, Width, Height,
                   Pagina, ReferenceWidth, ReferenceHeight
            FROM config.PacoteOcrCoordenada
            WHERE PacoteOcrId=@packageId AND (@sheetType IS NULL OR TipoFolha=@sheetType)
            ORDER BY TipoFolha, CampoDestino;
            """;
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.Add("@packageId", SqlDbType.Int).Value = packageId;
        command.Parameters.Add("@sheetType", SqlDbType.NVarChar, 50).Value = (object?)sheetType ?? DBNull.Value;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var result = new List<OcrTemplateRegion>();
        while (await reader.ReadAsync(cancellationToken))
            result.Add(new(
                reader.GetInt32(0), reader.GetInt32(1), reader.GetString(2), reader.GetString(3), reader.GetDecimal(4), reader.GetDecimal(5),
                reader.GetDecimal(6), reader.GetDecimal(7), reader.GetInt32(8), reader.GetDecimal(9), reader.GetDecimal(10)));
        return result;
    }

    public async Task ReplaceRegionsAsync(int packageId, string sheetType, IReadOnlyList<SaveOcrTemplateRegion> regions, CancellationToken cancellationToken = default)
    {
        sheetType = (sheetType ?? string.Empty).Trim().ToUpperInvariant();
        if (sheetType.Length is < 1 or > 50) throw new ArgumentException("Tipo de folha inválido.", nameof(sheetType));
        ValidateRegions(regions);

        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(cancellationToken);
        try
        {
            await using (var delete = new SqlCommand("DELETE FROM config.PacoteOcrCoordenada WHERE PacoteOcrId=@packageId AND TipoFolha=@sheetType;", connection, transaction))
            {
                delete.Parameters.Add("@packageId", SqlDbType.Int).Value = packageId;
                delete.Parameters.Add("@sheetType", SqlDbType.NVarChar, 50).Value = sheetType;
                await delete.ExecuteNonQueryAsync(cancellationToken);
            }

            const string insertSql = """
                INSERT INTO config.PacoteOcrCoordenada
                    (PacoteOcrId, TipoFolha, CampoDestino, X, Y, Width, Height, Pagina, ReferenceWidth, ReferenceHeight)
                VALUES (@packageId, @sheetType, @field, @x, @y, @width, @height, @page, @referenceWidth, @referenceHeight);
                """;
            foreach (var region in regions)
            {
                await using var insert = new SqlCommand(insertSql, connection, transaction);
                insert.Parameters.Add("@packageId", SqlDbType.Int).Value = packageId;
                insert.Parameters.Add("@sheetType", SqlDbType.NVarChar, 50).Value = sheetType;
                insert.Parameters.Add("@field", SqlDbType.NVarChar, 100).Value = region.CampoDestino;
                AddDecimal(insert, "@x", region.X); AddDecimal(insert, "@y", region.Y);
                AddDecimal(insert, "@width", region.Width); AddDecimal(insert, "@height", region.Height);
                insert.Parameters.Add("@page", SqlDbType.Int).Value = region.Pagina;
                AddDecimal(insert, "@referenceWidth", region.ReferenceWidth); AddDecimal(insert, "@referenceHeight", region.ReferenceHeight);
                await insert.ExecuteNonQueryAsync(cancellationToken);
            }
            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    public async Task ClearRegionsAsync(int packageId, string sheetType, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand("DELETE FROM config.PacoteOcrCoordenada WHERE PacoteOcrId=@packageId AND TipoFolha=@sheetType;", connection);
        command.Parameters.Add("@packageId", SqlDbType.Int).Value = packageId;
        command.Parameters.Add("@sheetType", SqlDbType.NVarChar, 50).Value = sheetType.Trim().ToUpperInvariant();
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<DocumentQualityProjectConfiguration> GetProjectConfigurationAsync(string projectIdAcc, CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT P.Project_ID_ACC, P.Nome_Projeto, PC.PacoteOcrId, PO.Nome_Pacote,
                   PC.Folder_Id_Auditoria, PC.Folder_Path_Auditoria, PC.Data_Atualizacao
            FROM dbo.Projetos P
            LEFT JOIN config.ProjetoConfiguracao PC ON PC.Project_ID_ACC=P.Project_ID_ACC
            LEFT JOIN config.PacoteOcr PO ON PO.PacoteOcrId=PC.PacoteOcrId
            WHERE P.Project_ID_ACC=@projectId;
            """;
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.Add("@projectId", SqlDbType.NVarChar, 255).Value = projectIdAcc;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) throw new KeyNotFoundException("Projeto não encontrado.");
        return new(
            reader.GetString(0), reader.IsDBNull(1) ? null : reader.GetString(1), reader.IsDBNull(2) ? null : reader.GetInt32(2),
            reader.IsDBNull(3) ? null : reader.GetString(3), reader.IsDBNull(4) ? null : reader.GetString(4),
            reader.IsDBNull(5) ? null : reader.GetString(5), reader.IsDBNull(6) ? null : reader.GetDateTime(6));
    }

    public async Task<DocumentQualityProjectConfiguration> SaveProjectConfigurationAsync(string projectIdAcc, SaveDocumentQualityProjectConfiguration configuration, CancellationToken cancellationToken = default)
    {
        if (configuration.PackageId is <= 0) throw new ArgumentException("Template inválido.");
        if ((configuration.AuditFolderId?.Length ?? 0) > 255 || (configuration.AuditFolderPath?.Length ?? 0) > 1000)
            throw new ArgumentException("Pasta de auditoria inválida.");

        const string sql = """
            MERGE config.ProjetoConfiguracao AS target
            USING (SELECT @projectId AS Project_ID_ACC) AS source
              ON target.Project_ID_ACC=source.Project_ID_ACC
            WHEN MATCHED THEN UPDATE SET PacoteOcrId=@packageId, Folder_Id_Auditoria=@folderId,
                Folder_Path_Auditoria=@folderPath, Data_Atualizacao=SYSUTCDATETIME()
            WHEN NOT MATCHED THEN INSERT (Project_ID_ACC, PacoteOcrId, Folder_Id_Auditoria, Folder_Path_Auditoria)
                VALUES (@projectId, @packageId, @folderId, @folderPath);
            """;
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.Add("@projectId", SqlDbType.NVarChar, 255).Value = projectIdAcc;
        command.Parameters.Add("@packageId", SqlDbType.Int).Value = (object?)configuration.PackageId ?? DBNull.Value;
        command.Parameters.Add("@folderId", SqlDbType.NVarChar, 255).Value = Db(configuration.AuditFolderId);
        command.Parameters.Add("@folderPath", SqlDbType.NVarChar, 1000).Value = Db(configuration.AuditFolderPath);
        await command.ExecuteNonQueryAsync(cancellationToken);
        return await GetProjectConfigurationAsync(projectIdAcc, cancellationToken);
    }

    public async Task<IReadOnlyList<ProjectAuditTemplate>> GetProjectTemplatesAsync(CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT PC.Project_ID_ACC, PC.PacoteOcrId, PO.Nome_Pacote
            FROM config.ProjetoConfiguracao PC
            LEFT JOIN config.PacoteOcr PO ON PO.PacoteOcrId=PC.PacoteOcrId
            WHERE PC.PacoteOcrId IS NOT NULL;
            """;
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var list = new List<ProjectAuditTemplate>();
        while (await reader.ReadAsync(cancellationToken))
            list.Add(new(reader.GetString(0), reader.GetInt32(1), reader.IsDBNull(2) ? null : reader.GetString(2)));
        return list;
    }

    public async Task<DocumentQualityProjectConfiguration> SaveProjectTemplateAsync(string projectIdAcc, int? packageId, CancellationToken cancellationToken = default)
    {
        if (packageId is <= 0) throw new ArgumentException("Template inválido.");
        await GetProjectConfigurationAsync(projectIdAcc, cancellationToken); // projeto precisa existir em dbo.Projetos
        const string sql = """
            MERGE config.ProjetoConfiguracao AS target
            USING (SELECT @projectId AS Project_ID_ACC) AS source
              ON target.Project_ID_ACC=source.Project_ID_ACC
            WHEN MATCHED THEN UPDATE SET PacoteOcrId=@packageId, Data_Atualizacao=SYSUTCDATETIME()
            WHEN NOT MATCHED THEN INSERT (Project_ID_ACC, PacoteOcrId) VALUES (@projectId, @packageId);
            """;
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.Add("@projectId", SqlDbType.NVarChar, 255).Value = projectIdAcc;
        command.Parameters.Add("@packageId", SqlDbType.Int).Value = (object?)packageId ?? DBNull.Value;
        await command.ExecuteNonQueryAsync(cancellationToken);
        return await GetProjectConfigurationAsync(projectIdAcc, cancellationToken);
    }

    private const string MigrationPendente =
        "Estrutura de campos/formatos do template ainda não existe no banco: aplique Migrations/Configuracao/07_document_quality_campos_formatos.sql.";

    private static async Task<bool> TableExistsAsync(SqlConnection connection, string name, CancellationToken cancellationToken)
    {
        await using var command = new SqlCommand("SELECT CASE WHEN OBJECT_ID(@name, N'U') IS NULL THEN 0 ELSE 1 END;", connection);
        command.Parameters.Add("@name", SqlDbType.NVarChar, 256).Value = name;
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken)) == 1;
    }

    public async Task<IReadOnlyList<TemplateField>?> GetTemplateFieldsAsync(int packageId, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync(cancellationToken);
        if (!await TableExistsAsync(connection, "config.PacoteOcrCampo", cancellationToken)) return null;
        await using var command = new SqlCommand(
            "SELECT CampoDestino, Ordem, Ativo FROM config.PacoteOcrCampo WHERE PacoteOcrId=@packageId ORDER BY Ativo DESC, Ordem, CampoDestino;", connection);
        command.Parameters.Add("@packageId", SqlDbType.Int).Value = packageId;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var list = new List<TemplateField>();
        while (await reader.ReadAsync(cancellationToken)) list.Add(new(reader.GetString(0), reader.GetInt32(1), reader.GetBoolean(2)));
        return list;
    }

    public async Task<IReadOnlyList<TemplateField>> SaveTemplateFieldsAsync(int packageId, IReadOnlyList<string> fields, CancellationToken cancellationToken = default)
    {
        if (fields.Count == 0) throw new ArgumentException("O template precisa auditar ao menos um campo.");
        if (fields.Count > 200 || fields.Any(f => !DocumentQualityFields.IsValidIdentifier(f))) throw new ArgumentException("Campo inválido.");
        if (fields.GroupBy(f => f, StringComparer.OrdinalIgnoreCase).Any(g => g.Count() > 1)) throw new ArgumentException("Campo repetido.");
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync(cancellationToken);
        if (!await TableExistsAsync(connection, "config.PacoteOcrCampo", cancellationToken)) throw new DocumentQualitySchemaPendingException(MigrationPendente);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(cancellationToken);
        await using (var deactivate = new SqlCommand("UPDATE config.PacoteOcrCampo SET Ativo=0, Data_Atualizacao=SYSUTCDATETIME() WHERE PacoteOcrId=@packageId AND Ativo=1;", connection, transaction))
        {
            deactivate.Parameters.Add("@packageId", SqlDbType.Int).Value = packageId;
            await deactivate.ExecuteNonQueryAsync(cancellationToken);
        }
        for (var i = 0; i < fields.Count; i++)
        {
            await using var upsert = new SqlCommand("""
                MERGE config.PacoteOcrCampo AS target
                USING (SELECT @packageId AS PacoteOcrId, @field AS CampoDestino) AS source
                  ON target.PacoteOcrId=source.PacoteOcrId AND target.CampoDestino=source.CampoDestino
                WHEN MATCHED THEN UPDATE SET Ordem=@order, Ativo=1, Data_Atualizacao=SYSUTCDATETIME()
                WHEN NOT MATCHED THEN INSERT (PacoteOcrId, CampoDestino, Ordem, Ativo) VALUES (@packageId, @field, @order, 1);
                """, connection, transaction);
            upsert.Parameters.Add("@packageId", SqlDbType.Int).Value = packageId;
            upsert.Parameters.Add("@field", SqlDbType.NVarChar, 100).Value = fields[i];
            upsert.Parameters.Add("@order", SqlDbType.Int).Value = i + 1;
            await upsert.ExecuteNonQueryAsync(cancellationToken);
        }
        await transaction.CommitAsync(cancellationToken);
        return (await GetTemplateFieldsAsync(packageId, cancellationToken))!;
    }

    public async Task<IReadOnlyList<FormatMapping>> GetFormatMappingsAsync(int packageId, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync(cancellationToken);
        if (!await TableExistsAsync(connection, "config.PacoteOcrFormato", cancellationToken)) return Array.Empty<FormatMapping>();
        await using var command = new SqlCommand(
            "SELECT ValorFormatoLdp, Modo, TipoFolha FROM config.PacoteOcrFormato WHERE PacoteOcrId=@packageId ORDER BY ValorFormatoLdp;", connection);
        command.Parameters.Add("@packageId", SqlDbType.Int).Value = packageId;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var list = new List<FormatMapping>();
        while (await reader.ReadAsync(cancellationToken)) list.Add(new(reader.GetString(0), reader.GetString(1), reader.IsDBNull(2) ? null : reader.GetString(2)));
        return list;
    }

    public async Task<IReadOnlyList<FormatMapping>> SaveFormatMappingsAsync(int packageId, IReadOnlyList<FormatMapping> mappings, CancellationToken cancellationToken = default)
    {
        ValidateMappings(mappings);
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync(cancellationToken);
        if (!await TableExistsAsync(connection, "config.PacoteOcrFormato", cancellationToken)) throw new DocumentQualitySchemaPendingException(MigrationPendente);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(cancellationToken);
        await using (var delete = new SqlCommand("DELETE FROM config.PacoteOcrFormato WHERE PacoteOcrId=@packageId;", connection, transaction))
        {
            delete.Parameters.Add("@packageId", SqlDbType.Int).Value = packageId;
            await delete.ExecuteNonQueryAsync(cancellationToken);
        }
        foreach (var m in mappings)
        {
            await using var insert = new SqlCommand(
                "INSERT INTO config.PacoteOcrFormato (PacoteOcrId, ValorFormatoLdp, Modo, TipoFolha) VALUES (@packageId, @value, @mode, @sheet);", connection, transaction);
            insert.Parameters.Add("@packageId", SqlDbType.Int).Value = packageId;
            insert.Parameters.Add("@value", SqlDbType.NVarChar, 50).Value = m.ValorFormatoLdp.Trim();
            insert.Parameters.Add("@mode", SqlDbType.NVarChar, 20).Value = m.Modo;
            insert.Parameters.Add("@sheet", SqlDbType.NVarChar, 50).Value = m.Modo == FormatMappingMode.Perfil ? m.TipoFolha!.Trim() : DBNull.Value;
            await insert.ExecuteNonQueryAsync(cancellationToken);
        }
        await transaction.CommitAsync(cancellationToken);
        return await GetFormatMappingsAsync(packageId, cancellationToken);
    }

    public static void ValidateMappings(IReadOnlyList<FormatMapping>? mappings)
    {
        if (mappings == null) throw new ArgumentException("Mapeamento inválido.");
        if (mappings.Count > 200) throw new ArgumentException("Mapeamentos demais.");
        if (mappings.Any(m => string.IsNullOrWhiteSpace(m.ValorFormatoLdp) || m.ValorFormatoLdp.Trim().Length > 50 || !FormatMappingMode.IsValid(m.Modo)))
            throw new ArgumentException("Mapeamento inválido: informe o valor da LDP e o modo (PERFIL, DIMENSAO ou IGNORAR).");
        if (mappings.Any(m => m.Modo == FormatMappingMode.Perfil && (string.IsNullOrWhiteSpace(m.TipoFolha) || m.TipoFolha.Trim().Length > 50)))
            throw new ArgumentException("Mapeamento para perfil exige o perfil calibrado.");
        if (mappings.GroupBy(m => m.ValorFormatoLdp.Trim(), StringComparer.OrdinalIgnoreCase).Any(g => g.Count() > 1))
            throw new ArgumentException("Valor da LDP repetido no mapeamento.");
    }

    public async Task<IReadOnlyList<string>> GetLdpFormatValuesAsync(int packageId, CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT DISTINCT LTRIM(RTRIM(L.Format)) FROM dbo.Ldp_Unificada L
            WHERE L.Format IS NOT NULL AND LTRIM(RTRIM(L.Format)) <> ''
              AND (NOT EXISTS (SELECT 1 FROM config.ProjetoConfiguracao PC WHERE PC.PacoteOcrId=@packageId)
                   OR L.Project_ID_ACC IN (SELECT PC.Project_ID_ACC FROM config.ProjetoConfiguracao PC WHERE PC.PacoteOcrId=@packageId))
            ORDER BY 1;
            """;
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(sql, connection) { CommandTimeout = 60 };
        command.Parameters.Add("@packageId", SqlDbType.Int).Value = packageId;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var list = new List<string>();
        while (await reader.ReadAsync(cancellationToken)) list.Add(reader.GetString(0));
        return list;
    }

    private static object Db(string? value) => string.IsNullOrWhiteSpace(value) ? DBNull.Value : value.Trim();

    private static string NormalizePackageName(string? name)
    {
        name = (name ?? string.Empty).Trim();
        if (name.Length is < 2 or > 200) throw new ArgumentException("O nome do template deve ter entre 2 e 200 caracteres.", nameof(name));
        return name;
    }

    private static void AddDecimal(SqlCommand command, string name, decimal value)
    {
        var parameter = command.Parameters.Add(name, SqlDbType.Decimal);
        parameter.Precision = 18; parameter.Scale = 6; parameter.Value = value;
    }

    public static void ValidateRegions(IReadOnlyList<SaveOcrTemplateRegion>? regions)
    {
        if (regions == null || regions.Count == 0) throw new ArgumentException("Informe ao menos uma região.");
        if (regions.Count > 200) throw new ArgumentException("Quantidade de regiões inválida.");
        if (regions.Any(r => !DocumentQualityFields.IsValidIdentifier(r.CampoDestino))) throw new ArgumentException("Campo de destino inválido.");
        if (regions.GroupBy(r => r.CampoDestino, StringComparer.OrdinalIgnoreCase).Any(g => g.Count() > 1)) throw new ArgumentException("Campo de destino repetido.");
        if (regions.Any(r => r.Pagina < 1 || r.ReferenceWidth <= 0 || r.ReferenceHeight <= 0 || r.Width <= 0 || r.Height <= 0 || r.X < 0 || r.Y < 0
            || r.X + r.Width > r.ReferenceWidth + 0.01m || r.Y + r.Height > r.ReferenceHeight + 0.01m))
            throw new ArgumentException("Coordenadas fora dos limites da página de referência.");
    }
}
