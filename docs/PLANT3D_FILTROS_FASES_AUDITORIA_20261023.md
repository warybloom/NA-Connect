# N&A Connect — Plant 3D: filtros globais, listas, fases e painéis
Atualização: 23/10/2026. Referência: _agent_handoffs/PLANT_DASHBOARD_20261023/CONTRATO_03.md.

## Arquitetura
Dashboard BIM SQL (DB_DASHBOARD_BIM) permanece somente leitura. Históricos de ProcessPower/Piping são reconstruídos a partir de snapshots completos e deltas COMPLETED. Filtros, DE/PARA, vínculo e fases ficam em DB_NA_CONNECT e não alteram DCF nem espelho original.

Migração aplicada em DB_NA_CONNECT: Migrations/PlantIntegration/20261023_002_FiltersPhases.sql: dbo.PlantCategoryFilters (FilterJson) e dbo.PlantMeasurementPhases (PhasesJson), ambas versionadas por UpdatedAtUtc. Backup SQL anterior verificado:
C:\Program Files\Microsoft SQL Server\MSSQL16.SQLEXPRESS\MSSQL\Backup\DB_NA_CONNECT_pre_plant3d_rules_20261023_090000.bak

## Backend
Controllers/PlantIntegrationRuleEndpoints.cs + Services/PlantIntegration/PlantIntegrationRules.cs + PlantIntegrationPhases.cs
Rotas /api/plant-integration/projects/{projectIdAcc}:
- GET/PUT /filters: categorias -> regras side pid|piping|both, action include|exclude, target attribute|drawing|table, operator eq|neq|contains|notContains|startsWith|endsWith|in|notIn|isEmpty|isNotEmpty; regras persistidas; apenas plant3d.database.edit pode alterar.
- GET /inventory/{category}?side=pid|piping&offset=0&limit=100: raw/included/excluded, linhas e reasons paginadas.
- GET/PUT /phases: lista ordenável de fases arbitrárias com nome, expected por categoria, pesos opcionais.
- GET /measurement?phaseId={id}: meta cadastrada versus quantitativos reais P&ID/3D já filtrados.
- GET /results: o MESMO conjunto de regras é aplicado às linhas antes de contagens, TAGs, comparação P&ID×3D e indicador.
- GET /status: informa filtersUpdatedAtUtc para atualização automática das telas (polling leve; nenhum polling SQL pesado sem mudanças).

PnPDrawings: nome real em 'Dwg Name'. No P&ID, 'DwgName' fallback; no 3D, 'DRAWINGNO' fallback. P3dLineGroup pode pertencer a múltiplos DWGs via P3dDrawingLineGroupRelationship(Drawing,LineGroup). A tela lista todos os desenhos; excluir um DWG de estudo não remove a linha se existir outro DWG não excluído. No teste inicial de 23/10, 407 de 412 LineGroups possuíam relação; o quantitativo pode mudar com novas sincronizações. Faltantes são dados ausentes, não inferidos.

## Sidebar (AUDITORIA > Plant 3D)
Resultados: Geral; Auditoria P&ID x 3D; Quantitativa e Qualidade; Consistência.
Configuração: Linhas; Equipamentos; Instrumentos; Válvulas; Critérios de Medição; Planejamento 4D; DE/PARA e TAGs.
Cada uma possui URL própria sem barra principal de abas. Filtros por lado P&ID/3D dentro da lista são locais àquele inventário, não navegação de telas. Critérios de Medição e Planejamento 4D aparecem no sidebar como páginas reservadas; não estão funcionalmente implementadas nesta rodada. O editor de fases com metas foi desenvolvido em Geral/Fase; 4D continua pendente.
Frontend: ClientApp/src/features/plant3d/*, components/plant3d/plantRulesApi.ts, App.tsx, officialNavigation.ts, permissionLogic.ts, Plant3DDashboard.tsx. TypeScript e Vite build passaram.

## Testes verificáveis
- CHS001 teve snapshot P&ID e 3D lidos em modo read-only. Na conferência anterior, categorias mapeadas totalizaram 2.658 registros P&ID e 1.459 3D, podendo variar quando o DBSYNC atualizar.
- Em teste TEMPORÁRIO isolado com ID novo, copiamos apenas o DE/PARA da CHS001 para uma configuração não vinculada a usuário e registramos uma regra de exclusão para instrumento com Tag começando PY-. O total do inventário foi de 1.039 para 1.019, excluindo 20. A categoria da auditoria também retornou 1.019. Foram gravadas e lidas duas fases; na fase com meta de 1.200 instrumentos, a medição mostrou 1.019 realizados. EXIT=0; depois o teste apagou suas próprias linhas temporárias (PlantCategoryFilters=1, PlantMeasurementPhases=1, PlantCategoryMappings=1). Configuração real permaneceu intacta.
- Teste sintético: atributo case-insensitive e desenho 'ESTUDO' correspondem; múltiplos desenhos mistos não excluem uma linha válida. Leitura real confirmou nomes DWG.
- TAG incompleta '?.' ou '?-?' deve ser tratada como sem TAG, nunca pareada como objeto válido. Há muitos exemplos reais no CHS001 3D. CORREÇÃO validada em harness isolado; publicar após recompilação final de backend tags.
- Testes de autorização anônima: /Authentication/OAuth/GetLoginUrl retorna 200; login fictício recusa 401; APIs protegidas retornam 401. Frontend Vite 200. Login com credenciais reais e testes visuais permanecem para homologação do usuário.

## Limitações pendentes e próximas etapas
- CONSISTÊNCIA por linha (quantidade esperada de válvulas/instrumentos P&ID versus executado 3D com ponderação): NÃO mostrar 100% até comprovar relação P&ID por linha para esses componentes. Foi confirmado P3dLineGroupPartRelationship(LineGroup,Part) no 3D, e PipeLineGroupRelationship(PipeLine,PipeLineGroup) no P&ID; ainda não há relacionamento confiável completo entre P&ID e componentes por linha. A página exibe somente dados comprovados e aviso.
- Geral exibe quantitativos por objeto. O indicador de 'documentos modelados x não modelados' requer definição da correspondência entre documentos P&ID e modelos 3D; não converter proporção de itens em documento concluído sem regra.
- Critérios de Medição e 4D constam do sidebar, conforme solicitado, mas as respectivas funcionalidades detalhadas ainda não estão implementadas.
- Revisar DE/PARA de TAGs 3D de válvulas e instrumentos. 'Tag' pode ser '?-?' ou '?-JUNTA-?' e não deve criar falsa aprovação de pareamento.
- Filtros aprovados por administradores deverão ser testados visualmente no projeto CHS001, inclusive exclusão por atributos, tipos, DWGs de referência e teste com dois DWGs no mesmo LineGroup.
- Como cada /results reconstrói estado e compara linhas, testar concorrência/desempenho antes de liberar em escala maior. A consulta /status é leve e serve apenas para detectar revisão e alterações de filtro.

## Deploy e rollback
Primeira publicação em 23/10/2026 às 10:50, backend PID 32272, porta 5277, working directory do workspace (ConfigSource=WORKSPACE), backup binários:
C:\Dev\N-A-Connect aa\N-A-Connect\_backups\plant3d-filters-20261023_104957
Script original reiniciar-backend.ps1 corrigido para -WorkingDirectory $root (login do workspace), backup: _backups/reiniciar-backend-before-config-fix_20261023.ps1. Não reiniciar Dashboard BIM/Caddy por causa dessa integração.

## Publicação final de integridade das TAGs — 23/10/2026 às 11:02
- Antes de publicar, o backend foi recompilado em pasta isolada (backend-tags), gerando DLL, PDB e 127 DLLs sem erros de compilação. Para poupar RAM, a compilação de alto consumo foi encerrada depois de produzir todos os binários e o log de sucesso.
- Regra em PlantIntegrationService.Tag: valores incompletos que contêm o caractere '?' não podem virar TAG de comparação, mas os objetos permanecem na lista, contabilizados como sem TAG; banco de dados original e desenho não são alterados.
- Teste isolado com o CHS001: Valve Piping 655 registros, os 655 sem TAG válida; Instrument Piping 259, 256 sem TAG válida; P3dLineGroup Piping 414, 413 sem TAG válida. Esses dados são a leitura do instante de teste e podem mudar com DBSYNC e nova configuração DE/PARA. Não significam necessariamente ausência de objetos.
- Publicada DLL em bin/Debug/net9.0/AccIntegration.WebApp.dll, SHA-256 E625689F9CD928F637B509C348FE8844B7DDA47C7F67177F4AF9D5D4FF1E9800, por deploy-tags.ps1 com rollback. Backend novo PID 64312 na porta 5277. Smoke tests: OAuth 200; login fictício 401; API Plant protegida 401; frontend 200. Deploy EXIT=0.
- Backup binário anterior preservado em _backups/plant3d-tag-integrity-20261023_110213. Backup SQL original da rodada permanece preservado e verificado.
- Sinal de segurança para backlog: restore/build indicou avisos NU1902/NU1903 de pacotes MPXJ.Net 13.5.0 e System.Security.Cryptography.Pkcs 9.0.11. Nenhum pacote externo atualizado nesta rodada; revisar vulnerabilidades e atualizações compatíveis em trabalho separado.
