# Relatório frontend — BI BML011.016

PBIX original inspecionado diretamente; SHA-256 confirmado: `448947F79CB8F9C20A45246B8FE222B58097C415D8B450D3E83E1BEEB862E634`.

| # | Página | Canvas | Visuais | Fontes |
|---:|---|---:|---:|---|
| 1 | Resumo | 2048×1123 | 34 | ldp, cronograma, cronograma-diario, programacao-semanal |
| 2 | LDP | 2048×1123 | 18 | ldp |
| 3 | CAP externo | 2048×1123 | 13 | cap-externo |
| 4 | Critical Analysis | 1500×1000 | 10 | estática |
| 5 | Programação | 2048×1123 | 8 | cronograma, cronograma-diario |
| 6 | Controle de Desvio | 2048×1123 | 13 | desvios |
| 7 | Controle de Holds | 2048×1123 | 9 | holds |
| 8 | Curva financeira | 1500×1000 | 6 | curva-financeira (pendente) |
| 9 | Pagamento | 1500×1000 | 7 | pagamentos (pendente) |

## Paridade

- Geometria, ordem, dimensões, textos, estilos, filtros gravados e links `PageNavigation` vêm das nove definições PBIR.
- Renderer/tipos CHS001 são reutilizados sem modificar o módulo original.
- Assets originais são carregados somente pelo endpoint protegido `pbix/recurso`, nunca por `public/`.
- Dados usam somente `/api/bi-bml011016/fontes/{fonte}`; estados sem fonte não viram zeros ou mocks.
- Sidebar/rota exigem `isMasterAdmin === true` e ficam fora dos catálogos configurável e da TV.

## Pendências

- Curva financeira e Pagamento mostram `Pendente de mapeamento` até existirem fontes comprovadas.
- Visuais customizados não suportados pelo renderer compartilhado mostram pendência de paridade.
- Medidas DAX sem correspondência comprovada não são inventadas.
