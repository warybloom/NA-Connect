# N&A Connect — Integração Plant 3D — Complementação F7/4D (23/10/2026)

## Contrato do módulo
- O componente legato `Tab4D` usava `/api/Plant3D/list-projects`, `upload-cronograma-excel`, `calculate-4d`, endpoints indisponíveis no backend atual.
- Novo módulo ativo na rota de planejamento existente: `ClientApp/src/features/plant3d/Plant4DPage.tsx`.
- API nova: `POST /api/plant-integration/projects/{projectIdAcc}/4d/calculate`.
- Autorização: `[RequirePermission("plant3d.view")]` + `ProjectAccessError` por projeto; rejeita projeto sem vínculo Dashboard.
- Entrada JSON: `{ categoryId, grouping: "dia"|"semana"|"mes", rows: [{tag,date:"YYYY-MM-DD",weight}] }`. Limites: 8 MB de body, 25.000 linhas.
- Parse XLSX no navegador: arquivo até 6 MB; primeira etapa é escolha de aba, cabeçalho e colunas TAG, data prevista, peso/HH opcional.
- Banco `DB_DASHBOARD_BIM`: **somente leitura** dos DCF snapshots e deltas COMPLETED. Reutiliza DE/PARA e exclusões salvas (tipo, atributo, DWG); não insere padrões nem inventa dados. Sem migração SQL.
- A categoria escolhida é explícita, evitando colisão de TAG entre categorias; em cada TAG há validação de identidade e conferência dos atributos usados por `Compare`.
- O realizado é **estado atual** e considera como modelado somente pareamentos `matched` do inventário. Estados devolvidos: `matched`, `missing-3d`, `divergent`, `missing-pid`.
- Planejado até hoje = soma dos pesos com data prevista vencida / peso total. Curva planejada agrupada em dia/semana/mês.
- Não desenhar curva histórica do realizado, pois DCF não grava a data de conclusão de cada TAG. Não reaproveitar `mockCurvaS` nem rótulo fixo de atraso crítico.
- O upload de Excel é sob demanda e não fica persistido em SQL. Cada recarregamento exige selecionar a planilha para novo cálculo.

## Testes
- `4d-harness-build.log`: compilação C# sem erro no stage .NET 10.
- `4d-typecheck.log`: TypeScript sem erro.
- `4d-vite-build.log`: Vite finalizado EXIT 0.
- `4d-real-dcf-smoke.log`: teste CHS001 com 1.139 objetos em linhas na comparação, 326 com TAG. Validada uma TAG real P&ID e uma TAG inexistente, peso 10/5, agrupamento mensal, planejamento até 23/10/2026 = 66,67%, detecção de `missing-pid`, rejeição de TAG duplicada. `FOUR_D_REAL_DCF_SMOKE_PASS` e `REAL_READONLY_SMOKE_PASS`.
- Backup de fonte: `_backups/plant4d-native_20261023_192945`.
- Artefatos de compilação: `C:/Dev/_agent_runs/plant_fases_01_02_20261023/backend-4d-native` e `frontend-4d-build`.
- Script de deploy com rollback: `C:/Dev/_agent_runs/plant_fases_01_02_20261023/deploy-4d-native.ps1` — contém PID fixo anterior 74988; não reexecutar após publicar.

## Configuração e homologação (pendências por projeto)
- F1/F2: cadastrar padrões, fases e pesos reais; novas tabelas permanecem intencionalmente vazias.
- F4: associação por `TagLine` requer colunas reais sincronizadas no Plant; 325 linhas CHS001 sem comprovação não se tornam 100%.
- F6: fontes reais de Tie-ins (P&ID Tiein) e Suportes (Piping Support) agora cadastradas e testadas no CHS001; 6 de 7 tipos de relatório mapeados. Materiais depende de origem MTO confirmada, sem inferência de quantitativos.
- F7: usar XLSX real do cronograma e confirmar as colunas selecionadas, as datas, o peso e a categoria DE/PARA em projeto autenticado.
- A validação de sessão autenticada MasterAdmin e permissões por usuário exige login real no navegador e não é substituída por teste HTTP 401 sem sessão.

## Publicação técnica concluída — 19h33, 23/10/2026
- Código-fonte integrado ao projeto principal, com backup `_backups/plant4d-native_20261023_192945`.
- A compilação completa do N&A gerou DLL, PDB e manifests corretos, sem mensagens `error CS/MSB/NETSDK`. O processo auxiliar de build foi encerrado **depois** de estabilizar hashes/artefatos, pois continuava consumindo ~13 GB RAM; isso não equivale a alegar exit code 0 da compilação completa.
- Publicação do backend via `deploy-4d-native.ps1`: **DEPLOY_4D_OK_PID=15276**, DLL SHA256 `3957152A6C499BF3B74CB1F572D5D583F8EE80E1BD44B10028CF8E819A958DBD`.
- Backup pré-publicação da DLL/PDB: `_backups/plant4d-binaries_20261023_193343`. Rollback não necessário.
- Validação HTTP: OAuth 200; catálogo de projetos protegido 401 sem sessão; POST `/4d/calculate` protegido 401 sem sessão; frontend `/plant3d/4d` 200.
- Backend PID 15276, porta 5277, `plant4d_published.stderr.log` com zero bytes após inicialização.
- Oito testes Node de relatórios/BI novamente aprovados no código vivo (8/8, EXIT 0).
- **Homologação com conta real e Excel de produção ainda pendente.** Seis relatórios estão mapeados no CHS001; somente Materiais permanece não mapeado à espera da fonte MTO.

## Fechamento F6 no CHS001 — fontes verificadas no DCF
- Evidência sem escrita: `plant-f6-source-evidence.log` — `Pnid.Tiein` = 100 registros, com `TieInNumber`, `TIEINFROM`, `TIEINTO`, `TagLine`, `DwgName` etc.; `Piping.Support` = 35 registros.
- Backup da configuração anterior: `C:/Dev/_agent_runs/plant_fases_01_02_20261023/plant-f6-pre-source-config-20261023.json`. Configuração aplicada somente ao projeto CHS001 (`b.fcdb7ebd-1b23-440d-a04b-90391098522e`) por `SaveReportConfigAsync`, preservando quaisquer origens preexistentes.
- Evidência pós-configuração: `plant-f6-config-validation.log` — relatório `tie-ins` 100 registros, 11 colunas, página de 3; `supports` 35 registros, 9 colunas, página de 3; `F6_CONFIG_EXIT=0`.
- `materials` fica `unmapped` até vínculo comprovado com lista MTO/materiais. `PipeRunComponent` apresenta componentes (7.684 objetos), mas não equivale automaticamente a uma lista MTO; proibido usá-lo como origem sem critérios de quantitativo e unidade.
