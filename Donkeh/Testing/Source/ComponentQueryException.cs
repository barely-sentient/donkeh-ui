namespace Donkeh.Testing;

/// <summary>
/// Thrown when a component test query finds no matching element.
/// </summary>
/// <remarks>
/// The exception is deliberately separate from <see cref="InvalidOperationException"/>
/// so a test can distinguish an empty query from a failure inside the component.
/// </remarks>
public sealed class ComponentQueryException : Exception
{
    /// <summary>Creates a query failure with a diagnostic message.</summary>
    /// <param name="message">The message shown by the test runner.</param>
    public ComponentQueryException(string message)
        : base(message)
    {
    }

    /// <summary>Creates a query failure with a diagnostic message and inner cause.</summary>
    /// <param name="message">The message shown by the test runner.</param>
    /// <param name="innerException">The underlying failure.</param>
    public ComponentQueryException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
