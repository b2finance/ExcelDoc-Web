using ExcelDoc.Server.Background;
using ExcelDoc.Server.Models;
using ExcelDoc.Server.Services.Interfaces;

namespace ExcelDoc.Server.Services
{
    public class JsonBuilderService : IJsonBuilderService
    {
        private readonly IPayloadBuilderService _payloadBuilder;

        public JsonBuilderService(IPayloadBuilderService payloadBuilder)
        {
            _payloadBuilder = payloadBuilder;
        }

        public IDictionary<string, object?> BuildDocumentPayload(
            PerfilMapeamento perfil,
            IReadOnlyList<ExcelRowData> groupRows)
        {
            var document = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

            var headerItems = perfil.Itens
                .Where(i => i.Colecao.TipoColecao == TipoColecao.Header)
                .OrderBy(i => i.Id)
                .ToList();

            var lineItems = perfil.Itens
                .Where(i => i.Colecao.TipoColecao == TipoColecao.Line)
                .OrderBy(i => i.Id)
                .ToList();

            var lineChildrenByParentId = lineItems
                .Where(i => i.FK_IdPerfilMapeamentoItemPai.HasValue)
                .GroupBy(i => i.FK_IdPerfilMapeamentoItemPai!.Value)
                .ToDictionary(g => g.Key, g => g.OrderBy(i => i.Id).ToList());

            var rootLineItems = lineItems
                .Where(i => !i.FK_IdPerfilMapeamentoItemPai.HasValue)
                .ToList();

            var firstRow = groupRows[0];

            foreach (var headerItem in headerItems)
            {
                var headerPayload = _payloadBuilder.BuildPayload(
                    perfil.Documento, headerItem.Mapeamento, firstRow);

                foreach (var kvp in headerPayload)
                {
                    document[kvp.Key] = kvp.Value;
                }
            }

            foreach (var lineItem in rootLineItems)
            {
                var lineCollection = BuildLineCollection(
                    perfil,
                    lineItem,
                    groupRows,
                    lineChildrenByParentId);

                if (lineCollection.Count > 0)
                {
                    document[lineItem.Colecao.NomeColecao] = lineCollection;
                }
            }

            return document;
        }

        private List<IDictionary<string, object?>> BuildLineCollection(
            PerfilMapeamento perfil,
            PerfilMapeamentoItem lineItem,
            IReadOnlyList<ExcelRowData> rows,
            IReadOnlyDictionary<int, List<PerfilMapeamentoItem>> childrenByParentId)
        {
            var collection = new List<IDictionary<string, object?>>();

            foreach (var row in rows)
            {
                var linePayload = _payloadBuilder.BuildPayload(
                    perfil.Documento, lineItem.Mapeamento, row);

                if (childrenByParentId.TryGetValue(lineItem.Id, out var childItems))
                {
                    foreach (var childItem in childItems)
                    {
                        var childCollection = BuildLineCollection(
                            perfil,
                            childItem,
                            [row],
                            childrenByParentId);

                        if (childCollection.Count > 0)
                        {
                            linePayload[childItem.Colecao.NomeColecao] = childCollection;
                        }
                    }
                }

                // Coleções opcionais fazem parte do perfil SAP para ficarem disponíveis
                // no clone, mas não devem gerar arrays/objetos sem conteúdo no payload.
                // Um filho preenchido, por outro lado, torna a linha pai significativa.
                if (HasMeaningfulContent(linePayload))
                {
                    collection.Add(linePayload);
                }
            }

            return collection;
        }

        private static bool HasMeaningfulContent(IDictionary<string, object?> payload) =>
            payload.Values.Any(HasMeaningfulValue);

        private static bool HasMeaningfulValue(object? value) =>
            value switch
            {
                null => false,
                string text => !string.IsNullOrWhiteSpace(text),
                IEnumerable<IDictionary<string, object?>> children => children.Any(HasMeaningfulContent),
                _ => true
            };
    }
}
