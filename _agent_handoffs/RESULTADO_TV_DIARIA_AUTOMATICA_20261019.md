# RESULTADO — TV: telas diárias AUTOMÁTICAS (hoje + pendências de ontem) — 19–20/10/2026

Handoff: `_agent_handoffs/CLAUDE_TV_DIARIA_AUTOMATICA_20261019.md`

## Status real

| Item | Status |
|---|---|
| Implementação (frontend + backend + Portal TV) | **Pronta e testada no worktree isolado.** |
| Aplicação no checkout principal | **Pendente.** O usuário roda o `aplicar.py`. Nada foi escrito no checkout principal. |
| Publicação/reinício do backend | **Não feita.** Nenhum processo foi parado ou reiniciado. Sem o backend novo, o botão fica desabilitado com o aviso "backend desatualizado". |
| Validação visual no navegador (editor/player real) | **Pendente (usuário).** Coberta só por renderização em teste e pelas regressões isoladas do portal. |
| SQL / permissões / LDP / robôs / Normalizador / Emídio-perfil 9 | **Nada alterado.** O banco só foi consultado em leitura. |

## Onde está

- **Worktree:** `.claude/worktrees/tv-diaria-automatica-20261019`, branch `worktree-tv-diaria-automatica-20261019`.
  - Commit `6122863`: baseline, snapshot do checkout principal.
  - Commit seguinte: a implementação.
- **Pacote:** `C:\Dev\N-A-Connect aa\_entrega_claude_tv_diaria_automatica_20261020\`.
  - `novo/` e `base/`
  - `manifesto.txt`
  - `aplicar.py`
  - `TV_DIARIA_AUTOMATICA.patch`, só para referência.
- **Builds de verificação** (fora do repositório), em `C:\Dev\N-A-Connect aa\_build_verify\tv_diaria_auto_20261020\`:
  - `backend-bin`
  - `vite-dist`
  - `tvportal-tests`

## Como aplicar

```powershell
python "C:\Dev\N-A-Connect aa\_entrega_claude_tv_diaria_automatica_20261020\aplicar.py" --conferir   # nada é escrito
python "C:\Dev\N-A-Connect aa\_entrega_claude_tv_diaria_automatica_20261020\aplicar.py"              # backup + aplica (aborta inteiro em conflito)
python "C:\Dev\N-A-Connect aa\_entrega_claude_tv_diaria_automatica_20261020\aplicar.py" --reverter   # só se os arquivos ainda estiverem como a entrega deixou
cd "C:\Dev\N-A-Connect aa\N-A-Connect"; graphify update .
```

- **Conflitos:** o script compara cada arquivo com a base, ignorando só CRLF/LF. Se outro agente (Claude/Codex) tiver mudado algum deles, nada é escrito.
- **Backup:** é feito na hora da aplicação, em `C:\Dev\N-A-Connect aa\_backup_claude_tv_diaria_automatica_20261020\<data-hora>`.
- **Frontend:** entra via Vite/HMR.
- **Backend:** só passa a valer depois de publicado pelo seu procedimento controlado. São 4 arquivos .cs e um novo, `Services/Tv/TvDiaAutomatico.cs`. Antes de publicar, confira os jobs críticos e não pare o sincronizador.
- **Enquanto o backend antigo roda:**
  - o botão fica desabilitado com o motivo;
  - um JSON com `"AUTO"` seria recusado pelo servidor antigo.

## O que foi feito

### 1. Botão único no editor (Configurações → Telas para TV → TV selecionada)

O bloco "Telas diárias automáticas" tem:
- a escolha **Projetos selecionados (n)** ou **Todos os permitidos (n)**;
- o botão **Gerar telas diárias automáticas**.

Regras:
- **"Todos os permitidos"** usa SOMENTE `opcoes` de `useProjetosPermitidosTv` com `podeTela(projeto, keys da Programação Diária)`. Nunca usa a lista bruta.
- **Um slide por projeto:** `gestao-a-vista-programacao-diaria` com `dataReferencia: "AUTO"`. Nunca mescla projetos.
- **Sem duplicata:** se já existe AUTO para o mesmo projeto, ele não é recriado. Os projetos já existentes são listados.
- **01 Visão Geral:** se já está na sequência, nada muda e a posição é mantida. Se não está, é inserida no INÍCIO, consolidando os projetos escolhidos, só quando o usuário tem permissão da 01 em todos eles. Se não tiver, aparece o aviso com os projetos sem permissão e a 01 não é inserida.
- **Limite de 50:** a contagem é mostrada (atuais + diárias + 01). Acima do limite, **nada** é adicionado e a mensagem diz quantas telas seriam necessárias. Nunca adiciona parcialmente.
- **Projetos sem permissão:** com "Selecionados", são ignorados e listados.
- **Só altera o rascunho:** o usuário clica em **Salvar**. Telas existentes não são alteradas.

Na sequência, o item AUTO aparece como **"Hoje + pendências de ontem (automático)"**, sem `input type=date`. Itens de data fixa continuam com o campo de data.

### 2. Modo AUTO (mesmo screenId, `DataReferencia = "AUTO"`)

- **Validação:**
  - backend em `TvConfigurationRules.DataReferenciaDoSlideValida`;
  - frontend em `dataReferenciaDoSlideValida`.
- **O que é aceito:**
  - "AUTO" exato;
  - data ISO real, com a mesma regra de antes.
- **O que continua sendo erro:**
  - ausência de data;
  - "auto", " AUTO" e variantes.
- **AUTO idêntico** (mesma tela + mesmo projeto na mesma apresentação) é recusado no front e no back. Em outra apresentação é permitido.
- **Nas demais telas** o campo continua sendo descartado. O JSON antigo é lido e salvo sem alteração (testado).
- `GET /api/tv-config` passa a informar `diariaAutomatica: true`, para o editor saber se o servidor aceita AUTO.

### 3. HOJE/ONTEM pela data civil da Bahia

- **Backend:** `TvDiaAutomatico.Hoje` usa `America/Bahia` ou `Bahia Standard Time`. Sem nenhum dos dois, usa UTC−3 fixo, já que a Bahia não tem horário de verão desde 2012.
- **Frontend:** `dataCivilBahia` com `timeZone: "America/Bahia"`.
- **Virada do dia:**
  - `useHojeBahia` tem UM timer de 30 s que só muda o estado quando a data muda.
  - Na tela AUTO, a data é a `key`: na virada, a tela remonta e busca programação e LDP de novo, sem F5 e sem loop.
  - O cabeçalho do player também atualiza: "CHS001 · Hoje 20/10/2026 + pendências de 19/10/2026".
- **Janela consultada:** `[ontem, hoje]`, numa consulta só ao MESMO endpoint semanal (`/api/reports/gestao-vista-jamile/programacao`, que aceita até 62 dias).
  - No domingo a janela cruza a semana (sábado + domingo); na segunda, pega domingo + segunda.
  - Linhas fora de ontem/hoje são descartadas. Não é histórico.
- **Consulta separada:** a tela AUTO usa uma consulta própria (`progAuto`). A linha `useProgramacao` da 01, da 02 e da diária de data fixa ficou **byte a byte igual**.

### 4. Tela AUTO por disciplina

Cartões no topo:
- documentos previstos hoje;
- HH previsto hoje;
- pendentes de ontem (comprovados);
- HH pendente de ontem;
- ontem sem comprovação.

Cada disciplina tem um bloco com:
- **PREVISTO HOJE:** código, título oficial da LDP, atividade, elaborador/verificador/aprovador, situação da programação e HH.
- **PENDENTE DE ONTEM:** documento programado ontem que a LDP oficial (mesmo projeto e código) mostra **NÃO emitido**. Mostra código, título, atividades, recursos e HH.
- **SEM COMPROVAÇÃO / INDETERMINADO:** fica separado e **nunca** é contado como pendente. Os casos são:
  - sem código;
  - código fora da LDP;
  - cancelado;
  - código repetido com situações diferentes;
  - emitido sem data;
  - LDP indisponível.

Regras:
- Emitido até ontem não é pendência. Emitido depois de ontem também não: aparece só na contagem.
- Usa a MESMA deduplicação da 02 (`deduplicarProgramacao`) e a MESMA classificação de disciplina.
- A conciliação da 01 foi generalizada para `situacaoEmissaoNoCorte` (corte = sábado na 01, ontem na diária) **sem mudar a regra da 01**. Isso foi testado, e os testes antigos da 01 passam sem alteração.
- Dia sem nada: mensagem explícita, "Nenhuma atividade programada para hoje (…) nem para ontem (…)". Disciplina sem item numa seção mostra "Nada programado para hoje…" ou "Nenhuma pendência comprovada…".
- Falha da programação ou da LDP bloqueia a tela (`ConsolidacaoIncompleta`). Não há fallback.

### 5. Portal TV

- **Slide AUTO:** `TvPortalResources.PeriodoPermitido` só aceita `inicio/fim` = a janela `[ontem, hoje]` **calculada no servidor**. Há tolerância de ±5 min só na virada da meia-noite, para relógio TV × servidor.
- **Negado:** qualquer outra janela (semana, mês, invertida, só hoje).
- **Inalterados:**
  - projeto/slide/apresentação validados como antes;
  - LDP do próprio projeto liberada (conciliação);
  - slides de data fixa (semana da data salva) e telas sem data.

### Conferência no banco (somente leitura, 19/10 23h)

| Projeto | 19/10 (ontem, em 20/10) | 20/10 (hoje, em 20/10) |
|---|---|---|
| CHS001 | 5 atividades / 5 documentos | — (vai mostrar "nada programado para hoje") |
| TIM003 | 1 | 1 |
| BML012.01 | 1 | — |

## Testes (no worktree)

| Verificação | Resultado |
|---|---|
| `tsc --noEmit` | OK, 0 erros |
| `npm run test:pmo` | 343 testes: 337 passam, **6 falham**. Na linha de base (mesmo snapshot sem as alterações, rodado em worktree destacado) são 329 testes com **as mesmas 6 falhas**: mojibake ×2, "Tela 02 … ATIVIDADES IMPORTADAS", Tipo 2 ×2 e contrato TvTelas×Tipo 2. Os 14 novos passam. |
| `npm test` (sidebar) | 22/22 |
| `node scripts/verify-tv-portal.mjs` (navegador isolado) | OK |
| `node scripts/verify-tv-auth-routing.mjs` (navegador isolado) | OK |
| Vite build | OK. Saída em `_build_verify\tv_diaria_auto_20261020\vite-dist`; o `wwwroot` não foi tocado |
| `dotnet build` isolado (`-o _build_verify\…\backend-bin`) | OK, 0 erros (avisos pré-existentes) |
| `NA_Connect.TvPortal.Tests` (DLL nova) | **74/74**: 62 anteriores + 11 em `TvDiariaAutomaticaTests` + 1 de middleware em `TvPortalTests` |

### Testes novos

**`tvDiariaAutomatica.test.ts`** (14):
- AUTO exato e variantes recusadas;
- AUTO repetido;
- selecionados × todos permitidos, com projeto negado;
- duplicidade em segunda e terceira geração;
- 01 existente no meio, mantida;
- data fixa não conta como AUTO;
- 01 sem permissão ou indisponível;
- limite 50 (50 cabe, 51 bloqueia tudo);
- virada da meia-noite UTC−3, domingo/segunda, fim de mês/ano;
- análise por disciplina: dedup, HH desconhecido = null, pendente comprovado, emitido até ontem, emitido depois, sem comprovação por motivo;
- LDP indisponível, cancelado e repetido;
- virada do dia com os mesmos dados;
- dia vazio;
- 01 inalterada;
- player com HOJE/ONTEM vigentes;
- contratos front×back.

**`TvDiariaAutomaticaTests.cs`** (11):
- validação e descarte nas demais telas;
- JSON antigo + data fixa preservados ao salvar AUTO, com a outra apresentação idêntica;
- limite 50;
- data da Bahia na meia-noite, domingo e ano novo;
- janela;
- portal: só a janela do servidor, tolerância, data fixa e semanal inalteradas.

**`TvPortalTests.AutoDailySlideReadsOnlyServerWindowAndItsProject`:** janela correta = 200. Recebem 403:
- outra semana;
- 30 dias;
- projeto do outro slide;
- apresentação não atribuída;
- slide fixo com a janela AUTO;
- recurso de outra tela;
- POST.

### Rodar os testes do portal no checkout principal depois de aplicar

O projeto referencia `_build_verify\tv_scope_bin_20261019\AccIntegration.WebApp.dll`. Copie para lá o conteúdo de `C:\Dev\N-A-Connect aa\_build_verify\tv_diaria_auto_20261020\backend-bin\` (ou de um build novo) e rode:

```powershell
dotnet test Tests\NA_Connect.TvPortal.Tests -o <pasta externa>
```

## Pendências

1. Rodar `aplicar.py --conferir`, depois aplicar e rodar `graphify update .`.
2. Publicar o backend de forma controlada. Antes disso o botão fica desabilitado.
3. Validação visual:
   - no editor, Gerar → Salvar;
   - no player e no Portal TV, olhar a TV na virada da meia-noite e numa segunda-feira.
4. Decisão de negócio (não implementada): hoje "pendente de ontem" = documento programado ontem e **não emitido** na LDP. Atividades intermediárias (ex.: verificação) não têm status de conclusão próprio na fonte oficial. Se a gestão quiser outro critério (ex.: `Weekly_Progress_Status`), é preciso definir qual status comprova a conclusão.
