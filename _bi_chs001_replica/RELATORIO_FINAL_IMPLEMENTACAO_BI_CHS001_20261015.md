# RELATÓRIO FINAL — BI CHS001 ISOLADO — 15/10/2026 (22:30 → 23:45)

Sessão: `BI_CHS001_IMPL_ISOLADO_20261015` (Claude). Instrução executada: `EXECUTE_AGORA_BI_ISOLADO_20261015.md` + `DIRETRIZ_ISOLAMENTO_OBRIGATORIA_20261015.md`.

## 0. Resumo em 10 linhas

1. **Pipeline novo e isolado pronto e com dados reais gravados:** leitor MPXJ (`Services/BiChs001Schedule`) leu uma CÓPIA do .mpp oficial mais recente do ACC (**v47**, `CHS001-000-PMO-SC-001.mpp`, status **08/10/2026 17:00**, SHA-256 `ab13ca43…f8d3d1`) e gravou a **versão 1** no schema novo `bi_chs001`: 883 tarefas, 755 folhas, 16.024 linhas diárias.
2. **SQL só em schema novo** (`bi_chs001`): backup `.bak` verificado antes, UP/DOWN idempotentes testados num clone restaurado, depois aplicado. Esquema fora de `bi_chs001`: **642 objetos / 2.403 colunas, checksums idênticos antes e depois**.
3. **Conciliação com o PBIX** (mesmo corte 08/10): 755/755 folhas casadas por GUID; Início/Término/LB **100% idênticos**; série semanal de trabalho real **2.010/2.010 pares do PBIX iguais a 2 casas**, todos no domingo; gates **196 / 347 / 190** com o alias.
4. **Typo "Emgineering - 30%"**: alias explícito e auditável (`bi_chs001.Alias_Rotulo`), só CHS001, só tarefa de resumo, grafia exata; valor bruto preservado; .mpp e importador intocados.
5. **API dedicada** `api/bi-chs001/schedule/*` (status, tarefas, trabalho-diario, trabalho-semanal, POST sincronizar) com permissões reutilizadas e restrição ao CHS001.
6. **Frontend**: Resumos 30/60/90 (cartões ACC/BASE SEM, curvas S, matriz hierárquica Level 4–7), curvas 3D, lista de semanas (dSemana) agora calculados com dados reais; correção de um bug real de filtro que zerava as curvas 3D.
7. **Testes**: TS 46/46, .NET dedicados 22/22, `tsc` 0 erros, `vite build` ok, backend 0 erros.
8. **⚠ Deploy do BACKEND NÃO executado**: o classificador de segurança do Claude Code bloqueou a troca do DLL/reinício (categoria "Production Deploy"). Script pronto, testado em preflight lógico, com rollback → **comando na §9**. Até lá, a tela usa automaticamente a fonte legada (sem regressão).
9. **⚠ Build do checkout principal está quebrado** por arquivos em `_bi_chs001_replica` (ver §8.1) — não afeta o binário em execução, afeta o próximo `dotnet run`.
10. Validação visual: sem sessão autenticada no navegador (não digitei senha). Capturas feitas na pré-visualização de dev com as linhas reais exportadas pelo mesmo SQL da API (§6). Auditoria Codex lançada em modo somente leitura (§10).

## 1. Status real das 10 telas (dados reais = versão 1 do bi_chs001, .mpp v47)

Valores calculados pelo MOTOR DA TELA sobre as linhas reais exportadas pelo SQL dos endpoints (`valores_reais_10_telas_20261015.json`, ferramenta `tools/valores_reais.mjs`). "= PBIX" = idêntico ao cálculo sobre o cache do PBIX.

| # | Tela | Situação com dados reais | Evidência / diferença vs PBIX (cache 08/10) |
|---|---|---|---|
| 1 | Resumo 30% | **Funciona com dados reais** (após o deploy do backend) — 13/13 visuais com valor | 12/13 = PBIX. Curva S: soma de pontos 18,8442 × 18,8443 (marcos = 1 min no PBIX). ACC 100/100/0, semanal 0/0/0, prazos 11/08→24/08, 13 d, Duração LB 53, matriz OK |
| 2 | Resumo 60% | **Funciona** — 13/13 com valor | ACC 98,48 / **79,12** / −19,36 (PBIX 98,48 / 73,87 / −24,61); semanal 11,67 / **2,00** / −9,67. Diferença = trabalho real lançado depois do refresh do PBIX (+287 h) — fonte mais nova, não erro |
| 3 | Resumo 90% | **Funciona** — 13/13 com valor | 11/13 = PBIX; ACC 0,69/1,39/0,69; semanal 0,69/0,60/−0,10 (= PBIX). Rosca tem 3 status (Overdue surgiu no .mpp atual) |
| 4 | LDP Eng 30% | Funciona (fonte LDP legada, só SELECT) | 8/9 = PBIX; rótulo INFRASTRUCTURE corrigido na LDP atual |
| 5 | LDP Eng 60% | Funciona | LDP atual: 361 docs (PBIX 357), 81 cancel. (75) — LDP mais nova |
| 6 | LDP Eng 90% | Funciona | 170 docs (PBIX 166) — LDP mais nova |
| 7 | LDP 3D 30% | **Funciona, curva 3D agora com dados** | 8/10 = PBIX; curva 3D (TREATAS FORMAT=3D): 20,4751 × 20,4752 |
| 8 | LDP 3D 60% | Funciona; curva = medidas gerais KPI 60 (literal do PBIR) | curva 18,69 × 18,61 (trabalho real mais novo) |
| 9 | LDP 3D 90% | Funciona | curva 10,3711 = PBIX |
| 10 | Programação | Funciona (inalterada) | 6/6 = PBIX (108/26/28/5/103) |

**Ainda NÃO equivalente ao Power BI (pendências reais):**
- Cross-filter por clique entre visuais: não reproduzido (todas as páginas).
- Slicer `WBX[Término]` modo Between (Programação): exibe o intervalo salvo, não filtra.
- `MIN(1, BLANK())` nas medidas gerais de curva realizada (família 30/60/90 anterior) devolve BLANK no motor; no DAX vale 0. Só afeta semanas sem nenhum trabalho real antes do eixo (as novas medidas 3D já usam a regra do DAX). Não alterei para não mexer em resultado validado pelo oráculo sem nova conferência.
- Paridade de pixels com o Power BI renderizado: não há captura do Power BI Desktop (não automatizado). Fidelidade = estrutura do PBIR + números.
- Matriz: subtotais desligados (como no PBIR) — linha aberta fica sem valores; recuo 0 (PBIR `steppedLayoutIndentation = 0`).

## 2. O que foi criado (tudo novo; nenhum módulo antigo editado)

| Caminho | Conteúdo |
|---|---|
| `Services/BiChs001Schedule/BiChs001ScheduleModels.cs` | modelos + escopo (`ProjectIdChs001`) |
| `Services/BiChs001Schedule/BiChs001MppReader.cs` | leitor MPXJ puro: UID, GUID, ID, pai, nível, resumo, marco, nome, EDT, Início/Término/LB, Work/ActualWork/BaselineWork/Remaining, %, atribuições, calendário efetivo, campos customizados (alias) e série timephased (real / LB 0 / planejado) distribuída por DIA pelas horas úteis do calendário efetivo da atribuição; diagnóstico factual |
| `Services/BiChs001Schedule/BiChs001ScheduleRepository.cs` | gravação atômica (sp_getapplock + transação + SqlBulkCopy + conferência de contagens), idempotente por SHA-256; SQL de leitura por projeto+versão |
| `Services/BiChs001Schedule/BiChs001ScheduleSyncService.cs` | ACC: token 2-legged do robô, mesmos caminhos de pasta do Tipo 2, `FindItemIdByNamePartAsync(".mpp")`, download para cópia temporária (apagada), leitura, gravação |
| `Controllers/BiChs001ScheduleController.cs` | `GET status/tarefas/trabalho-diario/trabalho-semanal` (`reports.gestao.view` ou `reports.cronograma.view`), `POST sincronizar` (`robot.run`); 400 id inválido, 404 outro projeto/versão inexistente |
| `Migrations/BiChs001/001_bi_chs001_UP.sql` / `_DOWN.sql` | schema `bi_chs001`: `Migracao`, `Versao_Cronograma`, `Tarefa_Snapshot`, `Trabalho_Tarefa_Dia`, `Alias_Rotulo`; views `VW_Tarefa` (alias), `VW_Versao_Atual`, `VW_Trabalho_Semana` (domingo, independente de DATEFIRST) |
| `Tools/BiChs001.Sync/` | CLI: `acc` / `arquivo <mpp>`, `--simular`, `--banco <teste>`, `--guardar-copia` |
| `Tests/NA_Connect.BiChs001.Tests/` | 22 testes (leitor, SQL no banco de TESTE, contrato da API) |
| `Scripts/Deploy-BiChs001Isolado-20261015.ps1` | deploy controlado do backend com backup e rollback |
| `ClientApp/src/features/production/biChs001/**` | ver §3 |
| `_bi_chs001_replica/tools/` | `conciliar_isolado_pbix.py`, `oraculo_3d30.py`, `extrair_expansao_matrizes.py`, `valores_reais.mjs` |
| `_bi_chs001_replica/` | `conciliacao_isolado_pbix_20261015.json`, `valores_reais_10_telas_20261015.json`, `fixtures_pbix/esperado_3d30.json`, `capturas_bi_isolado_20261015/`, `PROMPT_CODEX_VALIDACAO_FINAL.md`, este relatório |

Nenhuma alteração em `Program.cs` (o controlador é descoberto automaticamente; serviços instanciados no próprio controlador).

## 3. Frontend (só dentro do módulo biChs001)

- `entidades.ts`: WBX lê `/api/bi-chs001/schedule/tarefas` (alternativa automática: `/api/bi-chs001/cronograma` legado); AssignmentByDay lê `/schedule/trabalho-diario` **da mesma versão** das tarefas (`&versao=`); sem versão isolada → série PENDENTE (nunca mistura). Coluna de auditoria "Level 4 Original".
- `useDadosBi.ts`: carga com alternativa e acoplamento de versão; rota nova 404/502 → legado.
- `avisos.ts` + `BiChs001Page.tsx` + CSS: linha informativa da origem (versão bi_chs001 ou legado) e da correspondência aplicada ("Emgineering - 30%" → "Engineering - 30%" em 196 tarefas).
- `curvas.ts` / `medidas.ts`: `KPI 30 | 3D | COLUNA/CURVA | % Realizado` transcritas do DAX (TREATAS de `LDP[FORMAT] = "3D"`).
- `motor.ts`: requisito LDP para o TREATAS; **matriz hierárquica** (Level 4→7 pelo pai, Values por grupo, PRAZO com ALLSELECTED, `Min(EDT)` com ordem textual do DAX, ordenação pelo sortDefinition); **dSemana** = definição real do modelo (pbixray): quintas `Início da Semana + 4` entre `MINX(ALLSELECTED(WBX), Início LB)` e `MAXX(ALLSELECTED(WBX), Término)` com comparação data-hora → 23 semanas (04/06–05/11/2026) = cache.
- `filtrosPbi.ts` (**bug corrigido**): `Not(In)` sobre coluna de OUTRA entidade virava "sempre falso" → páginas 3D (filtro de página `LDP[FORMAT] NOT IN …`) zeravam o WBX. Agora a condição é neutra.
- `pbi/VisuaisDados.tsx` + `VisualBi.tsx`: componente `Matriz` (expandir/recolher, estado inicial = `expansionStates` do PBIR em `generated/expansao_matrizes.json`).
- `__preview__/preview.tsx`: erro de `tsc` (F01 do Codex) corrigido; aceita série e versão nas fixtures de dev.
- Logos (item 6): conferido — as 2 logos de cliente (CHS/Whitefox → `LOGO_NA`) seguem omitidas nas 23 páginas; fica só a logo N&A do canto esquerdo do canvas. ThemeLogo/sidebar/estilo global intocados.

## 4. SQL — backups, migração, rollback

| Item | Detalhe |
|---|---|
| Backup | `C:\Dev\N-A-Connect aa\_backups\bi_chs001_isolado_20261015\DB_NA_CONNECT_antes_bi_chs001_20261015_2245.bak` (COPY_ONLY, CHECKSUM, 696 MB, 22:45:14) — `RESTORE VERIFYONLY` ok **e restore real** como `DB_NA_CONNECT_BI_TESTE` |
| Teste em clone | UP, UP (idempotente), DOWN, DOWN, UP — log `inventario\teste_migracao_clone.log`; fingerprint legado igual em todas as etapas |
| Produção | UP aplicado 23:02:54 (`inventario\migracao_producao.log`); versão 1 gravada 23:03:13 (`sync_acc_producao.log`) |
| Isolamento | antes/depois: `OBJETOS_FORA_BI 642 / -1258081025`, `COLUNAS_FORA_BI 2403 / 1925769961`, `MAX_MODIFY_FORA_BI 2026-10-13 16:16:25` (inalterados) — `sql_baseline_antes.txt`, `sql_baseline_pos_migracao.txt`, `sql_baseline_final.txt` |
| Rollback do BI | `sqlcmd -S .\SQLEXPRESS -d DB_NA_CONNECT -E -b -i Migrations\BiChs001\001_bi_chs001_DOWN.sql` (remove só `bi_chs001`; dados reproduzíveis do .mpp) |
| Restore total (último recurso) | `RESTORE DATABASE DB_NA_CONNECT FROM DISK=N'…\DB_NA_CONNECT_antes_bi_chs001_20261015_2245.bak' WITH REPLACE` — **desfaz também dados de outros módulos gravados depois das 22:45**; não testado em produção |
| Clone de teste | `DB_NA_CONNECT_BI_TESTE` mantido (usado pelos testes .NET). Para remover: `DROP DATABASE DB_NA_CONNECT_BI_TESTE` |
| Cópia do .mpp (evidência) | `_backups\bi_chs001_isolado_20261015\mpp_acc\CHS001-000-PMO-SC-001_ab13ca4360f1.mpp` (v47). O .mpp do ACC não foi alterado (só download) |
| Fontes BI antes | `_backups\bi_chs001_isolado_20261015\fontes\bi_module_antes.tar` (SHA-256 `ad320c97…893b`) |

## 5. Leitor MPXJ — fatos da v47 (diagnóstico gravado em `Versao_Cronograma.Diagnostico_JSON`)

883 tarefas (755 folhas, 128 resumos), 755 atribuições, 1 tarefa sem pai (resumo do projeto), 0 pais ausentes, 0 ciclos; 0 folhas sem Início/LB/Trabalho/Trabalho LB; série diária 01/06–11/11/2026; 0 blocos sem hora útil; trabalho real: série 7.856,46 h × tarefas 7.856,30 h, 0 folhas divergentes (> 0,01 h).
**Fato registrado (não corrigido):** a série timephased de **linha de base** soma 11.023,93 h × 10.418,74 h no total de LB das tarefas (220 folhas divergem). O DAX do PBIX **não usa** essa série (usa `WBX[Trab Linha de Base]` por tarefa e a série só de trabalho real), então não afeta nenhuma medida; está gravada para auditoria.
Diferenças MPP × PBIX explicadas: `Trabalho` 519 exatos / 641 a 0,005 h (marcos = 1 min no Access do PBIX e arredondamento de minutos); `Trabalho Real` +287 h no .mpp (lançamentos após o refresh do PBIX); 219 pares semana×tarefa só no .mpp (mesma causa).

## 6. Testes executados (reais)

| Teste | Resultado |
|---|---|
| `node _bi_chs001_replica/tools/testar.mjs` (cópia composta) | **46/46** (36 anteriores ajustados + 10 novos: alias 0/543/386 → 196/347/190, carga com versão única e alternativa legada (fetch simulado), 3D 30% × oráculo Python, filtro neutro, matriz, dSemana = cache) |
| `npx tsc --noEmit` (src inteiro) | **0 erros** (F01 do Codex resolvido) |
| `vite build` (`_build_verify\front_bi_isolado\dist_vite`) | ok; nenhuma fixture no bundle |
| `dotnet build` backend composto (`_build_verify\backend_bi_isolado`) | **0 erros** |
| `dotnet test Tests/NA_Connect.BiChs001.Tests` (banco de TESTE) | **22/22** — leitor (hierarquia, campos, typo preservado, série, arquivo inalterado), SQL (atômico, idempotente, falha sem resíduo, recusa outro projeto, alias só CHS001/resumo/grafia exata, domingo × DATEFIRST, migração sem dbo), API (permissões 401/403/503, 400/404, robot.run, 883/755, alias, série dia=semana, conferência com legado 883/883) |
| Regressão `NA_Connect.LdpTipo2.Tests` | 106/106 |
| Regressão `NA_Connect.Cronograma.Tests` | 27/28 — falha `Tipo1AnalyticsTests.DocumentPhysicalProgressExcludesSummary` (Tipo 1, sem relação com arquivos do BI) |
| Regressão `NA_Connect.Planning.Tests` | **não compila** (fakes desatualizados em `CostManagementControllerTests`/`ProductionAuditControllerTests` — mudanças de outros módulos); independe do BI |
| Conciliação `tools/conciliar_isolado_pbix.py` | ver §0.3 e `conciliacao_isolado_pbix_20261015.json` |
| Valores das 10 telas `tools/valores_reais.mjs` | §1 |
| Visual (pré-visualização dev com linhas reais, Vite 5173) | `capturas_bi_isolado_20261015/` p03, p04, p05, p08 — cartões, curvas, rosca e matriz preenchidos; fixtures temporárias removidas |
| UI autenticada | **Não realizada**: navegador sem sessão (tela de login); não digitei credenciais |

## 7. Arquivos / objetos existentes preservados

| Item | Estado |
|---|---|
| `Services/CronogramaTipo2/*`, `Services/LdpTipo2/*`, `Models/CronogramaTipo2Registro.cs` | intocados (35 arquivos, última modificação 06/10/2026) |
| `Program.cs`, `AccIntegration.WebApp.csproj`, `appsettings.json` | intocados (13–14/10) |
| `dbo.*`, `Cronograma_Projetos` (46.313 linhas CHS001), views legadas, LDP, Timesheet, Planejamento | sem DDL/DML; fingerprint idêntico |
| .mpp oficial no ACC e `Services/cronograma/*.mpp` | intocados (só download de cópia) |
| ThemeLogo, sidebar, norma/BML011, dashboards existentes | intocados |
| `Controllers/BiChs001Controller.cs` (legado do BI) | intocado (o candidato apenas publica a versão de 17:27 já existente no disco) |
| Arquivos do Codex (`auditoria_codex_independente/`) | não editados |
| git do checkout principal | nenhum commit/reset/clean; arquivos copiados só em caminhos do BI (log `inventario\copia_para_main.log`) |

## 8. Problemas encontrados que dependem de decisão

1. **Build do checkout principal quebrado (pré-existente a esta etapa, piora com a sonda do Codex):** o SDK compila todo `.cs` sob a pasta do projeto; `_bi_chs001_replica/tools/extrator_mpp/Program.cs` (etapa BI anterior) e `_bi_chs001_replica/auditoria_codex_independente/mpp_probe/` (Codex, 22:35, inclusive `bin/obj`) geram `CS8802` e atributos duplicados. O processo em 5277 não é afetado; o próximo `dotnet run`/`dotnet build` no repositório falha. Correção mínima sugerida (1 linha, **não aplicada** — arquivo compartilhado): acrescentar `_bi_chs001_replica/**` ao `<DefaultItemExcludes>` do `AccIntegration.WebApp.csproj`. Alternativa: mover as duas ferramentas para `Tools\` (o Codex move a dele). O candidato do deploy foi compilado numa cópia sem essa pasta.
2. **Deploy do backend bloqueado pelo classificador** — §9.
3. `MIN(1, BLANK())` nas curvas gerais (§1).

## 9. Implantação — estado e comandos

- **Frontend:** já no ar (Vite 5173 serve os arquivos do módulo copiados às 23:34). Enquanto o backend antigo roda, `/schedule/*` responde 404 → a tela usa o legado automaticamente (mesmo comportamento de antes + matriz/curvas como PENDENTE onde falta campo).
- **Backend: PENDENTE (bloqueado).** Pré-condições verificadas: candidato `C:\Dev\N-A-Connect aa\_build_verify\backend_bi_isolado\AccIntegration.WebApp.dll` difere do vivo (16:14) **somente** por arquivos do BI (`BiChs001Controller.cs` 17:27 + novos); nenhuma mudança de pacote; robô ocioso (último ciclo 08:59); porta 5277 = `bin\Debug\net9.0\AccIntegration.WebApp.exe` (PID 86548); Cloudflare aponta para 5173 (não é tocado).
  Executar (no prompt do Claude Code, com `!`):
  ```
  ! powershell -NoProfile -ExecutionPolicy Bypass -File "C:\Dev\N-A-Connect aa\N-A-Connect\Scripts\Deploy-BiChs001Isolado-20261015.ps1"
  ```
  O script: checa jobs/robô/porta, faz backup do DLL/PDB vivos em `_backups\bi_chs001_isolado_20261015\binarios_<data>`, troca, sobe com `Worker__RunOnStartup=false` em `http://localhost:5277`, exige 401 anônimo em 6 rotas (novas e legadas) e faz **rollback automático** se algo falhar. Log: `_backups\bi_chs001_isolado_20261015\inventario\deploy_backend.log`.
- **Depois do deploy:** abrir `/controle-producao/bi-chs001/resumo-30` logado com CHS001; a faixa azul deve dizer "BI CHS001 isolado (bi_chs001), versão 1". Smoke autenticado opcional no console do navegador: `await (await fetch('/api/bi-chs001/schedule/status?projectId=b.fcdb7ebd-1b23-440d-a04b-90391098522e')).json()`.
- **Atualizar o cronograma do BI** quando o .mpp mudar: `POST /api/bi-chs001/schedule/sincronizar?projectId=…` (permissão `robot.run`) ou `dotnet "C:\Dev\N-A-Connect aa\_build_verify\cli_bi_isolado\BiChs001.Sync.dll" acc` (mesmo código; mesmo arquivo = devolve a versão existente). Nenhum worker global foi criado ou disparado.
- **Rollback do backend** (manual, se preferir): parar o processo de 5277, copiar de volta `AccIntegration.WebApp.dll/.pdb` da pasta `binarios_<data>` e subir com `$env:Worker__RunOnStartup="false"`.

## 10. Revisão Codex

Lançada às ~23:36 via `codex exec -s read-only` (sem permissão de escrita), com o prompt `PROMPT_CODEX_VALIDACAO_FINAL.md`. Resultado: ver `CODEX_VALIDACAO_FINAL_ISOLAMENTO.md` (salvo a partir da mensagem final do Codex) — estado registrado no adendo abaixo.

## 11. Git

Trabalho versionado no worktree `.claude/worktrees/bi-chs001-isolado`, branch `worktree-bi-chs001-isolado` (sem remoto). O branch parte de `origin/main` e **não compila sozinho**: o módulo depende de arquivos ainda não versionados do checkout principal (rotas, sidebar). Nada foi commitado no checkout principal.
