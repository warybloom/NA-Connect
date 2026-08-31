// Caminho: src/main.tsx
import React from 'react'
import ReactDOM from 'react-dom/client'
import App from './App.tsx'
import './index.css'
import { Toaster } from 'react-hot-toast'
import { PermissionsProvider } from './contexts/PermissionsContext.tsx'
import { ScrollArea } from './components/ui/scroll-area.tsx'

ReactDOM.createRoot(document.getElementById('root')!).render(
  <React.StrictMode>
    {/* Envolva a aplicação com as classes do Tailwind */}
    <ScrollArea className="h-screen bg-background text-foreground transition-colors duration-500 ease-in-out">
    <div className="min-h-screen bg-background text-foreground transition-colors duration-500 ease-in-out">
      <PermissionsProvider>
        <App />
      </PermissionsProvider>
      
    </div>
    </ScrollArea>

    <Toaster
      position="top-right"
      reverseOrder={false}
      toastOptions={{
        duration: 5000,
        style: {
          background: '#363636',
          color: '#fff',
        },
        success: {
          style: {
            background: '#28a745',
            color: 'white',
          },
        },
        error: {
          style: {
            background: '#dc3545',
            color: 'white',
          },
        },
      }}
    />
  </React.StrictMode>,
)