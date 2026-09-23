using System.Text;

namespace WTEditor.Application.Services;

/// <summary>Reads the product codes stored in a modern client's .product.db.</summary>
public static class ClientProductCatalog
{
    public static IReadOnlyList<string> GetProducts(string clientFolder)
    {
        if (string.IsNullOrWhiteSpace(clientFolder))
            return [];

        try
        {
            var path = Path.Combine(clientFolder.Trim(), ".product.db");
            if (!File.Exists(path) || new FileInfo(path).Length > 1024 * 1024)
                return [];

            var data = File.ReadAllBytes(path);
            var products = new List<string>();

            // Recent files contain a Database with repeated ProductInstall messages.
            try
            {
                VisitFields(data, (field, value) =>
                {
                    if (field == 1)
                        AddProductInstall(value, products);
                });
            }
            catch (FormatException)
            {
                products.Clear();
            }

            // Older .product.db files can be a single ProductInstall message.
            if (products.Count == 0)
                AddProductInstall(data, products);

            return products.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or FormatException or OverflowException)
        {
            return [];
        }
    }

    private static void AddProductInstall(ReadOnlySpan<byte> data, List<string> products)
    {
        string? code = null;
        VisitFields(data, (field, value) =>
        {
            if (field == 2)
                code = Encoding.UTF8.GetString(value).Trim();
        });

        if (code is { Length: > 0 } && code.All(character => char.IsAsciiLetterOrDigit(character) || character is '_' or '-'))
            products.Add(code);
    }

    private delegate void FieldVisitor(int field, ReadOnlySpan<byte> value);

    private static void VisitFields(ReadOnlySpan<byte> data, FieldVisitor visit)
    {
        var offset = 0;
        while (offset < data.Length)
        {
            var tag = ReadVarint(data, ref offset);
            var field = checked((int)(tag >> 3));
            if (field == 0)
                throw new FormatException("Invalid product database field.");

            switch (tag & 7)
            {
                case 0:
                    ReadVarint(data, ref offset);
                    break;
                case 1:
                    Skip(data, ref offset, 8);
                    break;
                case 2:
                    var length = checked((int)ReadVarint(data, ref offset));
                    if (length < 0 || length > data.Length - offset)
                        throw new FormatException("Invalid product database length.");
                    visit(field, data.Slice(offset, length));
                    offset += length;
                    break;
                case 5:
                    Skip(data, ref offset, 4);
                    break;
                default:
                    throw new FormatException("Unsupported product database field.");
            }
        }
    }

    private static ulong ReadVarint(ReadOnlySpan<byte> data, ref int offset)
    {
        ulong result = 0;
        for (var shift = 0; shift < 64; shift += 7)
        {
            if (offset >= data.Length)
                throw new FormatException("Incomplete product database value.");
            var value = data[offset++];
            result |= (ulong)(value & 0x7f) << shift;
            if ((value & 0x80) == 0)
                return result;
        }
        throw new FormatException("Invalid product database value.");
    }

    private static void Skip(ReadOnlySpan<byte> data, ref int offset, int length)
    {
        if (length > data.Length - offset)
            throw new FormatException("Incomplete product database field.");
        offset += length;
    }
}
