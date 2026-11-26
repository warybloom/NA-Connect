from pathlib import Path
p=Path(r"C:\Dev\N-A-Connect aa\N-A-Connect\ClientApp\src\features\production\analytics\ResumoProducaoPage.tsx")
s=p.read_text(encoding="utf-8-sig")
a='  const semanaTxt = av.inicioSemana && av.fimSemana ?'
assert s.count(a)==1
i=s.index(a)
j=s.index("\n",i)
s=s[:j]+'''
  const emitidosDoc = doc.cards.find(c => c.key === "emitidos");
  const canceladosDoc = doc.cards.find(c => c.key === "cancelados");
  const aEmitirDoc = doc.cards.find(c => c.key === "aEmitir");
'''+s[j:]
assert '      <HistoricalMeasurementPanel reports={fr} />' in s
s=s.replace('      <HistoricalMeasurementPanel reports={fr} />\n','',1)
start=s.index('      <Grupo titulo="Documentos">')
end=s.index('      {av.porFase.length > 0 && (',start)
replacement='''      {/* Faixas de leitura executiva: documento 3+3, HH 4, custo 4, avanço 2 e gráficos 2. */}
      <Grupo titulo="Documentos · Situação" colunas={3}>
        <Card icone={FileText} titulo={doc.total.label} valor={numero(doc.total)} cor="violet" para={ldpLink(doc.total)} />
        {emitidosDoc
          ? <CardDoc card={emitidosDoc} icone={ICONE_DOC.emitidos} cor={COR_DOC.emitidos} numero={numero} link={ldpLink} />
          : <Card icone={CheckCircle2} titulo="Emitidos" valor="—" cor="emerald" />}
        {canceladosDoc
          ? <CardDoc card={canceladosDoc} icone={ICONE_DOC.cancelados} cor={COR_DOC.cancelados} numero={numero} link={ldpLink} />
          : <Card icone={X} titulo="Cancelados" valor="—" cor="slate" />}
      </Grupo>
      <Grupo titulo="Documentos · Prazos e emissão" colunas={3}>
        {aEmitirDoc
          ? <CardDoc card={aEmitirDoc} icone={ICONE_DOC.aEmitir} cor={COR_DOC.aEmitir} numero={numero} link={ldpLink} />
          : <Card icone={Hourglass} titulo="A emitir" valor="—" cor="amber" />}
        <Card icone={AlertTriangle} titulo="Atrasados" valor={String(r.documentos.atrasados)} cor="rose" para={prazo("ATRASADO")} />
        <Card icone={Target} titulo="No prazo" valor={String(r.documentos.noPrazo)} cor="sky" para={prazo("NO_PRAZO")} />
      </Grupo>
      <Grupo titulo="Horas" colunas={4}>
        <Card icone={Gauge} titulo="Consumo HH" valor={pct(r.consumoHh)} cor="sky" para={financeiro} barra={r.consumoHh} />
        <Card icone={Clock} titulo="HH previsto" valor={formatHH(r.hhPrevisto)} cor="sky" para={financeiro} />
        <Card icone={Clock} titulo="HH realizado" valor={formatHH(r.hhRealizado)} cor="sky" para={financeiro} />
        <Card icone={Clock} titulo="Saldo HH" valor={formatHH(r.saldoHh)} cor="sky" para={financeiro} />
      </Grupo>
      <Grupo titulo="Custo" colunas={4}>
        <Card icone={Gauge} titulo="Consumo de custo" valor={pct(r.consumoCusto)} cor="emerald" para={financeiro} barra={r.consumoCusto}
          detalhe="Custo realizado ÷ custo previsto" />
        <Card icone={Wallet} titulo="Custo previsto" valor={formatCurrency(r.custoPrevisto)} cor="emerald" para={financeiro} />
        <Card icone={Coins} titulo="Custo realizado" valor={formatCurrency(r.custoRealizado)} cor="emerald" para={financeiro} />
        <Card icone={Coins} titulo="Saldo de custo" valor={formatCurrency(r.saldoCusto)} cor="emerald" para={financeiro} />
      </Grupo>
      <Grupo titulo="Avanço" colunas={2}>
        <Card icone={TrendingUp} titulo="Avanço previsto" valor={pct(av.previsto)} cor="amber" para={curvaS} barra={av.previsto}
          detalhe={semanaTxt ? "Corte: fim da semana vigente (" + semanaTxt + ")" : undefined} />
        <Card icone={TrendingUp} titulo="Avanço realizado" valor={pct(av.fisico)} cor="violet" para={curvaS} barra={av.fisico}
          detalhe={r.tiposLdp.includes(2) ? "Avanço físico: gates consolidados por documento" : "Avanço físico do projeto"} />
      </Grupo>
      <GraficosResumoDisciplinas reports={fr} />
      <HistoricalMeasurementPanel reports={fr} />
'''
s=s[:start]+replacement+s[end:]
old='''const Grupo = ({ titulo, children }: { titulo: string; children: ReactNode }) => (
  <section aria-label={titulo}>
    <h2 className="mb-2 text-[10px] font-black uppercase tracking-[0.25em] text-muted-foreground">{titulo}</h2>
    <div className="grid grid-cols-1 gap-3 sm:grid-cols-2 xl:grid-cols-4 2xl:grid-cols-5">{children}</div>
  </section>
);'''
new='''const Grupo = ({ titulo, children, colunas }: { titulo: string; children: ReactNode; colunas: 2 | 3 | 4 }) => (
  <section aria-label={titulo}>
    <h2 className="mb-2 text-[10px] font-black uppercase tracking-[0.25em] text-muted-foreground">{titulo}</h2>
    <div className={cn("grid grid-cols-1 gap-3 sm:grid-cols-2",
      colunas === 3 ? "xl:grid-cols-3" : colunas === 4 ? "xl:grid-cols-4" : "xl:grid-cols-2")}>{children}</div>
  </section>
);'''
assert old in s
s=s.replace(old,new,1)
p.write_text(s,encoding="utf-8")
print("RESUMO_LAYOUT_UPDATED",p)
