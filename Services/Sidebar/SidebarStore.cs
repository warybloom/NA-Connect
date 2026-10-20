using System.Data;
using System.Text.Json;
using Microsoft.Data.SqlClient;
namespace APS.WebApp.Services.Sidebar;
public interface ISidebarStore
{
    Task<SidebarConfiguration> ReadAsync(CancellationToken ct);
    Task<IReadOnlyList<SidebarHistory>> HistoryAsync(CancellationToken ct);
    Task<SidebarConfiguration?> RevisionAsync(long revision, CancellationToken ct);
    Task<SidebarConfiguration> PublishAsync(IReadOnlyList<SidebarItem> items, long expectedRevision, string author, long? restoredFrom, CancellationToken ct);
}
// No schema creation or migrations at runtime. LocalConnection must point to DB_NA_CONNECT.
public sealed class SqlSidebarStore(IConfiguration configuration) : ISidebarStore
{
    private async Task<SqlConnection> Open(CancellationToken ct)
    {
        var connection = new SqlConnection(configuration.GetConnectionString("LocalConnection") ?? throw new InvalidOperationException("Conexão indisponível."));
        try { await connection.OpenAsync(ct); if(!string.Equals(connection.Database,"DB_NA_CONNECT",StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Banco de sidebar inválido."); return connection; }
        catch { await connection.DisposeAsync(); throw; }
    }
    private static SidebarConfiguration Parse(long revision, string json)
    {
        var items = JsonSerializer.Deserialize<SidebarItem[]>(json, SidebarRules.Json)
            ?? throw new ArgumentException("Configuração ausente.");
        // Migração ADITIVA de catálogo: nunca atualizar a configuração publicada.
        // Preservar todas as personalizações e adicionar em memória apenas itens oficiais novos.
        var existing=items.Select(x=>x.Id).ToHashSet(StringComparer.Ordinal);
        foreach(var fresh in SidebarRules.Catalog.Where(x=>!existing.Contains(x.Id))) {
            var nextOrder=items.Where(x=>x.ParentId==fresh.ParentId)
                .Select(x=>x.Order).DefaultIfEmpty(-1).Max()+1;
            items=items.Append(new SidebarItem(fresh.Id,fresh.ParentId,
                nextOrder,fresh.Label,fresh.Icon,fresh.Hidden)).ToArray();
            existing.Add(fresh.Id);
        }
        return new(revision, SidebarRules.Validate(items));
    }
    public async Task<SidebarConfiguration> ReadAsync(CancellationToken ct)
    {
        await using var c=await Open(ct);
        using var cmd=new SqlCommand("SELECT Revision, ItemsJson FROM dbo.SidebarOfficial WHERE SingletonId=1",c);
        await using var r=await cmd.ExecuteReaderAsync(ct);
        return await r.ReadAsync(ct) ? Parse(r.GetInt64(0),r.GetString(1)) : SidebarRules.Default;
    }
    public async Task<IReadOnlyList<SidebarHistory>> HistoryAsync(CancellationToken ct)
    {
        await using var c=await Open(ct);
        using var cmd=new SqlCommand("SELECT TOP (100) Revision, UpdatedAt, UpdatedBy, RestoredFrom FROM dbo.SidebarOfficialHistory ORDER BY Revision DESC",c);
        await using var r=await cmd.ExecuteReaderAsync(ct);
        var rows=new List<SidebarHistory>();
        while(await r.ReadAsync(ct)) rows.Add(new(r.GetInt64(0),r.GetDateTime(1),r.GetString(2),r.IsDBNull(3)?null:r.GetInt64(3)));
        return rows;
    }
    public async Task<SidebarConfiguration?> RevisionAsync(long revision,CancellationToken ct)
    {
        if(revision==0) return SidebarRules.Default;
        await using var c=await Open(ct);
        using var cmd=new SqlCommand("SELECT ItemsJson FROM dbo.SidebarOfficialHistory WHERE Revision=@revision",c);
        cmd.Parameters.Add("@revision",SqlDbType.BigInt).Value=revision;
        return await cmd.ExecuteScalarAsync(ct) is string json ? Parse(revision,json) : null;
    }
    public async Task<SidebarConfiguration> PublishAsync(IReadOnlyList<SidebarItem> items,long expectedRevision,string author,long? restoredFrom,CancellationToken ct)
    {
        SidebarRules.Validate(items);
        if(expectedRevision<0) throw new ArgumentException("Revisão inválida.");
        await using var c=await Open(ct);
        await using var tx=(SqlTransaction)await c.BeginTransactionAsync(IsolationLevel.Serializable,ct);
        using var current=new SqlCommand("SELECT Revision FROM dbo.SidebarOfficial WITH (UPDLOCK,HOLDLOCK) WHERE SingletonId=1",c,tx);
        var value=await current.ExecuteScalarAsync(ct);
        var revision=value is long v?v:0;
        if(revision!=expectedRevision) throw new SidebarConflictException();
        var next=checked(revision+1);
        const string sql="""
            INSERT INTO dbo.SidebarOfficialHistory(Revision,ItemsJson,UpdatedAt,UpdatedBy,RestoredFrom) VALUES(@revision,@json,SYSUTCDATETIME(),@author,@restored);
            IF EXISTS(SELECT 1 FROM dbo.SidebarOfficial WHERE SingletonId=1)
                UPDATE dbo.SidebarOfficial SET Revision=@revision,ItemsJson=@json,UpdatedAt=SYSUTCDATETIME(),UpdatedBy=@author WHERE SingletonId=1;
            ELSE INSERT INTO dbo.SidebarOfficial(SingletonId,Revision,ItemsJson,UpdatedAt,UpdatedBy) VALUES(1,@revision,@json,SYSUTCDATETIME(),@author);
            """;
        using var cmd=new SqlCommand(sql,c,tx);
        cmd.Parameters.Add("@revision",SqlDbType.BigInt).Value=next;
        cmd.Parameters.Add("@json",SqlDbType.NVarChar,-1).Value=JsonSerializer.Serialize(items,SidebarRules.Json);
        cmd.Parameters.Add("@author",SqlDbType.NVarChar,320).Value=author;
        cmd.Parameters.Add("@restored",SqlDbType.BigInt).Value=(object?)restoredFrom??DBNull.Value;
        await cmd.ExecuteNonQueryAsync(ct); await tx.CommitAsync(ct);
        return new(next,items);
    }
}
