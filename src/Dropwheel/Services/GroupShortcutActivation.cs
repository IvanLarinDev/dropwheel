namespace Dropwheel.Services;

/// <summary>Tracks the activation started by hovering the orb. Once armed, shortcuts remain
/// available throughout the open wheel, including after navigating back from a group.</summary>
public sealed class GroupShortcutActivation
{
    public bool IsArmed { get; private set; }
    public bool PointerOverOrb { get; private set; }

    public void PointerEntered() => PointerOverOrb = true;

    public void PointerLeft(bool wheelOpen, bool inputPending)
    {
        PointerOverOrb = false;
        if (!wheelOpen && !inputPending) IsArmed = false;
    }

    public void Refresh(bool hasCodes) => IsArmed = PointerOverOrb && hasCodes;

    public bool CanAcceptDigit(bool wheelOpen, bool inputPending)
    {
        if (!IsArmed) return false;
        if (PointerOverOrb || wheelOpen || inputPending) return true;
        IsArmed = false;
        return false;
    }

    public void ResetInput(bool preserveActivation, bool wheelOpen, bool hasCodes)
    {
        IsArmed = preserveActivation && hasCodes && (PointerOverOrb || wheelOpen);
    }
}
