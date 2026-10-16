# CODEX — AUDITORIA INDEPENDENTE DO BI ORIGINAL CHS001 (sem conflito com Claude)

Leia antes: C:\Dev\N-A-Connect aa\N-A-Connect\_bi_chs001_replica\BI_PAGINAS_REFERENCIA.md

Você é responsável pela AUDITORIA da fidelidade do novo BI a ser reproduzido. CLAUDE É O DONO DO FRONTEND E DA NAVEGAÇÃO. Para não gerar conflitos, NÃO EDITE productionRoutes.ts, ProductionPageContent.tsx, tvScreens.tsx, tema, páginas ou CSS que o Claude esteja editando. Não execute git reset/checkout, não modifique arquivos da pasta F:.

Objetivo: estudar todos os 23 layouts do original em F:\BI N&A CONNECT\CHS001-000-PM0-RT-002-01.pbix e da pasta extraída F:\BI N&A CONNECT\CHS001-000-PfM0-RT-002-01\Report\definition. Gerar documentos em _bi_chs001_replica/AUDITORIA_CODEX_*.md:
- INVENTARIO: páginas em ordem, IDs, dimensões, hidden/visible, grupos, backgrounds, contagem dos visuais e screenshots se houver Power BI Desktop; mapear todos os nomes e títulos.
- MAPA_VISUAIS: para cada visual, tipo, posição X/Y/W/H, camada, estilo, cores, legenda, fonte, campos/filtros, medidas DAX e interação/drill-through; atenção especial a visual customizado e bookmarks.
- MAPA_FONTES: onde vêm os dados do PBIX (Power Query/Power BI), identificação segura e somente leitura de quais endpoints/dados oficiais N&A Connect podem servir para CHS001; não confundir status do fluxo e Document Status; identificar onde divergem. Sem valores inventados, sem modificar SQL.
- PARIDADE: checklist por página para comparar a saída de Claude (quando existir) com original: pixel layout, cards, tabelas, filtros, eixos, valores e logo. Salvar a matriz de falta de fidelidade e screenshot correspondentes se as ferramentas suportarem, mas não tocar nos componentes Claude.

O usuário quer replicação IDENTICA, exceto substituir CHS/Whitefox por logo oficial N&A. Não interpretar ou modernizar o design. Se Power BI Desktop estiver instalado, abra uma CÓPIA do PBIX para capturas, não altere o original; se não estiver, recupere JSON de definição e ressalve que não é uma comparação visual renderizada. Priorize prova visual, não suposição.

PERMISSÕES: iniciar em modo interativo normal com aprovações solicitadas; o próprio usuário ajustará manualmente para 'aprovar sempre'. Não habilite bypass ou full access por conta própria. Pode criar documentação e testes em sua pasta de auditoria; alterações de implementação somente quando o usuário ou Claude delegar uma parte sem conflito.

COMECE AGORA. Inspecione as 23 páginas e produza o primeiro mapa de estrutura, tipos de visual e dados. Peça permissão para ações conforme o seu modo normal.


## ALTERAÇÃO OFICIAL DE PRIORIDADE — 15/10/2026
ANTES de seguir a ordem anterior das 23 páginas, LEIA e APLIQUE integralmente `_bi_chs001_replica/PRIORIDADE_10_TELAS.md`. As dez telas dessa lista são a primeira entrega, com paridade visual e dados verificados. NÃO sacrificar qualidade. As outras 13 ficam para depois.
