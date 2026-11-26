# AUDITORIA — ADD Projeto sem LDP (MVP)

Implantado no N&A Connect em 26/10/2026. Integração Plant 3D permanece congelada.

## Acesso
- Sidebar: **AUDITORIA → ADD Projeto sem LDP**.
- Rota: `/auditoria/projetos-sem-ldp`.
- A categoria AUDITORIA só aparece para MasterAdmin; a nova rota exige `permissions.manage` na UI, e a API exige também `IsMasterAdmin` real.
- O seletor busca projetos e contas do próprio usuário autenticado no ACC através dos endpoints Autodesk existentes.

## Cadastro histórico
- Vínculo: `ProjectIdAcc`, `HubIdAcc`, nome do projeto, `TimesheetProject` (coluna `Projeto` do BI), `CrOs`, `Afe`.
- Permite cadastrar projetos com CR-OS, AFE ou ambos; sempre exige código de projeto no Timesheet.
- Metadados apenas em `DB_NA_CONNECT.audit.LegacyProjectRegistry`. Nunca insere LDP fictícia, não adiciona registros em `dbo.Projetos`, não altera Timesheet nem o Dashboard BIM.
- Migração: `Migrations/LegacyProjects/001_LegacyProjectsRegistry.sql`, aplicada; tabela inicialmente vazia.
- Documentos tratados **provisoriamente corretos** para análise; não marcados homologados.

## Fontes e semântica, conforme validação do responsável
- `db_na_bi.dbo.tbTimeSheet`: horas por `Projeto` + `AFE`. Sem atribuir automaticamente código de documento.
- `db_na_bi.dbo.tbTimeSheetRealizadoDocumento`: horas por `Projeto` + `doc1` (CR-OS). Inclui `DocumentoCodigo`, `Documento`, `doc2`, `doc3` (disciplina), `doc4` (tipo documento), `doc5` (original).
- Mantém `Doc2` e `Doc5` exatamente como estão, sem interpretar ou normalizar indevidamente.
- Consulta parametrizada, case-insensitive, paginada a 100 lançamentos, com somas independentes, agrupamento documental limitado a 500 grupos.
- **NÃO somar HH brutas AFE + HH de documentos**, pois as fontes podem ter o mesmo lançamento. A reconciliação futura requer uma chave ou regra histórica confiável.
- Custo histórico fica nulo/indeterminado, não zero. Posteriormente vincular profissional e vigência de custo na data do lançamento.
- Se a tabela do BI estiver globalmente vazia, o endpoint avisa possível carga em andamento; a ausência de linhas não implica ausência de horas.
- Foi observada alternância no conteúdo dos dois bancos de origem durante a checagem: `tbTimeSheetRealizadoDocumento` retornou 0 numa leitura e 185.609 noutra, enquanto `tbTimeSheet` alternou. Não foram feitas operações de escrita nessas fontes. Evitar concluir indicadores em cargas parciais.

## APIs
- `GET /api/legacy-projects` — cadastros.
- `POST /api/legacy-projects/preview` — leitura de AFE e documentos, sem persistência.
- `POST /api/legacy-projects` — registrar/atualizar apenas o vínculo.
- `GET /api/legacy-projects/{projectIdAcc}?afeOffset=&documentOffset=` — detalhe paginado conforme o vínculo salvo.
- Fonte: `Services/LegacyProjects/LegacyProjectRegistryService.cs`.
- Controller: `Controllers/LegacyProjectsController.cs`.
- UI: `ClientApp/src/features/legacy-projects/LegacyProjectsPage.tsx`.

## Deploy e evidências
- Backup SQL verificado: `C:\Program Files\Microsoft SQL Server\MSSQL16.SQLEXPRESS\MSSQL\Backup\DB_NA_CONNECT_before_legacy_no_ldp_20261026_1015.bak` (781.316.096 bytes).
- Backup fontes sidebar/rotas: `_backups/legacy-no-ldp-sources_20261026_101217`.
- Backup DLL/PDB: `_backups/legacy-no-ldp-binaries_20261026_102501`.
- Compilação backend `legacy-no-ldp-build-v2.log`: EXIT 0, zero erros; avisos legados preexistentes.
- Frontend TypeScript e build Vite: ambos EXIT 0 (`legacy-no-ldp-final-tsc.log`, `legacy-no-ldp-final-vite.log`).
- Publicação: `C:\Dev\_agent_runs\deploy-legacy-no-ldp.ps1`, EXIT 0. Backend PID 52788, nova DLL SHA256 `6FAA4E06FE15F7A895BE90BC6A488D08AF8551A9777D2FFCEF822C72C464E692`.
- HTTP: OAuth 200, novas rotas API 401 sem sessão, antigo Plant 401 sem sessão, frontend 200, log stderr vazio.
- Cadastro de projetos reais ainda não realizado; **usuário deve selecionar um ACC e informar Projeto Timesheet/CR-OS/AFE reais**.
- Homologação MasterAdmin autenticada pendente; os HTTP 401 validam o bloqueio sem sessão, não o sucesso autenticado.

## Atualização de 26/10/2026 — fonte corrigida e restart
- Regra de negócio confirmada: AFE e CR-OS são **independentes**. Exemplo: campo Projeto= `BML011`, `doc1/CR-OS=BML011.013`; em outros projetos os códigos podem coincidir (ex.: CHS001, conforme cadastro). Não concatenar automaticamente nem inferir igualdade.
- Fonte **documental agora corrigida**: `DB_NA_CONNECT.dbo.VW_TimeSheet_NA_Corrigida_Projeto`, preservando `Projeto`, `doc1`, `DocumentoCodigo`, `doc2`, `doc3`, `doc4`, `doc5`, `Trabalhadas`, `data`, `Nome`, `Funcao`, `Disciplina`, `Descricao`. Não usar a tabela bruta documental para os relatórios da nova tela. Não restringir `Status_Mapeamento_CR_OS=UNICO` pois os projetos antigos podem não estar cadastrados no mapa; o vínculo do usuário faz o recorte.
- Verificação de dados corrigidos: `Projeto=BML011 AND doc1=BML011.013` -> 2.941 lançamentos, 7.126,56 HH, 364 números de documentos distintos no momento do teste. `doc2=000` e `doc5=001` aparecem nos números amostrados.
- Significados dos campos documentais: `Doc1` = CR-OS, `Doc3` = sigla disciplina, `Doc4` = tipo. Na UI, `Doc2` é exibido como **Área (a confirmar)** e `Doc5` como **Sequencial (a confirmar)**, mantendo o valor original.
- Ressalva: na estrutura consultada não existe uma visão corrigida equivalente com a coluna `AFE`; portanto `tbTimeSheet` remota continua **somente consulta AFE auxiliar bruta**, exibida explicitamente separada e jamais somada ao documental até haver regra verificável de reconciliação.
- Nenhuma migração SQL adicional. Backup dos códigos: `_backups/legacy_corrected_timesheet_20261026_103110`.
- Testes: TypeScript EXIT=0, Vite EXIT=0, .NET backend EXIT=0 (zero erros).
- Reinício autorizado pelo usuário executado em 26/10/2026 às 10:38. Novo PID `55416`, porta `5277`, stderr 0 bytes. HTTP OAuth=200, API legacy list/preview=401 sem sessão, API Plant=401 sem sessão, frontend da nova tela=200. DLL SHA256 `58C37BE7C38B0F29AE718336F93E629043E279D2FFDB0BAB710478F7E6269E41`.
- Backup dos binários anteriores: `_backups/legacy-corrected-binaries_20261026_103811`; deploy com verificação de hash e rollback preparado; rollback não necessário.
- Autenticação MasterAdmin e prévia funcional com projeto cadastrado ainda requerem homologação no navegador. Nenhum projeto de produção foi cadastrado automaticamente.
