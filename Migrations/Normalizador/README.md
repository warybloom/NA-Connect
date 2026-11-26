# Normalizador de Siglas — migração (NÃO aplicada)

Aditiva e exclusiva do normalizador: cria só o schema `normalizador` com `Importacao`, `SiglaDisciplina` e
`SiglaDisciplinaAuditoria`. Não lê, não altera e não apaga nenhuma tabela legada (LDP, Timesheet, Norma de
Coordenação, cadastros de disciplina, projetos). Sem seed: nenhuma sigla é criada por script.

Enquanto não for aplicada, a API `/api/configuracoes/normalizador-siglas` responde 503 `instalado=false`, a tela de
Configurações mostra a instrução e a Gerência da Engenharia segue sem normalização (comportamento atual).

## Aplicar (somente com aprovação)
1. Backup verificável (cópia, não interfere na cadeia de logs):
   ```sql
   BACKUP DATABASE [DB_NA_CONNECT] TO DISK = N'C:\Backups\DB_NA_CONNECT_pre_normalizador_20261019.bak'
     WITH COPY_ONLY, CHECKSUM, INIT, STATS = 10;
   RESTORE VERIFYONLY FROM DISK = N'C:\Backups\DB_NA_CONNECT_pre_normalizador_20261019.bak' WITH CHECKSUM;
   ```
2. (Recomendado) restaurar o .bak num clone (`DB_NA_CONNECT_CLONE_NORMALIZADOR`), trocar o `USE` dos dois scripts para o
   clone, rodar UP → UP (idempotência) → DOWN → UP e conferir `SELECT * FROM sys.objects WHERE schema_id = SCHEMA_ID('normalizador')`.
3. Produção: `sqlcmd -S .\SQLEXPRESS -E -i 20261019_001_NormalizadorSiglas_UP.sql`.
4. Não é preciso reiniciar o backend para a tabela passar a ser usada — mas o código novo (controller/serviço) só existe
   no processo depois de um deploy/restart feito pelo usuário.

## Rollback
`20261019_001_NormalizadorSiglas_DOWN.sql` remove apenas os objetos acima. Se houver dados ele RECUSA, até que
`@ConfirmoExportacao = 1` seja definido depois de exportar as três tabelas. Código: reverter os arquivos listados em
`_agent_handoffs/RESULTADO_CLAUDE_GERENCIA_1_2_20261019.md` (o painel volta a não normalizar).

## 20261026_002 — Exceções pontuais de posição da disciplina (NÃO aplicada)
Aditiva: cria só `normalizador.ExcecaoPosicaoDisciplina` e `normalizador.ExcecaoPosicaoDisciplinaAuditoria`. Sem seed.
Não existe configuração "DOC4 do projeto": o padrão é sempre DOC3. Cada exceção vale para um número de documento exato
(CODIGO) ou um padrão limitado "DOC3 = X e DOC4 = sigla exata" (PADRAO), só no projeto dela. Sem a migração (ou sem exceção
ativa), todo documento é lido no DOC3 — comportamento anterior. Candidatos e prévia funcionam sem a migração; só a gravação
responde 503 `instalado=false`. Reverter = desativar a exceção na tela (auditoria preservada).
1. Backup: `BACKUP DATABASE [DB_NA_CONNECT] TO DISK = N'C:\Backups\DB_NA_CONNECT_pre_excecao_posicao_20261026.bak' WITH COPY_ONLY, CHECKSUM, INIT, STATS = 10;`
   e `RESTORE VERIFYONLY ... WITH CHECKSUM;`.
2. `sqlcmd -S .\SQLEXPRESS -E -i 20261026_002_ExcecaoPosicaoDisciplina_UP.sql` (pode rodar 2x: idempotente; em transação).
3. Rollback: `20261026_002_ExcecaoPosicaoDisciplina_DOWN.sql` (recusa se houver dados, até `@ConfirmoExportacao = 1`).
