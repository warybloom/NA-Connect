# CLAUDE — IMPLEMENTAÇÃO DAS 23 TELAS DO BI CHS001 (prioridade visual)

Leia antes: C:\Dev\N-A-Connect aa\N-A-Connect\_bi_chs001_replica\BI_PAGINAS_REFERENCIA.md

AUTORIZAÇÃO: usuário autorizou implementar o novo módulo e pediu Claude sem solicitações repetidas de permissão. Essa autorização NÃO inclui alterações em dados, restart dos serviços, publicar para produção ou exclusões fora do novo módulo.

Objetivo: criar seção EXCLUSIVA dentro de Controle de Produção (sugestão 'BI — CHS001 (Original)'), com as 23 subtelas, nomes e ordem exatamente iguais ao relatório. A fonte é o PBIX fornecido pelo usuário e sua estrutura Power BI Report definition JSON. Fazer reprodução visual FIEL, quase pixel a pixel. NÃO redesenhar, NÃO criar estilo próprio, NÃO simplificar layouts. A única exceção são as logos CHS e Whitefox: substituir por N&A usando os assets oficiais de theme/brandAssets.ts (N&A 30 anos, nunca mudar a logo atual). Preservar todos os outros elementos.

Fonte:
F:\BI N&A CONNECT\CHS001-000-PM0-RT-002-01.pbix
F:\BI N&A CONNECT\CHS001-000-PfM0-RT-002-01\Report\definition\

Plano:
1. Entender pages.json, page.json, cada visual.json, recursos, tema, bookmarks e visibilidades. Capturar páginas via Power BI Desktop se disponível; caso contrário reconstruir layout a partir de coordenadas e assinalar páginas sem referência visual verificada. O PBIX é READ-ONLY.
2. Inspecionar a navegação atual do Controle de Produção, sobretudo productionRoutes.ts e ProductionPageContent.tsx. Registrar um grupo novo com 23 folhas, rotas estáveis /controle-producao/bi-chs001/<slug>. NÃO misturar ou renomear telas existentes, não modificar permissão de outras telas. Usar guard das rotas com permissões existentes; se faltar permissão específica, reportar antes de introduzir uma permissão diferente. Não registrar tela nova na TV automaticamente sem sua real implementação e revisão de autorização.
3. Criar componentes e CSS exclusivamente no namespace novo ClientApp/src/features/production/biChs001/**. Usar proporções, fontes, legendas, eixos, cores, posições, gradientes, bordas, filtros, interações e unidades conforme BI. Nada de inventar gráficos. Identificar precisamente os gráficos e medidas do modelo PBIX; endpoints existentes CHS001 quando disponíveis. Não apresentar dado fictício como real. Se dados ou visual customizado não puderem ser reproduzidos com segurança, sinalizar PENDENTE DE PARIDADE (não considerar a tela concluída).
4. Trabalhar iterativamente pela ordem original: Capa, Resumo Geral, Resumos 30/60/90 etc., até completar as 23. Não substituir identidade N&A global, não editar CSS da Jamile. Não criar novos dados no SQL, somente consultas seguras e read-only. Respeitar Norma de Coordenação própria do projeto e números oficiais. Não alterar tratamentos de HH/custo da aplicação atual.
5. Rodar typecheck, build Vite em pasta externa e testes novos isolados. Comparar capturas do protótipo com PBIX página a página nos tamanhos da referência, com uma matriz de paridade. Concluído apenas com fidelidade visual demonstrável; se não houver screenshot de referência, dizer explicitamente.
6. Documentar o progresso em _bi_chs001_replica/STATUS_CLAUDE.md com páginas concluídas, páginas pendentes, limitações, arquivos alterados, modo de acessar o novo sidebar e testes. Nunca chamar algo de 'idêntico' sem comparação real de tela.

DIVISÃO DO TRABALHO: Codex está fazendo SOMENTE inventário do BI, auditoria e testes. Não edite documentos AUDITORIA_CODEX*. Se precisar de informação, leia os relatórios do Codex. Você é dono das mudanças em ClientApp/src/features/production/biChs001/** e dos arquivos de navegação indispensáveis.

COMECE AGORA. Mostre o inventário das páginas, crie o novo grupo e prossiga com a reprodução visual do BI, sem aguardar novas instruções para tarefas dentro desse escopo. Preserve o funcionamento das telas existentes.


## ALTERAÇÃO OFICIAL DE PRIORIDADE — 15/10/2026
ANTES de seguir a ordem anterior das 23 páginas, LEIA e APLIQUE integralmente `_bi_chs001_replica/PRIORIDADE_10_TELAS.md`. As dez telas dessa lista são a primeira entrega, com paridade visual e dados verificados. NÃO sacrificar qualidade. As outras 13 ficam para depois.
