from pathlib import Path
import re
root=Path(r"C:\Dev\N-A-Connect aa\N-A-Connect\ClientApp\src\features\production")
tv=(root/"analytics/ResumoTvTestePage.tsx").read_text("utf-8")
css=(root/"analytics/resumoTvTeste.css").read_text("utf-8")
routes=(root/"productionRoutes.ts").read_text("utf-8")
normal=(root/"analytics/ResumoProducaoPage.tsx").read_text("utf-8")
tests={
"sem valores monetarios no jsx":not re.search(r'R\$|formatCurrency|toLocaleString\(.+currency|Intl\.NumberFormat',tv),
"sem conciliacao historica no novo painel":"HistoricalMeasurementPanel" not in tv,
"nome da disciplina renderizado uma vez por linha":tv.count('>{d.Disciplina}</div>')==1,
"percentual de custos com base prevista":'ratio(d.Custo_Realizado,d.Custo_Previsto)' in tv,
"percentual de custo do projeto":'ratio(r.custoRealizado,r.custoPrevisto)' in tv,
"sem scroll/paginado":"overflow:hidden" in css and 'totalPages' in tv and 'setInterval' in tv,
"botao fullscreen":'requestFullscreen()' in tv,
"tela testes fora sidebar":'slug: "resumo-tv-teste"' in routes and 'foraDoMenu: true' in routes.split('slug: "resumo-tv-teste"')[1][:250],
"tela com mesmas permissoes gerenciais":'"reports.gestao.view", "production.managerial.hh-custo.view"' in routes.split('slug: "resumo-tv-teste"')[1][:350],
"resumo normal preserva conciliacao":"<HistoricalMeasurementPanel reports={fr} />" in normal,
"resumo normal tem link do piloto":'rotaProducao("resumo-tv-teste")' in normal,
}
for label,status in tests.items():print(("PASS" if status else "FAIL"),label)
if not all(tests.values()):raise SystemExit(1)
