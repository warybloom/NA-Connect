using APS.WebApp.Services.BiChs001Schedule;

namespace NA_Connect.BiChs001.Tests;

/// <summary>Leitor MPXJ isolado sobre a cópia do .mpp oficial (v47, status 08/10/2026) — fatos conferidos contra o PBIX.</summary>
public sealed class MppReaderTests
{
    private static readonly Lazy<BiLeituraCronograma> Leitura = new(() => BiChs001MppReader.Ler(Ambiente.CaminhoMpp));

    [Fact]
    public void LeTodasAsTarefasComHierarquiaConsistente()
    {
        var l = Leitura.Value;
        Assert.Equal(883, l.Tarefas.Count);
        Assert.Equal(755, l.Tarefas.Count(t => !t.EhResumo));
        Assert.Equal(new DateTime(2026, 10, 8, 17, 0, 0), l.DataStatus);
        var porUid = l.Tarefas.ToDictionary(t => t.UidTarefa);
        Assert.Single(l.Tarefas, t => t.UidTarefaPai is null); // só a tarefa de resumo do projeto
        foreach (var t in l.Tarefas)
        {
            // Pai existente, sem ciclo, nível = nível do pai + 1.
            var vistos = new HashSet<int>();
            for (var a = t; a.UidTarefaPai is int pai; a = porUid[pai])
            {
                Assert.True(vistos.Add(a.UidTarefa), $"ciclo em {t.UidTarefa}");
                Assert.True(porUid.ContainsKey(pai), $"pai {pai} ausente");
                Assert.Equal(porUid[pai].NivelEstrutura + 1, a.NivelEstrutura);
            }
        }
    }

    [Fact]
    public void FolhasTemCamposNativosQueOPbixUsa()
    {
        var folhas = Leitura.Value.Tarefas.Where(t => !t.EhResumo).ToList();
        Assert.All(folhas, f =>
        {
            Assert.NotNull(f.Inicio); Assert.NotNull(f.Termino); Assert.NotNull(f.InicioLinhaBase); Assert.NotNull(f.TerminoLinhaBase);
            Assert.NotNull(f.TrabalhoHoras); Assert.NotNull(f.TrabalhoLinhaBaseHoras); Assert.NotNull(f.TrabalhoRealHoras);
            Assert.False(string.IsNullOrEmpty(f.GuidTarefa));
        });
        Assert.Equal(10418.74m, Math.Round(folhas.Sum(f => f.TrabalhoHoras ?? 0), 2));
    }

    [Fact]
    public void PreservaOValorBrutoDoMpp_SemCorrigirOTypo()
    {
        var t = Leitura.Value.Tarefas.Single(x => x.UidTarefa == 3917);
        Assert.Equal("Emgineering - 30%", t.Nome); // a correspondência é só na camada do BI (SQL), nunca no leitor
        Assert.Equal("1.5", t.Edt);
        Assert.True(t.EhResumo);
    }

    [Fact]
    public void SerieDiariaPorDia_SomaDasAtribuicoes_ConciliaComOTrabalhoRealDaTarefa()
    {
        var l = Leitura.Value;
        Assert.Equal(16024, l.TrabalhoDiario.Count);
        Assert.All(l.TrabalhoDiario, d => Assert.Equal(TimeSpan.Zero, d.Data.TimeOfDay));
        Assert.Equal(l.TrabalhoDiario.Count, l.TrabalhoDiario.Select(d => (d.UidTarefa, d.Data)).Distinct().Count());
        Assert.Equal(0, l.Diagnostico.FolhasComTrabalhoRealSemSerie);
        Assert.Equal(0, l.Diagnostico.FolhasComSerieDivergenteDoTotalReal);
        Assert.Equal(0, l.Diagnostico.BlocosSemHoraUtil);
        Assert.True(Math.Abs(l.Diagnostico.SomaSerieReal - l.Diagnostico.SomaTrabalhoRealFolhas) < 1m);
        // Calendário Standard: nenhum trabalho real em sábado/domingo.
        Assert.DoesNotContain(l.TrabalhoDiario, d => (d.TrabalhoRealHoras ?? 0) > 0 && d.Data.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday);
    }

    [Fact]
    public void NaoAlteraOArquivoLido()
    {
        string antes = BiChs001MppReader.Sha256(Ambiente.CaminhoMpp);
        _ = BiChs001MppReader.Ler(Ambiente.CaminhoMpp);
        Assert.Equal(antes, BiChs001MppReader.Sha256(Ambiente.CaminhoMpp));
    }
}
