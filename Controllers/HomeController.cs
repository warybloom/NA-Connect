using APS.WebApp.Services.Permissions;
// Ficheiro: Controllers/HomeController.cs
using Microsoft.AspNetCore.Mvc;
using System.Diagnostics;
using APS.WebApp.Models;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Hosting;

namespace APS.WebApp.Controllers
{
    [PermissionExempt("Entrega a SPA; a autorização é feita por cada API.")]
    public class HomeController : Controller
    {
        private readonly IWebHostEnvironment _env;

        public HomeController(IWebHostEnvironment env)
        {
            _env = env;
        }

        public IActionResult Index()
        {
            // Se a aplicação estiver a rodar em modo de desenvolvimento...
            if (_env.IsDevelopment())
            {
                // ...redireciona para o servidor de desenvolvimento do Vite (a sua aplicação React).
                // Isto evita que a página antiga Index.cshtml seja renderizada.
                return Redirect("http://localhost:5173"); 
            }

            // Em produção, o ideal é servir o ficheiro index.html estático gerado pelo build do React.
            // Para isso, você precisaria de configurações adicionais. Por agora, isto resolve o problema em desenvolvimento.
            return View();
        }

        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
        }
    }
}