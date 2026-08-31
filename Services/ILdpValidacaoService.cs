using System.IO;
using System.Threading.Tasks;
using APS.WebApp.Models;

namespace APS.WebApp.Services
{
    public interface ILdpValidacaoService
    {
        Task<LdpValidacaoResult> ValidarDocumentoAsync(Stream arquivoStream, string nomeArquivo);
    }
}