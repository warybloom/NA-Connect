namespace APS.WebApp.Services.BiChs001Schedule;

/// <summary>
/// Sincronização do BI CHS001: localiza o .mpp mais recente na pasta oficial do ACC (mesmos caminhos e mesmos métodos públicos
/// do <c>CronogramaTipo2Service</c>, que NÃO é chamado nem alterado), baixa uma CÓPIA temporária, lê com
/// <see cref="BiChs001MppReader"/> e grava uma nova versão no schema bi_chs001. Só leitura no ACC (nenhum upload/versão nova).
/// Token: 2-legged da aplicação, o mesmo usado pelo robô (AutodeskApiService.Get2LeggedTokenAsync).
/// </summary>
public sealed class BiChs001ScheduleSyncService(AutodeskApiService autodeskApi, BiChs001ScheduleRepository repository, ILogger logger)
{
    /// <summary>Caminhos da pasta de cronograma — idênticos aos do CronogramaTipo2Service (PT e EN).</summary>
    public static readonly string[][] CaminhosCronograma =
    [
        ["01 - PLANEJAMENTO E CONTROLE", "01.03 - CRONOGRAMA FÍSICO, CURVAS DE AVANÇO"],
        ["01 - PLANNING & CONTROL", "01.03 - PROJECT SCHEDULE & PROGRESS CURVES"],
    ];

    /// <param name="guardarCopiaEm">Opcional (CLI de auditoria): pasta onde guardar a cópia baixada, nomeada pelo SHA-256.</param>
    public async Task<BiResultadoSincronizacao> SincronizarDoAccAsync(string projectId, string? executadoPor, bool gravar, CancellationToken ct,
        string? guardarCopiaEm = null)
    {
        var r = new BiResultadoSincronizacao();
        if (!BiChs001Escopo.ProjetoPermitido(projectId)) { r.Erros.Add("Projeto fora do escopo do BI CHS001."); return r; }
        if (gravar && !await repository.SchemaExisteAsync(ct)) { r.Erros.Add("Schema bi_chs001 não instalado (Migrations/BiChs001/001_bi_chs001_UP.sql)."); return r; }

        string? hub = await repository.ObterHubAsync(projectId, ct);
        if (string.IsNullOrWhiteSpace(hub)) { r.Erros.Add("Hub do projeto não encontrado em dbo.Projetos/Hubs_Conta (projeto ativo)."); return r; }

        string copia = Path.Combine(Path.GetTempPath(), $"BI_CHS001_{Guid.NewGuid():N}.mpp");
        try
        {
            string token = await autodeskApi.Get2LeggedTokenAsync();
            string? pasta = null;
            foreach (var caminho in CaminhosCronograma)
            {
                try { pasta = await autodeskApi.FindFolderIdByPathAsync(hub, projectId, caminho, token); }
                catch (Exception ex) { logger.LogWarning("[BI CHS001] Caminho {Caminho} indisponível: {Erro}", string.Join(" / ", caminho), ex.Message); }
                if (!string.IsNullOrEmpty(pasta)) break;
            }
            if (string.IsNullOrEmpty(pasta)) { r.Erros.Add("Pasta de cronograma não encontrada no ACC."); return r; }

            var (fileId, fileName) = await autodeskApi.FindItemIdByNamePartAsync(projectId, pasta, ".mpp", token);
            if (string.IsNullOrEmpty(fileId)) { r.Erros.Add("Arquivo .mpp não localizado na pasta de cronograma."); return r; }
            var detalhes = await autodeskApi.GetItemDetailsAsync(projectId, fileId, token);
            r.ArquivoNome = detalhes?.DisplayName ?? fileName ?? "cronograma.mpp";
            r.ArquivoVersaoUrn = detalhes?.TipVersionUrn ?? fileId;

            await using (var dl = await autodeskApi.DownloadFileStreamAsync(projectId, r.ArquivoVersaoUrn, token))
            await using (var fs = new FileStream(copia, FileMode.CreateNew, FileAccess.Write))
                await dl.CopyToAsync(fs, ct);

            var leitura = BiChs001MppReader.Ler(copia, r.ArquivoNome);
            Preencher(r, leitura);
            if (!string.IsNullOrEmpty(guardarCopiaEm))
            {
                Directory.CreateDirectory(guardarCopiaEm);
                string destino = Path.Combine(guardarCopiaEm, $"{Path.GetFileNameWithoutExtension(r.ArquivoNome)}_{leitura.ArquivoSha256[..12]}.mpp");
                if (!File.Exists(destino)) File.Copy(copia, destino);
            }
            if (!gravar) { r.Sucesso = true; return r; }
            var (versaoId, jaExistia) = await repository.PersistirAsync(projectId, leitura,
                new BiChs001ScheduleRepository.Origem("ACC", fileId, r.ArquivoVersaoUrn, executadoPor), ct);
            r.VersaoId = versaoId; r.JaExistia = jaExistia; r.Sucesso = true;
            logger.LogInformation("[BI CHS001] Versão {Versao} ({Estado}) | {Arquivo} | sha {Sha} | tarefas {T} | dias {D}",
                versaoId, jaExistia ? "já existia" : "gravada", r.ArquivoNome, r.ArquivoSha256, r.Tarefas, r.LinhasDiarias);
            return r;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "[BI CHS001] Falha na sincronização isolada do cronograma.");
            r.Erros.Add($"Falha ao localizar/baixar/ler/gravar o cronograma: {ex.Message}");
            return r;
        }
        finally
        {
            try { if (File.Exists(copia)) File.Delete(copia); } catch { /* cópia temporária */ }
        }
    }

    /// <summary>Lê um .mpp local (cópia) — usado em testes/conciliação. Nunca altera o arquivo de origem: lê uma cópia temporária.</summary>
    public async Task<BiResultadoSincronizacao> SincronizarDeArquivoAsync(string projectId, string caminhoMpp, string? executadoPor, bool gravar, CancellationToken ct)
    {
        var r = new BiResultadoSincronizacao();
        if (!BiChs001Escopo.ProjetoPermitido(projectId)) { r.Erros.Add("Projeto fora do escopo do BI CHS001."); return r; }
        string copia = Path.Combine(Path.GetTempPath(), $"BI_CHS001_{Guid.NewGuid():N}.mpp");
        try
        {
            File.Copy(caminhoMpp, copia);
            var leitura = BiChs001MppReader.Ler(copia, Path.GetFileName(caminhoMpp));
            Preencher(r, leitura);
            r.ArquivoNome = Path.GetFileName(caminhoMpp);
            if (!gravar) { r.Sucesso = true; return r; }
            var (versaoId, jaExistia) = await repository.PersistirAsync(projectId, leitura, new BiChs001ScheduleRepository.Origem("ARQUIVO", null, null, executadoPor), ct);
            r.VersaoId = versaoId; r.JaExistia = jaExistia; r.Sucesso = true;
            return r;
        }
        finally
        {
            try { if (File.Exists(copia)) File.Delete(copia); } catch { }
        }
    }

    private static void Preencher(BiResultadoSincronizacao r, BiLeituraCronograma leitura)
    {
        r.ArquivoSha256 = leitura.ArquivoSha256;
        r.Tarefas = leitura.Tarefas.Count;
        r.LinhasDiarias = leitura.TrabalhoDiario.Count;
        r.Diagnostico = leitura.Diagnostico;
    }
}
