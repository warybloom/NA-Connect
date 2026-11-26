import { useState, useEffect } from "react";
import { Save, AlertCircle, CheckCircle2, Edit, X, Trash2 } from "lucide-react";

// BACKEND_PENDENTE: contrato original do ForgeSync (/api/TimesheetReport/*) não existe no N&A.
// O N&A tem ScopeDeviationReportsController (/api/reports/scope-deviations) com contrato diferente
// (usado hoje por ClientApp/src/components/pmo/ScopeDeviationsSection.tsx, área congelada). Este
// formulário foi transplantado com a URL absoluta convertida para relativa, mas ainda aponta para um
// endpoint que não existe no backend N&A — não inventar o endpoint aqui; ver
// AUDITORIA_FORGESYNC_NA/14-TRANSPLANTE-LITERAL-DEFINITIVO.md.
export default function FormDesvio() {
  const [projetos, setProjetos] = useState<any[]>([]);
  const [listaDesvios, setListaDesvios] = useState<any[]>([]);
  const [loading, setLoading] = useState(false);
  const [mensagem, setMensagem] = useState<{ tipo: 'sucesso' | 'erro', texto: string } | null>(null);
  
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

  const carregarDados = () => {
    fetch("/api/TimesheetReport/projetos-lista")
      .then(res => res.json())
      .then(json => { if (json.sucesso) setProjetos(json.dados); });

    fetch("/api/TimesheetReport/qualidade-contratos?projetoCodigo=TODOS")
      .then(res => res.json())
      .then(json => { if (json.sucesso) setListaDesvios(json.dados.desvios.tabela); });
  };

  useEffect(() => { carregarDados(); }, []);

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
      const res = await fetch(`/api/TimesheetReport/deletar-desvio/${id}`, { method: "DELETE" });
      const json = await res.json();
      if (json.sucesso) {
        setMensagem({ tipo: 'sucesso', texto: json.mensagem });
        carregarDados();
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
      const response = await fetch(`/api/TimesheetReport/${endpoint}`, {
        method: "POST", headers: { "Content-Type": "application/json" }, body: JSON.stringify(payload)
      });
      const result = await response.json();
      if (result.sucesso) {
        setMensagem({ tipo: 'sucesso', texto: result.mensagem });
        handleCancelarEdicao(); carregarDados(); 
      } else { setMensagem({ tipo: 'erro', texto: result.mensagem }); }
    } catch (error) { setMensagem({ tipo: 'erro', texto: 'Erro de conexão.' }); } finally { setLoading(false); }
  };

  return (
    <div className="w-full space-y-8 animate-enter">
      <form onSubmit={handleSubmit} className="w-full space-y-6 bg-slate-900/50 p-6 rounded-xl border border-slate-700 shadow-sm">
        <div>
          <h2 className="text-lg font-bold text-slate-100 border-b border-slate-700 pb-2 mb-4">
            {isEditing ? <span className="text-amber-400 flex items-center gap-2"><Edit className="w-5 h-5"/> Editando: {formData.Num_Desvio}</span> : "Novo Desvio de Escopo"}
          </h2>
        </div>

        {mensagem && (
          <div className={`p-4 rounded-lg flex items-center gap-3 font-medium text-sm ${mensagem.tipo === 'sucesso' ? 'bg-emerald-900/40 text-emerald-400 border border-emerald-800' : 'bg-rose-900/40 text-rose-400 border border-rose-800'}`}>
            {mensagem.tipo === 'sucesso' ? <CheckCircle2 className="w-5 h-5"/> : <AlertCircle className="w-5 h-5"/>}{mensagem.texto}
          </div>
        )}

        <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-4 gap-5">
          <div className="lg:col-span-2">
            <label htmlFor="ProjetoCodigo" className="block text-xs font-bold text-slate-400 uppercase mb-1">Projeto Vinculado *</label>
            <select id="ProjetoCodigo" name="ProjetoCodigo" title="Selecione o Projeto" value={formData.ProjetoCodigo} onChange={handleChange} required className="w-full bg-slate-800 border border-slate-600 rounded-md px-3 py-2 text-slate-200 text-sm focus:outline-none focus:border-amber-500 transition-colors">
              <option value="">Selecione...</option>
              {projetos.map((p, i) => <option key={i} value={p.Codigo}>{p.Codigo}</option>)}
            </select>
          </div>

          <div>
            <label htmlFor="Num_Desvio" className="block text-xs font-bold text-slate-400 uppercase mb-1">Nº do Desvio *</label>
            <input id="Num_Desvio" type="text" name="Num_Desvio" title="Número do Desvio" value={formData.Num_Desvio} onChange={handleChange} required placeholder="Ex: DEV-004" className="w-full bg-slate-800 border border-slate-600 rounded-md px-3 py-2 text-slate-200 text-sm focus:outline-none focus:border-amber-500 transition-colors" />
          </div>

          <div>
            <label htmlFor="Data_Registro" className="block text-xs font-bold text-slate-400 uppercase mb-1">Data Registro *</label>
            <input id="Data_Registro" type="date" name="Data_Registro" title="Data" value={formData.Data_Registro} onChange={handleChange} required className="w-full bg-slate-800 border border-slate-600 rounded-md px-3 py-2 text-slate-200 text-sm focus:outline-none focus:border-amber-500 [color-scheme:dark]" />
          </div>

          {/* NOVOS CAMPOS ESTRATÉGICOS */}
          <div>
            <label htmlFor="Tipo_Desvio" className="block text-xs font-bold text-slate-400 uppercase mb-1">Tipo de Desvio *</label>
            <select id="Tipo_Desvio" name="Tipo_Desvio" title="Tipo" value={formData.Tipo_Desvio} onChange={handleChange} required className="w-full bg-slate-800 border border-slate-600 rounded-md px-3 py-2 text-amber-400 font-bold text-sm focus:outline-none focus:border-amber-500">
              <option value="INTERNO">INTERNO (Retrabalho)</option>
              <option value="EXTERNO">EXTERNO (Change Order Cliente)</option>
            </select>
          </div>

          <div>
            <label htmlFor="Categoria_Motivo" className="block text-xs font-bold text-slate-400 uppercase mb-1">Motivo Principal *</label>
            <select id="Categoria_Motivo" name="Categoria_Motivo" title="Motivo" value={formData.Categoria_Motivo} onChange={handleChange} required className="w-full bg-slate-800 border border-slate-600 rounded-md px-3 py-2 text-slate-200 text-sm focus:outline-none focus:border-amber-500">
              <option value="">Selecione...</option>
              <option value="Erro de Orçamento">Erro de Orçamento</option>
              <option value="Escopo não Previsto">Escopo não Previsto (Omissão)</option>
              <option value="Alteração do Cliente">Alteração Solicitada pelo Cliente</option>
              <option value="Erro de Engenharia">Erro Interno da Engenharia (N&A)</option>
              <option value="Falta de Dados">Falta de Dados / Premissas</option>
            </select>
          </div>

          <div>
            <label htmlFor="Impacto" className="block text-xs font-bold text-slate-400 uppercase mb-1">Impacto Previsto *</label>
            <select id="Impacto" name="Impacto" title="Impacto" value={formData.Impacto} onChange={handleChange} required className="w-full bg-slate-800 border border-slate-600 rounded-md px-3 py-2 text-slate-200 text-sm focus:outline-none focus:border-amber-500">
              <option value="">Selecione...</option>
              <option value="Apenas Custo (HH)">Apenas Custo (HH)</option>
              <option value="Apenas Prazo">Apenas Prazo</option>
              <option value="Custo e Prazo">Custo e Prazo</option>
              <option value="Afeta Outras Disciplinas">Impacta Outras Disciplinas</option>
            </select>
          </div>

          <div>
            <label htmlFor="Status" className="block text-xs font-bold text-slate-400 uppercase mb-1">Status</label>
            <select id="Status" name="Status" title="Status" value={formData.Status} onChange={handleChange} className="w-full bg-slate-800 border border-slate-600 rounded-md px-3 py-2 text-slate-200 text-sm focus:outline-none focus:border-amber-500 font-bold">
              <option value="EM ANÁLISE" className="text-amber-500">EM ANÁLISE</option>
              <option value="APROVADA" className="text-emerald-500">APROVADA</option>
              <option value="REPROVADA" className="text-rose-500">REPROVADA</option>
            </select>
          </div>

          <div>
            <label htmlFor="Disciplina" className="block text-xs font-bold text-slate-400 uppercase mb-1">Disciplina Origem</label>
            <select id="Disciplina" name="Disciplina" title="Disciplina" value={formData.Disciplina} onChange={handleChange} className="w-full bg-slate-800 border border-slate-600 rounded-md px-3 py-2 text-slate-200 text-sm focus:outline-none focus:border-amber-500">
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
            <label htmlFor="Quant_HH" className="block text-xs font-bold text-slate-400 uppercase mb-1">Esforço (HH)</label>
            <input id="Quant_HH" type="number" step="0.01" name="Quant_HH" title="HH" value={formData.Quant_HH} onChange={handleChange} className="w-full bg-slate-800 border border-slate-600 rounded-md px-3 py-2 text-slate-200 text-sm focus:outline-none focus:border-amber-500" />
          </div>

          {/* CAMPOS CONDICIONAIS */}
          {formData.Tipo_Desvio === "INTERNO" ? (
            <div className="lg:col-span-2">
              <label htmlFor="Desvio_Interno" className="block text-xs font-bold text-slate-400 uppercase mb-1">Código de Erro Interno</label>
              <input id="Desvio_Interno" type="text" name="Desvio_Interno" title="Desvio Interno" value={formData.Desvio_Interno} onChange={handleChange} placeholder="Referência interna do erro..." className="w-full bg-slate-800 border border-slate-600 rounded-md px-3 py-2 text-slate-200 text-sm focus:outline-none focus:border-amber-500" />
            </div>
          ) : (
            <>
              <div>
                <label htmlFor="Change_Order" className="block text-xs font-bold text-emerald-400 uppercase mb-1">Cód. Change Order</label>
                <input id="Change_Order" type="text" name="Change_Order" title="C.O." value={formData.Change_Order} onChange={handleChange} placeholder="Ex: CO-001" className="w-full bg-slate-800 border border-emerald-600/50 rounded-md px-3 py-2 text-emerald-300 text-sm focus:outline-none focus:border-emerald-500" />
              </div>
              <div>
                <label htmlFor="Valor_CO" className="block text-xs font-bold text-emerald-400 uppercase mb-1">Valor do Pleito (R$)</label>
                <input id="Valor_CO" type="number" step="0.01" name="Valor_CO" title="Valor" value={formData.Valor_CO} onChange={handleChange} placeholder="0.00" className="w-full bg-slate-800 border border-emerald-600/50 rounded-md px-3 py-2 text-emerald-300 text-sm focus:outline-none focus:border-emerald-500" />
              </div>
            </>
          )}
        </div>

        <div>
          <label htmlFor="Descricao" className="block text-xs font-bold text-slate-400 uppercase mb-1">Descrição Detalhada do Fato *</label>
          <textarea id="Descricao" name="Descricao" title="Descrição" value={formData.Descricao} onChange={handleChange} required rows={3} placeholder="Descreva tecnicamente o que motivou a mudança de escopo..." className="w-full bg-slate-800 border border-slate-600 rounded-md px-3 py-2 text-slate-200 text-sm focus:outline-none focus:border-amber-500 resize-none"></textarea>
        </div>

        <div className="flex justify-end gap-3 pt-4 border-t border-slate-700">
          {isEditing && (
            <button type="button" onClick={handleCancelarEdicao} className="flex items-center gap-2 bg-slate-700 hover:bg-slate-600 text-foreground font-bold py-2.5 px-4 rounded-md transition-colors"><X className="w-5 h-5" /> Cancelar</button>
          )}
          <button type="submit" disabled={loading} className={`flex items-center gap-2 text-white font-bold py-2.5 px-8 rounded-md shadow-lg transition-colors disabled:opacity-50 ${isEditing ? 'bg-sky-600 hover:bg-sky-500' : 'bg-amber-500 hover:bg-amber-600'}`}>
            {loading ? <span className="animate-spin text-xl">↻</span> : <Save className="w-5 h-5" />}
            {loading ? 'Processando...' : (isEditing ? 'Atualizar Desvio' : 'Salvar Novo Desvio')}
          </button>
        </div>
      </form>

      {/* TABELA DE GESTÃO E EXCLUSÃO */}
      <div className="w-full rounded-xl border border-slate-700 bg-slate-800/50 overflow-hidden shadow-lg">
        <div className="p-4 bg-slate-900 border-b border-slate-700"><h3 className="font-bold text-slate-200 text-sm">Histórico de Desvios Cadastrados</h3></div>
        <div className="overflow-x-auto custom-scrollbar">
          <table className="w-full text-xs text-left whitespace-nowrap">
            <thead className="bg-slate-900 text-slate-400">
              <tr>
                <th className="px-4 py-3 w-24 text-center">Ações</th>
                <th className="px-4 py-3 font-medium">Nº Desvio</th>
                <th className="px-4 py-3 font-medium">Tipo</th>
                <th className="px-4 py-3 font-medium">Categoria / Motivo</th>
                <th className="px-4 py-3 font-medium">Status</th>
                <th className="px-4 py-3 font-medium w-full">Descrição</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-slate-700/50">
              {listaDesvios.map((d: any, idx: number) => (
                <tr key={idx} className="hover:bg-slate-700/30 transition-colors">
                  <td className="px-4 py-2 flex items-center justify-center gap-2">
                    <button onClick={() => handleEditItem(d)} className="p-1.5 bg-sky-500/10 text-sky-400 rounded hover:bg-sky-500 hover:text-white" title="Editar"><Edit className="w-4 h-4" /></button>
                    <button onClick={() => handleExcluir(d.ID, d.Num_Desvio)} className="p-1.5 bg-rose-500/10 text-rose-400 rounded hover:bg-rose-500 hover:text-white" title="Excluir"><Trash2 className="w-4 h-4" /></button>
                  </td>
                  <td className="px-4 py-2 font-bold text-amber-400">{d.Num_Desvio}</td>
                  <td className="px-4 py-2"><span className="bg-slate-950 px-2 py-1 border border-slate-700 rounded text-slate-300 font-bold">{d.Tipo_Desvio}</span></td>
                  <td className="px-4 py-2 text-slate-300 font-bold">{d.Categoria_Motivo || '-'}</td>
                  <td className="px-4 py-2"><span className="bg-slate-900 border border-slate-700 px-2 py-1 rounded text-[10px] font-bold" style={{color: d.Status === 'EM ANÁLISE' ? '#eab308' : d.Status === 'APROVADA' ? '#10b981' : '#ef4444'}}>{d.Status}</span></td>
                  <td className="px-4 py-2 text-slate-400 truncate max-w-[400px]">{d.Descricao}</td>
                </tr>
              ))}
              {listaDesvios.length === 0 && <tr><td colSpan={6} className="text-center py-6 text-slate-500">Nenhum desvio cadastrado.</td></tr>}
            </tbody>
          </table>
        </div>
      </div>
    </div>
  );
}