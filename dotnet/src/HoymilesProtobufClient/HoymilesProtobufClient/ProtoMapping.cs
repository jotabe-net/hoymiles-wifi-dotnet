using System.Collections;
using System.Globalization;
using System.Reflection;
using Google.Protobuf;

namespace HoymilesProtobufClient;

internal static class ProtoMapping
{
    public static TDestination CopyMatchingProperties<TDestination>(object source)
        where TDestination : new()
    {
        ArgumentNullException.ThrowIfNull(source);

        TDestination destination = new();
        CopyMatchingProperties(source, destination);
        return destination;
    }

    public static void CopyMatchingProperties(object source, object destination)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(destination);

        PropertyInfo[] sourceProperties = source.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance);
        Dictionary<string, PropertyInfo> destinationProperties = destination
            .GetType()
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .ToDictionary(p => p.Name, p => p, StringComparer.Ordinal);

        foreach (PropertyInfo sourceProperty in sourceProperties)
        {
            if (!destinationProperties.TryGetValue(sourceProperty.Name, out PropertyInfo? destinationProperty))
            {
                continue;
            }

            object? sourceValue = sourceProperty.GetValue(source);
            if (sourceValue is null)
            {
                continue;
            }

            if (destinationProperty.CanWrite && destinationProperty.PropertyType.IsAssignableFrom(sourceProperty.PropertyType))
            {
                destinationProperty.SetValue(destination, sourceValue);
                continue;
            }

            if (sourceValue is IList sourceList && destinationProperty.GetValue(destination) is IList destinationList)
            {
                destinationList.Clear();
                foreach (object? item in sourceList)
                {
                    destinationList.Add(item);
                }
            }
        }
    }

    public static void SetIfExists<T>(object target, string propertyName, T value)
    {
        PropertyInfo? property = target.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance);
        if (property is null || !property.CanWrite)
        {
            return;
        }

        object? converted = value;
        if (value is not null && property.PropertyType != typeof(T))
        {
            Type targetType = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
            converted = Convert.ChangeType(value, targetType, CultureInfo.InvariantCulture);
        }

        property.SetValue(target, converted);
    }

    public static void AddRepeatedIfExists<T>(object target, string propertyName, T value)
    {
        PropertyInfo? property = target.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance);
        if (property?.GetValue(target) is IList list)
        {
            list.Add(value);
        }
    }

    public static TResponse Parse<TResponse>(byte[] payload)
        where TResponse : class, IMessage<TResponse>, new()
    {
        var message = new TResponse();
        message.MergeFrom(payload);
        return message;
    }

    public static TDestination InitializeSetConfig<TDestination>(object source)
        where TDestination : new()
    {
        return CopyMatchingProperties<TDestination>(source);
    }

    public static int EncodeDateTimeRange(string? fromDateTime, string? toDateTime, string delimiter = ":")
    {
        static (int first, int second) Parse(string? value, string delim)
        {
            string input = string.IsNullOrWhiteSpace(value) ? $"00{delim}00" : value;
            string[] parts = input.Split(delim, StringSplitOptions.None);
            if (parts.Length != 2)
            {
                throw new ArgumentException($"Invalid date/time format: {value}");
            }

            return (
                int.Parse(parts[0], CultureInfo.InvariantCulture),
                int.Parse(parts[1], CultureInfo.InvariantCulture));
        }

        (int fromFirst, int fromSecond) = Parse(fromDateTime, delimiter);
        (int toFirst, int toSecond) = Parse(toDateTime, delimiter);

        return (fromFirst << 24) | (fromSecond << 16) | (toFirst << 8) | toSecond;
    }

    public static int EncodeWeekRange(IEnumerable<int>? week)
    {
        int encoded = 0;
        if (week is null)
        {
            return encoded;
        }

        foreach (int value in week)
        {
            encoded |= value switch
            {
                1 => 1,
                2 => 2,
                3 => 4,
                4 => 8,
                5 => 16,
                6 => 32,
                7 => 64,
                _ => 0,
            };
        }

        return encoded;
    }

    public static int FloatToScaledInt(double? value)
    {
        return (int)((value ?? 0d) * 100d);
    }

    public static long ConvertInverterSerialNumber(string serialNumber)
    {
        return Convert.ToInt64(serialNumber, 16);
    }
}
