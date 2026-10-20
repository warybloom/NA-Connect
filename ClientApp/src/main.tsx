import { TvPortalEntry } from "./features/tv/portal/TvPortalEntry";
import React from 'react';
import ReactDOM from 'react-dom/client';
import App from './App.tsx';
// ✅ Importante: O index.css deve ser importado aqui para aplicar o reset do Tailwind e os tokens dos temas Claro/Escuro
import './index.css';
import { Toaster } from 'react-hot-toast';
import { ThemeProvider } from './features/theme/ThemeProvider';

/**
 * Ponto de entrada da aplicação Forges Sync Connect.
 * O Toaster está configurado para fornecer feedback visual às operações do DataController.cs,
 * como a sincronização de LDP e a criação de placeholders.
 */
ReactDOM.createRoot(document.getElementById('root')!).render(
  <React.StrictMode>
    {/* Tema global (Claro/Escuro/Automático) — envolve TODA a aplicação, inclusive Login e Home. */}
    <ThemeProvider>
      <TvPortalEntry><App /></TvPortalEntry>
      <Toaster
        position="top-right"
        reverseOrder={false}
        toastOptions={{
          duration: 5000,
          // Cores pelos tokens do tema (index.css), para acompanhar Claro/Escuro.
          style: {
            background: 'hsl(var(--popover))',
            color: 'hsl(var(--popover-foreground))',
            border: '1px solid hsl(var(--border))',
          },
          success: {
            style: {
              background: '#065F46', // Verde escuro para sucessos de sincronização (contraste com texto branco nos dois temas)
              color: 'white',
            },
          },
          error: {
            style: {
              background: '#991B1B', // Vermelho para erros de API (404/500)
              color: 'white',
            },
          },
        }}
      />
    </ThemeProvider>
  </React.StrictMode>,
);
