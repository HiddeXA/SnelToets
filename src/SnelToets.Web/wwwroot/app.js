// Small helpers called from Blazor via JS interop.
window.snelToets = {
    scrollTop: () => window.scrollTo({ top: 0, behavior: "smooth" }),
    focus: (selector) => document.querySelector(selector)?.focus(),
    download: (fileName, contentType, text) => {
        const blob = new Blob([text], { type: contentType });
        const a = document.createElement("a");
        a.href = URL.createObjectURL(blob);
        a.download = fileName;
        a.click();
        setTimeout(() => URL.revokeObjectURL(a.href), 1000);
    }
};

// ---- student ----
// Keyboard shortcuts for the players: forwards 1-9, A-D, Enter, Escape and arrows to a .NET object (method "OnKey").
// Ignored while typing in a field (except Escape) and for Enter on a focused, enabled button or link.
(() => {
    const handlers = new Map();
    let nextId = 1;
    const pattern = /^([1-9]|[a-dA-D]|Enter|Escape|ArrowLeft|ArrowRight)$/;
    window.snelToets.registerKeys = (dotNetRef) => {
        const id = nextId++;
        const handler = (e) => {
            if (e.ctrlKey || e.metaKey || e.altKey || e.isComposing) return;
            const t = e.target;
            const tag = t && t.tagName;
            const typing = tag === "INPUT" || tag === "TEXTAREA" || tag === "SELECT" || (t && t.isContentEditable);
            if (typing && e.key !== "Escape") return;
            if (e.key === "Enter" && (tag === "BUTTON" || tag === "A") && !t.disabled) return;
            if (!pattern.test(e.key)) return;
            if (e.key === "Enter") e.preventDefault();
            dotNetRef.invokeMethodAsync("OnKey", e.key).catch(() => { });
        };
        handlers.set(id, handler);
        document.addEventListener("keydown", handler);
        return id;
    };
    window.snelToets.unregisterKeys = (id) => {
        const handler = handlers.get(id);
        if (handler) { document.removeEventListener("keydown", handler); handlers.delete(id); }
    };
    window.snelToets.blur = () => document.activeElement && document.activeElement.blur && document.activeElement.blur();
    window.snelToets.scrollIntoView = (selector) => {
        const el = document.querySelector(selector);
        if (!el) return;
        const r = el.getBoundingClientRect();
        const bottomNav = window.matchMedia("(max-width: 767px)").matches ? 80 : 16;
        if (r.bottom > window.innerHeight - bottomNav || r.top < 60) el.scrollIntoView({ behavior: "smooth", block: "center" });
    };
    window.snelToets.scrollTopInstant = () => window.scrollTo({ top: 0 });
})();
