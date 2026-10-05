/**
 * antd Table (và Grid) luôn đăng ký lắng nghe breakpoint qua `window.matchMedia`.
 * Mọi trình duyệt hiện đại đều có API này nên hàm dưới đây không làm gì;
 * nó chỉ phòng môi trường thiếu API (trình duyệt nhúng cũ, jsdom) để antd không ném lỗi.
 * Fallback coi như không media query nào khớp, antd dùng bố cục mặc định.
 */
export function ensureMatchMedia() {
    if (typeof window === 'undefined' || typeof window.matchMedia === 'function') return;
    window.matchMedia = (query: string) => ({
        matches: false,
        media: query,
        onchange: null,
        addListener: () => {},
        removeListener: () => {},
        addEventListener: () => {},
        removeEventListener: () => {},
        dispatchEvent: () => false,
    }) as MediaQueryList;
}

ensureMatchMedia();
