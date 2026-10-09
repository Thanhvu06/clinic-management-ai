import '@testing-library/jest-dom/vitest';

if (typeof window !== 'undefined') {
    window.HTMLElement.prototype.scrollIntoView = () => {};

    if (typeof window.ResizeObserver === 'undefined') {
        class ResizeObserverStub {
            observe() {}
            unobserve() {}
            disconnect() {}
        }
        window.ResizeObserver = ResizeObserverStub as unknown as typeof ResizeObserver;
    }
}
