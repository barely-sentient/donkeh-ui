(function () {
    "use strict";

    var root = document.documentElement;
    var themeButton = document.getElementById("theme-toggle");
    var menuButton = document.getElementById("menu-toggle");
    var body = document.body;

    function applyTheme(theme) {
        root.dataset.theme = theme;
        try {
            localStorage.setItem("donkeh-theme", theme);
        } catch (error) {
            // Storage can be unavailable in private modes; the page still works.
        }

        if (themeButton) {
            var dark = theme === "dark";
            themeButton.textContent = dark ? "☀" : "☾";
            themeButton.setAttribute(
                "aria-label",
                dark ? "Switch to light theme" : "Switch to dark theme");
            themeButton.setAttribute("title", dark ? "Light theme" : "Dark theme");
        }
    }

    var savedTheme = null;
    try {
        savedTheme = localStorage.getItem("donkeh-theme");
    } catch (error) {
        savedTheme = null;
    }

    var prefersDark = window.matchMedia &&
        window.matchMedia("(prefers-color-scheme: dark)").matches;

    applyTheme(savedTheme || (prefersDark ? "dark" : "light"));

    if (themeButton) {
        themeButton.addEventListener("click", function () {
            applyTheme(root.dataset.theme === "dark" ? "light" : "dark");
        });
    }

    if (menuButton) {
        menuButton.addEventListener("click", function () {
            var open = body.classList.toggle("sidebar-open");
            menuButton.setAttribute("aria-expanded", String(open));
        });
    }

    document.addEventListener("keydown", function (event) {
        if (event.key === "Escape" && body.classList.contains("sidebar-open")) {
            body.classList.remove("sidebar-open");
            if (menuButton) {
                menuButton.setAttribute("aria-expanded", "false");
            }
        }
    });

    var current = window.location.pathname.split("/").pop() || "index.html";
    document.querySelectorAll(".nav a").forEach(function (link) {
        var href = link.getAttribute("href") || "";
        if (href === current) {
            link.classList.add("is-active");
            link.setAttribute("aria-current", "page");
        }
    });

    document.querySelectorAll(".nav a").forEach(function (link) {
        link.addEventListener("click", function () {
            body.classList.remove("sidebar-open");
            if (menuButton) {
                menuButton.setAttribute("aria-expanded", "false");
            }
        });
    });

    var csharpKeywords = new Set([
        "using", "static", "public", "private", "protected", "internal",
        "sealed", "abstract", "partial", "class", "record", "struct",
        "interface", "enum", "namespace", "return", "new", "var", "void",
        "async", "await", "if", "else", "foreach", "for", "while", "do",
        "switch", "case", "default", "break", "continue", "in", "is", "as",
        "null", "true", "false", "this", "base", "override", "virtual",
        "readonly", "init", "get", "set", "where", "params", "out", "ref",
        "throw", "try", "catch", "finally", "global", "typeof", "nameof",
        "when", "with", "operator", "implicit", "explicit", "event",
        "delegate", "lock", "volatile", "unsafe", "fixed", "sizeof",
        "stackalloc", "const", "required", "file", "scoped"
    ]);

    var csharpTypes = new Set([
        "Node", "Component", "Props", "Context", "Task", "Host",
        "HostPermission", "IPermissionService", "String", "Dictionary",
        "List", "CancellationToken", "Action", "Func", "IEnumerable",
        "Timer", "Console", "Math", "AppRoot", "Stylesheet", "Image",
        "Donkeh", "UILab", "Hosting", "Windowing"
    ]);

    var shellCommands = new Set([
        "dotnet", "donkeh", "cd", "git", "npm", "export", "set", "echo"
    ]);

    function escapeHtml(text) {
        return text
            .replace(/&/g, "&amp;")
            .replace(/</g, "&lt;")
            .replace(/>/g, "&gt;");
    }

    function token(className, text) {
        return '<span class="tok-' + className + '">' + escapeHtml(text) + "</span>";
    }

    function detectLanguage(block, raw) {
        var label = (block.dataset.label || "").toLowerCase();

        if (label.indexOf(".cs") >= 0 || label.indexOf("c#") >= 0) {
            return "csharp";
        }

        if (label.indexOf(".css") >= 0) {
            return "css";
        }

        if (label.indexOf(".csproj") >= 0
            || label.indexOf(".xml") >= 0
            || label.indexOf(".targets") >= 0) {
            return "xml";
        }

        if (label.indexOf("terminal") >= 0) {
            return "shell";
        }

        if (label.indexOf("mime") >= 0
            || label.indexOf("inspector") >= 0
            || label.indexOf("animations tab") >= 0) {
            return "text";
        }

        if (raw.indexOf("dotnet ") >= 0 || raw.indexOf("donkeh ") >= 0) {
            return "shell";
        }

        if (raw.indexOf("<Project") >= 0 || raw.indexOf("<?xml") >= 0) {
            return "xml";
        }

        if (raw.indexOf("@keyframes") >= 0 || /\.[\w-]+\s*\{/.test(raw)) {
            return "css";
        }

        if (raw.indexOf("public ") >= 0
            || raw.indexOf("class ") >= 0
            || raw.indexOf("using ") >= 0
            || raw.indexOf("var ") >= 0
            || raw.indexOf("=>") >= 0
            || raw.indexOf("H(") >= 0
            || raw.indexOf("C<") >= 0
            || raw.indexOf("Text(") >= 0
            || raw.indexOf("Provider(") >= 0) {
            return "csharp";
        }

        return "text";
    }

    function highlight(raw, language) {
        if (language === "text") {
            return escapeHtml(raw);
        }

        var output = "";
        var index = 0;
        var lineStart = true;

        while (index < raw.length) {
            var character = raw[index];
            var rest = raw.slice(index);

            if (language === "csharp" && rest.startsWith("//")) {
                var lineEnd = raw.indexOf("\n", index);
                if (lineEnd < 0) lineEnd = raw.length;
                output += token("comment", raw.slice(index, lineEnd));
                index = lineEnd;
                continue;
            }

            if ((language === "csharp" || language === "css")
                && rest.startsWith("/*")) {
                var close = raw.indexOf("*/", index + 2);
                close = close < 0 ? raw.length : close + 2;
                output += token("comment", raw.slice(index, close));
                index = close;
                continue;
            }

            if (language === "xml" && rest.startsWith("<!--")) {
                var xmlClose = raw.indexOf("-->", index + 4);
                xmlClose = xmlClose < 0 ? raw.length : xmlClose + 3;
                output += token("comment", raw.slice(index, xmlClose));
                index = xmlClose;
                continue;
            }

            if (language === "shell" && character === "#") {
                var shellEnd = raw.indexOf("\n", index);
                if (shellEnd < 0) shellEnd = raw.length;
                output += token("comment", raw.slice(index, shellEnd));
                index = shellEnd;
                continue;
            }

            if (language === "csharp"
                && character === "#"
                && (lineStart || /^[ \t]*$/.test(raw.slice(raw.lastIndexOf("\n", index) + 1, index)))) {
                var preprocEnd = raw.indexOf("\n", index);
                if (preprocEnd < 0) preprocEnd = raw.length;
                output += token("preproc", raw.slice(index, preprocEnd));
                index = preprocEnd;
                continue;
            }

            if (character === '"' || character === "'") {
                var quote = character;
                var cursor = index + 1;
                while (cursor < raw.length) {
                    if (raw[cursor] === "\\") {
                        cursor += 2;
                        continue;
                    }
                    if (raw[cursor] === quote) {
                        cursor++;
                        break;
                    }
                    if (raw[cursor] === "\n") {
                        break;
                    }
                    cursor++;
                }
                output += token("string", raw.slice(index, cursor));
                index = cursor;
                continue;
            }

            if (language === "xml" && character === "<") {
                var tagEnd = raw.indexOf(">", index + 1);
                if (tagEnd < 0) {
                    output += escapeHtml(raw.slice(index));
                    index = raw.length;
                    continue;
                }
                output += token("tag", raw.slice(index, tagEnd + 1));
                index = tagEnd + 1;
                continue;
            }

            if (/[0-9]/.test(character)
                || (character === "." && /[0-9]/.test(raw.charAt(index + 1)))) {
                var numberEnd = index + 1;
                while (numberEnd < raw.length && /[0-9a-fA-FxX._%]/.test(raw[numberEnd])) {
                    numberEnd++;
                }
                output += token("number", raw.slice(index, numberEnd));
                index = numberEnd;
                continue;
            }

            if (/[A-Za-z_]/.test(character)) {
                var wordEnd = index + 1;
                while (wordEnd < raw.length && /[A-Za-z0-9_]/.test(raw[wordEnd])) {
                    wordEnd++;
                }

                var word = raw.slice(index, wordEnd);

                if (language === "csharp" && csharpKeywords.has(word)) {
                    output += token("keyword", word);
                } else if (language === "csharp" && csharpTypes.has(word)) {
                    output += token("type", word);
                } else if (language === "shell" && shellCommands.has(word)) {
                    output += token("keyword", word);
                } else if (language === "css" && isCssProperty(raw, index, wordEnd)) {
                    output += token("property", word);
                } else {
                    output += escapeHtml(word);
                }

                index = wordEnd;
                continue;
            }

            if (character === "\n") {
                output += "\n";
                lineStart = true;
                index++;
                continue;
            }

            if (character !== " " && character !== "\t" && character !== "\r") {
                lineStart = false;
            }

            output += escapeHtml(character);
            index++;
        }

        return output;
    }

    function isCssProperty(raw, start, end) {
        var before = start - 1;
        while (before >= 0 && /\s/.test(raw[before])) {
            before--;
        }
        var previous = before >= 0 ? raw[before] : "\n";
        if (previous === "." || previous === "#" || previous === "&" || previous === ":") {
            return false;
        }

        var after = end;
        while (after < raw.length && /\s/.test(raw[after])) {
            after++;
        }

        return raw[after] === ":";
    }

    document.querySelectorAll(".code").forEach(function (block) {
        if (block.dataset.highlighted === "true") {
            return;
        }

        block.querySelectorAll("code").forEach(function (code) {
            var raw = code.textContent;
            var language = detectLanguage(block, raw);
            code.innerHTML = highlight(raw, language);
        });

        block.dataset.highlighted = "true";
    });

    var csxMode = true;
    try {
        var savedCsx = localStorage.getItem("donkeh-csx-mode");
        if (savedCsx !== null) {
            csxMode = savedCsx === "true";
        }
    } catch (error) {
        csxMode = true;
    }

    function applyCsxMode(on) {
        csxMode = !!on;
        try {
            localStorage.setItem("donkeh-csx-mode", String(csxMode));
        } catch (error) {
            // Storage can be unavailable in private modes.
        }
        document.querySelectorAll(".code").forEach(function (block) {
            var csx = block.querySelector("pre.csx, pre.variant.csx");
            var csharp = block.querySelector("pre.csharp, pre.variant.csharp");
            if (csx && csharp) {
                csx.classList.toggle("is-active", csxMode);
                csharp.classList.toggle("is-active", !csxMode);
                csx.style.display = csxMode ? "" : "none";
                csharp.style.display = csxMode ? "none" : "";
                var input = block.querySelector(".csx-switch input");
                if (input && input.checked !== csxMode) {
                    input.checked = csxMode;
                }
            }
        });
    }

    // Inject an iOS-style switch into every code block that offers both variants.
    document.querySelectorAll(".code").forEach(function (block) {
        var csx = block.querySelector("pre.csx, pre.variant.csx");
        var csharp = block.querySelector("pre.csharp, pre.variant.csharp");
        if (!csx || !csharp) {
            return;
        }

        if (block.querySelector(".csx-switch")) {
            return;
        }

        var label = document.createElement("label");
        label.className = "csx-switch";
        label.title = "Toggle CSX syntax";

        var input = document.createElement("input");
        input.type = "checkbox";
        input.checked = csxMode;
        input.setAttribute("aria-label", "CSX mode");

        var track = document.createElement("span");
        track.className = "track";

        var text = document.createElement("span");
        text.className = "label-text";
        text.textContent = "CSX";

        label.appendChild(input);
        label.appendChild(track);
        label.appendChild(text);

        input.addEventListener("change", function () {
            applyCsxMode(input.checked);
        });

        block.appendChild(label);
    });

    applyCsxMode(csxMode);

    document.querySelectorAll(".code").forEach(function (block) {
        var button = document.createElement("button");
        button.type = "button";
        button.className = "copy-code";
        button.textContent = "Copy";
        button.setAttribute("aria-label", "Copy code");

        button.addEventListener("click", function () {
            var code = block.querySelector("pre.is-active code") || block.querySelector("code");
            var text = code ? code.innerText : "";

            function done() {
                button.textContent = "Copied";
                window.setTimeout(function () {
                    button.textContent = "Copy";
                }, 1200);
            }

            if (navigator.clipboard && navigator.clipboard.writeText) {
                navigator.clipboard.writeText(text).then(done, function () {
                    button.textContent = "Failed";
                });
                return;
            }

            var area = document.createElement("textarea");
            area.value = text;
            area.style.position = "fixed";
            area.style.opacity = "0";
            document.body.appendChild(area);
            area.select();
            try {
                document.execCommand("copy");
                done();
            } catch (error) {
                button.textContent = "Failed";
            }
            document.body.removeChild(area);
        });

        block.appendChild(button);
    });
})();
