# 26/10/2026 — UNIPAR Sem LDP, Curva S e Resumo

## Diagnóstico
- Projeto `UNP001 - UNIPAR`, ACC `b.90621daf-972c-4291-af87-04843e717ec9`, `dbo.Projetos.Sem_LDP=1`, `Timesheet_Projeto=UNP001`, `CR_OS=UNP001`, `AFE_Historico=UNP001`.
- `gestao.VW_TimeSheet_Corrigido_Espelho_Gerencial` por Project_ID_ACC retorna **0** registros.
- `dbo.VW_TimeSheet_NA_Corrigida_Projeto` por Projeto=UNP001 e doc1=UNP001 retorna **1.484** lançamentos, **3.290,38 HH** e **198 códigos documentais**.
- Fonte remota AFE `db_na_bi.dbo.tbTimeSheet` por Projeto=UNP001, AFE=UNP001 retorna 0. Nenhuma soma duplicada é realizada entre as duas fontes.

## Correção aplicada e publicada
- Em `Services/Planning/PmoActualFactsProvider.cs`, somente quando `dbo.Projetos.Sem_LDP=1`, a leitura de HH usa `dbo.VW_TimeSheet_NA_Corrigida_Projeto` cruzada pelos campos explícitos do projeto (`Timesheet_Projeto`, `CR_OS`). Demais projetos mantêm a view gerencial atual.
- Retorna todas as linhas distintas usando um identificador local `ROW_NUMBER`; nenhuma nova tabela/registro/LD​​P foi criada.
- Custo histórico segue indeterminado porque essa visão não possui taxa/custo profissional; nunca imputar custo zerado. AFE é fonte independente e permanece visível na auditoria histórica.
- Frontend `ResumoProducaoPage.tsx` usa a flag oficial `semLdp` de `/api/me/projects` para mostrar selo **Sem LDP**, não `LDP normal Tipo 1`.
- Frontend removeu os links de teste Resumo TV e de exploração de documentos/HH/custos por disciplina.
- `ViewCurvaS.tsx`: título único **Curva S**; removidos "Como ler esta curva", "Progress Curve" e "modelo Power BI"; `ProgressCurveCombo.tsx` conserva gráfico combinado barras-período + linhas-acumuladas.
- Não reclassifica emissão ou avanço de documentos sem LDP: os códigos de Timesheet comprovam lançamentos, não aprovação ou emissão. HH previsto, saldo, consumo e físico exigem dados planejados que ainda não existem.

## Verificações e deploy
- SQL de leitura ligado a `dbo.Projetos` repetiu **1.484 / 3.290,38 HH / 198 códigos**, sem escrita.
- TypeScript `tsc --noEmit`: exit 0. Vite build: exit 0.
- Backend `dotnet build`: exit 0, 0 erros (avisos legados). DLL SHA256 `84B5F8B7A42DB01F7FB57DCBC2EEB5D9B2B565035D5ADDC16A310CC8B84FA884`.
- Backup binário: `_backups/unipar-hh-binaries_20261026_115359`; reinício efetuado, backend novo PID `23452` na porta 5277.
- Smoke HTTP: OAuth 200, rotas protegidas sem sessão 401, frontend 200. stderr vazio. Rollback não necessário.
- A visualização do campo HH Realizado na sessão MasterAdmin precisa ser confirmada no navegador; smoke sem sessão não é homologação autenticada.

## Avanço físico BML011.016
- Auditoria de pesos e escopo sem escrita no arquivo `docs/AUDITORIA_BML011016_AVANCO_FISICO_20261026.md`.
- 12 documentos elegíveis sem peso de status definido = 192,07 HH ponderáveis. Verificar com o planejamento antes de modificar pesos, exclusões e avanço.
