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
