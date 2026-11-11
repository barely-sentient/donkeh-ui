using Donkeh.Parser.Objects;

namespace Donkeh.Parser.Builder
{
    /// <summary>
    /// Builds the table of exact-match lexical rules that the tokenizer compiles
    /// into its matcher.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Rules are stored in <see cref="ExactRules"/>, keyed by the
    /// <see cref="InternTable"/> id of their text. Registration happens once,
    /// from the <see cref="Tokenizer"/> static constructor, before
    /// <see cref="Compilation.Compiler.CompileExactRules"/> runs; it is not
    /// thread-safe.
    /// </para>
    /// <para>
    /// The helpers on this type only add rules. They do not validate CSS syntax
    /// and they do not prevent duplicates.
    /// </para>
    /// </remarks>
    /// <seealso cref="Tokenizer"/>
    /// <seealso cref="Compilation.Compiler"/>
    public static partial class RuleBuilder
    {
        /// <summary>
        /// The registered exact rules, keyed by the interned id of the matched
        /// text.
        /// </summary>
        /// <remarks>
        /// The field is read-only but the dictionary is mutable and is populated
        /// by the registration methods on this type. Values are the
        /// <see cref="TokenKind"/> returned when the text matches exactly.
        /// </remarks>
        public static readonly Dictionary<int, TokenKind> ExactRules = [];

        /// <summary>
        /// Registers an exact-match rule for a string.
        /// </summary>
        /// <param name="sequence">The text to match, exactly and case-sensitively.</param>
        /// <param name="kind">The token kind produced on a match.</param>
        /// <exception cref="ArgumentNullException">
        /// Thrown when <paramref name="sequence"/> is <see langword="null"/>.
        /// </exception>
        /// <remarks>
        /// If the text is already registered, the existing kind wins unless the
        /// new kind is <see cref="TokenKind.PropertyName"/>, which overwrites it.
        /// That exception exists for names that are both a property and a
        /// function: the property kind is stored and the tokenizer upgrades the
        /// token to <see cref="TokenKind.Function"/> when it sees a following
        /// <c>'('</c>.
        /// </remarks>
        public static void Exact(string sequence, TokenKind kind)
        {
            var id = InternTable.Intern(sequence);

            // Some names are both a property and a function, for example
            // 'color', 'rotate' and 'scale'. A lexer can only tell them apart
            // by looking ahead for '(', so keep the property kind here and let
            // the tokenizer upgrade the token to Function when it sees '('.
            if (!ExactRules.TryAdd(id, kind) && kind == TokenKind.PropertyName)
            {
                ExactRules[id] = kind;
            }
        }

        /// <summary>
        /// Registers an exact-match rule for a single character.
        /// </summary>
        /// <param name="c">The character to match.</param>
        /// <param name="kind">The token kind produced on a match.</param>
        public static void Exact(char c, TokenKind kind)
        {
            Exact(c.ToString(), kind);
        }

        /// <summary>
        /// Registers a CSS property name.
        /// </summary>
        /// <param name="propertyName">The property name without a leading marker.</param>
        /// <exception cref="ArgumentNullException">
        /// Thrown when <paramref name="propertyName"/> is <see langword="null"/>.
        /// </exception>
        public static void CssPropertyName(string propertyName)
        {
            Exact(propertyName, TokenKind.PropertyName);
        }
        
        /// <summary>
        /// Registers a CSS property group 'margin', 'padding', 'animation' etc...
        /// </summary>
        /// <param name="baseName">The unprefixed group name, e.g. <c>margin</c>.</param>
        /// <param name="properties">
        /// The member suffixes. Each is registered as
        /// <c><paramref name="baseName"/>-suffix</c>.
        /// </param>
        /// <exception cref="ArgumentNullException">
        /// Thrown when <paramref name="baseName"/> is <see langword="null"/>.
        /// </exception>
        /// <remarks>
        /// The base name itself is registered too, so <c>margin</c> and
        /// <c>margin-top</c> both become property names.
        /// </remarks>
        public static void CssPropertyGroup(string baseName, params string[] properties)
        {
            CssPropertyName(baseName);
            
            foreach (var property in properties)
            {
                Exact(string.Join('-', baseName, property), TokenKind.PropertyName);
            }
        }

        /// <summary>
        /// Registers a CSS pseudo-class rule; the leading <c>':'</c> is added
        /// automatically.
        /// </summary>
        /// <param name="name">The pseudo-class name, e.g. <c>hover</c>.</param>
        public static void CssPseudoClass(string name)
        {
            Exact($":{name}", TokenKind.PseudoClass);
        }
        
        /// <summary>
        /// Registers a CSS pseudo-element rule; the leading <c>'::'</c> is added
        /// automatically.
        /// </summary>
        /// <param name="name">The pseudo-element name, e.g. <c>before</c>.</param>
        public static void CssPseudoElement(string name)
        {
            Exact($"::{name}", TokenKind.PseudoElement);
        }

        /// <summary>
        /// Registers a CSS function name.
        /// </summary>
        /// <param name="functionName">The function name, without parentheses.</param>
        /// <remarks>
        /// The tokenizer only emits <see cref="TokenKind.Function"/> when the
        /// name is followed by <c>'('</c>; otherwise it falls back to
        /// <see cref="TokenKind.Identifier"/>.
        /// </remarks>
        public static void Function(string functionName)
        {
            Exact(functionName, TokenKind.Function);
        }
    }
}