using Hasseware.AspNetCore.Components.Bpmn.Editing;
using Hasseware.AspNetCore.Components.Bpmn.Validation;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Hasseware.AspNetCore.Components.Bpmn;

/// <summary>Shared context and style contract for the built-in property editors.</summary>
public abstract class BpmnPropertyEntryComponent : ComponentBase
{
    /// <summary>The property metadata and guarded write callbacks; an empty read-only entry when omitted.</summary>
    [Parameter]
    public BpmnPropertyEntryContext Context { get; set; } = new(new("", "", BpmnPropertyEntryKind.ReadOnly), default, default, true, 0);

    /// <summary>The surface this editor is rendered in; required.</summary>
    [CascadingParameter]
    private BpmnPropertyScope? CascadedScope { get; set; }

    /// <summary>Rejects use without the required property scope before entry-specific initialization.</summary>
    protected override void OnInitialized() => _ = Scope;

    /// <summary>The property scope in force.</summary>
    protected BpmnPropertyScope Scope => CascadedScope ?? throw new InvalidOperationException(
        $"{GetType().Name} must be rendered inside a BpmnPropertyScopeProvider (a BpmnPropertyPanel provides one).");

    /// <summary>The CSS block the surface styles its controls with.</summary>
    protected string Block => Scope.Block;

    /// <summary>Rows a multiline text box is given.</summary>
    protected int MultilineRows => Scope.MultilineRows;

    /// <summary>The editor registry the surface resolves entries with.</summary>
    protected BpmnPropertyEditorRegistry Editors => Scope.Editors;

    /// <summary>The current property's metadata.</summary>
    protected BpmnPropertyEntry Entry => Context.Entry;

    /// <summary>Choice captions resolved even when a host renders an entry outside a property panel.</summary>
    protected IReadOnlyList<BpmnPropertyChoice> Choices =>
        new BpmnDisplayLocalizer(Localizer<Resources.BpmnStrings>()).Property(Entry).Choices;

    /// <summary>Whether this editor must prevent edits.</summary>
    protected bool ReadOnly => Scope.IsReadOnly || Context.IsReadOnly || Entry.IsReadOnly || Entry.Kind == BpmnPropertyEntryKind.ReadOnly;

    private static readonly IBpmnExpressionValidator DefaultValidator = new BpmnExpressionValidator();
    private static readonly IStringLocalizerFactory FallbackLocalizerFactory = new ResourceManagerStringLocalizerFactory(Options.Create(new LocalizationOptions()), NullLoggerFactory.Instance);
    private BpmnPropertyEntry? _validatedEntry;
    private string? _validatedValue;
    private IBpmnExpressionValidator? _validatedBy;
    private string? _expressionError;
    private System.Globalization.CultureInfo? _validatedCulture;
    private readonly string _errorId = "bpmn-expression-" + Guid.NewGuid().ToString("N");

    /// <summary>Resolves the host's syntax validator, or the managed fallback for standalone controls.</summary>
    [Inject] protected IServiceProvider Services { get; set; } = default!;

    /// <summary>Localized built-in property editor text.</summary>
    protected string Text(string key) => Localizer<Resources.BpmnStrings>()[key];

    /// <summary>Resolves the host's localizer, or the neutral resource localizer for standalone controls.</summary>
    protected IStringLocalizer<TResource> Localizer<TResource>() =>
        Services.GetService<IStringLocalizer<TResource>>() ?? new StringLocalizer<TResource>(FallbackLocalizerFactory);

    /// <summary>Accessible error text id shared with the entry frame.</summary>
    protected string ErrorId => Context.ErrorId ?? _errorId;

    /// <summary>Returns an advisory syntax warning without preventing writes.</summary>
    protected string? ExpressionError(string? value)
    {
        var validator = Services.GetService<IBpmnExpressionValidator>() ?? DefaultValidator;
        if (!ReferenceEquals(_validatedEntry, Entry) || _validatedValue != value || !ReferenceEquals(_validatedBy, validator)
            || !Equals(_validatedCulture, System.Globalization.CultureInfo.CurrentUICulture))
        {
            _validatedEntry = Entry;
            _validatedValue = value;
            _validatedBy = validator;
            _validatedCulture = System.Globalization.CultureInfo.CurrentUICulture;
            var warning = BpmnExpressionEntryValidation.Error(Entry, value, validator);
            _expressionError = warning;
            if (warning is not null)
            {
                var language = Entry.ExpressionLanguage == BpmnExpressionLanguage.Juel ? "JUEL" : "FEEL";
                var prefix = language + " warning: ";
                if (warning.StartsWith(prefix, StringComparison.Ordinal))
                    _expressionError = Localizer<Resources.BpmnStrings>()
                        ["Expression.Warning", Text(language == "FEEL" ? "Expression.Feel" : "Expression.Juel"), warning[prefix.Length..]];
            }
        }
        return _expressionError;
    }

    /// <summary>Preserves binding errors and supplies advisory expression diagnostics.</summary>
    protected BpmnPropertyEntryContext ValidatedContext(string? value) => Context with
    {
        ErrorId = ErrorId,
        Entry = Entry with { Error = Entry.Error ?? ExpressionError(value) },
    };

    /// <summary>Submits a scalar value without bypassing the read-only flag.</summary>
    protected Task SubmitAsync(string? value) => ReadOnly ? Task.CompletedTask : Context.OnValueChanged.InvokeAsync(value);
}
