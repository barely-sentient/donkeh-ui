using static Donkeh.Parser.Builder.RuleBuilder;

namespace Donkeh.Parser
{
    public static partial class Tokenizer
    {
        /// <summary>
        /// Registers the supported CSS pseudo-element names.
        /// </summary>
        /// <remarks>
        /// Each name is registered through
        /// <see cref="Builder.RuleBuilder.CssPseudoElement"/>, which prepends the
        /// leading double colon. Names are matched case-sensitively.
        /// </remarks>
        private static void PseudoElements()
        {
            CssPseudoElement("after");
            CssPseudoElement("backdrop");
            CssPseudoElement("before");
            CssPseudoElement("checkmark");
            CssPseudoElement("column");
            CssPseudoElement("cue");
            CssPseudoElement("details-content");
            CssPseudoElement("file-selector-button");
            CssPseudoElement("first-letter");
            CssPseudoElement("first-line");
            CssPseudoElement("grammar-error");
            CssPseudoElement("highlight");
            CssPseudoElement("marker");
            CssPseudoElement("part");
            CssPseudoElement("picker-icon");
            CssPseudoElement("picker");
            CssPseudoElement("placeholder");
            CssPseudoElement("scroll-button");
            CssPseudoElement("scroll-marker");
            CssPseudoElement("scroll-marker-group");
            CssPseudoElement("search-text");
            CssPseudoElement("selection");
            CssPseudoElement("slotted");
            CssPseudoElement("spelling-error");
            CssPseudoElement("target-text");
            CssPseudoElement("view-transition");
            CssPseudoElement("view-transition-group");
            CssPseudoElement("view-transition-image-pair");
            CssPseudoElement("view-transition-new");
            CssPseudoElement("view-transition-old");

        }
    }
}