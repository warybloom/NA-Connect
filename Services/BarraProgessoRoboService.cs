using System;

namespace NA_Connect.Services
{
    public class RoboStatusService
    {
        public bool Executando { get; private set; }
        public int Progresso { get; private set; }
        public string EtapaAtual { get; private set; } = "Ocioso";

        public void IniciarExecucao()
        {
            Executando = true;
            Progresso = 0;
            EtapaAtual = "Iniciando processamento...";
        }

        public void AtualizarProgresso(int progresso, string etapa)
        {
            Progresso = Math.Clamp(progresso, 0, 100);
            EtapaAtual = etapa;
        }

        public void FinalizarExecucao(string mensagemFinal = "Concluído com sucesso")
        {
            Executando = false;
            Progresso = 100;
            EtapaAtual = mensagemFinal;
        }

        // 📍 Chamado dentro do catch (Exception ex)
        public void RegistrarErro(string mensagemErro)
        {
            Executando = false;
            EtapaAtual = $"Erro: {mensagemErro}";
        }
    }
}