using Ignite.Parser.Objects;
using static Ignite.Parser.Builder.RuleBuilder;

namespace Ignite.Parser
{
    public static partial class Tokenizer
    {
        static Tokenizer()
        {
            Punctuation();
            PropertiesA();
        }

        private static void Punctuation()
        {
            // punctuation.
            Exact(';', TokenKind.SemiColon);
            Exact(':', TokenKind.Colon);
            Exact('{', TokenKind.LeftCurlyBrace);
            Exact('}', TokenKind.RightCurlyBrace);
            Exact('[', TokenKind.LeftSquareBracket);
            Exact(']', TokenKind.RightSquareBracket);
            Exact('(', TokenKind.LeftParenthesis);
            Exact(')', TokenKind.RightParenthesis);
            Exact('>', TokenKind.DirectChildBracket);
            Exact("::", TokenKind.PseudoElementColon);
        }
        
        /// <summary>
        /// All CSS properties beginning with 'a'
        /// </summary>
        private static void PropertiesA()
        {
            CssPropertyName("accent-color");
            
            CssPropertyGroup("align", 
                "content", 
                "items", 
                "self", 
                "baseline"
            );
            
            CssPropertyName("all");
            
            CssPropertyName("anchor-scope");
            
            CssPropertyGroup("animation",
                "composition", 
                "delay",
                "direction",
                "duration",
                "fill-mode",
                "iteration-count",
                "name",
                "play-state",
                "range",
                "range-end",
                "range-start",
                "timeline",
                "timing-function",
                "trigger"
            );

            CssPropertyName("appearance");

            CssPropertyName("aspect-ratio");
        }

        private static void PropertiesB()
        {
            CssPropertyName("backdrop-filter");
            CssPropertyName("backface-visibility");
            
            CssPropertyGroup("background",
                "attachment",
                "blend-mode",
                "clip",
                "color",
                "image",
                "origin",
                "position",
                "position-x",
                "position-y",
                "repeat",
                "repeat-x",
                "repeat-y",
                "size"
            );
            
            CssPropertyName("baseline-shift");
            CssPropertyName("baseline-source");
            CssPropertyName("block-size");
            
            CssPropertyGroup("border",
                "block",
                "block-color",
                "block-end",
                "block-end-color",
                "block-end-style",
                "block-end-width",
                "block-start",
                "block-start-color",
                "block-start-style",
                "block-start-width",
                "block-width",
                "bottom",
                "bottom-color",
                "bottom-left-radius",
                "bottom-right-radius",
                "bottom-style",
                "bottom-width",
                "collapse",
                "color",
                "end-end-radius",
                "end-start-radius",
                "image",
                "image-outset",
                "image-repeat",
                "image-slice",
                "image-source",
                "image-width",
                "inline",
                "inline-color",
                "inline-end",
                "inline-end-color",
                "inline-end-style",
                "inline-end-width",
                "inline-start",
                "inline-start-color",
                "inline-start-style",
                "inline-start-width",
                "left",
                "left-color",
                "left-style",
                "left-width",
                "radius",
                "right",
                "right-color",
                "right-style",
                "right-width",
                "shape",
                "spacing",
                "start-end-radius",
                "start-start-radius",
                "style",
                "top",
                "top-color",
                "top-left-radius",
                "top-right-radius",
                "top-style",
                "top-width",
                "width"
            );

            CssPropertyName("bottom");
            
        }
    }
}