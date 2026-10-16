# AUTORIZAÇÃO TOTAL DO USUÁRIO — BI CHS001 — 15/10/2026

**DECISÃO EXPRESSA DO USUÁRIO NESTE CHAT:** "sim, tem autorização total para ambas as ações ou qualquer outra que precise para resolver esse problema".

Objetivo: FAZER FUNCIONAR, EM DADOS REAIS ATUALIZÁVEIS, os Resumos 30/60/90, curvas S, progresso semanal e acumulado, cards e matrizes das 10 telas BI CHS001; manter somente a primeira logo N&A no extremo esquerdo. Não basta criar layout com selos PENDENTE.

Você (CLAUDE BI) é o ÚNICO IMPLEMENTADOR da cadeia de fontes/visualização nesta etapa: Services/CronogramaTipo2/*, Models/CronogramaTipo2Registro.cs, Controllers/BiChs001Controller.cs, módulos React biChs001, migrações SQL, scripts e infraestrutura de deploy. O Codex terá ESCOPO INDEPENDENTE: testes e auditoria/validação, sem alterar esses arquivos. Existe outro Claude atuando no tema/disciplinas BML011.013: NÃO TOCAR em seus arquivos.

LEITURA OBRIGATÓRIA: _bi_chs001_replica/AUDITORIA_CODEX_GRAFICOS_20261015.md, _bi_chs001_replica/STATUS_POS_AUDITORIA_CLAUDE.md, _bi_chs001_replica/MAPA_MEDIDAS_DAX.md. O Codex validou origem MPXJ, gaps de início/start LB/work real/baseline, UID pai, series temporais. ATENÇÃO: suas investigações recentes mostram que AssignmentByDay no cache PBIX tem datas dominicais (grão semanal), apesar de nome "ByDay": verificar e conciliar sem arredondar indevidamente.

AUTORIZADO: implementar parser MPXJ, adicionar colunas nullable/tabelas e views com migração ADITIVA, executar script SQL no banco local após BACKUP verificável, criar snapshot transacional das tarefas + fatos temporais, testar sobre cópia .mpp e dados reais, corrigir tipografia "Emgineering - 30%" no .mpp oficial (com cópia/versionamento e garantia de arquivo correto) OU alias explicito por projeto versionado sem afetar outros, rodar sincronização controlada CHS001, publicar/reiniciar backend/frontend com segurança, validar em navegador autenticado se sessão disponível, remover logos duplicadas e consertar graficos. O usuário não precisa confirmar novamente essas ações de escopo. Não alterar credenciais nem operar módulos alheios.

REQUISITOS DE SEGURANÇA:
1. Antes: git status, inventário de outros agentes, backup .bak DB_NA_CONNECT (verificar conclusão e local), backup seletivo de fontes, .mpp e binários; script DOWN/reversão verificável.
2. Testar migrações idempotentes contra cópia/teste antes de produção; esquema compatível com SQL existente. Preserve snapshots históricos; scripts aditivos/nullable. Não excluir/zerar horas/custo/timesheet/LDP e não disparar workers globais.
3. Extrair fields nativos Start, BaselineStart, Work, ActualWork, BaselineWork, ParentTaskUID, OutlineLevel e timephased das assignments com calendário real. Agregar por UID tarefa + período da medida e conciliar com DAX original. Não inventar início, percentual, datas, horas ou interpolar trabalho real. Respeitar folhas vs tarefas resumo e distintas atribuições.
4. Validar gate 30 com a forma exata do PBIX; 60/90 usam filtros por exclusão. Corrigir origem com versionamento ou alias explícito só CHS001, nunca fuzzy global.
5. Reaproveitar o importador Tipo 2 e o endpoint existente; criar API de fatos/séries coerente por snapshot. Garantir acesso com permissão e consultas paginadas/agregadas.
6. Só implantar/reiniciar com compilação/testes/rollback, Worker__RunOnStartup=false e checagem da sincronização e dos robôs; preservar serviços 5173/5277, Cloudflare e dados de produção.
7. Testes de integração e navegador REAL: retorno endpoint, filtro por projeto/gate, Resumo 30/60/90, gráficos com pontos reais, período semanal, matriz e seis LDP + Programação sem regressão. Screenshot comparado e resultado executado, não só build. Erros transientes corrigir sem esconder selos.
8. Se impedimento técnico genuíno (fato temporal ausente do .mpp mais recente, dependência sem acesso), registrar campo, arquivo, falha e alternativa confiável; NÃO afirmar conclusão ou gerar fake.

RELATÓRIO CONTÍNUO: atualizar _bi_chs001_replica/STATUS_IMPLANTACAO_GRAFICOS_20261015.md com etapas concluídas, SQL antes/depois, backups/rollback, arquivo .mpp/versão, páginas validadas, evidências visuais, endpoints e bloqueios concretos. Trabalhar diretamente; não parar no planejamento.

**IMPORTANTE:** o usuário não quer mais telas vazias. ENTREGAR execução funcional final com dados reais.