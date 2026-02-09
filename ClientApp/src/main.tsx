// Caminho: src/main.tsx
import React from 'react'
import ReactDOM from 'react-dom/client'
import App from './App.tsx'
import './index.css'
import { Toaster } from 'react-hot-toast' // Importa o componente Toaster

ReactDOM.createRoot(document.getElementById('root')!).render(
  <React.StrictMode>
    {/* O componente App é a sua aplicação principal */}
    <App />

    {/* O Toaster fica aqui, no topo da árvore de componentes.
      Ele vai ouvir e exibir qualquer notificação 'toast' chamada em qualquer lugar da aplicação.
    */}
    <Toaster
      position="top-right" // Define a posição das notificações na tela
      reverseOrder={false} // Mantém a ordem padrão das notificações
      toastOptions={{
        // Estilos padrão para os diferentes tipos de toast
        duration: 5000, // Duração de 5 segundos por padrão
        style: {
          background: '#363636',
          color: '#fff',
        },
        // Estilos específicos para sucesso e erro
        success: {
          style: {
            background: '#28a745', // Fundo verde para sucesso
            color: 'white',
          },
        },
        error: {
          style: {
            background: '#dc3545', // Fundo vermelho para erro
            color: 'white',
          },
        },
      }}
    />
  </React.StrictMode>,
)