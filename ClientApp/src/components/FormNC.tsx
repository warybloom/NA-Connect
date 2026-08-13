import { useState, useEffect, useMemo } from "react";
import { Save, AlertCircle, CheckCircle2, Edit, X, Trash2 } from "lucide-react";
import { Norma } from "@/types/control-painel";
import { useParams } from "react-router-dom";
import {toast} from "react-hot-toast"

export default function FormNC() {
  const [projetos, setProjetos] = useState<any[]>([]);
  const [listaNCs, setListaNCs] = useState<any[]>([]);
  const [loading, setLoading] = useState(false);
  const [mensagem, setMensagem] = useState<{ tipo: 'sucesso' | 'erro', texto: string } | null>(null);
  const [projects, setProjects] = useState<any[]>([]);
  const [normas, setNormas] = useState<Norma[]>([]);
  const [templates, setTemplates] = useState<any[]>([]);
  const [pacotesOCR, setPacotesOCR] = useState<any[]>([]);
       const [savedConfigsLDP, setSavedConfigsLDP] = useState<any[]>([]);
  const [isEditing, setIsEditing] = useState(false);

  const [formData, setFormData] = useState({
    ID: null, ProjetoCodigo: "", Num_Doc: "", Data_Registro: new Date().toISOString().split('T')[0],
    Tipo_Registro: "RAP", Empresa: "", Disciplina: "", Status: "ATRASADA", Descricao_Projeto: "",
    Responsavel_Abertura: "", Descricao: "", Categoria_Erro: ""
  });

    const carregarDesvios = async (projetoCodigo?: string) => {
    try
    {
      const filtro = projetoCodigo && projetoCodigo.trim() !== "" ? projetoCodigo : "TODOS";
      const res = await fetch(`http://localhost:5277/api/TimesheetReport/qualidade-contratos?projetoCodigo=${encodeURIComponent(filtro)}`);
      if (res.ok) {
        const json = await res.json();
        if (json.sucesso){
          setListaNCs(json.dados?.ncs?.tabela || []);
        }
      }
    }
    catch (error)
    {
      console.error("Erro ao buscar desvios:", error);
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
      ID: item.ID, ProjetoCodigo: item.ProjetoCodigo, Num_Doc: item.Num_Doc, Data_Registro: item.Data_Registro.split('T')[0],
      Tipo_Registro: item.Tipo_Registro, Empresa: item.Empresa, Disciplina: item.Disciplina, Status: item.Status,
      Descricao_Projeto: item.Descricao_Projeto, Responsavel_Abertura: item.Responsavel_Abertura, Descricao: item.Descricao, Categoria_Erro: item.Categoria_Erro || ""
    });
    setIsEditing(true); window.scrollTo({ top: 0, behavior: 'smooth' });
  };

  const handleCancelarEdicao = () => {
    setIsEditing(false); setFormData({ ...formData, ID: null, Num_Doc: "", Empresa: "", Descricao_Projeto: "", Responsavel_Abertura: "", Descricao: "", Categoria_Erro: "" });
  };

  const handleExcluir = async (id: number, numDoc: string) => {
    if (!window.confirm(`ATENÇÃO! Tem certeza que deseja EXCLUIR a NC do documento ${numDoc}?`)) return;
    setMensagem(null);
    try {
      const res = await fetch(`http://localhost:5277/api/TimesheetReport/deletar-nc/${id}`, { method: "DELETE" });
      const json = await res.json();
      if (json.sucesso) { setMensagem({ tipo: 'sucesso', texto: json.mensagem }); carregarDadosReais(); carregarDesvios(formData.ProjetoCodigo); if (formData.ID === id) handleCancelarEdicao(); } 
      else { setMensagem({ tipo: 'erro', texto: json.mensagem }); }
    } catch (error) { setMensagem({ tipo: 'erro', texto: 'Erro de conexão ao tentar excluir.' }); }
  };

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!formData.ProjetoCodigo) { setMensagem({ tipo: 'erro', texto: 'Selecione um projeto!' }); return; }
    setLoading(true); setMensagem(null);
    const endpoint = isEditing ? "atualizar-nc" : "nova-nc";

    try {
      const response = await fetch(`http://localhost:5277/api/TimesheetReport/${endpoint}`, { method: "POST", headers: { "Content-Type": "application/json" }, body: JSON.stringify(formData) });
      const result = await response.json();
      if (result.sucesso) { setMensagem({ tipo: 'sucesso', texto: result.mensagem }); handleCancelarEdicao(); carregarDadosReais(); carregarDesvios(formData.ProjetoCodigo) } 
      else { setMensagem({ tipo: 'erro', texto: result.mensagem }); }
    } catch (error) { setMensagem({ tipo: 'erro', texto: 'Erro de conexão.' }); } 
    finally { setLoading(false); }
  };

  return (
    <div className="w-full space-y-8 animate-enter">
      <form onSubmit={handleSubmit} className="w-full space-y-6 bg-card p-6 rounded-xl border border-border shadow-sm">
        <div>
          <h2 className="text-lg font-bold text-foreground border-b border-border pb-2 mb-4">
            {isEditing ? <span className="text-emerald-500 flex items-center gap-2"><Edit className="w-5 h-5"/> Editando NC: {formData.Num_Doc}</span> : "Registro de Não Conformidade"}
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
            <label htmlFor="Num_Doc" className="block text-xs font-bold text-muted-foreground uppercase mb-1">Nº do Documento *</label>
            <input id="Num_Doc" type="text" name="Num_Doc" value={formData.Num_Doc} onChange={handleChange} required placeholder="Ex: NC-001" className="w-full bg-background border border-border rounded-md px-3 py-2 text-foreground text-sm focus:outline-none focus:border-emerald-500 transition-colors" />
          </div>

          <div>
            <label htmlFor="Data_Registro_NC" className="block text-xs font-bold text-muted-foreground uppercase mb-1">Data Registro *</label>
            <input id="Data_Registro_NC" type="date" name="Data_Registro" value={formData.Data_Registro} onChange={handleChange} required className="w-full bg-background border border-border rounded-md px-3 py-2 text-foreground text-sm focus:outline-none focus:border-emerald-500 transition-colors" />
          </div>

          <div className="lg:col-span-2">
            <label htmlFor="Categoria_Erro" className="block text-xs font-bold text-muted-foreground uppercase mb-1">Categoria do Erro (Motivo da NC) *</label>
            <select id="Categoria_Erro" name="Categoria_Erro" title="Categoria do Erro" aria-label="Categoria do Erro" value={formData.Categoria_Erro} onChange={handleChange} required className="w-full bg-background border border-border rounded-md px-3 py-2 text-emerald-600 dark:text-emerald-400 font-bold text-sm focus:outline-none focus:border-emerald-500 transition-colors">
              <option value="">Selecione o processo de falha...</option>
              <option value="Erro no Número do Documento">Erro no Número do Documento</option>
              <option value="Erro de Procedimento / Normas">Erro de Procedimento / Normas Técnicas</option>
              <option value="Erro de Preenchimento (Template)">Erro de Preenchimento (Template)</option>
              <option value="Falta de Revisão / Verificação">Falta de Revisão / Verificação Interna</option>
              <option value="Incompatibilidade de Disciplinas">Incompatibilidade de Disciplinas (Interferência)</option>
              <option value="Atraso na Entrega">Atraso Crítico na Entrega</option>
              <option value="Outros">Outros</option>
            </select>
          </div>

          <div>
            <label htmlFor="Tipo_Registro" className="block text-xs font-bold text-muted-foreground uppercase mb-1">Tipo de Ação</label>
            <select id="Tipo_Registro" name="Tipo_Registro" title="Tipo de Ação" aria-label="Tipo de Ação" value={formData.Tipo_Registro} onChange={handleChange} className="w-full bg-background border border-border rounded-md px-3 py-2 text-foreground text-sm focus:outline-none focus:border-emerald-500 font-bold transition-colors">
              <option value="RAP">RAP (Ação Preventiva)</option>
              <option value="RAC">RAC (Ação Corretiva)</option>
            </select>
          </div>

          <div>
            <label htmlFor="StatusNC" className="block text-xs font-bold text-muted-foreground uppercase mb-1">Status Atual</label>
            <select id="StatusNC" name="Status" title="Status Atual" aria-label="Status Atual" value={formData.Status} onChange={handleChange} className="w-full bg-background border border-border rounded-md px-3 py-2 text-foreground text-sm focus:outline-none focus:border-emerald-500 font-bold transition-colors">
              <option value="ATRASADA" className="text-rose-500">ATRASADA</option>
              <option value="NO PRAZO" className="text-emerald-500">NO PRAZO</option>
              <option value="CONCLUÍDA" className="text-sky-500">CONCLUÍDA</option>
            </select>
          </div>

          <div>
            <label htmlFor="Empresa" className="block text-xs font-bold text-muted-foreground uppercase mb-1">Empresa / Setor *</label>
            <input id="Empresa" type="text" name="Empresa" value={formData.Empresa} onChange={handleChange} required placeholder="Ex: N&A" className="w-full bg-background border border-border rounded-md px-3 py-2 text-foreground text-sm focus:outline-none focus:border-emerald-500 transition-colors" />
          </div>

          <div>
            <label htmlFor="DisciplinaNC" className="block text-xs font-bold text-muted-foreground uppercase mb-1">Disciplina</label>
            <select id="DisciplinaNC" name="Disciplina" title="Disciplina" aria-label="Disciplina" value={formData.Disciplina} onChange={handleChange} className="w-full bg-background border border-border rounded-md px-3 py-2 text-foreground text-sm focus:outline-none focus:border-emerald-500 transition-colors">
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
            <label htmlFor="Descricao_Projeto" className="block text-xs font-bold text-muted-foreground uppercase mb-1">Nome do Empreendimento / Área *</label>
            <input id="Descricao_Projeto" type="text" name="Descricao_Projeto" value={formData.Descricao_Projeto} onChange={handleChange} required placeholder="Ex: Projeto Boston" className="w-full bg-background border border-border rounded-md px-3 py-2 text-foreground text-sm focus:outline-none focus:border-emerald-500 transition-colors" />
          </div>

          <div className="lg:col-span-2">
            <label htmlFor="Responsavel_Abertura" className="block text-xs font-bold text-muted-foreground uppercase mb-1">Autor do Erro / Responsável *</label>
            <input id="Responsavel_Abertura" type="text" name="Responsavel_Abertura" value={formData.Responsavel_Abertura} onChange={handleChange} required placeholder="Nome de quem gerou ou abriu a NC" className="w-full bg-background border border-border rounded-md px-3 py-2 text-foreground text-sm focus:outline-none focus:border-emerald-500 transition-colors" />
          </div>
        </div>

        <div>
          <label htmlFor="DescricaoNC" className="block text-xs font-bold text-muted-foreground uppercase mb-1">Descrição do Problema *</label>
          <textarea id="DescricaoNC" name="Descricao" value={formData.Descricao} onChange={handleChange} required rows={3} placeholder="Descreva a não conformidade encontrada..." className="w-full bg-background border border-border rounded-md px-3 py-2 text-foreground text-sm focus:outline-none focus:border-emerald-500 resize-none transition-colors"></textarea>
        </div>

        <div className="flex justify-end gap-3 pt-4 border-t border-border">
          {isEditing && (
            <button type="button" onClick={handleCancelarEdicao} className="flex items-center gap-2 bg-muted hover:bg-muted/80 text-foreground font-bold py-2.5 px-4 rounded-md transition-colors">
              <X className="w-5 h-5" /> Cancelar
            </button>
          )}
          <button type="submit" disabled={loading} className={`flex items-center gap-2 text-white font-bold py-2.5 px-8 rounded-md shadow-lg transition-colors disabled:opacity-50 ${isEditing ? 'bg-sky-600 hover:bg-sky-500' : 'bg-sky-500 hover:bg-sky-400'}`}>
            {loading ? <span className="animate-spin text-xl">↻</span> : <Save className="w-5 h-5" />}
            {loading ? 'Processando...' : (isEditing ? 'Atualizar NC' : 'Salvar Nova NC')}
          </button>
        </div>
      </form>

      {/* TABELA DE GESTÃO */}
      <div className="w-full rounded-xl border border-border bg-card overflow-hidden shadow-lg">
        <div className="p-4 bg-muted/50 border-b border-border">
          <h3 className="font-bold text-foreground text-sm">Histórico de Não Conformidades</h3>
        </div>
        <div className="overflow-x-auto custom-scrollbar">
          <table className="w-full text-xs text-left whitespace-nowrap">
            <thead className="bg-muted text-muted-foreground">
              <tr>
                <th className="px-4 py-3 w-24 text-center">Ações</th>
                <th className="px-4 py-3 font-medium">Nº Doc</th>
                <th className="px-4 py-3 font-medium">Categoria / Motivo</th>
                <th className="px-4 py-3 font-medium">Autor/Resp.</th>
                <th className="px-4 py-3 font-medium">Disciplina</th>
                <th className="px-4 py-3 font-medium text-center">Status</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-border">
              {listaNCs.map((nc: any, idx: number) => (
                <tr key={idx} className="hover:bg-muted/50 transition-colors">
                  <td className="px-4 py-2 flex items-center justify-center gap-2">
                    <button onClick={() => handleEditItem(nc)} className="p-1.5 bg-emerald-500/10 text-emerald-600 dark:text-emerald-400 rounded hover:bg-emerald-500 hover:text-white transition-colors" title="Editar"><Edit className="w-4 h-4" /></button>
                    <button onClick={() => handleExcluir(nc.ID, nc.Num_Doc)} className="p-1.5 bg-rose-500/10 text-rose-600 dark:text-rose-400 rounded hover:bg-rose-500 hover:text-white transition-colors" title="Excluir"><Trash2 className="w-4 h-4" /></button>
                  </td>
                  <td className="px-4 py-2 font-bold text-emerald-600 dark:text-emerald-500">{nc.Num_Doc}</td>
                  <td className="px-4 py-2 text-foreground font-bold">{nc.Categoria_Erro || '-'}</td>
                  <td className="px-4 py-2 text-foreground">{nc.Responsavel_Abertura}</td>
                  <td className="px-4 py-2 text-muted-foreground">{nc.Disciplina}</td>
                  <td className="px-4 py-2 text-center">
                    <span className="bg-background border border-border px-2 py-1 rounded text-[10px] font-bold" style={{color: nc.Status === 'ATRASADA' ? '#ef4444' : '#10b981'}}>{nc.Status}</span>
                  </td>
                </tr>
              ))}
              {listaNCs.length === 0 && (
                <tr><td colSpan={6} className="text-center py-6 text-muted-foreground">Nenhuma NC cadastrada.</td></tr>
              )}
            </tbody>
          </table>
        </div>
      </div>
    </div>
  );
}