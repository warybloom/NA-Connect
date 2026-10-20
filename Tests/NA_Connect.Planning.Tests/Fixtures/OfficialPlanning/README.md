# Evidências oficiais de Planejamento

Os cinco XLSX são cópias byte a byte dos arquivos fornecidos em `TEMPLATES PLANEJAMENTO`. Não editar estas cópias para criar cenários negativos: os testes abrem streams/cópias em memória. Os hashes esperados estão nos testes e em `build_verify_output/planning-official/official-hashes-final.json`.

CAP Externo e Interno têm o mesmo SHA-256. A classificação vem da pasta/processo solicitado. HOLD tem nove headers mesclados nas linhas 7/8, sem subheader DATE. As linhas atuais são evidências; a descoberta aceita início de cabeçalho nas linhas 1..10.

`sql-schema-evidence.json` é o resultado de uma consulta somente leitura a INFORMATION_SCHEMA.COLUMNS em 2026-09-10, usando a configuração LocalConnection do repositório. Não contém credenciais, registros de projetos nem dados pessoais. É evidência versionada para testes de tipos e nulabilidade, não configuração de produção.

Execução reproduzível, a partir da raiz do repositório:

```powershell
dotnet test Tests/NA_Connect.Planning.Tests/NA_Connect.Planning.Tests.csproj --artifacts-path Tests/.planning-official-artifacts
```

Nenhum teste depende do caminho absoluto externo dos templates, de macros ou de conexão SQL ativa.
