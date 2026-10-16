# EXECUÇÃO AUTÔNOMA / PASSAGEM DE TURNO — N&A CONNECT BI CHS001 — 15/10/2026

## ORDEM DIRETA DO USUÁRIO
"Eu vou sair então se tiver algum prompt para o codex mande para o claude, faça tudo que quando chegar eu vejo."

Você, CLAUDE BI CHS001, fica RESPONSÁVEL por conduzir e concluir a correção das **10 telas prioritárias** e coordenar a auditoria paralela do Codex, sem novos pedidos de autorização ao usuário dentro do escopo autorizado. Se um bloqueio real exigir ação externa (credenciais não disponíveis, fonte inexistente, permissão negada), documente o bloqueio e continue nas partes independentes. Trabalhe na sessão atual enquanto estiver disponível; não prometa execução se terminal/processo for encerrado.

## REGRA MAIS IMPORTANTE: NÃO MEXER NO QUE JÁ EXISTE E FUNCIONA
**Leia primeiro** `_bi_chs001_replica/DIRETRIZ_ISOLAMENTO_OBRIGATORIA_20261015.md`. Esta regra revoga qualquer permissão anterior para editar o importador Tipo 2, o cronograma oficial .mpp, schema legado, sincronizações, LDP, Timesheet, custos, HH, qualidade, dashboards e identidade visual compartilhada.

Crie recursos NOVOS, ISOLADOS para o módulo BI CHS001. Somente leitura dos dados, serviços e arquivos legados, inclusive ACC/.mpp. Não mude os arquivos legados para corrigir o typo. Exemplo de arquitetura aprovada:
- `Services/BiChs001Schedule/` novo leitor MPXJ independente com acesso de leitura/cópia ao .mpp e validação de versão/checksum
- `bi_chs001` (schema/tabelas/views SQL novos) para snapshots de tarefas, UID pai, níveis, início/termino atual e LB, trabalho real/planejado/baseline, e fatos timephased por dia/semana; sem alterar `dbo.Cronograma_Projetos` nem views/colunas já utilizadas
- endpoints novos em controlador dedicado do BI; leitura de sistemas existentes somente SELECT
- frontend somente `ClientApp/src/features/production/biChs001/` e composição da página BI para remover LOGOS REPETIDAS (deixar logo global primeira na esquerda). Nunca alterar `ThemeLogo` ou trabalho paralelo BML011.013
- alias explícito, auditável e limitado ao projeto CHS001: `Emgineering - 30%` -> `Engineering - 30%` apenas na camada de BI. Preserve o rótulo original, trate filtro inclusivo 30 e exclusões 60/90 com testes.

## EVIDÊNCIAS JÁ CONFIRMADAS
- `_bi_chs001_replica/AUDITORIA_CODEX_GRAFICOS_20261015.md`: importador atual descarta starts, work e UID pai e não persiste séries temporais; não alterá-lo.
- `_bi_chs001_replica/STATUS_POS_AUDITORIA_CLAUDE.md`: cards de término têm fonte, mas curvas, avanço semanal/acumulado, matriz e certos KPIs necessitam timephased.
- `_bi_chs001_replica/MAPA_MEDIDAS_DAX.md`, PBIX fonte somente leitura em `F:\BI N&A CONNECT\CHS001-000-PM0-RT-002-01.pbix`. Cache PBIX pode ser oráculo de testes, nunca fonte ativa.
- Sua investigação recente encontrou que o cache `AssignmentByDay` contém períodos com datas dominicais (semanal). Validar semântica original e aplicar agregação correta de calendário/atribuição; séries de trabalho real nunca devem ser inventadas ou interpoladas.
- 883 tarefas, 755 folhas no cronograma; verificar casamento por identificadores apropriados e hierarquia; `TaskUID` no arquivo novo pode divergir historicamente do cache, portanto NÃO forçar joins impossíveis; conciliar por evidência.

## ORDEM DE EXECUÇÃO
1. CONFERIR estado atual: qual dos Claudes está editando o quê, `git status`, arquivos não versionados, processos e versão do serviço. Não usar reset/clean ou rollback cego. Registrar inventário e backups em caminho exclusivo BI.
2. Implementar importador novo MPXJ read-only, validando dados nativos: Task UID, parent UID/outline, start, baseline start, finish, baseline finish, work/actual/baseline work, resource assignments + timephased, unidades e calendários. Comparar com PBIX original. Se dado timephased não existir no .mpp oficial mais recente, registrar fato e alternativa verificável, não estimar.
3. Criar migrações aditivas APENAS do namespace BI isolado, com script idempotente, backup SQL verificável antes, dry-run em clone e rollback próprio. Nunca ALTER/UPDATE/DELETE dos objetos operacionais antigos.
4. Persistir snapshots completos, tarefas + fatos temporais em uma transação coerente por versão e projeto. Aplicar para CHS001 apenas. Garantir índices, isolamento por projeto e diagnóstico de datas/versionamento.
5. Criar API dedicada, com permissões existentes por leitura, testes de contrato e endpoints validáveis.
6. Completar DAX equivalente dos Resumos 30/60/90, Cumulative/Weekly/Progress Curve, %Planned, %Complete, %Deviation, matriz Level5-7 e curvas 3D. Preservar filtros por exclusão 60/90, slicers, crossfilter e páginas LDP/Programação; sem mocks em produção.
7. Remover as duas logos repetidas somente do layout de páginas BI; preservar a logo original da sidebar à esquerda. Testar claro/escuro.
8. Testar .NET/TS/Vite, regression, endpoints reais, navegador autenticado se já houver sessão, capturas antes/depois das 10 telas, correspondência com PBIX. Em falha de navegador autenticado, registrar limitação sem dizer que validou.
9. Publicar novos artefatos com plano de rollback. Antes de reiniciar serviço: verificar worker e robôs, proteger portas 5173/5277 e Cloudflare, backup de binários, confirmar retorno da aplicação. Usuário deu autorização de implantação SOMENTE nos recursos novos isolados.
10. Não iniciar outras 13 telas até haver paridade funcional validada nas 10 prioritárias.

## COORDENAÇÃO COM CODEX — VOCÊ É O INTERMEDIÁRIO
O usuário pediu para não precisar receber prompts do Codex. Após terminar uma etapa implementada e testável, **você mesmo deve preparar e enviar ao Codex** a seguinte auditoria independente. Se o Codex em execução estiver ocupado, não interrompa; se não houver sessão acessível e o CLI permitir, use execução não interativa em trabalho paralelo de SOMENTE LEITURA. NUNCA atribua edição dos mesmos arquivos simultaneamente. Se não puder conectar ao Codex, escreva `_bi_chs001_replica/PROMPT_CODEX_VALIDACAO_FINAL.md` e registre honestamente que o envio não ocorreu, mantendo testes independentes locais.

### TEXTO A DELEGAR AO CODEX
"Audite SOMENTE LEITURA o módulo isolado BI CHS001 implementado pelo Claude. Compare contratos, migrações, cálculos DAX, séries temporais timephased e filters com PBIX. Verifique 10 páginas, Resumos 30/60/90, 6 LDP, Programação; falhas de série calendário semanal/domingo, aliases restritos, UI, duas logos extras, permissões e regressões. Confirme que nenhum arquivo/tabela/view/importador/MPP operacional legado foi modificado. Valide backups SQL e rollback; reporte erros e gravidade com evidências e scripts de testes que não alterem arquivos compartilhados. Relatório único _bi_chs001_replica/CODEX_VALIDACAO_FINAL_ISOLAMENTO.md. NÃO editar arquivos nem fazer deploy, nem SQL writes."

Se Codex levantar problemas, VOCÊ corrige e pede nova verificação READ ONLY dentro do possível.

## RELATÓRIOS OBRIGATÓRIOS PARA QUANDO O USUÁRIO VOLTAR
Arquivo único principal: `_bi_chs001_replica/RELATORIO_FINAL_IMPLEMENTACAO_BI_CHS001_20261015.md` com:
- status exato de CADA uma das 10 telas e CADA curva/card/matriz: funciona com dados reais / pendente com evidência
- arquivos e schema/tabelas NOVOS, inventário de arquivos/objetos existentes preservados
- backups .bak, logs migração, restore/rollback testado (sem mexer na produção para testar restore)
- testes compilação, frontend, SQL readonly de conciliação, DAX por ponto semanal, captura de tela
- endereço de teste, porta de backend, status de worker e serviços
- status da revisão Codex (auditado, pendente ou não acessível)
- eventuais limitações REAIS, sem declarar pronto só por build
- comandos de recuperação / reinício seguros para o usuário.

**ENTREGA é sistema funcional testado; NÃO apenas relatório ou plano. A autorização é suficiente para tarefas ISOLADAS e reversíveis. Não sobrescrever mudanças de outros agentes. Não consumir esforço em Graphify se bloqueado.**
