using System.Text;

namespace Ignite.Inspector;

/// <summary>The inspector's own stylesheet. It is appended after author CSS.</summary>
public static class InspectorStyles
{
    /// <summary>Height of the panel when docked to the bottom, in CSS pixels.</summary>
    public const float DockBottomHeight = 320;

    /// <summary>Width of the panel when docked to the right, in CSS pixels.</summary>
    public const float DockRightWidth = 460;

    public static string Css { get; } = $$"""
        .dt-host {
            position: relative;
            width: 100%;
            height: 100%;
            margin: 0;
            padding: 0;
        }

        #ignite-devtools {
            position: fixed;
            z-index: 2147483000;
            display: flex;
            flex-direction: column;
            box-sizing: border-box;
            overflow: hidden;
            background-color: #202124;
            color: #e8eaed;
            font-family: "Segoe UI", "Helvetica Neue", Arial, sans-serif;
            font-size: 12px;
            line-height: 1.4;
            border: 1px solid #3c4043;
        }

        #ignite-devtools.dt-standalone {
            top: 0;
            left: 0;
            right: 0;
            bottom: 0;
            width: auto;
            height: auto;
            border-width: 0;
        }

        #ignite-devtools.dt-dock-bottom {
            left: 0;
            right: 0;
            bottom: 0;
            height: {{DockBottomHeight}}px;
            border-width: 1px 0 0 0;
        }

        #ignite-devtools.dt-dock-right {
            top: 0;
            right: 0;
            bottom: 0;
            width: {{DockRightWidth}}px;
            border-width: 0 0 0 1px;
        }

        #ignite-devtools.dt-hidden {
            display: none;
        }

        #ignite-devtools .dt-toolbar {
            display: flex;
            flex-direction: row;
            align-items: center;
            height: 36px;
            min-height: 36px;
            background-color: #292a2d;
            border-bottom-width: 1px;
            border-bottom-style: solid;
            border-bottom-color: #3c4043;
            padding-left: 6px;
            padding-right: 6px;
        }

        #ignite-devtools .dt-tabs {
            display: flex;
            flex-direction: row;
            height: 36px;
        }

        #ignite-devtools .dt-tab {
            display: flex;
            align-items: center;
            padding-left: 12px;
            padding-right: 12px;
            height: 36px;
            color: #9aa0a6;
            border-bottom-width: 2px;
            border-bottom-style: solid;
            border-bottom-color: transparent;
        }

        #ignite-devtools .dt-tab-active {
            color: #e8eaed;
            border-bottom-color: #8ab4f8;
        }

        #ignite-devtools .dt-spacer {
            flex: 1;
        }

        #ignite-devtools .dt-icon-btn {
            display: flex;
            align-items: center;
            justify-content: center;
            width: 26px;
            min-width: 26px;
            height: 26px;
            border-radius: 4px;
            margin-left: 2px;
            color: #9aa0a6;
        }

        #ignite-devtools .dt-icon-btn:hover {
            background-color: #3c4043;
            color: #e8eaed;
        }

        #ignite-devtools .dt-glyph {
            width: 14px;
            height: 11px;
            border-width: 1px;
            border-style: solid;
            border-color: #9aa0a6;
            position: relative;
            box-sizing: border-box;
        }

        #ignite-devtools .dt-glyph-bar-bottom {
            position: absolute;
            left: 0;
            right: 0;
            bottom: 0;
            height: 4px;
            background-color: #8ab4f8;
        }

        #ignite-devtools .dt-glyph-bar-right {
            position: absolute;
            top: 0;
            right: 0;
            bottom: 0;
            width: 4px;
            background-color: #8ab4f8;
        }

        #ignite-devtools .dt-glyph-window {
            width: 14px;
            height: 11px;
            border-width: 1px;
            border-style: solid;
            border-color: #9aa0a6;
            box-sizing: border-box;
            position: relative;
        }

        #ignite-devtools .dt-glyph-window-bar {
            position: absolute;
            left: 0;
            right: 0;
            top: 0;
            height: 3px;
            background-color: #8ab4f8;
        }

        #ignite-devtools .dt-close {
            font-size: 16px;
            line-height: 16px;
            color: #9aa0a6;
        }

        #ignite-devtools .dt-body {
            display: flex;
            flex-direction: row;
            flex: 1;
            overflow: hidden;
            min-height: 0;
        }

        #ignite-devtools .dt-elements {
            display: flex;
            flex-direction: row;
            flex: 1;
            overflow: hidden;
            width: 100%;
            height: 100%;
        }

        #ignite-devtools.dt-dock-right .dt-elements {
            flex-direction: column;
        }

        #ignite-devtools .dt-dom-pane {
            flex: 1 1 46%;
            overflow: auto;
            background-color: #202124;
            border-right-width: 1px;
            border-right-style: solid;
            border-right-color: #3c4043;
            padding-top: 4px;
            padding-bottom: 4px;
        }

        #ignite-devtools.dt-dock-right .dt-dom-pane {
            flex: 0 0 44%;
            border-right-width: 0;
            border-bottom-width: 1px;
            border-bottom-style: solid;
            border-bottom-color: #3c4043;
        }

        #ignite-devtools .dt-styles-pane {
            flex: 1 1 54%;
            overflow: auto;
            padding: 8px 10px;
        }

        #ignite-devtools.dt-dock-right .dt-styles-pane {
            flex: 1;
        }

        #ignite-devtools .dt-tree-row {
            display: flex;
            flex-direction: row;
            align-items: center;
            height: 20px;
            min-height: 20px;
            white-space: nowrap;
            padding-right: 8px;
        }

        #ignite-devtools .dt-tree-row:hover {
            background-color: #35363a;
        }

        #ignite-devtools .dt-tree-selected {
            background-color: #0e3a5f;
        }

        #ignite-devtools .dt-tree-children {
            padding-left: 12px;
        }

        #ignite-devtools .dt-caret {
            width: 14px;
            min-width: 14px;
            text-align: center;
            color: #9aa0a6;
            font-size: 10px;
        }

        #ignite-devtools .dt-caret-empty {
            color: transparent;
        }

        #ignite-devtools .dt-tag {
            color: #7ee0ff;
        }

        #ignite-devtools .dt-component {
            color: #c58af9;
        }

        #ignite-devtools .dt-component-badge {
            color: #202124;
            background-color: #c58af9;
            border-radius: 3px;
            padding-left: 4px;
            padding-right: 4px;
            margin-left: 6px;
            font-size: 9px;
            height: 13px;
            line-height: 13px;
        }

        #ignite-devtools .dt-attr {
            color: #f0b26b;
        }

        #ignite-devtools .dt-val {
            color: #a5d6a7;
        }

        #ignite-devtools .dt-dim {
            color: #9aa0a6;
        }

        #ignite-devtools .dt-text-node {
            color: #9aa0a6;
            font-style: italic;
        }

        #ignite-devtools .dt-selected-head {
            color: #e8eaed;
            font-family: Consolas, "Courier New", monospace;
            font-size: 13px;
            padding-bottom: 6px;
            border-bottom-width: 1px;
            border-bottom-style: solid;
            border-bottom-color: #3c4043;
        }

        #ignite-devtools .dt-section-title {
            color: #9aa0a6;
            font-size: 10px;
            letter-spacing: 1px;
            margin-top: 12px;
            margin-bottom: 6px;
        }

        #ignite-devtools .dt-rule {
            background-color: #292a2d;
            border-width: 1px;
            border-style: solid;
            border-color: #3c4043;
            border-radius: 4px;
            margin-bottom: 6px;
            padding: 6px 8px;
        }

        #ignite-devtools .dt-rule-head {
            display: flex;
            flex-direction: row;
            align-items: center;
            margin-bottom: 3px;
        }

        #ignite-devtools .dt-selector {
            color: #8ab4f8;
            font-family: Consolas, "Courier New", monospace;
        }

        #ignite-devtools .dt-origin {
            color: #9aa0a6;
            font-size: 10px;
            margin-left: auto;
            padding-left: 8px;
        }

        #ignite-devtools .dt-decl {
            display: flex;
            flex-direction: row;
            align-items: center;
            min-height: 20px;
            margin-top: 2px;
            font-family: Consolas, "Courier New", monospace;
        }

        #ignite-devtools .dt-decl:hover {
            background-color: #2a2d2e;
        }

        #ignite-devtools .dt-prop {
            color: #e8a87c;
        }

        #ignite-devtools .dt-colon {
            color: #9aa0a6;
        }

        #ignite-devtools .dt-value {
            color: #a5d6a7;
        }

        #ignite-devtools .dt-prop-input,
        #ignite-devtools .dt-value-input {
            background-color: transparent;
            border-width: 0;
            border-style: none;
            border-color: transparent;
            border-radius: 2px;
            padding: 0 2px;
            margin: 0;
            height: 18px;
            min-height: 18px;
            font-family: Consolas, "Courier New", monospace;
            font-size: 12px;
            line-height: 18px;
            outline-width: 0;
            outline-style: none;
            white-space: nowrap;
            overflow: hidden;
        }

        #ignite-devtools .dt-prop-input {
            width: 140px;
            min-width: 140px;
            color: #e8a87c;
        }

        #ignite-devtools .dt-value-input {
            flex: 0 1 300px;
            width: 300px;
            min-width: 80px;
            color: #a5d6a7;
        }

        #ignite-devtools .dt-prop-input:focus,
        #ignite-devtools .dt-value-input:focus {
            background-color: #1e1e1e;
            outline-width: 1px;
            outline-style: solid;
            outline-color: #007fd4;
        }

        #ignite-devtools .dt-swatch {
            display: block;
            flex: 0 0 12px;
            width: 12px;
            height: 12px;
            min-width: 12px;
            margin-right: 6px;
            margin-left: 2px;
        }

        #ignite-devtools .dt-add-row {
            min-height: 20px;
            margin-top: 2px;
        }

        #ignite-devtools .dt-add {
            color: #8ab4f8;
            padding-left: 4px;
            padding-right: 4px;
            cursor: pointer;
        }

        #ignite-devtools .dt-computed-row {
            display: flex;
            flex-direction: row;
            padding-top: 2px;
            padding-bottom: 2px;
            border-bottom-width: 1px;
            border-bottom-style: solid;
            border-bottom-color: #2a2b2e;
            font-family: Consolas, "Courier New", monospace;
        }

        #ignite-devtools .dt-computed-name {
            color: #e8a87c;
            width: 150px;
            min-width: 150px;
        }

        #ignite-devtools .dt-computed-value {
            color: #a5d6a7;
        }

        #ignite-devtools .dt-perf {
            display: flex;
            flex-direction: column;
            flex: 1;
            overflow: auto;
            padding: 10px;
            width: 100%;
            height: 100%;
        }

        #ignite-devtools .dt-cards {
            display: flex;
            flex-direction: row;
            gap: 8px;
            margin-bottom: 10px;
        }

        #ignite-devtools .dt-card {
            display: flex;
            flex-direction: column;
            flex: 1;
            background-color: #292a2d;
            border-width: 1px;
            border-style: solid;
            border-color: #3c4043;
            border-radius: 6px;
            padding: 8px 10px;
        }

        #ignite-devtools .dt-card-label {
            color: #9aa0a6;
            font-size: 10px;
            letter-spacing: 0.5px;
        }

        #ignite-devtools .dt-card-value {
            color: #e8eaed;
            font-size: 19px;
            font-weight: 600;
            margin-top: 2px;
        }

        #ignite-devtools .dt-card-sub {
            color: #9aa0a6;
            font-size: 10px;
        }

        #ignite-devtools .dt-graph {
            display: flex;
            flex-direction: row;
            align-items: flex-end;
            gap: 1px;
            height: 124px;
            min-height: 124px;
            background-color: #292a2d;
            border-width: 1px;
            border-style: solid;
            border-color: #3c4043;
            border-radius: 4px;
            padding: 4px;
            margin-bottom: 10px;
        }

        #ignite-devtools .dt-bar {
            flex: 1;
            min-width: 1px;
            background-color: #8ab4f8;
        }

        #ignite-devtools .dt-bar-layout {
            background-color: #f0b26b;
        }

        #ignite-devtools .dt-bar-render {
            background-color: #81c995;
        }

        #ignite-devtools .dt-legend {
            display: flex;
            flex-direction: row;
            gap: 14px;
            margin-bottom: 6px;
            color: #9aa0a6;
            font-size: 10px;
        }

        #ignite-devtools .dt-legend-dot {
            width: 8px;
            height: 8px;
            border-radius: 4px;
            margin-right: 4px;
            background-color: #8ab4f8;
        }

        #ignite-devtools .dt-dot-frame { background-color: #8ab4f8; }
        #ignite-devtools .dt-dot-layout { background-color: #f0b26b; }
        #ignite-devtools .dt-dot-render { background-color: #81c995; }

        #ignite-devtools .dt-legend-item {
            display: flex;
            flex-direction: row;
            align-items: center;
        }

        #ignite-devtools .dt-status {
            display: flex;
            flex-direction: row;
            align-items: center;
            height: 22px;
            min-height: 22px;
            background-color: #292a2d;
            border-top-width: 1px;
            border-top-style: solid;
            border-top-color: #3c4043;
            padding-left: 8px;
            padding-right: 8px;
            color: #9aa0a6;
            font-size: 10px;
        }

        #ignite-devtools .dt-status-gap {
            flex: 1;
        }
        """;

    /// <summary>
    /// Height utility classes used by the performance graph. The engine has no
    /// inline style support, so bar heights are chosen from this table.
    /// </summary>
    public static string HeightClasses { get; } = BuildHeightClasses();

    private static string BuildHeightClasses()
    {
        var builder = new StringBuilder();

        for (var i = 0; i <= 120; i++)
        {
            builder.Append("#ignite-devtools .dt-h");
            builder.Append(i);
            builder.Append(" { height: ");
            builder.Append(i);
            builder.Append("px; }\n");
        }

        return builder.ToString();
    }
}
