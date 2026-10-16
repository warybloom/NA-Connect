# Auditoria Codex — dez páginas prioritárias do BI CHS001

> Escopo: auditoria técnica independente. Nenhum arquivo do frontend do Claude, dado SQL ou PBIX foi modificado.

## Fontes e método

- PBIX: `F:\BI N&A CONNECT\CHS001-000-PM0-RT-002-01.pbix`.
- PBIR extraído: `F:\BI N&A CONNECT\CHS001-000-PfM0-RT-002-01\Report\definition`.
- Modelo semântico: `DataModel` do PBIX, aberto em modo somente leitura com PBIXRay 0.15.5.
- Resultado: 10 páginas, 317 visuais, 77 medidas efetivamente referenciadas e 16 relacionamentos do modelo.
- A igualdade de IDs/coordenadas do JSON confirma estrutura; não confirma cálculo nem renderização. As fórmulas deste relatório vêm do modelo semântico, não de inferência.

## Regras transversais comprovadas

- Os Resumos usam `WBX[Level 4]` para separar Engineering 30/60/90. O filtro de 60% é uma exclusão de sete valores e não uma igualdade simples; o de 90% também é uma exclusão.
- Planejado e realizado são ponderados por trabalho/HH. As curvas não são médias simples de percentuais por tarefa.
- Os cards de LDP contam linhas de `LDP (4)` dentro do gate e usam `DOCUMENT STATUS n%` ou `SITUATION n%`; o status geral não substitui o status do gate.
- `CANCELED n` compara exatamente a situação à grafia `Canceled`. Registros com `CANCELLED` em outra coluna não entram automaticamente.
- `Programação Semanal[N&A DOCUMENT NUMBER]` relaciona-se 1:1 e bidirecionalmente com `LDP (4)[N&A DOCUMENT NUMBER]`; ignorar essa propagação muda cards, gráfico e tabela.
- `AssignmentByDay[TaskUID]` filtra `WBX[NodeKey]`, e `AssignmentByDay[TimeByDay]` filtra `dCalendario[Data]`. Esses caminhos sustentam o realizado semanal/acumulado.

## Valores de controle disponíveis no cache CHS001

| Página | Total | Issued | Canceled | Client review | Addressing comments |
|---|---:|---:|---:|---:|---:|
| LDP - 3D - 30% | 4 | 4 | 0 | 0 | 0 |
| LDP - 3D - 60% | 7 | 0 | 1 | 0 | 0 |
| LDP - 3D - 90% | 7 | 0 | 0 | 0 | 0 |
| LDP - Engineering - 30% | 181 | 172 | 0 | 0 | 0 |
| LDP - Engineering - 60% | 357 | 129 | 75 | 129 | 0 |
| LDP - Engineering - 90% | 166 | 0 | 0 | 0 | 0 |

Esses números foram calculados sobre as 442 linhas de `LDP (4)` do cache embutido, aplicando os filtros de página e as medidas DAX originais. Servem para comparar o PBIX; não devem ser tratados como estado atual do banco.

A Programação contém 148 atividades, todas na fase textual `0.6`: NOT STARTED 28, ELABORATION 26, VERIFICATION 49, AUTORIZATION 40 e ISSUANCE FLOW 5.

## Resumo 30%

Página 3, ID `03af0eebc780428b46b2`, 2048×1123; 44 visuais. Fonte: `F:\BI N&A CONNECT\CHS001-000-PfM0-RT-002-01\Report\definition\pages\03af0eebc780428b46b2\page.json`.

Tipos: actionButton (2), card (6), cardVisual (4), donutChart (1), image (4), lineClusteredColumnComboChart (1), pivotTable (1), shape (18), slicer (4), textbox (3).

Filtro: `WBX[Level 4] = "Engineering - 30%"`; `WBX[Pacote]` está no painel sem condição persistida.

### Visuais de dados

| ID | Tipo | Título | Campos/medidas | Evidência |
|---|---|---|---|---|
| `2fe2ff8d9e07d896037a` | card | % Planned | Measure `KPI[KPI 30 / BASE SEM / % Planejado]` | `F:\BI N&A CONNECT\CHS001-000-PfM0-RT-002-01\Report\definition\pages\03af0eebc780428b46b2\visuals\2fe2ff8d9e07d896037a\visual.json` |
| `322c94e19d367caa19e2` | card | % Planned | Measure `KPI[KPI 30 / ACC / % Planejado]` | `F:\BI N&A CONNECT\CHS001-000-PfM0-RT-002-01\Report\definition\pages\03af0eebc780428b46b2\visuals\322c94e19d367caa19e2\visual.json` |
| `3cf5867d08c056446e95` | slicer | Date | Column `d[Data]`; Column `dSemana[Data]` | `F:\BI N&A CONNECT\CHS001-000-PfM0-RT-002-01\Report\definition\pages\03af0eebc780428b46b2\visuals\3cf5867d08c056446e95\visual.json` |
| `48adb2e2db6a126e6823` | pivotTable | — | Aggregation `Assignment[Porcentagem de Trabalho Concluído]`; Aggregation `WBX[EDT]`; Aggregation `WBX[Status1]`; Column `Assignment[Porcentagem de Trabalho Concluído]`; Column `WBX[EDT]`; Column `WBX[Level 3]`; Column `WBX[Level 4]`; Column `WBX[Level 5]`; Column `WBX[Level 6]`; Column `WBX[Level 7]`; Column `WBX[Status1]`; Column `w[Level 4]`; Measure `KPI[KPI 30 / PRAZO / Início Baseline]`; Measure `KPI[KPI 30 / PRAZO / Início Reprogramado]`; Measure `KPI[KPI 30 / PRAZO / Término Baseline]`; Measure `KPI[KPI 30 / PRAZO / Término Reprogramado]` | `F:\BI N&A CONNECT\CHS001-000-PfM0-RT-002-01\Report\definition\pages\03af0eebc780428b46b2\visuals\48adb2e2db6a126e6823\visual.json` |
| `4a4c60d08a5fc643307f` | donutChart | — | Aggregation `WBX[TaskID]`; Column `WBX[Status1]`; Column `WBX[TaskID]` | `F:\BI N&A CONNECT\CHS001-000-PfM0-RT-002-01\Report\definition\pages\03af0eebc780428b46b2\visuals\4a4c60d08a5fc643307f\visual.json` |
| `6d9c8b4630c4c1f0282e` | cardVisual | — | Measure `KPI[KPI 30 / PRAZO / Término Baseline]`; Measure `KPI[KPI 30 / PRAZO / Término Reprogramado]` | `F:\BI N&A CONNECT\CHS001-000-PfM0-RT-002-01\Report\definition\pages\03af0eebc780428b46b2\visuals\6d9c8b4630c4c1f0282e\visual.json` |
| `8c2e6dda007e8c3354c5` | card | % Devation | Measure `KPI[KPI 30 / BASE SEM / % Desvio]` | `F:\BI N&A CONNECT\CHS001-000-PfM0-RT-002-01\Report\definition\pages\03af0eebc780428b46b2\visuals\8c2e6dda007e8c3354c5\visual.json` |
| `8d19da71dfee6625ce73` | cardVisual | — | Measure `KPI[KPI 30 / PRAZO / Desvio]` | `F:\BI N&A CONNECT\CHS001-000-PfM0-RT-002-01\Report\definition\pages\03af0eebc780428b46b2\visuals\8d19da71dfee6625ce73\visual.json` |
| `93a3321f9cf13dcd5865` | slicer | — | Column `WBX[Pacote]` | `F:\BI N&A CONNECT\CHS001-000-PfM0-RT-002-01\Report\definition\pages\03af0eebc780428b46b2\visuals\93a3321f9cf13dcd5865\visual.json` |
| `959cb0df0729d907e717` | card | % Complete | Measure `KPI[KPI 30 / ACC / % Realizado]` | `F:\BI N&A CONNECT\CHS001-000-PfM0-RT-002-01\Report\definition\pages\03af0eebc780428b46b2\visuals\959cb0df0729d907e717\visual.json` |
| `9f5e7fa97182efd05fb0` | cardVisual | — | Measure `KPI[Activity Title]` | `F:\BI N&A CONNECT\CHS001-000-PfM0-RT-002-01\Report\definition\pages\03af0eebc780428b46b2\visuals\9f5e7fa97182efd05fb0\visual.json` |
| `ab5500bfee9556530ff4` | slicer | — | Column `WBX[Responsabilidade]` | `F:\BI N&A CONNECT\CHS001-000-PfM0-RT-002-01\Report\definition\pages\03af0eebc780428b46b2\visuals\ab5500bfee9556530ff4\visual.json` |
| `bb4e196646286dc3c5a8` | cardVisual | — | Measure `KPI[KPI 30 / PRAZO / Dias Restantes]`; Measure `KPI[KPI 30 / PRAZO / Duração Baseline]` | `F:\BI N&A CONNECT\CHS001-000-PfM0-RT-002-01\Report\definition\pages\03af0eebc780428b46b2\visuals\bb4e196646286dc3c5a8\visual.json` |
| `c4668e5b90042f7c1182` | card | % Complete | Measure `KPI[KPI 30 / BASE SEM / % Realizado]` | `F:\BI N&A CONNECT\CHS001-000-PfM0-RT-002-01\Report\definition\pages\03af0eebc780428b46b2\visuals\c4668e5b90042f7c1182\visual.json` |
| `ea13570fc26d9bbfeb65` | lineClusteredColumnComboChart | — | Column `EAP[Status in English Column]`; Column `EAP[Status]`; Column `dCalendario Fonte 2[Data Corte]`; Column `dCalendario[Data Corte]`; Measure `KPI[KPI 30 / COLUNA / % Baseline]`; Measure `KPI[KPI 30 / COLUNA / % Realizado]`; Measure `KPI[KPI 30 / CURVA / % Baseline]`; Measure `KPI[KPI 30 / CURVA / % Realizado]`; Measure `KPI[KPI 30 / CURVA / % Reprogramado]`; Measure `KPI[KPI 30 / CURVA / Mostrar Período]`; Measure `KPI[KPI / COLUNA / % Baseline]`; Measure `KPI[KPI / COLUNA / % Realizado]`; Measure `KPI[KPI / CURVA / % Baseline]`; Measure `KPI[KPI / CURVA / % Realizado]`; Measure `KPI[KPI / CURVA / % Reprogramado]`; Measure `k[KPI 30 / CURVA / Mostrar Período]` | `F:\BI N&A CONNECT\CHS001-000-PfM0-RT-002-01\Report\definition\pages\03af0eebc780428b46b2\visuals\ea13570fc26d9bbfeb65\visual.json` |
| `f69054d36cdb5d11da9b` | slicer | — | Column `WBX[Disciplina]` | `F:\BI N&A CONNECT\CHS001-000-PfM0-RT-002-01\Report\definition\pages\03af0eebc780428b46b2\visuals\f69054d36cdb5d11da9b\visual.json` |
| `f7a2b922e8e7ec35227f` | card | % Devation | Measure `KPI[KPI 30 / ACC / % Desvio]` | `F:\BI N&A CONNECT\CHS001-000-PfM0-RT-002-01\Report\definition\pages\03af0eebc780428b46b2\visuals\f7a2b922e8e7ec35227f\visual.json` |

### Inventário completo (ID, tipo e coordenadas)

| ID | Tipo | X | Y | L | A | Z |
|---|---|---:|---:|---:|---:|---:|
| `22c4d958c2e4a4dcde75` | shape | 225.71428571428572 | 222.85714285714286 | 1800.0000000000002 | 147.14285714285714 | 0 |
| `23d9e0e9f0c97644d391` | shape | 752.3546798029556 | 387.2413793103448 | 1273.7475369458127 | 338.8362068965517 | 1000 |
| `48adb2e2db6a126e6823` | pivotTable | 231.42857142857144 | 731.4285714285714 | 1791.4285714285716 | 390 | 2000 |
| `1ce280eb63601a3d7ab0` | image | 30.000000000000004 | 0 | 145.71428571428572 | 130 | 3000 |
| `321f86851df1ee33e3fb` | shape | 710.8645320197044 | 5.532019704433497 | 830 | 52.85714285714286 | 4000 |
| `9f5e7fa97182efd05fb0` | cardVisual | 720.545566502463 | 89.89532019704433 | 905.8682266009852 | 55.320197044334975 | 5000 |
| `f69054d36cdb5d11da9b` | slicer | 227.14285714285717 | 145.71428571428572 | 595.7142857142858 | 70 | 6000 |
| `ab5500bfee9556530ff4` | slicer | 840 | 145.71428571428572 | 582.8571428571429 | 70 | 7000 |
| `93a3321f9cf13dcd5865` | slicer | 1438.5714285714287 | 147.14285714285714 | 584.2857142857143 | 70 | 8000 |
| `bb4e196646286dc3c5a8` | cardVisual | 1870 | 230 | 141.66666666666669 | 135 | 9000 |
| `8d19da71dfee6625ce73` | cardVisual | 1719.0751231527092 | 229.57881773399012 | 136.91748768472905 | 130.0024630541872 | 10000 |
| `6d9c8b4630c4c1f0282e` | cardVisual | 1542.0504926108374 | 226.81280788177338 | 156.2795566502463 | 141.06650246305418 | 11000 |
| `f2c233a224ec4c317820` | textbox | 1122.857142857143 | 225.71428571428572 | 338.5714285714286 | 48.57142857142858 | 12000 |
| `2fe2ff8d9e07d896037a` | card | 1062.5 | 293.25 | 155 | 67.5 | 13000 |
| `c4668e5b90042f7c1182` | card | 1207.5 | 293.5 | 155 | 67.5 | 14000 |
| `8c2e6dda007e8c3354c5` | card | 1352.5 | 293.75 | 155 | 67 | 15000 |
| `015bde2e0c0e40d1531e` | shape | 1506.6666666666667 | 223.33333333333334 | 40 | 146.66666666666669 | 16000 |
| `92d68d790567b869eed8` | shape | 1016.6666666666666 | 223.33333333333331 | 40 | 146.66666666666666 | 17000 |
| `322c94e19d367caa19e2` | card | 542.8571428571429 | 291.904761904762 | 154.2857142857143 | 67.14285714285715 | 18000 |
| `959cb0df0729d907e717` | card | 694.4285714285716 | 291.904761904762 | 154 | 67.14285714285715 | 19000 |
| `f7a2b922e8e7ec35227f` | card | 845.7142857142858 | 291.904761904762 | 154 | 67.14285714285715 | 20000 |
| `1ea189c9cd0d848fff2f` | textbox | 617.1428571428572 | 225.71428571428572 | 344.28571428571433 | 47.142857142857146 | 21000 |
| `94bf008e7adcd40db91c` | image | 271.06896551724134 | 298.72906403940885 | 81.59729064039408 | 60.85221674876847 | 22000 |
| `d3127c71b9377b27d930` | shape | 225.429802955665 | 387.2413793103448 | 510.3288177339901 | 342.9852216748768 | 23000 |
| `4a4c60d08a5fc643307f` | donutChart | 241.42857142857144 | 420 | 478.5714285714286 | 290 | 24000 |
| `bbe083bbde874f965250` | shape | 5 | 231.87841945288753 | 191.4164133738602 | 81 | 25000 |
| `77dd2d9cdb3bc58530a1` | textbox | 789.6958128078817 | 455.0086206896551 | 1175.5541871921182 | 658.3103448275862 | 26000 |
| `3cf5867d08c056446e95` | slicer | 241.42857142857144 | 230.00000000000003 | 214.2857142857143 | 74.28571428571429 | 27000 |
| `3a0899f49d2e0d52d8ed` | image | 26.27709359605911 | 139.6834975369458 | 149.36453201970443 | 76.06527093596058 | 29000 |
| `98c3212d0ea1e150712f` | image | 212.98275862068965 | 5.532019704433497 | 156.2795566502463 | 84.36330049261083 | 30000 |
| `5b432d74108f549e019d` | shape | 5 | 314.3586626139818 | 192.9726443768997 | 81 | 31000 |
| `e28c8e6eea4195f0ecca` | shape | 5 | 395.90516717325227 | 191.4164133738602 | 81 | 32000 |
| `c11e30302f41040447a1` | shape | 5 | 477.45167173252275 | 189.86018237082067 | 81 | 33000 |
| `10fb6da7df63ea6b2cc7` | shape | 5 | 558.9981762917932 | 189.86018237082067 | 81 | 34000 |
| `ae9f470cdba748e4efcd` | actionButton | 1940.3636363636363 | 21.818181818181817 | 59.63636363636363 | 49.45454545454545 | 35000 |
| `e84d2f8956589ecb6ff1` | shape | 5 | 881.9012158054711 | 191.4164133738602 | 81 | 36000 |
| `36ce9bfd09df2b41b063` | shape | 5 | 962.3434650455927 | 191.4164133738602 | 81 | 37000 |
| `d6e38f0df43d26c1e5c6` | shape | 5 | 801.4589665653496 | 192.9726443768997 | 81 | 38000 |
| `975cb12241bf151ba51a` | shape | 5 | 640.5446808510637 | 189.86018237082067 | 81 | 39000 |
| `4eb6b82d3afb24435a4a` | shape | 5 | 722.0911854103342 | 189.86018237082067 | 81 | 40000 |
| `d5423b7cd105cb263835` | actionButton | 1855.9926108374384 | 15.213054187192117 | 62.23522167487684 | 66.38423645320196 | 41000 |
| `ea13570fc26d9bbfeb65` | lineClusteredColumnComboChart | 765.9671889257771 | 419.88234608888115 | 1243.4285714285713 | 292.57142857142856 | 42000 |
| `3fee0bc065b90c933ba2` | shape | 5.097583225066401 | 1042 | 190.85467980295564 | 81 | 43000 |
| `eec84f26c46c6736504a` | shape | 743.8784194528876 | 57.58054711246201 | 755 | 32 | 43001 |

## Resumo 60%

Página 4, ID `dbdd8baab8e8eb03281f`, 2048×1123; 44 visuais. Fonte: `F:\BI N&A CONNECT\CHS001-000-PfM0-RT-002-01\Report\definition\pages\dbdd8baab8e8eb03281f\page.json`.

Tipos: actionButton (2), card (6), cardVisual (4), donutChart (1), image (4), lineClusteredColumnComboChart (1), pivotTable (1), shape (18), slicer (4), textbox (3).

Filtro: `WBX[Level 4] NOT IN {Engineering - 30%, Engineering - 90%, Field Activies, Milestone, WFX Deliverables, Engineering - 15%, CHS Deliverables}`.

### Visuais de dados

| ID | Tipo | Título | Campos/medidas | Evidência |
|---|---|---|---|---|
| `1cb81bc001906b9e973d` | cardVisual | — | Measure `KPI[KPI 60 / PRAZO / Término Baseline]`; Measure `KPI[KPI 60 / PRAZO / Término Reprogramado]` | `F:\BI N&A CONNECT\CHS001-000-PfM0-RT-002-01\Report\definition\pages\dbdd8baab8e8eb03281f\visuals\1cb81bc001906b9e973d\visual.json` |
| `288c0b007a0ead1549b2` | card | % Complete | Measure `KPI[KPI 60 / ACC / % Realizado]` | `F:\BI N&A CONNECT\CHS001-000-PfM0-RT-002-01\Report\definition\pages\dbdd8baab8e8eb03281f\visuals\288c0b007a0ead1549b2\visual.json` |
| `2dbe2e245f07d1e69f81` | lineClusteredColumnComboChart | — | Column `EAP[Status in English Column]`; Column `EAP[Status]`; Column `dCalendario Fonte 2[Data Corte]`; Column `dCalendario[Data Corte]`; Measure `KPI[KPI 30 / COLUNA / % Baseline]`; Measure `KPI[KPI 30 / COLUNA / % Realizado]`; Measure `KPI[KPI 30 / CURVA / % Baseline]`; Measure `KPI[KPI 30 / CURVA / % Realizado]`; Measure `KPI[KPI 30 / CURVA / % Reprogramado]`; Measure `KPI[KPI 60 / COLUNA / % Baseline]`; Measure `KPI[KPI 60 / COLUNA / % Realizado]`; Measure `KPI[KPI 60 / CURVA / % Baseline]`; Measure `KPI[KPI 60 / CURVA / % Realizado]`; Measure `KPI[KPI 60 / CURVA / % Reprogramado]`; Measure `KPI[KPI 60 / CURVA / Mostrar Período]`; Measure `KPI[KPI / COLUNA / % Baseline]`; Measure `KPI[KPI / COLUNA / % Realizado]`; Measure `KPI[KPI / CURVA / % Baseline]`; Measure `KPI[KPI / CURVA / % Realizado]`; Measure `KPI[KPI / CURVA / % Reprogramado]`; Measure `k[KPI 60 / CURVA / Mostrar Período]` | `F:\BI N&A CONNECT\CHS001-000-PfM0-RT-002-01\Report\definition\pages\dbdd8baab8e8eb03281f\visuals\2dbe2e245f07d1e69f81\visual.json` |
| `35161a05fd773d71a4aa` | slicer | Date | Column `d[Data]`; Column `dSemana[Data]` | `F:\BI N&A CONNECT\CHS001-000-PfM0-RT-002-01\Report\definition\pages\dbdd8baab8e8eb03281f\visuals\35161a05fd773d71a4aa\visual.json` |
| `57da2e3673d098d27917` | slicer | — | Column `WBX[Pacote]` | `F:\BI N&A CONNECT\CHS001-000-PfM0-RT-002-01\Report\definition\pages\dbdd8baab8e8eb03281f\visuals\57da2e3673d098d27917\visual.json` |
| `68d2224dc725813a357f` | card | % Planned | Measure `KPI[KPI 60 / ACC / % Planejado]` | `F:\BI N&A CONNECT\CHS001-000-PfM0-RT-002-01\Report\definition\pages\dbdd8baab8e8eb03281f\visuals\68d2224dc725813a357f\visual.json` |
| `794318312f717596a4ea` | slicer | — | Column `WBX[Responsabilidade]` | `F:\BI N&A CONNECT\CHS001-000-PfM0-RT-002-01\Report\definition\pages\dbdd8baab8e8eb03281f\visuals\794318312f717596a4ea\visual.json` |
| `81e54c4b6acead0c8c1e` | card | % Complete | Measure `KPI[KPI 60 / BASE SEM / % Realizado]` | `F:\BI N&A CONNECT\CHS001-000-PfM0-RT-002-01\Report\definition\pages\dbdd8baab8e8eb03281f\visuals\81e54c4b6acead0c8c1e\visual.json` |
| `8e01a472a4d77433153d` | pivotTable | — | Aggregation `Assignment[Porcentagem de Trabalho Concluído]`; Aggregation `WBX[EDT]`; Aggregation `WBX[Status1]`; Column `Assignment[Porcentagem de Trabalho Concluído]`; Column `WBX[EDT]`; Column `WBX[Level 3]`; Column `WBX[Level 4]`; Column `WBX[Level 5]`; Column `WBX[Level 6]`; Column `WBX[Level 7]`; Column `WBX[Status1]`; Measure `KPI[KPI 60 / PRAZO / Início Baseline]`; Measure `KPI[KPI 60 / PRAZO / Início Reprogramado]`; Measure `KPI[KPI 60 / PRAZO / Término Baseline]`; Measure `KPI[KPI 60 / PRAZO / Término Reprogramado]` | `F:\BI N&A CONNECT\CHS001-000-PfM0-RT-002-01\Report\definition\pages\dbdd8baab8e8eb03281f\visuals\8e01a472a4d77433153d\visual.json` |
| `9d839ffe6b6dd3e63f64` | slicer | — | Column `WBX[Disciplina]` | `F:\BI N&A CONNECT\CHS001-000-PfM0-RT-002-01\Report\definition\pages\dbdd8baab8e8eb03281f\visuals\9d839ffe6b6dd3e63f64\visual.json` |
| `acd6bef2d1efb2e24ac4` | cardVisual | — | Measure `KPI[KPI 60 / PRAZO / Desvio]` | `F:\BI N&A CONNECT\CHS001-000-PfM0-RT-002-01\Report\definition\pages\dbdd8baab8e8eb03281f\visuals\acd6bef2d1efb2e24ac4\visual.json` |
| `ae8c65ffaa04a588c5a4` | cardVisual | — | Measure `KPI[Activity Title]` | `F:\BI N&A CONNECT\CHS001-000-PfM0-RT-002-01\Report\definition\pages\dbdd8baab8e8eb03281f\visuals\ae8c65ffaa04a588c5a4\visual.json` |
| `b2955cbafe0fb06e19f3` | donutChart | — | Aggregation `WBX[TaskID]`; Column `WBX[Status1]`; Column `WBX[TaskID]` | `F:\BI N&A CONNECT\CHS001-000-PfM0-RT-002-01\Report\definition\pages\dbdd8baab8e8eb03281f\visuals\b2955cbafe0fb06e19f3\visual.json` |
| `c67b041d0188f0b43075` | card | % Devation | Measure `KPI[KPI 60 / BASE SEM / % Desvio]` | `F:\BI N&A CONNECT\CHS001-000-PfM0-RT-002-01\Report\definition\pages\dbdd8baab8e8eb03281f\visuals\c67b041d0188f0b43075\visual.json` |
| `c859d8dfdb220c4f7c8e` | cardVisual | — | Measure `KPI[KPI 60 / PRAZO / Dias Restantes]`; Measure `KPI[KPI 60 / PRAZO / Duração Baseline]` | `F:\BI N&A CONNECT\CHS001-000-PfM0-RT-002-01\Report\definition\pages\dbdd8baab8e8eb03281f\visuals\c859d8dfdb220c4f7c8e\visual.json` |
| `c88d9bd5241e0b16e414` | card | % Devation | Measure `KPI[KPI 60 / ACC / % Desvio]` | `F:\BI N&A CONNECT\CHS001-000-PfM0-RT-002-01\Report\definition\pages\dbdd8baab8e8eb03281f\visuals\c88d9bd5241e0b16e414\visual.json` |
| `df5ca39b703d47ce86e5` | card | % Planned | Measure `KPI[KPI 60 / BASE SEM / % Planejado]` | `F:\BI N&A CONNECT\CHS001-000-PfM0-RT-002-01\Report\definition\pages\dbdd8baab8e8eb03281f\visuals\df5ca39b703d47ce86e5\visual.json` |

### Inventário completo (ID, tipo e coordenadas)

| ID | Tipo | X | Y | L | A | Z |
|---|---|---:|---:|---:|---:|---:|
| `5f7228ea0e00a0e136bc` | shape | 225.71428571428572 | 222.85714285714286 | 1800.0000000000002 | 147.14285714285714 | 0 |
| `8bd962ac0fcb16933448` | shape | 752.3546798029556 | 387.2413793103448 | 1273.7475369458127 | 338.8362068965517 | 1000 |
| `8e01a472a4d77433153d` | pivotTable | 231.42857142857144 | 731.4285714285714 | 1791.4285714285716 | 390 | 2000 |
| `76821bb3af7daace8e9e` | image | 30.000000000000004 | 0 | 145.71428571428572 | 130 | 3000 |
| `d4c1a1acdf75390e33b0` | shape | 710.8645320197044 | 5.532019704433497 | 830 | 52.85714285714286 | 4000 |
| `ae8c65ffaa04a588c5a4` | cardVisual | 720.545566502463 | 89.89532019704433 | 905.8682266009852 | 55.320197044334975 | 5000 |
| `9d839ffe6b6dd3e63f64` | slicer | 227.14285714285717 | 145.71428571428572 | 595.7142857142858 | 70 | 6000 |
| `794318312f717596a4ea` | slicer | 840 | 145.71428571428572 | 582.8571428571429 | 70 | 7000 |
| `57da2e3673d098d27917` | slicer | 1438.5714285714287 | 147.14285714285714 | 584.2857142857143 | 70 | 8000 |
| `c859d8dfdb220c4f7c8e` | cardVisual | 1870 | 230 | 141.66666666666669 | 135 | 9000 |
| `acd6bef2d1efb2e24ac4` | cardVisual | 1719.0751231527092 | 229.57881773399012 | 136.91748768472905 | 130.0024630541872 | 10000 |
| `1cb81bc001906b9e973d` | cardVisual | 1542.0504926108374 | 226.81280788177338 | 156.2795566502463 | 141.06650246305418 | 11000 |
| `92212fd51e8f8d86310a` | textbox | 1122.857142857143 | 225.71428571428572 | 338.5714285714286 | 48.57142857142858 | 12000 |
| `df5ca39b703d47ce86e5` | card | 1062.5 | 293.25 | 155 | 67.5 | 13000 |
| `81e54c4b6acead0c8c1e` | card | 1207.5 | 293.5 | 155 | 67.5 | 14000 |
| `c67b041d0188f0b43075` | card | 1352.5 | 293.75 | 155 | 67 | 15000 |
| `ad79a06cc404bb09473b` | shape | 1506.6666666666667 | 223.33333333333334 | 40 | 146.66666666666669 | 16000 |
| `e60d0cbc638c59aedbc3` | shape | 1016.6666666666666 | 223.33333333333331 | 40 | 146.66666666666666 | 17000 |
| `68d2224dc725813a357f` | card | 542.8571428571429 | 291.904761904762 | 154.2857142857143 | 67.14285714285715 | 18000 |
| `288c0b007a0ead1549b2` | card | 694.4285714285716 | 291.904761904762 | 154 | 67.14285714285715 | 19000 |
| `c88d9bd5241e0b16e414` | card | 845.7142857142858 | 291.904761904762 | 154 | 67.14285714285715 | 20000 |
| `53afc5d47cfc888afffc` | textbox | 617.1428571428572 | 225.71428571428572 | 344.28571428571433 | 47.142857142857146 | 21000 |
| `5b463014fbebc0e53cb5` | image | 271.06896551724134 | 298.72906403940885 | 81.59729064039408 | 60.85221674876847 | 22000 |
| `093b648573b5253acfcd` | shape | 225.429802955665 | 387.2413793103448 | 510.3288177339901 | 342.9852216748768 | 23000 |
| `b2955cbafe0fb06e19f3` | donutChart | 241.42857142857144 | 420 | 478.5714285714286 | 290 | 24000 |
| `feec452b02b5329e32d5` | shape | 5 | 231.87841945288753 | 191.4164133738602 | 81 | 25000 |
| `5ab1f712c481f8b4b586` | textbox | 789.6958128078817 | 455.0086206896551 | 1175.5541871921182 | 658.3103448275862 | 26000 |
| `35161a05fd773d71a4aa` | slicer | 241.42857142857144 | 230.00000000000003 | 214.2857142857143 | 74.28571428571429 | 27000 |
| `fc4b056ff090feaa9b88` | image | 26.27709359605911 | 139.6834975369458 | 149.36453201970443 | 76.06527093596058 | 29000 |
| `4339d680707ec808caaf` | image | 212.98275862068965 | 5.532019704433497 | 156.2795566502463 | 84.36330049261083 | 30000 |
| `3251c2f7fe4463df8084` | shape | 5.622492401215794 | 313.06180344478213 | 192.9726443768997 | 81 | 31000 |
| `f823d9eb3f1d8b085fd2` | shape | 4.377507598784177 | 392.7497467071935 | 191.4164133738602 | 81 | 32000 |
| `fd7faf7973c7b333ad14` | shape | 4.688753799392089 | 472.3617021276595 | 189.86018237082067 | 81 | 33000 |
| `e49b02bdcf3c0bdd95b1` | shape | 5 | 551.9736575481255 | 189.86018237082067 | 81 | 34000 |
| `794f393b2ccb1d7e001b` | actionButton | 1940.3636363636363 | 21.818181818181817 | 59.63636363636363 | 49.45454545454545 | 35000 |
| `b0d84a4a48beaf9e1205` | shape | 6.5562310030395 | 881.8642350557244 | 191.4164133738602 | 81 | 36000 |
| `bad9e41525a9cb6c091a` | shape | 6.5562310030395 | 962.2695035460993 | 191.4164133738602 | 81 | 37000 |
| `04601904263bd2a21f81` | shape | 5 | 801.4589665653496 | 192.9726443768997 | 81 | 38000 |
| `8c19fa1086ba8b3fa0de` | shape | 5 | 631.5856129685916 | 189.86018237082067 | 81 | 39000 |
| `3109819a30cba42156df` | shape | 5 | 709.6413373860181 | 189.86018237082067 | 81 | 40000 |
| `591af280302b0a4940e0` | actionButton | 1855.9926108374384 | 15.213054187192117 | 62.23522167487684 | 66.38423645320196 | 41000 |
| `2dbe2e245f07d1e69f81` | lineClusteredColumnComboChart | 769.0809585610616 | 419.2808964117937 | 1243.4285714285713 | 292.57142857142856 | 42000 |
| `4e9c2b699e823d543ba8` | shape | 5.097583225066401 | 1042 | 190.85467980295564 | 81 | 43000 |
| `a4eb48ecf81c9b5da21b` | shape | 743.8784194528876 | 57.58054711246201 | 755 | 32 | 43001 |

## Resumo 90%

Página 5, ID `7340affd805e6aa3300a`, 2048×1123; 44 visuais. Fonte: `F:\BI N&A CONNECT\CHS001-000-PfM0-RT-002-01\Report\definition\pages\7340affd805e6aa3300a\page.json`.

Tipos: actionButton (2), card (6), cardVisual (4), donutChart (1), image (4), lineClusteredColumnComboChart (1), pivotTable (1), shape (18), slicer (4), textbox (3).

Filtro: `WBX[Level 4] NOT IN {Engineering - 30%, Engineering - 60%, Field Activies, Milestone, WFX Deliverables, Engineering - 15%, CHS Deliverables}`.

### Visuais de dados

| ID | Tipo | Título | Campos/medidas | Evidência |
|---|---|---|---|---|
| `05c37b733f3141ff0651` | lineClusteredColumnComboChart | — | Column `EAP[Status in English Column]`; Column `EAP[Status]`; Column `dCalendario Fonte 2[Data Corte]`; Column `dCalendario[Data Corte]`; Measure `KPI[KPI 30 / COLUNA / % Baseline]`; Measure `KPI[KPI 30 / COLUNA / % Realizado]`; Measure `KPI[KPI 30 / CURVA / % Baseline]`; Measure `KPI[KPI 30 / CURVA / % Realizado]`; Measure `KPI[KPI 30 / CURVA / % Reprogramado]`; Measure `KPI[KPI 60 / COLUNA / % Baseline]`; Measure `KPI[KPI 60 / COLUNA / % Realizado]`; Measure `KPI[KPI 60 / CURVA / % Baseline]`; Measure `KPI[KPI 60 / CURVA / % Realizado]`; Measure `KPI[KPI 60 / CURVA / % Reprogramado]`; Measure `KPI[KPI 90 / COLUNA / % Baseline]`; Measure `KPI[KPI 90 / COLUNA / % Realizado]`; Measure `KPI[KPI 90 / CURVA / % Baseline]`; Measure `KPI[KPI 90 / CURVA / % Realizado]`; Measure `KPI[KPI 90 / CURVA / % Reprogramado]`; Measure `KPI[KPI 90 / CURVA / Mostrar Período]`; Measure `KPI[KPI / COLUNA / % Baseline]`; Measure `KPI[KPI / COLUNA / % Realizado]`; Measure `KPI[KPI / CURVA / % Baseline]`; Measure `KPI[KPI / CURVA / % Realizado]`; Measure `KPI[KPI / CURVA / % Reprogramado]`; Measure `k[KPI 90 / CURVA / Mostrar Período]` | `F:\BI N&A CONNECT\CHS001-000-PfM0-RT-002-01\Report\definition\pages\7340affd805e6aa3300a\visuals\05c37b733f3141ff0651\visual.json` |
| `07da38527d3bbc91c181` | slicer | — | Column `WBX[Responsabilidade]` | `F:\BI N&A CONNECT\CHS001-000-PfM0-RT-002-01\Report\definition\pages\7340affd805e6aa3300a\visuals\07da38527d3bbc91c181\visual.json` |
| `2a90aabcc1970de62ecb` | slicer | Date | Column `d[Data]`; Column `dSemana[Data]` | `F:\BI N&A CONNECT\CHS001-000-PfM0-RT-002-01\Report\definition\pages\7340affd805e6aa3300a\visuals\2a90aabcc1970de62ecb\visual.json` |
| `485db311e7e38517d3a1` | pivotTable | — | Aggregation `Assignment[Porcentagem de Trabalho Concluído]`; Aggregation `WBX[EDT]`; Aggregation `WBX[Status1]`; Column `Assignment[Porcentagem de Trabalho Concluído]`; Column `WBX[EDT]`; Column `WBX[Level 3]`; Column `WBX[Level 4]`; Column `WBX[Level 5]`; Column `WBX[Level 6]`; Column `WBX[Level 7]`; Column `WBX[Status1]`; Measure `KPI[KPI 90 / PRAZO / Início Baseline]`; Measure `KPI[KPI 90 / PRAZO / Início Reprogramado]`; Measure `KPI[KPI 90 / PRAZO / Término Baseline]`; Measure `KPI[KPI 90 / PRAZO / Término Reprogramado]` | `F:\BI N&A CONNECT\CHS001-000-PfM0-RT-002-01\Report\definition\pages\7340affd805e6aa3300a\visuals\485db311e7e38517d3a1\visual.json` |
| `4d42161abc0acb822bbb` | card | % Planned | Measure `KPI[KPI 90 / BASE SEM / % Planejado]` | `F:\BI N&A CONNECT\CHS001-000-PfM0-RT-002-01\Report\definition\pages\7340affd805e6aa3300a\visuals\4d42161abc0acb822bbb\visual.json` |
| `703423ed3cf746656234` | card | % Planned | Measure `KPI[KPI 90 / ACC / % Planejado]` | `F:\BI N&A CONNECT\CHS001-000-PfM0-RT-002-01\Report\definition\pages\7340affd805e6aa3300a\visuals\703423ed3cf746656234\visual.json` |
| `819db0fa8c16f76fba3a` | cardVisual | — | Measure `KPI[KPI 90 / PRAZO / Dias Restantes]`; Measure `KPI[KPI 90 / PRAZO / Duração Baseline]` | `F:\BI N&A CONNECT\CHS001-000-PfM0-RT-002-01\Report\definition\pages\7340affd805e6aa3300a\visuals\819db0fa8c16f76fba3a\visual.json` |
| `9a23de1bd69e9fb00a02` | cardVisual | — | Measure `KPI[KPI 90 / PRAZO / Desvio]` | `F:\BI N&A CONNECT\CHS001-000-PfM0-RT-002-01\Report\definition\pages\7340affd805e6aa3300a\visuals\9a23de1bd69e9fb00a02\visual.json` |
| `a07bd0e8b0bf95e8eab2` | slicer | — | Column `WBX[Disciplina]` | `F:\BI N&A CONNECT\CHS001-000-PfM0-RT-002-01\Report\definition\pages\7340affd805e6aa3300a\visuals\a07bd0e8b0bf95e8eab2\visual.json` |
| `a5a78671a7e188397cd0` | card | % Complete | Measure `KPI[KPI 90 / ACC / % Realizado]` | `F:\BI N&A CONNECT\CHS001-000-PfM0-RT-002-01\Report\definition\pages\7340affd805e6aa3300a\visuals\a5a78671a7e188397cd0\visual.json` |
| `ad4dde21f1811240207a` | card | % Devation | Measure `KPI[KPI 90 / ACC / % Desvio]` | `F:\BI N&A CONNECT\CHS001-000-PfM0-RT-002-01\Report\definition\pages\7340affd805e6aa3300a\visuals\ad4dde21f1811240207a\visual.json` |
| `b3c33a1f9c33a350ed4e` | cardVisual | — | Measure `KPI[Activity Title]` | `F:\BI N&A CONNECT\CHS001-000-PfM0-RT-002-01\Report\definition\pages\7340affd805e6aa3300a\visuals\b3c33a1f9c33a350ed4e\visual.json` |
| `b78178015d40fb883474` | slicer | — | Column `WBX[Pacote]` | `F:\BI N&A CONNECT\CHS001-000-PfM0-RT-002-01\Report\definition\pages\7340affd805e6aa3300a\visuals\b78178015d40fb883474\visual.json` |
| `b88b6d735ef6f6bfb988` | card | % Complete | Measure `KPI[KPI 90 / CARD / Weekly Complete]` | `F:\BI N&A CONNECT\CHS001-000-PfM0-RT-002-01\Report\definition\pages\7340affd805e6aa3300a\visuals\b88b6d735ef6f6bfb988\visual.json` |
| `bc7e47d19ecccb00d5c6` | cardVisual | — | Measure `KPI[KPI 90 / PRAZO / Término Baseline]`; Measure `KPI[KPI 90 / PRAZO / Término Reprogramado]` | `F:\BI N&A CONNECT\CHS001-000-PfM0-RT-002-01\Report\definition\pages\7340affd805e6aa3300a\visuals\bc7e47d19ecccb00d5c6\visual.json` |
| `be151583b05c38d410aa` | donutChart | — | Aggregation `WBX[TaskID]`; Column `WBX[Status1]`; Column `WBX[TaskID]` | `F:\BI N&A CONNECT\CHS001-000-PfM0-RT-002-01\Report\definition\pages\7340affd805e6aa3300a\visuals\be151583b05c38d410aa\visual.json` |
| `f07c53307af0c5f22a9b` | card | % Devation | Measure `KPI[KPI 90 / CARD / Weekly Deviation]` | `F:\BI N&A CONNECT\CHS001-000-PfM0-RT-002-01\Report\definition\pages\7340affd805e6aa3300a\visuals\f07c53307af0c5f22a9b\visual.json` |

### Inventário completo (ID, tipo e coordenadas)

| ID | Tipo | X | Y | L | A | Z |
|---|---|---:|---:|---:|---:|---:|
| `b0dcd80b82e93b7b304d` | shape | 225.71428571428572 | 222.85714285714286 | 1800.0000000000002 | 147.14285714285714 | 0 |
| `2c2f2555be29deda12e3` | shape | 752.3546798029556 | 387.2413793103448 | 1273.7475369458127 | 338.8362068965517 | 1000 |
| `485db311e7e38517d3a1` | pivotTable | 231.42857142857144 | 731.4285714285714 | 1791.4285714285716 | 390 | 2000 |
| `47a6a0c13e374b24051d` | image | 30.000000000000004 | 0 | 145.71428571428572 | 130 | 3000 |
| `8d06ac295e5a0779f0f9` | shape | 710.8645320197044 | 5.532019704433497 | 830 | 52.85714285714286 | 4000 |
| `a07bd0e8b0bf95e8eab2` | slicer | 227.14285714285717 | 145.71428571428572 | 595.7142857142858 | 70 | 5000 |
| `07da38527d3bbc91c181` | slicer | 840 | 145.71428571428572 | 582.8571428571429 | 70 | 6000 |
| `b78178015d40fb883474` | slicer | 1438.5714285714287 | 147.14285714285714 | 584.2857142857143 | 70 | 7000 |
| `819db0fa8c16f76fba3a` | cardVisual | 1870 | 230 | 141.66666666666669 | 135 | 8000 |
| `9a23de1bd69e9fb00a02` | cardVisual | 1719.0751231527092 | 229.57881773399012 | 136.91748768472905 | 130.0024630541872 | 9000 |
| `bc7e47d19ecccb00d5c6` | cardVisual | 1542.0504926108374 | 226.81280788177338 | 156.2795566502463 | 141.06650246305418 | 10000 |
| `a8653f194b1329cf9d04` | textbox | 1122.857142857143 | 225.71428571428572 | 338.5714285714286 | 48.57142857142858 | 11000 |
| `4d42161abc0acb822bbb` | card | 1062.5 | 293.25 | 155 | 67.5 | 12000 |
| `b88b6d735ef6f6bfb988` | card | 1207.5 | 293.5 | 155 | 67.5 | 13000 |
| `f07c53307af0c5f22a9b` | card | 1352.5 | 293.75 | 155 | 67 | 14000 |
| `8d0d1bce4ba8e95c80f0` | shape | 1506.6666666666667 | 223.33333333333334 | 40 | 146.66666666666669 | 15000 |
| `ddd00745d103cea76609` | shape | 1016.6666666666666 | 223.33333333333331 | 40 | 146.66666666666666 | 16000 |
| `703423ed3cf746656234` | card | 542.8571428571429 | 291.904761904762 | 154.2857142857143 | 67.14285714285715 | 17000 |
| `a5a78671a7e188397cd0` | card | 694.4285714285716 | 291.904761904762 | 154 | 67.14285714285715 | 18000 |
| `ad4dde21f1811240207a` | card | 845.7142857142858 | 291.904761904762 | 154 | 67.14285714285715 | 19000 |
| `c48c68e49ffd8d05872f` | textbox | 617.1428571428572 | 225.71428571428572 | 344.28571428571433 | 47.142857142857146 | 20000 |
| `91e388665506a0e18ff1` | image | 271.06896551724134 | 298.72906403940885 | 81.59729064039408 | 60.85221674876847 | 21000 |
| `0f6204a5fb23b2345c98` | shape | 225.429802955665 | 387.2413793103448 | 510.3288177339901 | 342.9852216748768 | 22000 |
| `be151583b05c38d410aa` | donutChart | 241.42857142857144 | 420 | 478.5714285714286 | 290 | 23000 |
| `afbcf42ee0273e204eb4` | shape | 6.5562310030395 | 231.87841945288753 | 191.4164133738602 | 81 | 24000 |
| `92bdaf79a49d464b6d54` | textbox | 789.6958128078817 | 455.0086206896551 | 1175.5541871921182 | 658.3103448275862 | 25000 |
| `2a90aabcc1970de62ecb` | slicer | 241.42857142857144 | 230.00000000000003 | 214.2857142857143 | 74.28571428571429 | 26000 |
| `1ef6fd71734efc037b87` | image | 26.27709359605911 | 139.6834975369458 | 149.36453201970443 | 76.06527093596058 | 28000 |
| `a2fa887b6261808e6ba3` | image | 212.98275862068965 | 5.532019704433497 | 156.2795566502463 | 84.36330049261083 | 29000 |
| `1421a31df96379a6e750` | shape | 3.9625126646403146 | 311.50557244174263 | 192.9726443768997 | 81 | 30000 |
| `5259295fc7992d1b6f33` | shape | 2.925025329280629 | 391.13272543059776 | 191.4164133738602 | 81 | 31000 |
| `7cfff8d9245b392a0926` | shape | 3.443768996960472 | 470.7598784194529 | 189.86018237082067 | 81 | 32000 |
| `e5fbb8a248b5cbba64c0` | shape | 3.9625126646403146 | 550.387031408308 | 189.86018237082067 | 81 | 33000 |
| `764ad691ae57c59276ab` | actionButton | 1940.3636363636363 | 21.818181818181817 | 59.63636363636363 | 49.45454545454545 | 34000 |
| `c1e0ddc60295db49765a` | shape | 6.5562310030395 | 881.9012158054711 | 191.4164133738602 | 81 | 35000 |
| `f1f3930dce41c4053862` | shape | 6.5562310030395 | 962.3434650455927 | 191.4164133738602 | 81 | 36000 |
| `309d60d4f6f15eb95e57` | shape | 5 | 801.4589665653496 | 192.9726443768997 | 81 | 37000 |
| `2220e6d03e7790f211f8` | shape | 4.481256332320157 | 630.014184397163 | 189.86018237082067 | 81 | 38000 |
| `2f1aebdd380608f6a11c` | shape | 5 | 709.6413373860181 | 189.86018237082067 | 81 | 39000 |
| `b3c33a1f9c33a350ed4e` | cardVisual | 720.545566502463 | 89.89532019704433 | 905.8682266009852 | 55.320197044334975 | 40000 |
| `02adc24808a37445a0d7` | actionButton | 1855.9926108374384 | 15.213054187192117 | 62.23522167487684 | 66.38423645320196 | 41000 |
| `05c37b733f3141ff0651` | lineClusteredColumnComboChart | 769.0809585610616 | 419.2808964117937 | 1243.4285714285713 | 292.57142857142856 | 42000 |
| `436b0b7c423b714e2612` | shape | 5.097583225066401 | 1042 | 190.85467980295564 | 81 | 43000 |
| `3ec54e122523c68dbb98` | shape | 743.8784194528876 | 57.58054711246201 | 755 | 32 | 43001 |

## LDP - Engineering - 30%

Página 12, ID `4d0c3d1a40ad48f4ead6`, 2048×1123; 25 visuais. Fonte: `F:\BI N&A CONNECT\CHS001-000-PfM0-RT-002-01\Report\definition\pages\4d0c3d1a40ad48f4ead6\page.json`.

Tipos: actionButton (2), cardVisual (5), clusteredBarChart (1), clusteredColumnChart (2), image (3), shape (11), tableEx (1).

Filtros: `FORMAT NOT IN {null, 3D}` e `Fase 30% <> BLANK()`.

### Visuais de dados

| ID | Tipo | Título | Campos/medidas | Evidência |
|---|---|---|---|---|
| `1c6e40b19e78ec0753de` | cardVisual | — | Measure `LDP (4)[In Review CHS]`; Measure `dFase Engenharia 30%[CLIENT REVIEW 30]` | `F:\BI N&A CONNECT\CHS001-000-PfM0-RT-002-01\Report\definition\pages\4d0c3d1a40ad48f4ead6\visuals\1c6e40b19e78ec0753de\visual.json` |
| `2d7506eb935d84cc43d9` | cardVisual | — | Measure `dFase Engenharia 30%[ADRESSING 30]` | `F:\BI N&A CONNECT\CHS001-000-PfM0-RT-002-01\Report\definition\pages\4d0c3d1a40ad48f4ead6\visuals\2d7506eb935d84cc43d9\visual.json` |
| `3ed78afe5dd6a6332962` | cardVisual | — | Measure `LDP (4)[Issued por Fase]`; Measure `dFase Engenharia 30%[ISSUED 30]` | `F:\BI N&A CONNECT\CHS001-000-PfM0-RT-002-01\Report\definition\pages\4d0c3d1a40ad48f4ead6\visuals\3ed78afe5dd6a6332962\visual.json` |
| `46c74ecdea630149cf6d` | clusteredBarChart | DOCUMENTS BY DISCIPLINE | Aggregation `LDP (4)[Fase 30%]`; Column `EAP[Status in English Column]`; Column `EAP[Status]`; Column `LDP (4)[DISCIPLINE]`; Column `LDP (4)[Fase 30%]`; Column `LDP[STATUS GERAL]`; Column `Ricardo[Status Task]`; Column `Schedule[Status]`; Column `WBS[Status]` | `F:\BI N&A CONNECT\CHS001-000-PfM0-RT-002-01\Report\definition\pages\4d0c3d1a40ad48f4ead6\visuals\46c74ecdea630149cf6d\visual.json` |
| `95a23f2fbeec19024e11` | tableEx | — | Column `LDP (4)[DISCIPLINE]`; Column `LDP (4)[DOCUMENT STATUS 30%]`; Column `LDP (4)[DOCUMENT TITLE]`; Column `LDP (4)[FORMAT]`; Column `LDP (4)[Fase 30%]`; Column `LDP (4)[ITEM]`; Column `LDP (4)[N&A DOCUMENT NUMBER]`; Column `LDP (4)[STATUS IN THE FLOW]`; Column `l[FORMAT]`; Measure `KPI[Adh]` | `F:\BI N&A CONNECT\CHS001-000-PfM0-RT-002-01\Report\definition\pages\4d0c3d1a40ad48f4ead6\visuals\95a23f2fbeec19024e11\visual.json` |
| `96cec642a256e4e118fd` | clusteredColumnChart | SITUATION | Aggregation `LDP (4)[Fase 30%]`; Column `EAP[Status in English Column]`; Column `EAP[Status]`; Column `LDP (4)[DOCUMENT STATUS]`; Column `LDP (4)[Fase 30%]`; Column `LDP (4)[Personalizar.3]`; Column `LDP (4)[SITUATION 30%]`; Column `LDP (4)[SITUATION]`; Column `LDP[STATUS GERAL]`; Column `Ricardo[Status Task]`; Column `Schedule[Status]`; Column `WBS[Status]` | `F:\BI N&A CONNECT\CHS001-000-PfM0-RT-002-01\Report\definition\pages\4d0c3d1a40ad48f4ead6\visuals\96cec642a256e4e118fd\visual.json` |
| `9aa325983a72e51c5a5f` | cardVisual | — | Measure `LDP (4)[Cancelled]`; Measure `dFase Engenharia 30%[CANCELED 30]` | `F:\BI N&A CONNECT\CHS001-000-PfM0-RT-002-01\Report\definition\pages\4d0c3d1a40ad48f4ead6\visuals\9aa325983a72e51c5a5f\visual.json` |
| `c1d38717011031879ba3` | clusteredColumnChart | STATUS  | Aggregation `LDP (4)[Fase 30%]`; Column `EAP[Status in English Column]`; Column `EAP[Status]`; Column `LDP (4)[DOCUMENT STATUS 30%]`; Column `LDP (4)[DOCUMENT STATUS]`; Column `LDP (4)[Fase 30%]`; Column `LDP (4)[Personalizar.2]`; Column `LDP[STATUS GERAL]`; Column `Ricardo[Status Task]`; Column `Schedule[Status]`; Column `WBS[Status]` | `F:\BI N&A CONNECT\CHS001-000-PfM0-RT-002-01\Report\definition\pages\4d0c3d1a40ad48f4ead6\visuals\c1d38717011031879ba3\visual.json` |
| `c76cb8f4af950186a0e9` | cardVisual | Total | Aggregation `LDP (4)[Fase 30%]`; Column `LDP (4)[Fase 30%]` | `F:\BI N&A CONNECT\CHS001-000-PfM0-RT-002-01\Report\definition\pages\4d0c3d1a40ad48f4ead6\visuals\c76cb8f4af950186a0e9\visual.json` |

### Inventário completo (ID, tipo e coordenadas)

| ID | Tipo | X | Y | L | A | Z |
|---|---|---:|---:|---:|---:|---:|
| `95a23f2fbeec19024e11` | tableEx | 203.33333333333334 | 715 | 1825 | 386.6666666666667 | 0 |
| `c1d38717011031879ba3` | clusteredColumnChart | 1596.6666666666667 | 101.66666666666667 | 431 | 603 | 1000 |
| `ab38b90c6128af996e27` | shape | 217 | 94 | 210 | 623 | 2000 |
| `c76cb8f4af950186a0e9` | cardVisual | 240 | 128 | 160 | 88 | 3000 |
| `9aa325983a72e51c5a5f` | cardVisual | 238.16666666666669 | 588.3333333333334 | 160 | 88 | 4000 |
| `3ed78afe5dd6a6332962` | cardVisual | 240.3333333333334 | 474.58333333333337 | 160 | 88 | 5000 |
| `46c74ecdea630149cf6d` | clusteredBarChart | 435 | 103.33333333333334 | 691.6666666666667 | 603.3333333333334 | 6000 |
| `96cec642a256e4e118fd` | clusteredColumnChart | 1141.25 | 105 | 436.25 | 603 | 7000 |
| `2d7506eb935d84cc43d9` | cardVisual | 240 | 248 | 160 | 88 | 8000 |
| `8be72ca9bf8764b8b7cf` | shape | 698.417487684729 | 0 | 833.3333333333334 | 60 | 9000 |
| `242269a728b6b62a5050` | image | 30.000000000000004 | 0 | 145.71428571428572 | 130 | 10000 |
| `57f544364c3399e57728` | shape | 5.957512315270918 | 231 | 190.85467980295564 | 81.59729064039408 | 11000 |
| `985aff5bc9f8c1775ad6` | shape | 5.957512315270918 | 410.52375087966226 | 190.85467980295564 | 81 | 12000 |
| `1c6e40b19e78ec0753de` | cardVisual | 240 | 360 | 160 | 88 | 13000 |
| `891b903ce5ff22999fed` | shape | 5.957512315270918 | 766.0195285010557 | 190.85467980295564 | 78.83128078817734 | 14000 |
| `7a3faec1b1dc7ab41d51` | image | 26.27709359605911 | 139.6834975369458 | 149.36453201970443 | 76.06527093596058 | 16000 |
| `67513d5a3438c2f15b8d` | image | 212.98275862068965 | 5.532019704433497 | 156.2795566502463 | 84.36330049261083 | 17000 |
| `8ceffa8598e25e2393d6` | shape | 5.957512315270918 | 499.98698099929635 | 190.85467980295564 | 80.21428571428571 | 18000 |
| `2dd7b5fb8ca2ef65abea` | shape | 5.957512315270918 | 588.6644968332162 | 190.85467980295564 | 80.21428571428571 | 19000 |
| `a9a97be1d1981b97130f` | shape | 5.957512315270918 | 677.3420126671359 | 190.85467980295564 | 80.21428571428571 | 20000 |
| `ca46377a6a3f3f95f92e` | actionButton | 1940.3636363636363 | 21.818181818181817 | 59.63636363636363 | 49.45454545454545 | 21000 |
| `aad1e28ea15a9a3523b4` | shape | 4.574507389162534 | 321.06052076002817 | 193.6206896551724 | 81 | 22000 |
| `bd871f1f700009bacc77` | actionButton | 1855.9926108374384 | 15.213054187192117 | 62.23522167487684 | 66.38423645320196 | 23000 |
| `b22c671b9091d318b1d7` | shape | 5.957512315270918 | 853.3140394088671 | 190.85467980295564 | 81 | 24000 |
| `0f59dab7812b1ec8994c` | shape | 743.8784194528876 | 57.58054711246201 | 755 | 32 | 24001 |

## LDP - Engineering - 60%

Página 13, ID `24ea8fc081207ebc9c38`, 2048×1123; 25 visuais. Fonte: `F:\BI N&A CONNECT\CHS001-000-PfM0-RT-002-01\Report\definition\pages\24ea8fc081207ebc9c38\page.json`.

Tipos: actionButton (2), cardVisual (5), clusteredBarChart (1), clusteredColumnChart (2), image (3), shape (11), tableEx (1).

Filtros: `FORMAT NOT IN {null, 3D}` e `Fase 60% <> BLANK()`.

### Visuais de dados

| ID | Tipo | Título | Campos/medidas | Evidência |
|---|---|---|---|---|
| `2b0758374caf11b95501` | tableEx | — | Column `LDP (4)[DISCIPLINE]`; Column `LDP (4)[DOCUMENT STATUS 60%]`; Column `LDP (4)[DOCUMENT TITLE]`; Column `LDP (4)[FORMAT]`; Column `LDP (4)[Fase 60%]`; Column `LDP (4)[ITEM]`; Column `LDP (4)[N&A DOCUMENT NUMBER]`; Column `LDP (4)[STATUS IN THE FLOW]`; Measure `KPI[Adh]` | `F:\BI N&A CONNECT\CHS001-000-PfM0-RT-002-01\Report\definition\pages\24ea8fc081207ebc9c38\visuals\2b0758374caf11b95501\visual.json` |
| `609669a7074cc1b181dd` | cardVisual | Total | Aggregation `LDP (4)[Fase 60%]`; Column `LDP (4)[Fase 60%]` | `F:\BI N&A CONNECT\CHS001-000-PfM0-RT-002-01\Report\definition\pages\24ea8fc081207ebc9c38\visuals\609669a7074cc1b181dd\visual.json` |
| `671006ee7d7c7aa0a146` | clusteredColumnChart | SITUATION | Aggregation `LDP (4)[Fase 60%]`; Column `EAP[Status in English Column]`; Column `EAP[Status]`; Column `LDP (4)[DOCUMENT STATUS]`; Column `LDP (4)[Fase 60%]`; Column `LDP (4)[Personalizar.3]`; Column `LDP (4)[SITUATION 60%]`; Column `LDP (4)[SITUATION]`; Column `LDP[STATUS GERAL]`; Column `Ricardo[Status Task]`; Column `Schedule[Status]`; Column `WBS[Status]`; Column `l[SITUATION 60%]` | `F:\BI N&A CONNECT\CHS001-000-PfM0-RT-002-01\Report\definition\pages\24ea8fc081207ebc9c38\visuals\671006ee7d7c7aa0a146\visual.json` |
| `67a267ff45b19f11dd73` | cardVisual | — | Measure `LDP (4)[In Review CHS]`; Measure `dFase Engenharia 60%[CLIENT REVIEW 60]` | `F:\BI N&A CONNECT\CHS001-000-PfM0-RT-002-01\Report\definition\pages\24ea8fc081207ebc9c38\visuals\67a267ff45b19f11dd73\visual.json` |
| `9b1eefccb3738e63ad1d` | cardVisual | — | Measure `LDP (4)[Cancelled]`; Measure `dFase Engenharia 60%[CANCELED 60]` | `F:\BI N&A CONNECT\CHS001-000-PfM0-RT-002-01\Report\definition\pages\24ea8fc081207ebc9c38\visuals\9b1eefccb3738e63ad1d\visual.json` |
| `b5eecab7e475851cac6c` | clusteredColumnChart | STATUS  | Aggregation `LDP (4)[Fase 60%]`; Column `EAP[Status in English Column]`; Column `EAP[Status]`; Column `LDP (4)[DOCUMENT STATUS 60%]`; Column `LDP (4)[DOCUMENT STATUS]`; Column `LDP (4)[Fase 60%]`; Column `LDP (4)[Personalizar.2]`; Column `LDP[STATUS GERAL]`; Column `Ricardo[Status Task]`; Column `Schedule[Status]`; Column `WBS[Status]`; Column `l[DOCUMENT STATUS 60%]` | `F:\BI N&A CONNECT\CHS001-000-PfM0-RT-002-01\Report\definition\pages\24ea8fc081207ebc9c38\visuals\b5eecab7e475851cac6c\visual.json` |
| `c370079fb7c02005858a` | cardVisual | — | Measure `LDP (4)[Issued por Fase]`; Measure `dFase Engenharia 60%[ISSUED 60]` | `F:\BI N&A CONNECT\CHS001-000-PfM0-RT-002-01\Report\definition\pages\24ea8fc081207ebc9c38\visuals\c370079fb7c02005858a\visual.json` |
| `ca62268a15fdb9df1f6d` | clusteredBarChart | DOCUMENTS BY DISCIPLINE | Aggregation `LDP (4)[Fase 60%]`; Column `EAP[Status in English Column]`; Column `EAP[Status]`; Column `LDP (4)[DISCIPLINE]`; Column `LDP (4)[Fase 60%]`; Column `LDP[STATUS GERAL]`; Column `Ricardo[Status Task]`; Column `Schedule[Status]`; Column `WBS[Status]` | `F:\BI N&A CONNECT\CHS001-000-PfM0-RT-002-01\Report\definition\pages\24ea8fc081207ebc9c38\visuals\ca62268a15fdb9df1f6d\visual.json` |
| `e807f67b89dbbec173f2` | cardVisual | — | Measure `LDP (4)[In Review N&A]`; Measure `dFase Engenharia 60%[ADRESSING 60]` | `F:\BI N&A CONNECT\CHS001-000-PfM0-RT-002-01\Report\definition\pages\24ea8fc081207ebc9c38\visuals\e807f67b89dbbec173f2\visual.json` |

### Inventário completo (ID, tipo e coordenadas)

| ID | Tipo | X | Y | L | A | Z |
|---|---|---:|---:|---:|---:|---:|
| `2b0758374caf11b95501` | tableEx | 203.33333333333334 | 715 | 1825 | 386.6666666666667 | 0 |
| `b5eecab7e475851cac6c` | clusteredColumnChart | 1596.6666666666667 | 101.66666666666667 | 431 | 603 | 1000 |
| `02862e2726f0e6a173ba` | shape | 217 | 94 | 210 | 623 | 2000 |
| `609669a7074cc1b181dd` | cardVisual | 240 | 128 | 160 | 88 | 3000 |
| `9b1eefccb3738e63ad1d` | cardVisual | 238.16666666666669 | 588.3333333333334 | 160 | 88 | 4000 |
| `c370079fb7c02005858a` | cardVisual | 240.3333333333334 | 474.58333333333337 | 160 | 88 | 5000 |
| `ca62268a15fdb9df1f6d` | clusteredBarChart | 435 | 103.33333333333334 | 691.6666666666667 | 603.3333333333334 | 6000 |
| `671006ee7d7c7aa0a146` | clusteredColumnChart | 1141.25 | 105 | 436.25 | 603 | 7000 |
| `e807f67b89dbbec173f2` | cardVisual | 240 | 248 | 160 | 88 | 8000 |
| `6233c50b220cb09f4a28` | shape | 698.417487684729 | 0 | 833.3333333333334 | 60 | 9000 |
| `257cce4aca3306c1f4f0` | image | 30.000000000000004 | 0 | 145.71428571428572 | 130 | 10000 |
| `942aa2ad3b12397de210` | shape | 5.516807397023371 | 231 | 190.85467980295564 | 81 | 11000 |
| `0965d59e7694f4a1c42a` | shape | 5.516807397023371 | 408.8040112596764 | 190.85467980295564 | 81 | 12000 |
| `67a267ff45b19f11dd73` | cardVisual | 240 | 360 | 160 | 88 | 13000 |
| `a4cf5e7add0672f216a0` | shape | 5.516807397023371 | 764.4120337790291 | 190.85467980295564 | 81 | 14000 |
| `cf32dca0331c9e31da8a` | image | 26.27709359605911 | 139.6834975369458 | 149.36453201970443 | 76.06527093596058 | 16000 |
| `04193711052d185d1291` | image | 212.98275862068965 | 5.532019704433497 | 156.2795566502463 | 84.36330049261083 | 17000 |
| `5c4550516a18587caa0a` | shape | 5.516807397023371 | 497.70601688951456 | 190.85467980295564 | 81 | 18000 |
| `eee8c9d24c0bf1a1e3b0` | shape | 5.516807397023371 | 586.6080225193527 | 190.85467980295564 | 81 | 19000 |
| `4abb27cae74cbf8f74ce` | shape | 5.516807397023371 | 675.5100281491909 | 190.85467980295564 | 81 | 20000 |
| `66948fff0846e56de8d5` | actionButton | 1940.3636363636363 | 21.818181818181817 | 59.63636363636363 | 49.45454545454545 | 21000 |
| `adc74d8d5d5158aa8bcd` | shape | 4.4578251100513455 | 319.9020056298382 | 192.9726443768997 | 81 | 22000 |
| `70b71ce1eb0121c0e8a3` | actionButton | 1855.9926108374384 | 15.213054187192117 | 62.23522167487684 | 66.38423645320196 | 23000 |
| `6d23cbf99b9eca4337be` | shape | 5.516807397023371 | 853.3140394088673 | 190.85467980295564 | 81 | 24000 |
| `362f4f982a1a0307a79a` | shape | 743.8784194528876 | 57.58054711246201 | 755 | 32 | 24001 |

## LDP - Engineering - 90%

Página 14, ID `a549eb6c70bd79a484e7`, 2048×1123; 25 visuais. Fonte: `F:\BI N&A CONNECT\CHS001-000-PfM0-RT-002-01\Report\definition\pages\a549eb6c70bd79a484e7\page.json`.

Tipos: actionButton (2), cardVisual (5), clusteredBarChart (1), clusteredColumnChart (2), image (3), shape (11), tableEx (1).

Filtros: `FORMAT NOT IN {null, AT, 3D}` e `Fase 90% <> BLANK()`.

### Visuais de dados

| ID | Tipo | Título | Campos/medidas | Evidência |
|---|---|---|---|---|
| `14ff715b6cc8447366f7` | cardVisual | — | Measure `LDP (4)[Cancelled]`; Measure `dFase Engenharia 90%[CANCELED 90]` | `F:\BI N&A CONNECT\CHS001-000-PfM0-RT-002-01\Report\definition\pages\a549eb6c70bd79a484e7\visuals\14ff715b6cc8447366f7\visual.json` |
| `4513d60a5658970b46a7` | clusteredBarChart | DOCUMENTS BY DISCIPLINE | Aggregation `LDP (4)[Fase 90%]`; Column `EAP[Status in English Column]`; Column `EAP[Status]`; Column `LDP (4)[DISCIPLINE]`; Column `LDP (4)[Fase 90%]`; Column `LDP[STATUS GERAL]`; Column `Ricardo[Status Task]`; Column `Schedule[Status]`; Column `WBS[Status]` | `F:\BI N&A CONNECT\CHS001-000-PfM0-RT-002-01\Report\definition\pages\a549eb6c70bd79a484e7\visuals\4513d60a5658970b46a7\visual.json` |
| `58c537142463f831aaf9` | clusteredColumnChart | STATUS  | Aggregation `LDP (4)[Fase 90%]`; Column `EAP[Status in English Column]`; Column `EAP[Status]`; Column `LDP (4)[DOCUMENT STATUS 90%]`; Column `LDP (4)[DOCUMENT STATUS]`; Column `LDP (4)[Fase 90%]`; Column `LDP (4)[Personalizar.2]`; Column `LDP[STATUS GERAL]`; Column `Ricardo[Status Task]`; Column `Schedule[Status]`; Column `WBS[Status]`; Column `l[DOCUMENT STATUS 90%]` | `F:\BI N&A CONNECT\CHS001-000-PfM0-RT-002-01\Report\definition\pages\a549eb6c70bd79a484e7\visuals\58c537142463f831aaf9\visual.json` |
| `6aa4cb19df045d0f1310` | cardVisual | — | Measure `dFase Engenharia 90%[CLIENT REVIEW 90]` | `F:\BI N&A CONNECT\CHS001-000-PfM0-RT-002-01\Report\definition\pages\a549eb6c70bd79a484e7\visuals\6aa4cb19df045d0f1310\visual.json` |
| `8d8358a84572eb391d3c` | clusteredColumnChart | SITUATION | Aggregation `LDP (4)[Fase 90%]`; Column `EAP[Status in English Column]`; Column `EAP[Status]`; Column `LDP (4)[DOCUMENT STATUS]`; Column `LDP (4)[Fase 90%]`; Column `LDP (4)[Personalizar.3]`; Column `LDP (4)[SITUATION 90%]`; Column `LDP (4)[SITUATION]`; Column `LDP[STATUS GERAL]`; Column `Ricardo[Status Task]`; Column `Schedule[Status]`; Column `WBS[Status]`; Column `l[SITUATION 90%]` | `F:\BI N&A CONNECT\CHS001-000-PfM0-RT-002-01\Report\definition\pages\a549eb6c70bd79a484e7\visuals\8d8358a84572eb391d3c\visual.json` |
| `8e1b7156caf61663f705` | tableEx | — | Column `LDP (4)[DISCIPLINE]`; Column `LDP (4)[DOCUMENT STATUS 90%]`; Column `LDP (4)[DOCUMENT TITLE]`; Column `LDP (4)[FORMAT]`; Column `LDP (4)[Fase 90%]`; Column `LDP (4)[ITEM]`; Column `LDP (4)[N&A DOCUMENT NUMBER]`; Column `LDP (4)[STATUS IN THE FLOW]`; Measure `KPI[Adh]` | `F:\BI N&A CONNECT\CHS001-000-PfM0-RT-002-01\Report\definition\pages\a549eb6c70bd79a484e7\visuals\8e1b7156caf61663f705\visual.json` |
| `b8a28b51bdb0eff0036f` | cardVisual | — | Measure `LDP (4)[Issued por Fase]`; Measure `dFase Engenharia 90%[ISSUED 90]` | `F:\BI N&A CONNECT\CHS001-000-PfM0-RT-002-01\Report\definition\pages\a549eb6c70bd79a484e7\visuals\b8a28b51bdb0eff0036f\visual.json` |
| `e5ed0bbeb9e74de5f151` | cardVisual | Total | Aggregation `LDP (4)[Fase 90%]`; Column `LDP (4)[Fase 90%]` | `F:\BI N&A CONNECT\CHS001-000-PfM0-RT-002-01\Report\definition\pages\a549eb6c70bd79a484e7\visuals\e5ed0bbeb9e74de5f151\visual.json` |
| `f402115663ab01639058` | cardVisual | — | Measure `LDP (4)[In Review CHS]`; Measure `dFase Engenharia 90%[ADRESSING 90]` | `F:\BI N&A CONNECT\CHS001-000-PfM0-RT-002-01\Report\definition\pages\a549eb6c70bd79a484e7\visuals\f402115663ab01639058\visual.json` |

### Inventário completo (ID, tipo e coordenadas)

| ID | Tipo | X | Y | L | A | Z |
|---|---|---:|---:|---:|---:|---:|
| `8e1b7156caf61663f705` | tableEx | 203.33333333333334 | 715 | 1825 | 386.6666666666667 | 0 |
| `58c537142463f831aaf9` | clusteredColumnChart | 1596.6666666666667 | 101.66666666666667 | 431 | 603 | 1000 |
| `b361a4c2457b4cc4bd85` | shape | 217 | 94 | 210 | 623 | 2000 |
| `e5ed0bbeb9e74de5f151` | cardVisual | 240 | 128 | 160 | 88 | 3000 |
| `14ff715b6cc8447366f7` | cardVisual | 238.16666666666669 | 588.3333333333334 | 160 | 88 | 4000 |
| `b8a28b51bdb0eff0036f` | cardVisual | 240.3333333333334 | 474.58333333333337 | 160 | 88 | 5000 |
| `4513d60a5658970b46a7` | clusteredBarChart | 435 | 103.33333333333334 | 691.6666666666667 | 603.3333333333334 | 6000 |
| `8d8358a84572eb391d3c` | clusteredColumnChart | 1141.25 | 105 | 436.25 | 603 | 7000 |
| `6aa4cb19df045d0f1310` | cardVisual | 240 | 248 | 160 | 88 | 8000 |
| `681357c178d7ea73bf60` | shape | 698.417487684729 | 0 | 833.3333333333334 | 60 | 9000 |
| `2a90b8641473c0d26a39` | image | 30.000000000000004 | 0 | 145.71428571428572 | 130 | 10000 |
| `67ce4e6805ab466fe873` | shape | 5.292564655172413 | 231 | 190.85467980295564 | 81 | 11000 |
| `6bbee33cc67f4736afbf` | shape | 5.292564655172413 | 408.8040112596764 | 190.85467980295564 | 81 | 12000 |
| `f402115663ab01639058` | cardVisual | 240 | 360 | 160 | 88 | 13000 |
| `249abcde95da89a65f55` | shape | 5.292564655172413 | 764.4120337790291 | 190.85467980295564 | 81 | 14000 |
| `9dcf63b1f6891c50a793` | image | 26.27709359605911 | 139.6834975369458 | 149.36453201970443 | 76.06527093596058 | 16000 |
| `e4f13db19239bb955c39` | image | 212.98275862068965 | 5.532019704433497 | 156.2795566502463 | 84.36330049261083 | 17000 |
| `13f2902b034fadf9edd7` | shape | 5.292564655172413 | 497.70601688951456 | 190.85467980295564 | 81 | 18000 |
| `07d92c8c771d6796d047` | shape | 5.292564655172413 | 586.6080225193527 | 190.85467980295564 | 81 | 19000 |
| `45f8e4ae60dabc760016` | shape | 5.292564655172413 | 675.5100281491909 | 190.85467980295564 | 81 | 20000 |
| `12902e9171b462e02304` | actionButton | 1940.3636363636363 | 21.818181818181817 | 59.63636363636363 | 49.45454545454545 | 21000 |
| `f8ce28c8c54b33cacd66` | shape | 3.9095597290640285 | 319.9020056298382 | 193.6206896551724 | 81 | 22000 |
| `6e681f41bdd58b9bb5ea` | actionButton | 1855.9926108374384 | 15.213054187192117 | 62.23522167487684 | 66.38423645320196 | 23000 |
| `4e8f044c1a583d213799` | shape | 5.292564655172413 | 853.3140394088673 | 190.85467980295564 | 81 | 24000 |
| `615f2a8a13ef9f5c32d0` | shape | 743.8784194528876 | 57.58054711246201 | 755 | 32 | 24001 |

## LDP - 3D - 30%

Página 8, ID `fbeec2691dbe6e33e5ce`, 2048×1123; 29 visuais. Fonte: `F:\BI N&A CONNECT\CHS001-000-PfM0-RT-002-01\Report\definition\pages\fbeec2691dbe6e33e5ce\page.json`.

Tipos: actionButton (3), cardVisual (5), clusteredBarChart (1), clusteredColumnChart (2), image (3), lineClusteredColumnComboChart (1), shape (12), slicer (1), tableEx (1).

Filtro: `LDP (4)[FORMAT] NOT IN {A, AT, B, D, null, A1}`. O conjunto remanescente não deve ser simplificado para `FORMAT = 3D` sem verificar os valores do cache.

### Visuais de dados

| ID | Tipo | Título | Campos/medidas | Evidência |
|---|---|---|---|---|
| `0669486e42327ab54c26` | clusteredColumnChart | SITUATION | Aggregation `LDP (4)[Fase 30%]`; Column `EAP[Status in English Column]`; Column `EAP[Status]`; Column `LDP (4)[DOCUMENT STATUS]`; Column `LDP (4)[Fase 30%]`; Column `LDP (4)[Personalizar.3]`; Column `LDP (4)[SITUATION 30%]`; Column `LDP (4)[SITUATION]`; Column `LDP[STATUS GERAL]`; Column `Ricardo[Status Task]`; Column `Schedule[Status]`; Column `WBS[Status]` | `F:\BI N&A CONNECT\CHS001-000-PfM0-RT-002-01\Report\definition\pages\fbeec2691dbe6e33e5ce\visuals\0669486e42327ab54c26\visual.json` |
| `166e07dc7d5926bb1940` | slicer | Date | Column `d[Data]`; Column `dSemana[Data]` | `F:\BI N&A CONNECT\CHS001-000-PfM0-RT-002-01\Report\definition\pages\fbeec2691dbe6e33e5ce\visuals\166e07dc7d5926bb1940\visual.json` |
| `4df3b18e4057ebbfe684` | clusteredBarChart | DOCUMENTS BY DISCIPLINE | Aggregation `LDP (4)[Fase 30%]`; Column `EAP[Status in English Column]`; Column `EAP[Status]`; Column `LDP (4)[DISCIPLINE]`; Column `LDP (4)[Fase 30%]`; Column `LDP[STATUS GERAL]`; Column `Ricardo[Status Task]`; Column `Schedule[Status]`; Column `WBS[Status]` | `F:\BI N&A CONNECT\CHS001-000-PfM0-RT-002-01\Report\definition\pages\fbeec2691dbe6e33e5ce\visuals\4df3b18e4057ebbfe684\visual.json` |
| `52062bc89e7d5c234597` | clusteredColumnChart | STATUS  | Aggregation `LDP (4)[ITEM]`; Column `EAP[Status in English Column]`; Column `EAP[Status]`; Column `LDP (4)[DOCUMENT STATUS 30%]`; Column `LDP (4)[DOCUMENT STATUS]`; Column `LDP (4)[Fase 30%]`; Column `LDP (4)[ITEM]`; Column `LDP (4)[Personalizar.2]`; Column `LDP[STATUS GERAL]`; Column `Ricardo[Status Task]`; Column `Schedule[Status]`; Column `WBS[Status]`; Column `l[DOCUMENT STATUS 30%]`; Column `l[Fase 30%]` | `F:\BI N&A CONNECT\CHS001-000-PfM0-RT-002-01\Report\definition\pages\fbeec2691dbe6e33e5ce\visuals\52062bc89e7d5c234597\visual.json` |
| `59602fe37f9bfac4c173` | lineClusteredColumnComboChart | — | Column `EAP[Status in English Column]`; Column `EAP[Status]`; Column `dCalendario Fonte 2[Data Corte]`; Column `dCalendario[Data Corte]`; Measure `KPI[KPI 30 / 3D / COLUNA / % Realizado]`; Measure `KPI[KPI 30 / 3D / CURVA / % Realizado]`; Measure `KPI[KPI 30 / COLUNA / % Baseline]`; Measure `KPI[KPI 30 / COLUNA / % Realizado]`; Measure `KPI[KPI 30 / CURVA / % Baseline]`; Measure `KPI[KPI 30 / CURVA / % Realizado]`; Measure `KPI[KPI 30 / CURVA / % Reprogramado]`; Measure `KPI[KPI 30 / CURVA / Mostrar Período]`; Measure `KPI[KPI / COLUNA / % Baseline]`; Measure `KPI[KPI / COLUNA / % Realizado]`; Measure `KPI[KPI / CURVA / % Baseline]`; Measure `KPI[KPI / CURVA / % Realizado]`; Measure `KPI[KPI / CURVA / % Reprogramado]`; Measure `k[KPI 30 / CURVA / Mostrar Período]` | `F:\BI N&A CONNECT\CHS001-000-PfM0-RT-002-01\Report\definition\pages\fbeec2691dbe6e33e5ce\visuals\59602fe37f9bfac4c173\visual.json` |
| `6308f22bd4b363b98781` | cardVisual | — | Measure `dFase Engenharia 30%[ADRESSING 30]` | `F:\BI N&A CONNECT\CHS001-000-PfM0-RT-002-01\Report\definition\pages\fbeec2691dbe6e33e5ce\visuals\6308f22bd4b363b98781\visual.json` |
| `c48415c75384d07c76b6` | cardVisual | — | Measure `dFase Engenharia 30%[CANCELED 30]` | `F:\BI N&A CONNECT\CHS001-000-PfM0-RT-002-01\Report\definition\pages\fbeec2691dbe6e33e5ce\visuals\c48415c75384d07c76b6\visual.json` |
| `de52f6324b02606efc96` | cardVisual | — | Measure `LDP (4)[In Review CHS]`; Measure `dFase Engenharia 30%[CLIENT REVIEW 30]` | `F:\BI N&A CONNECT\CHS001-000-PfM0-RT-002-01\Report\definition\pages\fbeec2691dbe6e33e5ce\visuals\de52f6324b02606efc96\visual.json` |
| `e84b23242144306cef9d` | cardVisual | — | Measure `LDP (4)[Issued por Fase]`; Measure `dFase Engenharia 30%[ISSUED 30]` | `F:\BI N&A CONNECT\CHS001-000-PfM0-RT-002-01\Report\definition\pages\fbeec2691dbe6e33e5ce\visuals\e84b23242144306cef9d\visual.json` |
| `fc0e8e96e8037fda032f` | cardVisual | Total | Aggregation `LDP (4)[Fase 30%]`; Column `LDP (4)[Fase 30%]` | `F:\BI N&A CONNECT\CHS001-000-PfM0-RT-002-01\Report\definition\pages\fbeec2691dbe6e33e5ce\visuals\fc0e8e96e8037fda032f\visual.json` |
| `ff5bdd78071525d5e1d4` | tableEx | — | Column `LDP (4)[DISCIPLINE]`; Column `LDP (4)[DOCUMENT STATUS 30%]`; Column `LDP (4)[DOCUMENT TITLE]`; Column `LDP (4)[FORMAT]`; Column `LDP (4)[Fase 30%]`; Column `LDP (4)[ITEM]`; Column `LDP (4)[N&A DOCUMENT NUMBER]`; Column `LDP (4)[STATUS IN THE FLOW]`; Column `l[FORMAT]`; Column `l[Fase 30%]`; Measure `KPI[Adh]` | `F:\BI N&A CONNECT\CHS001-000-PfM0-RT-002-01\Report\definition\pages\fbeec2691dbe6e33e5ce\visuals\ff5bdd78071525d5e1d4\visual.json` |

### Inventário completo (ID, tipo e coordenadas)

| ID | Tipo | X | Y | L | A | Z |
|---|---|---:|---:|---:|---:|---:|
| `ff5bdd78071525d5e1d4` | tableEx | 204.18593276269283 | 829.9170170354612 | 1824.5001088795457 | 272.24791035025714 | 0 |
| `52062bc89e7d5c234597` | clusteredColumnChart | 1595.9876847290639 | 100.95935960591132 | 432.88054187192114 | 390.00738916256154 | 1000 |
| `0659553e11507ee97fa8` | shape | 217.3592187473827 | 94.408549556944 | 208.5770280909228 | 735.5084674785172 | 2000 |
| `fc0e8e96e8037fda032f` | cardVisual | 240 | 128 | 160 | 88 | 3000 |
| `c48415c75384d07c76b6` | cardVisual | 238.16666666666669 | 588.3333333333334 | 160 | 88 | 4000 |
| `e84b23242144306cef9d` | cardVisual | 240.3333333333334 | 474.58333333333337 | 160 | 88 | 5000 |
| `4df3b18e4057ebbfe684` | clusteredBarChart | 434.7184374947654 | 103.19074021340391 | 691.5975141962177 | 711.3574431732525 | 6000 |
| `0669486e42327ab54c26` | clusteredColumnChart | 1142.3620689655172 | 105.10837438423644 | 434.26354679802955 | 385.8583743842364 | 7000 |
| `6308f22bd4b363b98781` | cardVisual | 240 | 248 | 160 | 88 | 8000 |
| `9729089344369a0fc7db` | shape | 698.417487684729 | 0 | 833.3333333333334 | 60 | 9000 |
| `0092854527bb1ec37b7d` | image | 30.000000000000004 | 0 | 145.71428571428572 | 130 | 10000 |
| `c28f9d038c47a9fbe3e6` | shape | 5 | 231 | 190.85467980295564 | 81.59729064039408 | 11000 |
| `e3ced215ca63a1c8cbf5` | shape | 5.71813423645321 | 319 | 190.85467980295564 | 80.21428571428571 | 12000 |
| `de52f6324b02606efc96` | cardVisual | 240 | 360 | 160 | 88 | 13000 |
| `ce324fbe65b77edc8cc6` | shape | 5.71813423645321 | 873.9644616467275 | 190.85467980295564 | 81 | 14000 |
| `91e1b60b82d04d946a16` | image | 26.27709359605911 | 139.6834975369458 | 149.36453201970443 | 76.06527093596058 | 16000 |
| `a7823fe4815b660ca4b7` | image | 212.98275862068965 | 5.532019704433497 | 156.2795566502463 | 84.36330049261083 | 17000 |
| `5d77f638ab93b13081e5` | shape | 5.71813423645321 | 410.97026741731173 | 190.85467980295564 | 80.21428571428571 | 18000 |
| `8060979cbe428995db18` | shape | 5.71813423645321 | 502.94053483462346 | 190.85467980295564 | 81 | 19000 |
| `34132f8ca84c1ae79caf` | shape | 5.71813423645321 | 595.6965165376495 | 190.85467980295564 | 81 | 20000 |
| `89fad2aa1afe3cd3d91e` | actionButton | 1940.3636363636363 | 21.818181818181817 | 59.63636363636363 | 49.45454545454545 | 21000 |
| `75e7bc13b85731dbfb85` | shape | 5.026631773399018 | 781.2084799437015 | 192.23768472906403 | 81 | 22000 |
| `a374ec7f06042e2625e6` | actionButton | 1855.9926108374384 | 15.213054187192117 | 62.23522167487684 | 66.38423645320196 | 23000 |
| `7db8e69c02e7021d7ba1` | actionButton | 241.51024305264744 | 689.4019665321027 | 155.88388415216335 | 100.99519254928893 | 24000 |
| `e8049a87a051819c0246` | shape | 5.71813423645321 | 688.4524982406755 | 190.85467980295564 | 81 | 25000 |
| `546e4e06c6f2e2c841c5` | shape | 5.71813423645321 | 966.7204433497535 | 190.85467980295564 | 81 | 26000 |
| `59602fe37f9bfac4c173` | lineClusteredColumnComboChart | 1142.273556231003 | 516.6686930091186 | 887.0516717325228 | 306.5775075987842 | 26001 |
| `166e07dc7d5926bb1940` | slicer | 383.0684133915575 | 8.943231441048034 | 214.2857142857143 | 74.28571428571429 | 26002 |
| `371d6e7533a501449d70` | shape | 743.8784194528876 | 57.58054711246201 | 755 | 32 | 26003 |

## LDP - 3D - 60%

Página 9, ID `95b26829169a5f2d0252`, 2048×1123; 29 visuais. Fonte: `F:\BI N&A CONNECT\CHS001-000-PfM0-RT-002-01\Report\definition\pages\95b26829169a5f2d0252\page.json`.

Tipos: actionButton (3), cardVisual (5), clusteredBarChart (1), clusteredColumnChart (2), image (3), lineClusteredColumnComboChart (1), shape (12), slicer (1), tableEx (1).

Filtro: `LDP (4)[FORMAT] NOT IN {A, B, D, AT, null}`. O conjunto remanescente não deve ser simplificado para `FORMAT = 3D` sem verificar os valores do cache.

### Visuais de dados

| ID | Tipo | Título | Campos/medidas | Evidência |
|---|---|---|---|---|
| `17be1480230e39d55ac0` | slicer | Date | Column `d[Data]`; Column `dSemana[Data]` | `F:\BI N&A CONNECT\CHS001-000-PfM0-RT-002-01\Report\definition\pages\95b26829169a5f2d0252\visuals\17be1480230e39d55ac0\visual.json` |
| `4c9078052c53b752beab` | cardVisual | Total | Aggregation `LDP (4)[Fase 60%]`; Column `LDP (4)[Fase 60%]` | `F:\BI N&A CONNECT\CHS001-000-PfM0-RT-002-01\Report\definition\pages\95b26829169a5f2d0252\visuals\4c9078052c53b752beab\visual.json` |
| `7c21c0ba6417927a5393` | clusteredColumnChart | STATUS  | Aggregation `LDP (4)[Fase 60%]`; Column `EAP[Status in English Column]`; Column `EAP[Status]`; Column `LDP (4)[DOCUMENT STATUS 60%]`; Column `LDP (4)[DOCUMENT STATUS]`; Column `LDP (4)[Fase 60%]`; Column `LDP (4)[Personalizar.2]`; Column `LDP[STATUS GERAL]`; Column `Ricardo[Status Task]`; Column `Schedule[Status]`; Column `WBS[Status]`; Column `l[Fase 60%]` | `F:\BI N&A CONNECT\CHS001-000-PfM0-RT-002-01\Report\definition\pages\95b26829169a5f2d0252\visuals\7c21c0ba6417927a5393\visual.json` |
| `9bd852d7dad89a36ea49` | lineClusteredColumnComboChart | — | Column `EAP[Status in English Column]`; Column `EAP[Status]`; Column `dCalendario Fonte 2[Data Corte]`; Column `dCalendario[Data Corte]`; Measure `KPI[KPI 30 / COLUNA / % Baseline]`; Measure `KPI[KPI 30 / COLUNA / % Realizado]`; Measure `KPI[KPI 30 / CURVA / % Baseline]`; Measure `KPI[KPI 30 / CURVA / % Realizado]`; Measure `KPI[KPI 30 / CURVA / % Reprogramado]`; Measure `KPI[KPI 60 / COLUNA / % Baseline]`; Measure `KPI[KPI 60 / COLUNA / % Realizado]`; Measure `KPI[KPI 60 / CURVA / % Baseline]`; Measure `KPI[KPI 60 / CURVA / % Realizado]`; Measure `KPI[KPI 60 / CURVA / % Reprogramado]`; Measure `KPI[KPI 60 / CURVA / Mostrar Período]`; Measure `KPI[KPI / COLUNA / % Baseline]`; Measure `KPI[KPI / COLUNA / % Realizado]`; Measure `KPI[KPI / CURVA / % Baseline]`; Measure `KPI[KPI / CURVA / % Realizado]`; Measure `KPI[KPI / CURVA / % Reprogramado]`; Measure `k[KPI 60 / CURVA / Mostrar Período]` | `F:\BI N&A CONNECT\CHS001-000-PfM0-RT-002-01\Report\definition\pages\95b26829169a5f2d0252\visuals\9bd852d7dad89a36ea49\visual.json` |
| `a889a460fc54d880ee75` | cardVisual | — | Measure `LDP (4)[In Review N&A]`; Measure `dFase Engenharia 60%[ADRESSING 60]` | `F:\BI N&A CONNECT\CHS001-000-PfM0-RT-002-01\Report\definition\pages\95b26829169a5f2d0252\visuals\a889a460fc54d880ee75\visual.json` |
| `aa321e49eebc0eb0bba1` | clusteredColumnChart | SITUATION | Aggregation `LDP (4)[Fase 60%]`; Column `EAP[Status in English Column]`; Column `EAP[Status]`; Column `LDP (4)[DOCUMENT STATUS]`; Column `LDP (4)[Fase 60%]`; Column `LDP (4)[Personalizar.3]`; Column `LDP (4)[SITUATION 60%]`; Column `LDP (4)[SITUATION]`; Column `LDP[STATUS GERAL]`; Column `Ricardo[Status Task]`; Column `Schedule[Status]`; Column `WBS[Status]` | `F:\BI N&A CONNECT\CHS001-000-PfM0-RT-002-01\Report\definition\pages\95b26829169a5f2d0252\visuals\aa321e49eebc0eb0bba1\visual.json` |
| `c14d0b312346d3359a4e` | tableEx | — | Column `LDP (4)[DISCIPLINE]`; Column `LDP (4)[DOCUMENT STATUS 60%]`; Column `LDP (4)[DOCUMENT TITLE]`; Column `LDP (4)[FORMAT]`; Column `LDP (4)[Fase 60%]`; Column `LDP (4)[ITEM]`; Column `LDP (4)[N&A DOCUMENT NUMBER]`; Column `LDP (4)[STATUS IN THE FLOW]`; Column `l[FORMAT]`; Measure `KPI[Adh]` | `F:\BI N&A CONNECT\CHS001-000-PfM0-RT-002-01\Report\definition\pages\95b26829169a5f2d0252\visuals\c14d0b312346d3359a4e\visual.json` |
| `c8ef976d5ec64de09ba5` | cardVisual | — | Measure `LDP (4)[Issued por Fase]`; Measure `dFase Engenharia 60%[ISSUED 60]` | `F:\BI N&A CONNECT\CHS001-000-PfM0-RT-002-01\Report\definition\pages\95b26829169a5f2d0252\visuals\c8ef976d5ec64de09ba5\visual.json` |
| `e2df5cc8e3046c8947f5` | cardVisual | — | Measure `LDP (4)[In Review CHS]`; Measure `dFase Engenharia 60%[CLIENT REVIEW 60]` | `F:\BI N&A CONNECT\CHS001-000-PfM0-RT-002-01\Report\definition\pages\95b26829169a5f2d0252\visuals\e2df5cc8e3046c8947f5\visual.json` |
| `e595d4cbe7a4535b733a` | clusteredBarChart | DOCUMENTS BY DISCIPLINE | Aggregation `LDP (4)[Fase 60%]`; Column `EAP[Status in English Column]`; Column `EAP[Status]`; Column `LDP (4)[DISCIPLINE]`; Column `LDP (4)[Fase 60%]`; Column `LDP[STATUS GERAL]`; Column `Ricardo[Status Task]`; Column `Schedule[Status]`; Column `WBS[Status]` | `F:\BI N&A CONNECT\CHS001-000-PfM0-RT-002-01\Report\definition\pages\95b26829169a5f2d0252\visuals\e595d4cbe7a4535b733a\visual.json` |
| `fcb7f8a429e998b114d2` | cardVisual | — | Measure `LDP (4)[Cancelled]`; Measure `dFase Engenharia 60%[CANCELED 60]` | `F:\BI N&A CONNECT\CHS001-000-PfM0-RT-002-01\Report\definition\pages\95b26829169a5f2d0252\visuals\fcb7f8a429e998b114d2\visual.json` |

### Inventário completo (ID, tipo e coordenadas)

| ID | Tipo | X | Y | L | A | Z |
|---|---|---:|---:|---:|---:|---:|
| `c14d0b312346d3359a4e` | tableEx | 203.8662613981763 | 829.4711246200608 | 1825.4589665653496 | 272.3404255319149 | 0 |
| `7c21c0ba6417927a5393` | clusteredColumnChart | 1597.5737138368895 | 101.61246236418378 | 430.91210891477937 | 406.44984945673514 | 1000 |
| `8ae8be297fcf51add24c` | shape | 216.3161094224924 | 93.37386018237082 | 210.09118541033433 | 709.6413373860182 | 2000 |
| `4c9078052c53b752beab` | cardVisual | 240 | 128 | 160 | 88 | 3000 |
| `fcb7f8a429e998b114d2` | cardVisual | 238.16666666666669 | 588.3333333333334 | 160 | 88 | 4000 |
| `c8ef976d5ec64de09ba5` | cardVisual | 240.3333333333334 | 474.58333333333337 | 160 | 88 | 5000 |
| `e595d4cbe7a4535b733a` | clusteredBarChart | 435.74468085106383 | 102.7112462006079 | 690.9665653495441 | 700.3039513677811 | 6000 |
| `aa321e49eebc0eb0bba1` | clusteredColumnChart | 1140.3176331980626 | 103.49417463018719 | 438.438957978793 | 404.56813719073176 | 7000 |
| `a889a460fc54d880ee75` | cardVisual | 240 | 248 | 160 | 88 | 8000 |
| `54f1338ada8f1ee93a1e` | shape | 698.417487684729 | 0 | 833.3333333333334 | 60 | 9000 |
| `f5725cdaa52386c4f3d0` | image | 30.000000000000004 | 0 | 145.71428571428572 | 130 | 10000 |
| `d9705e203547ac3b6dff` | shape | 5.369406814449931 | 231 | 190.85467980295564 | 81.59729064039408 | 11000 |
| `7107f071d96826d8fa03` | shape | 5.369406814449931 | 323.48768472906403 | 190.85467980295564 | 81 | 12000 |
| `e2df5cc8e3046c8947f5` | cardVisual | 240 | 360 | 160 | 88 | 13000 |
| `4dde31c890f12d10ecf5` | image | 26.27709359605911 | 139.6834975369458 | 149.36453201970443 | 76.06527093596058 | 15000 |
| `baf718b69320b7aa4cd3` | image | 212.98275862068965 | 5.532019704433497 | 156.2795566502463 | 84.36330049261083 | 16000 |
| `28f330595e11f7d34390` | shape | 5.369406814449931 | 415.378078817734 | 190.85467980295564 | 81 | 17000 |
| `4851167e396a112a2de9` | shape | 5.369406814449931 | 507.26847290640393 | 190.85467980295564 | 81 | 18000 |
| `26905606a72288f4327e` | shape | 5.369406814449931 | 599.1588669950738 | 190.85467980295564 | 81 | 19000 |
| `17ef0fd20804bbbe2084` | actionButton | 1940.3636363636363 | 21.818181818181817 | 59.63636363636363 | 49.45454545454545 | 20000 |
| `94a42035dce58b76f9c9` | shape | 5.369406814449931 | 874.8300492610837 | 190.85467980295564 | 81 | 21000 |
| `ea31d6cbc36bfaeebce6` | shape | 4.677904351395739 | 782.9396551724137 | 192.23768472906403 | 81 | 22000 |
| `4540ecfe0eb03945022e` | actionButton | 1855.9926108374384 | 15.213054187192117 | 62.23522167487684 | 66.38423645320196 | 23000 |
| `a99e0117bcae53c3be74` | actionButton | 241.51024305264744 | 689.4019665321027 | 155.88388415216335 | 100.99519254928893 | 24000 |
| `752dc29804d6da122797` | shape | 5.369406814449931 | 691.0492610837438 | 190.85467980295564 | 81 | 25000 |
| `f85588b6de37b9ca294b` | shape | 5.369406814449931 | 966.7204433497536 | 190.85467980295564 | 81 | 26000 |
| `9bd852d7dad89a36ea49` | lineClusteredColumnComboChart | 1140.7173252279636 | 510.4437689969605 | 887.0516717325228 | 292.57142857142856 | 26001 |
| `17be1480230e39d55ac0` | slicer | 383.0684133915575 | 8.943231441048034 | 214.2857142857143 | 74.28571428571429 | 26002 |
| `46a57ac5a578dbd4efe1` | shape | 743.8784194528876 | 57.58054711246201 | 755 | 32 | 26003 |

## LDP - 3D - 90%

Página 10, ID `f7e070ab93ba4e1aa3bd`, 2048×1123; 29 visuais. Fonte: `F:\BI N&A CONNECT\CHS001-000-PfM0-RT-002-01\Report\definition\pages\f7e070ab93ba4e1aa3bd\page.json`.

Tipos: actionButton (3), cardVisual (5), clusteredBarChart (1), clusteredColumnChart (2), image (3), lineClusteredColumnComboChart (1), shape (12), slicer (1), tableEx (1).

Filtro: `LDP (4)[FORMAT] NOT IN {A, B, D, null}`. O conjunto remanescente não deve ser simplificado para `FORMAT = 3D` sem verificar os valores do cache.

### Visuais de dados

| ID | Tipo | Título | Campos/medidas | Evidência |
|---|---|---|---|---|
| `291e9db4fa498824a995` | cardVisual | — | Measure `LDP (4)[Issued por Fase]`; Measure `dFase Engenharia 90%[ISSUED 90]` | `F:\BI N&A CONNECT\CHS001-000-PfM0-RT-002-01\Report\definition\pages\f7e070ab93ba4e1aa3bd\visuals\291e9db4fa498824a995\visual.json` |
| `2c9e8683b347b30efac7` | lineClusteredColumnComboChart | — | Column `EAP[Status in English Column]`; Column `EAP[Status]`; Column `dCalendario Fonte 2[Data Corte]`; Column `dCalendario[Data Corte]`; Measure `KPI[KPI 30 / COLUNA / % Baseline]`; Measure `KPI[KPI 30 / COLUNA / % Realizado]`; Measure `KPI[KPI 30 / CURVA / % Baseline]`; Measure `KPI[KPI 30 / CURVA / % Realizado]`; Measure `KPI[KPI 30 / CURVA / % Reprogramado]`; Measure `KPI[KPI 60 / COLUNA / % Baseline]`; Measure `KPI[KPI 60 / COLUNA / % Realizado]`; Measure `KPI[KPI 60 / CURVA / % Baseline]`; Measure `KPI[KPI 60 / CURVA / % Realizado]`; Measure `KPI[KPI 60 / CURVA / % Reprogramado]`; Measure `KPI[KPI 90 / COLUNA / % Baseline]`; Measure `KPI[KPI 90 / COLUNA / % Realizado]`; Measure `KPI[KPI 90 / CURVA / % Baseline]`; Measure `KPI[KPI 90 / CURVA / % Realizado]`; Measure `KPI[KPI 90 / CURVA / % Reprogramado]`; Measure `KPI[KPI 90 / CURVA / Mostrar Período]`; Measure `KPI[KPI / COLUNA / % Baseline]`; Measure `KPI[KPI / COLUNA / % Realizado]`; Measure `KPI[KPI / CURVA / % Baseline]`; Measure `KPI[KPI / CURVA / % Realizado]`; Measure `KPI[KPI / CURVA / % Reprogramado]`; Measure `k[KPI 90 / CURVA / Mostrar Período]` | `F:\BI N&A CONNECT\CHS001-000-PfM0-RT-002-01\Report\definition\pages\f7e070ab93ba4e1aa3bd\visuals\2c9e8683b347b30efac7\visual.json` |
| `2d376cb60446afce7f95` | clusteredColumnChart | STATUS  | Aggregation `LDP (4)[Fase 90%]`; Column `EAP[Status in English Column]`; Column `EAP[Status]`; Column `LDP (4)[DOCUMENT STATUS 90%]`; Column `LDP (4)[DOCUMENT STATUS]`; Column `LDP (4)[Fase 90%]`; Column `LDP (4)[Personalizar.2]`; Column `LDP[STATUS GERAL]`; Column `Ricardo[Status Task]`; Column `Schedule[Status]`; Column `WBS[Status]`; Column `l[Fase 90%]` | `F:\BI N&A CONNECT\CHS001-000-PfM0-RT-002-01\Report\definition\pages\f7e070ab93ba4e1aa3bd\visuals\2d376cb60446afce7f95\visual.json` |
| `47c579e4486267ac53a8` | slicer | Date | Column `d[Data]`; Column `dSemana[Data]` | `F:\BI N&A CONNECT\CHS001-000-PfM0-RT-002-01\Report\definition\pages\f7e070ab93ba4e1aa3bd\visuals\47c579e4486267ac53a8\visual.json` |
| `51e521cc0a893897b572` | tableEx | — | Column `LDP (4)[DISCIPLINE]`; Column `LDP (4)[DOCUMENT STATUS 90%]`; Column `LDP (4)[DOCUMENT TITLE]`; Column `LDP (4)[FORMAT]`; Column `LDP (4)[Fase 90%]`; Column `LDP (4)[ITEM]`; Column `LDP (4)[N&A DOCUMENT NUMBER]`; Column `LDP (4)[STATUS IN THE FLOW]`; Column `l[FORMAT]`; Measure `KPI[Adh]` | `F:\BI N&A CONNECT\CHS001-000-PfM0-RT-002-01\Report\definition\pages\f7e070ab93ba4e1aa3bd\visuals\51e521cc0a893897b572\visual.json` |
| `5ac83aca570deab8f0a0` | clusteredBarChart | DOCUMENTS BY DISCIPLINE | Aggregation `LDP (4)[Fase 90%]`; Column `EAP[Status in English Column]`; Column `EAP[Status]`; Column `LDP (4)[DISCIPLINE]`; Column `LDP (4)[Fase 90%]`; Column `LDP[STATUS GERAL]`; Column `Ricardo[Status Task]`; Column `Schedule[Status]`; Column `WBS[Status]` | `F:\BI N&A CONNECT\CHS001-000-PfM0-RT-002-01\Report\definition\pages\f7e070ab93ba4e1aa3bd\visuals\5ac83aca570deab8f0a0\visual.json` |
| `947d01e906caadf8bdd3` | cardVisual | — | Measure `LDP (4)[Cancelled]`; Measure `dFase Engenharia 90%[CANCELED 90]` | `F:\BI N&A CONNECT\CHS001-000-PfM0-RT-002-01\Report\definition\pages\f7e070ab93ba4e1aa3bd\visuals\947d01e906caadf8bdd3\visual.json` |
| `a2dbaffeffec04dd2797` | cardVisual | Total | Aggregation `LDP (4)[Fase 90%]`; Column `LDP (4)[Fase 90%]` | `F:\BI N&A CONNECT\CHS001-000-PfM0-RT-002-01\Report\definition\pages\f7e070ab93ba4e1aa3bd\visuals\a2dbaffeffec04dd2797\visual.json` |
| `c2ba931a184a07f0248a` | cardVisual | — | Measure `LDP (4)[In Review CHS]`; Measure `dFase Engenharia 90%[ADRESSING 90]` | `F:\BI N&A CONNECT\CHS001-000-PfM0-RT-002-01\Report\definition\pages\f7e070ab93ba4e1aa3bd\visuals\c2ba931a184a07f0248a\visual.json` |
| `f476e3ebfbfbac695fb4` | cardVisual | — | Measure `dFase Engenharia 90%[CLIENT REVIEW 90]` | `F:\BI N&A CONNECT\CHS001-000-PfM0-RT-002-01\Report\definition\pages\f7e070ab93ba4e1aa3bd\visuals\f476e3ebfbfbac695fb4\visual.json` |
| `f8355e32ed1ae94ee856` | clusteredColumnChart | SITUATION | Aggregation `LDP (4)[Fase 90%]`; Column `EAP[Status in English Column]`; Column `EAP[Status]`; Column `LDP (4)[DOCUMENT STATUS]`; Column `LDP (4)[Fase 90%]`; Column `LDP (4)[Personalizar.3]`; Column `LDP (4)[SITUATION 90%]`; Column `LDP (4)[SITUATION]`; Column `LDP[STATUS GERAL]`; Column `Ricardo[Status Task]`; Column `Schedule[Status]`; Column `WBS[Status]` | `F:\BI N&A CONNECT\CHS001-000-PfM0-RT-002-01\Report\definition\pages\f7e070ab93ba4e1aa3bd\visuals\f8355e32ed1ae94ee856\visual.json` |

### Inventário completo (ID, tipo e coordenadas)

| ID | Tipo | X | Y | L | A | Z |
|---|---|---:|---:|---:|---:|---:|
| `51e521cc0a893897b572` | tableEx | 203.8662613981763 | 829.4711246200608 | 1823.90273556231 | 272.3404255319149 | 0 |
| `2d376cb60446afce7f95` | clusteredColumnChart | 1597.5737138368895 | 101.61246236418378 | 430.91210891477937 | 395.1595758607147 | 1000 |
| `c49bcfa17fe49260c634` | shape | 216.3161094224924 | 93.37386018237082 | 210.09118541033433 | 722.0911854103343 | 2000 |
| `a2dbaffeffec04dd2797` | cardVisual | 240 | 128 | 160 | 88 | 3000 |
| `947d01e906caadf8bdd3` | cardVisual | 238.16666666666669 | 588.3333333333334 | 160 | 88 | 4000 |
| `291e9db4fa498824a995` | cardVisual | 240.3333333333334 | 474.58333333333337 | 160 | 88 | 5000 |
| `5ac83aca570deab8f0a0` | clusteredBarChart | 435.74468085106383 | 102.7112462006079 | 690.9665653495441 | 701.8601823708207 | 6000 |
| `f8355e32ed1ae94ee856` | clusteredColumnChart | 1140.3176331980626 | 103.49417463018719 | 438.438957978793 | 393.27786359471133 | 7000 |
| `f476e3ebfbfbac695fb4` | cardVisual | 240 | 248 | 160 | 88 | 8000 |
| `f6615ea6fa0929d68011` | shape | 698.417487684729 | 0 | 833.3333333333334 | 60 | 9000 |
| `b75b84f450819ffdc6f7` | image | 30.000000000000004 | 0 | 145.71428571428572 | 130 | 10000 |
| `e097b6cadab9b6781fc3` | shape | 5.929169359722366 | 231 | 190.85467980295564 | 81.59729064039408 | 11000 |
| `d5277d7701e2c12e627e` | shape | 5.929169359722366 | 323.48768472906403 | 190.85467980295564 | 81 | 12000 |
| `c2ba931a184a07f0248a` | cardVisual | 240 | 360 | 160 | 88 | 13000 |
| `7a245cd804bc0ca62f25` | image | 26.27709359605911 | 139.6834975369458 | 149.36453201970443 | 76.06527093596058 | 15000 |
| `44f9fe4389ee835438cb` | image | 212.98275862068965 | 5.532019704433497 | 156.2795566502463 | 84.36330049261083 | 16000 |
| `c32b72b3d0244684621d` | shape | 5.929169359722366 | 415.378078817734 | 190.85467980295564 | 81 | 17000 |
| `942cb7cdfe86afe7e521` | shape | 5.929169359722366 | 507.26847290640393 | 190.85467980295564 | 81 | 18000 |
| `868ef574c4d50ee9dce6` | shape | 5.929169359722366 | 599.1588669950738 | 190.85467980295564 | 81 | 19000 |
| `4efc9119da8a6d26a5df` | actionButton | 1940.3636363636363 | 21.818181818181817 | 59.63636363636363 | 49.45454545454545 | 20000 |
| `6c5f09bd0615f40da654` | shape | 5.929169359722366 | 874.8300492610837 | 190.85467980295564 | 81 | 21000 |
| `b03990dfd1f3b771d6ad` | shape | 5.237666896668173 | 782.9396551724137 | 192.23768472906403 | 81 | 22000 |
| `c3312b519c7b09e96b5a` | actionButton | 1855.9926108374384 | 15.213054187192117 | 62.23522167487684 | 66.38423645320196 | 23000 |
| `ec8ee308a8d45841b4dd` | actionButton | 241.51024305264744 | 689.4019665321027 | 155.88388415216335 | 100.99519254928893 | 24000 |
| `a4a0aedd369c2c7a9eb4` | shape | 5.929169359722366 | 691.0492610837438 | 190.85467980295564 | 81 | 25000 |
| `d60d1ff8048da7b60b98` | shape | 5.929169359722366 | 966.7204433497536 | 190.85467980295564 | 81 | 26000 |
| `2c9e8683b347b30efac7` | lineClusteredColumnComboChart | 1140.7173252279636 | 512 | 879.2705167173252 | 292.57142857142856 | 26001 |
| `47c579e4486267ac53a8` | slicer | 383.0684133915575 | 8.943231441048034 | 214.2857142857143 | 74.28571428571429 | 26002 |
| `2700efca3bbd0b7db3c7` | shape | 743.8784194528876 | 57.58054711246201 | 755 | 32 | 26003 |

## Programação

Página 17, ID `2511ad67d65b7a106a91`, 2048×1123; 23 visuais. Fonte: `F:\BI N&A CONNECT\CHS001-000-PfM0-RT-002-01\Report\definition\pages\2511ad67d65b7a106a91\page.json`.

Tipos: actionButton (2), cardVisual (4), clusteredColumnChart (1), image (3), shape (11), slicer (1), tableEx (1).

Sem filtro de página persistido; os quatro cards têm filtros próprios por `DOCUMENT STATUS`, e o slicer usa `WBX[Término]`.

### Visuais de dados

| ID | Tipo | Título | Campos/medidas | Evidência |
|---|---|---|---|---|
| `4f0d9cb46000a11e0c89` | tableEx | — | Aggregation `Assignment[Porcentagem de Trabalho Concluído]`; Column `Assignment[Porcentagem de Trabalho Concluído]`; Column `Programação Semanal[BASELINE DATE]`; Column `Programação Semanal[DISCIPLINE]`; Column `Programação Semanal[DOCUMENT STATUS]`; Column `Programação Semanal[DOCUMENT TITLE]`; Column `Programação Semanal[ITEM]`; Column `Programação Semanal[N&A DOCUMENT NUMBER]`; Column `Programação Semanal[PHASE]`; Column `Programação Semanal[REPLANNED DATE]`; Column `p[DOCUMENT STATUS]` | `F:\BI N&A CONNECT\CHS001-000-PfM0-RT-002-01\Report\definition\pages\2511ad67d65b7a106a91\visuals\4f0d9cb46000a11e0c89\visual.json` |
| `ac7de2b1978e37d8b854` | slicer | — | Column `WBX[Término]`; Column `w[Término]` | `F:\BI N&A CONNECT\CHS001-000-PfM0-RT-002-01\Report\definition\pages\2511ad67d65b7a106a91\visuals\ac7de2b1978e37d8b854\visual.json` |
| `bfc533e4a08b51000a29` | clusteredColumnChart | DOCUMENTOS/ATIVIDADES POR DISCIPLINA | Aggregation `Programação Semanal[DISCIPLINE]`; Column `Programação Semanal[DISCIPLINE]`; Column `Programação Semanal[DOCUMENT STATUS]`; Column `p[DOCUMENT STATUS]` | `F:\BI N&A CONNECT\CHS001-000-PfM0-RT-002-01\Report\definition\pages\2511ad67d65b7a106a91\visuals\bfc533e4a08b51000a29\visual.json` |
| `bfc7392ac34a6e919cb0` | cardVisual | NOT STARTED | Aggregation `Programação Semanal[DOCUMENT STATUS]`; Column `Programação Semanal[DOCUMENT STATUS]`; Column `p[DOCUMENT STATUS]` | `F:\BI N&A CONNECT\CHS001-000-PfM0-RT-002-01\Report\definition\pages\2511ad67d65b7a106a91\visuals\bfc7392ac34a6e919cb0\visual.json` |
| `c9849695b8d2eda0670b` | cardVisual | ELABORATION | Aggregation `Programação Semanal[DOCUMENT STATUS]`; Column `Programação Semanal[DOCUMENT STATUS]`; Column `p[DOCUMENT STATUS]` | `F:\BI N&A CONNECT\CHS001-000-PfM0-RT-002-01\Report\definition\pages\2511ad67d65b7a106a91\visuals\c9849695b8d2eda0670b\visual.json` |
| `d2c0e0e969040a07a905` | cardVisual | ACTIVITIES | Aggregation `Programação Semanal[ITEM]`; Column `Programação Semanal[DOCUMENT STATUS]`; Column `Programação Semanal[ITEM]`; Column `p[DOCUMENT STATUS]` | `F:\BI N&A CONNECT\CHS001-000-PfM0-RT-002-01\Report\definition\pages\2511ad67d65b7a106a91\visuals\d2c0e0e969040a07a905\visual.json` |
| `fdc158528e988a61dd22` | cardVisual | ISSUANCE FLOW | Aggregation `Programação Semanal[DOCUMENT STATUS]`; Column `Programação Semanal[DOCUMENT STATUS]`; Column `p[DOCUMENT STATUS]` | `F:\BI N&A CONNECT\CHS001-000-PfM0-RT-002-01\Report\definition\pages\2511ad67d65b7a106a91\visuals\fdc158528e988a61dd22\visual.json` |

### Inventário completo (ID, tipo e coordenadas)

| ID | Tipo | X | Y | L | A | Z |
|---|---|---:|---:|---:|---:|---:|
| `f5fe31f52ee1eb90a4b8` | shape | 724.6945812807882 | 0 | 818.3333333333334 | 56.66666666666667 | 0 |
| `69634f1fc291e586586b` | image | 30.000000000000004 | 0 | 145.71428571428572 | 130 | 1000 |
| `d5a12c1b2c2a58d3c731` | shape | 9.912855930372288 | 420.3636319129601 | 190.85467980295564 | 81 | 2000 |
| `cd5b02e3ab1c210d30ae` | image | 26.27709359605911 | 139.6834975369458 | 149.36453201970443 | 76.06527093596058 | 4000 |
| `6fea5d5f4da9d000a9e8` | image | 212.98275862068965 | 5.532019704433497 | 156.2795566502463 | 84.36330049261083 | 5000 |
| `06eb8e5929b5a3a35d0c` | actionButton | 1940.3636363636363 | 21.818181818181817 | 59.63636363636363 | 49.45454545454545 | 6000 |
| `6769d7116a5491018314` | actionButton | 1855.9926108374384 | 15.213054187192117 | 62.23522167487684 | 66.38423645320196 | 7000 |
| `4508ded5958552606075` | shape | 9.631989144920013 | 598.9936528024007 | 191.4164133738602 | 81 | 8000 |
| `08b2a7d8c1dc70cb90db` | shape | 10.410104646439777 | 509.67864235768036 | 189.86018237082067 | 81 | 9000 |
| `7cdca78622eb43353d85` | shape | 9.631989144920013 | 688.308663247121 | 191.4164133738602 | 81 | 10000 |
| `ac7de2b1978e37d8b854` | slicer | 1532.25768210417 | 106.58054719204085 | 12 | 22 | 11000 |
| `4f0d9cb46000a11e0c89` | tableEx | 247.45758851645965 | 322.08765489443954 | 1781.3018474954674 | 773.7959513927389 | 12000 |
| `d2c0e0e969040a07a905` | cardVisual | 247.45758851645965 | 133.5485398342798 | 200 | 120 | 13000 |
| `c9849695b8d2eda0670b` | cardVisual | 485.0954314568693 | 131.5845907190698 | 200 | 120 | 14000 |
| `bfc7392ac34a6e919cb0` | cardVisual | 724.6972235124889 | 131.5845907190698 | 200 | 120 | 15000 |
| `fdc158528e988a61dd22` | cardVisual | 958.4071682224786 | 133.5485398342798 | 202.2867588666297 | 119.80089602780983 | 16000 |
| `bfc533e4a08b51000a29` | clusteredColumnChart | 1327.629601881958 | 102.12535399091985 | 699.1658850147589 | 212.1065044426797 | 17000 |
| `b8f53ca06aee23e2a3e8` | shape | 9.631989144920013 | 777.6236736918413 | 191.4164133738602 | 81 | 18000 |
| `dcba45f75a01e97ab880` | shape | 9.631989144920013 | 866.9386841365616 | 191.4164133738602 | 81 | 19000 |
| `00568c7990caa85c0aee` | shape | 9.631989144920013 | 956.2536945812819 | 191.4164133738602 | 81 | 20000 |
| `7687c2c0211700762b10` | shape | 9.234117454016726 | 224.71533885733976 | 190.85467980295564 | 81.59729064039408 | 21000 |
| `ebf1dcba7a4cb56c9d4a` | shape | 9.912855930372288 | 331.04862146823984 | 190.85467980295564 | 81 | 22000 |
| `560bdf9a30717a6179aa` | shape | 743.8784194528876 | 57.58054711246201 | 755 | 32 | 22001 |

## Relacionamentos que afetam as dez páginas

| De | Para | Ativo | Cardinalidade | Direção |
|---|---|---:|---|---|
| `AssignmentByDay[TimeByDay]` | `dCalendario[Data]` | 1 | M:1 | Single |
| `AssignmentByDay[TaskUID]` | `Assignment[TaskUID]` | 1 | M:1 | Single |
| `AssignmentByDay[TaskUID]` | `WBX[NodeKey]` | 1 | M:1 | Single |
| `ResourceByDay[PROJ_ID]` | `Assignment[PROJ_ID]` | 1 | M:1 | Single |
| `AssignmentByDay Fonte 2[TaskUID]` | `Assignment Fonte 2[TaskUID]` | 1 | M:1 | Single |
| `AssignmentByDay Fonte 2[TaskUID]` | `WBX Fonte 2[NodeKey]` | 1 | M:1 | Single |
| `AssignmentByDay Fonte 2[TimeByDay]` | `dCalendario Fonte 2[Data]` | 1 | M:1 | Single |
| `ResourceByDay Fonte 2[PROJ_ID]` | `Assignment Fonte 2[PROJ_ID]` | 1 | M:1 | Single |
| `TaskByDay[Data]` | `MODELO FEDERADO[Data]` | 1 | M:1 | Single |
| `TaskByDay Fonte 2[Data]` | `MODELO FEDERADO[Data]` | 1 | M:1 | Single |
| `TaskByDay Fonte 3[Data]` | `MODELO FEDERADO[Data]` | 1 | M:1 | Single |
| `WBX[IsLeaf]` | `MODELO FEDERADO[IsLeaf]` | 1 | M:1 | Single |
| `WBX Fonte 2[IsLeaf]` | `MODELO FEDERADO[IsLeaf]` | 1 | M:1 | Single |
| `WBX Fonte 3[IsLeaf]` | `MODELO FEDERADO[IsLeaf]` | 1 | M:1 | Single |
| `Programação Semanal[N&A DOCUMENT NUMBER]` | `LDP (4)[N&A DOCUMENT NUMBER]` | 1 | 1:1 | Both |
| `Desvio de Qualidade[NÚMERO DO DOCUMENTO]` | `LDP (4)[N&A DOCUMENT NUMBER]` | 1 | M:1 | Single |

## Limites desta evidência

O PBIX não foi renderizado no Power BI Desktop nesta auditoria. Cores, coordenadas, fontes e conteúdos configurados estão comprovados pelo PBIR; a comparação pixel a pixel ainda exige captura do Desktop na mesma seleção de filtros da aplicação. Os valores de controle são do cache do PBIX e podem divergir legitimamente do banco atual por data de atualização.
