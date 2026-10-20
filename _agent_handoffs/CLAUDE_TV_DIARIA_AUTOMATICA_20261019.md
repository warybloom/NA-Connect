# N&A Connect - TV Diária Automática (Botão simples)
Tarefa 19/10/2026. Repositório C:\Dev\N-A-Connect aa\N-A-Connect

Pedido do usuário: Quer configurar UMA VEZ uma apresentação rodando sempre, com 01 Visão Geral consolidada e em seguida uma tela por projeto com análise diária por disciplina: o que está PREVISTO HOJE e o que ficou PENDENTE de ONTEM. As datas mudam automaticamente todos os dias, não quer operadores clicando. Na configuração da TV deseja apenas 1 botão "Gerar telas diárias automáticas" dos projetos selecionados ou de todos os projetos permitidos, a critério do usuário. Reutilizar o layout diário já publicado.

ARQUITETURA ATUAL:
- Frontend ClientApp/src/features/settings/TelasTvConfigPage.tsx possui editor de apresentações, seletor de projetos, adicionar slides em lote, itensParaAdicionar; tvLogic.ts tem TvSlide.dataReferencia, tvScreens.tsx screenId gestao-a-vista-programacao-diaria; TvPlayerPage.tsx monta slide; GestaoVistaJamilePage.tsx Programming modo diario.
- Backend Services/Tv/TvConfiguration.cs valida data ISO de TvSlide, Services/Tv/TvTelas.cs cataloga telas, Portal/TvPortalResources.cs e Portal/TvPortalMiddleware.cs validam período/projeto/slide no portal.
- 01 Visão Geral já corrigida para mesma fonte da 02 Programação via dbo.tb_WeeklyEngineeringSchedule; não reverter. 02 semanal e diário de data FIXA devem continuar funcionando.
- Usuário Emídio/perfil9 só vê seus projetos, não Gestão à Vista Tipo2 nem Resumo TV Testes. Não modificar permissões, SQL ou normalizador. Codex e outros agentes trabalhando: isolar para evitar conflito.

IMPLEMENTAÇÃO:
1) Botão único no editor de TV "Gerar telas diárias automáticas", com controle mínimo Selecionados / Todos permitidos. Se Selecionados, um slide por projeto selecionado existente. Se Todos, usar SOMENTE projetos autorizados para esta tela pelo filtro efetivo useProjetosPermitidosTv + podeTela (NUNCA todos brutos). Mostrar contagem e aviso para limite atual 50; nunca parcial silencioso; não sobrescrever slides existentes; não criar duplicatas mesma tela automática + projeto; o botão só altera RASCUNHO, o usuário clica SALVAR.
2) Apresentação deve ter 01 Visão Geral antes das análises por projeto, mas preservar a ordem já configurada: se visão geral já está presente não duplicar; se não, inseri-la no início apenas se usuário tem permissão de Visão Geral em todos projetos escolhidos (senão avisar). Não mudar telas existentes.
3) Criar modo AUTO explícito e validado para a tela diária (preferência manter mesmo screenId e DataReferencia="AUTO" ou campo novo backward compatible). Datas FIXAS ISO continuam intactas; ausência de data continua erro. Em AUTO, cálculo HOJE e ONTEM pela data civil da Bahia (America/Bahia), não data congelada de configuração. Em TV aberta 24/7 atualizar no virar do dia sem F5, e refetch dados ao virar dia; timers leves, não criar loop.
4) Tela AUTO dividida POR DISCIPLINA com seções PREVISTO HOJE (mesma programação semanal/importação, documentos, título, hh, recursos, status) e PENDENTE ONTEM (apenas ontem, não histórico), com HH/quantidades. Use as regras de deduplicação de 02 e reconciliação de emissão de 01; NÃO chamar um item de atrasado sem comprovação de não-emissão. Quando status inconclusivo, seção SEGUIMENTO SEM COMPROVAÇÃO/INDETERMINADO; datas/projetos separados. Quando vazio mostrar mensagem explícita. Fonte somente banco programação semanal e status LDP oficial; não inventar dados.
5) Domingo / segunda: consulta pode cruzar semanas; buscar janela que inclua ontem e hoje conforme contrato backend (máx 62 dias), sem duplicar. Se AUTO, PORTAL TV só permite intervalo estritamente calculado no servidor (timezone Bahia), read-only, escopo slide e projeto; validar outras URLs não passam. Não alterar autorização de slides fixos; JSON antigo preservado.
6) Editor exibe AUTO como "Hoje + pendências de ontem (automático)" (não input type=date com valor AUTO). Player mostra projeto e datas vigentes. Um slide por projeto pode ser repetido com configurações fixas diferentes, mas nunca duplicar AUTO idêntico.
7) Tests: seleção versus todos permitidos, duplicidade, limite 50, dois projetos, transição meia-noite/domingo, emissão ontem e status desconhecido, outras apresentações e projetos negados no TV Portal, data fixa anterior e JSON legado, integridade do agrupamento. TypeScript, npm tests pertinentes, Vite build, .NET build isolado e TvPortal tests. Não publicar se falhar.
8) Trabalho ISOLADO em worktree; backup/manifesto e script seguro para integrar verificando conflitos com checkout principal. Não sobrescrever Claude/Codex; não modificar SQL, permissões, LDP ou robôs; não reiniciar processos críticos. Gerar _agent_handoffs/RESULTADO_TV_DIARIA_AUTOMATICA_20261019.md com entrega, testes, instruções e status real; não afirmar pronto se não estiver aplicado/deploy.

Comece implementação agora; foco em botao SIMPLES e automação robusta, sem tela nova complexa.


## ADENDO DE REQUISITO DO USUÁRIO — MESMO VISUAL + OPÇÃO DISCIPLINA POR CARD (19/10/2026 22h)
Solicitação literal: "seguir o mesmo padrao visual, e identico ao que remos porem diario e detalhe provavelmente meu chefe queira ate mesmo separado cada disciplina individualmente por card ja deixa a opcao pronta".

IMPORTANTE:
- A nova tela diária deve ser **VISUALMENTE IDÊNTICA À 02 PROGRAMAÇÃO** que já existe (estrutura, cores, tipografia, cards, tabela, alinhamentos, indicadores e responsividade). Reutilizar o mesmo componente / estilos, NÃO redesenhar.
- Adicionar ao editor TV a opção simples de visualização para o botão "Gerar telas diárias automáticas":
  * "Padrão" — uma tela diária por projeto, idêntica à Programação atual, separando Hoje e Pendências de Ontem, com disciplinas agrupadas como já estão;
  * "Cards por disciplina" — uma tela diária por projeto, com um card dedicado para CADA disciplina do projeto, mesmo design e componentes existentes, mostrando previsão de hoje e pendências comprovadas de ontem daquela disciplina. Não combinar projetos. Campos mantidos.
- Antecipar também a possibilidade de **uma tela/slide separado por disciplina**, sem obrigar o chefe a usar. Se adicionado, que seja um modo explícito "1 tela por disciplina" na configuração, sem complicar demais e respeitando limite máximo de slides. A opção principal "Cards por disciplina" tem prioridade.
- Não inventar disciplinas: usar Normalizador/catálogo real e disciplinas dos documentos; itens sem mapeamento ficam diagnosticados, não desaparecem. Datas AUTO viram sozinhas.
- Ao usar opção por disciplina, não repetir os totais do PROJETO em cada card; cada card tem seus próprios documentos, HH, atrasos e status. Se a tela permitir comparação consolidada, indicar total do projeto à parte.
- Persistir o modo de layout escolhido por SLIDE de TV (compatível com configuração antiga e TV Portal) para poder misturar Padrão e Cards por disciplina na mesma sequência. Não quebrar slides fixos existentes.
- NÃO reimplementar UI do zero. Priorizar simples adição de seletor "Exibição: Padrão / Cards por disciplina" ao lado do botão gerador, respeitando permissões.
- Incluir testes visuais/contrato de compartilhamento de componente, classificação por disciplina, HH não duplicado e total. Ao apresentar progresso, explicar com precisão o que foi implementado versus apenas planejado.

Este adendo faz parte integral da tarefa. O usuário deseja esse modo pronto AGORA; não deixar somente como sugestão.
