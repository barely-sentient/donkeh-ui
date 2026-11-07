namespace Ignite.Parser.Objects
{
    public enum TokenKind
    {
        /// <summary>
        /// A property name 'color', 'text-align' etc...
        /// </summary>
        PropertyName,
        
        
        #region Punctuation
        
        /// <summary>
        /// the ':' character
        /// </summary>
        Colon, 
        
        /// <summary>
        /// the ';' character
        /// </summary>
        SemiColon,
        
        /// <summary>
        /// the '(' character
        /// </summary>
        LeftParenthesis,
        
        /// <summary>
        /// the ')' character
        /// </summary>
        RightParenthesis,
        
        /// <summary>
        /// the '{' character
        /// </summary>
        LeftCurlyBrace,
        
        /// <summary>
        /// the '}' character
        /// </summary>
        RightCurlyBrace,
        
        /// <summary>
        /// the '[' character
        /// </summary>
        LeftSquareBracket,
        
        /// <summary>
        /// the ']' character
        /// </summary>
        RightSquareBracket,
        
        /// <summary>
        /// the '>' character
        /// </summary>
        DirectChildBracket,
        
        #endregion
        #region Special functionality
            
        PseudoClass,
        
        PseudoElement,
        
        Function,
        
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
        Delimiter
        
        #endregion
    }
}