using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace APS.WebApp.Models
{
    /// <summary>
    /// Representa a estrutura de dados para atualizar os atributos de uma versão de ficheiro.
    /// </summary>
    public class UpdateVersionPayload
    {
        [JsonPropertyName("jsonapi")]
        public JsonApiVersion JsonApi { get; set; } = new JsonApiVersion();

        [JsonPropertyName("data")]
        public VersionData Data { get; set; }
    }

    public class JsonApiVersion
    {
        [JsonPropertyName("version")]
        public string Version { get; set; } = "1.0";
    }

    public class VersionData
    {
        [JsonPropertyName("type")]
        public string Type { get; set; } = "versions";

        [JsonPropertyName("id")]
        public string Id { get; set; }

        [JsonPropertyName("attributes")]
        public VersionAttributes Attributes { get; set; }
    }

    public class VersionAttributes
    {
        // A API espera um dicionário onde a chave é o ID do atributo prefixado com "attribute-".
        [JsonPropertyName("customAttributes")]
        public Dictionary<string, object> CustomAttributes { get; set; }
    }
}