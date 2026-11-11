using static Ignite.Parser.Builder.RuleBuilder;

namespace Ignite.Parser
{
    public static partial class Tokenizer
    {
        /// <summary>
        /// Registers the supported CSS pseudo-class names.
        /// </summary>
        /// <remarks>
        /// Each name is registered through
        /// <see cref="Builder.RuleBuilder.CssPseudoClass"/>, which prepends the
        /// leading colon. Names are matched case-sensitively.
        /// </remarks>
        private static void PseudoClasses()
        {
            CssPseudoClass("active-view-transition");
            CssPseudoClass("active-view-transition-type");
            CssPseudoClass("active");
            CssPseudoClass("any-link");
            CssPseudoClass("autofill");
            CssPseudoClass("blank");
            CssPseudoClass("buffering");
            CssPseudoClass("checked");
            CssPseudoClass("current");
            CssPseudoClass("default");
            CssPseudoClass("defined");
            CssPseudoClass("dir");
            CssPseudoClass("disabled");
            CssPseudoClass("empty");
            CssPseudoClass("enabled");
            CssPseudoClass("first-child");
            CssPseudoClass("first-of-type");
            CssPseudoClass("first");
            CssPseudoClass("focus-visible");
            CssPseudoClass("focus-within");
            CssPseudoClass("focus");
            CssPseudoClass("fullscreen");
            CssPseudoClass("future");
            CssPseudoClass("has-slotted");
            CssPseudoClass("has");
            CssPseudoClass("heading");
            CssPseudoClass("host-context");
            CssPseudoClass("host");
            CssPseudoClass("hover");
            CssPseudoClass("in-range");
            CssPseudoClass("indeterminate");
            CssPseudoClass("interest-source");
            CssPseudoClass("interest-target");
            CssPseudoClass("invalid");
            CssPseudoClass("is");
            CssPseudoClass("lang");
            CssPseudoClass("last-child");
            CssPseudoClass("last-of-type");
            CssPseudoClass("left");
            CssPseudoClass("link");
            CssPseudoClass("local-link");
            CssPseudoClass("modal");
            CssPseudoClass("muted");
            CssPseudoClass("not");
            CssPseudoClass("nth-child");
            CssPseudoClass("nth-last-child");
            CssPseudoClass("nth-last-of-type");
            CssPseudoClass("nth-of-type");
            CssPseudoClass("only-child");
            CssPseudoClass("only-of-type");
            CssPseudoClass("open");
            CssPseudoClass("optional");
            CssPseudoClass("out-of-range");
            CssPseudoClass("past");
            CssPseudoClass("paused");
            CssPseudoClass("picture-in-picture");
            CssPseudoClass("placeholder-shown");
            CssPseudoClass("playing");
            CssPseudoClass("popover-open");
            CssPseudoClass("read-only");
            CssPseudoClass("read-write");
            CssPseudoClass("required");
            CssPseudoClass("right");
            CssPseudoClass("root");
            CssPseudoClass("scope");
            CssPseudoClass("seeking");
            CssPseudoClass("stalled");
            CssPseudoClass("target-after");
            CssPseudoClass("target-before");
            CssPseudoClass("target-current");
            CssPseudoClass("target");
            CssPseudoClass("user-invalid");
            CssPseudoClass("user-valid");
            CssPseudoClass("valid");
            CssPseudoClass("visited");
            CssPseudoClass("volume-locked");
            CssPseudoClass("where");
            CssPseudoClass("xr-overlay");
        }
    }
}