/*
============================================================
OBJETIVO
============================================================
Criar o schema [coordenacao] — cadastro FUNCIONAL de Norma de Coordenação (Sigla -> Disciplina), usado
para classificar a disciplina do HH/Custo REALIZADO (gestao.TimesheetSnapshotAtual.Doc3), regra oficial
definida em 2026-09-22. Ver AUDITORIA_FORGESYNC_NA (checkpoint "NORMA-DE-COORDENACAO").

============================================================
AUDITORIA PRÉVIA (por que um schema NOVO, nunca reaproveitando o que já existe)
============================================================
- config.Norma / config.NormaMapeamento (schema config, ambas vazias em produção nesta auditoria):
  representam OUTRO conceito — nomenclatura de PLACEHOLDER/pasta destino do
  Services/PlaceholderCreationService.cs (NormaNomenclatura N&A/Cliente). NormaMapeamento tem
  Sigla -> Pasta_Destino/Tipo_Documento/Ignorar, nunca Disciplina. FKs existentes
  (FK_Template_Norma, FK_ProjetoConfiguracao_Norma) amarram essas tabelas a Template/ProjetoConfiguracao
  do motor de placeholder legado — universo diferente do Planejador/PMO. NÃO reutilizado.
- config.ProjetoConfiguracao.NormaId: aponta para config.Norma acima (mesmo conceito legado). Nenhum
  código C#/TS atual referencia "NormaId" (confirmado por busca no repositório) — órfão do app atual.
- ldp.ProjetoConfiguracao (Novo Motor de LDP, Migrations/ConfiguracaoLdp/05): vínculo projeto->
  Template/Mapeamento/Espelho/Controle de ESTRUTURA da LDP — também outro conceito, já documentado como
  "isolado de config.ProjetoConfiguracao" no próprio LdpConfigModels.cs.
- ref.tbMapaDisciplina (Sigla_Doc3 -> Disciplina_PT/EN, 264 linhas): É o candidato mais próximo
  (Sigla->Disciplina), mas é uma tabela legada, ACHATADA (uma única lista global, sem conceito de
  "Norma"/múltiplas normas, sem Origem N&A/Cliente, sem vínculo por projeto) e não referenciada por
  nenhum código atual (confirmado por busca). Não atende ao modelo pedido (múltiplas normas, N&A x
  Cliente, uma por projeto, CRUD+import). Fica disponível como fonte de dados para uma futura
  importação manual (Excel) para uma Norma "N&A CORPORATIVA", mas a tabela em si NÃO é usada aqui.
Conclusão: nenhuma estrutura existente representa "Norma de Coordenação" como modelado nesta tarefa —
schema novo e isolado, por instrução explícita do usuário ("se o conceito existente for diferente, não
misturar silenciosamente").

Nenhuma tabela/coluna existente em [dbo], [config], [gestao], [ldp] ou [ref] é alterada por esta série.

============================================================
DEPENDÊNCIAS
============================================================
Nenhuma. Primeiro script da série (ordem: 00 -> 01).
============================================================
*/
IF NOT EXISTS (SELECT 1 FROM sys.schemas WHERE name = 'coordenacao')
BEGIN
    EXEC('CREATE SCHEMA coordenacao');
    PRINT 'Schema [coordenacao] criado.';
END
ELSE
BEGIN
    PRINT 'Schema [coordenacao] já existe — nada a fazer.';
END
GO
