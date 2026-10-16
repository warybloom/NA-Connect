# Fixtures do cache do PBIX — SOMENTE TESTE

Linhas do cache de dados embutido em `F:\BI N&A CONNECT\CHS001-000-PM0-RT-002-01.pbix` (cópia com SHA-256
`6a5d0a83021c10f9a4e0e55ea71819b92a28ee29d0ef782ed5759c6983aaa197`, igual ao original), lidas com pbixray em 15/10/2026.
Usadas apenas por `ClientApp/src/features/production/biChs001/__tests__/paridadePbix.test.ts` e
`_bi_chs001_replica/tools/paridade_dados.cjs` para provar que o motor reproduz o Power BI. **Não são dado de produção:**
nada no aplicativo importa estes arquivos; a tela lê sempre o banco pelos endpoints `/api/bi-chs001/*`.

| Arquivo | Tabela do modelo | Linhas | Colunas mantidas |
|---|---|---:|---|
| `ldp4.json` | `LDP (4)` | 442 | colunas usadas pelas páginas LDP (sem HH/custo) |
| `programacao.json` | `Programação Semanal` | 148 | colunas da página Programação (sem responsáveis) |
| `wbx.json` | `WBX` (folhas) | 755 | Level 3–7, TaskID, EDT, Status1, Disciplina, Responsabilidade, Pacote, NA DOCUMENT NUMBER, datas (ISO, segundo arredondado) |
