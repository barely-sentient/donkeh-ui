namespace Donkeh.Parser.Objects
{
    /// <summary>
    /// Identifies the lexical category of a <see cref="Token"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The underlying numeric values are assigned implicitly from declaration
    /// order, starting at zero. They are embedded in compiled IL by
    /// <see cref="Compilation.Compiler"/> and stored in
    /// <see cref="Token.Kind"/>, so reordering or inserting members changes the
    /// values produced by the tokenizer.
    /// </para>
    /// <para>
    /// Several kinds mirror CSS syntax tokens (<see cref="Function"/>,
    /// <see cref="Hash"/>, <see cref="Dimension"/>); others are Ignite-specific
    /// helpers such as <see cref="PropertyName"/> and
    /// <see cref="PseudoElementColon"/>.
    /// </para>
    /// </remarks>
    /// <seealso cref="Token"/>
    public enum TokenKind
    {
        /// <summary>
        /// A known CSS property name, e.g. <c>color</c> or <c>text-align</c>.
        /// </summary>
        PropertyName,
        
        
        #region Punctuation
        
        /// <summary>
        /// The <c>':'</c> character when it does not begin a recognized pseudo
        /// selector.
        /// </summary>
        Colon, 
        
        /// <summary>
        /// The <c>';'</c> character.
        /// </summary>
        SemiColon,
        
        /// <summary>
        /// The <c>'('</c> character.
        /// </summary>
        LeftParenthesis,
        
        /// <summary>
        /// The <c>')'</c> character.
        /// </summary>
        RightParenthesis,
        
        /// <summary>
        /// The <c>'{'</c> character.
        /// </summary>
        LeftCurlyBrace,
        
        /// <summary>
        /// The <c>'}'</c> character.
        /// </summary>
        RightCurlyBrace,
        
        /// <summary>
        /// The <c>'['</c> character.
        /// </summary>
        LeftSquareBracket,
        
        /// <summary>
        /// The <c>']'</c> character.
        /// </summary>
        RightSquareBracket,
        
        /// <summary>
        /// The <c>'>'</c> child combinator.
        /// </summary>
        DirectChildBracket,
        
        #endregion
        #region Special functionality

        /// <summary>
        /// A complete pseudo-class including its leading colon, e.g. <c>:hover</c>.
        /// </summary>
        PseudoClass,

        /// <summary>
        /// A complete pseudo-element including its leading double colon, e.g.
        /// <c>::before</c>.
        /// </summary>
        PseudoElement,

        /// <summary>
        /// A function name, emitted only when the identifier is immediately
        /// followed by <c>'('</c>, e.g. <c>calc</c>.
        /// </summary>
        Function,

        /// <summary>
        /// The <c>::</c> pair on its own, used when the following pseudo-element
        /// name is not recognized.
        /// </summary>
        PseudoElementColon,

        #endregion
        #region Values
        
        /// <summary>
        /// A generic identifier that is not a known property, function or pseudo selector.
        /// </summary>
        Identifier,
        
        /// <summary>
        /// A numeric literal, e.g. 10, 1.5 or -2e3.
        /// </summary>
        Number,
        
        /// <summary>
        /// A number followed by a unit, e.g. 10px.
        /// </summary>
        Dimension,
        
        /// <summary>
        /// A number followed by '%', e.g. 50%.
        /// </summary>
        Percentage,
        
        /// <summary>
        /// A hash token, e.g. #fff or #id.
        /// </summary>
        Hash,
        
        /// <summary>
        /// A quoted string.
        /// </summary>
        String,
        
        /// <summary>
        /// An at-keyword, e.g. @media.
        /// </summary>
        AtKeyword,
        
        /// <summary>
        /// The ',' character.
        /// </summary>
        Comma,
        
        /// <summary>
        /// The '+' character.
        /// </summary>
        Plus,
        
        /// <summary>
        /// The '~' character.
        /// </summary>
        Tilde,
        
        /// <summary>
        /// The '*' character.
        /// </summary>
        Asterisk,
        
        /// <summary>
        /// The '=' character.
        /// </summary>
        Equals,
        
        /// <summary>
        /// Any other single character that has no dedicated token kind.
        /// </summary>
        Delimiter,

        /// <summary>
        /// The '||' column combinator.
        /// </summary>
        ColumnCombinator,
        
        #endregion
    }
}