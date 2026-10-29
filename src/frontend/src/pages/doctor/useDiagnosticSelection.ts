import { useMemo, useState } from 'react';
import type { DiagnosticOrderDto } from '../../types';

export function isDiagnosticServiceOrdered(orders: DiagnosticOrderDto[], serviceId: number): boolean {
    return orders.some(order => order.status !== 'Cancelled' && order.items.some(item => item.status !== 'Cancelled' && item.diagnosticServiceId === serviceId));
}

export function useDiagnosticSelection(orders: DiagnosticOrderDto[]) {
    const [selection, setSelection] = useState<number[]>([]);
    const blockedKey = JSON.stringify([...new Set(orders.filter(order => order.status !== 'Cancelled')
        .flatMap(order => order.items.filter(item => item.status !== 'Cancelled').map(item => item.diagnosticServiceId)))].sort((a, b) => a - b));
    const availableSelection = useMemo(() => {
        const blocked = new Set<number>(JSON.parse(blockedKey));
        return selection.filter(id => !blocked.has(id));
    }, [selection, blockedKey]);
    return [availableSelection, setSelection] as const;
}
