namespace Hasseware.AspNetCore.Components.Bpmn.FeelSharp;

internal sealed record FeelSharpFunction(string Name, string Signature, string Description, string Example, string? Note = null)
{
    internal string Category => Name switch
    {
        "abs" or "ceiling" or "floor" or "max" or "mean" or "min" or "number" or "round" or "sum" => "number",
        "date" or "date and time" or "day of week" or "day of year" or "duration" or "now" or "time" or "today" => "temporal",
        "get entries" or "get value" => "context",
        "append" or "concatenate" or "count" or "distinct values" or "index of" or "insert before" or "list contains" or "reverse" or "sort" => "list",
        _ => "string",
    };
}

internal sealed class FeelSharpFunctionCatalog
{
    // Representative signatures, not an exhaustive list of engine overloads.
    internal static IReadOnlyList<FeelSharpFunction> Functions { get; } =
    [
        new("abs", "abs(number)", "Absolute value of a number.", "abs(-3)"),
        new("append", "append(list, item)", "Append an item to a list.", "append([1, 2], 3)"),
        new("ceiling", "ceiling(number)", "Smallest integer greater than or equal to a number.", "ceiling(1.2)"),
        new("concatenate", "concatenate(list1, list2)", "Combine lists in order.", "concatenate([1], [2])"),
        new("contains", "contains(string, match)", "Check whether a string contains a substring.", "contains(\"hello\", \"ell\")"),
        new("count", "count(list)", "Number of items in a list.", "count([1, 2])"),
        new("date", "date(string)", "Convert an ISO date string to a date.", "date(\"2025-01-01\")", "Feel.TemporalResult"),
        new("date and time", "date and time(string)", "Convert an ISO date-time string.", "date and time(\"2025-01-01T12:00:00\")", "Feel.TemporalResult"),
        new("day of week", "day of week(date)", "Weekday name for a date.", "day of week(date(\"2025-01-01\"))"),
        new("day of year", "day of year(date)", "Day number within the year.", "day of year(date(\"2025-01-01\"))"),
        new("distinct values", "distinct values(list)", "Remove duplicate list items.", "distinct values([1, 1, 2])"),
        new("duration", "duration(string)", "Convert an ISO duration string.", "duration(\"P1D\")", "Feel.TemporalResult"),
        new("ends with", "ends with(string, match)", "Check a string suffix.", "ends with(\"hello\", \"lo\")"),
        new("floor", "floor(number)", "Largest integer less than or equal to a number.", "floor(1.8)"),
        new("get entries", "get entries(context)", "List the keys and values of a context.", "get entries({x: 1})"),
        new("get value", "get value(context, key)", "Read a context entry by its key.", "get value({x: 1}, \"x\")"),
        new("index of", "index of(list, match)", "One-based positions of matching list items.", "index of([1, 2, 1], 1)"),
        new("insert before", "insert before(list, position, item)", "Insert an item at a one-based list position.", "insert before([1, 3], 2, 2)"),
        new("list contains", "list contains(list, item)", "Check whether a list contains an item.", "list contains([1, 2], 2)"),
        new("lower case", "lower case(string)", "Convert text to lower case.", "lower case(\"HELLO\")"),
        new("max", "max(list)", "Maximum value in a list.", "max([1, 3, 2])"),
        new("mean", "mean(list)", "Arithmetic mean of a list of numbers.", "mean([1, 3])"),
        new("min", "min(list)", "Minimum value in a list.", "min([1, 3, 2])"),
        new("now", "now()", "Current date and time from the engine clock.", "now()", "Feel.TemporalResult"),
        new("number", "number(string)", "Convert a numeric string to a number.", "number(\"12\")"),
        new("reverse", "reverse(list)", "Reverse the order of list items.", "reverse([1, 2])"),
        new("round", "round(number, scale)", "Round a number to a decimal scale.", "round(1.234, 2)"),
        new("sort", "sort(list, precedes)", "Sort items using a comparison function.", "sort([3, 1, 2], function(x, y) x < y)"),
        new("split", "split(string, delimiter)", "Split text using a delimiter pattern.", "split(\"a,b\", \",\")"),
        new("starts with", "starts with(string, match)", "Check a string prefix.", "starts with(\"hello\", \"he\")"),
        new("string", "string(value)", "Convert a value to its string representation.", "string(12)"),
        new("string join", "string join(list, delimiter)", "Join strings with a delimiter.", "string join([\"a\", \"b\"], \",\")"),
        new("string length", "string length(string)", "Number of characters in a string.", "string length(\"hello\")"),
        new("substring", "substring(string, start, length)", "Extract text using a one-based start position.", "substring(\"hello\", 2, 3)"),
        new("sum", "sum(list)", "Sum a list of numbers.", "sum([1, 2, 3])"),
        new("time", "time(string)", "Convert an ISO time string.", "time(\"12:00:00\")", "Feel.TemporalResult"),
        new("today", "today()", "Current date from the engine clock.", "today()", "Feel.TemporalResult"),
        new("upper case", "upper case(string)", "Convert text to upper case.", "upper case(\"hello\")"),
    ];
}
