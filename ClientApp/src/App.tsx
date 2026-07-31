// Caminho: ClientApp/src/App.tsx
import { BrowserRouter, Routes, Route, useParams, useNavigate, Navigate } from "react-router-dom";
import ProtectedRoute from "./components/ProtectedRoute";
import LoginPage from "./pages/Index";
import DashboardPage from "./pages/Dashboard";
import ProjectsPage from "./pages/Projects";
import ControlPanel from "./pages/ControlPanel";
import FilesPage from "./pages/Files";
import NotFoundPage from "./pages/NotFound";
import { ThemeProvider } from "./components/provedor-de-tema";
import VisionStudio from "./pages/VisionStudio";
import AgendamentoRobo from "./pages/AgendamentoRobo";
import { LDPConfigPage } from "./pages/LDPConfigPage";
import Plant3DAuditHub from "./pages/Plant3DAuditHub";
import Plant3DDashboard from "./components/Plant3DDashboard";
import { OCRCalibrator } from "./pages/OCRCalibrator";
import AdminControl from "./pages/AdminControl";
import AdminTemplates from "./pages/AdminTemplates";
import TabTimesheet from "./components/TabTimesheet";
import TabLancamentos from "./components/TabLancamentos";
import { OCRManager } from "./pages/OCRManager";
import TabSettings from "./components/plant3d/TabSettings";
import AndamentoRobo from "./pages/AndamentoRobo";

const Plant3DAuditHubWrapper = () => {
  const { projectId } = useParams<{ projectId: string }>();
  return <Plant3DAuditHub projectId={projectId || ""} />;
};

const OcrCalibratorWrapper = () => {
  const navigate = useNavigate();
  const { pacoteId, tipoFolha } = useParams<{ pacoteId?: string; tipoFolha?: string }>();

  return (
    <OCRCalibrator
      pacoteId={pacoteId ? parseInt(pacoteId, 10) : 0}
      tipoFolha={tipoFolha || "A1"}
      onBack={() => navigate(-1)}
    />
  );
};

const App = () => (
  <ThemeProvider defaultTheme="dark" storageKey="vite-ui-theme">
    <BrowserRouter>
      <Routes>
        <Route path="/login" element={<LoginPage />} />

        {/* Rotas Protegidas */}
        <Route element={<ProtectedRoute />}>

          {/* Visão Geral — redireciona a raiz direto para Projects */}
          <Route path="/" element={<Navigate to="/projects" replace />} />
          <Route path="/dashboard" element={<DashboardPage />} />

          {/* Projetos */}
          <Route path="/projects" element={<ProjectsPage />} />
          <Route path="/projects/:hubId" element={<ProjectsPage />} />
          <Route path="/projetos/:projectId/conteudo/:folderId" element={<FilesPage />} />

          {/* 🔴 NOVA ROTA ADICIONADA AQUI */}
          <Route path="/painel-de-controle" element={<ControlPanel />} />
          <Route path="/vision-studio" element={<VisionStudio />} />
          <Route path="/agend-robo" element={<AgendamentoRobo />} />
          <Route path="/confi-ldp" element={<LDPConfigPage />} />
          <Route path="/platn3d-audit/:projecId" element={<Plant3DAuditHubWrapper />} />
          <Route path="/folhas/:pacoteId/:tipoFolha" element={<OcrCalibratorWrapper />} />
          <Route path="/admincontrol" element={<AdminControl />} />
          <Route path="/motor-templates" element={<AdminTemplates />} />
          <Route path="/relatorio-gen" element={<TabTimesheet />} />
          <Route path="/lançamentos-pmo" element={<TabLancamentos />} />
          <Route path="/calibração-visão" element={<OCRManager />} />
          <Route path="/auditoria-platn3d" element={<TabSettings />} />
          <Route path="/andamento-robo" element = {<AndamentoRobo/>} />

        </Route>

        <Route path="*" element={<NotFoundPage />} />
      </Routes>
    </BrowserRouter>
  </ThemeProvider>
);

export default App;