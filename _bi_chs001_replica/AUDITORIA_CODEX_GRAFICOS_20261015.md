# Auditoria das fontes dos gráficos BI CHS001 — 2026-10-15

## Escopo e conclusão executiva

Esta auditoria cobre as páginas **Resumo 30%**, **Resumo 60%** e **Resumo 90%** e as dependências que também alimentam LDP Engineering, LDP 3D e Programação. Foram usados como referência o PBIX original e sua estrutura extraída em `F:\BI N&A CONNECT`, o código atual do importador e consultas somente leitura ao SQL Server local.

Os gráficos vazios não têm uma causa única. A conclusão é:

1. O `.mpp` oficial já contém a fonte adequada, e o importador já o lê com MPXJ. A perda ocorre depois da abertura do arquivo: o parser não materializa início, início da linha de base, trabalho atual/real/linha de base, parentesco nem séries diárias.
2. As medidas de curva S e avanço semanal dependem de `AssignmentByDay[TimeByDay]` e `AssignmentByDay[Trabalho Real]`. Esses fatos diários não existem no banco atual. Não é possível reproduzir essas medidas com fidelidade usando apenas término e percentuais customizados.
3. A hierarquia Level 5–7 não deve ser inferida pelo texto do EDT ou pela ordem da lista. O PBIX a constrói a partir de `TaskUID` e `TaskParentUID`; o importador descarta o vínculo com o pai.
4. O grupo atual do cronograma está escrito `Emgineering - 30%`, enquanto o PBIX filtra literalmente `Engineering - 30%`. Isso zera o recorte de 30% e, devido aos filtros por exclusão das páginas 60% e 90%, permite que as 196 tarefas desse grupo entrem indevidamente nesses recortes.
5. A ausência de dados é a causa principal dos gráficos vazios. O suporte visual existente não corrige fatos ausentes. Há ainda lacunas separadas de interação, como cross-filter e slicer do tipo Between.

Nenhum arquivo do Claude, dado SQL, PBIX, `.mpp`, sincronizador ou schema foi alterado nesta auditoria.

## Evidências examinadas

### Aplicação

- `Services/CronogramaTipo2/CronogramaTipo2Service.cs`: localiza o `.mpp` mais recente nas pastas oficiais do ACC, baixa para arquivo temporário, chama MPXJ e persiste o snapshot em transação.
- `Services/CronogramaTipo2/CronogramaTipo2Parser.cs`: usa `UniversalProjectReader`, percorre tarefas resumo e folha e extrai IDs, nome, EDT, campos customizados, término e término da linha de base.
- `Models/CronogramaTipo2Registro.cs`: possui os campos legados `PlannedDateStart`, `PlannedDateBaseline`, `ReplannedDate` e `Trabalho`, mas a própria classe registra que eles permanecem nulos no fluxo Tipo 2 por falta de mapeamento validado.
- `Services/CronogramaTipo2/CronogramaTipo2Repository.cs`: não inclui os quatro campos anteriores no `INSERT`; inclui término e término da linha de base. O snapshot completo é transacional.
- `Services/Reports/ScheduleReportRepository.cs`: já consome `VW_Cronograma_Tipo2_Ultima_Versao`, inclusive a coluna `Trabalho`, que hoje chega nula.
- `Controllers/BiChs001Controller.cs`: o contrato de cronograma expõe os dados hoje persistidos, mas não possui fonte para início, trabalho, hierarquia completa ou fatos diários.

### Banco local, somente leitura

Projeto encontrado: `CHS001 - ETHANOL PLANT`, `Project_ID_ACC = b.fcdb7ebd-1b23-440d-a04b-90391098522e`, LDP Tipo 2.

No snapshot mais recente da view `dbo.VW_Cronograma_Tipo2_Ultima_Versao`:

| Verificação | Resultado |
|---|---:|
| Tarefas | 883 |
| Tarefas resumo | 128 |
| Tarefas folha | 755 |
| `PLANNED_DATE_START` nulo | 883 |
| `PLANNED_DATA_BASELINE` nulo | 883 |
| `REPLANNED_DATE` nulo | 883 |
| `Trabalho` nulo | 883 |
| `Termino` nulo | 0 |
| `Termino_Linha_Base` nulo | 0 |

A view seleciona o snapshot de maior `Data_Sincronizacao` por projeto; ela não cria nem transforma fatos temporais. A tabela `dbo.Cronograma_Projetos` não possui colunas de UID do pai, nível de outline, trabalho real, trabalho da linha de base ou fatos diários.

O registro de EDT `1.5`, UID `3917`, aparece como `Emgineering - 30%`. Os grupos `1.6` e `1.7` aparecem como `Engineering - 60%` e `Engineering - 90%`.

### PBIX original

- A consulta Power Query `AssignmentByDay` lê a view `MSP_EpmAssignmentByDay_OlapView` de uma exportação Access do Project. No cache original existem 10.186 linhas e 93 colunas; os campos determinantes são `TaskUID`, `TimeByDay`, `Trabalho Real` e trabalhos da linha de base.
- A consulta `WBX` contém 755 tarefas folha e 236 colunas. Ela chama `GeraWBS(Task, "TaskUID", "TaskParentUID", "Nome")`, filtra `IsLeaf = true` e produz `NodeKey`, `FirstParentKey`, `Path`, `HierarchyDepth` e `Level1` a `Level7`.
- Relações semânticas: `AssignmentByDay[TaskUID]` muitos-para-um com `WBX[NodeKey]`; `AssignmentByDay[TimeByDay]` muitos-para-um com `dCalendario[Data]`.
- O cache do PBIX é evidência de validação, não fonte aceitável para produção. A atualização deve continuar partindo do `.mpp` oficial.

## O que o importador preserva e o que descarta

| Informação do cronograma | Situação atual | Consequência |
|---|---|---|
| ID e UID da tarefa | Preservada | Chave disponível para ampliar o modelo |
| Nome, EDT e tarefa resumo | Preservados | Estrutura parcial disponível |
| Término atual e da linha de base | Preservados | Cartões de datas finais podem ser reais |
| Campos customizados de gate, disciplina, status e percentuais | Preservados | Filtros documentais e alguns cartões funcionam |
| Início atual | Lido pelo arquivo, não extraído/persistido | `% Planned` e curva reprogramada inviáveis |
| Início da linha de base | Lido pelo arquivo, não extraído/persistido | Curva baseline e cartões de prazo incompletos |
| Trabalho atual | Snapshot auxiliar existe no JSON, coluna tipada não é populada | Denominadores e ponderações não são auditáveis pela API |
| Trabalho real e trabalho da linha de base | Não persistidos em campos próprios | `% Complete` e curvas realizadas inviáveis |
| UID do pai e outline level | Não persistidos | Level 5–7 e filtros de gate não podem ser reconstruídos com segurança |
| Trabalho diário por atribuição/tarefa | Não extraído nem persistido | `AssignmentByDay`, semanal e acumulado ficam vazios |

O `__NA_CONNECT_WORK_V1` gravado no JSON customizado demonstra que o parser já consegue ler duração em horas. Ele não substitui colunas tipadas nem fatos diários e não contém a distribuição temporal necessária.

## Medidas originais e viabilidade

As definições DAX completas e seus caminhos de origem estão catalogados em `MAPA_MEDIDAS_DAX.md`. Abaixo está a decomposição operacional, preservando diferenças entre medidas de mesmo nome.

### Cumulative Progress / ACC Planned

**Fórmula original:** para cada tarefa folha do gate exato em `WBX[Level4]`, calcula progresso linear entre `Início` e `Término` até o fim da semana selecionada; limita cada tarefa entre 0 e 1; pondera por `WBX[Trabalho]`; divide pela soma do trabalho no mesmo contexto.

**Dependências:** Level4 confiável, início atual, término atual, trabalho e calendário.

**Disponível agora:** término e uma classificação parcial inferida. **Ausente:** início e trabalho tipados, além do parentesco oficial. **Diagnóstico:** ausência de dados; não é erro de renderização. **Correção:** importar os campos nativos e gerar Level1–7 pelo pai.

### % Planned / Progress Curve planejada

Há duas famílias distintas:

- **Baseline:** interpolação ponderada por trabalho entre início e término da linha de base.
- **Reprogramado:** interpolação ponderada por trabalho entre início e término atuais.

Ambas avaliam uma data de corte, limitam a contribuição de cada tarefa a `[0,1]` e somam `progresso_tarefa × trabalho_tarefa / trabalho_total`.

**Ausente:** início baseline, início atual e trabalho. O término já existe. **Correção:** ampliar a importação; não derivar o início a partir do término ou duração agregada.

### % Complete / ACC Complete

**Fórmula original:** `Trabalho Real acumulado / Trabalho Linha Base`, com limite superior de 100%. O numerador vem do fato diário relacionado por TaskUID; o denominador vem da linha de base no contexto filtrado.

**Ausente:** trabalho real diário e trabalho da linha de base. O percentual customizado da tarefa não é substituto semântico para horas reais acumuladas. **Diagnóstico:** ausência de fonte.

### % Deviation / ACC Deviation

**Fórmula original:** `% Complete - % Planned` no mesmo gate e data de corte.

**Dependência transitiva:** todas as fontes das duas medidas anteriores. **Diagnóstico:** cálculo bloqueado por dados ausentes; exibir zero seria incorreto.

### Weekly Progress / avanço semanal planejado

**Fórmula original:** diferença entre os acumulados planejados no fim da semana atual e no fim da semana anterior, mantendo o mesmo denominador de trabalho e contexto de gate.

**Ausente:** início e trabalho. **Observação:** não corresponde a dividir o avanço acumulado pelo número de semanas.

### Weekly Progress / avanço semanal realizado

**Fórmula original:** soma de `AssignmentByDay[Trabalho Real]` dentro da semana dividida pelo trabalho da linha de base. A semana que contém a data de corte aplica a regra específica do modelo; semanas futuras permanecem em branco.

**Ausente:** fato diário, baseline work e data de corte equivalente. **Diagnóstico:** fonte ausente.

### Weekly Deviation

**Fórmula original:** avanço semanal realizado menos avanço semanal planejado. Herda as dependências e não pode ser calculado com os percentuais customizados atuais.

### Progress Curve realizada / avanço acumulado

**Fórmula original:** soma cumulativa de `AssignmentByDay[Trabalho Real]` até a data do eixo dividida pelo trabalho da linha de base, limitada a 100%; datas posteriores ao corte ficam em branco conforme a medida original.

**Ausente:** fato diário e baseline work. O valor final de trabalho real não permite reconstruir a forma histórica da curva.

### Baseline Start/Finish e Start/Finish

**Fórmula original:** mínimos dos inícios e máximos dos términos no contexto filtrado. Desvios de prazo usam diferenças de datas; prazo restante usa a data de corte do modelo.

**Disponível:** ambos os términos. **Ausente:** ambos os inícios. Portanto, somente cartões baseados exclusivamente em máximo de término são hoje completos.

### Curvas 3D

As versões 3D reaplicam as mesmas fórmulas, mas filtram documentos com `LDP[FORMAT] = "3D"` e transferem os números documentais para WBX com `TREATAS`, além do filtro de gate em Level4. Elas exigem as mesmas fontes temporais; uma curva geral funcionando não garante que a curva 3D esteja correta.

## Classificação das falhas observadas

| Categoria | Evidência | Tratamento |
|---|---|---|
| Ausência de dados | starts, work, baseline work, actual work, fatos diários, pais | Ampliar importador e persistência |
| Erro de filtro/dado mestre | `Emgineering - 30%` versus filtro literal | Corrigir fonte ou aprovar alias explícito |
| Cálculo indisponível | curvas e KPIs dependem das fontes anteriores | Implementar após fatos auditáveis existirem |
| Renderização | visuais suportados podem desenhar valores já disponíveis | Não resolve curvas vazias |
| Interação | cross-filter e slicer Between ainda não equivalentes | Pode ser tratado no frontend sem fabricar dados |

## Divergência dos gates

O PBIX compara o nome de Level4 de forma literal. Não há evidência de normalização tolerante.

- **Resumo 30%:** o filtro `Engineering - 30%` não encontra `Emgineering - 30%`; indicadores e gráficos do grupo ficam vazios.
- **Resumo 60% e 90%:** os filtros originais usam exclusões. Como a grafia incorreta não pertence à lista de exclusão esperada, as 196 tarefas sob o grupo incorreto podem contaminar os dois conjuntos.
- **Risco de normalização automática:** correção fuzzy, troca global ou equivalência por prefixo pode unir grupos homônimos de outros projetos e alterar a semântica histórica.

### Correspondência segura proposta para aprovação

Opção preferencial: corrigir o nome na fonte oficial `.mpp` para `Engineering - 30%` e deixar a próxima importação preservar o valor literal. Isso mantém o contrato do PBIX.

Se a fonte não puder ser corrigida, criar configuração versionada e restrita ao projeto CHS001, contendo valor bruto e valor canônico. O alias deve ser aplicado de modo simétrico ao filtro inclusivo de 30% e às exclusões de 60%/90%, sem apagar o nome bruto. Até a aprovação, manter a comparação literal e exibir diagnóstico de gate ausente.

## Solução de integração proposta

### 1. Ampliar o parser existente

Reutilizar `CronogramaTipo2Parser` e a mesma leitura MPXJ:

- `Start` atual → início reprogramado/atual, após validar a semântica da coluna legada `REPLANNED_DATE`.
- `BaselineStart` → `PLANNED_DATA_BASELINE`.
- `Work` convertido para horas → `Trabalho`, reutilizando a conversão já utilizada por `CronogramaTipo2Work`.
- `BaselineWork` e `ActualWork` → novos campos tipados.
- `ParentTask.UniqueID` e `OutlineLevel` → novos campos tipados.

`PLANNED_DATE_START` precisa de decisão semântica: o nome sugere planejamento, mas o PBIX distingue início atual de início baseline. Não preencher duas colunas com o mesmo valor para satisfazer a tela.

### 2. Gerar a hierarquia original

Persistir UID do pai e nível. No backend, construir a cadeia do próprio snapshot e materializar Level1–7 para leitura, ou retorná-la já resolvida pela API. Validar ciclos, pais ausentes e tarefas órfãs. Não inferir pais pelo ponto no EDT nem pela posição no arquivo.

### 3. Materializar fatos diários

O MPXJ expõe séries temporais nas atribuições de recursos e permite agregá-las por tarefa e intervalo diário. A documentação oficial descreve séries de trabalho real, restante, total e baseline e a aplicação do calendário efetivo da atribuição. A implementação deve usar a API compatível com a versão instalada `MPXJ.Net 13.5.0`; as versões atuais também oferecem métodos parametrizados para fatos temporais.

Criar uma tabela de snapshot, por exemplo `dbo.Cronograma_Tarefa_Dia`, com:

| Campo | Finalidade |
|---|---|
| `Project_ID_ACC` | Isolamento do projeto |
| `Data_Sincronizacao` | Mesma versão do snapshot de tarefas |
| `UID_Tarefa` | Relação com a tarefa |
| `Data` | Grão diário |
| `Trabalho_Real_Horas` | Numerador das curvas realizadas |
| `Trabalho_Planejado_Horas` | Auditoria e usos futuros, se presente no MPP |
| `Trabalho_Linha_Base_Horas` | Curva baseline diária, se presente |
| identificação da versão/arquivo | Rastreabilidade operacional |

Somar atribuições distintas apenas dentro da mesma combinação tarefa/data. Não distribuir o total da tarefa uniformemente, não interpolar trabalho real e não preencher dias sem fonte. Gerar intervalos diários usando o calendário efetivo do Project/atribuição, pois fins de semana e calendários especiais alteram a série.

Persistir tarefas e fatos diários na mesma transação. Para cerca de 10 mil fatos por snapshot, usar operação em lote; o `INSERT` linha a linha atual é adequado para 883 tarefas, mas ampliá-lo diretamente aumenta tempo de bloqueio e risco de snapshot parcial.

Referências técnicas do MPXJ: `https://www.mpxj.org/howto-use-timephased/` e `https://www.mpxj.org/apidocs/org/mpxj/ResourceAssignment.html`.

### 4. Banco e views

Alterações propostas, todas dependentes de aprovação:

- Novas colunas nullable em `Cronograma_Projetos`: `UID_Tarefa_Pai`, `Nivel_Outline`, `Trabalho_Linha_Base`, `Trabalho_Real`.
- Uso validado das colunas existentes `PLANNED_DATA_BASELINE`, `REPLANNED_DATE` e `Trabalho`.
- Nova tabela diária descrita acima.
- Índice de tarefa `(Project_ID_ACC, Data_Sincronizacao, UID_Tarefa)`.
- Índice diário `(Project_ID_ACC, Data_Sincronizacao, Data, UID_Tarefa)`.
- View de última versão diária unida pela mesma `Data_Sincronizacao` do snapshot de tarefas.

As migrações devem ser aditivas e nullable. Não há base para retropreencher snapshots históricos; históricos anteriores devem continuar marcados como fonte indisponível.

### 5. API e cálculo

Reaproveitar o controlador e a autorização já existentes. Há duas superfícies úteis:

- ampliar `/api/bi-chs001/cronograma` com campos opcionais de tarefa e hierarquia;
- adicionar recurso filho de série diária, ou calcular curvas no backend e retornar pontos auditáveis por data/gate.

Para desempenho, o backend pode retornar séries agregadas. Deve manter um contrato de leitura dos fatos/diagnóstico que permita conciliar cada ponto com snapshot, gate, data e horas. Todos os recortes devem usar a mesma versão de tarefas e fatos diários.

## Riscos e controles

| Risco | Controle proposto |
|---|---|
| Unidade de duração diferente de horas | Converter via propriedades/calendário do projeto e testar contra totais do MPP/PBIX |
| Dupla contagem de tarefa resumo e folhas | Curvas usam tarefas folha; validar `IsLeaf` pela hierarquia |
| Soma duplicada de atribuições | Agregar por UID da tarefa e dia somente após ler cada assignment uma vez |
| Snapshot de tarefas sem fatos correspondentes | Uma transação e chave de sincronização comum |
| Crescimento do banco | Índices, inserção em lote e política explícita de retenção |
| Mudança em relatórios existentes | Campos novos nullable e testes de regressão da view e `ScheduleReportRepository` |
| Alias de gate afetar outros projetos | Configuração por projeto, versionada, preservando valor bruto |
| Cache PBIX virar fonte operacional | Usá-lo somente como oráculo de teste e conciliação |

## Validação antes de ativar

1. Criar um teste de parser com uma cópia controlada do `.mpp`, verificando UID, pai, nível, starts e trabalhos conhecidos.
2. Conciliar contagem: 883 tarefas totais, 755 folhas e hierarquia sem ciclos.
3. Conciliar somas de trabalho atual, real e baseline entre MPXJ, snapshot SQL e PBIX original.
4. Conciliar `AssignmentByDay` por TaskUID/data em amostra e no total. O cache original contém 10.186 linhas, mas esse número é referência do arquivo original, não constante funcional.
5. Recalcular medidas para 30%, 60% e 90% e comparar ponto a ponto, incluindo semana de corte e datas futuras em branco.
6. Testar 3D com `FORMAT = 3D` e transferência por número documental.
7. Testar tarefas sem atribuição, sem baseline, com calendário especial, resumo, órfãs e com mais de uma atribuição.
8. Validar que relatórios atuais de término, LDP, Timesheet, HH e custos não mudaram.

## Plano objetivo para concluir as dez telas

1. Aprovar a semântica dos campos, o schema aditivo, a tabela diária e a resolução do typo de gate.
2. Implementar e testar a leitura MPXJ dos campos de tarefa, parentesco e séries por atribuição.
3. Persistir o snapshot completo de modo atômico e criar views de última versão coerentes.
4. Ampliar a API com tarefas, hierarquia, disponibilidade de fonte e séries/curvas.
5. Reproduzir as medidas conforme o DAX original, sem substituir trabalho por percentual customizado.
6. Conciliar CHS001 com o PBIX página a página: primeiro Resumos 30/60/90, depois Engineering 30/60/90, 3D 30/60/90 e Programação.
7. Só então ativar a sincronização controlada e validar atualização a partir de uma nova versão oficial do `.mpp`.

## Ações disponíveis ao Claude agora

Sem alterar banco nem importação, o Claude pode:

- manter filtros literais e sinalizar a divergência `Emgineering`/`Engineering`;
- exibir “fonte temporal indisponível” em vez de zero para curvas e KPIs sem fatos;
- usar término e término baseline nos indicadores que dependem exclusivamente desses campos;
- preservar os nomes brutos e a data/versão do snapshot na interface;
- implementar a estrutura das fórmulas e testes usando o cache do PBIX apenas como oráculo de teste;
- corrigir interações e renderização que independem das fontes, como cross-filter e slicer Between;
- preparar contratos frontend opcionais para os novos campos sem simular valores.

Dependem de aprovação: preencher colunas legadas com nova semântica, criar colunas e tabela diária, mudar o processo de importação, executar sincronização/backfill, corrigir o `.mpp` oficial ou introduzir alias canônico e publicar/reiniciar serviços.

