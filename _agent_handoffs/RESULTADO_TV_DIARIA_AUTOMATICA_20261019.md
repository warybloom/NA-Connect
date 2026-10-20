# RESULTADO — TV: telas diárias AUTOMÁTICAS (hoje + pendências de ontem) — 19–20/10/2026

Handoff: `_agent_handoffs/CLAUDE_TV_DIARIA_AUTOMATICA_20261019.md`
Complemento (20/10): `_agent_handoffs/COMPLEMENTO_LAYOUT_POR_DISCIPLINA_20261019.md` — exibições Padrão / Cards por disciplina / Uma tela por disciplina.

## Status real

| Item | Status |
|---|---|
| Implementação (frontend + backend + Portal TV) | **Pronta e testada no worktree isolado.** |
| Exibição **Padrão** (layout da 02 em Hoje / Pendências de ontem) | **Pronta e testada** (tsc, testes de renderização, checagem visual estática). |
| Exibição **Cards por disciplina** | **Pronta e testada** (idem). A checagem visual pegou e corrigiu um conflito de classe CSS. |
| Exibição **Uma tela por disciplina** | **Pronta e testada.** As disciplinas vêm do servidor (Norma do projeto + programação de ontem/hoje). Nada é digitado. Sem Norma, nada é oferecido. |
| Editor: seletor de exibição + escolha de disciplinas | **Pronto.** Coberto por tsc e por testes de contrato/lógica. **Sem validação visual no navegador** (exige o backend novo rodando). |
| Aplicação no checkout principal | **Pendente.** O usuário roda o `aplicar.py`. Nada foi escrito no checkout principal. |
| Publicação/reinício do backend | **Não feita.** Nenhum processo foi parado ou reiniciado. Sem o backend novo, o botão fica desabilitado com o aviso "backend desatualizado". |
| Validação visual no navegador (editor/player real) | **Pendente (usuário).** Coberta só por renderização em teste e pelas regressões isoladas do portal. |
| SQL / permissões / LDP / robôs / Normalizador / Emídio-perfil 9 | **Nada alterado.** O banco só foi consultado em leitura. |

## Onde está

- **Worktree:** `.claude/worktrees/tv-diaria-automatica-20261019`, branch `worktree-tv-diaria-automatica-20261019`.
  - Commit `6122863`: baseline, snapshot do checkout principal.
  - Commit `3110435`: a implementação de 19–20/10.
  - Commits seguintes: o complemento das exibições (20/10) e este relatório.
- **Pacote (atualizado 20/10 com as exibições):** `C:\Dev\N-A-Connect aa\_entrega_claude_tv_diaria_automatica_20261020\`.
  - `novo/` e `base/`
  - `entrega_anterior/`: o `novo/` da versão de 19–20/10, para reconhecer um checkout que já a tenha recebido;
  - `manifesto.txt` (19 arquivos)
  - `aplicar.py`
  - `TV_DIARIA_AUTOMATICA.patch`, só para referência (base → novo).
- **Builds de verificação** (fora do repositório), em `C:\Dev\N-A-Connect aa\_build_verify\tv_diaria_layouts_20261020\`:
  - `backend-bin`
  - `vite-dist`
  - `tvportal-tests` (+ `tvportal-tests-src`)
  - `visual`

## Como aplicar

```powershell
python "C:\Dev\N-A-Connect aa\_entrega_claude_tv_diaria_automatica_20261020\aplicar.py" --conferir   # nada é escrito
python "C:\Dev\N-A-Connect aa\_entrega_claude_tv_diaria_automatica_20261020\aplicar.py"              # backup + aplica (aborta inteiro em conflito)
python "C:\Dev\N-A-Connect aa\_entrega_claude_tv_diaria_automatica_20261020\aplicar.py" --reverter   # só se os arquivos ainda estiverem como a entrega deixou
cd "C:\Dev\N-A-Connect aa\N-A-Connect"; graphify update .
```

- **Conflitos:** o script compara cada arquivo com a base, ignorando só CRLF/LF. Se outro agente (Claude/Codex) tiver mudado algum deles, nada é escrito.
  - Um arquivo igual à entrega anterior de 19–20/10 é aceito e atualizado (aparece como "entrega anterior -> atualizar").
- **Backup:** é feito na hora da aplicação, em `C:\Dev\N-A-Connect aa\_backup_claude_tv_diaria_automatica_20261020\<data-hora>`.
- **Frontend:** entra via Vite/HMR.
- **Backend:** só passa a valer depois de publicado pelo seu procedimento controlado. São 4 arquivos .cs e um novo, `Services/Tv/TvDiaAutomatico.cs` (com as constantes das exibições). Antes de publicar, confira os jobs críticos e não pare o sincronizador.
- **Enquanto o backend antigo roda:**
  - o botão fica desabilitado com o motivo;
  - com um backend sem `layoutsDiarios`, o seletor só permite Padrão;
  - um JSON com `"AUTO"` seria recusado pelo servidor antigo.

## Complemento 20/10 — exibições do slide automático

### Persistência (por slide, compatível com o JSON atual)

- Campos novos no `TvSlide`, opcionais e omitidos do JSON quando nulos (`JsonIgnore WhenWritingNull`):
  - `layoutDiario`: `"padrao"` (ou ausente), `"cards"` ou `"disciplina"`;
  - `disciplina`: chave do grupo, obrigatória só em `"disciplina"`;
  - `disciplinaNome`: rótulo.
- **Só valem no slide AUTO** (`gestao-a-vista-programacao-diaria` + `dataReferencia "AUTO"`). Em data fixa e nas outras telas são descartados ao salvar.
- `"padrao"` é gravado como ausente: o slide padrão continua **byte a byte igual** ao formato anterior, e arquivo antigo é lido como Padrão.
- `dataReferencia "AUTO"`, datas fixas, validação de AUTO exato e limite de 50: **inalterados**.
- **Duplicata** = mesma tela + mesmo projeto + mesma exibição + mesma disciplina (front e back). Exibições ou disciplinas diferentes do mesmo projeto são slides distintos e permitidos.
- **Recusado:** exibição desconhecida (ex.: `"Cards"`, `"mosaico"`) e `"disciplina"` sem disciplina (ou com mais de 200 caracteres).
- `GET /api/tv-config` passa a informar `layoutsDiarios: true`. Sem isso (backend antigo), o editor só oferece Padrão: o servidor antigo descartaria o campo em silêncio.

### Editor (Configurações → Telas para TV → TV selecionada → "Telas diárias automáticas")

- Seletor **"Exibição: Padrão | Cards por disciplina | Uma tela por disciplina"** ao lado de "Gerar telas diárias automáticas". O escopo (Selecionados / Todos os permitidos) continua o mesmo.
- **Padrão / Cards:** um slide por projeto na exibição escolhida. Um Padrão já existente não impede um Cards do mesmo projeto.
- **Uma tela por disciplina:**
  - escolhe-se **um projeto** (entre os do escopo com permissão na Programação Diária);
  - a lista de disciplinas vem do servidor:
    - as cadastradas na **Norma de Coordenação** do projeto (`/disciplinas`);
    - as **descobertas** na programação de ontem/hoje (`/programacao`), com a MESMA chave que a tela usa (DOC3 → Norma, `criarCatalogo` de 1 projeto, sem Normalizador — idêntico à tela).
  - Já vêm marcadas as que têm programação ontem/hoje. As que só estão na Norma podem ser marcadas explicitamente, e a tela declara isso.
  - Gera um slide por disciplina marcada.
  - **Norma indisponível ⇒ nenhuma disciplina oferecida**, porque a chave poderia divergir da que a TV calcula. Programação indisponível ⇒ só as da Norma, com aviso.
- **Regras mantidas:** 01 Visão Geral no início, permissões efetivas, tudo-ou-nada no limite de 50 (cada disciplina conta um slide) e só rascunho (o usuário clica em **Salvar**).
- **Na sequência:** o item mostra "Hoje + pendências de ontem (automático) · Cards por disciplina" ou "· Disciplina: MECÂNICA".
- **"Usar seleção":** num item por disciplina fica desabilitado, porque a disciplina é do projeto do item. Num Cards, mantém a exibição.

### Tela (player e Portal TV)

As três exibições usam a mesma consulta `progAuto` (janela [ontem, hoje]), a mesma `analiseDiariaAutomatica` (deduplicação da 02, HH = elab. + verif. + aprov., conciliação com a LDP) e o mesmo agrupamento de disciplina.

- **Padrão:** é o próprio componente da 02 (`Programming` no modo diário: cartões, tabela "Gerência da Engenharia" e gráficos por disciplina), em duas seções:
  - **HOJE · dia**: atividades de hoje;
  - **PENDÊNCIAS DE ONTEM · dia**: só as atividades de ontem cujo documento a LDP comprova **não emitido**.
  - Os itens sem comprovação são listados à parte e nunca contados como pendentes.
  - A 02 e a diária de data fixa não mudam: os títulos só são trocados quando a prop opcional `rotulos` é passada.
- **Cards por disciplina:**
  - os 5 cartões de totais;
  - um **cartão de indicadores por disciplina**: doc. hoje, HH hoje, pendentes de ontem, HH pendente, sem comprovação, emitidos até ontem;
  - os blocos por disciplina, com Previsto hoje / Pendente de ontem / Sem comprovação.
  - Só aparecem disciplinas com algo hoje ou ontem. Nenhum card vazio.
- **Uma tela por disciplina:**
  - os 5 cartões com os totais **da disciplina** e o bloco **só dela**;
  - o cabeçalho mostra "PROJETO · DISCIPLINA: NOME";
  - **sem atividade hoje/ontem:** aviso explícito ("Nenhuma atividade de X programada para hoje… nem para ontem…"), sem card vazio;
  - **disciplina fora da Norma atual:** o aviso pede revisão do slide.
- **Exibição inválida ou "disciplina" sem disciplina:** aviso de erro. Nunca cai em outra exibição.
- **Player:** o cabeçalho acrescenta " · Cards por disciplina" ou " · Disciplina: X". A key do slide continua a mesma de antes (o índice já distingue os slides).
- **Portal TV:** a exibição **não abre recurso nem período novo**. Valem a mesma janela [ontem, hoje] do servidor e os mesmos recursos/projeto do slide. Isso é testado com Cards e Disciplina: 200 na janela; 403 em outra semana, outro projeto e recurso fora da tela.

### Impedimento real

Nenhum. Os três modos são gerados a partir da fonte com segurança. A única limitação é declarada no editor: a lista de disciplinas depende da Norma do projeto estar acessível no servidor. Sem ela, nada é oferecido.

## O que foi feito (entrega de 19–20/10)

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
| `npm run test:pmo` | **Complemento 20/10:** 354 testes, 348 passam, **as mesmas 6 falhas pré-existentes** (mojibake ×2, "Tela 02 … ATIVIDADES IMPORTADAS COM DATA", Tipo 2 ×2, contrato TvTelas×Tipo 2). O texto que falta na "Tela 02" já não existia na linha de base. Os 11 novos de `tvDiariaLayouts.test.ts` passam; os 14 de `tvDiariaAutomatica` e os de `tvProgramacaoDiaria` continuam passando. Na entrega de 19–20/10 eram 343 testes, com as mesmas 6 falhas. |
| `npm test` (sidebar) | 22/22 |
| `node scripts/verify-tv-portal.mjs` (navegador isolado) | OK |
| `node scripts/verify-tv-auth-routing.mjs` (navegador isolado) | OK |
| Vite build | OK. Saída em `_build_verify\tv_diaria_layouts_20261020\vite-dist`; o `wwwroot` não foi tocado |
| `dotnet build` isolado (`-o _build_verify\tv_diaria_layouts_20261020\backend-bin`) | OK, 0 erros (avisos pré-existentes) |
| `NA_Connect.TvPortal.Tests` (DLL nova; cópia externa do projeto de teste em `_build_verify\tv_diaria_layouts_20261020\tvportal-tests-src`) | **82/82**: 74 anteriores + 7 em `TvDiariaLayoutsTests` + 1 de middleware em `TvPortalTests` (`AutoDailyLayoutsKeepSameResourcesWindowAndProject`) |
| Checagem visual estática das 3 exibições | HTML renderizado pelo componente real, com o CSS do Vite build e dados fictícios, capturado no Edge headless. Screenshots em `_build_verify\tv_diaria_layouts_20261020\visual\{padrao,cards,disciplina}.png`. Isso **não** substitui a validação no player real. |

### Testes novos do complemento (20/10)

**`tvDiariaLayouts.test.ts`** (11):
- persistência: padrão idêntico ao slide anterior; cards/disciplina gravados;
- validação: exibição inválida, disciplina ausente, duplicata só por projeto+exibição+disciplina;
- botão Cards: padrão existente não impede; segunda geração não duplica;
- botão Disciplina: um slide por disciplina; repetida ignorada; sem disciplina bloqueia;
- limite 50 por slide de disciplina, sem parcial;
- disciplinas do editor com a mesma chave da tela; nada inventado; sem Norma = vazio;
- renderização Padrão (2× layout da 02, pendente só comprovado);
- renderização Cards (2 cards, nenhum vazio, classe sem colisão);
- renderização Disciplina (só a disciplina, aviso sem atividade, fora da Norma);
- player (cabeçalho projeto + disciplina, exibição inválida);
- contratos front×back×Portal.

**`TvDiariaLayoutsTests.cs`** (7):
- normalização (`"padrao"` → ausente, trim);
- inválidas recusadas;
- duplicata;
- descarte fora do AUTO;
- JSON sem campos no padrão + arquivo antigo = padrão;
- limite 50;
- Portal com a mesma janela para as 3 exibições.

**`TvPortalTests.AutoDailyLayoutsKeepSameResourcesWindowAndProject`** (middleware):
- recebem 200: Cards e Disciplina na janela; `/disciplinas` do projeto;
- recebem 403: outra semana, outro projeto, recurso fora da tela.

### Testes novos da entrega de 19–20/10

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

O projeto referencia `_build_verify\tv_scope_bin_20261019\AccIntegration.WebApp.dll`. Copie para lá o conteúdo de `C:\Dev\N-A-Connect aa\_build_verify\tv_diaria_layouts_20261020\backend-bin\` (ou de um build novo) e rode:

```powershell
dotnet test Tests\NA_Connect.TvPortal.Tests -o <pasta externa>
```

## Pendências

1. Rodar `aplicar.py --conferir`, depois aplicar e rodar `graphify update .`.
2. Publicar o backend de forma controlada. Antes disso o botão fica desabilitado.
3. Validação visual:
   - no editor, Gerar → Salvar em cada exibição. Em "Uma tela por disciplina", conferir a lista de disciplinas do projeto (Norma + descobertas);
   - no player e no Portal TV, olhar a TV na virada da meia-noite e numa segunda-feira.
4. Decisão de negócio (não implementada): hoje "pendente de ontem" = documento programado ontem e **não emitido** na LDP. Atividades intermediárias (ex.: verificação) não têm status de conclusão próprio na fonte oficial. Se a gestão quiser outro critério (ex.: `Weekly_Progress_Status`), é preciso definir qual status comprova a conclusão.
