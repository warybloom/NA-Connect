# UNP001 — Disciplina e simulação de LDP provisória (26/10/2026)

## Diagnóstico
- `dbo.Projetos` projeto 63 / `UNP001` / `Sem_LDP=1` sem norma de coordenação vinculada.
- `dbo.VW_TimeSheet_NA_Corrigida_Projeto` pelo par `Projeto=UNP001`, `doc1=UNP001`: **1484 lançamentos**, **198 códigos únicos** e **3290,38 HH**.
- Todas as **32 siglas doc3** encontradas possuem regras globais ativas em `normalizador.SiglaDisciplina`; cadastro adicional dispensável.
- Fonte gerencial por `Project_ID_ACC` não produzia lançamentos e o antigo resolvedor devolvia SEM DISCIPLINA pela ausência da Norma do projeto.

## Correções
- `PmoActualFactsProvider`: para `Sem_LDP=1`, a classificação aplica Norma do projeto quando disponível; se não resolver, usa `SiglaNormalizer.Resolver` com regras ativas de `normalizador.SiglaDisciplina` com prioridade de contexto e global, `Doc3` e os dois primeiros caracteres. Para projetos com LDP, regra vigente da Norma intocada.
- `ProjectActualFacts.ProvisionalDocuments`: reconstrói inventário por código existente sem inserir em LDP; cada documento só uma vez.
- `GestaoVistaReportRepository`: disponibiliza os documentos provisórios por disciplina no Resumo, com HH real por código. Não infere HH prevista, custo profissional antigo, datas, emissão, cancelamento ou avanço físico.
- `ResumoProducaoPage`: para projeto sem LDP mostra inventário real; situação não comprovada mostra travessão, não zero; `ResumoDisciplineCard` mostra HH por disciplina, documentos e indicação de situação indisponível.
- `LegacyProjectsController`: novo `GET /api/legacy-projects/{projectIdAcc}/simulate` protegido por MasterAdmin e `permissions.manage`, somente leitura; devolve por disciplina, total, documentos e diferença de reconciliação. `LegacyProjectsPage.tsx`: botão **Simular disciplinas** para projeto registrado e tabela de resultados.

## Reconciliação SQL de pré-publicação
| Disciplina | Documentos | HH |
|---|---:|---:|
| ELÉTRICA | 105 | 1649,14 |
| INSTRUMENTAÇÃO E AUTOMAÇÃO | 38 | 741,58 |
| CIVIL | 12 | 375,75 |
| TUBULAÇÃO | 20 | 179,75 |
| PROCESSO | 8 | 168,25 |
| SEGURANÇA | 4 | 137,75 |
| INFRAESTRUTURA E HIDRÁULICA | 5 | 22,50 |
| ARQUITETURA | 6 | 15,66 |
| TOTAL | 198 | 3290,38 |

- Ausência de HH sem mapeamento e diferença 0 HH na verificação SQL global.
- Backend build `C:\Dev\_agent_runs\unipar-disciplinas-build-v2.log`: exit=0; frontend TSC exit=0, Vite exit=0.
- Backup `_backups/unipar-disciplinas-binaries_20261026_135123`.
- Deploy com rollback preparado `C:\Dev\_agent_runs\deploy-unipar-disciplines-20261026.ps1`: OK, PID 42592 porta 5277; hash SHA256 `3A4DCB1B5C6C023FD28FC95238DAE1ABCE142FF12AEEB297C8BAAD894F212A6E`.
- HTTP OAuth 200, página Auditoria 200, API protegida simulação 401 sem sessão (bloqueio confirmado), stderr vazio.
- **Homologação autenticada no navegador ainda pendente**: conferir que o cartão do Resumo e a simulação exibem os números esperados e a reconciliacão é zero; não há alegação de teste autenticado.
- Nenhuma tabela criada, nenhuma LDP artificial criada, nenhum Timesheet editado, sem mudanças nos pesos de avanço.
