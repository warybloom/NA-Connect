# N&A Connect — Plant 3D — Fases 1–6 / Homologação de 23/10/2026
Status: código implantado e serviços ativos; configurações de engenharia e homologação visual autenticada ainda pendentes. Não declarar auditoria de consistência aprovada enquanto TagLine e pesos não permitirem vinculação comprovada.

## Entrega por fase
1. Sidebar Plant 3D com telas individuais; Resumo com três fileiras gráficas, Catálogo de Padrões e Validação com seleção de tabelas/colunas e importação XLSX; sem barra de abas superior.
2. Critérios de Medição, fases/gates livres, quantitativos previstos por fase, pesos de linha, URLs de três telas por fase. Metas cumulativas não são somadas.
3. Resultados individuais para Linhas, Equipamentos, Instrumentos, Válvulas e Tie-ins com visão resumida e detalhamento; configuração por filtros globais separada.
4. GET /consistency por projeto/fase: relaciona componentes pelo atributo configurado TagLine (case-insensitive), compara esperados P&ID e identificados 3D por TAG de componente, rejeita identificadores incompletos e marca desconhecido quando não consegue comprovar. Gráficos no Resumo e fase agora usam indicadores do backend: completude ponderada, pendências em válvulas e pendências em instrumentos. A API calcula agregados sobre todo o projeto e pagina apenas linhas de detalhe; os gráficos não dependem do tamanho de página.
5. Painéis por fase Quantitativa, Qualitativa e Consistência, visual Gestão à Vista, metas por fase sem duplicação.
6. Central de Relatórios com linhas/equipamentos/instrumentos/válvulas/tie-ins/materiais/suportes; configurações de origem DE/PARA ou tabelas, exportação CSV no backend/XLSX paginado no cliente, neutralização de fórmulas, filtros globais também aplicados a exportação. Planejamento 4D aparece no sidebar, mas seu módulo completo depende de endpoints Plant3D legados não implantados (não simular execução).

## Implantação
- Antes da implantação: backup SQL SQL Server COPY_ONLY + CHECKSUM, verificado por RESTORE VERIFYONLY:
  C:\Program Files\Microsoft SQL Server\MSSQL16.SQLEXPRESS\MSSQL\Backup\DB_NA_CONNECT_before_plant_phases_1_6_20261023_185527.bak
- Migrações 003 (dbo.PlantIntegrationStandards e dbo.PlantLineWeights) e 005 (dbo.PlantReportConfigs e dbo.PlantReportUserPreferences) aplicadas em DB_NA_CONNECT em transação e inicialmente vazias, sem alterar DB_DASHBOARD_BIM.
- Backend seis fases publicado em 19:12; última correção dos indicadores dinâmicos publicada em 19:24, porta 5277 PID 74988.
- SHA-256 da última DLL AccIntegration.WebApp.dll: 359CA58D3DAF38C13748353ABE4CE8D371C09805622C621261B6BA6275D19A5B
- Backup antes do último deploy DLL/PDB: C:\Dev\N-A-Connect aa\N-A-Connect\_backups\plant3d-ring-indicators-20261023_192424
- Backup dos quatro arquivos de origem da última correção: C:\Dev\N-A-Connect aa\N-A-Connect\_backups\plant3d-consistency-graphics-20261023_192100
- Frontend em desenvolvimento Vite porta 5173, HMR lendo source live; builds isoladas disponíveis: C:\Dev\_agent_runs\plant_fases_01_02_20261023\frontend-rings e backend-rings. ClientApp/dist antigo não foi substituído porque servidor FE atual é Vite.

## Verificações
- TypeScript tsc --noEmit sem erros, Vite build sucesso, testes TS para relatórios 8/8.
- Backend integral compilado sem erros, 127 DLLs em stage e PDB; avisos de pacotes NuGet pré-existentes (MPXJ.Net NU1902/NU1903). Recomenda-se planejar atualização de dependências separadamente.
- Login OAuth GET 200; tentativa de login inválido 401; endpoints Plant sem autenticação 401 (proteção confirmada); sete rotas frontend respondendo HTTP 200; stderr backend novo tem 0 bytes.
- Smoke read-only CHS001: 7 tipos de relatórios, 4 mapeados — Linhas total de registros de relatório 1.139, Equipamentos 204, Instrumentos 1.296, Válvulas 1.444; amostras paginadas têm colunas alinhadas; Tie-ins, Materiais e Suportes requerem mapeamento.
- CHS001: 0 catálogos de padrões, 0 fases/gates no banco; atributo TagLine padrão. Auditoria de consistência identificou 325 linhas P&ID e 325 ainda não verificadas por ausência de vínculo TagLine comprovado. Não atribuir 100% a nenhuma.

## Homologação pendente, por ordem
1. Com usuário de acesso real, abrir Resumo, Resultados individuais, Configurações e Relatórios; testar criação de catálogo, regra de qualidade e Excel. Verificar visual, permissões e refresh; HTTP 200 no SPA não substitui um teste visual autenticado.
2. Cadastrar fases/gates e metas reais (30, 60, 90 ou livres) e pesos da linha totalizando 100; conferir as três telas criadas por fase no sidebar.
3. Configurar o atributo TagLine no Plant 3D, sincronizar DCF P&ID e 3D, mapear corretamente componentes; retestar consistência e três anéis gráficos com valores reais. Verificar linhas que aparecem em mais de um DWG e componentes sem TAG.
4. Mapear Tie-ins, Materiais e Suportes para tabelas de origem reais; testar relatório/excel de cada categoria, campos dinâmicos, filtros globais, exportações limitadas e sanitização.
5. Planejamento 4D funcional exige revisar/implementar endpoints Plant3D legados (não integrado às seis fases como cálculo operacional completo).
6. Otimizar bundle frontend grande (>500 kB) e revisar alertas de vulnerabilidade NuGet em trabalho separado.

Não há automação de mudanças futuras; próxima rodada deve partir do source live e deste documento, não publicar cópias anteriores sem comparar hash.
