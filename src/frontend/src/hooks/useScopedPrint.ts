import { useCallback, useEffect } from 'react';

export const useScopedPrint = (bodyClass: string, active: boolean) => {
    useEffect(() => {
        if (!active) return;
        const cleanup = () => document.body.classList.remove(bodyClass);
        window.addEventListener('afterprint', cleanup);
        return () => {
            window.removeEventListener('afterprint', cleanup);
            cleanup();
        };
    }, [active, bodyClass]);

    return useCallback(() => {
        document.body.classList.add(bodyClass);
        try {
            window.print();
        } catch (error) {
            document.body.classList.remove(bodyClass);
            throw error;
        }
    }, [bodyClass]);
};
