using Microsoft.Data.SqlClient;

namespace NA_Connect.BiChs001.Tests;

/// <summary>Recursos externos dos testes: cópia do .mpp e banco de TESTE (nunca o banco de produção).</summary>
internal static class Ambiente
{
    public const string ProjetoChs001 = "b.fcdb7ebd-1b23-440d-a04b-90391098522e";

    /// <summary>Cópia do .mpp (v47, baixada do ACC em 15/10/2026). Sobrescrever com BI_CHS001_MPP.</summary>
    public static string CaminhoMpp =>
        Environment.GetEnvironmentVariable("BI_CHS001_MPP")
        ?? Directory.EnumerateFiles(@"C:\Dev\N-A-Connect aa\_backups\bi_chs001_isolado_20261015\mpp_acc", "*.mpp").OrderBy(x => x).FirstOrDefault()
        ?? throw new InvalidOperationException("Cópia do .mpp não encontrada: defina BI_CHS001_MPP.");

    public static string BancoTeste => Environment.GetEnvironmentVariable("BI_CHS001_TEST_DB") ?? "DB_NA_CONNECT_BI_TESTE";

    public static string ConexaoTeste
    {
        get
        {
            if (string.Equals(BancoTeste, "DB_NA_CONNECT", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Os testes NUNCA rodam no banco de produção.");
            var cs = new SqlConnectionStringBuilder
            {
                DataSource = @".\SQLEXPRESS", InitialCatalog = BancoTeste, IntegratedSecurity = true, Encrypt = false, TrustServerCertificate = true,
            }.ConnectionString;
            using var cn = new SqlConnection(cs);
            try { cn.Open(); }
            catch (SqlException ex)
            {
                throw new InvalidOperationException(
                    $"Banco de teste '{BancoTeste}' indisponível ({ex.Message}). Prepare: RESTORE do backup como clone e rode Migrations/BiChs001/001_bi_chs001_UP.sql nele.", ex);
            }
            return cs;
        }
    }

    public static object? Escalar(string sql, params (string, object)[] p)
    {
        using var cn = new SqlConnection(ConexaoTeste);
        cn.Open();
        using var cmd = new SqlCommand(sql, cn);
        foreach (var (n, v) in p) cmd.Parameters.AddWithValue(n, v);
        return cmd.ExecuteScalar();
    }
}
