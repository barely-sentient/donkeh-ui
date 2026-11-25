// Donkeh browser DOM bridge.
//
// The C# host owns the virtual tree and sends compact patch batches. This module
// owns the real DOM nodes, delegated event listeners and the translation from
// browser events to the same delegate shapes the desktop host invokes.

let root = null;
let dotNetRef = null;

/** @type {Map<number, Node>} */
const nodes = new Map();

/** @type {WeakMap<Element, {x: number, y: number}>} */
const scrollPositions = new WeakMap();

/** @type {Element | null} */
let pointerTarget = null;

export function init(containerId, ref) {
    root = document.getElementById(containerId) || document.body;
    dotNetRef = ref;

    root.addEventListener("click", onClick);
    root.addEventListener("mousedown", onMouseDown);
    root.addEventListener("mouseup", onMouseUp);
    root.addEventListener("mouseover", onMouseOver);
    root.addEventListener("mouseout", onMouseOut);
    root.addEventListener("contextmenu", onContextMenu);
    root.addEventListener("pointerdown", onPointerDown, true);
    root.addEventListener("pointermove", onPointerMove, true);
    root.addEventListener("pointerup", onPointerUp, true);
    root.addEventListener("pointercancel", onPointerUp, true);
    root.addEventListener("input", onInput);
    root.addEventListener("change", onChange);
    root.addEventListener("submit", onSubmit);
    root.addEventListener("keydown", onKeyDown);
    root.addEventListener("focusin", onFocusIn);
    root.addEventListener("focusout", onFocusOut);
    root.addEventListener("paste", onPaste);
}

export function setStyle(css) {
    let style = document.getElementById("donkeh-styles");

    if (!style) {
        style = document.createElement("style");
        style.id = "donkeh-styles";
        document.head.appendChild(style);
    }

    style.textContent = css || "";
}

export function setTitle(title) {
    if (title) {
        document.title = title;
    }
}

export function apply(json) {
    const batch = JSON.parse(json);

    for (const patch of batch.Patches) {
        switch (patch.Op) {
            case "create":
                createNode(patch);
                break;

            case "remove":
                removeNode(patch.Id);
                break;

            case "children":
                setChildren(patch.Id, patch.Children || []);
                break;

            case "text":
                {
                    const node = nodes.get(patch.Id);
                    if (node && node.nodeType === Node.TEXT_NODE) {
                        node.nodeValue = patch.Text || "";
                    }
                }
                break;

            case "content":
                {
                    const node = nodes.get(patch.Id);
                    if (node && node.nodeType === Node.ELEMENT_NODE) {
                        node.textContent = patch.Text || "";
                    }
                }
                break;

            case "attrs":
                {
                    const node = nodes.get(patch.Id);
                    if (node && node.nodeType === Node.ELEMENT_NODE) {
                        applyAttributes(node, patch.Attrs || {});
                    }
                }
                break;

            case "events":
                {
                    const node = nodes.get(patch.Id);
                    if (node && node.nodeType === Node.ELEMENT_NODE) {
                        applyEvents(node, patch.Events || []);
                    }
                }
                break;
        }
    }
}

function createNode(patch) {
    const node = patch.IsText
        ? document.createTextNode(patch.Text || "")
        : document.createElement(patch.Tag || "div");

    node.__donkehId = patch.Id;
    nodes.set(patch.Id, node);

    if (!patch.IsText) {
        node.dataset.donkehId = String(patch.Id);
    }
}

function removeNode(id) {
    const node = nodes.get(id);

    if (node) {
        node.remove();
        nodes.delete(id);
    }
}

function setChildren(parentId, children) {
    const parent = parentId === 0 ? root : nodes.get(parentId);

    if (!parent) {
        return;
    }

    const desired = new Set(children);

    for (const child of Array.from(parent.childNodes)) {
        if (child.__donkehId && !desired.has(child.__donkehId)) {
            parent.removeChild(child);
        }
    }

    for (let i = 0; i < children.length; i++) {
        const child = nodes.get(children[i]);

        if (!child) {
            continue;
        }

        const current = parent.childNodes[i];

        if (current !== child) {
            parent.insertBefore(child, current || null);
        }
    }
}

function applyAttributes(element, attributes) {
    const keep = new Set(Object.keys(attributes));

    for (const attribute of Array.from(element.attributes)) {
        // The bridge owns these attributes. A full attribute replacement from
        // the virtual tree must not remove the node id or event registry.
        if (attribute.name.startsWith("data-donkeh-")) {
            continue;
        }

        if (!keep.has(attribute.name)) {
            element.removeAttribute(attribute.name);

            if (attribute.name === "checked" && element.__donkehChecked !== false) {
                element.checked = false;
                element.__donkehChecked = false;
            }

            if (attribute.name === "selected" && element.__donkehSelected !== false) {
                element.selected = false;
                element.__donkehSelected = false;
            }
        }
    }

    for (const [name, value] of Object.entries(attributes)) {
        switch (name) {
            case "value":
                if ("value" in element) {
                    if (element.value !== value) {
                        // A focused control keeps the user's live value unless
                        // the virtual tree changed it to something other than
                        // the last user-typed value.
                        if (document.activeElement !== element || element.__donkehValue !== value) {
                            element.value = value;
                        }
                    }

                    // Keep defaultValue in sync without touching a focused
                    // control's live value or caret.
                    element.setAttribute("value", value);
                } else {
                    element.setAttribute(name, value);
                }
                break;

            case "checked":
                {
                    const wanted = value !== "false";
                    if (element.__donkehChecked !== wanted) {
                        element.checked = wanted;
                        element.__donkehChecked = wanted;
                    }
                    element.setAttribute("checked", "");
                }
                break;

            case "selected":
                {
                    const wanted = value !== "false";
                    if (element.__donkehSelected !== wanted) {
                        element.selected = wanted;
                        element.__donkehSelected = wanted;
                    }
                    element.setAttribute("selected", "");
                }
                break;

            default:
                element.setAttribute(name, value);
                break;
        }
    }
}

function applyEvents(element, events) {
    const list = events.join(" ");

    if (list) {
        element.dataset.donkehEvents = list;
    } else {
        element.removeAttribute("data-donkeh-events");
    }

    if (events.includes("scroll")) {
        ensureScrollTracking(element);
    }

    if (events.includes("play") || events.includes("pause")
        || events.includes("ended") || events.includes("timeupdate")) {
        ensureMediaTracking(element);
    }
}

function ensureScrollTracking(element) {
    if (element.__donkehScroll) {
        return;
    }

    element.__donkehScroll = true;
    scrollPositions.set(element, { x: element.scrollLeft, y: element.scrollTop });

    element.addEventListener("scroll", () => {
        const previous = scrollPositions.get(element) || { x: 0, y: 0 };
        const x = element.scrollLeft;
        const y = element.scrollTop;
        scrollPositions.set(element, { x, y });

        const handler = nearestHandlerElement(element, "scroll");

        if (handler) {
            call(handler, element, "scroll", x - previous.x, y - previous.y, null, null);
        }
    }, { passive: true });
}

function ensureMediaTracking(element) {
    if (element.__donkehMedia) {
        return;
    }

    element.__donkehMedia = true;

    element.addEventListener("play", () => {
        const handler = nearestHandlerElement(element, "play");
        if (handler) call(handler, element, "play", 0, 0, null, null);
    });

    element.addEventListener("pause", () => {
        const handler = nearestHandlerElement(element, "pause");
        if (handler) call(handler, element, "pause", 0, 0, null, null);
    });

    element.addEventListener("ended", () => {
        const handler = nearestHandlerElement(element, "ended");
        if (handler) call(handler, element, "ended", 0, 0, null, null);
    });

    element.addEventListener("timeupdate", () => {
        const handler = nearestHandlerElement(element, "timeupdate");
        if (handler) call(handler, element, "timeupdate", element.currentTime || 0, 0, null, null);
    });
}

function onClick(event) {
    const target = asElement(event.target);
    if (!target) return;

    const handler = nearestHandlerElement(target, "click");
    if (!handler) return;

    // The desktop host does not perform browser navigation or form submission
    // from a click; the app decides what happens in its handler.
    if (target.closest("a[href]")
        || target.closest("button, input[type=submit], input[type=image]")) {
        event.preventDefault();
    }

    const point = localPoint(handler, event);
    call(handler, target, "click", point.x, point.y, null, null, modifiersOf(event));
}

function onMouseDown(event) {
    dispatchMouse(event, "mousedown");
}

function onMouseUp(event) {
    dispatchMouse(event, "mouseup");
}

function dispatchMouse(event, type) {
    const target = asElement(event.target);
    if (!target) return;

    const handler = nearestHandlerElement(target, type);
    if (!handler) return;

    const point = localPoint(handler, event);
    call(handler, target, type, point.x, point.y, null, null, modifiersOf(event));
}

function onMouseOver(event) {
    dispatchSimple(event, "mouseover");
}

function onMouseOut(event) {
    dispatchSimple(event, "mouseout");
}

function onContextMenu(event) {
    const target = asElement(event.target);
    if (!target) return;

    const handler = nearestHandlerElement(target, "contextmenu");
    if (!handler) return;

    event.preventDefault();
    const point = localPoint(handler, event);
    call(handler, target, "contextmenu", point.x, point.y, null, null, modifiersOf(event));
}

function onPointerDown(event) {
    const target = asElement(event.target);
    if (!target) return;

    const handler = nearestHandlerElement(target, "pointerdown");
    if (!handler) return;

    pointerTarget = handler;

    try {
        handler.setPointerCapture(event.pointerId);
    } catch {
        // Pointer capture is best-effort; some synthetic events cannot use it.
    }

    const point = localPoint(handler, event);
    call(handler, target, "pointerdown", point.x, point.y, null, null, modifiersOf(event));
}

function onPointerMove(event) {
    if (!pointerTarget) return;

    const point = localPoint(pointerTarget, event);
    call(pointerTarget, pointerTarget, "pointermove", point.x, point.y, null, null, modifiersOf(event));
}

function onPointerUp(event) {
    if (!pointerTarget) return;

    const point = localPoint(pointerTarget, event);
    call(pointerTarget, pointerTarget, "pointerup", point.x, point.y, null, null, modifiersOf(event));
    pointerTarget = null;
}

function onInput(event) {
    const target = asElement(event.target);
    if (!target) return;

    const handler = nearestHandlerElement(target, "input");
    if (!handler) return;

    const value = readValue(target);
    target.__donkehValue = value;
    call(handler, target, "input", 0, 0, value, value);
}

function onChange(event) {
    const target = asElement(event.target);
    if (!target) return;

    const handler = nearestHandlerElement(target, "change");
    if (!handler) return;

    const value = readValue(target);
    target.__donkehValue = value;
    call(handler, target, "change", 0, 0, value, value);
}

function onSubmit(event) {
    // Desktop forms never perform a native submission.
    event.preventDefault();

    const target = asElement(event.target);
    if (!target) return;

    const handler = nearestHandlerElement(target, "submit");
    if (handler) call(handler, target, "submit", 0, 0, null, null);
}

function onKeyDown(event) {
    const target = asElement(document.activeElement) || asElement(event.target);
    if (!target) return;

    const handler = nearestHandlerElement(target, "keydown");
    const textControl = isTextControl(target);

    const modifiers = modifiersOf(event);

    if (handler || (textControl && event.key === "Enter")) {
        call(handler || target, target, "keydown", 0, 0, event.key, readValue(target), modifiers, event.repeat);
    }

    if (event.key === "Escape" && textControl) {
        target.blur();
    }
}

function onFocusIn(event) {
    const target = asElement(event.target);
    if (!target || !hasEvent(target, "focus")) return;

    call(target, target, "focus", 0, 0, null, null);
}

function onFocusOut(event) {
    const target = asElement(event.target);
    if (!target || !hasEvent(target, "blur")) return;

    call(target, target, "blur", 0, 0, null, null);
}

function onPaste(event) {
    const target = asElement(event.target);
    if (!target) return;

    const handler = nearestHandlerElement(target, "paste");
    if (!handler) return;

    // Desktop lets a paste handler own the edit; it does not also insert text.
    event.preventDefault();
    const text = event.clipboardData ? event.clipboardData.getData("text") : "";
    call(handler, target, "paste", 0, 0, text, text);
}

function dispatchSimple(event, type) {
    const target = asElement(event.target);
    if (!target) return;

    const handler = nearestHandlerElement(target, type);
    if (!handler) return;

    const point = localPoint(handler, event);
    call(handler, target, type, point.x, point.y, null, null, modifiersOf(event));
}

function modifiersOf(event) {
    // Bit-for-bit the KeyModifiers values the desktop host builds.
    return (event.ctrlKey ? 1 : 0)
        | (event.shiftKey ? 2 : 0)
        | (event.altKey ? 4 : 0)
        | (event.metaKey ? 8 : 0);
}

function call(handler, target, type, x, y, text, value, modifiers = 0, repeat = false) {
    if (!dotNetRef || !handler || handler.__donkehId === undefined) {
        return;
    }

    const targetId = target && target.__donkehId !== undefined
        ? target.__donkehId
        : handler.__donkehId;

    dotNetRef.invokeMethodAsync(
        "HandleEvent",
        handler.__donkehId,
        targetId,
        type,
        x,
        y,
        text,
        value,
        modifiers,
        repeat);
}

function asElement(node) {
    if (node instanceof Element) {
        return node;
    }

    return node && node.parentElement instanceof Element ? node.parentElement : null;
}

function nearestHandlerElement(element, type) {
    for (let current = element; current; current = current.parentElement) {
        if (hasEvent(current, type)) {
            return current;
        }
    }

    return null;
}

function hasEvent(element, type) {
    const list = element.dataset ? element.dataset.donkehEvents : null;
    return !!list && (` ${list} `).includes(` ${type} `);
}

function localPoint(element, event) {
    const rect = element.getBoundingClientRect();
    return {
        x: event.clientX - rect.left,
        y: event.clientY - rect.top
    };
}

function readValue(element) {
    if (element.type === "checkbox" || element.type === "radio") {
        return element.checked ? "true" : "false";
    }

    return element.value === undefined || element.value === null
        ? ""
        : String(element.value);
}

function isTextControl(element) {
    if (element.tagName === "TEXTAREA") {
        return true;
    }

    if (element.tagName !== "INPUT") {
        return false;
    }

    const type = (element.getAttribute("type") || "text").toLowerCase();
    return ["text", "search", "email", "url", "tel", "password", "number"].includes(type);
}

export async function requestPermission(name) {
    try {
        switch (name) {
            case "camera":
                return await requestMedia({ video: true });

            case "microphone":
                return await requestMedia({ audio: true });

            case "location":
                return await requestLocation();

            case "notifications":
                return await requestNotifications();

            case "storage":
                if (navigator.storage && navigator.storage.persist) {
                    return await navigator.storage.persist();
                }
                return false;

            case "motion":
                return await requestMotion();

            case "clipboard":
                if (navigator.clipboard && navigator.clipboard.readText) {
                    await navigator.clipboard.readText();
                    return true;
                }
                return false;

            default:
                return false;
        }
    } catch {
        return false;
    }
}

async function requestMedia(constraints) {
    if (!navigator.mediaDevices || !navigator.mediaDevices.getUserMedia) {
        return false;
    }

    const stream = await navigator.mediaDevices.getUserMedia(constraints);
    stream.getTracks().forEach(track => track.stop());
    return true;
}

async function requestLocation() {
    if (!navigator.geolocation) {
        return false;
    }

    return await new Promise(resolve => {
        navigator.geolocation.getCurrentPosition(
            () => resolve(true),
            () => resolve(false),
            { timeout: 15000, maximumAge: 60000 });
    });
}

async function requestNotifications() {
    if (typeof Notification === "undefined") {
        return false;
    }

    if (Notification.permission === "granted") {
        return true;
    }

    if (Notification.permission === "denied") {
        return false;
    }

    return (await Notification.requestPermission()) === "granted";
}

async function requestMotion() {
    if (typeof DeviceMotionEvent === "undefined") {
        return false;
    }

    if (typeof DeviceMotionEvent.requestPermission === "function") {
        return (await DeviceMotionEvent.requestPermission()) === "granted";
    }

    return true;
}
