# REVISÃO URGENTE — Normalizador da BYD002: DOC4 é EXCEÇÃO PONTUAL

Data: 26/10/2026. Instrução do responsável pelo N&A Connect: NÃO trocar
a posição da disciplina em todo o projeto BYD002.

## Regra aprovada
- Padrão geral continua DOC3, inclusive na BYD002.
- Só os documentos comprovadamente afetados de UMA disciplina/sigla
  específica usam DOC4 (e somente dentro da BYD002).
- A mesma disciplina pode ter documentos normais em DOC3 e exceções DOC4.
- Outras disciplinas da BYD002 e outros projetos não podem mudar.
- Não usar varredura automática DOC2..DOC5 nem fallback DOC4 global.

## Verificação inicial
Consultar os ~80 documentos sem mapeamento com códigos completos e
separá-los por padrão. Identificar a(s) sigla(s) autorizada(s) no DOC4
contra a Norma de Coordenação; documentar quais códigos precisam da exceção
e quais outros motivos de pendência existem. NÃO assumir 80 casos corrigíveis.

## Arquitetura
Em vez de `PosicaoProjeto {principal, alternativa}` aplicável a todas
as disciplinas do projeto, criar exceção contextual versionada:
`Project_ID_ACC + sigla esperada/DisciplinaNorma + critério determinístico
de código/padrão (ou lista de códigos identificados) + posicao = DOC4`.
Se houver ambiguidade entre DOC3 e DOC4, não sobrescrever identificação
validada sem uma exceção EXPLÍCITA que prove o documento afetado.
Prioridade:
1. correspondência exata de número de documento em exceção ativa;
2. regra contextual específica (padrão limitado + sigla oficialmente
   cadastrada na Norma + escopo projeto);
3. DOC3 normal existente;
4. não identificado, com diagnóstico.

Toda nova regra deve trazer preview antes/depois por código e
opção de reverter/desativar, com trilha de auditoria. Evitar nome
de disciplina livre; reutilizar Norma de Coordenação.

## Atenção ao desenvolvimento em andamento
A UI atual (NormalizadorSiglasPage.tsx, PosicaoDisciplinaPanel.tsx)
e a API normalizadorSiglasApi.ts expõem `posicao-disciplina` com
`principal/alternativa` por projeto. Isso NÃO atende a regra revisada
se qualquer disciplina puder cair em DOC4. Adequar contrato,
controller, serviço e migração antes de publicar.
NÃO aplicar configuração DOC4 global à BYD002 em hipótese alguma.
Outros agentes estão alterando Curva S; não tocar nas telas do gráfico.

## Validação / relatório
- total antes/depois de documentos mapeados/desmapeados da BYD002;
- corrigidos especificamente por exceção DOC4 e números de documentos;
- outros documentos da disciplina preservados em DOC3;
- documentos de OUTRAS disciplinas preservados em DOC3;
- outro projeto DOC3 intacto; ambíguos ficam pendentes;
- comparação invariável de código, revisão, HH/custo, gate/status e ACC;
- migração em transação, backup e testes antes do deploy.
Pedir autorização se persistência/migração destrutiva ou alteração
não reversível se mostrar necessária.
