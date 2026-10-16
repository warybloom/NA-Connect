# CLAUDE — PRIORIDADE IMEDIATA: TELAS DO BI CHS001 FUNCIONAIS (15/10/2026)

O USUARIO ACABOU DE ABRIR A PAGINA "4/23 RESUMO 60%" E CONFIRMOU:
- TRES LOGOS N&A: quer APENAS a primeira logo na barra lateral, bem no canto esquerdo; retirar as duas repetidas do CABECALHO/MIolo da pagina BI.
- CURVA/PROGRESS CURVE, cumulative/weekly progress, KPIs planejado/concluido/desvio e MATRIZ de atividades estao vazios ("PENDENTE DE PARIDADE").
- Grafico donut STATUS JA FUNCIONA: PRESERVAR.
- Aviso: "Engineering - 30%" (196 tarefas) nao existe exatamente no PBIX/origem; no cronograma atual existe "Emgineering - 30%" (erro ortografico).

OBJETIVO: FAZER AS TELAS JA CRIADAS EXIBIREM INFORMACOES REAIS E GRAFICOS, NAO NOVA AUDITORIA NEM MOCK.

ANTES DE EDITAR:
1. Leia _bi_chs001_replica/CLAUDE_POS_AUDITORIA.md e STATUS_POS_AUDITORIA_CLAUDE.md, AUDITORIA_CODEX_PRIORITARIAS.md, MAPA_MEDIDAS_DAX.md e PRIORIDADE_10_TELAS.md.
2. Inspecione git status e identifique edicoes paralelas. Outro Claude trabalha em ThemeLogo/brandAssets/norma BML011.013. NAO MEXER nesses arquivos. Corrigir logos repetidas APENAS na camada/estrutura BI CHS001; preservar logo esquerda e temas.
3. Priorize o RESUMO 60%, depois RESUMOS 30/90 e todas as outras 7 telas prioritarias, preservando seis LDP e Programacao.

EXECUCAO:
A) Corrigir a duplicacao da logo. Identificar origem de cada imagem (sidebar versus canvas de BI); manter a primeira logo do lado esquerdo e remover exatamente as outras duas do cabecalho, sem esconder com overlays nem quebrar telas/rotas.
B) Diagnosticar por que cards e graficos nao mostram dados no AMBIENTE REAL: inspecionar API /api/bi-chs001/{ldp,programacao,cronograma}, permisos, falhas no frontend, imports, conexao ao banco, build/backend EM EXECUCAO. Testar chamadas autenticadas disponiveis com seguranca; nao supor que compilacao equivale a implantacao. Nao reiniciar servidor sem plano/backup/aval do usuario, pois ha servicos e agentes ativos.
C) Conforme STATUS_POS_AUDITORIA_CLAUDE.md, faltam fontes para medidas: AssignmentByDay (10.186 linhas no cache PBIX de testes, NUNCA como dado vivo), Inicio e Inicio da LB, Trabalho por tarefa, pai da tarefa/Level5-7. Identifique no .mpp oficial ou no fluxo de importacao real de cronograma COMO extrair/persistir os campos corretos de forma atualizavel. Implementar as correcoes de codigo seguras que NAO exigem mutacao de dados de producao; preparar o plano/migration/importador se precisarem de escrita/mudanca de schema e pedir aprovacao antes de aplicar SQL de producao. Nao simular curvas.
D) Manter semantica EXATA do PBIX: filtros dos Resumos 60 e 90 sao por exclusao; NUNCA inventar gate equality. A divergencia "Emgineering - 30%" precisa da decisao explicita do usuario antes de alterar .mpp, dados ou mapear alias — indicar solucao recomendada e impacto. Nao renomear silenciosamente.
E) Cada visual das dez telas: identificar fonte real, endpoint, medida DAX, retorno numérico e comportamento. Exibir todo dado valido que existe; para indicadores sem fonte mostrar pendencia objetiva e quais campos faltam, nao painel vazio por erro de codigo.
F) Verificar responsividade, console, permissao, filtros, datas e graficos; manter sidebar N&A Connect e outros modulos intactos.

TESTES/ENTREGA:
- Rodar testes existentes, TypeScript, Vite build, dotnet build sem atualizar backend em producao inadvertidamente.
- Conferir por endpoint/visuais que funcionaram e tirar screenshots quando possivel.
- Atualizar _bi_chs001_replica/STATUS_CORRECAO_TELAS_20261015.md com causas, arquivos, medidas recuperadas, imagens duplicadas removidas, endpoints, teste real vs teste isolado, pendencias e autorizacoes SQL/deploy.
- Entregar resultados CONCRETOS. Nao declarar sucesso se curvas/matriz continuarem sem fonte real. Nao reiniciar servicos, nao iniciar robo, nao modificar banco LDP/Timesheet/Norma, nao tocar PBIX original, nao sobrescrever trabalho de outros agentes.
COMECE PELA LOGO E PELO RESUMO 60% NA INTERFACE REAL.