# CODEX — BI BML011.016, reprodução PBIX fiel no frontend
## Fonte inequívoca
C:\Users\Christian Sanatana\Downloads\BML011.016.pbix
SHA256 448947F79CB8F9C20A45246B8FE222B58097C415D8B450D3E83E1BEEB862E634.
É EXATAMENTE o relatório fornecido pelo usuário; ABRIR e inspecionar, não usar prints/referências genéricas.
Projeto BML011.016 (não confundir com BML016 ou BYD); identificar Project_ID_ACC real da aplicação.
## Meta
Adicionar grupo **BI BML011.016** à sidebar, com as NOVE páginas fiéis ao PBIX, na ordem original:
1 Resumo; 2 LDP; 3 CAP externo; 4 Critical Analysis; 5 Programação;
6 Controle de Desvio; 7 Controle de Holds; 8 Curva financeira; 9 Pagamento.
Páginas PBIR com visuais 34,18,13,10,8,13,9,6,7 respectivamente.
O PBIX é ZIP moderno: Report/definition/pages/*/page.json e visuals/*/visual.json;
contém Report/StaticResources e tema CY24SU10. Modelo de dados embutido em DataModel.
Manter geometria, títulos, logos, cores, fontes, interações, navegação,
filtros/slicers e comportamentos do original — não apenas lookalike.
## Reutilização
Pesquisar e reutilizar renderer/parsers já existentes em
ClientApp/src/features/production/biChs001 (pbi, BiChs001Canvas, VisualBi,
generated, tipos etc). Converter a definição PBIR das 9 páginas sem
inventar medidas. Usar assets originais do PBIX, não stock nem placeholders.
Identidade visual correta em telas desktop/responsivas; cada página largura
2048x1123 ou 1500x1000 conforme PBIX, não forçar uma só escala.
Dados: consumir backend novo de Claude via contrato compartilhado;
usar dados reais somente; se não houver fonte comprovada mostrar
"Pendente de mapeamento" no visual correspondente, jamais zeros ou mocks.
## PRIVACIDADE CRÍTICA
Visibilidade somente usuário com /api/permissions/me isMasterAdmin === true.
Não basta can('permissions.manage'), perfil admin comum, acesso a projeto
ou modo permissões.Enforced=false. Em erro de permissões = não renderizar.
Criar rota guard explícito para acesso direto via URL (não só ocultar menu).
Gravar nós só na árvore exclusiva master, FORA do catálogo comum/sidebar
configurável, e sem entrada no catálogo da TV. Nunca usar links públicos
para assets/arquivos sensíveis.
## Limites de edição para trabalho paralelo
VOCE é dono exclusivamente do FRONTEND e seus testes:
ClientApp/src/features/production/biBml011016/**,
ClientApp/src/features/sidebar/officialNavigation.ts,
ClientApp/src/App.tsx e arquivos de testes frontend estritamente necessários.
Não alterar controllers, Services, SQL, normalizador BYD, Curva S geral ou
pastas biChs001 originais salvo extensão estritamente necessária; preferir
novo componente/adapter sem mutar renderers compartilhados.
Claude é dono exclusivamente do BACKEND BI BML011016, segurança de APIs,
fontes reais e testes backend. Sincronizar contrato por
docs/CONTRATO_BI_BML011016.md; se não existir, definir proposta ali.
## Entrega e validação
Documentar inventário página/visual/campo do PBIX e paridade realizada,
explicando claramente fontes/métricas ainda não atendidas.
Executar tsc --noEmit, vite build isolado sem tocar distribuição corrente
antes de homologar, testes de roteamento (master permitido, comum negado,
deslogado negado). Não publicar, reiniciar backend nem sobrescrever DB.
Não pedir ao usuário detalhes já fornecidos. Fazer trabalho real agora,
reportar arquivos modificados, número de páginas, testes, pendências.
