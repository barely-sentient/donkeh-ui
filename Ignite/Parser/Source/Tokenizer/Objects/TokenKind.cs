namespace Ignite.Parser.Objects
{
    public enum TokenKind : byte
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
            
        PseudoClass
        #endregion
    }
}