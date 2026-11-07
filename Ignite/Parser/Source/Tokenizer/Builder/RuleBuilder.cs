using Ignite.Parser.Objects;

namespace Ignite.Parser.Builder
{
    public static partial class RuleBuilder
    {
        public static readonly Dictionary<int, TokenKind> ExactRules = [];

        /// <summary>
        /// Add a Token by exact value
        /// </summary>
        /// <param name="sequence"></param>
        /// <param name="kind"></param>
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
        /// Add a Token by exact value
        /// </summary>
        /// <param name="c"></param>
        /// <param name="kind"></param>
        public static void Exact(char c, TokenKind kind)
        {
            Exact(c.ToString(), kind);
        }

        /// <summary>
        /// Adds a CSS property name
        /// </summary>
        /// <param name="propertyName"></param>
        public static void CssPropertyName(string propertyName)
        {
            Exact(propertyName, TokenKind.PropertyName);
        }
        
        /// <summary>
        /// Adds a CSS property group 'margin', 'padding', 'animation' etc...
        /// </summary>
        /// <param name="baseName"></param>
        /// <param name="properties"></param>
        public static void CssPropertyGroup(string baseName, params string[] properties)
        {
            CssPropertyName(baseName);
            
            foreach (var property in properties)
            {
                Exact(string.Join('-', baseName, property), TokenKind.PropertyName);
            }
        }

        /// <summary>
        /// Add a pseudo class in CSS, you don't need to specify the ':'
        /// </summary>
        /// <param name="name"></param>
        public static void CssPseudoClass(string name)
        {
            Exact($":{name}", TokenKind.PseudoClass);
        }
        
        /// <summary>
        /// Add a pseudo class in CSS, you don't need to specify the '::'
        /// </summary>
        /// <param name="name"></param>
        public static void CssPseudoElement(string name)
        {
            Exact($"::{name}", TokenKind.PseudoElement);
        }

        /// <summary>
        /// Add a function from CSS
        /// </summary>
        /// <param name="functionName"></param>
        public static void Function(string functionName)
        {
            Exact(functionName, TokenKind.Function);
        }
    }
}