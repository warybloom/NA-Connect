# Validação — Programação Semanal / Painel do Planejador

19/09/2026. Não iniciar o aplicativo backend para testar esta fundação: seu startup possui serviços existentes, e nenhum Worker foi executado por esta frente.

| Verificação | Resultado |
|---|---|
| TypeScript global (`tsc -p ClientApp/tsconfig.json --noEmit`) | Aprovado, sem erros. |
| Build frontend existente (`npm.cmd run build -- --outDir dist/weekly-planning-verify`, cwd ClientApp) | Aprovado: 3332 módulos; saída isolada. Avisos existentes de Browserslist/chunk grande. |
| Build do preview novo (`node ClientApp/src/features/planning/build-preview.mjs`) | Aprovado: 47 módulos, incluindo as duas páginas ainda não ligadas ao App. Saída dist/weekly-planning-preview. |
| Testes frontend (`node --test ClientApp/src/features/planning/tests/workflow.test.cjs`) | 5 aprovados, 0 falhas. |
| Compilação backend novo e testes (`dotnet test Tests/NA_Connect.WeeklyPlanning.Tests/NA_Connect.WeeklyPlanning.Tests.csproj --no-restore`) | 12 aprovados, 0 falhas, sem warnings no último run. Compila os arquivos reais novos de Models/Services/Controllers via links, sem iniciar host/Worker. |
| Build backend completo padrão, saída/intermediários isolados | Não aprovado: falhou ao resolver MPXJ 13.5.0 pelo Maven por restrição de rede. |
| Repetição com repositório Maven local existente | Avançou para IKVM, emitiu avisos de classes Java, mas não retornou conclusão durante a validação. Não contabilizado como build aprovado; log em backend-build.log. Nenhum pacote/csproj compartilhado foi alterado para contornar dependências. |
| SQL real | Abertura de conexão falhou. Executor standalone compilado, sem iniciar aplicação: schema-audit.json registra READ_ONLY_AUDIT_FAILED/SqlException. Sem DDL/DML. Queries dos endpoints não puderam ser verificadas contra schema implantado. |
| Preview HTTP local | HTTP 200 em /src/features/planning/preview.html, Vite na porta isolada 5186 durante validação. Servidor de preview encerrado após o teste. |
| Inspeção visual/browser | Não realizada: ferramenta retornou “No browser is available”. Não alegar screenshot/E2E visual aprovado. |

## Cobertura

Backend: ciclo rascunho/envio/devolução/reenvio/aprovação; bloqueio de edição; snapshots históricos preservados; revisão desatualizada; corte exato do prazo; documento duplicado; data fora da semana; HH desconhecido/negativo/overflow/escala; responsável ausente; disciplina não esperada; comentário de devolução obrigatório; geração/reabertura XLSX e texto semelhante a fórmula; sessão ausente; acesso negado/indisponível; filtros inválidos não chegam à consulta.

Frontend: transições e bloqueios na store; snapshots/atores DEMO; prazo derivado; validação de responsável/data/HH; sobreposição e intervalo de semanas; HH independente; limites de precisão e observação. Testes executam módulos TS reais transpilados em memória, sem mock da lógica de workflow.

## Restore offline dos testes neste ambiente

O restore normal tentou NuGet sem acesso à rede. A fonte local já instalada resolveu as dependências, copiadas para obj do projeto de teste (nenhuma alteração no package.json/csproj principal):

```powershell
dotnet restore Tests/NA_Connect.WeeklyPlanning.Tests/NA_Connect.WeeklyPlanning.Tests.csproj --source 'C:/Users/Christian Sanatana/.nuget/packages' --packages 'Tests/NA_Connect.WeeklyPlanning.Tests/obj/packages' -p:NuGetAudit=false
dotnet test Tests/NA_Connect.WeeklyPlanning.Tests/NA_Connect.WeeklyPlanning.Tests.csproj --no-restore
```

`NuGetAudit=false` se aplica somente ao comando local de restore offline, não a configuração do produto.

## Reprodução do preview

Na pasta ClientApp, iniciar `node node_modules/vite/bin/vite.js --host 127.0.0.1 --port 5186 --strictPort` e abrir `http://127.0.0.1:5186/src/features/planning/preview.html`. As duas abas pertencem ao preview isolado, sem integração no menu/rotas compartilhados. Para gerar bundle independente: `node src/features/planning/build-preview.mjs`.

Integração com sessão ACC, banco real, persistência e autorização por papel ainda não validada nem implementada. Não há submissão operacional ou exportação oficial disponível; os comandos visuais e arquivos de saída são DEMO/PREVIEW.
