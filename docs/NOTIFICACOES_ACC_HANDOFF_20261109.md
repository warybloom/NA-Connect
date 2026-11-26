# N&A Connect — 09/11/2026 — Central de notificações / ativação ACC

## Estado confirmado
- NA-1708 online, PID V7 do backend 47856 e HTTP 200 em 5277; frontend HTTP 200 em 5180.
- V7 paralelismo Robôs 01/02 continua em testes pelo usuário. NÃO reiniciar ou substituir a V7.
- Código-fonte editado em C:\Dev\N-A-Connect aa\N-A-Connect, sem deploy.

## Código entregue
1. ClientApp/src/pages/ControlPanel.tsx — após PATCH de ativação, tenta POST /api/permissions/projects/{idAcc}/sync-acc; se falhar apenas avisa (ativação permanece). Botão manual de sincronização, no UsersPanel, mantido.
2. ClientApp/src/features/notifications/NotificationsMenu.tsx — dropdown não modal, contador, filtro não lidas, marcação de lida, resumo inicial dispensável, detalhe de erro e atualização a cada 60s.
3. ClientApp/src/components/Layout.tsx — ícone triangular da central no cabeçalho interno.
4. ClientApp/src/pages/Dashboard.tsx — ícone da central na Home.
5. Controllers/OperationalNotificationsController.cs — lê erros REAIS dos arquivos C:\Robo_NA_Connect\Logs_Historico\Erros_YYYYMMDD.txt (7 dias padrão), agrupa repetidos, vincula com projetos do cadastro, aplica permissões do usuário antes de exibir; status 'A verificar'; endpoints GET /api/notifications e POST /api/notifications/{id}/read.
6. Migrations/Notifications_20261109.sql — tabela dbo.NA_NotificationReads, ainda não aplicada.

## Validações
- npx tsc --noEmit: TSC_EXIT=0 (17.78s).
- dotnet build AccIntegration.WebApp.csproj --no-restore: BUILD_EXIT=0, 0 erros (avisos legados).
- NÃO foi feita verificação HTTP autenticada do novo endpoint, pois backend novo não está publicado.

## CRÍTICO / PRÓXIMA AÇÃO
- Não publicar o build geral do frontend diretamente: release ao vivo foi alterado em 09/11 (monitor dos robôs lado a lado), enquanto alguns arquivos-fonte na base principal estão mais antigos. Reconciliar diffs antes de publicar para não regredir o monitor dos robôs.
- Não reiniciar o backend durante o teste paralelo ativo. Em janela segura, preparar release a partir da V7 e incorporar apenas controller/notificações, aplicar migração SQL de forma controlada e validar permissões/endpoints com sessão real.
- A sincronização automática está no frontend-fonte (não em backend); só funciona para quem pode chamar o endpoint 'permissions.manage'. Avaliar regra mais ampla no backend antes de tratar a automação como concluída para todos os perfis.
- Verificar logs, retentativas e mensagens dos erros; nunca afirmar que um erro histórico está atualmente pendente.
- Frontend e backend de produção continuam na versão anterior; código preparado e compilado, mas não publicado.

## ATUALIZACAO 09/11/2026 — PREVIA PUBLICADA SEM RESTART
- Usuario confirmou que deseja usar o sino original, ao lado do seletor claro/escuro e da casinha. NotificationsMenu.tsx (fonte) foi mudado para usar Bell em vez de AlertTriangle.
- Para liberar o teste sem reiniciar os robos nem sobrescrever o frontend atual, foi instalada uma **PREVIA ISOLADA** que usa o proprio botao Bell atual no frontend publicado (via listener no documento).
- Arquivo: C:\Dev\_services\NAConnect\frontend\releases\release_20261106_multirobot_serial_v4\notifications-preview-v1.js
- Index.html modificado APENAS para carregar o script depois do root. Backup reversao: index.html.before_notifications_preview_20261109.bak
- A previa usa GET /api/worker/status da sessão autenticada (exige acesso robot.view), não altera a ACC, não grava SQL, nem inicia/para robos.
- Exibe erros reais em recentEntries do status do robo, agrupados e marcados "A verificar"; pode mostrar somente eventos recentes do status, não o histórico completo dos arquivos.
- "Marcar lida" nesta prévia usa sessionStorage local, apenas para experimento; NÃO é a persistencia por usuario no SQL preparada na central definitiva.
- Smoke test headless Chrome com fixture sintética e mock local de status: PASS (DOM do painel renderizado, evento de erro exibido). Node --check=0, script HTTP=200.
- Verificação HTTP final: 5180 root=200, JS=200, backend 5277=200, backend V7 PID 47856 preservado.
- A central definitiva (OperationalNotificationsController, migração dbo.NA_NotificationReads, frontend TSX) continua APENAS no fonte, SEM deploy/backend. Nao confundir teste de preview com publicação definitiva.
- Para remover a prévia: restaurar o backup de index.html da mesma pasta. Sem necessidade de reiniciar backend.
