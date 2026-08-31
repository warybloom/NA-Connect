import { useState, useMemo, useEffect } from "react";
import { SidebarProvider, SidebarInset, SidebarTrigger } from "@/components/ui/sidebar";
import { ScrollArea } from "@/components/ui/scroll-area"
import { AppSidebar } from "@/components/app-sidebar";
import {
  Search,
  Trash2,
  AlertTriangle,
  ShieldCheck,
  UserCog,
  Users,
  Mail,
  KeyRound,
  Eye,
} from "lucide-react";
import { Input } from "@/components/ui/input";
import { Button } from "@/components/ui/button";
import { Switch } from "@/components/ui/switch";
import { ModeToggle } from "@/components/alternar-modo";
import toast from "react-hot-toast";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/Select";
import {
  Dialog,
  DialogContent,
  DialogHeader,
  DialogTitle,
  DialogFooter,
} from "@/components/ui/dialog";
import { cn } from "@/lib/utils";

// =========================================================================
// TIPOS
// =========================================================================
type PapelUsuario = string;

interface TelaPermissao {
  id: number;
  code: string;
  name: string;
  hint: string;
  canAccess: boolean;
}

interface Usuario {
  id: number;
  nome: string;
  email: string;
  papel: PapelUsuario;
  ativo: boolean;
  ultimoAcesso: string;
  qtdPermissoes: number;
  totalTelas: number;
}




// =========================================================================
// API
// =========================================================================
const API_URL = "/api/AdminMaster";

async function fetchUsers(): Promise<Usuario[]> {
  const res = await fetch(`${API_URL}/users`, { credentials: "include" });
  if (!res.ok) throw new Error("Erro ao buscar usuários");
  return res.json();
}

async function fetchRoles(): Promise<string[]> {
  const res = await fetch(`${API_URL}/roles`, { credentials: "include" });
  if (!res.ok) throw new Error("Erro ao buscar papéis");
  return res.json();
}

async function deleteUser(id: number) {
  const res = await fetch(`${API_URL}/users/${id}`, {
    method: "DELETE",
    credentials: "include",
  });
  if (!res.ok) throw new Error("Erro ao remover usuário");
}

const updateUserRole = async (userId: number, role: string) => {
  const response = await fetch(`${API_URL}/users/${userId}/role`, {
    method: "PUT",
    credentials: "include",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ role }),
  });
  if (!response.ok) throw new Error("Erro ao atualizar papel do usuário");
  return await response.json();
};

const toggleUserStatus = async (userId: number) => {
  const response = await fetch(`${API_URL}/users/${userId}/status`, {
    method: "PUT",
    credentials: "include",
  });
  if (!response.ok) throw new Error("Erro ao alterar status");
  return await response.json();
};

const fetchUserPermissions = async (userId: number) => {
  const response = await fetch(`${API_URL}/users/${userId}/permissions`, {
    credentials: "include",
  });
  if (!response.ok) throw new Error("Erro ao carregar permissões");
  return await response.json();
};

const saveUserPermissions = async (userId: number, permissions: Record<number, boolean>) => {
  const response = await fetch(`${API_URL}/users/${userId}/permissions`, {
    method: "PUT",
    credentials: "include",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(permissions),
  });
  if (!response.ok) throw new Error("Erro ao salvar permissões");
  return await response.json();
};

const applyDefaultProfile = async (userId: number) => {
  const response = await fetch(`${API_URL}/users/${userId}/apply-default-profile`, {
    method: "POST",
    credentials: "include",
  });
  if (!response.ok) throw new Error("Erro ao aplicar perfil padrão");
  return await response.json();
};

// =========================================================================
// ESTILOS DOS PAPÉIS (dinâmico — funciona pra qualquer papel novo cadastrado)
// =========================================================================
const ROLE_COLORS: Record<string, string> = {
  admin: "bg-primary/10 text-primary border-primary/20",
  editor: "bg-green-500/10 text-green-500 border-green-500/20",
  viewer: "bg-red-500/10 text-red-500 border-red-500/20", 
  planejamento: "bg-orange-500/10 text-amber-500 border-amber-500/20",
};

const ROLE_COLOR_FALLBACK = "bg-yellow/10 text-muted-foreground border-muted/20";

function getRoleStyle(papel: string): string {
  const chave = papel?.trim().toLowerCase();
  return ROLE_COLORS[chave] ?? ROLE_COLOR_FALLBACK;
}




// =========================================================================
// COMPONENTE PRINCIPAL
// =========================================================================
export default function UserPermissionsPanel() {
  const [users, setUsers] = useState<Usuario[]>([]);
  const [roles, setRoles] = useState<string[]>([]);
  const [loadingUsers, setLoadingUsers] = useState(true);
  const [searchTerm, setSearchTerm] = useState("");
  const ITEMS_PER_PAGE = 10;
  const [currentPage, setCurrentPage] = useState(1);
  const [usuarios, setUsuarios] = useState<Usuario[]>([])

  // Modal de edição de papéis em lote
  const [isBatchRoleOpen, setIsBatchRoleOpen] = useState(false);
  const [batchSearch, setBatchSearch] = useState("");
  const [tempRoles, setTempRoles] = useState<Record<number, PapelUsuario>>({});
  const [savingBatch, setSavingBatch] = useState(false);

  const [editingUser, setEditingUser] = useState<Usuario | null>(null);
  const [permissoesUsuario, setPermissoesUsuario] = useState<TelaPermissao[]>([]);
  const [loadingPermissoes, setLoadingPermissoes] = useState(false);
  const [savingPermissoes, setSavingPermissoes] = useState(false);
  const [applyingDefault, setApplyingDefault] = useState(false);

  const [isConfirmDeleteOpen, setIsConfirmDeleteOpen] = useState(false);
  const [userToDelete, setUserToDelete] = useState<Usuario | null>(null);
  const [deleting, setDeleting] = useState(false);
  

  

  useEffect(() => {
    loadUsers();
    fetchRoles()
      .then(setRoles)
      .catch(() => toast.error("Erro ao carregar papéis."));
  }, []);

  const loadUsers = () => {
    setLoadingUsers(true);
    fetchUsers()
      .then(setUsers)
      .catch(() => toast.error("Erro ao carregar usuários."))
      .finally(() => setLoadingUsers(false));
  };

  useEffect(() => {
    if (!editingUser) {
      setPermissoesUsuario([]);
      return;
    }
    setLoadingPermissoes(true);
    fetchUserPermissions(editingUser.id)
      .then(setPermissoesUsuario)
      .catch(() => toast.error("Erro ao carregar permissões."))
      .finally(() => setLoadingPermissoes(false));
  }, [editingUser]);

  // Inicializa o estado dos papéis quando o modal em lote abre
  const handleOpenBatchModal = () => {
    const rolesMap: Record<number, PapelUsuario> = {};
    users.forEach((u) => {
      rolesMap[u.id] = u.papel;
    });
    setTempRoles(rolesMap);
    setBatchSearch("");
    setIsBatchRoleOpen(true);
  };

  const filteredUsers = useMemo(
    () =>
      users.filter(
        (u) =>
          u.nome.toLowerCase().includes(searchTerm.toLowerCase()) ||
          u.email.toLowerCase().includes(searchTerm.toLowerCase())
      ),
    [users, searchTerm]
  );

  const batchFilteredUsers = useMemo(
    () =>
      users.filter(
        (u) =>
          u.nome.toLowerCase().includes(batchSearch.toLowerCase()) ||
          u.email.toLowerCase().includes(batchSearch.toLowerCase())
      ),
    [users, batchSearch]
  );

  const totalPage = Math.max(1, Math.ceil(filteredUsers.length / ITEMS_PER_PAGE));
  const paginatedUsers = useMemo(() => {
    const start = (currentPage - 1) * ITEMS_PER_PAGE;
    return filteredUsers.slice(start, start + ITEMS_PER_PAGE);
  }, [filteredUsers, currentPage]);

  const handleToggleAtivo = async (id: number) => {
    try {
      const result = await toggleUserStatus(id);
      setUsers((prev) => prev.map((u) => (u.id === id ? { ...u, ativo: result.ativo } : u)));
      toast.success(result.ativo ? "Usuário ativado!" : "Usuário desativado!");
    } catch {
      toast.error("Erro ao atualizar status.");
    }
  };

  const handleRoleChange = async (id: number, papel: PapelUsuario) => {
    const anterior = users.find((u) => u.id === id)?.papel;
    setUsers((prev) => prev.map((u) => (u.id === id ? { ...u, papel } : u)));
    try {
      await updateUserRole(id, papel);
      toast.success("Papel atualizado!");
    } catch {
      toast.error("Erro ao atualizar papel.");
      if (anterior) setUsers((prev) => prev.map((u) => (u.id === id ? { ...u, papel: anterior } : u)));
    }
  };

  const handleBatchRoleChange = (userId: number, role: PapelUsuario) => {
    setTempRoles((prev) => ({ ...prev, [userId]: role }));
  };

  

  const handleSaveBatchRoles = async () => {
    setSavingBatch(true);
    try {
      const updates = users
        .filter((u) => tempRoles[u.id] && tempRoles[u.id] !== u.papel)
        .map((u) => updateUserRole(u.id, tempRoles[u.id]));

      if (updates.length === 0) {
        setIsBatchRoleOpen(false);
        setSavingBatch(false);
        return;
      }

      await Promise.all(updates);

      setUsers((prev) =>
        prev.map((u) => (tempRoles[u.id] ? { ...u, papel: tempRoles[u.id] } : u))
      );

      toast.success("Papéis atualizados em lote com sucesso!");
      setIsBatchRoleOpen(false);
    } catch {
      toast.error("Erro ao atualizar alguns papéis.");
    } finally {
      setSavingBatch(false);
    }
  };

  const togglePermissao = (screenId: number) => {
    setPermissoesUsuario((prev) =>
      prev.map((p) => (p.id === screenId ? { ...p, canAccess: !p.canAccess } : p))
    );
  };

  const handleSavePermissoes = async () => {
    if (!editingUser) return;
    setSavingPermissoes(true);
    try {
      const payload = Object.fromEntries(permissoesUsuario.map((p) => [p.id, p.canAccess]));
      await saveUserPermissions(editingUser.id, payload);

      const qtd = permissoesUsuario.filter((p) => p.canAccess).length;
      setUsers((prev) =>
        prev.map((u) => (u.id === editingUser.id ? { ...u, qtdPermissoes: qtd } : u))
      );

      toast.success("Permissões salvas!");
      setEditingUser(null);
    } catch {
      toast.error("Erro ao salvar permissões.");
    } finally {
      setSavingPermissoes(false);
    }
  };

  const handleApplyDefaultProfile = async () => {
    if (!editingUser) return;
    setApplyingDefault(true);
    try {
      await applyDefaultProfile(editingUser.id);
      const atualizado = await fetchUserPermissions(editingUser.id);
      setPermissoesUsuario(atualizado);
      toast.success("Perfil padrão aplicado!");
    } catch {
      toast.error("Erro ao aplicar perfil padrão.");
    } finally {
      setApplyingDefault(false);
    }
  };

  const triggerDelete = (u: Usuario) => {
    setUserToDelete(u);
    setIsConfirmDeleteOpen(true);
  };

  const handleConfirmDelete = async () => {
    if (!userToDelete) return;
    setDeleting(true);
    try {
      await deleteUser(userToDelete.id);
      setUsers((prev) => prev.filter((u) => u.id !== userToDelete.id));
      toast.success("Usuário removido.");
      setIsConfirmDeleteOpen(false);
      setUserToDelete(null);
    } catch {
      toast.error("Erro ao remover usuário.");
    } finally {
      setDeleting(false);
    }
  };

  return (
    <SidebarProvider>
      <AppSidebar />
      <SidebarInset>
        <div className="flex flex-col gap-1 animate-enter m-4">
          <div className="flex items-center gap-7">
            <SidebarTrigger className="-m-5" />
            <ModeToggle />
          </div>

          <div className="flex flex-col gap-4 animate-enter m-4">
            <div className="space-y-2">
              <div className="flex items-center gap-2 text-primary mb-1">
                <ShieldCheck className="w-5 h-5" />
                <span className="text-[10px] font-black uppercase tracking-[0.3em]">Administração</span>
              </div>
              <h1 className="text-3xl font-black text-foreground uppercase italic tracking-tight">
                Controle de Permissões de Usuários
              </h1>
            </div>

            {/* Cards de resumo */}
            <div className="grid grid-cols-1 md:grid-cols-3 gap-4 mb-2">
              <div className="glass-card p-5 rounded-xl border border-border/50 flex items-center gap-4">
                <div className="p-3 bg-primary/10 rounded-xl text-primary">
                  <Users className="w-5 h-5" />
                </div>
                <div>
                  <span className="text-2xl font-black text-foreground italic">{users.length}</span>
                  <p className="text-[9px] font-black uppercase tracking-widest text-muted-foreground">
                    Usuários cadastrados
                  </p>
                </div>
              </div>
              <div className="glass-card p-5 rounded-xl border border-border/50 flex items-center gap-4">
                <div className="p-3 bg-emerald-500/10 rounded-xl text-emerald-500">
                  <UserCog className="w-5 h-5" />
                </div>
                <div>
                  <span className="text-2xl font-black text-foreground italic">
                    {users.filter((u) => u.ativo).length}
                  </span>
                  <p className="text-[9px] font-black uppercase tracking-widest text-muted-foreground">
                    Ativos no momento
                  </p>
                </div>
              </div>
              <div className="glass-card p-5 rounded-xl border border-border/50 flex items-center gap-4">
                <div className="p-3 bg-amber-500/10 rounded-xl text-amber-500">
                  <ShieldCheck className="w-5 h-5" />
                </div>
                <div>
                  <span className="text-2xl font-black text-foreground italic">
                    {users.filter((u) => u.papel === "ADMIN").length}
                  </span>
                  <p className="text-[9px] font-black uppercase tracking-widest text-muted-foreground">
                    Administradores
                  </p>
                </div>
              </div>
            </div>

            {/* Busca + Ação */}
            <div className="mb-2 flex justify-between items-center gap-4">
              <div className="relative max-w-sm w-full">
                <Search className="absolute left-3 top-1/2 -translate-y-1/2 w-4 h-4 text-muted-foreground" />
                <Input
                  placeholder="Pesquisar por nome ou e-mail..."
                  className="pl-10 bg-card/30 border-border/50"
                  value={searchTerm}
                  onChange={(e) => setSearchTerm(e.target.value)}
                />
              </div>

              <div className="flex items-center gap-3">
                <Button onClick={handleOpenBatchModal}>
                  <UserCog className="w-4 h-4 mr-2" />
                  Gerenciar Papéis em Lote
                </Button>
              </div>
            </div>

            {/* Tabela Principal */}
            <div className="w-full overflow-hidden rounded-xl border border-border/50 glass-card shadow-2xl">
              <table className="w-full text-left border-collapse table-fixed">
                <thead>
                  <tr className="bg-muted/40 text-[10px] uppercase tracking-[0.2em] text-muted-foreground font-black border-b border-border/50">
                    <th className="px-6 py-5 w-[28%]">Usuário</th>
                    <th className="px-6 py-5 w-[16%] text-center">Papel</th>
                    <th className="px-4 py-5 text-center w-[12%]">Status</th>
                    <th className="px-4 py-5 w-[16%]">Data de Cadastro</th>
                    <th className="px-4 py-5 w-[16%]">Permissões</th>
                    <th className="px-4 py-5 w-[12%] text-right">Ações</th>
                  </tr>
                </thead>
                <tbody className="divide-y divide-border/30 bg-card/10">
                  {loadingUsers ? (
                    <tr>
                      <td colSpan={6} className="px-6 py-10 text-center text-xs text-muted-foreground font-bold uppercase">
                        Carregando usuários...
                      </td>
                    </tr>
                  ) : paginatedUsers.length === 0 ? (
                    <tr>
                      <td colSpan={6} className="px-6 py-10 text-center text-xs text-muted-foreground font-bold uppercase">
                        Nenhum usuário encontrado.
                      </td>
                    </tr>
                  ) : (
                    paginatedUsers.map((user) => (
                      <tr key={user.id} className="group hover:bg-primary/5 transition-all duration-300">
                        <td className="px-6 py-4">
                          <div className="flex items-center gap-3">
                            <div className="w-9 h-9 rounded-lg bg-primary/10 flex items-center justify-center text-primary font-black text-xs italic shrink-0">
                              {user.nome
                                .split(" ")
                                .slice(0, 2)
                                .map((n) => n[0])
                                .join("")}
                            </div>
                            <div className="flex flex-col gap-0.5 overflow-hidden">
                              <span className="text-lg font-bold text-foreground truncate">{user.nome}</span>
                              <span className="text-sm text-muted-foreground font-medium truncate flex items-center gap-1">
                                <Mail className="w-3 h-3 shrink-0" /> {user.email}
                              </span>
                            </div>
                          </div>
                        </td>

                        <td className="px-6 py-1">
                          <Select value={user.papel} onValueChange={(val) => handleRoleChange(user.id, val)}>
                            <SelectTrigger
                              className={cn(
                                "font-black uppercase justify-center text-center [&>span]:w-full [&>span]:text-center p-2 bg-primary-foreground text-foreground",
                                getRoleStyle(user.papel)
                              )}
                            >
                              <SelectValue />
                            </SelectTrigger>
                            <SelectContent className="bg-popover border-border/50 backdrop-blur-xl">
                              {roles.map((r) => (
                                <SelectItem key={r} value={r} className="text-[10px] font-black uppercase">
                                  {r}
                                </SelectItem>
                              ))}
                            </SelectContent>
                          </Select>
                        </td>

                        <td className="px-4 py-4 text-center">
                          <div className="flex items-center justify-center gap-2">
                            <Switch checked={user.ativo} onCheckedChange={() => handleToggleAtivo(user.id)} />
                            <span
                              className={cn(
                                "text-[9px] font-black uppercase",
                                user.ativo ? "text-emerald-500" : "text-muted-foreground"
                              )}
                            >
                              {user.ativo ? "Ativo" : "Inativo"}
                            </span>
                          </div>
                        </td>

                        <td className="px-14 py-5">
                          <span className="text-[10px] font-bold text-muted-foreground uppercase">
                            {user.ultimoAcesso}
                          </span>
                        </td>

                        <td className="px-4 py-4">
                          <button
                            onClick={() => setEditingUser(user)}
                            className="flex items-center gap-2 text-[10px] font-black uppercase text-primary hover:underline"
                          >
                            <KeyRound className="w-3.5 h-3.5" />
                            {user.qtdPermissoes}/{user.totalTelas} telas
                          </button>
                        </td>

                        <td className="px-4 py-4">
                          <div className="flex items-center justify-end gap-1">
                            <Button
                              variant="ghost"
                              size="sm"
                              onClick={() => setEditingUser(user)}
                              className="h-8 w-8 p-0 opacity-0 group-hover:opacity-100 hover:bg-primary/10 hover:text-primary transition-all rounded-lg"
                              title="Ver/editar permissões"
                            >
                              <Eye className="w-3.5 h-3.5" />
                            </Button>
                            <Button
                              variant="ghost"
                              size="sm"
                              onClick={() => triggerDelete(user)}
                              className="h-8 w-8 p-0 opacity-0 group-hover:opacity-100 hover:bg-destructive/20 hover:text-destructive transition-all rounded-lg"
                              title="Remover usuário"
                            >
                              <Trash2 className="w-3.5 h-3.5" />
                            </Button>
                          </div>
                        </td>
                      </tr>
                    ))
                  )}
                </tbody>
              </table>

              {filteredUsers.length > ITEMS_PER_PAGE && (
                <div className="flex items-center justify-between px-6 py-4 border-t border-border/50 bg-card/10">
                  <span className="text-[10px] font-black uppercase text-muted-foreground">
                    Mostrando {(currentPage - 1) * ITEMS_PER_PAGE + 1}–
                    {Math.min(currentPage * ITEMS_PER_PAGE, filteredUsers.length)} de {filteredUsers.length} usuários
                  </span>
                  <div className="flex items-center gap-2">
                    <Button
                      variant="outline"
                      size="sm"
                      disabled={currentPage === 1}
                      onClick={() => setCurrentPage((p) => Math.max(1, p - 1))}
                      className="font-black text-[10px] uppercase h-8"
                    >
                      Anterior
                    </Button>
                    <span className="text-[10px] font-black uppercase text-muted-foreground px-2">
                      Página {currentPage} de {totalPage}
                    </span>
                    <Button
                      variant="outline"
                      size="sm"
                      disabled={currentPage === totalPage}
                      onClick={() => setCurrentPage((p) => Math.min(totalPage, p + 1))}
                      className="font-black text-[10px] uppercase h-8"
                    >
                      Próxima
                    </Button>
                  </div>
                </div>
              )}
            </div>
          </div>

          {/* DIALOG: Gerenciar Papéis dos Usuários em Lote */}
          <Dialog open={isBatchRoleOpen} onOpenChange={setIsBatchRoleOpen}>
            <DialogContent className=" border-border/25 backdrop-blur-xl bg-transparent text-white max-w-4xl h-full flex flex-col">
              <DialogHeader>
                <div className="flex items-center gap-1 text-primary mb-1">
                  <UserCog className="w-5 h-5" />
                  <span className="text-[10px] font-black uppercase tracking-[0.2em]">Gestão em Lote</span>
                </div>
                <DialogTitle className="text-xl font-black uppercase italic">
                  Atribuir Papéis aos Usuários
                </DialogTitle>
              </DialogHeader>

              <div className="py-2 space-y-2 flex-1 overflow-hidden flex flex-col">
                <div className="relative w-full">
                  <Search className="absolute left-3 top-1/2 -translate-y-1/2 w-4 h-4 text-muted-foreground" />
                  <Input
                    placeholder="Pesquisar usuário"
                    value={batchSearch}
                    onChange={(e) => setBatchSearch(e.target.value)}
                    className="pl-10 bg-card/30  "
                  />
                </div>

                <ScrollArea className="flex-1 min-h-0">
                <div className="overflow-y-auto max-h-[350px] pr-1 space-y-2 divide-y divide-border/20">
                  {batchFilteredUsers.length === 0 ? (
                    <p className="text-xs text-muted-foreground text-center py-6 font-bold uppercase">
                      Nenhum usuário encontrado.
                    </p>
                  ) : (
                    batchFilteredUsers.map((user) => (
                      <div
                        key={user.id}
                        className="flex items-center justify-between gap-4 pt-3 pb-1"
                      >
                        <div className="flex flex-col overflow-hidden">
                          <span className="text-xs font-bold uppercase text-white truncate">
                            {user.nome}
                          </span>
                          <span className="text-[10px] text-white truncate">
                            {user.email}
                          </span>
                        </div>

                        <div className="w-[140px] shrink-0">
                          <Select
                            value={tempRoles[user.id] || user.papel}
                            onValueChange={(val) => handleBatchRoleChange(user.id, val)}
                          >
                            <SelectTrigger
                              className={cn(
                                "font-black uppercase text-[10px] h-8 justify-center text-center",
                                getRoleStyle(tempRoles[user.id] || user.papel)
                              )}
                            >
                              <SelectValue />
                            </SelectTrigger>
                            <SelectContent>
                              {roles.map((r) => (
                                <SelectItem key={r} value={r} className="text-[10px] font-black uppercase">
                                  {r}
                                </SelectItem>
                              ))}
                            </SelectContent>
                          </Select>
                        </div>
                      </div>
                    ))
                  )}
                </div>
                </ScrollArea>
              </div>

              <DialogFooter className="pt-4 border-t border-border/30">
                <Button
                  variant="outline"
                  onClick={() => setIsBatchRoleOpen(false)}
                  className="font-black text-[10px] text-foreground uppercase font-sans hover:bg-red-500"
                  disabled={savingBatch}
                >
                  Cancelar
                </Button>
                <Button
                  onClick={handleSaveBatchRoles}
                  disabled={savingBatch}
                  className="font-black text-[10px] uppercase font-sans tracking-widest bg-primary hover:bg-green-300"
                >
                  {savingBatch ? "Salvando..." : "Salvar Alterações"}
                </Button>
              </DialogFooter>
            </DialogContent>
          </Dialog>

          {/* DIALOG: Editar Permissões por Usuário */}
 <Dialog open={!!editingUser} onOpenChange={(open) => !open && setEditingUser(null)}>
  <DialogContent className="border-border/50 backdrop-blur-xl bg-transparent text-foreground max-w-[700px] h-[85vh] flex flex-col">
    <DialogHeader>
      <div className="flex items-center justify-between gap-2 mb-2">
        <div className="flex items-center gap-2 text-primary">
          <KeyRound className="w-5 h-5" />
          <span className="text-[10px] font-black uppercase tracking-[0.2em]">Permissões de Acesso</span>
        </div>
      </div>
      <DialogTitle className="text-xl text-white font-black uppercase italic tracking-tighter">
        {editingUser?.nome}
      </DialogTitle>
      <p className="text-[10px] text-white uppercase font-bold">{editingUser?.email}</p>
    </DialogHeader>

    <ScrollArea className="flex-1 min-h-0">
      <div className="space-y-3 py-2 pr-4">
        {loadingPermissoes ? (
          <p className="text-xs text-muted-foreground font-bold uppercase text-center py-6">
            Carregando telas...
          </p>
        ) : permissoesUsuario.length === 0 ? (
          <p className="text-xs text-muted-foreground font-bold uppercase text-center py-6">
            Nenhuma tela cadastrada.
          </p>
        ) : (
          permissoesUsuario.map((tela) => (
            <div
              key={tela.id}
              className="flex items-center justify-between gap-4 p-4 bg-card/30 border border-border/50 rounded-xl"
            >
              <div className="flex flex-col gap-0.5">
                <span className="text-xl font-bold uppercase italic text-foreground">{tela.name}</span>
              </div>
              <Switch
                checked={tela.canAccess}
                onCheckedChange={() => togglePermissao(tela.id)}
              />
            </div>
          ))
        )}
      </div>
    </ScrollArea>

    <DialogFooter>
      <Button
        variant="outline"
        onClick={() => setEditingUser(null)}
        className="font-black text-[10px] uppercase font-sans hover:bg-red-600"
        disabled={savingPermissoes}
      >
        Cancelar
      </Button>
      <Button
        onClick={handleSavePermissoes}
        disabled={savingPermissoes || loadingPermissoes}
        className="font-black text-[11px] uppercase font-sans tracking-widest bg-primary hover:bg-green-600 text-foreground"
      >
        {savingPermissoes ? "Salvando..." : "Salvar Permissões"}
      </Button>
    </DialogFooter>
  </DialogContent>
</Dialog>

          {/* DIALOG: Confirmar Exclusão */}
          <Dialog open={isConfirmDeleteOpen} onOpenChange={setIsConfirmDeleteOpen}>
            <DialogContent className="glass-card border-destructive/20 backdrop-blur-2xl bg-background/95 text-foreground max-w-[400px]">
              <DialogHeader>
                <div className="flex items-center gap-3 text-destructive mb-2">
                  <AlertTriangle className="w-5 h-5" />
                  <span className="text-[10px] font-black uppercase tracking-[0.3em]">Ação Crítica</span>
                </div>
                <DialogTitle className="text-xl font-black uppercase italic tracking-tighter">
                  Remover {userToDelete?.nome}?
                </DialogTitle>
                <p className="text-muted-foreground text-xs font-medium leading-relaxed pt-2 text-center">
                  O usuário perderá acesso imediatamente. Esta operação é irreversível.
                </p>
              </DialogHeader>
              <DialogFooter className="mt-6 flex gap-2">
                <Button
                  variant="ghost"
                  onClick={() => setIsConfirmDeleteOpen(false)}
                  className="font-black text-[10px] text-foreground uppercase tracking-widest hover:bg-white/5 flex-1 font-sans"
                  disabled={deleting}
                >
                  Cancelar
                </Button>
                <Button
                  onClick={handleConfirmDelete}
                  disabled={deleting}
                  className="bg-red-600 hover:bg-red-700 text-white font-black text-[10px] uppercase tracking-widest italic transition-all duration-300 active:scale-95 flex-[1.5] font-sans"
                >
                  {deleting ? "Removendo..." : "Confirmar"}
                </Button>
              </DialogFooter>
            </DialogContent>
          </Dialog>
        </div>
      </SidebarInset>
    </SidebarProvider>
  );
}