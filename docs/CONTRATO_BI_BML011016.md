# CONTRATO — BI BML011.016 (backend Claude ↔ frontend Codex)

Versão 1 — 2026-10-26. Dono do backend: Claude (`Controllers/BiBml011016Controller.cs`, `Services/BiBml011016/**`,
`Tests/NA_Connect.BiBml011016.Tests/**`). Dono do frontend: Codex (`ClientApp/src/features/production/biBml011016/**`,
sidebar, `App.tsx`). Mudança de contrato = editar ESTE arquivo e registrar no histórico no fim.

Fonte de referência: `BML011.016.pbix` (SHA256 `448947F79CB8F9C20A45246B8FE222B58097C415D8B450D3E83E1BEEB862E634`).
Os valores gravados no PBIX NÃO são tratados como atuais: o backend só devolve linhas lidas agora do banco do N&A Connect.

## 1. Segurança (vale para TODAS as rotas `/api/bi-bml011016/**`)

| Situação | HTTP | Corpo |
|---|---|---|
| Sem sessão (N&A ou legado Autodesk) | **401** | `{ "message": "Sessão expirada." }` |
| Sessão válida, `IsMasterAdmin !== true` (inclui admin com `permissions.manage`, Gestão, TV, quem tem acesso ao projeto) | **403** | `{ "message": "BI BML011.016 restrito ao Master Admin." }` |
| Falha ao validar a identidade (banco de permissões fora) | **503** | `{ "message": "Não foi possível validar permissões." }` |
| Master Admin | 200 | conforme abaixo |

- Decisão SEMPRE no servidor por `IPermissionService.GetCurrentUserAsync(...).IsMasterAdmin`, a cada request,
  inclusive com `Permissions:Enforce=false`. Nenhum `projectId`, claim ou cabeçalho do frontend é aceito/considerado:
  o projeto é resolvido no servidor (seção 2). Parâmetro `projectId` na query é ignorado pelo BI.
- Todas as respostas: `Cache-Control: no-store`.
- O frontend deve, ALÉM disso, esconder o menu/rota quando `/api/permissions/me` não trouxer `isMasterAdmin === true`
  e tratar 401/403/503 como "não renderizar" (nunca exibir dado em cache após 401/403).

## 2. Projeto

Resolvido no servidor em `dbo.Projetos` por correspondência EXATA do código `BML011.016` em `Nome_Projeto`
(token delimitado: `BML011.0160`, `XBML011.016`, `BML011.015`, `BML016` NÃO casam). Opcionalmente fixado por
configuração `BiBml011016:ProjectIdAcc` (só servidor) — mesmo assim o nome precisa casar exatamente.
0 ou >1 candidatos ativos ⇒ `projeto.estado = "NAO_ENCONTRADO" | "AMBIGUO"` e todas as fontes viram `PENDENTE`.

```jsonc
"projeto": {
  "estado": "RESOLVIDO" | "NAO_ENCONTRADO" | "AMBIGUO" | "INDISPONIVEL",
  "codigo": "BML011.016",
  "projetoId": 123 | null,          // dbo.Projetos.ProjetoID
  "projectIdAcc": "b.xxxxxxxx-…" | null,
  "nome": "…" | null,
  "motivo": "…" | null,
  "candidatos": [ { "projetoId": 1, "nome": "…", "ativo": true } ]   // só quando AMBIGUO
}
```

## 3. Rotas

### GET `/api/bi-bml011016/status`
Projeto resolvido + catálogo de páginas (ordem do PBIX) + catálogo de fontes com estado de MAPEAMENTO (sem ler dados).

```jsonc
{
  "relatorio": "BML011.016",
  "pbix": { "sha256Esperado": "4489…E634", "recursosDisponiveis": true|false, "motivo": "…"|null },
  "projeto": { …seção 2… },
  "paginas": [
    { "ordem": 1, "nome": "Resumo", "visuais": 34, "fontes": ["ldp","cronograma","cronograma-diario","programacao-semanal"],
      "estado": "PARCIAL", "observacao": "…" }
    // … 9 páginas, ordem: Resumo, LDP, CAP externo, Critical Analysis, Programação, Controle de Desvio,
    //   Controle de Holds, Curva financeira, Pagamento
  ],
  "fontes": [
    { "fonte": "holds", "origem": "dbo.vw_Latest_HoldControl", "mapeamento": "MAPEADA" | "PENDENTE", "motivo": "…"|null,
      "tabelasPbix": ["HOLDS"], "correspondencia": "INVENTARIO" }
  ],
  "geradoEm": "2026-10-26T12:00:00Z"
}
```
`paginas[].estado`: `MAPEADA` (todas as fontes mapeadas), `PARCIAL`, `PENDENTE` (nenhuma fonte), `ESTATICA` (só layout).

### GET `/api/bi-bml011016/fontes/{fonte}`
`{fonte}` ∈ catálogo fixo (outro valor ⇒ 404). Envelope ÚNICO:

```jsonc
{
  "fonte": "holds",
  "estado": "OK" | "VAZIO" | "PENDENTE" | "INDISPONIVEL",
  "motivo": "…" | null,              // obrigatório quando estado ≠ OK
  "origem": "dbo.vw_Latest_HoldControl" | null,
  "tabelasPbix": ["HOLDS"],
  "correspondencia": "INVENTARIO" | "COMPROVADA",   // INVENTARIO = casada pelo inventário de páginas; DAX ainda não conferido
  "projeto": { …seção 2… },
  "ultimaSincronizacao": "2026-10-20T08:00:00" | null,   // MAX(Data_Sincronizacao) das linhas, quando houver
  "geradoEm": "…",
  "colunas": [ { "nome": "STATUS", "tipo": "texto" | "inteiro" | "decimal" | "dataHora" | "booleano" | "outro" } ],
  "totalLinhas": 0,
  "linhas": [ { "STATUS": "…", … } ]  // valores como armazenados (texto não é convertido)
}
```
Regras do frontend: `OK` ⇒ aplicar as medidas do PBIX sobre `linhas`; `VAZIO` ⇒ fonte existe mas não há linhas para o
projeto (mostrar "Sem dados na fonte"); `PENDENTE`/`INDISPONIVEL` ⇒ "Pendente de mapeamento" + `motivo`. NUNCA trocar
por zero, 100% ou valor do PBIX.

Catálogo de fontes (v1):

| fonte | origem (somente SELECT) | Tabelas/páginas do PBIX | mapeamento |
|---|---|---|---|
| `ldp` | `dbo.Ldp_Unificada` (snapshot atual `PmoLdpSnapshot`) + gates 30/60/90 `VW_Ldp_Documento_Gates_Ultima_Versao` | LDP, DISCIPLINE, DOCUMENT STATUS, SITUATION (Resumo, LDP) | MAPEADA |
| `cronograma` | `dbo.VW_Cronograma_Tipo2_Ultima_Versao` (última leitura do .mpp) | WBX Level 3..9, início/término, Trabalho (Resumo, Programação) | MAPEADA (colunas Início/LB/pai só se existirem na view) |
| `cronograma-diario` | `dbo.VW_Cronograma_Tarefa_Dia_Ultima_Versao` | AssignmentByDay (curva S, HH diário) | MAPEADA se a view existir; senão `INDISPONIVEL` |
| `programacao-semanal` | `dbo.tb_WeeklyEngineeringSchedule` (último lote) | KPI weekly planned/actual/deviation (Resumo) | MAPEADA |
| `cap-externo` | `dbo.vw_Latest_ExternalAction` | ACTION ITEM LIST, Company3, Responsible, Schedule, Status | MAPEADA |
| `desvios` | `dbo.vw_Latest_ScopeDeviation` | DESVIO (HH, disciplina, status, data, descrição) | MAPEADA |
| `holds` | `dbo.vw_Latest_HoldControl` | HOLDS (status, motivo, responsável, documento) | MAPEADA |
| `curva-financeira` | — | Curva financeira (progresso planejado, receita, valor agregado/mês) | **PENDENTE** |
| `pagamentos` | — | Pagamentos (BM, competência, NF, prazos/valores) | **PENDENTE** |

`dCalendario` do PBIX é tabela de datas derivada: o frontend gera a partir das datas das linhas (não há endpoint).

### GET `/api/bi-bml011016/pbix/manifesto`
Lista os recursos do PBIX servidos pelo backend (somente entradas `Report/**`; `DataModel` NUNCA é servido).
Requer `BiBml011016:PbixPath` configurado no servidor e SHA256 idêntico ao esperado.

```jsonc
{ "estado": "OK" | "PENDENTE" | "PBIX_DIVERGENTE", "motivo": "…"|null, "sha256": "…"|null,
  "recursos": [ { "caminho": "Report/definition/pages/xxx/page.json", "bytes": 1234 } ] }
```

### GET `/api/bi-bml011016/pbix/recurso?caminho=Report/...`
Conteúdo bruto da entrada (content-type por extensão; `nosniff`; `no-store`). 404 se o caminho não estiver no
manifesto; 409 se PBIX ausente/divergente. Use isto em vez de copiar assets sensíveis para `public/`.

## 4. Garantias do backend
- Somente leitura (SELECT); nenhuma escrita em LDP, Timesheet, número de documento, revisão, custos ou configurações.
- Nenhuma rota existente muda (incl. `/api/bi-chs001/**`).
- Erro SQL de UMA fonte (objeto/coluna inexistente) ⇒ essa fonte responde `INDISPONIVEL` com motivo; as demais seguem.

## 5. Configuração do servidor (não versionar caminhos pessoais)
- `BiBml011016:PbixPath` — caminho do PBIX de referência no servidor. Sem ele, `pbix/*` responde `PENDENTE`/409.
- `BiBml011016:ProjectIdAcc` — opcional, só para desempatar `AMBIGUO`.

## Histórico
- v1 2026-10-26 (Claude): proposta inicial publicada antes da implementação; implementada em `BiBml011016Controller`
  (`Services/BiBml011016/*`). Todas as fontes estão `correspondencia: "INVENTARIO"` até a conferência do DataModel/DAX.
