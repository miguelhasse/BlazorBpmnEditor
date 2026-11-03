namespace Hasseware.AspNetCore.Components.Bpmn;

public sealed partial class BpmnEditorSession
{
    private BpmnPropertyAction? _propertyAction;
    private int _propertyActionRevision;
    private bool _propertyActionFocusPending;

    /// <summary>Action belonging to the last-focused eligible property, if still valid.</summary>
    public BpmnPropertyAction? PropertyAction => _propertyAction;

    /// <summary>Activates a property without transferring ownership to another editor.</summary>
    public void ActivatePropertyAction(BpmnPropertyAction action)
    {
        ArgumentNullException.ThrowIfNull(action);
        if (IsReadOnly || _selection.Count != 1 || _selection[0] != action.ElementId)
        {
            return;
        }
        if (_propertyAction == action)
        {
            return;
        }
        _propertyAction = action;
        _propertyActionFocusPending = false;
        _propertyActionRevision++;
        NotifyStateChanged();
    }

    /// <summary>Releases only the action registered by this control.</summary>
    public void ReleasePropertyAction(object owner)
    {
        if (ReferenceEquals(_propertyAction?.Owner, owner))
        {
            _propertyAction = null;
            _propertyActionFocusPending = false;
            _propertyActionRevision++;
            NotifyStateChanged();
        }
    }

    /// <summary>Invokes the current property action without changing canvas selection.</summary>
    public Task ExecutePropertyActionAsync() => _propertyAction is { Enabled: true } action && !IsReadOnly
        ? action.ExecuteAsync() : Task.CompletedTask;

    /// <summary>Returns focus to the owning toolbar after the property's modal has closed.</summary>
    public void ReturnPropertyActionFocus(object owner)
    {
        if (!ReferenceEquals(_propertyAction?.Owner, owner) || IsReadOnly) return;
        _propertyActionFocusPending = true;
        _propertyActionRevision++;
        NotifyStateChanged();
    }

    internal bool TakePropertyActionFocus()
    {
        var pending = _propertyActionFocusPending;
        _propertyActionFocusPending = false;
        return pending;
    }

    private void ValidatePropertyAction()
    {
        if (_propertyAction is { } action && (_selection.Count != 1 || _selection[0] != action.ElementId ||
            IsReadOnly))
        {
            _propertyAction = null;
            _propertyActionFocusPending = false;
            _propertyActionRevision++;
        }
    }
}
