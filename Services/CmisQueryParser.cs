using System.Text.RegularExpressions;
using CMIS_IyaSoft.Entities;

namespace CMIS_IyaSoft.Services;

/// <summary>
/// A simplified CMIS-SQL parser covering the subset required by the project spec:
/// SELECT * FROM &lt;type&gt; [WHERE &lt;conditions&gt;] [ORDER BY &lt;prop&gt; [ASC|DESC]]
/// Conditions support: IN_FOLDER('id'), =, &lt;&gt;, &gt;, &lt;, &gt;=, &lt;=, LIKE, IS [NOT] NULL,
/// combined with AND / OR (left-to-right, no parenthesis nesting - documented limitation).
/// </summary>
public static class CmisQueryParser
{
    private static readonly Regex SelectFromRegex = new(
        @"^\s*SELECT\s+\*\s+FROM\s+(?<type>[\w:]+)(?<rest>.*)$",
        RegexOptions.IgnoreCase | RegexOptions.Singleline);

    private static readonly Regex OrderByRegex = new(
        @"ORDER\s+BY\s+(?<prop>[\w:]+)\s*(?<dir>ASC|DESC)?\s*$",
        RegexOptions.IgnoreCase);

    private static readonly Regex WhereRegex = new(
        @"WHERE\s+(?<where>.*?)(?=ORDER\s+BY|$)",
        RegexOptions.IgnoreCase | RegexOptions.Singleline);

    // property -> a function that extracts the value from a CmisObject
    private static readonly Dictionary<string, Func<CmisObject, object?>> PropertyMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["cmis:name"] = o => o.Name,
        ["cmis:objectId"] = o => o.Id,
        ["cmis:objectTypeId"] = o => o.TypeId,
        ["cmis:parentId"] = o => o.ParentId,
        ["cmis:path"] = o => o.Path,
        ["cmis:creationDate"] = o => o.CreationDate,
        ["cmis:lastModificationDate"] = o => o.LastModificationDate,
        ["cmis:contentStreamLength"] = o => o.ContentStreamLength,
        ["cmis:contentStreamMimeType"] = o => o.MimeType,
    };

    public class ParsedQuery
    {
        public string TypeId { get; set; } = "cmis:document";
        public string? WhereClause { get; set; }
        public string? OrderByProperty { get; set; }
        public bool OrderDescending { get; set; }
    }

    public static ParsedQuery Parse(string statement)
    {
        var selectMatch = SelectFromRegex.Match(statement.Trim());
        if (!selectMatch.Success)
        {
            throw new InvalidOperationException(
                "Invalid CMIS-SQL statement. Expected format: SELECT * FROM <type> [WHERE ...] [ORDER BY ...]");
        }

        var result = new ParsedQuery { TypeId = selectMatch.Groups["type"].Value };
        var rest = selectMatch.Groups["rest"].Value;

        var whereMatch = WhereRegex.Match(rest);
        if (whereMatch.Success)
        {
            result.WhereClause = whereMatch.Groups["where"].Value.Trim();
        }

        var orderMatch = OrderByRegex.Match(rest);
        if (orderMatch.Success)
        {
            result.OrderByProperty = orderMatch.Groups["prop"].Value;
            result.OrderDescending = string.Equals(orderMatch.Groups["dir"].Value, "DESC", StringComparison.OrdinalIgnoreCase);
        }

        return result;
    }

    /// <summary>
    /// Evaluates the WHERE clause against a single object. Splits on AND/OR left-to-right
    /// (no operator precedence / parentheses - documented simplification for this project's scope).
    /// </summary>
    public static bool Evaluate(CmisObject obj, string? whereClause)
    {
        if (string.IsNullOrWhiteSpace(whereClause))
        {
            return true;
        }

        var parts = Regex.Split(whereClause, @"\s+(AND|OR)\s+", RegexOptions.IgnoreCase);

        bool? result = null;
        string pendingOperator = "AND";

        foreach (var rawPart in parts)
        {
            var part = rawPart.Trim();
            if (part.Equals("AND", StringComparison.OrdinalIgnoreCase) ||
                part.Equals("OR", StringComparison.OrdinalIgnoreCase))
            {
                pendingOperator = part.ToUpperInvariant();
                continue;
            }

            var conditionResult = EvaluateCondition(obj, part);

            result = result == null
                ? conditionResult
                : (pendingOperator == "AND" ? result.Value && conditionResult : result.Value || conditionResult);
        }

        return result ?? true;
    }

    private static bool EvaluateCondition(CmisObject obj, string condition)
    {
        condition = condition.Trim();

        var inFolderMatch = Regex.Match(condition, @"IN_FOLDER\(\s*'(?<id>[^']*)'\s*\)", RegexOptions.IgnoreCase);
        if (inFolderMatch.Success)
        {
            return string.Equals(obj.ParentId, inFolderMatch.Groups["id"].Value, StringComparison.OrdinalIgnoreCase);
        }

        var isNullMatch = Regex.Match(condition, @"^(?<prop>[\w:]+)\s+IS\s+(?<not>NOT\s+)?NULL$", RegexOptions.IgnoreCase);
        if (isNullMatch.Success)
        {
            var value = GetPropertyValue(obj, isNullMatch.Groups["prop"].Value);
            var isNull = value == null || (value is string s && string.IsNullOrEmpty(s));
            return isNullMatch.Groups["not"].Success ? !isNull : isNull;
        }

        var likeMatch = Regex.Match(condition, @"^(?<prop>[\w:]+)\s+LIKE\s+'(?<pattern>[^']*)'$", RegexOptions.IgnoreCase);
        if (likeMatch.Success)
        {
            var value = GetPropertyValue(obj, likeMatch.Groups["prop"].Value)?.ToString() ?? string.Empty;
            var pattern = "^" + Regex.Escape(likeMatch.Groups["pattern"].Value)
                .Replace("%", ".*").Replace("_", ".") + "$";
            return Regex.IsMatch(value, pattern, RegexOptions.IgnoreCase);
        }

        var comparisonMatch = Regex.Match(
            condition,
            @"^(?<prop>[\w:]+)\s*(?<op><>|>=|<=|=|>|<)\s*'?(?<value>[^']*)'?$",
            RegexOptions.IgnoreCase);

        if (comparisonMatch.Success)
        {
            return EvaluateComparison(
                obj,
                comparisonMatch.Groups["prop"].Value,
                comparisonMatch.Groups["op"].Value,
                comparisonMatch.Groups["value"].Value);
        }

        throw new InvalidOperationException($"Unable to parse WHERE condition: '{condition}'");
    }

    private static bool EvaluateComparison(CmisObject obj, string propName, string op, string rawValue)
    {
        var propValue = GetPropertyValue(obj, propName);
        int cmp;

        if (propName.Equals("cmis:contentStreamLength", StringComparison.OrdinalIgnoreCase))
        {
            var left = Convert.ToInt64(propValue ?? 0L);
            var right = long.TryParse(rawValue, out var parsedLong) ? parsedLong : 0L;
            cmp = left.CompareTo(right);
        }
        else if (propValue is DateTime dateValue)
        {
            var right = DateTime.TryParse(rawValue, out var parsedDate) ? parsedDate : DateTime.MinValue;
            cmp = dateValue.CompareTo(right);
        }
        else
        {
            var left = propValue?.ToString() ?? string.Empty;
            cmp = string.CompareOrdinal(left, rawValue);
        }

        return op switch
        {
            "=" => cmp == 0,
            "<>" => cmp != 0,
            ">" => cmp > 0,
            "<" => cmp < 0,
            ">=" => cmp >= 0,
            "<=" => cmp <= 0,
            _ => throw new InvalidOperationException($"Unsupported operator '{op}'.")
        };
    }

    private static object? GetPropertyValue(CmisObject obj, string propName)
    {
        if (PropertyMap.TryGetValue(propName, out var accessor))
        {
            return accessor(obj);
        }

        throw new InvalidOperationException($"Unknown or unsupported property '{propName}' in query.");
    }

    public static IEnumerable<CmisObject> Sort(IEnumerable<CmisObject> objects, ParsedQuery query)
    {
        if (string.IsNullOrEmpty(query.OrderByProperty))
        {
            return objects;
        }

        object? KeySelector(CmisObject o) => GetPropertyValue(o, query.OrderByProperty!);

        return query.OrderDescending
            ? objects.OrderByDescending(KeySelector, Comparer<object?>.Create(CompareObjects))
            : objects.OrderBy(KeySelector, Comparer<object?>.Create(CompareObjects));
    }

    private static int CompareObjects(object? a, object? b)
    {
        if (a == null && b == null) return 0;
        if (a == null) return -1;
        if (b == null) return 1;
        if (a is IComparable ca && a.GetType() == b.GetType()) return ca.CompareTo(b);
        return string.CompareOrdinal(a.ToString(), b.ToString());
    }
}
