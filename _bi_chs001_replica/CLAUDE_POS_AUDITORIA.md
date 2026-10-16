# CLAUDE — SEGUNDA ETAPA: fechar paridade das DEZ telas do BI CHS001 após auditoria Codex

Contexto: o usuário autorizou você a trabalhar diretamente no terminal, sem pedir permissões repetidas, somente dentro do escopo do novo BI. O Codex concluiu a auditoria e não deve ser interrompido; os dois agentes não devem editar os mesmos arquivos. NÃO iniciar construção das outras 13 páginas até fechar as 10 prioritárias.

LEIA integralmente ANTES de editar:
- _bi_chs001_replica/STATUS_CLAUDE.md
- _bi_chs001_replica/DIVERGENCIAS_BI_CHS001.md
- _bi_chs001_replica/AUDITORIA_CODEX_PRIORITARIAS.md
- _bi_chs001_replica/MAPA_MEDIDAS_DAX.md
- _bi_chs001_replica/PRIORIDADE_10_TELAS.md
- _bi_chs001_replica/paridade_dados_claude_20261015.json, quando disponível.

Sua missão: fechar divergências técnicas COMPROVADAS nas 10 telas prioritárias, com foco nos Resumos 30%, 60%, 90% que estão PENDENTE; só atestar cada KPI/curva se existir fonte verificável. O modelo semântico do PBIX original está no arquivo F:\BI N&A CONNECT\CHS001-000-PM0-RT-002-01.pbix, em modo SOMENTE LEITURA; use os relatórios DAX extraídos e o snapshot original como referência de teste; compare também o banco CHS001 read-only. Nunca criar valor fictício para satisfazer a tela.

ATENÇÃO: discrepâncias críticas Codex × seu relatório:
- Engine 30: Codex apurou Canceled=0 para a MEDIDA DAX original; relatório Claude mostra 9 CANCELLED em um campo. Provavelmente são indicadores com semânticas diferentes. Rastreie exato campo, expressão DAX, filtro, status e o número EFETIVAMENTE EXIBIDO pelo visual. Não normalize grafia Canceled/CANCELLED.
- Programação: 148 registros totais, distribuições status 28/26/49/40/5, mas outros cards/tabelas utilizam subconjuntos; não confundir 148 linhas com 103 linhas ou card 108. Verifique contexto específico por visual.
- Resumo 60 e 90: filtros PBIX são exclusões (NOT IN), não igualdade de gate. Preserve cada filtro real do PBIX.
- LDP 3D 90: preservar 1 linha AT no total 7 (FORMAT=3D reduziria a 6).
- KPI[Adh] e medidas não mapeadas: impedir que tabelas inteiras sejam marcadas pendentes sem justificativa, MAS nunca implementar aproximação como se fosse DAX original.
- Interações, relacionamentos bidirecionais, filtros cruzados e datas: comparar com fontes e modelagem reais; nunca ignorar filtro de outra entidade como se fosse aplicado.

Trabalho em passos verificáveis:
1. Produza matriz das 10 páginas com cada visual de dados, identificando: IMPLEMENTADO e conferido com cache PBIX / IMPLEMENTADO mas difere do banco atualizado / PENDENTE sem fonte / PENDENTE interação.
2. Avalie se é possível implementar prazo, duração e cards de datas dos Resumos a partir do cronograma oficial CHS001 sem inventar campos; para curvas e AssignmentByDay que não têm fonte atual equivalente, mantenha PENDENTE e registre a fonte necessária. Para validação pode usar SOMENTE cache real PBIX como fixture isolada de teste; NÃO embutir snapshot estático como dado vivo de produção, não modificar banco.
3. Corrija apenas divergências verificadas e preserve as seis LDP e a Programação que já funcionam. Mantenha os demais módulos intactos.
4. Rode typecheck, build Vite externo, testes isolados e de navegação; para cada correção mostre valor esperado PBIX por visual e resultado calculado. Se possível comparar screenshot real PBIX × React (sem credenciais), registre evidência; sem screenshots declare limitação.
5. Deixe parecer de PRONTIDÃO PARA TESTE AO VIVO no arquivo _bi_chs001_replica/STATUS_POS_AUDITORIA_CLAUDE.md, separando: dados já prontos, dependentes do novo backend, fontes faltantes e correções pendentes.
6. NÃO reinicie backend; NÃO publique build; NÃO execute robô; NÃO altere tabelas SQL/LDP/Timesheet/Norma; NÃO peça credenciais; NÃO edite os relatórios Codex nem o PBIX original. Faça backup dos arquivos editados e mantenha possibilidade de rollback.

Comece imediatamente. A prioridade é QUALIDADE, fidelidade numérica/visual e HONESTIDADE sobre o que ainda está pendente. Não criar telas superficiais.