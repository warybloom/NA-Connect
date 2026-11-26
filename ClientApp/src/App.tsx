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

import Plant3DDashboard, { Plant3DResultsPage, Plant3DMappingPage } from "./pages/Plant3DDashboard";
import PlantResumoPage from "./features/plant3d/PlantResumoPage";
import PlantFaseResultadoPage from "./features/plant3d/PlantFaseResultadoPage";
import PlantCatalogoPage from "./features/plant3d/PlantCatalogoPage";
import PlantCriteriosPage from "./features/plant3d/PlantCriteriosPage";
import { GeralFaseRedirect } from "./features/plant3d/PlantRedirects";
import { PLANT_REDIRECIONAMENTOS, PLANT_ROTAS, TELAS_FASE, padraoTelaFase } from "./features/plant3d/plantRoutes";
import PlantQualityPage from "./features/plant3d/PlantQualityPage";
import PlantInventoryPage from "./features/plant3d/PlantInventoryPage";
import PlantItemResultsPage from "./features/plant3d/PlantItemResultsPage";
import PlantConsistencyDetailPage from "./features/plant3d/PlantConsistencyDetailPage";
import PlantReportsCenterPage from "./features/plant3d/PlantReportsCenterPage";
import PlantReportConfigPage from "./features/plant3d/PlantReportConfigPage";
import Plant4DPage from "./features/plant3d/Plant4DPage";
import PlantPhaseBiPage from "./features/plant3d/PlantPhaseBiPage";

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
import NormalizadorSiglasPage from "./features/settings/normalizador-siglas/NormalizadorSiglasPage";
import FluxosRevisaoPage from "./features/planning/fluxos-revisao/FluxosRevisaoPage";
import { ROTA_FLUXOS_REVISAO, ROTA_FLUXOS_REVISAO_ANTIGA } from "./features/planning/fluxos-revisao/fluxosRevisaoRotas";
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
import EmissionStatusesPage from "./features/planning/EmissionStatusesPage";
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
import LegacyProjectsPage from "./features/legacy-projects/LegacyProjectsPage";
import EngineeringProfessionalScopePage from "./features/document-quality/EngineeringProfessionalScopePage";
import BiBml011016Page from "./features/production/biBml011016/BiBml011016Page";
import { MasterAdminOnly } from "./features/production/biBml011016/MasterAdminOnly";

// Plant 3D — telas das Fases 3–6 (rota → título), todas "em desenvolvimento" até serem entregues.
const PLANT_EM_BREVE: ReadonlyArray<readonly [string, string]> = [
  [PLANT_ROTAS.configuracao.tieIns, "Configuração · Tie-ins"],
];

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
          <Route element={<MasterAdminOnly />}>
            <Route path="/bi-bml011016/:slug" element={<BiBml011016Page />} />
          </Route>
          <Route element={<PermissionRouteGuard />}>

            {/* Configuração dos Projetos (antigo /painel) = Painel de Planejamento → Configuração do Projeto. Rotas antigas redirecionam. */}
            <Route path="/planejamento/configuracao-do-projeto" element={<ConfiguracaoProjetoPage />} />
            <Route path="/planejamento/status-validos-emissao" element={<EmissionStatusesPage />} />
            {/* Qualidade: auditoria de conteúdo, configuração das árvores documentais e reconciliação LDP x ACC. */}
            <Route path="/qualidade/auditoria" element={<InformationQualityAuditPage />} />
            <Route path="/auditoria/projetos-sem-ldp" element={<LegacyProjectsPage />} />
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
            <Route path="/agendamento" element={<Navigate to="/automacao/configuracao-robo" replace />} />
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
            {/* Plant 3D: cada tela é independente, todas acessadas pelo sidebar. */}
            <Route path="/plant3d/resultados" element={<Navigate to={PLANT_ROTAS.resumo} replace />} />
            <Route path="/plant3d/configuracao" element={<Navigate to="/plant3d/configuracao/de-para-tags" replace />} />
            <Route path={PLANT_ROTAS.resumo} element={<PlantResumoPage />} />
            {/* Rotas antigas ("Geral") preservadas como redirecionamento. */}
            <Route path={PLANT_REDIRECIONAMENTOS.geral} element={<Navigate to={PLANT_ROTAS.resumo} replace />} />
            <Route path={PLANT_REDIRECIONAMENTOS.geralFase} element={<GeralFaseRedirect />} />
            <Route path={PLANT_REDIRECIONAMENTOS.planejamento4dAntigo} element={<Navigate to={PLANT_ROTAS.planejamento4d} replace />} />
            {/* Fases dinâmicas (uma por fase salva): 3 telas cada. A fase vem da URL e é validada contra /phases. */}
            {TELAS_FASE.map(t => <Route key={t.slug} path={padraoTelaFase(t.slug)} element={<PlantFaseResultadoPage tela={t.slug} />} />)}
            <Route path="/plant3d/resultados/auditoria" element={<Plant3DResultsPage />} />
            <Route path="/plant3d/resultados/qualidade" element={<PlantQualityPage />} />
            <Route path="/plant3d/resultados/consistencia" element={<PlantConsistencyDetailPage />} />
            <Route path="/plant3d/configuracao/linhas" element={<PlantInventoryPage category="line" />} />
            <Route path="/plant3d/configuracao/equipamentos" element={<PlantInventoryPage category="equip" />} />
            <Route path="/plant3d/configuracao/instrumentos" element={<PlantInventoryPage category="inst" />} />
            <Route path="/plant3d/configuracao/valvulas" element={<PlantInventoryPage category="valve" />} />
            <Route path={PLANT_ROTAS.configuracao.catalogo} element={<PlantCatalogoPage />} />
            <Route path={PLANT_ROTAS.configuracao.criterios} element={<PlantCriteriosPage />} />
            {/* Itens das Fases 3–6 ainda não implementados: placeholders honestos, sem dados fictícios. */}
            {PLANT_EM_BREVE.map(([to, title]) => (
              <Route key={to} path={to} element={<EmDesenvolvimentoPage title={`${title} — Plant 3D`} description="Tela prevista para uma próxima fase. Nenhum dado é exibido ou simulado enquanto ela não for entregue." />} />
            ))}
            {/* Resultados por item: inventários aprovados e indicadores reais. */}
            <Route path={PLANT_ROTAS.resultados.linhas} element={<PlantItemResultsPage category="line" />} />
            <Route path={PLANT_ROTAS.resultados.equipamentos} element={<PlantItemResultsPage category="equip" />} />
            <Route path={PLANT_ROTAS.resultados.instrumentos} element={<PlantItemResultsPage category="inst" />} />
            <Route path={PLANT_ROTAS.resultados.valvulas} element={<PlantItemResultsPage category="valve" />} />
            <Route path={PLANT_ROTAS.resultados.tieIns} element={<PlantItemResultsPage category="tieins" />} />
            <Route path={PLANT_ROTAS.planejamento4d} element={<Plant4DPage />} />
            <Route path={PLANT_ROTAS.criterioMedicao} element={<PlantPhaseBiPage />} />
            {/* Central de relatórios: os filtros globais valem também para exportação. */}
            <Route path={PLANT_ROTAS.relatorios.linhas} element={<PlantReportsCenterPage kind="lines" />} />
            <Route path={PLANT_ROTAS.relatorios.equipamentos} element={<PlantReportsCenterPage kind="equipment" />} />
            <Route path={PLANT_ROTAS.relatorios.instrumentos} element={<PlantReportsCenterPage kind="instruments" />} />
            <Route path={PLANT_ROTAS.relatorios.valvulas} element={<PlantReportsCenterPage kind="valves" />} />
            <Route path={PLANT_ROTAS.relatorios.tieIns} element={<PlantReportsCenterPage kind="tie-ins" />} />
            <Route path={PLANT_ROTAS.relatorios.materiais} element={<PlantReportsCenterPage kind="materials" />} />
            <Route path={PLANT_ROTAS.relatorios.suportes} element={<PlantReportsCenterPage kind="supports" />} />
            <Route path={PLANT_ROTAS.relatorios.configuracao} element={<PlantReportConfigPage />} />
            <Route path="/plant3d/configuracao/de-para-tags" element={<Plant3DMappingPage />} />
            {/* URLs antigas preservadas com redirecionamento. */}
            <Route path="/auditoria-3d" element={<Plant3DDashboard />} />
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
            {/* Configurações → Normalizador de Siglas (agregação de disciplinas nos painéis multiprojeto). */}
            <Route path="/configuracoes/normalizador-siglas" element={<NormalizadorSiglasPage />} />
            {/* Configurações → Fluxos de Revisão (etapas da 03. Documentação; /api/review-flows). Sem item de menu ainda: catálogo da sidebar é gerado com o backend. */}
            {/* Fluxos de Revisão pertence ao Painel de Planejamento; a rota antiga de Configurações só redireciona. */}
            <Route path={ROTA_FLUXOS_REVISAO} element={<FluxosRevisaoPage />} />
            <Route path={ROTA_FLUXOS_REVISAO_ANTIGA} element={<Navigate to={ROTA_FLUXOS_REVISAO} replace />} />
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
