#nullable enable

// Compatibility adapters for the legacy core's progress callbacks.
// The macOS web host reports progress through its own API instead of WinForms.
namespace System.Windows.Forms;

public delegate void MethodInvoker();

public sealed class ProgressBar
{
    public int Minimum { get; set; }
    public int Maximum { get; set; }
    public int Value { get; set; }
    public int Step { get; set; } = 1;
    public bool InvokeRequired => false;
    public void PerformStep() => Value = Math.Min(Maximum, Value + Step);
    public object? Invoke(Delegate method) => method.DynamicInvoke();
    public object? Invoke(Action method) { method(); return null; }
}

public sealed class RichTextBox
{
    public string Text { get; set; } = string.Empty;
    public int TextLength => Text.Length;
    public int SelectionStart { get; set; }
    public bool InvokeRequired => false;
    public void AppendText(string value) => Text += value;
    public void ScrollToCaret() { }
    public object? Invoke(Delegate method) => method.DynamicInvoke();
    public object? Invoke(Action method) { method(); return null; }
}
