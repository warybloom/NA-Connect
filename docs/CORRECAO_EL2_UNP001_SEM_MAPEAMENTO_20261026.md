# 26/10/2026 — UNIPAR EL2: doc3 válido ocultado por separador duplicado

## Sintoma
Em Controle de Produção > Resumo > Sem mapeamento, a janela "Conferir siglas" mostrava "DOC3 AUSENTE" para:
- `UNP001-000--EL2-LI-001` — 11 lançamentos, 14,92 HH.
- `UNP001-000--EL2-LI-002` — 9 lançamentos, 17,75 HH.
Total 20 lançamentos / **32,67 HH**, já incluídas nas 3.290,38 HH do projeto.

## Causa verificada
Na `dbo.VW_TimeSheet_NA_Corrigida_Projeto`, as colunas físicas estavam CORRETAS: `doc3=EL2`, `doc4=LI`, em ambos os documentos.
O normalizador global `normalizador.SiglaDisciplina` já tinha `EL2 -> ELÉTRICA` ativo.
O problema estava em `Services/Planning/PmoActualFactsProvider.cs`:
`doc3 = document.Split('-')[2]` ignorava a coluna SQL e escolhia a parte vazia do código com `--`.
Adicionalmente `ProvisionalDocuments` montava `Doc3/Doc4` pelo número de documento: retornava `Doc3=''`, `Doc4='EL2'`.
A UI `resumoPhaseData.ts` também caía em `DOC3 AUSENTE` ao tentar a terceira parte da string.

## Alteração restrita e não destrutiva
- Para `Sem_LDP=1`, usar diretamente o `Doc3` original do Timesheet corrigido como entrada do normalizador; projetos com LDP seguem regra anterior para evitar regressões.
- `ActualEntry` ganha argumento opcional `RawDoc4`. Inventário de `ProvisionalDocuments` usa `NormalizedSigla` e `RawDoc4` da fonte estruturada, não fatias do código.
- UI: sigla fornecida pela API tem prioridade; compatibilidade somente para padrões de 5 partes ou de 6 partes com separador vazio antes do Doc3, sem alterar o número original.
- Teste xUnit adicionado em `Tests/NA_Connect.Planning.Tests/UniparHistoricalDoc3RegressionTests.cs`, cobrindo dois códigos e total 32,67 HH.

## Validações
- SQL read-only confirmou ambos doc3=EL2, doc4=LI e normalizador ativo.
- TypeScript `--noEmit`: exit 0; Vite: exit 0.
- .NET backend: exit 0; zero erros; avisos preexistentes.
- Backup: `_backups/el2-doc3-binaries_20261026_141506`.
- Deploy script: `C:\Dev\_agent_runs\deploy-el2-doc3-20261026.ps1`.
- Backend reiniciado no PID 71044 (5277). DLL SHA256 `9273144C834863B0256082B0C71F4B7210BAB8C7BE5FEF6698D783F36FC64C3A`.
- HTTP OAuth 200, APIs privadas 401 sem autenticação (esperado), frontend 200; stderr inicial vazio.
- Falta homologação visual autenticada de que o card Sem mapeamento da UNIPAR não mostra mais esses dois códigos; não afirmar a verificação do navegador sem login.
