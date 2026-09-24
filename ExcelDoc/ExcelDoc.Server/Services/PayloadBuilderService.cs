using System.Globalization;
using ExcelDoc.Server.Localization;
using ExcelDoc.Server.Models;
using ExcelDoc.Server.Services.Interfaces;

namespace ExcelDoc.Server.Services
{
    public class PayloadBuilderService : IPayloadBuilderService
    {
        private static readonly CultureInfo PtBrCulture = CultureInfo.GetCultureInfo("pt-BR");
        private static readonly CultureInfo EnUsCulture = CultureInfo.GetCultureInfo("en-US");
        private static readonly CultureInfo[] DateCultures = [PtBrCulture, EnUsCulture, CultureInfo.InvariantCulture];
        private static readonly CultureInfo[] NumberCultures = [PtBrCulture, EnUsCulture, CultureInfo.InvariantCulture];
        private readonly IMessageService _messageService;
        private static readonly string[] KnownDateFormats =
        [
            "dd/MM/yyyy",
            "d/M/yyyy",
            "dd/MM/yyyy HH:mm:ss",
            "d/M/yyyy H:m:s",
            "dd/MM/yyyy HH:mm",
            "d/M/yyyy H:m",
            "MM/dd/yyyy",
            "M/d/yyyy",
            "MM/dd/yyyy HH:mm:ss",
            "M/d/yyyy H:m:s",
            "MM/dd/yyyy HH:mm",
            "M/d/yyyy H:m",
            "yyyy-MM-dd",
            "yyyy-MM-dd HH:mm:ss",
            "yyyy-MM-dd HH:mm",
            "yyyy-MM-ddTHH:mm:ss",
            "yyyy-MM-ddTHH:mm",
            "dd-MM-yyyy",
            "d-M-yyyy",
            "MM-dd-yyyy",
            "M-d-yyyy"
        ];

        public PayloadBuilderService(IMessageService messageService)
        {
            _messageService = messageService;
        }

        public IDictionary<string, object?> BuildPayload(Documento documento, Mapeamento mapeamento, IReadOnlyDictionary<int, string?> rowValues)
        {
            var payload = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

            foreach (var campo in mapeamento.Campos
                         .Where(x => x.Ativo)
                         .OrderBy(x => x.IndiceColuna))
            {
                rowValues.TryGetValue(campo.IndiceColuna, out var rawValue);

                var value = string.Equals(campo.NomeCampo, "SequenceModel", StringComparison.OrdinalIgnoreCase)
                    ? (string.IsNullOrWhiteSpace(rawValue) ? null : rawValue.Trim())
                    : ConvertValue(rawValue, campo);

                if (value is not null)
                {
                    payload[campo.NomeCampo] = value;
                }
            }

            return payload;
        }

        private object? ConvertValue(string? rawValue, MapeamentoCampo campo)
        {
            if (string.IsNullOrWhiteSpace(rawValue))
            {
                return null;
            }

            var value = rawValue.Trim();

            return campo.TipoCampo switch
            {
                TipoCampo.String => value,
                TipoCampo.Int => ParseInt(value),
                TipoCampo.Double => ParseDouble(value),
                TipoCampo.DateTime => ParseDateTime(value, campo.Formato),
                TipoCampo.Boolean => ParseBoolean(value),
                _ => value
            };
        }

        private int ParseInt(string value)
        {
            var number = ParseDouble(value);

            if (number % 1 != 0)
            {
                throw new FormatException(_messageService.Get(MessageKeys.InvalidIntegerValue, value));
            }

            return Convert.ToInt32(number);
        }

        private double ParseDouble(string value)
        {
            var normalizedValue = value.Trim();

            foreach (var culture in NumberCultures)
            {
                if (double.TryParse(normalizedValue, NumberStyles.Number | NumberStyles.AllowCurrencySymbol, culture, out var result))
                {
                    return result;
                }
            }

            var sanitizedValue = new string(normalizedValue.Where(c => char.IsDigit(c) || c is ',' or '.' or '-' or '+').ToArray());

            foreach (var culture in NumberCultures)
            {
                if (double.TryParse(sanitizedValue, NumberStyles.Number | NumberStyles.AllowLeadingSign, culture, out var result))
                {
                    return result;
                }
            }

            throw new FormatException(_messageService.Get(MessageKeys.InvalidNumericValue, value));
        }

        private string ParseDateTime(string value, string? format)
        {
            if (TryParseExcelDate(value, format, out var date))
            {
                return date.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture);
            }

            throw new FormatException(_messageService.Get(MessageKeys.DateInvalid, value));
        }

        private static bool TryParseExcelDate(string value, string? format, out DateTime date)
        {
            var normalizedValue = value.Trim();

            if (!string.IsNullOrWhiteSpace(format))
            {
                foreach (var culture in DateCultures)
                {
                    if (DateTime.TryParseExact(normalizedValue, format, culture, DateTimeStyles.AllowWhiteSpaces, out date))
                    {
                        return true;
                    }
                }
            }

            foreach (var culture in DateCultures)
            {
                if (DateTime.TryParseExact(normalizedValue, KnownDateFormats, culture, DateTimeStyles.AllowWhiteSpaces, out date))
                {
                    return true;
                }
            }

            foreach (var culture in DateCultures)
            {
                if (DateTime.TryParse(normalizedValue, culture, DateTimeStyles.AllowWhiteSpaces, out date))
                {
                    return true;
                }
            }

            if (double.TryParse(normalizedValue, NumberStyles.Float, CultureInfo.InvariantCulture, out var oaDate)
                || double.TryParse(normalizedValue, NumberStyles.Float, PtBrCulture, out oaDate)
                || double.TryParse(normalizedValue, NumberStyles.Float, EnUsCulture, out oaDate))
            {
                try
                {
                    date = DateTime.FromOADate(oaDate);
                    return true;
                }
                catch (ArgumentException)
                {
                }
            }

            date = default;
            return false;
        }

        private bool ParseBoolean(string value)
        {
            return value.ToLowerInvariant() switch
            {
                "1" => true,
                "true" => true,
                "sim" => true,
                "s" => true,
                "yes" => true,
                "y" => true,
                "0" => false,
                "false" => false,
                "nao" => false,
                "não" => false,
                "n" => false,
                "no" => false,
                _ => throw new FormatException(_messageService.Get(MessageKeys.InvalidBooleanValue, value))
            };
        }
    }
}
