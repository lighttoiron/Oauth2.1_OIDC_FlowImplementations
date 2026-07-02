// cache maps CSS file paths to a Promise<CSSStyleSheet>
// We store the promise and not just the sheet itself so that if two different components
//  both request the same sheet, the second gets the same promise that was returned to the first
//  and they can share the sheet when its ready, no second fetch required.
const stylesCache = new Map();

export async function loadSheet(path) {
    if (!stylesCache.has(path)) {
        stylesCache.set(path, fetch(path)
            .then(r => r.text())
            .then(css => {
                const sheet = new CSSStyleSheet();
                sheet.replaceSync(css);
                return sheet;
            })
        );
    }

    return stylesCache.get(path);
}

// Pre-loads the tokens and shared sheets and returns them together
// Components call this once at the module level to resolve all styles before any element is constructed
export async function loadBaseSheets() {
    return Promise.all([
        loadSheet('/components/styles/tokens.css'),
        loadSheet('/components/styles/shared.css')
    ]);
}