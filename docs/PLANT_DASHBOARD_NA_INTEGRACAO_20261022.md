# Integração Plant 3D — N&A Connect e Dashboard BIM (22/10/2026)

## Arquitetura
Dashboard BIM é a fonte somente leitura dos dados P&ID e 3D. O N&A Connect reconstrói o estado atual dos snapshots COMPLETED e deltas COMPLETED, sem depender da tabela PlantRows antiga. Os vínculos de projeto e regras DE/PARA + TAGs ficam exclusivamente no DB_NA_CONNECT.
CHS001: Project_ID_ACC b.fcdb7ebd-1b23-440d-a04b-90391098522e associado ao Dashboard ProjectId 78dc2949-ee24-4a3d-b8af-4e4d0375fa4e. Identidade AUTODESK_PLANT ExternalProjectId 1b4ccae9-5867-4be6-b23e-876b967ee99b. Nome somente auxilia.

## Entrega
Frontend Plant 3D: Resultados e Configuração DE/PARA + TAGs unificada; outras abas ocultas, preservadas. Resultados mostram categorias, TAGs, divergência de atributos e bruto sem TAG. Atualização de metadados a cada 15 segundos, manual via botão.
Backend sob /api/plant-integration: /projects, /catalog, /projects/{projectIdAcc}/link, /status, /schema, /configuration e /results, autorizados por projeto e permissões vigentes. Banco Dashboard não recebe escrita.
Scripts SQL em Migrations/PlantIntegration/20261022_001_PlantProjectLinks.sql criam dbo.PlantProjectLinks e dbo.PlantCategoryMappings no DB_NA_CONNECT. Migração aplicada e vínculo CHS001 persistido; nenhum mapeamento padrão foi salvo sem confirmação.

## Segurança/Validação
Backup DB_NA_CONNECT verificado via RESTORE VERIFYONLY, antes da migração. Backup dos binários em C:\Dev\N-A-Connect aa\N-A-Connect\_backups\plant3d-staged-20261022_172040.
Frontend: TypeScript e build Vite passam. Backend: DLL gerada em estágio isolado sem erros de compilação C# detectados; o build auxiliar foi parado após gerar os binários, devido a consumo anormal de RAM pela dependência Maven. Teste isolado passou para parsing de PnPID numérico, JSON PascalCase, TAG real e linhagem delta. Corrigidos os erros descobertos nesta integração.
Publicado backend novo em 22/10 às 17:21 na porta 5277, PID 65312, com script seguro e rollback em C:\Dev\_agent_runs\plant_na_integration_20261022\deploy-staged-plant.ps1. Vite permanece na porta 5173. Smoke tests: rota Plant, rota catalog e rota TV antiga = 401 anônimo (correto); tela Plant = 200. Nenhum reinício do Dashboard BIM Online.
Log novo N&A: Logs/plant3d_new_20261022_172133.stdout.log e stderr.log (stderr vazio na inicialização).

## Validação funcional pendente
O CHS001 no DB_DASHBOARD_BIM estava sem snapshots completos depois da limpeza. O AutoCAD Plant estava fechado. Para obter resultados reais é obrigatório abrir o Plant, NETLOAD na DLL nova, DBSYNC e login até haver primeira base PnId+Piping. O N&A deverá informar no-baseline até lá, nunca apresentar os dados legados como atuais.
Depois da base, configurar tabelas/atributos e regras de TAG na tela unificada e validar os números com exemplos reais de equipamentos/válvulas/linhas/instrumentos; checar desempenho de reconstrução com 250 mil linhas.
Arquivos de trabalho/revisão Codex+Claude: _agent_handoffs/PLANT_DASHBOARD_20261022. 


## 22/10/2026 — Login e navegação Plant 3D (atualização posterior)

**Correção operacional de login**
- O backend inicialmente publicado em 22/10 estava rodando com `WorkingDirectory=bin\Debug\net9.0`. O `Program.cs` somente dá prioridade ao `appsettings.json` atual do repositório quando iniciado no diretório do projeto com o csproj. Resultado: ConfigSource=DEPLOYED, com `APS:CallbackUrl` e `Permissions:PilotUserIds` anteriores.
- Reiniciado exclusivamente o backend N&A executável, agora com `WorkingDirectory=C:\Dev\N-A-Connect aa\N-A-Connect` e o mesmo binário já compilado. Backend PID 24728 na porta 5277; log mostra `ConfigSource=WORKSPACE` e `PilotCount=3`.
- Validação sem senha real: GET `/Authentication/OAuth/GetLoginUrl` retorna 200, POST `/Authentication/Na/Login` com credenciais fictícias retorna 401 (esperado), frontend HTTP 200. Ainda falta login real do usuário no navegador.
- O script de deploy isolado `C:\Dev\_agent_runs\plant_na_integration_20261022\deploy-staged-plant.ps1` foi ajustado para reiniciar no workspace. ATENÇÃO: o script antigo do projeto `reiniciar-backend.ps1` ainda usa `WorkingDirectory=$target` (bin); uma tentativa de alteração foi bloqueada pela ferramenta. Não o execute sem corrigir esse ponto; não mudar política de segurança ou login para contornar.
- Backup desse script em `_backups\reiniciar-backend-before-auth-workdir_20261022.ps1`.

**Navegação sem aba superior**
- Backup integral dos arquivos de navegação em `_backups\plant3d_sidebar_20261022_173350`.
- Criadas rotas independentes `/plant3d/resultados` e `/plant3d/configuracao`, com renderização direta de `TabDashboard` e `TabSettings`, sem `DashboardHeader` ou `MainTabs`.
- No sidebar MasterAdmin `AUDITORIA > Plant 3D`, apenas duas folhas: `Tela de Resultados` e `Configuração DE/PARA e TAGs`. Outras telas Plant 3D antigas ocultas mas código preservado.
- O legado `/plant3d?tab=dashboard`, `/plant3d?tab=mapping`, etc. e `/auditoria-3d` redirecionam para as rotas novas.
- Rotas protegidas por keys `plant3d.results.view` e `plant3d.mapping.view` respectivamente; autenticação existente preservada.
- TypeScript `tsc --noEmit` passou com exit 0. Build Vite isolada em `C:\Dev\_agent_runs\plant_na_integration_20261022\sidebar-vite-build` passou exit 0. Vite de desenvolvimento está rodando em 5173 com atualizações HMR. Nenhuma mudança de SQL nesta etapa.

**Etapas restantes**
- Componentes, vínculo, persistência, motor e resultado codificados e publicados. Falta homologação ponta a ponta: nova base CHS001 com DBSYNC (PnId e Piping), conferir as regras de TAG e comparação com objetos reais e desempenho de grandes projetos. Sem base, mostrar no-baseline sem dados legados.

## 22/10/2026 — Homologação de fontes reais e correção de TAG herdada

- Na leitura posterior, o CHS001 no DB_DASHBOARD_BIM possui snapshots COMPLETED: PnId 80.011 registros (81/81 lotes), Piping 171.595 (172/172 lotes), ambos da mesma origem/fingerprint e sem revisões incrementais novas na conferência. N&A SQL continua read-only no Dashboard.
- Teste isolado `GetSchemaAsync` leu as duas partes em cerca de 1,7 segundo, retornou 137 tabelas P&ID e 67 Piping, 316 MB de memória managed no processo isolado; não alterou dados.
- Confirmada a herança PnPID: as 655 linhas da classe 3D Valve e as 259 da classe Instrument têm classe ancestral PipeRunComponent com Tag para o mesmo RowId. Antes, o leitor não disponibilizava Tag nessas duas classes, podendo gerar falso sem TAG.
- Ajustado `PlantIntegrationService.RebuildAsync`: depois dos deltas, somente para a parte Piping, copia atributos faltantes do PipeRunComponent em Valve/Instrument de mesmo PnPID, sem criar novas linhas nem alterar a contagem. Teste isolado com os dados reais passou: Valve.Tag e Instrument.Tag expostos, além de LineNumberTag.
- Backup source em `C:\Dev\_agent_runs\plant_na_integration_20261022\PlantIntegrationService.before-inheritance-fix.cs`.
- Backend inteiro recompilado para estágio isolado `C:\Dev\_agent_runs\plant_na_integration_20261022\backend-inheritance`; DLL e PDB gerados, 127 DLLs, nenhuma falha de compilação detectada. Build auxiliar de alta memória foi encerrado após gerar os binários. Publicado somente DLL/PDB do projeto, mantendo configurações/dependências anteriores. Backup em `_backups\plant3d-inheritance-20261022_174612`. Backend PID 62416 após publicação; smoke tests OAuth 200, login fictício 401, Plant protegido 401, frontend 200.
- Padrões sugeridos em `TabSettings.tsx` foram corrigidos e mantidos SEM PERSISTIR: Equipamentos Equipment/Equipment; Válvulas HandValves/Valve; Instrumentos Instrumentation/Instrument; Linhas PipeLineGroup/P3dLineGroup. Todos com campo Tag nos dois lados (herança já exposta para válvulas/instrumentos). Atributos adicionais não comprovados foram removidos dos defaults; usuário poderá configurar extras via DE/PARA. Backup em `_backups\TabSettings_before_real_pnid_schema_20261022.tsx`.
- TypeScript tsc --noEmit e build Vite depois de toda a revisão passaram exit 0 (ver `C:\Dev\_agent_runs\plant_na_integration_20261022\final-front-tsc.log` e `final-vite.log`).
- Ainda falta HOMOLOGAÇÃO de autenticação real do usuário, salvar/validar as quatro regras sugeridas no CHS001 e conferir resultados de pareamento e quantitativos contra dados conhecidos; o sistema não deve inventar dados quando falta mapeamento. Não declarar a funcionalidade 100% homologada antes dessa conferência.
