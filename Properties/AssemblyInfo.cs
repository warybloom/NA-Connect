using System.Runtime.CompilerServices;

// Permite que o projeto de testes acesse os membros "internal" usados para testar, de forma isolada,
// a lógica pura da correção Status_In_Flow (extração segura de approveStatus/itemUrn, decisão de
// status por versão, correspondência N&A/Cliente/composto) sem expô-los como API pública do serviço.
[assembly: InternalsVisibleTo("NA_Connect.Workflow.Tests")]

// Permite que o harness de diagnóstico externo (C:\Dev\DiagnosticoHarness, fora do repositório) chame
// diretamente a lógica real de correspondência (EncontrarCorrespondenciaControlada) em vez de manter uma
// réplica duplicada — evita divergência entre o que o harness simula e o que a produção realmente faz.
[assembly: InternalsVisibleTo("DiagnosticoHarness")]

// Permite que os testes locais do Cronograma Tipo 1 (xUnit, sem acesso a SQL/ACC) exercitem
// diretamente a resolução estrutural de REALIZADO (alias/título de coluna), a distribuição de
// valores, a resolução da coluna %PHYSICAL COMPLETED na LDP e a resolução de documentos duplicados.
[assembly: InternalsVisibleTo("NA_Connect.Cronograma.Tests")]

// Permite que os testes da Auditoria Operacional (Fase 2) exercitem diretamente os helpers "internal"
// de infraestrutura (ReportFileNaming, AuditConnectionStringResolver) sem expô-los como API pública.
[assembly: InternalsVisibleTo("NA_Connect.OperationalAudit.Tests")]

// Permite que os testes de LDP Tipo 2 / Cronograma Tipo 2 (parser dinâmico de cabeçalho, duplicidade
// N&A/Client e matching por N&A com fallback para Client Document Number) exercitem diretamente
// LdpTipo2HeaderResolver.LocalizarLinhaCabecalho e CronogramaTipo2LdpSyncService.ResolverDecisoesDeMatching
// sem expô-los como API pública.
[assembly: InternalsVisibleTo("NA_Connect.LdpTipo2.Tests")]

// Permite que os testes da ETAPA 03 (snapshot Timesheet) exercitem diretamente
// GestaoVistaTimesheetSnapshotRepository.DentroDaTolerancia (regra pura de validação Excel==Banco) sem
// expô-la como API pública.
[assembly: InternalsVisibleTo("NA_Connect.GestaoVista.Tests")]

// Permite que os testes da Auditoria de Qualidade exerçam as regras puras sem expô-las na API pública.
[assembly: InternalsVisibleTo("NA_Connect.Qualidade.Tests")]
