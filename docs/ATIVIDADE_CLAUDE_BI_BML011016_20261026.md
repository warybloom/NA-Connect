# CLAUDE — BI BML011.016: DATA BACKEND / PRIVACIDADE MASTER ADMIN
## Fonte real
C:\Users\Christian Sanatana\Downloads\BML011.016.pbix
SHA256 448947F79CB8F9C20A45246B8FE222B58097C415D8B450D3E83E1BEEB862E634
Usuário pediu cópia IDÊNTICA do BI com TODOS os dados possíveis do sistema,
nova seção sidebar (feita por Codex), visível SOMENTE Master Admin.
## Inventário original
9 páginas na ordem: Resumo (34 visuais), LDP (18), CAP externo (13),
Critical Analysis (10), Programação (8), Controle de Desvio (13),
Controle de Holds (9), Curva financeira (6), Pagamento (7).
Fonte PBIR: Report/definition/pages/*; DataModel contém medidas;
tema CY24SU10 e assets no PBIX.
Padrões de dados por página identificados:
- Resumo: WBX, AssignmentByDay, dCalendario, KPI (curva S,
  weekly planned/actual/deviation, datas, WBS e status)
- LDP: LDP (4), DISCIPLINE, DOCUMENT STATUS, SITUATION, datas;
- CAP externo: ACTION ITEM LIST, Company3, Responsible, schedule, status
- Critical Analysis: layouts estáticos, verificar interações.
- Programação: WBX hierarquia Level 3..9, início/término, HH trabalho diário
- Controle de Desvio: DESVIO, HH, disciplina, status, data e descrição
- Controle de Holds: HOLDS, status, motivo, responsável, documento
- Curva financeira: Curva financeira, progresso planejado, receita,
  valor agregado por mês
- Pagamento: Pagamentos, BM, competência, NF, prazos/valores.
## MISSÃO BACKEND
1. Inspecionar DataModel do PBIX, relações e expressões DAX no limite das
   ferramentas; mapear tabelas/medidas por visuais. Reutilizar fontes REAIS
   de SQL, LDP, cronograma, timesheet, CAP, desvio, holds e medição quando
   correspondência por documento/escopo/datas estiver comprovada.
2. Criar endpoints isolados /api/bi-bml011016/..., respondendo com dados,
   fontes, datas, formatos e estados explícitos de não mapeado.
   NÃO assumir valores do PBIX como atuais; se dado exigir Excel/MPP externo
   não existente, marcar PENDENTE com motivo, nunca mock, zero ou 100%.
3. Resolver ID real do projeto BML011.016 no cadastro, não usar substring
   que pegue BML011.015 / BML011.016 etc. Tudo fixado a escopo autorizado.
4. **Segurança ABSOLUTA**: a cada request autenticar usuário real via
   IPermissionService.GetCurrentUserAsync e EXIGIR IsMasterAdmin === true
   mesmo quando Enforced=false. Outros usuários, inclusive admins com
   permissions.manage, perfis Gestão/TV ou acesso ao projeto: HTTP 403;
   anônimos HTTP 401. NUNCA confiar em projectId ou claim do frontend.
   Se possível servir PBIR/recursos só pelo endpoint protegido; não criar
   acesso público ao conteúdo confidencial. Nenhuma API existente perde
   segurança nem muda acesso, inclusive /api/bi-chs001.
5. Privilégio mínimo: só consultas, sem alteração de dados. Não alterar
   LDP, Timesheet, número de documento, revisão, custos nem configurações.
6. Criar testes automatizados para 401/403/master e para dados ausentes.
## Trabalho em paralelo e integração
CODEX é dono apenas do frontend/sidebar/rota BI BML011.016.
VOCÊ só toca BACKEND Controller/Services, testes backend e docs de contratos;
não editar telas de Codex, normalizador BYD002 nem Curva S.
Coordenar contrato via docs/CONTRATO_BI_BML011016.md (crie cedo).
Sem build/deploy/reinício em produção antes de testes, backup e integração;
não afetar backend supervisionado da porta 5277.
Relatar indicadores/páginas resolvidos e quais dependem de fontes externas.
Entregar código, testes e relatório de autenticação.
