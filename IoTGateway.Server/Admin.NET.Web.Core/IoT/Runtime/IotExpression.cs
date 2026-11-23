using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Admin.NET.Web.Core;

/// <summary>
/// 变量表达式解析（移植自原 Plugin/Expression.cs）
/// </summary>
public static class IotExpression
{
    private static readonly Regex ParameterRegex = new(@"\{(.*?)\}", RegexOptions.Compiled);

    /// <summary>
    /// 获取表达式字符串
    /// </summary>
    /// <param name="expression">表达式模板</param>
    /// <param name="values">数组中依次为当前值、前次值、前前次值</param>
    /// <param name="datas">其他参数字典</param>
    public static string GetExpressionText(this string expression, object[] values, Dictionary<string, object> datas)
    {
        if (expression == null)
            throw new ArgumentNullException(nameof(expression));
        if (values == null || values.Length < 3)
            throw new ArgumentException("Values must contain at least three elements", nameof(values));
        if (datas == null)
            throw new ArgumentNullException(nameof(datas));

        var current = values[0];
        var previous = values[1];
        var prePrevious = values[2];

        var result = expression.Trim()
            .ReplaceExpressionText("raw", current)
            .ReplaceExpressionText("$v", current)
            .ReplaceExpressionText("$pv", previous)
            .ReplaceExpressionText("$ppv", prePrevious);

        var parameterNames = ParameterRegex.Matches(result)
            .Select(m => m.Groups[1].Value)
            .Distinct();

        foreach (var kvp in datas.Where(kvp => parameterNames.Contains(kvp.Key)))
            result = result.ReplaceExpressionText($"{{{kvp.Key}}}", kvp.Value);

        return result;
    }

    /// <summary>
    /// 替换表达式中的占位符为指定的值
    /// </summary>
    public static string ReplaceExpressionText(this string expression, string placeholder, object value)
    {
        if (expression == null)
            return null;

        return expression.Replace(
                placeholder,
                value is bool ? $"Convert.ToBoolean(\"{value}\")" : value?.ToString())
            .Replace("&lt;", "<")
            .Replace("&gt;", ">")
            .Replace("&amp;", "&")
            .Replace("&quot;", "\"");
    }
}
