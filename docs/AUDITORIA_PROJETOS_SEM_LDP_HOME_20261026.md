# N&A Connect — Projetos sem LDP integrados à HOME — 26/10/2026

## Decisão definitiva
- Não existe mais cadastro paralelo `audit.LegacyProjectRegistry`: tabela removida após migração com validação de vínculo, origem e usuário. A migração que a criava foi arquivada como `001_LegacyProjectsRegistry.deprecated.txt`.
- Projetos com e sem LDP ficam na **mesma tabela principal** `dbo.Projetos`. Somente **colunas** adicionadas: `Sem_LDP`, `Timesheet_Projeto`, `AFE_Historico`, `Sem_LDP_CriadoEm`, `Sem_LDP_AtualizadoEm`, `Sem_LDP_CriadoPor`. CR-OS continua em `dbo.Projetos.CR_OS`. Sem tabela nova de projetos ou de lançamentos.
- Status do documento é aceito **provisoriamente**. Não existe LDP inventada: `File_ID_LDP_ACC` permanece nulo em projeto histórico.
- `dbo.Projetos.Is_Ativo=1` + `perm.UserProjects` = aparece no endpoint `GET /api/me/projects`, ou seja, na Home, com o **mesmo card dos outros projetos** e selo **Sem LDP**; clique leva à auditoria daquele projeto. O cadastro/edição fica restrito ao MasterAdmin, com atribuição apenas do próprio usuário no registro do projeto.
- As consultas do robô `DatabaseService.GetProjetosAtivos` e `GetProjetoPorId` desconsideram `Sem_LDP=1`. A Home também remove projetos sem LDP da lista que dispara o robô de LDP.
- Novo projeto ACC cadastrado recebe um registro em `dbo.Projetos`, ou reaproveita uma linha existente sem arquivo LDP; caso um projeto já possua uma LDP vinculada, o cadastro histórico bloqueia a conversão. Não cria registro duplicado para o mesmo `Project_ID_ACC`.
- Para ambientes com vários usuários, a permissão de acesso ao projeto continua em `perm.UserProjects`, não é aberta globalmente.

## Fontes de HH: somente as duas fontes já adotadas
- **Por AFE:** `db_na_bi.dbo.tbTimeSheet` (`Projeto` + `AFE`).
- **Por documento:** `DB_NA_CONNECT.dbo.VW_TimeSheet_NA_Corrigida_Projeto` (visão CORRIGIDA do Timesheet; `Projeto` + `doc1`/CR-OS). Continua lendo a visão existente; não persiste nem importa lançamentos para novas tabelas.
- Não existe visão corrigida que exponha `AFE`. AFE e CR-OS são identidades diferentes, inclusive quando coincidirem textualmente. Horas por AFE e documentais são exibidas separadamente para não haver dupla contagem.
- BML011 pode ter Projeto=`BML011` e CR-OS=`BML011.013`; CHS001 pode ter Projeto=CR-OS=`CHS001`. Nunca assumir igualdade ou derivar automaticamente AFE a partir de CR-OS.
- **Confirmado:** `Doc1` = CR-OS; `Doc2` = Área; `Doc3` = Disciplina; `Doc4` = Tipo do Documento; `Doc5` = Sequencial.

## Migração e integridade
- Backup integral verificado: `C:\Program Files\Microsoft SQL Server\MSSQL16.SQLEXPRESS\MSSQL\Backup\DB_NA_CONNECT_before_legacy_home_20261026.bak`.
- Migração aplicada: `Migrations\LegacyProjects\002_LegacyProject_InMainRegistry.sql`.
- Registro existente `UNP001 - UNIPAR` migrado da tabela paralela sem perda dos valores CR-OS, AFE, Projeto Timesheet e conta ACC. Projeto principal #63 passou de inativo a ativo, com `Sem_LDP=1`. Usuário criador foi vinculado em `perm.UserProjects`.
- Verificação pós-migração: `dbo.Projetos` tem 1 projeto marcado `Sem_LDP=1`; antiga `audit.LegacyProjectRegistry` não existe.
- Sem alterações às duas fontes Timesheet, LDPs, custos e documentos ACC.

## Publicação
- Fonte: `Services/LegacyProjects/LegacyProjectRegistryService.cs`, `Controllers/LegacyProjectsController.cs`, `Services/Permissions/PermissionRepository.cs`, `Controllers/MeProjectsController.cs`, `Services/DatabaseService.cs`, `ClientApp/src/pages/Dashboard.tsx`, `ClientApp/src/components/projects/ProjectEnvironmentCard.tsx`, `ClientApp/src/features/legacy-projects/LegacyProjectsPage.tsx`.
- TypeScript + Vite: EXIT 0. .NET: EXIT 0, 0 erros de compilação. Avisos legados preexistentes no build.
- DLL hash: `941D985A80C070B087CC63E04737865CF27CE69BBA0A1AC4A01CD45AC8AC8EA4`.
- Backend implantado com backup anterior: `_backups/legacy-home-binaries_20261026_110221`, novo PID `48564`, porta `5277`. HTTP OAuth 200, /api/legacy-projects 401 sem sessão, /api/legacy-projects/preview 401 sem sessão, /api/plant-integration/projects 401 sem sessão, frontend 200. Reinício bem-sucedido.
- A homologação final da navegação na Home e de salvar um **novo** projeto com sessão MasterAdmin precisa ocorrer no navegador; não foram criados projetos de teste artificiais.
