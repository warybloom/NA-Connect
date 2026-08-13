import { useState, useEffect, useMemo } from "react";
import { Save, AlertCircle, CheckCircle2, Edit, X , Trash2} from "lucide-react";
import { Norma } from "@/types/control-painel";
import { useParams } from "react-router-dom";
import { toast } from "react-hot-toast";

export default function FormHold() {
  const [projetos, setProjetos] = useState<any[]>([]);
  const [listaHolds, setListaHolds] = useState<any[]>([]);
  const [loading, setLoading] = useState(false);
  const [mensagem, setMensagem] = useState<{ tipo: 'sucesso' | 'erro', texto: string } | null>(null);
  const { projectId: urlProjectId } = useParams();
  const [projects, setProjects] = useState<any[]>([]);
  const [normas, setNormas] = useState<Norma[]>([]);
  const [templates, setTemplates] = useState<any[]>([]);
  const [pacotesOCR, setPacotesOCR] = useState<any[]>([]);
  const [savedConfigsLDP, setSavedConfigsLDP] = useState<any[]>([]);
  const [isEditing, setIsEditing] = useState(false);


  const [formData, setFormData] = useState({
    ID: null, ProjetoCodigo: "", Num_Hold: "", Status: "PENDENTE", Num_NA_Documento: "",
    Titulo_Documento: "", Revisao: "", Disciplina: "", Responsavel: "", Motivo_Hold: ""
  });
   
  const carregarDesvios = async (projetoCodigo?: string) =>{
    try
    {
      const filtro = projetoCodigo && projetoCodigo.trim() !== "" ? projetoCodigo : "TODOS"
      const res = await fetch(`http://localhost:5277/api/TimesheetReport/qualidade-contratos?projetoCodigo=${encodeURIComponent(filtro)}`);
      if (res.ok)
      {
        const json = await res.json();
        if (json.sucesso)
        {
          setListaHolds(json.dados?.holds?.tabela || []);
        }
      }

    }
    catch (erros)
    {
      console.error("Erro ao buscar holds")
    }
  }



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

  const handleChange = (e: any) => setFormData({ ...formData, [e.target.name]: e.target.value });

  const handleEditItem = (item: any) => {
    setFormData({
      ID: item.ID, ProjetoCodigo: item.ProjetoCodigo, Num_Hold: item.Num_Hold, Status: item.Status,
      Num_NA_Documento: item.Num_NA_Documento, Titulo_Documento: item.Titulo_Documento,
      Revisao: item.Revisao, Disciplina: item.Disciplina, Responsavel: item.Responsavel, Motivo_Hold: item.Motivo_Hold
    });
    setIsEditing(true); window.scrollTo({ top: 0, behavior: 'smooth' });
  };

    const handleExcluir = async (id: number, numDesvio: string) => {
    if (!window.confirm(`ATENÇÃO! Tem certeza que deseja EXCLUIR o desvio ${numDesvio}?`)) return;
    setMensagem(null);
    try {
      const res = await fetch(`http://localhost:5277/api/TimesheetReport/deletar-hold/${id}`, { method: "DELETE" });
      const json = await res.json();
      if (json.sucesso) {
        setMensagem({ tipo: 'sucesso', texto: json.mensagem });
        carregarDadosReais(); 
        carregarDesvios(formData.ProjetoCodigo);

        if (formData.ID === id) handleCancelarEdicao(); 
      } else { setMensagem({ tipo: 'erro', texto: json.mensagem }); }
    } catch (error) { setMensagem({ tipo: 'erro', texto: 'Erro de conexão ao tentar excluir.' }); }
  };

  const handleCancelarEdicao = () => {
    setIsEditing(false);
    setFormData({ ...formData, ID: null, Num_Hold: "", Num_NA_Documento: "", Titulo_Documento: "", Revisao: "", Responsavel: "", Motivo_Hold: "" });
  };

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!formData.ProjetoCodigo) { setMensagem({ tipo: 'erro', texto: 'Selecione um projeto!' }); return; }
    setLoading(true); setMensagem(null);
    const endpoint = isEditing ? "atualizar-hold" : "novo-hold";

    try {
      const response = await fetch(`http://localhost:5277/api/TimesheetReport/${endpoint}`, {
        method: "POST", headers: { "Content-Type": "application/json" }, body: JSON.stringify(formData)
      });
      const result = await response.json();
      if (result.sucesso) { setMensagem({ tipo: 'sucesso', texto: result.mensagem }); handleCancelarEdicao(); carregarDadosReais(); carregarDesvios(formData.ProjetoCodigo) } 
      else { setMensagem({ tipo: 'erro', texto: result.mensagem }); }
    } catch (error) { setMensagem({ tipo: 'erro', texto: 'Erro de conexão com o servidor.' }); } 
    finally { setLoading(false); }
  };

  return (
    <div className="w-full space-y-8 animate-enter">
      <form onSubmit={handleSubmit} className="w-full space-y-6 bg-card p-6 rounded-xl border border-border shadow-sm">
        <div>
          <h2 className="text-lg font-bold text-foreground border-b border-border pb-2 mb-4">
            {isEditing ? <span className="text-sky-500 flex items-center gap-2"><Edit className="w-5 h-5"/> Editando Hold: {formData.Num_Hold}</span> : "Novo Registro de Hold"}
          </h2>
        </div>

        {mensagem && (
          <div className={`p-4 rounded-lg flex items-center gap-3 font-medium text-sm ${mensagem.tipo === 'sucesso' ? 'bg-emerald-500/10 text-emerald-600 dark:text-emerald-400 border border-emerald-500/20' : 'bg-rose-500/10 text-rose-600 dark:text-rose-400 border border-rose-500/20'}`}>
            {mensagem.tipo === 'sucesso' ? <CheckCircle2 className="w-5 h-5"/> : <AlertCircle className="w-5 h-5"/>}
            {mensagem.texto}
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
            <label htmlFor="Num_Hold" className="block text-xs font-bold text-muted-foreground uppercase mb-1">Nº do Hold *</label>
            <input id="Num_Hold" type="text" name="Num_Hold" value={formData.Num_Hold} onChange={handleChange} required placeholder="Ex: 001" className="w-full bg-background border border-border rounded-md px-3 py-2 text-foreground text-sm focus:outline-none focus:border-sky-500 transition-colors" />
          </div>

          <div>
            <label htmlFor="StatusHold" className="block text-xs font-bold text-muted-foreground uppercase mb-1">Status Inicial</label>
            <select id="StatusHold" name="Status" title="Status" aria-label="Status Inicial" value={formData.Status} onChange={handleChange} className="w-full bg-background border border-border rounded-md px-3 py-2 text-foreground text-sm focus:outline-none focus:border-sky-500 font-bold transition-colors">
              <option value="PENDENTE" className="text-orange-500 dark:text-orange-400">PENDENTE</option>
              <option value="CONCLUÍDO" className="text-emerald-600 dark:text-emerald-500">CONCLUÍDO</option>
            </select>
          </div>

          <div>
            <label htmlFor="Num_NA_Documento" className="block text-xs font-bold text-muted-foreground uppercase mb-1">Cód. Documento N&A *</label>
            <input id="Num_NA_Documento" type="text" name="Num_NA_Documento" value={formData.Num_NA_Documento} onChange={handleChange} required placeholder="Ex: NA-PRO-001" className="w-full bg-background border border-border rounded-md px-3 py-2 text-foreground text-sm focus:outline-none focus:border-sky-500 transition-colors" />
          </div>

          <div className="lg:col-span-2">
            <label htmlFor="Titulo_Documento" className="block text-xs font-bold text-muted-foreground uppercase mb-1">Título do Documento *</label>
            <input id="Titulo_Documento" type="text" name="Titulo_Documento" value={formData.Titulo_Documento} onChange={handleChange} required placeholder="Ex: P&ID ÁREA 100" className="w-full bg-background border border-border rounded-md px-3 py-2 text-foreground text-sm focus:outline-none focus:border-sky-500 transition-colors" />
          </div>

          <div>
            <label htmlFor="Revisao" className="block text-xs font-bold text-muted-foreground uppercase mb-1">Revisão *</label>
            <input id="Revisao" type="text" name="Revisao" value={formData.Revisao} onChange={handleChange} required placeholder="Ex: 0 ou A" className="w-full bg-background border border-border rounded-md px-3 py-2 text-foreground text-sm focus:outline-none focus:border-sky-500 transition-colors" />
          </div>

          <div className="lg:col-span-2">
            <label htmlFor="DisciplinaHold" className="block text-xs font-bold text-muted-foreground uppercase mb-1">Disciplina *</label>
            <select id="DisciplinaHold" name="Disciplina" title="Disciplina" aria-label="Disciplina" value={formData.Disciplina} onChange={handleChange} required className="w-full bg-background border border-border rounded-md px-3 py-2 text-foreground text-sm focus:outline-none focus:border-sky-500 transition-colors">
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

          <div className="lg:col-span-2">
            <label htmlFor="Responsavel" className="block text-xs font-bold text-muted-foreground uppercase mb-1">Responsável *</label>
            <input id="Responsavel" type="text" name="Responsavel" value={formData.Responsavel} onChange={handleChange} required placeholder="Nome de quem deve resolver" className="w-full bg-background border border-border rounded-md px-3 py-2 text-foreground text-sm focus:outline-none focus:border-sky-500 transition-colors" />
          </div>
        </div>

        <div>
          <label htmlFor="Motivo_Hold" className="block text-xs font-bold text-muted-foreground uppercase mb-1">Motivo do Hold *</label>
          <textarea id="Motivo_Hold" name="Motivo_Hold" value={formData.Motivo_Hold} onChange={handleChange} required rows={3} placeholder="Descreva o que está travando o documento..." className="w-full bg-background border border-border rounded-md px-3 py-2 text-foreground text-sm focus:outline-none focus:border-sky-500 resize-none transition-colors"></textarea>
        </div>

        <div className="flex justify-end gap-3 pt-4 border-t border-border">
          {isEditing && (
            <button type="button" onClick={handleCancelarEdicao} className="flex items-center gap-2 bg-muted hover:bg-muted/80 text-foreground font-bold py-2.5 px-4 rounded-md transition-colors">
              <X className="w-5 h-5" /> Cancelar
            </button>
          )}
          <button type="submit" disabled={loading} className={`flex items-center gap-2 text-white font-bold py-2.5 px-8 rounded-md shadow-lg transition-colors disabled:opacity-50 ${isEditing ? 'bg-sky-600 hover:bg-sky-500' : 'bg-sky-500 hover:bg-sky-400'}`}>
            {loading ? <span className="animate-spin text-xl">↻</span> : <Save className="w-5 h-5" />}
            {loading ? 'Processando...' : (isEditing ? 'Atualizar Hold' : 'Salvar Novo Hold')}
          </button>
        </div>
      </form>

      {/* TABELA DE GESTÃO */}
      <div className="w-full rounded-xl border border-border bg-card overflow-hidden shadow-lg">
        <div className="p-4 bg-muted/50 border-b border-border">
          <h3 className="font-bold text-foreground text-sm">Histórico de Holds Cadastrados</h3>
        </div>
        <div className="overflow-x-auto custom-scrollbar">
          <table className="w-full text-xs text-left whitespace-nowrap">
            <thead className="bg-muted text-muted-foreground">
              <tr>
                <th className="px-4 py-3 w-16 text-center">Ação</th>
                <th className="px-4 py-3 font-medium">Nº Hold</th>
                <th className="px-4 py-3 font-medium">Documento</th>
                <th className="px-4 py-3 font-medium">Projeto</th>
                <th className="px-4 py-3 font-medium">Responsável</th>
                <th className="px-4 py-3 font-medium text-center">Status</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-border">
              {listaHolds.map((h: any, idx: number) => (
                <tr key={idx} className="hover:bg-muted/50 transition-colors">
                  <td className="px-4 py-2 text-center">
                    <button onClick={() => handleEditItem(h)} className="p-1.5 bg-sky-500/10 text-sky-600 dark:text-sky-400 rounded hover:bg-sky-500 hover:text-white transition-colors" title="Editar este Hold">
                      <Edit className="w-4 h-4" />
                    </button>
                    <button onClick={() => handleExcluir(h.ID, h.Num_Hold)} className="p-1.5 bg-rose-500/10 text-rose-600 dark:text-rose-400 rounded hover:bg-rose-500 hover:text-white transition-colors" title="Excluir" aria-label="Excluir"><Trash2 className="w-4 h-4" /></button>
                  </td>
                  <td className="px-4 py-2 font-bold text-sky-600 dark:text-sky-500">{h.Num_Hold}</td>
                  <td className="px-4 py-2">
                    <div className="font-bold text-foreground">{h.Titulo_Documento} <span className="text-muted-foreground text-[10px] font-normal">({h.Revisao})</span></div>
                    <div className="text-[10px] text-muted-foreground">{h.Num_NA_Documento} • {h.Disciplina}</div>
                  </td>
                  <td className="px-4 py-2 text-foreground">{h.ProjetoCodigo}</td>
                  <td className="px-4 py-2 text-muted-foreground">{h.Responsavel}</td>
                  <td className="px-4 py-2 text-center">
                    <span className="bg-background border border-border px-2 py-1 rounded text-[10px] font-bold" style={{color: h.Status === 'PENDENTE' ? '#f97316' : '#10b981'}}>{h.Status}</span>
                  </td>
                </tr>
              ))}
              {listaHolds.length === 0 && (
                <tr><td colSpan={6} className="text-center py-6 text-muted-foreground">Nenhum Hold cadastrado.</td></tr>
              )}
            </tbody>
          </table>
        </div>
      </div>
    </div>
  );
}