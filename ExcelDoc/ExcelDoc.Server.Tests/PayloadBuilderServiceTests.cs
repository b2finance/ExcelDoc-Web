using ExcelDoc.Server.Models;
using ExcelDoc.Server.Services;

namespace ExcelDoc.Server.Tests;

public sealed class PayloadBuilderServiceTests
{
    [Fact]
    public void BuildPayload_KeepsSequenceModelDescriptionEvenWhenMappedAsInteger()
    {
        var service = new PayloadBuilderService(new StubMessageService());
        var mapping = new Mapeamento
        {
            Campos = [new MapeamentoCampo
            {
                NomeCampo = "SequenceModel",
                IndiceColuna = 1,
                TipoCampo = TipoCampo.Int,
                Ativo = true
            }]
        };

        var payload = service.BuildPayload(new Documento(), mapping,
            new Dictionary<int, string?> { [1] = " Modelo 55 " });

        Assert.Equal("Modelo 55", payload["SequenceModel"]);
    }

    [Fact]
    public void BuildPayload_IgnoresInactiveFieldsEvenWhenTheirValueIsInvalid()
    {
        var service = new PayloadBuilderService(new StubMessageService());
        var mapping = new Mapeamento
        {
            Campos =
            [
                new MapeamentoCampo
                {
                    NomeCampo = "CardCode",
                    IndiceColuna = 1,
                    TipoCampo = TipoCampo.String,
                    Ativo = true
                },
                new MapeamentoCampo
                {
                    NomeCampo = "SequenceCode",
                    IndiceColuna = 2,
                    TipoCampo = TipoCampo.Int,
                    Ativo = false
                }
            ]
        };

        var result = service.BuildPayload(
            new Documento(),
            mapping,
            new Dictionary<int, string?>
            {
                [1] = "C20000",
                [2] = "valor-invalido"
            });

        Assert.Equal("C20000", result["CardCode"]);
        Assert.False(result.ContainsKey("SequenceCode"));
    }
}
