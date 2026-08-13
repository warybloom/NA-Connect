using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Threading.Tasks;
using System;
using AccIntegration.WebApp.Data;

namespace AccIntegration.WebApp.Controllers
{
    [ApiController]
    [Route("api/admin/agendamento")] 
    public class ProjetoController : ControllerBase
    {
        private readonly ConnectDbContext _context;

        public ProjetoController(ConnectDbContext context)
        {
            _context = context;
        }

        [HttpPatch("{id}")] 
        public async Task<IActionResult> SalvarAgendamento(int id, [FromBody] AgendamentoDto data)
        {
            Console.WriteLine($"[DEBUG] id={id} | HorariosSync='{data.HorariosSync}' | NomeProjeto='{data.NomeProjeto}'");
            try
            {
                // 🚀 A MÁGICA AQUI: Em vez de depender do mapeamento do C#, 
                // disparamos o UPDATE com INNER JOIN diretamente para o SQL Server via EF.
                string sql = @"
                    UPDATE M
                    SET M.[HorariosSync] = {0},
                    M.[Data_Ultima_Atualizacao] = GETDATE()
                    FROM [dbo].[Metadados_Projetos] M
                    INNER JOIN [dbo].[Projetos] P 
                    ON REPLACE(LTRIM(RTRIM(M.[Project_ID_ACC])), 'b.', '') 
                    = REPLACE(LTRIM(RTRIM(P.[Project_ID_ACC])), 'b.', '')
                    WHERE P.[ProjetoID] = {1}";

                int linhasAfetadas = await _context.Database.ExecuteSqlRawAsync(sql, data.HorariosSync ?? "", id);

                // PLANO B: Se a query acima não afetou nada (caso as tabelas estejam soltas), tenta pelo Nome via C# normal
                if (linhasAfetadas == 0 && !string.IsNullOrEmpty(data.NomeProjeto))
                {
                    var projeto = await _context.MetadadosProjetos.FirstOrDefaultAsync(p => 
                        p.Descricao_Corporativa == data.NomeProjeto || 
                        p.Nome_LDP == data.NomeProjeto);

                    if (projeto != null)
                    {
                        projeto.HorariosSync = data.HorariosSync;
                        projeto.Data_Ultima_Atualizacao = DateTime.Now;
                        await _context.SaveChangesAsync();
                        linhasAfetadas = 1;
                    }
                }   

                if (linhasAfetadas == 0)
                    return NotFound(new { message = "Projeto não localizado no banco pelo ID nem pelo Nome." });

                Console.WriteLine($"[API SUCESSO] Horário '{data.HorariosSync}' salvo no banco para o projeto {data.NomeProjeto}!");
                return Ok(new { message = "Cronograma atualizado no SQL Server!" });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = $"Erro ao salvar: {ex.Message}" });
            }
        }
    }

    public class AgendamentoDto
    {
        public string HorariosSync { get; set; }
        public string NomeProjeto { get; set; } // ✅ Recebe o nome do React para o Plano B
    }
}