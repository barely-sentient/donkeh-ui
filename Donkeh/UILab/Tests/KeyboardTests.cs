namespace Donkeh.UILab.Tests;

public sealed class KeyboardTests
{
    [Fact]
    public void FormatKeepsPlainKeysUnchanged()
    {
        Assert.Equal("Enter", KeyChord.Format("Enter", KeyModifiers.None));
        Assert.Equal("B", KeyChord.Format("B", KeyModifiers.None));
    }

    [Fact]
    public void FormatPrefixesModifiersInOrder()
    {
        Assert.Equal("Ctrl+C", KeyChord.Format("C", KeyModifiers.Control));
        Assert.Equal("Ctrl+Shift+C", KeyChord.Format("C", KeyModifiers.Control | KeyModifiers.Shift));
        Assert.Equal(
            "Ctrl+Shift+Alt+Meta+Z",
            KeyChord.Format("Z", KeyModifiers.Control | KeyModifiers.Shift | KeyModifiers.Alt | KeyModifiers.Meta));
    }

    [Fact]
    public void FormatLeavesAModifierKeyUnprefixed()
    {
        Assert.Equal("ShiftLeft", KeyChord.Format("ShiftLeft", KeyModifiers.Shift));
        Assert.Equal("SuperLeft", KeyChord.Format("SuperLeft", KeyModifiers.Meta));
        Assert.Equal("Meta", KeyChord.Format("Meta", KeyModifiers.Meta));
    }

    [Fact]
    public void FormatReturnsEmptyForAnEmptyKey()
    {
        Assert.Equal(string.Empty, KeyChord.Format(string.Empty, KeyModifiers.Control));
    }
}
