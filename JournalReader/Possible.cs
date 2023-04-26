// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text;

namespace JournalReader;

/// <summary>
/// Represents the result of some possibly-successful function.
/// One should read <see cref="Possible{TResult,TFailure}" /> as 'possibly TResult, but TFailure otherwise'.
/// </summary>
/// <remarks>
/// This is similar to the 'Either' monad, where the binding <c>Then</c> operates on Left (result) or forwards Right (failure)
/// and Haskell's 'Exceptional' monad (which is itself describe as similar to 'Either', but with more convention).
/// The type of <typeparamref name="TFailure" /> may encode rich error information (why / how did the operation fail?) via
/// <see cref="BuildXL.Utilities.Core.Failure{TContent}" />, or a captured recoverable exception via <see cref="RecoverableExceptionFailure" />.
/// </remarks>
public readonly struct Possible<TResult, TFailure>
    where TFailure : Failure
{
    // Note that TFailure is constrained to a ref type, so we can use null as a success-or-fail marker.
    private readonly TFailure? _failure;
    private readonly TResult? _result;

    /// <summary>
    /// Creates a success outcome.
    /// </summary>
    public Possible(TResult result)
    {
        _failure = null;
        _result = result;
    }

    /// <summary>
    /// Creates a failure outcome.
    /// </summary>
    public Possible(TFailure failure)
    {
        _failure = failure;
        _result = default;
    }

    /// <nodoc />
    public static implicit operator Possible<TResult, TFailure>(TResult result) => new Possible<TResult, TFailure>(result);

    /// <nodoc />
    public static implicit operator Possible<TResult, TFailure>(TFailure failure) => new Possible<TResult, TFailure>(failure);

    /// <summary>
    /// Indicates if this is a successful outcome (<see cref="Result" /> available) or not (<see cref="Failure" /> available).
    /// </summary>
    public bool Succeeded => _failure == null;

    /// <summary>
    /// Result, available only if <see cref="Succeeded" />.
    /// </summary>
    public TResult Result => Succeeded ? _result! : throw new InvalidOperationException("Result is not available because the operation failed.");

    /// <summary>
    /// Failure, available only if not <see cref="Succeeded" />.
    /// </summary>
    public TFailure Failure => Succeeded ? throw new InvalidOperationException("Failure is not available because the operation succeeded.") : _failure!;

    /// <summary>
    /// Erases the specific failure type of this instance.
    /// </summary>
    public Possible<TResult, TFailure> WithGenericFailure() => Succeeded ? new Possible<TResult, TFailure>(_result!) : new Possible<TResult, TFailure>(_failure!);

    /// <summary>
    /// Monadic bind. Returns a new result or failure if this one <see cref="Succeeded" />; otherwise
    /// forwards the current <see cref="Failure" />.
    /// </summary>
    /// <example>
    ///     <![CDATA[
    /// Possible<string> maybeString = TryGetString();
    /// Possible<int> maybeInt = maybeString.Then(s => TryParse(s));
    /// ]]>
    /// </example>
    public Possible<TResult2, TFailure> Then<TResult2>(Func<TResult, Possible<TResult2, TFailure>> binder) => Succeeded ? binder(_result!) : new Possible<TResult2, TFailure>(_failure!);

    /// <summary>
    /// Async version of <c>Then{TResult2}</c>
    /// </summary>
    public Task<Possible<TResult2, TFailure>> ThenAsync<TResult2>(Func<TResult, Task<Possible<TResult2, TFailure>>> binder) => Succeeded ? binder(_result!) : Task.FromResult(new Possible<TResult2, TFailure>(_failure!));

    /// <summary>
    /// Dual version of <c>Then{TResult2}</c> which calls <paramref name="resultBinder"/> on success
    /// or <paramref name="failureBinder"/> on failure. Unlike single bind, since the failure type is not constrained to
    /// that of the input value, a new type can be specified as <typeparamref name="TFailure2"/>.
    /// </summary>
    /// <example>
    ///     <![CDATA[
    /// Possible<string, AlphaFailure> maybeString = TryGetString();
    /// Possible<int, BetaFailure> maybeInt = maybeString.Then<int, BetaFailure>(s => TryParse(s), f => new BetaFailure(...));
    /// ]]>
    /// </example>
    public Possible<TResult2, TFailure2> Then<TResult2, TFailure2>(
        Func<TResult, Possible<TResult2, TFailure2>> resultBinder,
        Func<TFailure, Possible<TResult2, TFailure2>> failureBinder)
        where TFailure2 : Failure => Succeeded ? resultBinder(_result!) : failureBinder(_failure!);

    /// <summary>
    /// Monadic 'then' (Haskell operator &gt;&gt;. Returns a new, transformed <see cref="Result" /> if this one <see cref="Succeeded" />; otherwise
    /// forwards the current <see cref="Failure" />.
    /// </summary>
    /// <example>
    ///     <![CDATA[
    /// Possible<string> maybeString = TryGetString();
    /// Possible<int> maybeInt = maybeString.Then(s => s.Length);
    /// ]]>
    /// </example>
    public Possible<TResult2, TFailure> Then<TResult2>(Func<TResult, TResult2> thenFunc) => Succeeded ? new Possible<TResult2, TFailure>(thenFunc(_result!)) : new Possible<TResult2, TFailure>(_failure!);
}

/// <summary>
/// Base failure type. A failure is like an exception, but less exceptional.
/// </summary>
/// <remarks>
/// All failure types for a <see cref="Possible{TResult,TFailure}"/> are constrained to be subclasses of this
/// (a reference type) rather than some equivalent interface. This allows safely using the failure field of a possible
/// result such that <c>null</c> indicates success (a value-type implementing some <c>IFailure</c> would silently break that check).
/// TODO: Failure / Possible are good candidates for instrumentation (we could generically log them all as they are created to ETW).
/// </remarks>
public abstract class Failure
{
    /// <nodoc />
    protected Failure(Failure? innerFailure = null)
    {
        InnerFailure = innerFailure;
    }

    /// <summary>
    /// Returns a lower-level failure (if present) that caused this one.
    /// </summary>
    /// <remarks>
    /// A top-level may have a linear chain of inner failures. <see cref="DescribeIncludingInnerFailures" />
    /// can flatten a chain of inner failures to a user-readable string.
    /// </remarks>
    public Failure? InnerFailure { get; }

    /// <summary>
    /// Describes this failure for the purpose of user-facing logging.
    /// The chain of inner failures is included in the message.
    /// </summary>
    public string DescribeIncludingInnerFailures()
    {
        var builder = new StringBuilder();
        bool first = true;
        for (Failure? f = this; f != null; f = f.InnerFailure)
        {
            if (first)
            {
                first = false;
            }
            else
            {
                builder.AppendLine(": ");
            }

            builder.Append(f.Describe());
        }

        return builder.ToString();
    }

    /// <summary>
    /// Describes this failure for the purpose of user-facing logging.
    /// </summary>
    public abstract string Describe();

    /// <summary>
    /// Returns a throwable exception representation of this failure.
    /// </summary>
    /// <remarks>
    /// Explicit conversion to an exception is sometimes convenient for using <see cref="Possible{TResult,TFailure}"/> style
    /// functions where exceptions are the norm.
    /// </remarks>
    public abstract Exception CreateException();

    /// <summary>
    /// Throws this failure as an exception.
    /// </summary>
    /// <remarks>
    /// Conversion to and throwing as an exception is sometimes convenient for using <see cref="Possible{TResult,TFailure}"/> style
    /// functions where exceptions are the norm.
    /// </remarks>
    public abstract Exception Throw();

    /// <summary>
    /// Creates a failure wrapping this one, with additional content to annotate the failure.
    /// </summary>
    /// <example>
    /// <![CDATA[
    /// return someFailure.Annotate("Couldn't open the file to re-arrange all the bits");
    /// ]]>
    /// </example>
    public Failure<TContent> Annotate<TContent>(TContent content) => new Failure<TContent>(content, this);
}

/// <summary>
/// Represents a failure with associated content (something describing the why or how of the failure).
/// </summary>
public sealed class Failure<TContent> : Failure
{
    /// <nodoc />
    public Failure(TContent content, Failure? innerFailure = null)
        : base(innerFailure)
    {
        Content = content;
    }

    /// <nodoc />
    public TContent? Content { get; }

    /// <inheritdoc />
    public override Exception CreateException()
    {
        return new InvalidOperationException("Failure: " + DescribeIncludingInnerFailures());
    }

    /// <inheritdoc />
    public override InvalidOperationException Throw()
    {
        throw CreateException();
    }

    /// <inheritdoc />
#pragma warning disable CS8603 // Incorrect possible null reference return.
    public override string Describe() => Content == null
        ? "<null failure content of type>" + typeof(TContent).Name + ">"
        : Content.ToString();
#pragma warning restore CS8603 // // Incorrect possible null reference return.
}
