# PROMPT — Codex — AUDITORIA SOMENTE LEITURA do BI CHS001 isolado (15/10/2026)

**Modo obrigatório: SOMENTE LEITURA.** Não editar nenhum arquivo existente, não fazer deploy, não reiniciar serviços, não executar
SQL de escrita (INSERT/UPDATE/DELETE/DDL) em nenhum banco, não baixar/alterar o .mpp no ACC. O único arquivo que você pode criar é
`_bi_chs001_replica/CODEX_VALIDACAO_FINAL_ISOLAMENTO.md` (relatório). Scripts de teste próprios: só em pasta temporária fora do repositório.

Repositório: `C:\Dev\N-A-Connect aa\N-A-Connect`. Banco: `.\SQLEXPRESS` / `DB_NA_CONNECT` (somente SELECT). Clone de teste disponível:
`DB_NA_CONNECT_BI_TESTE` (pode ler; não escreva nele). PBIX: `F:\BI N&A CONNECT\CHS001-000-PM0-RT-002-01.pbix` (somente leitura).

## O que foi implementado (Claude, sessão BI_CHS001_IMPL_ISOLADO_20261015)
Leia primeiro `_bi_chs001_replica/RELATORIO_FINAL_IMPLEMENTACAO_BI_CHS001_20261015.md`.
- `Services/BiChs001Schedule/*` — leitor MPXJ isolado (cópia do .mpp do ACC), repositório (schema `bi_chs001`, uma transação por versão), serviço de sincronização (2-legged, mesmos caminhos do Tipo 2, que NÃO foi alterado).
- `Controllers/BiChs001ScheduleController.cs` — `GET api/bi-chs001/schedule/{status,tarefas,trabalho-diario,trabalho-semanal}`, `POST .../sincronizar` (robot.run).
- `Migrations/BiChs001/001_bi_chs001_UP.sql` / `_DOWN.sql` — só objetos `bi_chs001` (aplicado em produção às 23:02 de 15/10; versão 1 = .mpp v47).
- `Tools/BiChs001.Sync` (CLI), `Tests/NA_Connect.BiChs001.Tests` (xUnit, banco de teste), `Scripts/Deploy-BiChs001Isolado-20261015.ps1`.
- Frontend `ClientApp/src/features/production/biChs001/**`: fonte isolada com alternativa legada, versão única tarefas×série, alias auditável,
  medidas 3D 30% (TREATAS), matriz hierárquica Level 4–7 com expansão salva do PBIR, segmentação dSemana, correção do filtro Not sobre outra entidade.

## Auditar (com evidências e gravidade)
1. Isolamento: nenhum arquivo/tabela/view/importador/MPP legado modificado (compare `Services/CronogramaTipo2/*`, `Models/CronogramaTipo2Registro.cs`,
   `dbo.*`, views legadas). Use `sys.objects.modify_date` e o inventário em `C:\Dev\N-A-Connect aa\_backups\bi_chs001_isolado_20261015\inventario\`.
2. Migração: UP/DOWN idempotentes, sem `ALTER/UPDATE/DELETE` em dbo; backup `.bak` (`RESTORE VERIFYONLY` / `RESTORE HEADERONLY`) e rollback.
3. Leitor MPXJ: Start/Finish/LB, Work/ActualWork/BaselineWork, pai, nível, atribuições, timephased diário pelo calendário efetivo; unidades.
4. Série semanal (domingo) × `AssignmentByDay` do PBIX; conciliação em `_bi_chs001_replica/conciliacao_isolado_pbix_20261015.json`.
5. Alias `Emgineering - 30%` → `Engineering - 30%`: só CHS001, só resumo, grafia exata, valor bruto preservado; contagens 196/347/190.
6. Medidas DAX × motor: Resumos 30/60/90 (ACC, BASE SEM, curvas, prazos), 3D 30% (TREATAS), matriz (ALLSELECTED, Min(EDT) textual), dSemana.
7. Contratos/permissões da API (401/403/404/503; robot.run no POST); restrição ao projeto no backend.
8. UI: 10 telas (Resumo 30/60/90, LDP Eng 30/60/90, LDP 3D 30/60/90, Programação), duas logos extras ausentes, mensagens de fonte.
9. Regressões: build do checkout principal (ATENÇÃO: `_bi_chs001_replica/auditoria_codex_independente/mpp_probe` — sua sonda com bin/obj —
   faz o `dotnet build` do site falhar com CS8802/atributos duplicados; registre e proponha a correção, sem editar).

Rodar (só leitura): `node _bi_chs001_replica/tools/testar.mjs` (a partir de uma cópia), `dotnet test Tests/NA_Connect.BiChs001.Tests
--filter "FullyQualifiedName~MppReaderTests|FullyQualifiedName~ApiContratoTests"` (leem o .mpp e o banco de TESTE; a classe
SnapshotSqlTests GRAVA no banco de teste — não rode), `python -I _bi_chs001_replica/tools/conciliar_isolado_pbix.py <leitura.json> <saída fora do repo>`.
Builds: sempre com `-o` para pasta fora do repositório.

Saída única: `_bi_chs001_replica/CODEX_VALIDACAO_FINAL_ISOLAMENTO.md` com achados (gravidade, arquivo:linha, evidência, correção sugerida).
