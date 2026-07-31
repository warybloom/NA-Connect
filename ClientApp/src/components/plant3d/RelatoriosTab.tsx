import { useEffect, useState } from "react";

export default function RelatoriosTab() {
  const [kpis, setKpis] = useState({
    hh_previsto: 0,
    hh_realizado: 0,
    custo_previsto: 0,
    custo_realizado: 0,
    spi: 0,
    cpi: 0
  });
  const [loading, setLoading] = useState(true);

  // ID do projeto fixo para o MVP (depois você vincula ao combobox do sistema)
  const projectId = "b.c3e20d9d-779e-423c-9381-abec38cf11cb"; 

  useEffect(() => {
    const fetchKpis = async () => {
      try {
        setLoading(true);
        const response = await fetch(`http://localhost:5277/api/DashboardGen/kpis?projectId=${projectId}`);
        const result = await response.json();
        
        if (result.sucesso) {
          setKpis(result.dados);
        }
      } catch (error) {
        console.error("Erro ao buscar KPIs:", error);
      } finally {
        setLoading(false);
      }
    };

    fetchKpis();
  }, [projectId]);

  const formatarNumero = (num: number) => {
    return new Intl.NumberFormat('pt-BR', { minimumFractionDigits: 2, maximumFractionDigits: 2 }).format(num);
  };

  return (
    <div className="grid gap-4 md:grid-cols-2 lg:grid-cols-4 p-4">
      {/* CARD 1: HH PREVISTO */}
      <div className="rounded-xl border bg-card text-card-foreground shadow p-6">
        <h3 className="tracking-tight text-sm font-medium text-muted-foreground">HH Previsto</h3>
        <div className="text-2xl font-bold mt-2">
          {loading ? "..." : formatarNumero(kpis.hh_previsto)} h
        </div>
      </div>

      {/* CARD 2: HH REALIZADO */}
      <div className="rounded-xl border bg-card text-card-foreground shadow p-6">
        <h3 className="tracking-tight text-sm font-medium text-muted-foreground">HH Realizado</h3>
        <div className="text-2xl font-bold mt-2">
          {loading ? "..." : formatarNumero(kpis.hh_realizado)} h
        </div>
      </div>

      {/* CARD 3: SPI */}
      <div className="rounded-xl border bg-card text-card-foreground shadow p-6">
        <h3 className="tracking-tight text-sm font-medium text-muted-foreground">SPI (Prazo)</h3>
        <div className={`text-2xl font-bold mt-2 ${kpis.spi >= 1 ? "text-green-500" : "text-red-500"}`}>
          {loading ? "..." : kpis.spi}
        </div>
      </div>

      {/* CARD 4: CPI */}
      <div className="rounded-xl border bg-card text-card-foreground shadow p-6">
        <h3 className="tracking-tight text-sm font-medium text-muted-foreground">CPI (Custo)</h3>
        <div className={`text-2xl font-bold mt-2 ${kpis.cpi >= 1 ? "text-green-500" : "text-red-500"}`}>
          {loading ? "..." : kpis.cpi}
        </div>
      </div>
    </div>
  );
}