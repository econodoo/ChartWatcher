/**
 * Chart Watcher Agent — injected into every source WebView2.
 * Handles: selector resolution, mutation observation, picker overlay, crop mode.
 */
(function () {
    'use strict';

    const CW_VERSION = '0.1.0';
    const THROTTLE_MS = 100;
    const MAX_EMIT_PER_SEC = 10;

    // ═══ SELECTOR ENGINE ═══

    const selectorEngine = {
        /**
         * Try each selector in the cascade until one matches.
         * @param {Array} cascade - [{strategy, expression}, ...]
         * @returns {Element|null}
         */
        resolve(cascade) {
            for (const sel of cascade) {
                try {
                    const el = this._tryOne(sel);
                    if (el) return el;
                } catch { /* next */ }
            }
            return null;
        },

        _tryOne(sel) {
            switch (sel.strategy) {
                case 'ElementId':
                    return document.getElementById(sel.expression);
                case 'DataAttribute':
                    return document.querySelector(`[${sel.expression}]`);
                case 'CssPath':
                    return document.querySelector(sel.expression);
                case 'XPath': {
                    const r = document.evaluate(sel.expression, document, null,
                        XPathResult.FIRST_ORDERED_NODE_TYPE, null);
                    return r.singleNodeValue;
                }
                case 'TextMatch': {
                    const walker = document.createTreeWalker(document.body,
                        NodeFilter.SHOW_TEXT, null);
                    let node;
                    while ((node = walker.nextNode())) {
                        if (node.textContent?.trim().includes(sel.expression))
                            return node.parentElement;
                    }
                    return null;
                }
                default:
                    return null;
            }
        },

        /**
         * Generate all possible selectors for an element.
         * @param {Element} el
         * @returns {Array} cascade
         */
        generateCascade(el) {
            const cascade = [];

            // 1. ID (highest priority)
            if (el.id && !el.id.match(/^\d/) && !el.id.includes(':')) {
                cascade.push({ strategy: 'ElementId', expression: el.id });
            }

            // 2. data-* attributes
            for (const attr of el.attributes) {
                if (attr.name.startsWith('data-') && attr.value) {
                    cascade.push({
                        strategy: 'DataAttribute',
                        expression: `${attr.name}="${attr.value}"`
                    });
                }
            }

            // 3. CSS path
            const cssPath = this._buildCssPath(el);
            if (cssPath) {
                cascade.push({ strategy: 'CssPath', expression: cssPath });
            }

            // 4. XPath
            const xpath = this._buildXPath(el);
            if (xpath) {
                cascade.push({ strategy: 'XPath', expression: xpath });
            }

            // 5. Text content (last resort)
            const text = el.textContent?.trim();
            if (text && text.length < 50) {
                cascade.push({ strategy: 'TextMatch', expression: text });
            }

            return cascade;
        },

        _buildCssPath(el) {
            const parts = [];
            let cur = el;
            while (cur && cur !== document.body) {
                let sel = cur.tagName.toLowerCase();
                if (cur.id) {
                    sel = `#${cur.id}`;
                    parts.unshift(sel);
                    break;
                }
                if (cur.className && typeof cur.className === 'string') {
                    const classes = cur.className.trim().split(/\s+/).slice(0, 2).join('.');
                    if (classes) sel += `.${classes}`;
                }
                // nth-child for disambiguation
                const parent = cur.parentElement;
                if (parent) {
                    const siblings = Array.from(parent.children).filter(c => c.tagName === cur.tagName);
                    if (siblings.length > 1) {
                        const idx = siblings.indexOf(cur) + 1;
                        sel += `:nth-child(${idx})`;
                    }
                }
                parts.unshift(sel);
                cur = cur.parentElement;
            }
            return parts.join(' > ');
        },

        _buildXPath(el) {
            const parts = [];
            let cur = el;
            while (cur && cur !== document.body) {
                let idx = 1;
                let sib = cur.previousElementSibling;
                while (sib) {
                    if (sib.tagName === cur.tagName) idx++;
                    sib = sib.previousElementSibling;
                }
                parts.unshift(`${cur.tagName.toLowerCase()}[${idx}]`);
                cur = cur.parentElement;
            }
            return parts.length ? '//' + parts.join('/') : null;
        }
    };

    // ═══ MUTATION WATCHER ═══

    const watchers = new Map(); // stickerId -> { cascade, observer, lastEmit }

    function observe(stickerId, cascade) {
        if (watchers.has(stickerId)) unobserve(stickerId);

        const el = selectorEngine.resolve(cascade);
        if (!el) {
            bridge.send({ evt: 'stale', stickerId, reason: 'selector_miss' });
            // Retry in 2s
            setTimeout(() => {
                if (watchers.has(stickerId)) observe(stickerId, cascade);
            }, 2000);
            return;
        }

        const state = {
            cascade,
            lastEmit: 0,
            emitCount: 0,
            resetTime: Date.now()
        };

        const observer = new MutationObserver(() => {
            const now = Date.now();

            // Rate limiting
            if (now - state.resetTime > 1000) {
                state.emitCount = 0;
                state.resetTime = now;
            }
            if (state.emitCount >= MAX_EMIT_PER_SEC) return;
            if (now - state.lastEmit < THROTTLE_MS) return;

            state.lastEmit = now;
            state.emitCount++;

            const resolved = selectorEngine.resolve(cascade);
            if (resolved) {
                bridge.send({
                    evt: 'mutation',
                    stickerId,
                    html: resolved.outerHTML,
                    text: resolved.textContent?.trim() || '',
                    ts: now
                });
            }
        });

        observer.observe(el, {
            subtree: true,
            childList: true,
            characterData: true,
            attributes: true
        });

        state.observer = observer;
        watchers.set(stickerId, state);

        // Emit initial state
        bridge.send({
            evt: 'mutation',
            stickerId,
            html: el.outerHTML,
            text: el.textContent?.trim() || '',
            ts: Date.now()
        });
    }

    function unobserve(stickerId) {
        const state = watchers.get(stickerId);
        if (state?.observer) {
            state.observer.disconnect();
            watchers.delete(stickerId);
        }
    }

    // ═══ PICKER OVERLAY ═══

    let pickerActive = false;
    let pickerOverlay = null;
    let highlightBox = null;
    let breadcrumb = null;
    let currentTarget = null;

    function enterPick() {
        if (pickerActive) return;
        pickerActive = true;

        pickerOverlay = document.createElement('div');
        Object.assign(pickerOverlay.style, {
            position: 'fixed', inset: '0',
            zIndex: '2147483646',
            cursor: 'crosshair',
            background: 'transparent'
        });

        highlightBox = document.createElement('div');
        Object.assign(highlightBox.style, {
            position: 'fixed',
            border: '2px solid #536DFE',
            background: 'rgba(83,109,254,0.12)',
            pointerEvents: 'none',
            zIndex: '2147483647',
            transition: 'all 80ms ease',
            borderRadius: '3px'
        });

        breadcrumb = document.createElement('div');
        Object.assign(breadcrumb.style, {
            position: 'fixed', bottom: '0', left: '0', right: '0',
            background: 'rgba(0,0,0,0.85)',
            color: '#B0BEC5', fontFamily: 'Consolas, monospace',
            fontSize: '11px', padding: '6px 12px',
            zIndex: '2147483647',
            whiteSpace: 'nowrap', overflow: 'hidden',
            textOverflow: 'ellipsis'
        });

        document.body.appendChild(pickerOverlay);
        document.body.appendChild(highlightBox);
        document.body.appendChild(breadcrumb);

        pickerOverlay.addEventListener('mousemove', onPickerMove);
        pickerOverlay.addEventListener('click', onPickerClick);
        document.addEventListener('keydown', onPickerKey);
    }

    function exitPick() {
        pickerActive = false;
        currentTarget = null;
        pickerOverlay?.remove();
        highlightBox?.remove();
        breadcrumb?.remove();
        document.removeEventListener('keydown', onPickerKey);
    }

    function onPickerMove(e) {
        pickerOverlay.style.pointerEvents = 'none';
        const el = document.elementFromPoint(e.clientX, e.clientY);
        pickerOverlay.style.pointerEvents = 'auto';

        if (!el || el === pickerOverlay || el === highlightBox || el === breadcrumb) return;
        currentTarget = el;

        const rect = el.getBoundingClientRect();
        Object.assign(highlightBox.style, {
            left: `${rect.left}px`, top: `${rect.top}px`,
            width: `${rect.width}px`, height: `${rect.height}px`
        });

        breadcrumb.textContent = buildBreadcrumb(el);
    }

    function onPickerClick(e) {
        e.preventDefault();
        e.stopPropagation();
        if (!currentTarget) return;

        const cascade = selectorEngine.generateCascade(currentTarget);
        const rect = currentTarget.getBoundingClientRect();

        bridge.send({
            evt: 'picked',
            cascade,
            innerText: currentTarget.textContent?.trim().substring(0, 200) || '',
            attrs: getAttributes(currentTarget),
            rect: { x: rect.x, y: rect.y, w: rect.width, h: rect.height }
        });

        exitPick();
    }

    function onPickerKey(e) {
        if (e.key === 'Escape') {
            exitPick();
        } else if (e.key === 'ArrowUp' && currentTarget?.parentElement) {
            currentTarget = currentTarget.parentElement;
            highlightElement(currentTarget);
        } else if (e.key === 'ArrowDown' && currentTarget?.firstElementChild) {
            currentTarget = currentTarget.firstElementChild;
            highlightElement(currentTarget);
        } else if (e.key === 'Enter' && currentTarget) {
            onPickerClick(e);
        }
    }

    function highlightElement(el) {
        const rect = el.getBoundingClientRect();
        Object.assign(highlightBox.style, {
            left: `${rect.left}px`, top: `${rect.top}px`,
            width: `${rect.width}px`, height: `${rect.height}px`
        });
        breadcrumb.textContent = buildBreadcrumb(el);
    }

    function buildBreadcrumb(el) {
        const parts = [];
        let cur = el;
        while (cur && cur !== document.documentElement) {
            let s = cur.tagName.toLowerCase();
            if (cur.id) s += `#${cur.id}`;
            else if (cur.className && typeof cur.className === 'string') {
                const cls = cur.className.trim().split(/\s+/)[0];
                if (cls) s += `.${cls}`;
            }
            parts.unshift(s);
            cur = cur.parentElement;
        }
        return parts.join(' > ');
    }

    function getAttributes(el) {
        const obj = {};
        for (const a of el.attributes) obj[a.name] = a.value;
        return obj;
    }

    // ═══ CROP CONTROLLER ═══

    function applyCrop(cascades, slots) {
        // Hide everything except the picked elements
        for (let i = 0; i < cascades.length; i++) {
            const el = selectorEngine.resolve(cascades[i]);
            if (!el) continue;

            // Walk up, hiding siblings
            let cur = el;
            while (cur.parentElement) {
                for (const sib of cur.parentElement.children) {
                    if (sib !== cur) {
                        sib.style.cssText += ';display:none!important';
                    }
                }
                cur = cur.parentElement;
            }

            // Position the element
            if (cascades.length === 1) {
                el.style.cssText += `;position:fixed!important;inset:0!important
                    ;width:100vw!important;height:100vh!important
                    ;margin:0!important;z-index:2147483647!important`;
            }
        }
        document.documentElement.style.overflow = 'hidden';
        window.dispatchEvent(new Event('resize'));
    }

    // ═══ BRIDGE ═══

    const bridge = {
        send(msg) {
            try {
                window.chrome?.webview?.postMessage(JSON.stringify(msg));
            } catch { /* swallow if no webview */ }
        },

        listen() {
            window.chrome?.webview?.addEventListener('message', (e) => {
                try {
                    const msg = JSON.parse(e.data);
                    switch (msg.cmd) {
                        case 'observe':
                            observe(msg.stickerId, msg.cascade);
                            break;
                        case 'unobserve':
                            unobserve(msg.stickerId);
                            break;
                        case 'enterPick':
                            enterPick();
                            break;
                        case 'exitPick':
                            exitPick();
                            break;
                        case 'applyCrop':
                            applyCrop(msg.cascades, msg.slots);
                            break;
                    }
                } catch (err) {
                    console.error('[CW Agent] bridge error:', err);
                }
            });
        }
    };

    // ═══ INIT ═══

    bridge.listen();
    bridge.send({ evt: 'ready', version: CW_VERSION });
    console.log(`[Chart Watcher Agent v${CW_VERSION}] loaded`);

})();
