using Hasseware.AspNetCore.Components.Bpmn.Validation;
using Microsoft.Extensions.DependencyInjection;

namespace Hasseware.AspNetCore.Components.Bpmn.Tests.Validation;

public sealed class BpmnExpressionValidatorTests
{
    private readonly BpmnExpressionValidator _validator = new BpmnExpressionValidator();

    [Theory]
    [InlineData("1")]
    [InlineData("=1 + 2 * 3 ** 2")]
    [InlineData(".5 + 1.25e-3")]
    [InlineData("true")]
    [InlineData("false")]
    [InlineData("null")]
    [InlineData("\"hello\\nworld\\t\\u0041\"")]
    [InlineData("\"escaped \\\"quote\\\" and \\\\ slash\"")]
    [InlineData("customer")]
    [InlineData("customer_name")]
    [InlineData("customer2.address.city")]
    [InlineData("customer name.address line")]
    [InlineData("{\"if\": 1}.if")]
    [InlineData("{true: 1}.true")]
    [InlineData("{a: 1, and: 2}.and")]
    [InlineData("x.a and y")]
    [InlineData("{a: 1} instance of context<a: number>")]
    [InlineData("function(x: context<a: number>) x.a")]
    [InlineData("a instance of function<number> -> number")]
    [InlineData("a + -b")]
    [InlineData("-3 ** 2")]
    [InlineData("a = b")]
    [InlineData("a != b")]
    [InlineData("a < b and c >= d or e <= f")]
    [InlineData("a > b")]
    [InlineData("a between 1 and 10")]
    [InlineData("a between 1 + 2 and 9 - 1 and enabled")]
    [InlineData("a in [1, 2, 3]")]
    [InlineData("a in (1, 2, 3)")]
    [InlineData("a in [1..10]")]
    [InlineData("a instance of number")]
    [InlineData("a instance of list<number>")]
    [InlineData("a instance of date and time")]
    [InlineData("if approved then \"yes\" else \"no\"")]
    [InlineData("if x > 3 then if y then a else b else c")]
    [InlineData("for x in items return x.price * 2")]
    [InlineData("for x in items, y in other return x + y")]
    [InlineData("for x in 1..3 return x")]
    [InlineData("some x in items satisfies x > 3")]
    [InlineData("every x in items, y in other satisfies x = y")]
    [InlineData("[]")]
    [InlineData("[1, 2, if ok then 3 else 4]")]
    [InlineData("[1 = 1, 2 between 1 and 3]")]
    [InlineData("[[1], [2, 3]]")]
    [InlineData("items[item.price > 3].name")]
    [InlineData("items[1]")]
    [InlineData("[1,2,3][item > 1]")]
    [InlineData("sum(items)")]
    [InlineData("sum([])")]
    [InlineData("now()")]
    [InlineData("string join([\"a\", \"b\"], \",\")")]
    [InlineData("substring(string: \"abc\", start position: 1, length: 2)")]
    [InlineData("date and time(\"2025-01-01T00:00:00\")")]
    [InlineData("years and months duration(from: date(\"2020-01-01\"), to: date(\"2025-01-01\"))")]
    [InlineData("{}")]
    [InlineData("{a: 1, b: [2,3], \"customer name\": \"Ada\"}")]
    [InlineData("{a: 1, b: a + 1}.b")]
    [InlineData("{customer name: customer.name}")]
    [InlineData("[1..5]")]
    [InlineData("(1..5)")]
    [InlineData("(1..5]")]
    [InlineData("(1..5[")]
    [InlineData("[1..5)")]
    [InlineData("]1..5[")]
    [InlineData("[1..5[")]
    [InlineData("]1..5]")]
    [InlineData("[a[1]..b[2]]")]
    [InlineData("[1..5[ and enabled")]
    [InlineData("< 5")]
    [InlineData(">= 10, <= 3, \"approved\"")]
    [InlineData("-")]
    [InlineData("not(> 5, < 1)")]
    [InlineData("not(enabled)")]
    [InlineData("not(negand: true)")]
    [InlineData("get or else(value, 0)")]
    [InlineData("@\"2025-01-01\"")]
    [InlineData("@\"2025-01-01T12:30:00Z\"")]
    [InlineData("@\"P1D\"")]
    [InlineData("function(x) x + 1")]
    [InlineData("function(x: number, y: number) x + y")]
    [InlineData("(function(x) x + 1)(2)")]
    [InlineData("1 /* comment */ + 2 // tail\n")]
    public void AcceptsFeelSyntax(string expression) => Assert.Empty(_validator.Validate(BpmnExpressionLanguage.Feel, expression));

    [Theory]
    [InlineData("")]
    [InlineData("=")]
    [InlineData("1 +")]
    [InlineData("1 + -")]
    [InlineData("a > > 3")]
    [InlineData("1 2")]
    [InlineData("1foo")]
    [InlineData("1e+")]
    [InlineData("(x")]
    [InlineData("[1, 2}")]
    [InlineData("[1,]")]
    [InlineData("{a 1}")]
    [InlineData("{a:}")]
    [InlineData("{a: 1,}")]
    [InlineData("{a: 1")]
    [InlineData("if x then 1")]
    [InlineData("if then 1 else 2")]
    [InlineData("for x items return x")]
    [InlineData("for x in items x")]
    [InlineData("some x in items return x")]
    [InlineData("every in items satisfies true")]
    [InlineData("x between 1")]
    [InlineData("x between 1 and")]
    [InlineData("x in")]
    [InlineData("a.")]
    [InlineData("a[]")]
    [InlineData("sum(1,,2)")]
    [InlineData("sum(1,)")]
    [InlineData("substring(string:)")]
    [InlineData("[1..]")]
    [InlineData("sum([1..2], (3])")]
    [InlineData("([1..2]]")]
    [InlineData("]1,2[")]
    [InlineData("\"unterminated")]
    [InlineData("\"bad\\q\"")]
    [InlineData("\"bad\\u12zz\"")]
    [InlineData("\"bad\nnewline\"")]
    [InlineData("@123")]
    [InlineData("${x}")]
    [InlineData("a && b")]
    [InlineData("function(x,) x")]
    [InlineData("1 /* missing close")]
    public void RejectsFeelSyntaxWithSourceSpans(string expression)
    {
        var diagnostic = Assert.Single(_validator.Validate(BpmnExpressionLanguage.Feel, expression));
        Assert.NotEmpty(diagnostic.Message);
        Assert.InRange(diagnostic.Offset, 0, expression.Length);
        Assert.InRange(diagnostic.Length, 0, expression.Length - diagnostic.Offset);
    }

    [Theory]
    [InlineData("")]
    [InlineData("literal text")]
    [InlineData("plain {text} with $ and #")]
    [InlineData("\\${not an expression +}")]
    [InlineData("\\#{not an expression +}")]
    [InlineData("${x}")]
    [InlineData("#{x}")]
    [InlineData("Hello ${user.name}, #{count + 1} items")]
    [InlineData("${true}")]
    [InlineData("${false}")]
    [InlineData("${null}")]
    [InlineData("${1.5e+3 + .25}")]
    [InlineData("${a + b * c / 2 % 3}")]
    [InlineData("${a div 2 mod 3}")]
    [InlineData("${a eq b and c ne d}")]
    [InlineData("${a == b || c != d}")]
    [InlineData("${a lt b or a gt c}")]
    [InlineData("${a le b and c ge d}")]
    [InlineData("${a <= b && c >= d}")]
    [InlineData("${a < b || a > c}")]
    [InlineData("${empty list}")]
    [InlineData("${not empty list}")]
    [InlineData("${!empty list}")]
    [InlineData("${-value + +other}")]
    [InlineData("${ok?foo:bar}")]
    [InlineData("${ok ? (x ? a : b) : c}")]
    [InlineData("${execution.getVariable('name')}")]
    [InlineData("${bean.method(a, b, 'text')}")]
    [InlineData("${bean.method()}")]
    [InlineData("${items[0].name}")]
    [InlineData("${items['key']}")]
    [InlineData("${fn:length(items)}")]
    [InlineData("${'a\\\'b'}")]
    [InlineData("${\"a\\\"b\"}")]
    [InlineData("${'}'} text")]
    [InlineData("${x}${y}")]
    public void AcceptsJuelSyntax(string expression) => Assert.Empty(_validator.Validate(BpmnExpressionLanguage.Juel, expression));

    [Theory]
    [InlineData("${}")]
    [InlineData("#{ }")]
    [InlineData("${x")]
    [InlineData("prefix #{x +}")]
    [InlineData("${1 2}")]
    [InlineData("${1foo}")]
    [InlineData("${1e}")]
    [InlineData("${a and}")]
    [InlineData("${a eq eq b}")]
    [InlineData("${empty}")]
    [InlineData("${not}")]
    [InlineData("${ok ? x}")]
    [InlineData("${ok ? : y}")]
    [InlineData("${ok ? x :}")]
    [InlineData("${bean.}")]
    [InlineData("${a[]}")]
    [InlineData("${fn(1,)}")]
    [InlineData("${fn(,1)}")]
    [InlineData("${(x]}")]
    [InlineData("${a = b}")]
    [InlineData("${'unterminated}")]
    [InlineData("${'bad\\q'}")]
    [InlineData("valid ${x}, invalid #{y *}")]
    public void RejectsJuelSyntaxWithCompositeOffsets(string expression)
    {
        var diagnostic = Assert.Single(_validator.Validate(BpmnExpressionLanguage.Juel, expression));
        Assert.NotEmpty(diagnostic.Message);
        Assert.InRange(diagnostic.Offset, 0, expression.Length);
        Assert.InRange(diagnostic.Length, 0, expression.Length - diagnostic.Offset);
    }

    [Fact]
    public void DiagnosticIncludesPersistencePrefixOffset()
    {
        var diagnostic = Assert.Single(_validator.Validate(BpmnExpressionLanguage.Feel, "=a + )"));
        Assert.Equal(5, diagnostic.Offset);
        Assert.Equal(1, diagnostic.Length);
    }

    [Fact]
    public void DiagnosticIncludesCompositeLiteralOffset()
    {
        var diagnostic = Assert.Single(_validator.Validate(BpmnExpressionLanguage.Juel, "Hello ${a + )}"));
        Assert.Equal(12, diagnostic.Offset);
        Assert.Equal(1, diagnostic.Length);
    }

    [Fact]
    public void DeepNestingReturnsDiagnosticRatherThanOverflowing()
    {
        var expression = new string('(', 200) + "x" + new string(')', 200);
        Assert.Single(_validator.Validate(BpmnExpressionLanguage.Feel, expression));
    }

    [Fact]
    public void DeepTypeNestingReturnsDiagnosticRatherThanOverflowing()
    {
        var expression = "x instance of " + string.Concat(Enumerable.Repeat("list<", 200)) + "number" + new string('>', 200);
        Assert.Single(_validator.Validate(BpmnExpressionLanguage.Feel, expression));
    }

    [Fact]
    public void NestedCollectionsAndGroupsParseWithoutExponentialBacktracking()
    {
        var expression = new string('(', 60) + "[[[[1]]]]" + new string(')', 60);
        Assert.Empty(_validator.Validate(BpmnExpressionLanguage.Feel, expression));
    }

    [Fact]
    public void NoneDoesNotValidate()
    {
        Assert.Empty(_validator.Validate(BpmnExpressionLanguage.None, "anything ${"));
    }

    [Fact]
    public void RegistrationIsReplaceableAndIdempotent()
    {
        var custom = new StubValidator();
        var services = new ServiceCollection();
        services.AddSingleton<IBpmnExpressionValidator>(custom);
        services.AddBpmnComponents().AddBpmnComponents();
        using var provider = services.BuildServiceProvider();
        Assert.Same(custom, provider.GetRequiredService<IBpmnExpressionValidator>());
        Assert.Single(provider.GetServices<IBpmnExpressionValidator>());
    }

    [Fact]
    public void DefaultRegistrationUsesManagedValidator()
    {
        using var provider = new ServiceCollection().AddBpmnComponents().BuildServiceProvider();
        Assert.IsType<BpmnExpressionValidator>(provider.GetRequiredService<IBpmnExpressionValidator>());
    }

    private sealed class StubValidator : IBpmnExpressionValidator
    {
        public IReadOnlyList<BpmnExpressionDiagnostic> Validate(BpmnExpressionLanguage language, string expression) => [];
    }
}
