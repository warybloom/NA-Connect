// Caminho: ClientApp/src/App.tsx
import { BrowserRouter, Routes, Route } from "react-router-dom";
import ProtectedRoute from "./components/ProtectedRoute";
import LoginPage from "./pages/Index";
import DashboardPage from "./pages/Dashboard";
import ProjectsPage from "./pages/Projects";
import FilesPage from "./pages/Files"; // Garanta que a importação está correta
import NotFoundPage from "./pages/NotFound";

const App = () => (
    <BrowserRouter>
        <Routes>
            <Route path="/login" element={<LoginPage />} />
            <Route element={<ProtectedRoute />}>
                <Route path="/" element={<DashboardPage />} />
                <Route path="/dashboard" element={<DashboardPage />} />
                <Route path="/projects/:hubId" element={<ProjectsPage />} />
                
                {/* ✅ ESTA É A ROTA CRÍTICA QUE DEVE CORRESPONDER AO LINK */}
                <Route path="/projetos/:projectId/conteudo/:folderId" element={<FilesPage />} />
            </Route>
            <Route path="*" element={<NotFoundPage />} />
        </Routes>
    </BrowserRouter>
);

export default App;