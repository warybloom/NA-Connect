using System.Text.Json;
using Microsoft.Data.SqlClient;

// Standalone read-only audit. Never starts the application, hosted services or Worker.
if (args.Length != 2)
{
    Console.Error.WriteLine("Usage: audit <appsettings.json> <output.json>");
    return 2;
}
try
{
    using var config = JsonDocument.Parse(File.ReadAllText(args[0]), new JsonDocumentOptions
        { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
    var builder = new SqlConnectionStringBuilder(config.RootElement.GetProperty("ConnectionStrings").GetProperty("LocalConnection").GetString())
        { ConnectTimeout = 5, ApplicationName = "WeeklyPlanning.ReadOnlyAudit" };
    await using var connection = new SqlConnection(builder.ConnectionString);
    await connection.OpenAsync();
    const string query = """
        SELECT s.name AS SchemaName,o.name AS ObjectName,o.type_desc,c.column_id,c.name AS ColumnName,
          t.name AS DataType,c.max_length,c.precision,c.scale,c.is_nullable,c.is_identity
        FROM sys.objects o JOIN sys.schemas s ON s.schema_id=o.schema_id
        LEFT JOIN sys.columns c ON c.object_id=o.object_id
        LEFT JOIN sys.types t ON t.user_type_id=c.user_type_id
        WHERE o.name IN ('tb_WeeklyEngineeringSchedule','Cronograma_Projetos','Ldp_Unificada',
          'VW_Ldp_Unificada_Ultima_Versao','VW_Ldp_Unificada','Projetos')
        ORDER BY o.name,c.column_id;
        """;
    using var command = new SqlCommand(query, connection) { CommandTimeout = 15 };
    await using var reader = await command.ExecuteReaderAsync();
    var rows = new List<Dictionary<string, object?>>();
    while (await reader.ReadAsync())
    {
        var row = new Dictionary<string, object?>();
        for (var i = 0; i < reader.FieldCount; i++) row[reader.GetName(i)] = reader.IsDBNull(i) ? null : reader.GetValue(i);
        rows.Add(row);
    }
    File.WriteAllText(args[1], JsonSerializer.Serialize(new { status = "READ_ONLY_SUCCESS", at = DateTimeOffset.UtcNow, rows }, new JsonSerializerOptions { WriteIndented = true }));
    Console.WriteLine($"Read-only schema rows: {rows.Count}");
    return 0;
}
catch (Exception ex)
{
    // Never print a connection string, endpoint, credentials, stack trace or config contents.
    var result = new { status = "READ_ONLY_AUDIT_FAILED", at = DateTimeOffset.UtcNow,
        errorType = ex.GetType().Name, sqlNumber = ex is SqlException sql ? (int?)sql.Number : null };
    File.WriteAllText(args[1], JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
    Console.Error.WriteLine(JsonSerializer.Serialize(result));
    return 1;
}
