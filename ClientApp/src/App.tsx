import React from "react";
import { BrowserRouter, Routes, Route, Navigate, Outlet } from "react-router-dom";

// ============================================================================
// 🛡️ CORE & LAYOUTS
// ============================================================================
import ProtectedRoute from "./components/ProtectedRoute";
import Layout from "./components/Layout";

// ============================================================================
// 🌍 PÁGINAS PÚBLICAS & ACESSO MESTRE
// ============================================================================
import LoginPage from "./pages/Index";
import AdminControl from "./pages/MasterAdmin";// Seu Terminal Master (Sem Sidebar)

// ============================================================================
// 🏢 PÁGINAS DO DASHBOARD (CLIENTE / ENGENHEIRO)
// ============================================================================
import DashboardPage from "./pages/Dashboard";
import ProjectsPage from "./pages/Projects";
import FilesPage from "./pages/Files";

// ============================================================================
// 🛠️ MÓDULOS DE AUTOMAÇÃO & ENGENHARIA (N&A CONNECT)
// ============================================================================
import { OCRManager } from "./pages/OCRManager";
import { LDPConfigPage } from "./pages/LDPConfigPage";
import AgendamentoRobo from "./pages/AgendamentoRobo";
import Plant3DDashboard from "./pages/Plant3DDashboard";
import AdminTemplates from "./components/AdminTemplates"; // Motor de Templates

// ============================================================================
// 🧩 CONFIGURAÇÃO DA LDP — Novo Motor (fundação + rodada 2, ver AUDITORIA_FORGESYNC_NA/17-*.md)
// Estado local (LdpConfigProvider) com persistência REAL opcional contra api/ldp-config/* (schema
// `ldp` proposto, precisa das migrations de Migrations/ConfiguracaoLdp/ aplicadas manualmente para
// funcionar). NÃO conectado ao Worker/Tipo_LDP 1/2 atuais.
// ============================================================================
import { LdpConfigProvider } from "./pages/ldp/LdpConfigContext";
import TemplateBuilder from "./pages/ldp/TemplateBuilder";
import CronogramaMapping from "./pages/ldp/CronogramaMapping";
import ExcelEspelho from "./pages/ldp/ExcelGenerator";
import ExcelControle from "./pages/ldp/ExcelControle";

// ============================================================================
// 📊 RELATÓRIOS & PMO (GESTÃO À VISTA)
// ============================================================================
// ⚠️ CONGELADO: "/relatorios" e ClientApp/src/components/pmo/** ficam intocados
// nesta tarefa — mantém a página real do N&A (Reports.tsx), NÃO o TabTimesheet
// original do ForgeSync. Ver AUDITORIA_FORGESYNC_NA/14-TRANSPLANTE-LITERAL-DEFINITIVO.md.
import Reports from "./pages/Reports";
import TabLancamentos from "./components/TabLancamentos";
import { WeeklyPlanningPage, CoordinationStandardsPage, planningRoutePaths } from "./features/planning";
import RobotConfigurationPage from "./features/robot-configuration/RobotConfigurationPage";
import EmDesenvolvimentoPage from "./pages/EmDesenvolvimentoPage";
import CdePage from "./pages/cde/CdePage";
import CdeProjetoPage from "./pages/cde/CdeProjetoPage";
import ControleProducaoPage, { ControleProducaoIndex } from "./features/production/ControleProducaoPage";
import TelasTvConfigPage from "./features/settings/TelasTvConfigPage";
import TemplateConfigPage from "./features/settings/TemplateConfigPage";
import { PermissionProvider } from "./features/permissions/PermissionProvider";
import PermissionRouteGuard, { RotaInicialPermitida } from "./features/permissions/PermissionRouteGuard";
import SidebarManagerPage from "./features/sidebar/SidebarManagerPage";
import PermissoesPage from "./features/permissions/admin/PermissoesPage";
import { ProjetoAtualProvider } from "./features/project/ProjetoAtualContext";
import { RobotExecutionProvider } from "./features/robot/RobotExecutionProvider";
import { RobotExecutionMonitor } from "./features/robot/RobotExecutionMonitor";
import { ProjectIdentityProvider } from "./features/project/ProjectIdentity";
import PainelPlanejadorPage from "./features/planning/PainelPlanejadorPage";
import CustosProfissionaisPage from "./features/planning/costs/CustosProfissionaisPage";
import { CustosSoftwaresPage } from "./features/planning/costs/CustosItensCategoriaPage";
import CustosImpostosProjetoPage from "./features/planning/costs/CustosImpostosProjetoPage";
import CustosPacotesPage from "./features/planning/costs/CustosPacotesPage";
import AuditoriaProjetoPlanejamentoPage from "./features/planning/AuditoriaProjetoPlanejamentoPage";
import { ROTA_AUDITORIA_PROJETO } from "./features/planning/auditoriaProjeto";
import { REDIRECIONAMENTOS_CUSTOS, ROTAS_CUSTOS } from "./features/planning/costs/custosRoutes";
import ConfiguracaoProjetoPage from "./features/planning/ConfiguracaoProjetoPage";
import ConfiguracaoPastasDocumentosPage from "./features/upload-governado/ConfiguracaoPastasDocumentosPage";
import EnvioDocumentosPage from "./features/upload-governado/EnvioDocumentosPage";
import CdeDocumentosPage from "./pages/cde/CdeDocumentosPage";
import IdentidadeVisualPage from "./features/project/IdentidadeVisualPage";
import TvPlayerPage from "./features/tv/TvPlayerPage";
import ExtracoesEngenhariaPage from "./features/reports/ExtracoesEngenhariaPage";
import DocumentQualityAuditPage from "./features/document-quality/DocumentQualityAuditPage";
import DocumentPresenceAuditPage from "./features/document-quality/DocumentPresenceAuditPage";
import DisciplineAuditPage from "./features/document-quality/DisciplineAuditPage";
import InformationQualityAuditPage from "./features/document-quality/InformationQualityAuditPage";
import PhysicalProgressSettingsPage from "./features/document-quality/PhysicalProgressSettingsPage";
import TimesheetUnassignedQualityPage from "./features/document-quality/TimesheetUnassignedQualityPage";
import ComparativoQualidadePage from "./features/document-quality/ComparativoQualidadePage";
import AuditoriaDivergenciasPage from "./features/document-quality/AuditoriaDivergenciasPage";
import EngineeringProfessionalScopePage from "./features/document-quality/EngineeringProfessionalScopePage";

/**
 * 🚀 N&A Forge Sync Connect - Router Principal
 * Arquitetura de rotas centralizada e protegida.
 */
const App: React.FC = () => {
  return (
    <BrowserRouter>
      <Routes>

        {/* ==========================================
            🔓 ZONA PÚBLICA
        ========================================== */}
        <Route path="/login" element={<LoginPage />} />

        {/* ==========================================
            🔐 ZONA PROTEGIDA (Requer Autenticação)
        ========================================== */}
        <Route element={<ProtectedRoute />}>
        {/* Permissões efetivas (já resolvidas no backend) para menu, rotas e telas. Toda rota abaixo passa pelo
            PermissionRouteGuard (ROUTE_PERMISSIONS); a validação definitiva é o [RequirePermission] das APIs. */}
        {/* Robô: UM provider de status (polling único) + UM monitor flutuante para Home e áreas internas — fora do <Layout />, pois a Home não usa Layout. */}
        <Route element={<PermissionProvider><RobotExecutionProvider><ProjetoAtualProvider><ProjectIdentityProvider><Outlet /></ProjectIdentityProvider></ProjetoAtualProvider><RobotExecutionMonitor /></RobotExecutionProvider></PermissionProvider>}>

          {/* 👑 ROTA MASTER ADMIN
              Fica FORA do <Layout /> propositalmente para não herdar a Sidebar.
              Exige system.tenants.view (a "Chave Mestra" client-side antiga não é segurança). */}
          <Route element={<PermissionRouteGuard />}>
            <Route path="/master-admin" element={<AdminControl />} />
            {/* "/" leva à primeira área que o usuário pode acessar (normalmente a Home). */}
            <Route path="/" element={<RotaInicialPermitida />} />
            {/* HOME: cards de projeto, SEM sidebar. Escolher um card define o projeto atual (ProjetoAtualContext)
                e entra no ambiente do projeto (com sidebar). */}
            <Route path="/dashboard" element={<DashboardPage />} />
            {/* Gestão à Vista (player TV) em tela cheia; a apresentação é montada em Configuração de Telas para TV. */}
            <Route path="/gestao-a-vista" element={<TvPlayerPage />} />
          </Route>

          {/* 🏢 ROTAS DE SISTEMA (Com Sidebar e Header padrão) */}
          <Route element={<Layout />}>
          <Route element={<PermissionRouteGuard />}>

            {/* Configuração dos Projetos (antigo /painel) = Painel de Planejamento → Configuração do Projeto. Rotas antigas redirecionam. */}
            <Route path="/planejamento/configuracao-do-projeto" element={<ConfiguracaoProjetoPage />} />
            {/* Qualidade: auditoria de conteúdo, configuração das árvores documentais e reconciliação LDP x ACC. */}
            <Route path="/qualidade/auditoria" element={<InformationQualityAuditPage />} />
            <Route path="/qualidade/hh-sem-documento" element={<TimesheetUnassignedQualityPage />} />
            <Route path="/qualidade/classificacao-profissionais" element={<EngineeringProfessionalScopePage />} />
            <Route path="/qualidade/comparativo-hh-custos" element={<ComparativoQualidadePage />} />
            <Route path="/qualidade/auditoria-divergencias" element={<AuditoriaDivergenciasPage />} />
            <Route path="/qualidade/configuracao-avanco-fisico" element={<PhysicalProgressSettingsPage />} />
            <Route path="/qualidade/auditoria-documentos" element={<DocumentQualityAuditPage />} />
            <Route path="/qualidade/configuracao-pastas-documentos" element={<ConfiguracaoPastasDocumentosPage />} />
            <Route path="/qualidade/auditoria-ldp-acc" element={<DocumentPresenceAuditPage />} />
            <Route path="/qualidade/auditoria-disciplinas" element={<DisciplineAuditPage />} />
            {/* Rotas antigas preservadas por compatibilidade. */}
            <Route path="/planejamento/auditoria-qualidade-documentos" element={<Navigate to="/qualidade/auditoria-documentos" replace />} />
            <Route path="/planejamento/pastas-documentos" element={<Navigate to="/qualidade/configuracao-pastas-documentos" replace />} />
            {/* Rota antiga de envio mantida só por compatibilidade: o envio da produção é feito dentro das pastas (FilesPage). */}
            <Route path="/cde/envio-documentos" element={<EnvioDocumentosPage />} />
            {/* CDE → Documentos: Project Files do projeto atual (redireciona ao FilesPage). */}
            <Route path="/cde/documentos" element={<CdeDocumentosPage />} />
            {/* Configurações → Identidade Visual dos Projetos (logo; mesma implementação de antes). */}
            <Route path="/configuracoes/identidade-visual" element={<IdentidadeVisualPage />} />
            <Route path="/painel" element={<Navigate to="/planejamento/configuracao-do-projeto" replace />} />
            <Route path="/admin" element={<Navigate to="/planejamento/configuracao-do-projeto" replace />} />

            {/* Configurações de Automação */}
            <Route path="/ocr" element={<Navigate to="/configuracoes/templates/auditoria-documental" replace />} />
            <Route path="/agendamento" element={<AgendamentoRobo />} />
            <Route path="/configurar-ldp" element={<LDPConfigPage />} />
            <Route path="/templates" element={<AdminTemplates />} />

            {/* Painel de Planejamento → Programação Semanal: Vigente = tela real existente; as outras duas ainda sem
                implementação (placeholder, sem dados inventados). A rota antiga redireciona para a Vigente. */}
            <Route path={planningRoutePaths.weekly} element={<Navigate to={planningRoutePaths.weeklyCurrent} replace />} />
            <Route path={planningRoutePaths.weeklyCurrent} element={<WeeklyPlanningPage />} />
            <Route path={planningRoutePaths.weeklyPrevious} element={<EmDesenvolvimentoPage title="Análise da Programação da Semana Anterior"
              description="Comparação da programação da semana anterior com o realizado. Será disponibilizada em uma próxima fase." />} />
            <Route path={planningRoutePaths.weeklyHistogram} element={<EmDesenvolvimentoPage title="Histograma do Projeto"
              description="Histograma de HH do projeto por semana e disciplina. Será disponibilizado em uma próxima fase." />} />
            <Route path={planningRoutePaths.planner} element={<PainelPlanejadorPage />} />
            {/* Custos do Projeto: grupo com 4 telas (custosRoutes.ts). /planejamento/custos-do-projeto (página única antiga) redireciona. */}
            <Route path={ROTAS_CUSTOS.impostos} element={<CustosImpostosProjetoPage />} />
            <Route path={ROTAS_CUSTOS.profissionais} element={<CustosProfissionaisPage />} />
            <Route path={ROTAS_CUSTOS.softwares} element={<CustosSoftwaresPage />} />
            <Route path={ROTAS_CUSTOS.pacotes} element={<CustosPacotesPage />} />
            {/* Auditoria do Projeto (LDP × Timesheet): saiu do Controle de Produção; /controle-producao/auditoria redireciona. */}
            <Route path={ROTA_AUDITORIA_PROJETO} element={<AuditoriaProjetoPlanejamentoPage />} />
            {Object.entries(REDIRECIONAMENTOS_CUSTOS).map(([de, para]) => <Route key={de} path={de} element={<Navigate to={para} replace />} />)}
            <Route path={planningRoutePaths.coordination} element={<CoordinationStandardsPage />} />
            <Route path="/automacao/configuracao-robo" element={<RobotConfigurationPage />} />

            {/* Configuração da LDP — Novo Motor (fundação + rodada 2) */}
            <Route path="/configuracao-ldp" element={<LdpConfigProvider><Outlet /></LdpConfigProvider>}>
              <Route index element={<Navigate to="/configuracao-ldp/template" replace />} />
              <Route path="template" element={<TemplateBuilder />} />
              <Route path="cronograma" element={<CronogramaMapping />} />
              <Route path="espelho" element={<ExcelEspelho />} />
              <Route path="controle" element={<ExcelControle />} />
              {/* Rota antiga preservada como redirect — evita links quebrados de favoritos/histórico */}
              <Route path="excel" element={<Navigate to="/configuracao-ldp/espelho" replace />} />
            </Route>

            {/* Relatórios e Lançamentos (PMO) */}
            <Route path="/relatorios" element={<Reports />} />
            <Route path="/extracoes-engenharia" element={<ExtracoesEngenhariaPage />} />
            <Route path="/lancamentos" element={<TabLancamentos />} />
            <Route path="/auditoria-3d" element={<Plant3DDashboard />} />
            {/* Sidebar fase 1: mesma página Plant 3D, aba escolhida por ?tab= (ver MainTabs). */}
            <Route path="/plant3d" element={<Plant3DDashboard />} />

            {/* CDE → Documentos saiu do menu (a Lista de Documentos agora é tela do Controle de Produção); as rotas
                continuam válidas para links/favoritos. */}
            <Route path="/cde" element={<CdePage />} />
            <Route path="/cde/:hubId/:projectId" element={<CdeProjetoPage />} />

            {/* Controle de Produção: uma rota plana por tela (/controle-producao/<tela>), definida em
                features/production/productionRoutes.ts. "/controle-producao", ?tela= e os caminhos antigos com
                /gerencial|/operacional redirecionam. */}
            <Route path="/controle-producao" element={<ControleProducaoIndex />} />
            <Route path="/controle-producao/comparativo-hh-custos" element={<Navigate to="/qualidade/comparativo-hh-custos" replace />} />
            <Route path="/controle-producao/*" element={<ControleProducaoPage />} />

            {/* CDE → Modelo 3D / Planejamento 4D / Orçamento 5D: ainda sem tela funcional (placeholder). */}
            <Route path="/modelo-3d" element={<EmDesenvolvimentoPage title="Modelo 3D" />} />
            <Route path="/modelo-federado" element={<Navigate to="/modelo-3d" replace />} />
            <Route path="/planejamento-4d" element={<EmDesenvolvimentoPage title="Planejamento 4D" />} />
            <Route path="/orcamento-5d" element={<EmDesenvolvimentoPage title="Orçamento 5D" />} />

            {/* Navegação de Projetos (BIM 360 / ACC) */}
            <Route path="/projects/:hubId" element={<ProjectsPage />} />
            <Route path="/projects/:hubId/:projectId/:folderId" element={<FilesPage />} />
            <Route path="/projetos/:projectId/conteudo/:folderId" element={<FilesPage />} />

            {/* Configurações → Configuração de Telas para TV e Configuração de Templates (um editor por tipo). */}
            <Route path="/configuracoes/telas-tv" element={<TelasTvConfigPage />} />
            <Route path="/configuracoes/templates/:tipo" element={<TemplateConfigPage />} />
            <Route path="/configuracoes/templates/auditoria-documental" element={<OCRManager />} />

            {/* Administração de permissões (perfis + usuários) — permissions.manage. */}
            <Route path="/configuracoes/sidebar" element={<SidebarManagerPage />} />
            <Route path="/configuracoes/permissoes" element={<PermissoesPage />} />

          </Route>
          </Route>
        </Route>
        </Route>

        {/* Fallback para rotas não encontradas (Opcional, redireciona pro Dashboard se logado ou login se não) */}
        <Route path="*" element={<Navigate to="/" replace />} />

      </Routes>
    </BrowserRouter>
  );
};

export default App;
