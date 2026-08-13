import { useState, useEffect, useMemo } from "react";
import { Save, AlertCircle, CheckCircle2, Edit, X, Trash2 } from "lucide-react";
import { Norma } from "@/types/control-painel";
import { useParams } from "react-router-dom";
import { toast } from "react-hot-toast";

export default function FormDesvio() {
  const [projetos, setProjetos] = useState<any[]>([]);
  const [listaDesvios, setListaDesvios] = useState<any[]>([]);
  const [loading, setLoading] = useState(false);
  const [mensagem, setMensagem] = useState<{ tipo: 'sucesso' | 'erro', texto: string } | null>(null);
    const { projectId: urlProjectId } = useParams();
  const [isSaving, setIsSaving] = useState(false);
  const [isLoadingSchema, setIsLoadingSchema] = useState(false);
  
  const [selectedProjectId, setSelectedProjectId] = useState<string>(urlProjectId || "");
  const [isLoading, setIsLoading] = useState(false);
     const [projects, setProjects] = useState<any[]>([]);
      const [normas, setNormas] = useState<Norma[]>([]);
      const [templates, setTemplates] = useState<any[]>([]);
      const [searchTerm, setSearchTerm] = useState("");
      const [activeTab, setActiveTab] = useState("projetos");
      
      const [isNewOCRDocOpen, setIsNewOCRDocOpen] = useState(false);
      const [newOCRName, setNewOCRName] = useState("");
       const [pacotesOCR, setPacotesOCR] = useState<any[]>([]);
       const [savedConfigsLDP, setSavedConfigsLDP] = useState<any[]>([]);
  
  const [isEditing, setIsEditing] = useState(false);

  const [formData, setFormData] = useState({
    ID: null,
    ProjetoCodigo: "",
    Num_Desvio: "",
    Disciplina: "",
    Data_Registro: new Date().toISOString().split('T')[0],
    Quant_HH: 0,
    Status: "EM ANÁLISE",
    Desvio_Interno: "",
    Change_Order: "",
    Descricao: "",
    Valor_CO: 0,
    Tipo_Desvio: "INTERNO",
    Categoria_Motivo: "",
    Impacto: ""
  });

  const carregarDesvios = async (projetoCodigo?: string) => {
    try
    {
      const filtro = projetoCodigo && projetoCodigo.trim() !== "" ? projetoCodigo : "TODOS";
      const res = await fetch(`http://localhost:5277/api/TimesheetReport/qualidade-contratos?projetoCodigo=${encodeURIComponent(filtro)}`);
      if (res.ok) {
        const json = await res.json();
        if (json.sucesso){
          setListaDesvios(json.dados?.desvios?.tabela || []);
        }
      }
    }
    catch (error)
    {
      console.error("Erro ao buscar desvios:", error);
    }

  }

  
   const fetchNormas = async () => {
      try {
        const resN = await fetch('/api/admin/normas');
        if (resN.ok) setNormas(await resN.json());
      } catch (error) {
        console.error("Erro ao buscar normas:", error);
      }
    };
  
    const carregarDadosReais = async () => {
      setLoading(true);
      try {
        // 1. Recupera o HubID do LocalStorage ou endpoint
        let hubId = localStorage.getItem("hubId");
        if (hubId === null) {
          try {
            const resHubs = await fetch('/DataManagement/Hubs');
            if (resHubs.ok) {
              const hubsData = await resHubs.json();
              const hubs = Array.isArray(hubsData) ? hubsData : (hubsData.data || []);
              if (hubs.length > 0) {
                hubId = hubs[0].id;
                if (hubId !== null) localStorage.setItem("hubId", hubId);
              }
            }
          } catch (e) {
            console.error("Erro ao buscar hubs:", e);
          }
        }
  
        // 2. Chamadas paralelas
        const [resP, resN, resT, resOCR, resLDP, resACC] = await Promise.all([
          fetch('/api/admin/projetos'),
          fetch('/api/admin/normas'),
          fetch('/api/admin/templates'),
          fetch('/api/admin/ocr/pacotes'),
          fetch('/api/admin/projeto/listar-configuracoes-ldp'),
          hubId ? fetch(`/DataManagement/Data/Projetos?hubId=${hubId}`) : Promise.resolve(null)
        ]);
  
        let projetosBanco: any[] = [];
        if (resP.ok) {
          const dadosVindosDoBanco = await resP.json();
          projetosBanco = dadosVindosDoBanco.map((p: any) => ({
            ...p,
            is_Ativo: Boolean(p.is_Ativo)
          }));
        }
  
        let projetosACC: any[] = [];
        if (resACC && resACC.ok) {
          const text = await resACC.text();
          if (text && text.trim().length > 0) {
            const jsonACC = JSON.parse(text);
            projetosACC = Array.isArray(jsonACC) ? jsonACC : (jsonACC.data || []);
          }
        }
  
        // 3. Mesclagem entre ACC e Banco Local
        if (projetosACC.length > 0) {
          const projetosUnificados = projetosACC.map((accProj: any) => {
            const accId = accProj.id;
            const accName = accProj.attributes?.name || accProj.name || "Sem Nome";
  
            const matchBanco = projetosBanco.find(
              (p: any) => p.project_ID_ACC === accId || String(p.projetoId) === String(accId)
            );
  
            if (matchBanco) return matchBanco;
  
            return {
              projetoId: null,
              nome_Projeto: accName,
              project_ID_ACC: accId,
              is_Ativo: false,
              NormaID: null,
              PacoteOCRID: null,
              configId: "none"
            };
          });
  
          projetosBanco.forEach((pBanco: any) => {
            const jaExiste = projetosUnificados.some(
              (u: any) => u.project_ID_ACC === pBanco.project_ID_ACC || u.projetoId === pBanco.projetoId
            );
            if (!jaExiste) projetosUnificados.push(pBanco);
          });
  
          setProjects(projetosUnificados);
        } else {
          setProjects(projetosBanco);
        }
  
        if (resN && resN.ok) setNormas(await resN.json());
        if (resT && resT.ok) setTemplates(await resT.json());
        if (resOCR && resOCR.ok) setPacotesOCR(await resOCR.json());
        if (resLDP && resLDP.ok) setSavedConfigsLDP(await resLDP.json());
  
      } catch (error) {
        toast.error("Erro ao conectar com o servidor.");
      } finally {
        setLoading(false);
      }
    };
  
    useEffect(() => {
      carregarDadosReais();
      carregarDesvios();
    }, []);
  
    const filteredProjects = useMemo(
      () => projects.filter((p) => p.nome_Projeto?.toLowerCase().includes(searchTerm.toLowerCase())),
      [projects, searchTerm]
    );

  const handleChange = (e: any) => setFormData({ ...formData, [e.target.name]: e.target.value });

  const handleEditItem = (item: any) => {
    setFormData({
      ID: item.ID, ProjetoCodigo: item.ProjetoCodigo, Num_Desvio: item.Num_Desvio,
      Disciplina: item.Disciplina, Data_Registro: item.Data_Registro.split('T')[0],
      Quant_HH: item.Quant_HH, Status: item.Status, Desvio_Interno: item.Desvio_Interno || "",
      Change_Order: item.Change_Order || "", Descricao: item.Descricao,
      Valor_CO: item.Valor_CO || 0, Tipo_Desvio: item.Tipo_Desvio || "INTERNO",
      Categoria_Motivo: item.Categoria_Motivo || "", Impacto: item.Impacto || ""
    });
    setIsEditing(true);
    window.scrollTo({ top: 0, behavior: 'smooth' });
  };

  const handleCancelarEdicao = () => {
    setIsEditing(false);
    setFormData({ ...formData, ID: null, Num_Desvio: "", Descricao: "", Quant_HH: 0, Desvio_Interno: "", Change_Order: "", Valor_CO: 0 });
  };

  const handleExcluir = async (id: number, numDesvio: string) => {
    if (!window.confirm(`ATENÇÃO! Tem certeza que deseja EXCLUIR o desvio ${numDesvio}?`)) return;
    setMensagem(null);
    try {
      const res = await fetch(`http://localhost:5277/api/TimesheetReport/deletar-desvio/${id}`, { method: "DELETE" });
      const json = await res.json();
      if (json.sucesso) {
        setMensagem({ tipo: 'sucesso', texto: json.mensagem });
        carregarDadosReais(); 
        carregarDesvios(formData.ProjetoCodigo);

        if (formData.ID === id) handleCancelarEdicao(); 
      } else { setMensagem({ tipo: 'erro', texto: json.mensagem }); }
    } catch (error) { setMensagem({ tipo: 'erro', texto: 'Erro de conexão ao tentar excluir.' }); }
  };

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!formData.ProjetoCodigo) { setMensagem({ tipo: 'erro', texto: 'Selecione um projeto!' }); return; }

    setLoading(true); setMensagem(null);
    const endpoint = isEditing ? "atualizar-desvio" : "novo-desvio";

    // Limpa campos que não devem ir dependendo do Tipo (UX inteligente)
    const payload = { ...formData };
    if (payload.Tipo_Desvio === "INTERNO") { payload.Change_Order = ""; payload.Valor_CO = 0; }
    if (payload.Tipo_Desvio === "EXTERNO") { payload.Desvio_Interno = ""; }

    try {
      const response = await fetch(`http://localhost:5277/api/TimesheetReport/${endpoint}`, {
        method: "POST", headers: { "Content-Type": "application/json" }, body: JSON.stringify(payload)
      });
      const result = await response.json();
      if (result.sucesso) {
        setMensagem({ tipo: 'sucesso', texto: result.mensagem });
        handleCancelarEdicao(); carregarDadosReais();carregarDesvios(formData.ProjetoCodigo);
      } else { setMensagem({ tipo: 'erro', texto: result.mensagem }); }
    } catch (error) { setMensagem({ tipo: 'erro', texto: 'Erro de conexão.' }); } finally { setLoading(false); }
  };

  return (
    <div className="w-full space-y-8 animate-enter">
      <form onSubmit={handleSubmit} className="w-full space-y-6 bg-card p-6 rounded-xl border border-border shadow-sm">
        <div>
          <h2 className="text-lg font-bold text-foreground border-b border-border pb-2 mb-4">
            {isEditing ? <span className="text-amber-500 flex items-center gap-2"><Edit className="w-5 h-5"/> Editando: {formData.Num_Desvio}</span> : "Novo Desvio de Escopo"}
          </h2>
        </div>

        {mensagem && (
          <div className={`p-4 rounded-lg flex items-center gap-3 font-medium text-sm ${mensagem.tipo === 'sucesso' ? 'bg-emerald-500/10 text-emerald-600 dark:text-emerald-400 border border-emerald-500/20' : 'bg-rose-500/10 text-rose-600 dark:text-rose-400 border border-rose-500/20'}`}>
            {mensagem.tipo === 'sucesso' ? <CheckCircle2 className="w-5 h-5"/> : <AlertCircle className="w-5 h-5"/>}{mensagem.texto}
          </div>
        )}

        <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-4 gap-5">
          <div className="lg:col-span-2">
            <label htmlFor="ProjetoCodigo" className="block text-xs font-bold text-muted-foreground uppercase mb-1">Projeto Vinculado *</label>
                        <select id="projectSelector4D"  name="ProjetoCodigo" title="Selecione o Projeto" value={formData.ProjetoCodigo} onChange={handleChange} className="  block  font-bold text-muted-foreground uppercase w-full bg-slate-800 border border-slate-600 rounded-md px-3 py-2 text-slate-200 text-sm focus:outline-none focus:border-amber-500 transition-colors">
              <option value="">-- Escolha um Projeto --</option>
              {projects.map((proj) => (
                <option key={proj.project_ID_ACC ?? proj.projectId} value={proj.project_ID_ACC ?? proj.projectId}>
                  {proj.nome_Projeto}
                </option>
              ))}
            </select>
          </div>

          <div>
            <label htmlFor="Num_Desvio" className="block text-xs font-bold text-muted-foreground uppercase mb-1">Nº do Desvio *</label>
            <input id="Num_Desvio" type="text" name="Num_Desvio" title="Número do Desvio" aria-label="Número do Desvio" value={formData.Num_Desvio} onChange={handleChange} required placeholder="Ex: DEV-004" className="w-full bg-background border border-border rounded-md px-3 py-2 text-foreground text-sm focus:outline-none focus:border-amber-500 transition-colors" />
          </div>

          <div>
            <label htmlFor="Data_Registro" className="block text-xs font-bold text-muted-foreground uppercase mb-1">Data Registro *</label>
            <input id="Data_Registro" type="date" name="Data_Registro" title="Data de Registro" aria-label="Data de Registro" value={formData.Data_Registro} onChange={handleChange} required className="w-full bg-background border border-border rounded-md px-3 py-2 text-foreground text-sm focus:outline-none focus:border-amber-500 transition-colors" />
          </div>

          {/* NOVOS CAMPOS ESTRATÉGICOS */}
          <div>
            <label htmlFor="Tipo_Desvio" className="block text-xs font-bold text-muted-foreground uppercase mb-1">Tipo de Desvio *</label>
            <select id="Tipo_Desvio" name="Tipo_Desvio" title="Tipo de Desvio" aria-label="Tipo de Desvio" value={formData.Tipo_Desvio} onChange={handleChange} required className="w-full bg-background border border-border rounded-md px-3 py-2 text-amber-600 dark:text-amber-400 font-bold text-sm focus:outline-none focus:border-amber-500 transition-colors">
              <option value="INTERNO">INTERNO (Retrabalho)</option>
              <option value="EXTERNO">EXTERNO (Change Order Cliente)</option>
            </select>
          </div>

          <div>
            <label htmlFor="Categoria_Motivo" className="block text-xs font-bold text-muted-foreground uppercase mb-1">Motivo Principal *</label>
            <select id="Categoria_Motivo" name="Categoria_Motivo" title="Motivo Principal" aria-label="Motivo Principal" value={formData.Categoria_Motivo} onChange={handleChange} required className="w-full bg-background border border-border rounded-md px-3 py-2 text-foreground text-sm focus:outline-none focus:border-amber-500 transition-colors">
              <option value="">Selecione...</option>
              <option value="Erro de Orçamento">Erro de Orçamento</option>
              <option value="Escopo não Previsto">Escopo não Previsto (Omissão)</option>
              <option value="Alteração do Cliente">Alteração Solicitada pelo Cliente</option>
              <option value="Erro de Engenharia">Erro Interno da Engenharia (N&A)</option>
              <option value="Falta de Dados">Falta de Dados / Premissas</option>
            </select>
          </div>

          <div>
            <label htmlFor="Impacto" className="block text-xs font-bold text-muted-foreground uppercase mb-1">Impacto Previsto *</label>
            <select id="Impacto" name="Impacto" title="Impacto Previsto" aria-label="Impacto Previsto" value={formData.Impacto} onChange={handleChange} required className="w-full bg-background border border-border rounded-md px-3 py-2 text-foreground text-sm focus:outline-none focus:border-amber-500 transition-colors">
              <option value="">Selecione...</option>
              <option value="Apenas Custo (HH)">Apenas Custo (HH)</option>
              <option value="Apenas Prazo">Apenas Prazo</option>
              <option value="Custo e Prazo">Custo e Prazo</option>
              <option value="Afeta Outras Disciplinas">Impacta Outras Disciplinas</option>
            </select>
          </div>

          <div>
            <label htmlFor="Status" className="block text-xs font-bold text-muted-foreground uppercase mb-1">Status</label>
            <select id="Status" name="Status" title="Status" aria-label="Status" value={formData.Status} onChange={handleChange} className="w-full bg-background border border-border rounded-md px-3 py-2 text-foreground text-sm focus:outline-none focus:border-amber-500 font-bold transition-colors">
              <option value="EM ANÁLISE" className="text-amber-500">EM ANÁLISE</option>
              <option value="APROVADA" className="text-emerald-500">APROVADA</option>
              <option value="REPROVADA" className="text-rose-500">REPROVADA</option>
            </select>
          </div>

          <div>
            <label htmlFor="Disciplina" className="block text-xs font-bold text-muted-foreground uppercase mb-1">Disciplina Origem</label>
            <select id="Disciplina" name="Disciplina" title="Disciplina Origem" aria-label="Disciplina Origem" value={formData.Disciplina} onChange={handleChange} className="w-full bg-background border border-border rounded-md px-3 py-2 text-foreground text-sm focus:outline-none focus:border-amber-500 transition-colors">
              <option value="">Selecione...</option>
              <option value="TUBULAÇÃO">Tubulação</option>
              <option value="MECÂNICA">Mecânica</option>
              <option value="CIVIL">Civil</option>
              <option value="ESTRUTURA METÁLICA">Estrutura Metálica</option>
              <option value="ELÉTRICA">Elétrica</option>
              <option value="INSTRUMENTAÇÃO">Instrumentação</option>
              <option value="PROCESSOS">Processos</option>
            </select>
          </div>

          <div>
            <label htmlFor="Quant_HH" className="block text-xs font-bold text-muted-foreground uppercase mb-1">Esforço (HH)</label>
            <input id="Quant_HH" type="number" step="0.01" name="Quant_HH" title="Esforço em HH" aria-label="Esforço em HH" value={formData.Quant_HH} onChange={handleChange} className="w-full bg-background border border-border rounded-md px-3 py-2 text-foreground text-sm focus:outline-none focus:border-amber-500 transition-colors" />
          </div>

          {/* CAMPOS CONDICIONAIS */}
          {formData.Tipo_Desvio === "INTERNO" ? (
            <div className="lg:col-span-2">
              <label htmlFor="Desvio_Interno" className="block text-xs font-bold text-muted-foreground uppercase mb-1">Código de Erro Interno</label>
              <input id="Desvio_Interno" type="text" name="Desvio_Interno" title="Código de Erro Interno" aria-label="Código de Erro Interno" value={formData.Desvio_Interno} onChange={handleChange} placeholder="Referência interna do erro..." className="w-full bg-background border border-border rounded-md px-3 py-2 text-foreground text-sm focus:outline-none focus:border-amber-500 transition-colors" />
            </div>
          ) : (
            <>
              <div>
                <label htmlFor="Change_Order" className="block text-xs font-bold text-emerald-600 dark:text-emerald-400 uppercase mb-1">Cód. Change Order</label>
                <input id="Change_Order" type="text" name="Change_Order" title="Código Change Order" aria-label="Código Change Order" value={formData.Change_Order} onChange={handleChange} placeholder="Ex: CO-001" className="w-full bg-background border border-emerald-500/50 rounded-md px-3 py-2 text-emerald-600 dark:text-emerald-300 text-sm focus:outline-none focus:border-emerald-500 transition-colors" />
              </div>
              <div>
                <label htmlFor="Valor_CO" className="block text-xs font-bold text-emerald-600 dark:text-emerald-400 uppercase mb-1">Valor do Pleito (R$)</label>
                <input id="Valor_CO" type="number" step="0.01" name="Valor_CO" title="Valor do Pleito" aria-label="Valor do Pleito" value={formData.Valor_CO} onChange={handleChange} placeholder="0.00" className="w-full bg-background border border-emerald-500/50 rounded-md px-3 py-2 text-emerald-600 dark:text-emerald-300 text-sm focus:outline-none focus:border-emerald-500 transition-colors" />
              </div>
            </>
          )}
        </div>

        <div>
          <label htmlFor="Descricao" className="block text-xs font-bold text-muted-foreground uppercase mb-1">Descrição Detalhada do Fato *</label>
          <textarea id="Descricao" name="Descricao" title="Descrição Detalhada" aria-label="Descrição Detalhada" value={formData.Descricao} onChange={handleChange} required rows={3} placeholder="Descreva tecnicamente o que motivou a mudança de escopo..." className="w-full bg-background border border-border rounded-md px-3 py-2 text-foreground text-sm focus:outline-none focus:border-amber-500 resize-none transition-colors"></textarea>
        </div>

        <div className="flex justify-end gap-3 pt-4 border-t border-border">
          {isEditing && (
            <button type="button" onClick={handleCancelarEdicao} className="flex items-center gap-2 bg-muted hover:bg-muted/80 text-foreground font-bold py-2.5 px-4 rounded-md transition-colors"><X className="w-5 h-5" /> Cancelar</button>
          )}
          <button type="submit" disabled={loading} className={`flex items-center gap-2 text-white font-bold py-2.5 px-8 rounded-md shadow-lg transition-colors disabled:opacity-50 ${isEditing ? 'bg-sky-600 hover:bg-sky-500' : 'bg-sky-500 hover:bg-sky-400'}`}>
            {loading ? <span className="animate-spin text-xl">↻</span> : <Save className="w-5 h-5" />}
            {loading ? 'Processando...' : (isEditing ? 'Atualizar Desvio' : 'Salvar Novo Desvio')}
          </button>
        </div>
      </form>

      {/* TABELA DE GESTÃO E EXCLUSÃO */}
      <div className="w-full rounded-xl border border-border bg-card overflow-hidden shadow-lg">
        <div className="p-4 bg-muted/50 border-b border-border"><h3 className="font-bold text-foreground text-sm">Histórico de Desvios Cadastrados</h3></div>
        <div className="overflow-x-auto custom-scrollbar">
          <table className="w-full text-xs text-left whitespace-nowrap">
            <thead className="bg-muted text-muted-foreground">
              <tr>
                <th className="px-4 py-3 w-24 text-center">Ações</th>
                <th className="px-4 py-3 font-medium">Nº Desvio</th>
                <th className="px-4 py-3 font-medium">Tipo</th>
                <th className="px-4 py-3 font-medium">Categoria / Motivo</th>
                <th className="px-4 py-3 font-medium">Status</th>
                <th className="px-4 py-3 font-medium w-full">Descrição</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-border">
              {listaDesvios.map((d: any, idx: number) => (
                <tr key={idx} className="hover:bg-muted/50 transition-colors">
                  <td className="px-4 py-2 flex items-center justify-center gap-2">
                    <button onClick={() => handleEditItem(d)} className="p-1.5 bg-sky-500/10 text-sky-600 dark:text-sky-400 rounded hover:bg-sky-500 hover:text-white transition-colors" title="Editar" aria-label="Editar"><Edit className="w-4 h-4" /></button>
                    <button onClick={() => handleExcluir(d.ID, d.Num_Desvio)} className="p-1.5 bg-rose-500/10 text-rose-600 dark:text-rose-400 rounded hover:bg-rose-500 hover:text-white transition-colors" title="Excluir" aria-label="Excluir"><Trash2 className="w-4 h-4" /></button>
                  </td>
                  <td className="px-4 py-2 font-bold text-amber-600 dark:text-amber-500">{d.Num_Desvio}</td>
                  <td className="px-4 py-2"><span className="bg-background px-2 py-1 border border-border rounded text-foreground font-bold">{d.Tipo_Desvio}</span></td>
                  <td className="px-4 py-2 text-foreground font-bold">{d.Categoria_Motivo || '-'}</td>
                  <td className="px-4 py-2"><span className="bg-background border border-border px-2 py-1 rounded text-[10px] font-bold" style={{color: d.Status === 'EM ANÁLISE' ? '#eab308' : d.Status === 'APROVADA' ? '#10b981' : '#ef4444'}}>{d.Status}</span></td>
                  <td className="px-4 py-2 text-muted-foreground truncate max-w-[400px]" title={d.Descricao}>{d.Descricao}</td>
                </tr>
              ))}
              {listaDesvios.length === 0 && <tr><td colSpan={6} className="text-center py-6 text-muted-foreground">Nenhum desvio cadastrado.</td></tr>}
            </tbody>
          </table>
        </div>
      </div>
    </div>
  );
}