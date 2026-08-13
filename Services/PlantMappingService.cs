using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace ForgeSync.Modules.Plant3D.Services
{
    public class PlantMappingService
    {
        /// <summary>
        /// Obtém dinamicamente os cabeçalhos (nomes das colunas) extraídos dos arquivos JSON.
        /// </summary>
        /// <param name="pidJsonDirectory">Caminho da pasta onde estão os JSONs do ProcessPower (P&ID)</param>
        /// <param name="p3dJsonDirectory">Caminho da pasta onde estão os JSONs do Piping (3D)</param>
        /// <returns>Um dicionário contendo as listas de colunas do P&ID e do 3D</returns>
        public async Task<Dictionary<string, List<string>>> GetDynamicColumnsAsync(string pidJsonDirectory, string p3dJsonDirectory)
        {
            var result = new Dictionary<string, List<string>>
            {
                { "pidColumns", new List<string>() },
                { "p3dColumns", new List<string>() }
            };

            // Para o De/Para geral, a tabela EngineeringItems é a mais rica e contém a base de todas as propriedades (Tag, Material, Manufacturer, etc.)
            string pidEngineeringFile = Path.Combine(pidJsonDirectory, "EngineeringItems.json");
            string p3dEngineeringFile = Path.Combine(p3dJsonDirectory, "EngineeringItems.json");

            result["pidColumns"] = await ExtractKeysFromJsonFirstObjectAsync(pidEngineeringFile);
            result["p3dColumns"] = await ExtractKeysFromJsonFirstObjectAsync(p3dEngineeringFile);

            return result;
        }

        /// <summary>
        /// Abre o JSON, lê apenas o primeiro objeto do array e extrai os nomes das propriedades.
        /// Abordagem de altíssima performance (Stream) que previne estouro de memória (Out Of Memory).
        /// </summary>
        private async Task<List<string>> ExtractKeysFromJsonFirstObjectAsync(string filePath)
        {
            var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (!File.Exists(filePath))
            {
                Console.WriteLine($"[AVISO] Arquivo base não encontrado para mapeamento: {filePath}");
                return keys.ToList();
            }

            try
            {
                using (FileStream stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 4096, useAsync: true))
                {
                    using (JsonDocument document = await JsonDocument.ParseAsync(stream))
                    {
                        JsonElement root = document.RootElement;

                        // Verifica se o JSON principal é um array
                        if (root.ValueKind == JsonValueKind.Array)
                        {
                            // Pega o primeiro elemento do array
                            var firstElement = root.EnumerateArray().FirstOrDefault();
                            
                            if (firstElement.ValueKind == JsonValueKind.Object)
                            {
                                foreach (JsonProperty property in firstElement.EnumerateObject())
                                {
                                    keys.Add(property.Name);
                                }
                            }
                        }
                        else if (root.ValueKind == JsonValueKind.Object)
                        {
                            // Se for um objeto direto
                            foreach (JsonProperty property in root.EnumerateObject())
                            {
                                keys.Add(property.Name);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERRO CRÍTICO] Falha ao extrair metadados do arquivo {filePath}: {ex.Message}");
            }

            // Ordena alfabeticamente para facilitar a vida do usuário no React
            var sortedKeys = keys.ToList();
            sortedKeys.Sort();
            return sortedKeys;
        }
    }
}