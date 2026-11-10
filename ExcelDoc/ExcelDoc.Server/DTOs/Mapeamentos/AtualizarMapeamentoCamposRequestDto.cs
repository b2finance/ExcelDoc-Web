using System.ComponentModel.DataAnnotations;
using ExcelDoc.Server.Localization;
using ExcelDoc.Server.Models;

namespace ExcelDoc.Server.DTOs.Mapeamentos
{
    public sealed class AtualizarMapeamentoCamposRequestDto
    {
        [Required]
        public List<MapeamentoCampoLoteRequestDto> Campos { get; set; } = [];
    }

    public sealed class MapeamentoCampoLoteRequestDto
    {
        [Range(1, int.MaxValue)]
        public int? Id { get; set; }

        [Required(ErrorMessage = MessageKeys.FieldNameRequired)]
        [MaxLength(150, ErrorMessage = MessageKeys.FieldNameMaxLength)]
        public string NomeCampo { get; set; } = string.Empty;

        [MaxLength(500)]
        public string? DescricaoCampo { get; set; }

        [Range(1, int.MaxValue, ErrorMessage = MessageKeys.ColumnIndexPositive)]
        public int IndiceColuna { get; set; }

        [EnumDataType(typeof(TipoCampo), ErrorMessage = MessageKeys.FieldTypeRequired)]
        public TipoCampo TipoCampo { get; set; }

        [MaxLength(50, ErrorMessage = MessageKeys.FormatMaxLength)]
        public string? Formato { get; set; }

        public bool Ativo { get; set; } = true;
    }
}
