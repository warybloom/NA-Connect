using APS.WebApp.Services.BiChs001Schedule;
using Microsoft.Data.SqlClient;

namespace NA_Connect.BiChs001.Tests;

/// <summary>Persistência por versão no schema bi_chs001 (banco de TESTE): atômica, idempotente, isolada por projeto, alias restrito.</summary>
public sealed class SnapshotSqlTests
{
    private static BiLeituraCronograma Sintetica(string sha, bool duplicarUid = false)
    {
        var l = new BiLeituraCronograma { ArquivoNome = "teste.mpp", ArquivoSha256 = sha, ArquivoBytes = 1, LidoEmUtc = DateTime.UtcNow };
        l.Tarefas.Add(new BiTarefa { Ordem = 1, UidTarefa = 0, Nome = "Projeto", EhResumo = true, NivelEstrutura = 0 });
        l.Tarefas.Add(new BiTarefa { Ordem = 2, UidTarefa = 3917, UidTarefaPai = 0, Nome = "Emgineering - 30%", EhResumo = true, NivelEstrutura = 1 });
        l.Tarefas.Add(new BiTarefa { Ordem = 3, UidTarefa = 10, UidTarefaPai = 3917, Nome = "Emgineering - 30%", EhResumo = false, NivelEstrutura = 2, TrabalhoHoras = 8 });
        if (duplicarUid) l.Tarefas.Add(new BiTarefa { Ordem = 4, UidTarefa = 10, UidTarefaPai = 3917, Nome = "dup", NivelEstrutura = 2 });
        l.TrabalhoDiario.Add(new BiTrabalhoDia { UidTarefa = 10, Data = new DateTime(2026, 10, 5), TrabalhoRealHoras = 8 });
        l.Diagnostico.TotalTarefas = l.Tarefas.Count;
        return l;
    }

    private static void Apagar(string sha) => Ambiente.Escalar("DELETE FROM bi_chs001.Versao_Cronograma WHERE Arquivo_Sha256 = @s", ("@s", sha));
    private static string ShaTeste() => ("teste" + Guid.NewGuid().ToString("N")).PadRight(64, '0')[..64];

    [Fact]
    public async Task GravaVersaoCompleta_EIdempotentePorSha()
    {
        string sha = ShaTeste();
        var repo = new BiChs001ScheduleRepository(Ambiente.ConexaoTeste);
        try
        {
            var (v1, ja1) = await repo.PersistirAsync(Ambiente.ProjetoChs001, Sintetica(sha), new("ARQUIVO", null, null, "teste"), default);
            var (v2, ja2) = await repo.PersistirAsync(Ambiente.ProjetoChs001, Sintetica(sha), new("ARQUIVO", null, null, "teste"), default);
            Assert.False(ja1);
            Assert.True(ja2);
            Assert.Equal(v1, v2);
            Assert.Equal(3, (int)Ambiente.Escalar("SELECT COUNT(*) FROM bi_chs001.Tarefa_Snapshot WHERE Versao_Id = @v", ("@v", v1))!);
            Assert.Equal(1, (int)Ambiente.Escalar("SELECT COUNT(*) FROM bi_chs001.Trabalho_Tarefa_Dia WHERE Versao_Id = @v", ("@v", v1))!);
        }
        finally { Apagar(sha); }
    }

    [Fact]
    public async Task FalhaNoMeioDaGravacao_NaoDeixaNadaParcial()
    {
        string sha = ShaTeste();
        var repo = new BiChs001ScheduleRepository(Ambiente.ConexaoTeste);
        try
        {
            await Assert.ThrowsAnyAsync<SqlException>(() => repo.PersistirAsync(Ambiente.ProjetoChs001, Sintetica(sha, duplicarUid: true), new("ARQUIVO", null, null, "teste"), default));
            Assert.Equal(0, (int)Ambiente.Escalar("SELECT COUNT(*) FROM bi_chs001.Versao_Cronograma WHERE Arquivo_Sha256 = @s", ("@s", sha))!);
        }
        finally { Apagar(sha); }
    }

    [Fact]
    public async Task RecusaOutroProjeto()
    {
        var repo = new BiChs001ScheduleRepository(Ambiente.ConexaoTeste);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            repo.PersistirAsync("b.00000000-0000-0000-0000-000000000001", Sintetica(ShaTeste()), new("ARQUIVO", null, null, "teste"), default));
    }

    [Fact]
    public async Task Alias_SoNoCHS001_SoEmResumo_GrafiaExata_PreservandoOBruto()
    {
        string sha = ShaTeste();
        var repo = new BiChs001ScheduleRepository(Ambiente.ConexaoTeste);
        const string outro = "b.00000000-0000-0000-0000-000000000002";
        try
        {
            var (v, _) = await repo.PersistirAsync(Ambiente.ProjetoChs001, Sintetica(sha), new("ARQUIVO", null, null, "teste"), default);
            string Nome(int versao, int uid) => (string)Ambiente.Escalar("SELECT Nome_BI FROM bi_chs001.VW_Tarefa WHERE Versao_Id = @v AND UID_Tarefa = @u", ("@v", versao), ("@u", uid))!;
            Assert.Equal("Engineering - 30%", Nome(v, 3917)); // resumo do CHS001 → alias
            Assert.Equal("Emgineering - 30%", Nome(v, 10));   // folha com o mesmo texto → NÃO (Somente_Resumo)
            Assert.Equal("Emgineering - 30%", (string)Ambiente.Escalar("SELECT Nome_Original FROM bi_chs001.VW_Tarefa WHERE Versao_Id = @v AND UID_Tarefa = 3917", ("@v", v))!);
            // Outro projeto (gravado direto no banco de teste, o repositório recusaria) → sem alias.
            int vo = Convert.ToInt32(Ambiente.Escalar("""
                INSERT INTO bi_chs001.Versao_Cronograma (Project_ID_ACC, Origem, Arquivo_Nome, Arquivo_Sha256, Arquivo_Bytes, Leitor, Lido_Em_Utc, Total_Tarefas, Total_Folhas, Total_Linhas_Diarias, Total_Atribuicoes)
                OUTPUT INSERTED.Versao_Id VALUES (@p, N'ARQUIVO', N'x.mpp', @s, 1, N'teste', SYSUTCDATETIME(), 1, 0, 0, 0)
                """, ("@p", outro), ("@s", sha)));
            Ambiente.Escalar("INSERT INTO bi_chs001.Tarefa_Snapshot (Versao_Id, Project_ID_ACC, UID_Tarefa, Ordem, Eh_Resumo, Eh_Marco, Nome, Qtde_Atribuicoes) VALUES (@v, @p, 3917, 1, 1, 0, N'Emgineering - 30%', 0), (@v, @p, 3918, 2, 1, 0, N'emgineering - 30%', 0)",
                ("@v", vo), ("@p", outro));
            Assert.Equal("Emgineering - 30%", Nome(vo, 3917));
            // Grafia diferente (minúsculas) no CHS001 também não recebe alias (comparação exata, sensível a maiúsculas).
            Ambiente.Escalar("UPDATE bi_chs001.Tarefa_Snapshot SET Nome = N'emgineering - 30%' WHERE Versao_Id = @v AND UID_Tarefa = 3917", ("@v", v));
            Assert.Equal("emgineering - 30%", Nome(v, 3917));
        }
        finally
        {
            Ambiente.Escalar("DELETE FROM bi_chs001.Versao_Cronograma WHERE Arquivo_Sha256 = @s", ("@s", sha));
        }
    }

    [Fact]
    public void SemanaDaView_IniciaNoDomingo_IndependenteDeDateFirst()
    {
        var domingo = (DateTime)Ambiente.Escalar("SET DATEFIRST 1; SELECT CONVERT(datetime, CONVERT(date, DATEADD(DAY, (DATEDIFF(DAY, '19000107', '2026-10-10') / 7) * 7, '19000107')))")!;
        Assert.Equal(new DateTime(2026, 10, 4), domingo); // sábado 10/10 → domingo 04/10 (mesma regra do AssignmentByDay do PBIX)
        var dom2 = (DateTime)Ambiente.Escalar("SELECT CONVERT(datetime, CONVERT(date, DATEADD(DAY, (DATEDIFF(DAY, '19000107', '2026-10-11') / 7) * 7, '19000107')))")!;
        Assert.Equal(new DateTime(2026, 10, 11), dom2);
    }

    [Fact]
    public void ObjetosDaMigracao_SoNoSchemaBi()
    {
        var sql = File.ReadAllText(Path.Combine(RaizRepo(), "Migrations", "BiChs001", "001_bi_chs001_UP.sql"));
        var down = File.ReadAllText(Path.Combine(RaizRepo(), "Migrations", "BiChs001", "001_bi_chs001_DOWN.sql"));
        foreach (var texto in new[] { sql, down })
        {
            Assert.DoesNotContain("ALTER TABLE dbo", texto, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("UPDATE dbo", texto, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("DELETE FROM dbo", texto, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("DROP TABLE dbo", texto, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("VIEW dbo", texto, StringComparison.OrdinalIgnoreCase);
        }
        Assert.Equal(1, (int)Ambiente.Escalar("SELECT COUNT(*) FROM bi_chs001.Migracao WHERE Versao = N'001'")!);
    }

    internal static string RaizRepo()
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d != null; d = d.Parent)
            if (File.Exists(Path.Combine(d.FullName, "AccIntegration.WebApp.csproj"))) return d.FullName;
        return Environment.GetEnvironmentVariable("BI_CHS001_REPO") ?? throw new InvalidOperationException("Raiz do repositório não encontrada (BI_CHS001_REPO).");
    }
}
