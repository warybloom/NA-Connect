import { useEffect, useState } from "react";
import { usePermissions } from "../permissions/PermissionProvider";
import { SidebarApiError, sidebarRequest, type SidebarAccess } from "./sidebarApi";
import { validateItems, type SidebarConfiguration } from "./sidebarModel";
export interface SidebarManagerAccess { allowed:boolean; loading:boolean; email?:string; error?:SidebarApiError }
/** Ferramenta global: não depende do projeto atual. Falha da API (error) nunca é tratada como "sem permissão". */
export function useSidebarManagerAccess(): SidebarManagerAccess {
  const {effective}=usePermissions();
  const [state,setState]=useState<{subject: typeof effective}&SidebarManagerAccess|null>(null);
  useEffect(()=>{const ctrl=new AbortController();setState(null);
    sidebarRequest<SidebarAccess>("/access","GET",undefined,undefined,ctrl.signal).then(r=>{if(!ctrl.signal.aborted)setState({subject:effective,allowed:r.allowed===true,email:r.email,loading:false});}).catch(e=>{if(!ctrl.signal.aborted)setState({subject:effective,allowed:false,loading:false,error:e instanceof SidebarApiError?e:new SidebarApiError("Sem conexão com a API do N&A Connect.","network",0)});});
    return()=>ctrl.abort();},[effective]);
  return state?.subject===effective?state:{allowed:false,loading:true};
}
export function useOfficialSidebar(projectId?:string) {
  const {effective}=usePermissions();const [epoch,setEpoch]=useState(0);
  const [state,setState]=useState<{subject:typeof effective;projectId?:string;configuration:SidebarConfiguration}|null>(null);
  useEffect(()=>{const reload=()=>setEpoch(x=>x+1);window.addEventListener("sidebar-published",reload);window.addEventListener("focus",reload);return()=>{window.removeEventListener("sidebar-published",reload);window.removeEventListener("focus",reload);};},[]);
  useEffect(()=>{const ctrl=new AbortController();setState(null);
    sidebarRequest<SidebarConfiguration>(projectId?"?projectId="+encodeURIComponent(projectId):"","GET",undefined,undefined,ctrl.signal).then(configuration=>{
      if(!Number.isSafeInteger(configuration.revision)||configuration.revision<0)throw Error("Revisão inválida.");validateItems(configuration.items,false);
      if(!ctrl.signal.aborted)setState({subject:effective,projectId,configuration});
    }).catch(()=>{if(!ctrl.signal.aborted)setState(null);});return()=>ctrl.abort();},[effective,projectId,epoch]);
  return state?.subject===effective&&state.projectId===projectId&&state.configuration.revision>0?state.configuration:null;
}
