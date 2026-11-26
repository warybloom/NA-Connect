from pathlib import Path
root=Path(r'C:\Dev\N-A-Connect aa\N-A-Connect')
a=root/'ClientApp/src/App.tsx'
s=a.read_text(encoding='utf-8')
assert 'import Layout from "./components/Layout";' in s
if 'SidebarManagerPage' not in s:
 s=s.replace('import Layout from "./components/Layout";','import Layout from "./components/Layout";\nimport SidebarManagerPage from "./pages/SidebarManagerPage";')
 s=s.replace('<Route path="/configuracoes/permissoes" element={<PermissoesPage />} />','<Route path="/configuracoes/permissoes" element={<PermissoesPage />} />\n            <Route path="/configuracoes/sidebar" element={<SidebarManagerPage />} />')
a.write_text(s,encoding='utf-8')
p=root/'ClientApp/src/components/Layout.tsx'
s=p.read_text(encoding='utf-8')
find='import { useProjetoAtual } from "@/features/project/ProjetoAtualContext";'
assert find in s
if 'configuredNavigation' not in s:
 s=s.replace(find,find+'\nimport { configuredNavigation, sidebarFetch, type SidebarConfiguration } from "@/features/sidebar/sidebarConfig";')
s=s.replace('{ label: "Permissões", icon: KeyRound, to: "/configuracoes/permissoes", permissionKey: "permissions.manage" },',
'{ label: "Gerenciador de Sidebar", icon: Settings, to: "/configuracoes/sidebar", permissionKey: "permissions.manage" },\n      { label: "Permissões", icon: KeyRound, to: "/configuracoes/permissoes", permissionKey: "permissions.manage" },')
find='  const [isSidebarOpen, setIsSidebarOpen] = useState(true);'
assert find in s
if 'setOfficialSidebar' not in s:
 s=s.replace(find,find+'''
  const [officialSidebar, setOfficialSidebar] = useState<SidebarConfiguration | null>(null);
  useEffect(() => {
    let mounted=true;
    const load=()=>{
      sidebarFetch<{configuration:SidebarConfiguration}>()
        .then(data=>{if(mounted)setOfficialSidebar(data.configuration.revision ? data.configuration : null);})
        .catch(()=>{if(mounted)setOfficialSidebar(null);});
    };
    load();
    const interval=window.setInterval(load,15000);
    window.addEventListener("na-sidebar-updated",load);
    return ()=>{mounted=false;window.clearInterval(interval);window.removeEventListener("na-sidebar-updated",load);};
  }, []);
''')
needle='  const navegacaoVisivel = useMemo(() => permissionLoading ? [] : filtrarPorPermissao(navegacaoComCustosRestritos, can), [permissionLoading, navegacaoComCustosRestritos, can]);'
assert needle in s
s=s.replace(needle,'''  const navegacaoOficial = useMemo(() => officialSidebar ? configuredNavigation(navegacaoComCustosRestritos, officialSidebar.items) : navegacaoComCustosRestritos,
    [navegacaoComCustosRestritos, officialSidebar]);
  const navegacaoVisivel = useMemo(() => permissionLoading ? [] : filtrarPorPermissao(navegacaoOficial, can), [permissionLoading, navegacaoOficial, can]);''')
p.write_text(s,encoding='utf-8')
print('Wired App and Layout')
