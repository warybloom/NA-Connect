using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Data.SqlClient;
using Dapper;
using System;
using System.Threading.Tasks;

namespace APS.WebApp.Controllers
{
    // Uso: [RequirePagePermission("/rota-do-frontend")] em cima de qualquer action
    // Precisa que o UserID do usuário logado esteja salvo em HttpContext.Session
    public class RequirePagePermissionAttribute : Attribute, IAsyncActionFilter
    {
        private readonly string _rota;

        public RequirePagePermissionAttribute(string rota)
        {
            _rota = rota;
        }

        public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
        {
            // TODO: confirmar a chave exata usada pelo OAuthController ao salvar o usuário na sessão.
            // Se hoje só existe o AccessToken salvo (e não o UserID), essa checagem precisa
            // primeiro resolver o UserID a partir do token/e-mail antes de continuar.
            var userId = context.HttpContext.Session.GetInt32("UserID");
            if (userId == null)
            {
                context.Result = new UnauthorizedObjectResult(new { message = "Sessão inválida. Faça login novamente." });
                return;
            }

            // Mesma connection string usada no AdminMasterController.
            // Ideal no futuro: mover para appsettings.json e injetar via IConfiguration.
            const string connectionString = @"Server=.\SQLEXPRESS;Database=DB_NA_CONNECT;Integrated Security=True;TrustServerCertificate=True;";

            try
            {
                using (var conn = new SqlConnection(connectionString))
                {
                    await conn.OpenAsync();

                    const string sql = @"
                        SELECT COUNT(1)
                        FROM [dbo].[Paginas] p
                        LEFT JOIN [dbo].[PermissoesPerfil] pp 
                            ON pp.PaginaID = p.PaginaID 
                            AND pp.RoleSistema = (SELECT RoleSistema FROM [dbo].[Usuarios] WHERE UserID = @UserId)
                        LEFT JOIN [dbo].[PermissoesUsuario] pu 
                            ON pu.PaginaID = p.PaginaID 
                            AND pu.UserID = @UserId
                        WHERE p.Rota = @Rota 
                          AND p.Ativo = 1
                          AND (pp.PaginaID IS NOT NULL OR pu.PaginaID IS NOT NULL);";

                    int temAcesso = await conn.ExecuteScalarAsync<int>(sql, new { UserId = userId, Rota = _rota });

                    if (temAcesso == 0)
                    {
                        context.Result = new ObjectResult(new { message = "Você não tem permissão para acessar este recurso." })
                        {
                            StatusCode = 403
                        };
                        return;
                    }
                }
            }
            catch (Exception ex)
            {
                context.Result = new ObjectResult(new { message = $"Erro ao verificar permissão: {ex.Message}" })
                {
                    StatusCode = 500
                };
                return;
            }

            await next();
        }
    }
}