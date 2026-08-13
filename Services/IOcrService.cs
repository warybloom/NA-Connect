using System.Threading.Tasks;

namespace NA_Connect.Services
{
    public interface IOcrService
    {
        // O método que o motor chama para ler o pedacinho do PDF
        Task<string> LerAreaAsync(string filePath, decimal x, decimal y, decimal width, decimal height);
    }
}