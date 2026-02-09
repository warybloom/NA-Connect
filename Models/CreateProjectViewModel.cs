using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace APS.WebApp.Models
{
    public class CreateProjectViewModel
    {
        public ProjectFromTemplateInput Form { get; set; } = new ProjectFromTemplateInput();
        public List<AccProjectTemplate> AvailableTemplates { get; set; } = new List<AccProjectTemplate>();
    }

    public class ProjectFromTemplateInput
    {
        [Required(ErrorMessage = "Por favor, selecione um template.")]
        public string TemplateId { get; set; } = string.Empty;

        [Required(ErrorMessage = "O nome do projeto é obrigatório.")]
        public string ProjectName { get; set; } = string.Empty;

        // ✅ Propriedade renomeada para corresponder à API.
        [Required(ErrorMessage = "O número do projeto é obrigatório.")]
        public string JobNumber { get; set; } = string.Empty;

        [Required(ErrorMessage = "A data de início é obrigatória.")]
        public string StartDate { get; set; } = string.Empty;

        [Required(ErrorMessage = "A data de fim é obrigatória.")]
        public string EndDate { get; set; } = string.Empty;
    }
}