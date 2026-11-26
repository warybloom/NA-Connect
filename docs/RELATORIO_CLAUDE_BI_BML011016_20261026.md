# RELATÓRIO — Backend BI BML011.016 (Claude) — 2026-10-26

## Entregue (código, NÃO compilado nem testado nesta sessão — ver "Verificação")
- `Controllers/BiBml011016Controller.cs` — `GET api/bi-bml011016/{status, fontes/{fonte}, pbix/manifesto, pbix/recurso}`.
- `Services/BiBml011016/BiBml011016MasterOnlyAttribute.cs` — gate Master Admin (filtro `Order=int.MinValue` + repetição em cada action).
- `Services/BiBml011016/BiBml011016Projeto.cs` — resolução EXATA do projeto (token `BML011.016` em `dbo.Projetos.Nome_Projeto`).
- `Services/BiBml011016/BiBml011016DataSource.cs` — leituras somente SELECT das fontes reais.
- `Services/BiBml011016/BiBml011016Catalogo.cs` — 9 páginas / 9 fontes, estados de mapeamento.
- `Services/BiBml011016/BiBml011016Pbix.cs` — PBIR/assets só via endpoint protegido, SHA256 conferido, `DataModel` nunca servido.
- `Tests/NA_Connect.BiBml011016.Tests/` — 3 arquivos, 62 casos (sem banco, fakes).
- `docs/CONTRATO_BI_BML011016.md` — contrato com Codex.
- `_bi_bml011016/tools/inspect_model.py` — extrator pbixray do DataModel (NÃO executado; ver pendências).

Não alterado: frontend, sidebar, normalizador BYD002, Curva S, `BiChs001Controller` (apenas reaproveito constantes públicas
de SQL), `Program.cs` (sem DI nova: o controller cria a fonte SQL), `appsettings*`, banco (nenhum DDL/escrita).

## Relatório de autenticação
| Caso | Resultado | Teste |
|---|---|---|
| Anônimo | 401, nenhuma consulta ao banco | `SegurancaTests.Anonimo_401_SemTocarNasFontes` (6 rotas) |
| Sessão não-master, `Enforce=false` | 403 | `NaoMaster_403_ComEnforceDesligado` |
| Sessão não-master, `Enforce=true` (admin `permissions.manage`, Gestão, TV, vínculo com projeto) | 403 | `NaoMaster_403_ComEnforceLigado` — o gate só olha `IsMasterAdmin`; o fake lança exceção se alguém consultar keys |
| Falha ao obter identidade | 503 (fecha) | `FalhaNaIdentidade_503_FalhaFechada` |
| Master | 200 | `Master_200` |
| `?projectId=` forjado | ignorado; usa ACC resolvido no servidor | `ProjectIdDoRequestEIgnorado_…` |
| Filtro roda antes do `[RequirePermission]` | `Order=int.MinValue` | `Filtro_DecideAntesDeTudo_…` |
| Somente GET, `no-store`, sem `AllowAnonymous`/`PermissionExempt` | — | `Controller_RotaIsolada_…` |

Observação: com `Permissions:Enforce=false` (estado atual), `IsMasterAdmin` vem só de `Permissions:MasterAdmins`
(hoje `christian.santana@naconsult.com.br`). Perfil MasterAdmin atribuído no banco só conta com `Enforce=true` — é a regra
do `PermissionService`, mantida (falha fechada).
`[RequirePermission("permissions.manage")]` foi incluído só para cumprir o contrato `EndpointPermissionContractTests` sem
ampliar a lista fechada de isentos; ele nunca concede acesso (o gate master decide antes).

## Páginas × fontes
| # | Página | Fontes | Estado |
|---|---|---|---|
| 1 | Resumo (34) | ldp, cronograma, cronograma-diario, programacao-semanal | MAPEADA por inventário* |
| 2 | LDP (18) | ldp | MAPEADA* |
| 3 | CAP externo (13) | cap-externo (`vw_Latest_ExternalAction`) | MAPEADA* |
| 4 | Critical Analysis (10) | — | ESTATICA (interações não verificadas) |
| 5 | Programação (8) | cronograma, cronograma-diario | MAPEADA* |
| 6 | Controle de Desvio (13) | desvios (`vw_Latest_ScopeDeviation`) | MAPEADA* |
| 7 | Controle de Holds (9) | holds (`vw_Latest_HoldControl`) | MAPEADA* |
| 8 | Curva financeira (6) | curva-financeira | **PENDENTE — fonte externa** |
| 9 | Pagamento (7) | pagamentos | **PENDENTE — fonte externa** |

\* `correspondencia: "INVENTARIO"`: casada pelos nomes de tabela do inventário da atividade, NÃO pelas expressões DAX.

## Pendências de fontes reais
1. **Curva financeira**: não há no banco progresso planejado/receita/valor agregado mensal do BML011.016. Precisa do
   arquivo/sistema de origem da tabela do PBIX e aprovação de importação.
2. **Pagamentos**: não há BM, competência, NF nem prazos/valores de pagamento. Mesmo requisito.
3. **cronograma-diario (AssignmentByDay)**: depende de `dbo.VW_Cronograma_Tarefa_Dia_Ultima_Versao`; se a view não
   existir, a fonte responde `INDISPONIVEL` (Resumo/Programação ficam sem curva S/HH diário).
4. **cronograma**: lido de `VW_Cronograma_Tipo2_Ultima_Versao`; se o BML011.016 não for Tipo 2 ou não tiver .mpp lido,
   responde `VAZIO`.
5. **Projeto**: a resolução no cadastro real NÃO foi conferida (sqlcmd bloqueado). Se `Nome_Projeto` não contiver o token
   `BML011.016`, tudo fica `PENDENTE` com `NAO_ENCONTRADO`; nesse caso informar o nome cadastrado.
6. **DataModel/DAX/relações do PBIX**: não inspecionados — Python/pbixray, leitura do ZIP via .NET e sqlcmd exigiram
   aprovação e foram negados nesta sessão. Só o SHA256 foi conferido (idêntico: `448947F7…E634`).
7. **Timesheet**: não exposto — nenhuma página do inventário exige HH realizado de timesheet de forma comprovada.
8. **PBIR protegido**: precisa `BiBml011016:PbixPath` no servidor para `pbix/*` funcionar.

## Verificação
- NÃO executado: `dotnet build`, `dotnet test`, `graphify update .`, consultas SQL — todos exigiram aprovação nesta sessão.
- Para rodar: `dotnet test Tests/NA_Connect.BiBml011016.Tests -o "C:\Dev\N-A-Connect aa\_build_verify\bi_bml011016_tests"`
  e também `Tests/NA_Connect.Planning.Tests` (contrato de endpoints).
- Nenhum build/deploy/reinício; backend da porta 5277 intocado.
