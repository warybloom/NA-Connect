# Configuração operacional do robô — 21/09/2026

## Implementação

Tela real em **Automação → Configuração do Robô**, rota `/automacao/configuracao-robo`.
Não utiliza demo, localStorage ou cadastro paralelo. Projetos são lidos de `dbo.Projetos`
(`Project_ID_ACC`, `Nome_Projeto`, `Is_Ativo`, `Tipo_LDP`), com hub de `dbo.Hubs_Conta`.
A interface mostra ativos por padrão, permite arrastar, usar setas ou informar ordem,
selecionar etapas por projeto, salvar e executar a revisão salva.

Endpoints:

- `GET /api/robot-config`: configuração, cadastro e catálogo de etapas.
- `PUT /api/robot-config/{robotId}`: salva com `expectedRevision` e `robot`.
- `POST /api/worker/run-configured`: recebe `robotId` e `expectedRevision`; executa somente o snapshot da configuração salva.
- `GET /api/worker/status`: mantém contrato anterior e acrescenta robô, posição/total, etapa, próximo projeto e resultados individuais.
- `POST /api/worker/run`: permanece com o comportamento manual completo anterior. O botão novo usa exclusivamente `run-configured`.

As APIs exigem a sessão existente. Salvar/executar valida cadastro e acesso ACC pelo
`IProjectReportAccess` existente, sem aceitar hub informado pelo cliente. Todos os
projetos da fila são autorizados antes da admissão. O cadastro ativo é revalidado
antes de cada projeto. Não foi criado login ou bypass.

## Persistência

`App_Data/robot-configuration.json`, relativo ao ContentRoot do servidor (neste
projeto, diretório do executável). Para manter a configuração fora de uma pasta de
publicação, definir `RobotConfiguration:Directory` para uma pasta estável e gravável.
Exemplo de desenvolvimento: `C:\Dev\N-A-Connect aa\N-A-Connect\App_Data`.

O arquivo nasce no primeiro salvamento autorizado. Ausência retorna `main` inativo,
sem projetos; não inicia execução. Não foi gravada uma configuração operacional de
projetos durante esta implementação. O JSON contém versão, revisão, data UTC e
coleção de robôs; não contém credenciais. UI atual trabalha apenas com `main`.

Gravação: validação → temporário no mesmo diretório → flush em disco → releitura e
validação → `File.Replace` (ou `File.Move` na primeira gravação). Backup da versão
anterior válida em `.json.bak`. O mutex serializa leitores/escritores do serviço;
revisão otimista rejeita alterações concorrentes com 409. Temporários são removidos
em falhas. Arquivo corrompido bloqueia leitura operacional, salvamento e execução;
não há fallback automático para projetos nem restauração automática do backup.
Recuperação deve ser feita pelo administrador com o serviço parado, após validar
o backup. Esta versão pressupõe uma única instância de servidor.

## Motor, fila e startup

`RobotQueueRunner` adquire o **mesmo RobotRunGate** durante toda a fila, ordena os
projetos, copia as seleções e chama `GrdAutomationWorker.ExecutarCicloConfiguradoAsync`.
Este método chama o **mesmo ProcessarCicloCompleto**, com guardas aditivas de seleção.
Nenhum Worker ou corpo de etapa foi duplicado. Erro de um projeto é registrado e
não impede o próximo. O resultado final distingue sucesso de conclusão com erros.
Fila/status são transitórios; a configuração é persistente. Reiniciar o processo
não retoma fila parcialmente executada. Não há histórico durável novo nesta fase.

Startup passa a consultar a configuração salva: robô inativo/arquivo ausente não
dispara; arquivo inválido bloqueia sem fallback. `Worker:RunOnStartup=false` desliga
esse disparo, mantendo as APIs manuais. A compatibilidade explícita
`RobotConfiguration:LegacyStartup=true` mantém o caminho automático anterior,
incluindo WorkerTest. A configuração operacional não depende do WorkerTest.
Não foi implementado scheduler recorrente. Agenda não nula é rejeitada para evitar
que uma configuração pareça agendada sem estar.

Cancelamento manual foi adiado: várias chamadas legadas não propagam CancellationToken
até operações externas. Não existe kill de thread/processo. O encerramento do host
é observado entre projetos e nos pontos já compatíveis do motor.

## Auditoria das etapas reais

Ordem abaixo é a ordem observada no Worker, não a ordem numérica dos identificadores.
Discovery/download da LDP, preparação de serviços, auditoria e limpeza são
infraestrutura comum e permanecem; até etapas isoladas exigem LDP localizável.

| ID | Nome existente | Habilitação anterior | Dependência / execução isolada |
|---|---|---|---|
| 01 | TIMESHEET | Sempre no pipeline | Sim; reconstrói TIMESHEET |
| 03 | SNAPSHOT TIMESHEET | Após sucesso de 01 | Exige seleção explícita de 01 |
| sem número (`GESTAO_VISTA`) | GESTÃO À VISTA | Extração comum | Sim; lê LDP atual |
| 05 | WORKFLOW | Sempre | Sim; ACC Reviews |
| 00 | DNA DO PROJETO | WorkerTest no Tipo1; equivalente no router Tipo2 | Tipo1 isolado; Tipo2 conjunto 00/09/11 |
| 06 | CUSTO | Tipo1 direto; equivalente Gestão à Vista no Tipo2 | Tipo1 isolado; Tipo2 exige GESTAO_VISTA |
| 09 | CRONOGRAMA | Tipo1 direto; router Tipo2 | Tipo1 isolado; Tipo2 conjunto 00/09/11 |
| 10 | ESPELHAMENTO | Tipo1; desabilitada Tipo2 | Somente Tipo1 |
| 11 | PERSISTÊNCIA NA LDP UNIFICADA | WorkerTest Tipo1; router Tipo2 | Tipo1 isolado; Tipo2 conjunto 00/09/11 |
| 02 | ETAPA 02 | Temporariamente desabilitada | Não selecionável |
| 04 | GRDs | Sempre | Sim; LDP/GRDs acessíveis |
| 5B | SNAPSHOT DAS ETAPAS ATUAIS DOS ACC REVIEWS | WorkerTest | Exige 05 e seu workflowResult |
| 07 | ATRIBUTOS ACC | Sempre | Sim, usa VW_Ldp_Unificada existente; não adiciona 11 |
| 12 | External Action | WorkerTest | Sim, arquivo oficial |
| 13 | Internal Action | WorkerTest | Sim, arquivo oficial |
| 14 | Scope Deviation | WorkerTest | Sim, arquivo oficial |
| 15 | Weekly Schedule | WorkerTest | Sim, arquivo oficial |
| 16 | Hold Control | WorkerTest | Sim, arquivo oficial |
| 17 | Qualidade | WorkerTest | Sim, Base Lançamento/RNC/Desvio |
| 08 | RunApprovalVerdictSyncAsync | Removida por escritor concorrente de Status_In_Flow | Não selecionável |

Tipo2 possui router indivisível: parse/persistência LDP → cronograma → matching →
publicação condicional. Selecionar uma entre 00/09/11 exige selecionar as três.
O motor mantém a ordem interna existente desse conjunto; não foi alterado para
simular execução independente. Nenhuma dependência é acrescentada silenciosamente.
Etapas inválidas, duplicadas e indisponíveis são rejeitadas antes da execução.

## Verificação e limites encontrados

- 13 testes passaram em `Tests/NA_Connect.RobotConfiguration.Tests`: persistir/recarregar,
  ordenar, bloquear inativos, preservar etapas, rejeitar etapas/dependências inválidas,
  fila sequencial, continuar após erro, gate global, corrupção, escrita segura/backup,
  snapshot defensivo, status, sessão/acesso e revisão concorrente.
- Testes usam arquivos temporários e executor controlado. Nenhum projeto real executado.
- Frontend Vite: build concluído (3355 módulos); avisos de tamanho de bundle e Browserslist.
- Build normal do backend: bloqueado na resolução Maven de MPXJ 13.5.0 por rede.
- `Tools/RobotConfiguration.Verify`: todos os fontes C# de produção compilados com
  assemblies locais, zero erros; não substitui o build/publicação normal.
- Host isolado iniciado em 5289 com startup do Worker desligado. APIs retornaram 500
  no middleware de sessão porque o ambiente não consegue descriptografar/gravar as
  chaves Windows em `%LOCALAPPDATA%/ASP.NET/DataProtection-Keys`. Autenticação preservada.
- Ferramenta de browser retornou nenhuma superfície conectada. Não foi possível
  confirmar visualmente cadastro, drag/drop, salvar e Ctrl+F5 com sessão normal.
- Portanto, execução real e validação autenticada ponta a ponta permanecem pendentes.
  Não confundir os testes controlados com uma execução homologada no ACC/SQL.

## Arquivos desta frente

Novos: `Controllers/RobotConfigurationController.cs`; `Services/RobotRun/RobotConfiguration.cs`,
`RobotConfigurationStore.cs`, `RobotProjectDirectory.cs`, `RobotQueueRunner.cs`;
`ClientApp/src/features/robot-configuration/RobotConfigurationPage.tsx` e CSS;
`Tests/NA_Connect.RobotConfiguration.Tests/*`; `Tools/RobotConfiguration.Verify/*`;
este documento e checkpoint 20.

Alterados: `Services/RobotRun/RobotRun.cs`, `Services/GrdAutomationWorker.cs`, `Program.cs`,
`ClientApp/src/App.tsx`, `ClientApp/src/components/Layout.tsx`, `ClientApp/vite.config.ts`.
Mudanças preexistentes de outras frentes foram preservadas. Nenhuma alteração nos
corpos funcionais dos serviços de etapas, Cronograma Tipo2, LDP Tipo2, novo LDP,
Relatórios, Planejador ou Timesheet. Nenhum DDL/DML/migration executado.

## Próxima fase: múltiplos robôs

Expandir a coleção existente, com id/nome/ativo, projetos, ordem e etapas por projeto.
Adicionar agenda (horário, dias, habilitação) e timezone explícito, validação de horário
de verão e política para disparos perdidos. Manter gate global enquanto o motor usa
recursos compartilhados; agendas simultâneas devem enfileirar ou registrar conflito,
nunca iniciar o mesmo motor concorrentemente. Persistir execução, snapshot da revisão,
início/fim, origem, solicitante, resultados por projeto/etapa e tentativas. Só depois
considerar isolamento por robô e paralelismo. Não foram criados robôs extras nem agenda.

Para concluir a homologação: iniciar o backend normal sob a conta autorizada, conectar
o browser já autenticado, abrir a rota, salvar configuração controlada e conferir
GET/reload; escolher explicitamente um projeto seguro antes de executar de verdade.
