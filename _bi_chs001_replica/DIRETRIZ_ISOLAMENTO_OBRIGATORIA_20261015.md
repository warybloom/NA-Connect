# DIRETRIZ PRIORITÁRIA DO USUÁRIO — SOBREPÕE AUTORIZAÇÃO ANTERIOR — 15/10/2026

**O usuário acabou de determinar:** "só um detalhe não vamos mexer no que fizemos ta, se necessario cria um novo só para essas telas novas".

REGRA INEGOCIÁVEL E SUPERIOR AO DOCUMENTO AUTORIZACAO_TOTAL_IMPLEMENTACAO_20261015.md:

**NÃO MODIFICAR** o importador CronogramaTipo2 existente, seus modelos, repositório, snapshots, sync, jobs ou esquemas já em uso; não tocar em tabelas, views e módulos operacionais de LDP, Timesheet, Planejamento, custos, qualidade, dashboard existente, permissões ou identidade visual global. Não refatorar nem substituir o pipeline atual. Se arquivos de BI criados nesta etapa precisarem ajuste, podem mudar SOMENTE dentro do módulo novo BI CHS001 (e controlador dedicado se for exclusivamente do BI).

**IMPLEMENTAR O QUE FALTA EM PARALELO E ISOLADO:**
1. Criar leitor/serviço NOVO exclusivo do BI CHS001 (ex. Services/BiChs001Schedule/*) que pode usar MPXJ para ler COPIA/stream do .mpp oficial em READ ONLY e aproveitar a mesma estratégia de acesso ao ACC sem modificar serviço antigo.
2. Banco SQL: em schema próprio, por exemplo `bi_chs001`, criar apenas tabelas/views novas, aditivas, independentes, como TarefasSnapshot e TrabalhoTarefaPeriodo. SEM ALTER/CLEAR/UPDATE das tabelas legadas (Cronograma_Projetos, LDP, Timesheet, planejamento). Persistir task UID, parent UID, starts, work, baseline, séries temporais e versão com transações isoladas, backups e rollback.
3. API dedicada do BI CHS001; conexão aos dados antigos só SELECT. Não alterar endpoints antigos fora do próprio módulo de BI. Curvas S, KPI e matriz consomem exclusivamente fontes BI e, quando apropriado, leitura do cronograma legada sem escrita.
4. Corrigir typo 'Emgineering - 30%' SEM alterar o .mpp oficial nem históricos: mapeamento versionado **somente no BI CHS001**, limitado por identificador de projeto, mantendo valor bruto e compatível com filtros inclusivos 30% e exclusivos 60/90.
5. Corrigir logos duplicadas somente dentro da camada nova BI CHS001, preservando ThemeLogo global e sidebar. Não mexer no trabalho paralelo Claude BML011.013.
6. Validar contagens e numeradores contra PBIX; datas semanais do AssignmentByDay observadas no cache são domingos. Nunca usar snapshot PBIX como dado vivo e não inventar horas.
7. Implementação com backup, testes em clone, rollback; migrations novas sem alterar schema de outros módulos; implantação controlada; confirmar que demais módulos não mudaram antes/depois.

**AUTORIZAÇÃO TOTAL DO USUÁRIO PERMANECE VÁLIDA APENAS DENTRO DESTA ARQUITETURA ISOLADA.** A autorização anterior de alterar importador Tipo 2, .mpp oficial e estruturas compartilhadas está expressamente RETIRADA pela nova instrução. Se algum patch nesses arquivos já foi iniciado, pare e registre exatamente o estado, não reverta cegamente mudanças de outros agentes e não execute migrações até isolar.

Entregar uma matriz 'arquivos/tabelas existentes intocados' e somente então implantar. Trabalhar diretamente na solução isolada, e relatar.
