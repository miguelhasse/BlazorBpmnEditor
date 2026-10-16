using Hasseware.AspNetCore.Components.Bpmn.E2E.Tests.Fixtures;

using Microsoft.Playwright;

namespace Hasseware.AspNetCore.Components.Bpmn.E2E.Tests;

/// <summary>
/// Chooses the demo page a test drives, so one body of tests covers both component kits.
/// </summary>
/// <remarks>
/// The Fluent kit is the demo's <c>/editor</c>; the standard kit is <c>/editor-plain</c>. A test
/// class runs against the standard kit through a subclass whose name starts with
/// <c>Plain</c> (see <c>PlainKitTests.cs</c>); the inherited tests are discovered again there.
/// </remarks>
internal static class KitRoute
{
    internal const string Fluent = "/editor";
    internal const string Plain = "/editor-plain";

    /// <summary>The editor route for the kit the given test class represents.</summary>
    internal static string For(object test) =>
        test.GetType().Name.StartsWith("Plain", StringComparison.Ordinal) ? Plain : Fluent;

    /// <summary>The editor URL for the kit the given test class represents.</summary>
    internal static string Url(this DemoAppFixture app, object test) => $"{app.BaseUrl}{For(test)}";

    /// <summary>The diagram navigator's select, identical in class and accessible name in both kits.</summary>
    internal static ILocator DiagramSelect(this IPage page) => page.Locator(".bpmn-diagrams__select");

    /// <summary>The diagram navigator's "open" button, shared class in both kits (a Fluent button's accessible name is its title).</summary>
    internal static ILocator OpenButton(this IPage page) => page.Locator(".bpmn-diagrams__open");

    /// <summary>The diagram navigator's currently selected option, for a native or a Fluent select.</summary>
    internal static ILocator SelectedDiagram(this IPage page) =>
        page.Locator(".bpmn-diagrams__select :is(option:checked, fluent-option[selected])");

    /// <summary>
    /// Asserts a button's disabled state through the <c>disabled</c> attribute both a native and a Fluent
    /// button carry; Playwright's own check does not see a Fluent host as disabled.
    /// </summary>
    internal static async Task ExpectDisabledAsync(this ILocator button, bool disabled = true)
    {
        var expectation = Assertions.Expect(button);
        if (disabled)
        {
            await expectation.ToHaveAttributeAsync("disabled", new System.Text.RegularExpressions.Regex(".*"));
        }
        else
        {
            await expectation.Not.ToHaveAttributeAsync("disabled", new System.Text.RegularExpressions.Regex(".*"));
        }
    }

    /// <summary>
    /// Picks an option by its text from a native <c>select</c> or a Fluent select, as a user would.
    /// </summary>
    internal static async Task PickAsync(this IPage page, ILocator select, string optionText)
    {
        if (await IsNativeAsync(select))
        {
            await select.SelectOptionAsync(new SelectOptionValue { Label = optionText });
            return;
        }

        await select.Locator("fluent-dropdown").ClickAsync();
        await select.Locator("fluent-option", new() { HasTextString = optionText }).ClickAsync();
    }

    /// <summary>Picks an option by its position from a native <c>select</c> or a Fluent select.</summary>
    internal static async Task PickAsync(this IPage page, ILocator select, int index)
    {
        if (await IsNativeAsync(select))
        {
            await select.SelectOptionAsync(new SelectOptionValue { Index = index });
            return;
        }

        await select.Locator("fluent-dropdown").ClickAsync();
        await select.Locator("fluent-option").Nth(index).ClickAsync();
    }

    private static async Task<bool> IsNativeAsync(ILocator select) =>
        await select.EvaluateAsync<string>("element => element.tagName") == "SELECT";

    /// <summary>The import offer's accept button, found by role inside the offer.</summary>
    internal static ILocator ImportAccept(this IPage page) => page.Locator(".bpmn-import__accept");

    /// <summary>The shortcut-help overlay, found by role and name.</summary>
    internal static ILocator ShortcutDialog(this IPage page) =>
        page.GetByRole(AriaRole.Dialog, new() { Name = "Keyboard shortcuts" });

    /// <summary>
    /// A text box addressed by role and accessible name, so a native input and a Fluent text
    /// input are found the same way.
    /// </summary>
    internal static ILocator Textbox(this IPage page, string name) =>
        page.GetByRole(AriaRole.Textbox, new() { Name = name, Exact = true });
}
