import { useEffect, useRef } from 'react';
import { useSearchParams } from 'react-router-dom';

export function useOpenPrescriptionFromQuery(listLoaded: boolean, openPrescription: (id: number) => void) {
    const [searchParams] = useSearchParams();
    const requestedId = searchParams.get('id');
    const handledRef = useRef<string | null>(null);

    useEffect(() => {
        if (!listLoaded || !requestedId || handledRef.current === requestedId) return;
        handledRef.current = requestedId;
        const id = Number(requestedId);
        if (Number.isInteger(id) && id > 0) openPrescription(id);
    }, [listLoaded, requestedId, openPrescription]);
}
