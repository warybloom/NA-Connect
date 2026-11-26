# Plant 3D — N&A Connect | Fases 1 a 6 (23/10/2026)
**Publicação técnica, não homologação do usuário.** Ciclo de integração iniciado às 18h51, publicação às 19h12, ambiente NA-1708.

## Proteções e backups
- Antes de qualquer SQL foi criado e verificado com RESTORE VERIFYONLY/CHECKSUM: `C:\Program Files\Microsoft SQL Server\MSSQL16.SQLEXPRESS\MSSQL\Backup\DB_NA_CONNECT_before_plant_phases_1_6_20261023_185527.bak`, aproximadamente 735,7 MB.
- Aplicadas em DB_NA_CONNECT, em transações e sem dados iniciais inventados: `Migrations/PlantIntegration/20261023_003_StandardsAndLineWeights.sql` (PlantIntegrationStandards/PlantLineWeights) e `20261023_005_PlantReports.sql` (PlantReportConfigs/PlantReportUserPreferences). Todas criadas inicialmente vazias.
- DB_DASHBOARD_BIM não recebeu escrita; leitura SQL read-only dos snapshots DCF COMPLETED.
- Antigos binários DLL/PDB guardados em `_backups\plant3d-six-phases-20261023_191159`; backup de origem frontend e manifesto SHA-256 em `_backups\plant_front_fases_1_a_6_20261023`.
- Codigo de trabalho isolado, prompt Claude/Codex e logs: `C:\Dev\_agent_runs\plant_fases_01_02_20261023\workspace`.
- Script de implantação backend com rollback: `C:\Dev\_agent_runs\plant_fases_01_02_20261023\deploy-backend-all-phases.ps1`. Executar somente com a verificação de PID; este script usa PID anterior 64312 e NÃO é apropriado para reexecução após esta implantação.
- O script `reiniciar-backend.ps1` mantém working directory correto do workspace para carregar configuração OAuth/permissões.

## Entregue em código e publicado
**F1 — Base visual + Catálogo:** nova navegação de Plant 3D: Resumo, Resultados individuais, configurações, critérios, relatórios, planejamento 4D. Resumo estilo Gestão à Vista em 3 fileiras gráficas Quantitativa/Qualitativa/Consistência, sem lista operacional na visão executiva. Catálogo de Padrões por projeto (tipo equipamento/instrumento/linha, área/subárea/spec e custom), seleção tabela/coluna real do P&ID e 3D, CRUD manual, XLSX no frontend, `TagLine` pré-preenchido/editável, comparações de nomes de colunas case-insensitive. API GET/PUT standards e GET standards/quality.

**F2 — Fases e pesos:** metas de P&ID/modelagem por categoria e fases arbitrárias cadastradas em `PlantMeasurementPhases`; pesos de linha GET/PUT `line-weights?phaseId`, validação soma 100; grupo dinâmico no sidebar (Quantitativa, Qualitativa, Consistência por fase), sem somar metas cumulativas. A fase não é atribuída automaticamente a cada objeto do DCF.

**F3 — Resultados individuais:** Linhas, Equipamentos, Instrumentos, Válvulas, Tie-ins são páginas independentes com gráficos e paginação de divergências. Categorias não cadastradas no DE/PARA aparecem como indisponíveis, nunca zeradas. Filtros globais existentes /filters se aplicam às contagens por inventário/auditoria.

**F4 — Motor de consistência:** nova API GET `/api/plant-integration/projects/{id}/consistency` com categoria linha→componentes, seleção por atributo `TagLine` ajustável, proteção contra TAG invalida `?`, filtros globais, relacionamentos por atributos reais e pesos configurados; não calcula 100% onde vínculo não foi comprovado. Teste isolado real CHS001 retornou **325 linhas P&ID elegíveis, 325 não verificadas** por não haver TagLine suficiente nas classes de componentes. Isso é dado insuficiente, não falha do modelo.

**F5 — BI e fases:** visão de execução por fase, indicadores planejado versus P&ID e 3D, separação Qualitativa, três anéis de consistência com estado sem dado comprovado quando apropriado. Não soma fases cumulativas.

**F6 — Relatórios:** GET reports, config, preferences, data paginados e export CSV (linhas limitadas, campos configuráveis). Frontend gera XLSX sob limite declarado e protege células contra fórmula. Regras /filters aplicadas a comparação/exportação. Material/Suportes/Tie-ins sem fonte configurada ficam como não mapeados. Teste real CHS001: catálogo retornou 7 tipos (4 mapeados, 3 não mapeados), linhas retornou total 1.139 com página de 3 itens. As rotas de relatório estão no sidebar; extração universal não é cópia literal do Dashboard BIM porque fonte de referência original não ficou acessível neste lote.

**Planejamento 4D:** página própria ativada, mas fluxo de upload/cálculo original ainda depende dos endpoints legados `/api/Plant3D/*` não existentes no backend atual. UI avisa 'Requer configuração' em vez de mostrar cálculo falso. Completar 4D será uma etapa posterior.

## Testes técnicos
- C# serviço em harness .NET 10 stage: padrões, pesos, consistência e relatórios compilam sem erros; testes de peso invalidos/duplicados passam.
- Backend N&A .NET em output isolado `C:\Dev\_agent_runs\plant_fases_01_02_20261023\backend-final`: AccIntegration.WebApp.dll 7.006.720 bytes e 127 DLLs. Nenhum erro CS/MSB encontrado; processo de build auxiliar encerrado após gerar binário, PDB e deps para liberar RAM. Warnings preexistentes de nullable, CA2017 e pacotes MPXJ.Net com avisos NU1902/NU1903.
- Frontend: `tsc --noEmit` EXIT 0, Vite build EXIT 0. Unit test Node nativo: **8 testes de exportação segura e BI por fase passam** (ajustadas extensões no teste Node22).
- Implantação backend EXIT 0, novo PID **72108**, porta 5277, DLL SHA256 `59F52E9443EDB3BF9A3A391A5CB8F3ED74C8CE8DCA29AFA068B46CBA62164E2D`. Smoke HTTP: Autodesk login URL 200, tentativa credenciais fictícias 401, rotas do Plant antigas/novas 401 sem autenticação, frontend localhost:5173 rotas novas 200, stderr do backend 0 bytes.
- Publicado frontend com 8 arquivos existentes atualizados, 25 novos e 24 inalterados, manifesto + backups de versões anteriores.

## Homologação necessária
1. Entrar com usuário MasterAdmin real e revisar sidebar e permissões das páginas novas.
2. Configurações > Catálogo de Padrões: cadastrar tabela/coluna real por domínio, importar Excel, testar autorização de edição e qualidade no Resumo.
3. Configurações > Critérios/Fases: criar fase e pesos da linha; validar que exatamente três páginas aparecem e que metas revisadas não são somadas.
4. Sincronizar atributo **TagLine** nas classes P&ID/3D dos componentes para habilitar correspondências confiáveis por linha. Validar casos válvula/instrumento e dois DWGs/uma mesma linha.
5. Relatórios > configurar origens de Tie-ins, Materiais e Suportes; confrontar exportação universal com a referência antiga.
6. Validar desempenho com projetos maiores, 4D e cálculos de documentos modelados (pendentes de regras e backends).

**Não declarar 100% concluído até os testes de fluxo autenticado e a configuração da origem.**
