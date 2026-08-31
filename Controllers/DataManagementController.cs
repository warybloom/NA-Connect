// Caminho: Controllers/DataManagementController.cs
using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using APS.WebApp.Services;
using NA_Connect.Services;
using System.Text;

namespace NA_Connect.Controllers
{
    [ApiController]
    [Route("DataManagement")]
    public class DataManagementController : ControllerBase
    {
        private readonly GrdAutomationWorker _worker;
        private readonly RoboStatusService _statusRobo;
        private readonly ILogger<DataManagementController> _logger;
        private const string LogsFolderPath = @"C:\Dev\N-A-Connect\Logs";

        // 🎯 CORRIGIDO: removido o parâmetro duplicado "roboWorker" (o campo _roboWorker
        // nunca era atribuído, o que causava NullReferenceException em ExecutarRoboProjeto).
        // Agora usamos apenas _worker, já injetado corretamente, em todos os endpoints.
        public DataManagementController(
            GrdAutomationWorker worker,
            RoboStatusService statusRobo,
            ILogger<DataManagementController> logger)
        {
            _worker = worker;
            _statusRobo = statusRobo;
            _logger = logger;
        }

        [HttpPost("IniciarRobo")]
        public IActionResult IniciarRobo()
        {
            if (_worker.EstaExecutando)
            {
                return Conflict(new { message = "O robô já está em execução no momento." });
            }

            _ = Task.Run(async () =>
            {
                try
                {
                    await _worker.ProcessarCicloCompleto(CancellationToken.None);
                }
                catch (Exception ex)
                {
                    _logger.LogError($"Erro ao rodar ciclo manual do robô: {ex}");
                }
            });

            return Accepted(new { message = "Robô iniciado em background." });
        }

        // 🎯 Endpoint real: POST /DataManagement/ExecutarRoboProjeto?hubId=...&projectId=...
        [HttpPost("ExecutarRoboProjeto")]
        public IActionResult ExecutarRoboProjeto(string hubId, string projectId)
        {
            if (string.IsNullOrEmpty(projectId))
                return BadRequest(new { message = "projectId é obrigatório." });

            if (_worker.EstaExecutando)
                return Conflict(new { message = "O robô já está em execução. Aguarde a conclusão." });

            _ = Task.Run(async () =>
            {
                try
                {
                    await _worker.ExecutarParaProjetoAsync(projectId);
                }
                catch (Exception ex)
                {
                    _logger.LogError($"Erro ao rodar robô para o projeto {projectId}: {ex}");
                }
            });

            return Ok(new { message = "Execução do robô iniciada para este projeto." });
        }

        [HttpGet("StatusRobo")]
        public IActionResult StatusRobo()
        {
            return Ok(new
            {
                executando = _statusRobo.Executando,
                progresso = _statusRobo.Progresso,
                etapaAtual = _statusRobo.EtapaAtual
            });
        }

        [HttpGet("Logs")]
        public IActionResult ListarLogs()
        {
            try
            {
                if (!Directory.Exists(LogsFolderPath))
                {
                    return Ok(new { logs = Array.Empty<object>() });
                }

                var arquivos = Directory.GetFiles(LogsFolderPath, "Execucao_Completa_*.txt")
                    .Select(caminho => new FileInfo(caminho))
                    .OrderByDescending(f => f.LastWriteTime)
                    .Select(f => new
                    {
                        nomeArquivo = f.Name,
                        dataExecucao = f.LastWriteTime,
                        tamanhoKb = Math.Round(f.Length / 1024.0, 1)
                    })
                    .ToList();

                return Ok(new { logs = arquivos });
            }
            catch (Exception ex)
            {
                _logger.LogError($"Erro ao listar logs: {ex}");
                return BadRequest(new { message = "Falha ao listar os logs.", error = ex.Message });
            }
        }

        [HttpGet("Logs/{nomeArquivo}")]
        public IActionResult LerLog(string nomeArquivo)
        {
            try
            {
                string nomeSeguro = Path.GetFileName(nomeArquivo);
                string caminhoCompleto = Path.Combine(LogsFolderPath, nomeSeguro);

                if (!System.IO.File.Exists(caminhoCompleto))
                {
                    return NotFound(new { message = "Arquivo de log não encontrado." });
                }

                // 🎯 FileShare.ReadWrite permite ler o arquivo mesmo enquanto o
                // SimpleFileLoggerProvider está com ele aberto para escrita (StreamWriter).
                string conteudo;
                using (var fs = new FileStream(caminhoCompleto, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (var sr = new StreamReader(fs, Encoding.UTF8))
                {
                    conteudo = sr.ReadToEnd();
                }

                return Ok(new { nomeArquivo = nomeSeguro, conteudo });
            }
            catch (IOException ex)
            {
                // 🎯 Log momentaneamente indisponível (colisão rara mesmo com FileShare.ReadWrite,
                // ex: durante o Dispose() do provider). Não é erro fatal — o front tenta de novo no polling.
                _logger.LogWarning($"Log {nomeArquivo} temporariamente bloqueado: {ex.Message}");
                return StatusCode(423, new { message = "Log sendo escrito no momento, tente novamente em instantes." });
            }
            catch (Exception ex)
            {
                _logger.LogError($"Erro ao ler log {nomeArquivo}: {ex}");
                return BadRequest(new { message = "Falha ao ler o log.", error = ex.Message });
            }
        }
        
    }
}